namespace WorldsAdriftReborn.Storage.Schema
{
    /// <summary>
    /// Decides which schema scripts a file still needs. Pure: it takes a version
    /// number and a list of scripts and returns a list of scripts. No connection,
    /// no file, no clock - so the ordering rules can be tested without a database
    /// and, more usefully, without waiting until version 4 to find out they are
    /// wrong.
    /// </summary>
    public static class SchemaMigrator
    {
        /// <summary>
        /// The version a file is at once every script has been applied.
        /// </summary>
        public static int TargetVersion(IReadOnlyList<string> scripts)
        {
            if (scripts == null)
            {
                throw new ArgumentNullException(nameof(scripts));
            }

            return scripts.Count;
        }

        /// <summary>
        /// The scripts still to run, in order, for a file stamped
        /// <paramref name="currentVersion"/>. Empty when it is already current.
        ///
        /// A file stamped higher than we know about is a downgrade: the operator
        /// has rolled the server back under a database a newer build wrote. That
        /// throws rather than doing nothing, because "do nothing" here means
        /// running new code against an old schema and discovering it one INSERT
        /// at a time.
        /// </summary>
        public static IReadOnlyList<string> ScriptsToApply(
            int currentVersion,
            IReadOnlyList<string> scripts)
        {
            if (scripts == null)
            {
                throw new ArgumentNullException(nameof(scripts));
            }

            if (currentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentVersion),
                    currentVersion,
                    "A schema version cannot be negative.");
            }

            if (currentVersion > scripts.Count)
            {
                throw new InvalidOperationException(
                    "The database is at schema version " + currentVersion
                    + " but this build only knows up to " + scripts.Count
                    + ". Refusing to run: a newer build wrote this file.");
            }

            List<string> pending = new List<string>();

            for (int i = currentVersion; i < scripts.Count; i++)
            {
                pending.Add(scripts[i]);
            }

            return pending;
        }
    }
}
