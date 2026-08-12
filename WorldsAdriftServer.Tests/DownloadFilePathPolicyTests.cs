using System.IO;
using WorldsAdriftServer.Download;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The containment rule for <c>/download/&lt;name&gt;</c> on a root-run host.
    /// The handler only ever passes the fixed <c>WAPatch.exe</c>, but the rule is
    /// applied regardless and must reject every escape a refactor might one day
    /// feed it - a separator of either platform, a <c>..</c> climb, a drive letter,
    /// an absolute path, an already-decoded encoded separator - while still letting
    /// a plain name through. The mirror of <see cref="PatchFilePathPolicyTests"/>,
    /// with the file sitting DIRECTLY in the downloads dir rather than under files/.
    /// </summary>
    public class DownloadFilePathPolicyTests
    {
        private const string DownloadDir = "/opt/wareborn/downloads";

        // ---- happy path ----------------------------------------------------

        [Theory]
        [InlineData("WAPatch.exe")]
        [InlineData("installer.bin")]
        [InlineData("some-file_1.2.3.zip")]
        public void A_plain_file_name_is_safe(string name)
        {
            Assert.True(DownloadFilePathPolicy.IsSafeFileName(name));

            Assert.True(DownloadFilePathPolicy.TryResolveDownloadPath(DownloadDir, name, out string? full));
            Assert.NotNull(full);

            // It resolves to exactly <downloadDir>/<name>.
            string expected = Path.GetFullPath(Path.Combine(DownloadDir, name));
            Assert.Equal(expected, full);

            // ...and never outside the downloads dir.
            string root = Path.GetFullPath(DownloadDir) + Path.DirectorySeparatorChar;
            Assert.StartsWith(root, full!);
        }

        // ---- traversal / escape vectors ------------------------------------

        [Theory]
        [InlineData("../../etc/passwd")]        // classic climb (forward slash)
        [InlineData("..")]                       // bare parent
        [InlineData(".")]                        // bare current dir
        [InlineData("a/b")]                       // forward-slash separator
        [InlineData("sub/../../etc/passwd")]     // climb after a subdir
        [InlineData("..\\..\\windows\\win.ini")] // backslash climb (Wine/Windows sep)
        [InlineData("foo\\bar")]                  // backslash separator
        [InlineData("/etc/passwd")]              // absolute (leading slash)
        [InlineData("/opt/wareborn/downloads/../../secret")] // absolute + climb
        [InlineData("C:\\Windows\\win.ini")]     // drive-letter absolute
        [InlineData("C:evil")]                    // drive-relative (colon)
        [InlineData("evil:stream")]               // colon anywhere (NTFS ADS / drive)
        [InlineData("..%2f..%2fetc")]             // NOT decoded -> contains ".." -> still rejected
        [InlineData("foo..bar")]                  // embedded dot-dot, conservatively refused
        [InlineData(" ")]                          // whitespace only
        [InlineData("")]                          // empty
        [InlineData(" leading.exe")]              // untrimmed - not a clean segment
        [InlineData("trailing.exe ")]             // untrimmed - not a clean segment
        public void An_unsafe_name_is_rejected(string name)
        {
            Assert.False(DownloadFilePathPolicy.IsSafeFileName(name));

            Assert.False(DownloadFilePathPolicy.TryResolveDownloadPath(DownloadDir, name, out string? full));
            Assert.Null(full);
        }

        [Fact]
        public void A_null_name_is_rejected()
        {
            Assert.False(DownloadFilePathPolicy.IsSafeFileName(null));
            Assert.False(DownloadFilePathPolicy.TryResolveDownloadPath(DownloadDir, null, out string? full));
            Assert.Null(full);
        }

        [Fact]
        public void A_nul_byte_in_the_name_is_rejected()
        {
            // A NUL can truncate a path in the native open() below the managed
            // layer, so "WAPatch.exe\0.png" must never open "WAPatch.exe".
            Assert.False(DownloadFilePathPolicy.IsSafeFileName("WAPatch.exe\0.png"));
        }

        // ---- containment backstop ------------------------------------------

        [Fact]
        public void The_resolved_path_never_escapes_the_downloads_dir()
        {
            // A name that named a sibling dir ("downloads-evil") must not be
            // reachable: containment is checked with a trailing separator, so a
            // shared prefix is not enough.
            Assert.False(DownloadFilePathPolicy.TryResolveDownloadPath(DownloadDir, "..", out _));
            Assert.False(DownloadFilePathPolicy.TryResolveDownloadPath(DownloadDir, "../downloads-evil/x", out _));
        }

        [Fact]
        public void An_empty_download_dir_is_rejected()
        {
            Assert.False(DownloadFilePathPolicy.TryResolveDownloadPath("", "WAPatch.exe", out string? full));
            Assert.Null(full);
        }

        [Fact]
        public void The_fixed_patcher_name_resolves()
        {
            // The one name the handler actually serves must pass end to end.
            Assert.True(DownloadFilePathPolicy.TryResolveDownloadPath(
                DownloadDir, DownloadConfig.PatcherFileName, out string? full));
            Assert.NotNull(full);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(DownloadDir, "WAPatch.exe")),
                full);
        }
    }
}
