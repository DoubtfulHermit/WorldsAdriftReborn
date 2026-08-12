namespace WAPatch;

/// <summary>
/// The one window a player sees. Deliberately plain: pick the game folder,
/// check what is available, patch. All the real work is in PatchEngine; this
/// only wires buttons to it and keeps the folder + version on screen.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _dirBox;
    private readonly Button _browse;
    private readonly Button _check;
    private readonly Button _patch;
    private readonly Label _versions;
    private readonly TextBox _log;

    private readonly PatchConfig _cfg = PatchConfig.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private Manifest? _latest;

    public MainForm()
    {
        Text = "Worlds Adrift Reborn - Patcher";
        MinimumSize = new Size(640, 440);
        Size = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var lblDir = new Label { Text = "Worlds Adrift install folder:", AutoSize = true, Location = new Point(12, 15) };

        _dirBox = new TextBox
        {
            Location = new Point(12, 36),
            Width = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = _cfg.InstallDir ?? "",
        };
        _browse = new Button { Text = "Browse...", Location = new Point(580, 34), Width = 110, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _browse.Click += (_, _) => OnBrowse();

        _check = new Button { Text = "Check for updates", Location = new Point(12, 72), Width = 150 };
        _check.Click += async (_, _) => await OnCheckAsync();

        _patch = new Button { Text = "Patch", Location = new Point(172, 72), Width = 110, Enabled = false };
        _patch.Click += async (_, _) => await OnPatchAsync();

        _versions = new Label
        {
            Location = new Point(300, 76),
            AutoSize = true,
            Text = $"You have: {_cfg.InstalledVersion ?? "unknown"}    Latest: -",
        };

        _log = new TextBox
        {
            Location = new Point(12, 110),
            Size = new Size(678, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(28, 32, 38),
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 9f),
        };

        Controls.AddRange(new Control[] { lblDir, _dirBox, _browse, _check, _patch, _versions, _log });

        var v = PatchEngine.ValidateInstall(_dirBox.Text);
        Log(v.Message);
    }

    // ---- UI actions ------------------------------------------------------

    private void OnBrowse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select your Worlds Adrift folder" };
        if (!string.IsNullOrWhiteSpace(_dirBox.Text) && Directory.Exists(_dirBox.Text))
            dlg.SelectedPath = _dirBox.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _dirBox.Text = dlg.SelectedPath;
            var v = PatchEngine.ValidateInstall(_dirBox.Text);
            Log(v.Message);
        }
    }

    private async Task OnCheckAsync()
    {
        var v = PatchEngine.ValidateInstall(_dirBox.Text);
        if (!v.Ok) { Log("Cannot continue: " + v.Message); return; }

        SetBusy(true);
        try
        {
            var engine = new PatchEngine(_http, Log);
            _latest = await engine.FetchManifestAsync(_cfg.EffectiveManifestUrl);
            _versions.Text = $"You have: {_cfg.InstalledVersion ?? "unknown"}    Latest: {_latest.Version}";

            int need = 0;
            foreach (var p in engine.Plan(_dirBox.Text, _latest))
                if (p.State is PatchEngine.FileState.Missing or PatchEngine.FileState.Changed)
                {
                    Log($"  needs update: {p.File.DestPath} ({p.State})");
                    need++;
                }

            if (need == 0) { Log("You are up to date. Nothing to do."); _patch.Enabled = false; }
            else { Log($"{need} file(s) will be updated. Click Patch."); _patch.Enabled = true; }
        }
        catch (Exception e) { Log("ERROR checking for updates: " + e.Message); }
        finally { SetBusy(false); }
    }

    private async Task OnPatchAsync()
    {
        var v = PatchEngine.ValidateInstall(_dirBox.Text);
        if (!v.Ok) { Log("Cannot continue: " + v.Message); return; }
        if (_latest is null) { await OnCheckAsync(); if (_latest is null) return; }

        SetBusy(true);
        try
        {
            var engine = new PatchEngine(_http, Log);
            var r = await engine.ApplyAsync(_dirBox.Text, _latest!);

            _cfg.InstallDir = _dirBox.Text;
            if (!r.AnyFailed)
            {
                _cfg.InstalledVersion = _latest!.Version;
                _versions.Text = $"You have: {_cfg.InstalledVersion}    Latest: {_latest.Version}";
            }
            _cfg.Save();

            if (r.AnyFailed)
                Log("Some files failed. Close the game if it is running and click Patch again.");
            else if (r.AnyChanged)
                Log("Patched. You can close this and start the game.");
            else
                Log("Already up to date.");

            _patch.Enabled = r.AnyFailed;
        }
        catch (Exception e) { Log("ERROR while patching: " + e.Message); }
        finally { SetBusy(false); }
    }

    // ---- plumbing --------------------------------------------------------

    private void SetBusy(bool busy)
    {
        _check.Enabled = !busy;
        _browse.Enabled = !busy;
        _dirBox.Enabled = !busy;
        if (busy) _patch.Enabled = false;
        UseWaitCursor = busy;
    }

    private void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
        _log.AppendText(line + Environment.NewLine);
    }
}
