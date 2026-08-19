using System.Globalization;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace RelayBot
{
    /// <summary>
    /// Reads and writes the recorded soak baseline - the ONLY file-touching part
    /// of the level gate; the judgement itself is pure and lives in
    /// <see cref="SoakLevelPolicy"/> where the fast suite can reach it.
    ///
    /// WHY A FILE AND NOT A CONSTANT. The absolute ceilings say what the relay
    /// may never do; the baseline says what this world, on this harness, ACTUALLY
    /// did when it was known good. A step that stays under the ceilings - content
    /// that costs 10 ms and two points of delivery, say - is invisible to an
    /// absolute check and obvious against a recording. Keeping it in the repo
    /// makes moving it a reviewable diff instead of a silent drift, which is the
    /// whole failure this gate was extended to prevent.
    ///
    /// WHY IT IS ADVISORY WHEN ABSENT. A fresh checkout, a new world recipe or a
    /// deliberately different bot placement all produce a run with no comparable
    /// recording. None of those is a regression, so a missing or unreadable
    /// baseline prints a line and steps aside; the absolute ceilings still judge.
    /// </summary>
    internal static class SoakBaselineFile
    {
        /// <summary>
        /// What a baseline records. `world` is free text naming the shape of the
        /// run the numbers came from, because a Haven-spawn baseline says nothing
        /// about a tier-1 island run and comparing them would be the same class
        /// of lie this gate exists to stop.
        /// </summary>
        internal sealed record Recorded(
            string World,
            string Recorded_At,
            string Commit,
            double Minutes,
            double Staleness_P50_Ms,
            double Staleness_P95_Ms,
            double Overstale_Share,
            long Matched,
            long Sends);

        internal static SoakLevelPolicy.SoakLevels LevelsOf(Recorded recorded) =>
            new(recorded.Staleness_P50_Ms, recorded.Staleness_P95_Ms,
                recorded.Overstale_Share, recorded.Matched, recorded.Sends);

        /// <summary>
        /// The baseline for a world key, or null when there is none to compare
        /// against. Never throws: a malformed baseline must not be able to fail
        /// a soak, only to stop being consulted.
        /// </summary>
        internal static Recorded Read(string path, string worldKey, out string why)
        {
            why = "";
            try
            {
                if (!File.Exists(path))
                {
                    why = $"no baseline file at {path}";
                    return null;
                }

                Dictionary<string, Recorded> all =
                    JsonSerializer.Deserialize<Dictionary<string, Recorded>>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (all == null || !all.TryGetValue(worldKey, out Recorded recorded))
                {
                    why = $"{path} records no baseline for world '{worldKey}'";
                    return null;
                }

                if (recorded.Sends <= 0 || recorded.Matched <= 0)
                {
                    why = $"the recorded baseline for '{worldKey}' measured nothing";
                    return null;
                }

                return recorded;
            }
            catch (Exception ex)
            {
                why = $"{path} could not be read ({ex.GetType().Name}: {ex.Message})";
                return null;
            }
        }

        /// <summary>
        /// Record this run as the baseline for its world key, preserving every
        /// other key in the file. Only ever called for an explicit
        /// --write-baseline: a gate that re-records itself on every green run
        /// would ratchet quietly and measure nothing.
        /// </summary>
        internal static void Write(string path, string worldKey, Recorded recorded)
        {
            Dictionary<string, Recorded> all = new(StringComparer.Ordinal);
            try
            {
                if (File.Exists(path))
                {
                    all = JsonSerializer.Deserialize<Dictionary<string, Recorded>>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? all;
                }
            }
            catch
            {
                // An unreadable existing file is replaced rather than allowed to
                // block the deliberate re-record the operator just asked for.
            }

            all[worldKey] = recorded;

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(all,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[soak] recorded baseline '{0}' -> {1} (p50 {2:0.##} ms, p95 {3:0.##} ms, "
                + "{4:0.#}% delivered). COMMIT THIS FILE if the run was known good.",
                worldKey, path, recorded.Staleness_P50_Ms, recorded.Staleness_P95_Ms,
                recorded.Sends > 0 ? 100.0 * recorded.Matched / recorded.Sends : 0.0));
        }
    }
}
