using System.IO;

namespace WorldsAdriftServer.Download
{
    /// <summary>
    /// The rule that keeps a served download from escaping the downloads dir. The
    /// only filename this handler ever serves is the fixed <c>WAPatch.exe</c>, so
    /// there is no attacker-controlled path in practice - but this process runs as
    /// root on the VPS, and a resolve-then-serve with no containment check is one
    /// refactor away from a read-anything hole. So the rule exists, is applied on
    /// the fixed name too, and is pure so a test can hammer every escape vector
    /// without a socket or a disk.
    ///
    /// It follows <see cref="WorldsAdriftServer.Patch.PatchFilePathPolicy"/> exactly,
    /// the one difference being that a download sits DIRECTLY in the downloads dir
    /// rather than under a <c>files/</c> subdir. Two independent gates, either
    /// sufficient on its own: a whitelist-by-rejection on the name (a single path
    /// segment, no separator of either platform, no <c>..</c>, no drive/colon, not
    /// rooted), then a canonical-containment backstop via
    /// <see cref="Path.GetFullPath(string)"/>.
    ///
    /// Any name passed here must ALREADY be URL-decoded by the caller, so an
    /// encoded <c>..%2f</c> is seen as the <c>../</c> it decodes to and rejected.
    /// </summary>
    internal static class DownloadFilePathPolicy
    {
        /// <summary>
        /// Whether <paramref name="name"/> is a single, safe file-name segment.
        /// Pure string inspection, no disk, no platform assumptions. Rejects
        /// null/empty/whitespace, either separator, any <c>..</c>, a drive/colon,
        /// a leading separator (rooted) and control characters. Everything else -
        /// a plain <c>WAPatch.exe</c> - passes.
        /// </summary>
        internal static bool IsSafeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // A name the caller has to trim was not a clean segment; refuse it
            // rather than silently normalise attacker input.
            if (name != name.Trim())
            {
                return false;
            }

            foreach (char c in name)
            {
                if (c == '/' || c == '\\' || c == ':')
                {
                    return false;
                }

                // No control characters or NUL - a NUL can truncate a path in a
                // native open() below the managed layer.
                if (char.IsControl(c))
                {
                    return false;
                }
            }

            // "." and ".." as whole names, and any embedded parent traversal.
            if (name == "." || name.Contains(".."))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves <c>&lt;downloadDir&gt;/&lt;name&gt;</c> and, only if the name is
        /// safe AND the resolved path is still contained under the canonical
        /// downloads directory, hands back the full path to open. Returns false -
        /// with <paramref name="fullPath"/> null - for every rejection, so a caller
        /// cannot accidentally open an escaped path.
        /// </summary>
        internal static bool TryResolveDownloadPath(string downloadDir, string? name, out string? fullPath)
        {
            fullPath = null;

            if (string.IsNullOrWhiteSpace(downloadDir) || !IsSafeFileName(name))
            {
                return false;
            }

            // Canonical downloads root, with a trailing separator so a prefix test
            // cannot be fooled by a sibling like "<downloadDir>-evil".
            string root = Path.GetFullPath(downloadDir);
            string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            string candidate = Path.GetFullPath(Path.Combine(root, name!));

            // Must sit strictly inside the dir (not be the dir itself, not a
            // sibling). OrdinalIgnoreCase so the Wine/Windows case-insensitive
            // filesystem cannot dodge the check with a case flip; harmless on Linux
            // where the segments are byte-identical anyway.
            if (!candidate.StartsWith(rootWithSep, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
