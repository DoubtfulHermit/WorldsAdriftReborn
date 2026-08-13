using System.Text;

namespace WAPatch;

/// <summary>
/// Installs only the public Wareborn connection values into BepInEx's config.
/// The whole file is deliberately not shipped in the manifest: doing that would
/// overwrite a player's controls, diagnostics and future personal settings.
/// </summary>
internal static class WarebornConnectionConfig
{
    internal const string RelativePath = "BepInEx/config/WorldsAdriftReborn.cfg";

    private static readonly (string Section, string Key, string Value)[] Values =
    {
        ("GameServer", "GameServer_Host", "62.171.161.19"),
        ("GameServer", "GameServer_Port", "7779"),
        ("REST", "REST_ServerUrl", "http://62.171.161.19:8085"),
        ("REST", "REST_ServerDeploymentUrl", "http://62.171.161.19:8085/deploymentStatus"),
    };

    internal sealed record Result(bool Changed, string Path, string? BackupPath);

    internal static string PathFor(string installDir) =>
        Path.GetFullPath(Path.Combine(installDir,
            RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    internal static bool NeedsUpdate(string installDir)
    {
        string path = PathFor(installDir);
        if (!File.Exists(path)) return true;
        return NeedsUpdateText(File.ReadAllText(path));
    }

    internal static Result Ensure(string installDir)
    {
        string path = PathFor(installDir);
        string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (!NeedsUpdateText(existing)) return new Result(false, path, null);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string? backup = null;
        if (File.Exists(path))
        {
            backup = path + ".pre-wareborn.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
        }

        string updated = Merge(existing);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
        return new Result(true, path, backup);
    }

    internal static bool NeedsUpdateText(string text)
    {
        string? section = null;
        var found = new Dictionary<(string Section, string Key), bool>();

        foreach (string raw in SplitLines(text))
        {
            string line = raw.Trim();
            if (TrySection(line, out string? parsedSection))
            {
                section = parsedSection;
                continue;
            }
            if (section is null || line.StartsWith('#') || line.StartsWith(';')) continue;

            int equals = line.IndexOf('=');
            if (equals < 1) continue;
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            foreach (var wanted in Values)
            {
                if (section.Equals(wanted.Section, StringComparison.OrdinalIgnoreCase)
                    && key.Equals(wanted.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var id = (wanted.Section, wanted.Key);
                    bool matches = value.Equals(wanted.Value, StringComparison.Ordinal);
                    found[id] = found.TryGetValue(id, out bool prior) ? prior && matches : matches;
                }
            }
        }

        return Values.Any(v => !found.TryGetValue((v.Section, v.Key), out bool matches) || !matches);
    }

    internal static string Merge(string text)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(text).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        foreach (IGrouping<string, (string Section, string Key, string Value)> group
                 in Values.GroupBy(v => v.Section, StringComparer.OrdinalIgnoreCase))
        {
            int header = FindSection(lines, group.Key);
            if (header < 0)
            {
                if (lines.Count > 0 && lines[^1].Length != 0) lines.Add(string.Empty);
                lines.Add("[" + group.Key + "]");
                foreach (var wanted in group) lines.Add(wanted.Key + " = " + wanted.Value);
                continue;
            }

            int end = FindNextSection(lines, header + 1);
            foreach (var wanted in group)
            {
                bool replaced = false;
                for (int i = header + 1; i < end; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
                    int equals = trimmed.IndexOf('=');
                    if (equals < 1) continue;
                    if (!trimmed[..equals].Trim().Equals(wanted.Key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Replace every duplicate with the same value, so whichever
                    // occurrence BepInEx accepts cannot point at localhost.
                    lines[i] = wanted.Key + " = " + wanted.Value;
                    replaced = true;
                }
                if (!replaced)
                {
                    lines.Insert(end, wanted.Key + " = " + wanted.Value);
                    end++;
                }
            }
        }

        return string.Join(newline, lines) + newline;
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n').ToList();

    private static int FindSection(IReadOnlyList<string> lines, string section)
    {
        for (int i = 0; i < lines.Count; i++)
            if (TrySection(lines[i].Trim(), out string? parsed)
                && parsed!.Equals(section, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static int FindNextSection(IReadOnlyList<string> lines, int start)
    {
        for (int i = start; i < lines.Count; i++)
            if (TrySection(lines[i].Trim(), out _)) return i;
        return lines.Count;
    }

    private static bool TrySection(string line, out string? section)
    {
        if (line.Length >= 3 && line[0] == '[' && line[^1] == ']')
        {
            section = line[1..^1].Trim();
            return section.Length > 0;
        }
        section = null;
        return false;
    }
}
