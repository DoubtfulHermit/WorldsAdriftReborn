using System.IO;

namespace WorldsAdriftServer.Patch
{
    /// <summary>
    /// The single rule that keeps <c>/patch/files/&lt;name&gt;</c> from becoming a
    /// read-anything hole. This process runs as root on the VPS, so a name that
    /// escapes the patch dir hands an attacker the host's filesystem; this class
    /// is where that is stopped, and it is pure so a test can hammer every escape
    /// vector without a socket or a disk.
    ///
    /// <para>Two independent gates, either of which is sufficient on its own:</para>
    /// <list type="number">
    ///   <item>A whitelist-by-rejection on the NAME: it must be a single path
    ///   segment - no separator of EITHER platform (<c>/</c> or <c>\</c>), no
    ///   <c>..</c> anywhere, no drive/colon, not rooted. This gate does not depend
    ///   on how the runtime interprets separators, so it holds identically whether
    ///   the server runs native on Linux (tests) or under Wine as a Windows process
    ///   (production), where <c>\</c> IS a separator and <c>C:</c> IS a drive.</item>
    ///   <item>A canonical-containment backstop: resolve the final path with
    ///   <see cref="Path.GetFullPath(string)"/> and confirm it still sits inside the
    ///   canonical <c>&lt;patchDir&gt;/files/</c> directory. Even if some name slipped
    ///   the first gate on a platform quirk, a resolved path that points outside is
    ///   refused.</item>
    /// </list>
    ///
    /// The name passed here must ALREADY be URL-decoded by the caller, so that an
    /// encoded <c>..%2f</c> is seen as the <c>../</c> it decodes to and rejected by
    /// the separator/dot-dot checks rather than sailing through as opaque bytes.
    /// </summary>
    internal static class PatchFilePathPolicy
    {
        /// <summary>
        /// Whether <paramref name="name"/> is a single, safe file-name segment.
        /// Pure string inspection, no disk, no platform assumptions. Rejects
        /// null/empty/whitespace, either separator, any <c>..</c>, a drive/colon,
        /// and a leading separator (rooted). Everything else - a plain
        /// <c>Foo.dll</c> - passes.
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
        /// Resolves <c>&lt;patchDir&gt;/files/&lt;name&gt;</c> and, only if the name is
        /// safe AND the resolved path is still contained under the canonical
        /// <c>files</c> directory, hands back the full path to open. Returns false
        /// - with <paramref name="fullPath"/> null - for every rejection, so a
        /// caller cannot accidentally open an escaped path.
        /// </summary>
        internal static bool TryResolveFilePath(string patchDir, string? name, out string? fullPath)
        {
            fullPath = null;

            if (string.IsNullOrWhiteSpace(patchDir) || !IsSafeFileName(name))
            {
                return false;
            }

            // Canonical files root, with a trailing separator so a prefix test
            // cannot be fooled by a sibling like "<patchDir>/files-evil".
            string filesRoot = Path.GetFullPath(Path.Combine(patchDir, "files"));
            string filesRootWithSep = filesRoot.EndsWith(Path.DirectorySeparatorChar)
                ? filesRoot
                : filesRoot + Path.DirectorySeparatorChar;

            string candidate = Path.GetFullPath(Path.Combine(filesRoot, name!));

            // Must sit strictly inside files/ (not be files/ itself, not a sibling).
            // OrdinalIgnoreCase so the Wine/Windows case-insensitive filesystem
            // cannot dodge the check with a case flip; harmless on Linux where the
            // segments are byte-identical anyway.
            if (!candidate.StartsWith(filesRootWithSep, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
