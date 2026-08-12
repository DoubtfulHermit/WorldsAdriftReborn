using System.Text;

namespace WAPatch;

internal static class Program
{
    /// <summary>
    /// Two ways in. Double-clicked (no args), it opens the WinForms window a
    /// player uses. With --console it runs the exact same PatchEngine headless,
    /// which is how the pipe is smoke-tested under Wine where a GUI cannot be
    /// driven. The engine is identical either way.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && Array.Exists(args, a => a is "--console" or "-c"))
            return ConsoleMain(args).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    // ---- headless path ---------------------------------------------------

    private static async Task<int> ConsoleMain(string[] args)
    {
        string? dir = GetOpt(args, "--dir");
        string? manifestOverride = GetOpt(args, "--manifest");
        string? logFile = GetOpt(args, "--log");
        bool apply = Array.Exists(args, a => a == "--apply");

        // Log to stdout and, if asked, to a file. Wine does not forward a GUI
        // exe's stdout to the calling shell, so the smoke test reads the file.
        StreamWriter? fileLog = null;
        if (logFile is not null)
            fileLog = new StreamWriter(logFile, append: false, Encoding.UTF8) { AutoFlush = true };

        void Log(string s)
        {
            Console.WriteLine(s);
            fileLog?.WriteLine(s);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                Log("usage: WAPatch --console --dir <install> [--apply] [--manifest <url>] [--log <file>]");
                return 2;
            }

            PatchEngine.Validation v = PatchEngine.ValidateInstall(dir);
            Log(v.Ok ? $"OK: {v.Message}" : $"INVALID: {v.Message}");
            if (!v.Ok) return 2;

            var cfg = PatchConfig.Load();
            string manifestUrl = manifestOverride ?? cfg.EffectiveManifestUrl;

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var engine = new PatchEngine(http, Log);

            Manifest manifest = await engine.FetchManifestAsync(manifestUrl);
            Log($"You have: {cfg.InstalledVersion ?? "unknown"}   Latest: {manifest.Version}");

            if (!apply)
            {
                int need = 0;
                foreach (var p in engine.Plan(dir!, manifest))
                    if (p.State is PatchEngine.FileState.Missing or PatchEngine.FileState.Changed)
                    {
                        Log($"  would update: {p.File.DestPath} ({p.State})");
                        need++;
                    }
                Log(need == 0 ? "Already up to date." : $"{need} file(s) would be updated. Re-run with --apply.");
                return 0;
            }

            PatchEngine.ApplyResult r = await engine.ApplyAsync(dir!, manifest);
            if (r.AnyFailed) return 1;

            // Only record the version when everything landed cleanly.
            cfg.InstallDir = dir;
            cfg.InstalledVersion = manifest.Version;
            if (manifestOverride is not null) cfg.ManifestUrl = manifestOverride;
            cfg.Save();
            return 0;
        }
        catch (Exception e)
        {
            Log("FATAL: " + e.Message);
            return 1;
        }
        finally
        {
            fileLog?.Dispose();
        }
    }

    private static string? GetOpt(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }
}
