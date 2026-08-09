using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WAPatch;

/// <summary>
/// The whole patch decision, with no UI and no assumptions about where files
/// come from beyond an <see cref="HttpClient"/>. Everything the WinForms window
/// does, and everything the --console smoke test does, goes through here - so
/// the thing we test headless is the thing players run.
///
/// The contract mirrors UPDATE.bat, which players trusted before this existed:
///  * refuse anything that is not a real install with BepInEx already in it,
///  * back up each file exactly once (keep-first) before overwriting,
///  * never touch steam_api64.dll or winhttp.dll,
/// and adds the one thing a hand-run .bat could not: every downloaded byte is
/// verified against the manifest's sha256 before it is allowed to land. A hash
/// mismatch is refused, never written - this pipe drops DLLs into a game, so a
/// wrong or tampered file must fail loudly rather than install.
/// </summary>
public sealed class PatchEngine
{
    /// <summary>Files we must never write, whatever a manifest says.</summary>
    private static readonly HashSet<string> Forbidden =
        new(StringComparer.OrdinalIgnoreCase) { "steam_api64.dll", "winhttp.dll" };

    private readonly HttpClient _http;
    private readonly Action<string> _log;

    public PatchEngine(HttpClient http, Action<string> log)
    {
        _http = http;
        _log = log;
    }

    // ---- install validation ---------------------------------------------

    public readonly record struct Validation(bool Ok, string Message);

    /// <summary>
    /// A folder is a valid target only if it is a Worlds Adrift install
    /// (UnityClient@Windows.exe) that already has BepInEx - exactly the two
    /// checks UPDATE.bat makes before it changes anything.
    /// </summary>
    public static Validation ValidateInstall(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return new Validation(false, "Pick your Worlds Adrift folder to begin.");

        if (!File.Exists(Path.Combine(installDir, "UnityClient@Windows.exe")))
            return new Validation(false,
                "That folder is not a Worlds Adrift install - UnityClient@Windows.exe is not in it.");

        if (!Directory.Exists(Path.Combine(installDir, "BepInEx")))
            return new Validation(false,
                "BepInEx is not installed here. This patcher updates an existing mod install; "
                + "use the full setup pack first.");

        return new Validation(true, "Worlds Adrift install looks good.");
    }

    // ---- manifest fetch --------------------------------------------------

    public async Task<Manifest> FetchManifestAsync(string manifestUrl, CancellationToken ct = default)
    {
        _log($"Fetching manifest: {manifestUrl}");
        string json = await _http.GetStringAsync(manifestUrl, ct).ConfigureAwait(false);
        Manifest? m = JsonSerializer.Deserialize<Manifest>(json, JsonOpts);
        if (m is null || m.Files is null)
            throw new InvalidDataException("Manifest was empty or unreadable.");
        _log($"Latest version: {m.Version}  ({m.Files.Count} files, build '{m.Build}')");
        return m;
    }

    // ---- planning --------------------------------------------------------

    public enum FileState { UpToDate, Missing, Changed, Forbidden }

    public readonly record struct PlanItem(ManifestFile File, string LocalPath, FileState State);

    /// <summary>
    /// Decides, per file, whether the local copy already matches the manifest
    /// hash. No network here - just hashing what is on disk.
    /// </summary>
    public List<PlanItem> Plan(string installDir, Manifest manifest)
    {
        var plan = new List<PlanItem>(manifest.Files.Count);
        foreach (ManifestFile f in manifest.Files)
        {
            string local = LocalPathFor(installDir, f.DestPath);
            if (IsForbidden(f.DestPath))
            {
                plan.Add(new PlanItem(f, local, FileState.Forbidden));
                continue;
            }
            if (!File.Exists(local))
            {
                plan.Add(new PlanItem(f, local, FileState.Missing));
                continue;
            }
            string have = Sha256File(local);
            plan.Add(new PlanItem(f, local,
                string.Equals(have, f.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? FileState.UpToDate
                    : FileState.Changed));
        }
        return plan;
    }

    // ---- applying --------------------------------------------------------

    public sealed record ApplyResult(int Updated, int UpToDate, int Failed, int Skipped)
    {
        public bool AnyFailed => Failed > 0;
        public bool AnyChanged => Updated > 0;
    }

    /// <summary>
    /// Downloads and installs every file that differs, verifying each against
    /// its manifest hash before it is allowed to touch the disk. Idempotent:
    /// a second run with nothing changed downloads nothing and writes nothing.
    /// </summary>
    public async Task<ApplyResult> ApplyAsync(
        string installDir, Manifest manifest, CancellationToken ct = default)
    {
        int updated = 0, upToDate = 0, failed = 0, skipped = 0;

        foreach (PlanItem item in Plan(installDir, manifest))
        {
            ct.ThrowIfCancellationRequested();
            ManifestFile f = item.File;

            switch (item.State)
            {
                case FileState.Forbidden:
                    _log($"REFUSING to touch protected file: {f.DestPath}");
                    skipped++;
                    continue;

                case FileState.UpToDate:
                    upToDate++;
                    continue;

                case FileState.Missing:
                case FileState.Changed:
                    break;
            }

            string url = ResolveUrl(manifest, f);
            byte[] bytes;
            try
            {
                _log($"Downloading {f.DestPath} ({f.SizeBytes:N0} bytes)");
                bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log($"  ERROR download failed: {e.Message}");
                failed++;
                continue;
            }

            // Integrity gate. A wrong file never reaches the game folder.
            string got = Sha256Bytes(bytes);
            if (!string.Equals(got, f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _log($"  ERROR hash mismatch, REJECTED (expected {f.Sha256[..12]}..., got {got[..12]}...)");
                failed++;
                continue;
            }
            if (bytes.LongLength != f.SizeBytes)
            {
                _log($"  ERROR size mismatch, REJECTED (expected {f.SizeBytes}, got {bytes.LongLength})");
                failed++;
                continue;
            }

            try
            {
                string local = item.LocalPath;
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);

                // Back up once, keep-first: a second run must not clobber the
                // original backup with an already-patched file.
                if (File.Exists(local))
                {
                    string bak = local + ".bak";
                    if (!File.Exists(bak))
                    {
                        File.Copy(local, bak);
                        _log($"  backed up -> {Path.GetFileName(bak)}");
                    }
                }

                // Write to a temp file then move, so a crash mid-write can never
                // leave a half-written DLL where the game expects a whole one.
                string tmp = local + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(local)) File.Delete(local);
                File.Move(tmp, local);

                _log($"  installed {f.DestPath}");
                updated++;
            }
            catch (Exception e)
            {
                _log($"  ERROR could not write {f.DestPath}: {e.Message}");
                failed++;
            }
        }

        var result = new ApplyResult(updated, upToDate, failed, skipped);
        _log($"Done. {updated} updated, {upToDate} already current, "
             + $"{skipped} skipped, {failed} failed.");
        return result;
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Maps a manifest destPath (forward slashes) onto the install dir.</summary>
    public static string LocalPathFor(string installDir, string destPath)
    {
        string rel = destPath.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(installDir, rel));
    }

    private static bool IsForbidden(string destPath) =>
        Forbidden.Contains(Path.GetFileName(destPath));

    /// <summary>Prefer the file's own url; fall back to baseUrl + name.</summary>
    private static string ResolveUrl(Manifest m, ManifestFile f)
    {
        if (!string.IsNullOrWhiteSpace(f.Url)) return f.Url!;
        string baseUrl = (m.BaseUrl ?? "").TrimEnd('/');
        return $"{baseUrl}/{f.Name}";
    }

    public static string Sha256File(string path)
    {
        using var s = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
    }

    public static string Sha256Bytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// ---- manifest model ------------------------------------------------------

public sealed class Manifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("version")]       public string Version { get; set; } = "";
    [JsonPropertyName("build")]         public string Build { get; set; } = "";
    [JsonPropertyName("generatedUtc")]  public string GeneratedUtc { get; set; } = "";
    [JsonPropertyName("baseUrl")]       public string? BaseUrl { get; set; }
    [JsonPropertyName("files")]         public List<ManifestFile> Files { get; set; } = new();
}

public sealed class ManifestFile
{
    [JsonPropertyName("destPath")]  public string DestPath { get; set; } = "";
    [JsonPropertyName("name")]      public string Name { get; set; } = "";
    [JsonPropertyName("sha256")]    public string Sha256 { get; set; } = "";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("url")]       public string? Url { get; set; }
}
