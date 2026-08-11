using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Persistence
{
    /// <summary>
    /// Reads and writes one JSON document on disk, atomically enough that a crash
    /// mid-write cannot leave a truncated file behind, and never lets a corrupt or
    /// unreadable file take the server down.
    ///
    /// It is the game server's counterpart of the login server's
    /// <c>WorldsAdriftServer.Persistence.JsonFileStore</c> - same temp-then-replace
    /// discipline, same corrupt-file quarantine, same Wine fallback for the one
    /// prefix where <c>MoveFileEx</c> refuses the replace flag - but built on
    /// <see cref="System.Text.Json"/> so the pure Multiplayer assembly takes NO new
    /// dependency (the same library <c>CharacterIdentity</c> already parses 1088
    /// with). Deliberately plain files: the servers run under Wine, the dataset is a
    /// few kilobytes, and a human should be able to open and fix it.
    ///
    /// Pure I/O and engine-free, so a round trip and the atomic-write behaviour are
    /// asserted natively on Linux with a temp directory rather than by restarting a
    /// live server.
    /// </summary>
    public static class AtomicJsonFile
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        /// <summary>
        /// The document at <paramref name="path"/>, or null when the file is
        /// missing, empty, or unreadable. Null is the normal "nothing stored yet"
        /// answer and means "start from defaults", not "something went wrong" - the
        /// difference is in the log. A file that will not parse is moved aside to
        /// <c>.broken</c> rather than deleted, so a wipe is never silent and the bad
        /// payload survives for inspection.
        /// </summary>
        public static T? Read<T>(string path) where T : class
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

                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] failed to read " + path + ": " + e.Message);
                TryQuarantine(path);
                return null;
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="path"/> atomically:
        /// serialise to a sibling <c>.tmp</c> file, then replace the target with a
        /// single move, so a reader never sees a half-written document and a crash
        /// between the two steps leaves the LAST GOOD file intact. Returns whether
        /// the write succeeded; a failure is logged and swallowed so a full disk or a
        /// read-only mount can never unwind into the poll loop.
        /// </summary>
        public static bool Write(string path, object value)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(value, value.GetType(), Options);
                string tmp = path + ".tmp";

                File.WriteAllText(tmp, json);

                try
                {
                    File.Move(tmp, path, true);
                }
                catch (Exception)
                {
                    // Wine's MoveFileEx has been known to refuse the replace flag on
                    // some prefixes. Losing the file for an instant is worse than
                    // losing atomicity, but this path only runs when the atomic one
                    // already failed.
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
