using Newtonsoft.Json;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// Reads and writes JSON documents on disk, atomically enough that a crash
    /// mid-write cannot leave a truncated roster behind.
    ///
    /// Deliberately plain files: the servers run under Wine, where SQLite's
    /// native e_sqlite3 is an extra thing to go wrong, and the whole dataset here
    /// is a few kilobytes that a human should be able to open and fix.
    /// </summary>
    internal static class JsonFileStore
    {
        /// <summary>
        /// Root of all persisted state. Override with WAREBORN_DATA_DIR;
        /// defaults to a "data" folder next to the server binary so a dev box and
        /// the VPS behave the same without configuration.
        /// </summary>
        internal static string DataDir
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable("WAREBORN_DATA_DIR");

                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured;
                }

                return Path.Combine(AppContext.BaseDirectory, "data");
            }
        }

        internal static string PathFor(params string[] parts)
        {
            return Path.Combine(new[] { DataDir }.Concat(parts).ToArray());
        }

        internal static T? Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                // A corrupt file must not take the server down: fall back to
                // "nothing stored" and keep the bad file for inspection.
                Console.WriteLine("[error] failed to read " + path + ": " + e.Message);
                TryQuarantine(path);
                return null;
            }
        }

        internal static bool Write(string path, object value)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonConvert.SerializeObject(value, Formatting.Indented);
                string tmp = path + ".tmp";

                File.WriteAllText(tmp, json);

                try
                {
                    File.Move(tmp, path, true);
                }
                catch (Exception)
                {
                    // Wine's MoveFileEx has been known to refuse the replace flag
                    // on some prefixes. Losing the file for an instant is worse
                    // than losing atomicity, but this path only runs if the
                    // atomic one already failed.
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    File.Move(tmp, path);
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] failed to write " + path + ": " + e.Message);
                return false;
            }
        }

        private static void TryQuarantine(string path)
        {
            try
            {
                string broken = path + ".broken";

                if (File.Exists(broken))
                {
                    File.Delete(broken);
                }

                File.Move(path, broken);
                Console.WriteLine("[info] moved unreadable file aside to " + broken);
            }
            catch
            {
                // Nothing useful to do; the caller already treats this as empty.
            }
        }
    }
}
