namespace WorldsAdriftServer.PatchNotes
{
    /// <summary>
    /// Where the notes text comes from.
    ///
    /// TWO SOURCES, AND WHY.
    ///
    /// The one that ships is a FILE, <c>Web/Assets/patch-notes.md</c>, embedded in
    /// the binary like every other web asset. Patch notes describe a build, and a
    /// build is a commit; keeping them in the repository is what makes "what the
    /// page says shipped" and "what shipped" the same object, reviewable in the
    /// same diff and reachable in the same history. A release whose notes live
    /// only in a database row can be edited afterwards with nothing to compare
    /// against, which is the one thing a changelog must not be.
    ///
    /// The one that overrides it is a ROW: <c>server_config['patch_notes']</c>.
    /// That table already exists at schema 3 and was built for exactly this - its
    /// own comment says a key-value shape lets the next setting "be an INSERT
    /// rather than a migration" - so this costs NO migration. Production runs the
    /// game server and this server against one shared database, and a migration
    /// shipped in one binary alone turns persistence off for the other; there is
    /// nothing here worth that risk.
    ///
    /// So: the file is the record, and the row is the correction. Fixing a typo,
    /// or adding a line about something that broke an hour after a deploy, is a
    /// paste into the admin panel. The next release moves it back into the file
    /// and clears the row.
    ///
    /// The database is never allowed to take the page down. A row that cannot be
    /// read - Postgres restarting, credentials wrong, whatever - falls back to
    /// the file, which is in this process's own memory and cannot fail.
    /// </summary>
    internal static class PatchNotesSource
    {
        /// <summary>The <c>server_config</c> key an operator's override lives under.</summary>
        internal const string ConfigKey = "patch_notes";

        /// <summary>The embedded file the notes ship in.</summary>
        internal const string AssetName = "patch-notes.md";

        /// <summary>The notes as committed. Never fails, never touches the network.</summary>
        internal static string Committed() => Web.WebAssets.Read(AssetName);

        /// <summary>
        /// The override an operator has stored, or null if there is none. Any
        /// failure reads as "no override" rather than as an error: see the class
        /// comment - the page must not depend on the database being up.
        /// </summary>
        internal static string? Override()
        {
            try
            {
                string? stored = Persistence.Accounts.ServerConfig.Get(ConfigKey);
                return string.IsNullOrWhiteSpace(stored) ? null : stored;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The text the page should render right now.</summary>
        internal static string Current() => Override() ?? Committed();

        /// <summary>
        /// True when the text is worth storing as an override. An operator who
        /// clears the box means "go back to the committed notes", and
        /// <c>server_config</c> refuses a blank value anyway, so the caller
        /// deletes the row instead of writing an empty one.
        /// </summary>
        internal static bool IsStorable(string? text) => !string.IsNullOrWhiteSpace(text);
    }
}
