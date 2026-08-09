using System.Text.Json;
using System.Text.Json.Serialization;

namespace WAPatch;

/// <summary>
/// The tiny bit of state the patcher keeps between runs, in wapatch.config.json
/// next to the exe: which folder the player chose, and which version we last
/// installed there (so we can show "you have X, latest is Y" without re-hashing
/// everything just to answer that question).
/// </summary>
public sealed class PatchConfig
{
    [JsonPropertyName("installDir")]       public string? InstallDir { get; set; }
    [JsonPropertyName("installedVersion")] public string? InstalledVersion { get; set; }
    [JsonPropertyName("manifestUrl")]      public string? ManifestUrl { get; set; }

    /// <summary>Where the game normally downloads its patch manifest from.</summary>
    public const string DefaultManifestUrl = "https://wareborn.ratlabs.cc/patch/manifest.json";

    /// <summary>
    /// Effective manifest URL: an env override (used by the smoke test to point
    /// at a localhost server) wins, then the saved value, then the default.
    /// </summary>
    [JsonIgnore]
    public string EffectiveManifestUrl =>
        Environment.GetEnvironmentVariable("WAPATCH_MANIFEST_URL") is { Length: > 0 } env
            ? env
            : (string.IsNullOrWhiteSpace(ManifestUrl) ? DefaultManifestUrl : ManifestUrl!);

    // ---- load / save -----------------------------------------------------

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "wapatch.config.json");

    public static PatchConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<PatchConfig>(File.ReadAllText(ConfigPath))
                       ?? new PatchConfig();
        }
        catch
        {
            // A corrupt config must never stop the app - start clean.
        }
        return new PatchConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-fatal: worst case we forget the folder next launch.
        }
    }
}
