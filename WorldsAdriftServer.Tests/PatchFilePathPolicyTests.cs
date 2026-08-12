using System.IO;
using WorldsAdriftServer.Patch;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The one rule that keeps <c>/patch/files/&lt;name&gt;</c> from reading anything on
    /// a root-run host. Each assertion is an escape an attacker would try - a
    /// separator of either platform, a <c>..</c> climb, a drive letter, an
    /// absolute path, an encoded separator that has already been decoded to a real
    /// one - plus the happy path that must still work. The names here are what the
    /// handler passes in AFTER URL-decoding, which is why "..%2f" appears as its
    /// decoded "../" form.
    /// </summary>
    public class PatchFilePathPolicyTests
    {
        private const string PatchDir = "/opt/wareborn/patch";

        // ---- happy path ----------------------------------------------------

        [Theory]
        [InlineData("Assembly-CSharp.dll")]
        [InlineData("WorldsAdriftReborn.dll")]
        [InlineData("some-file_1.2.3.bin")]
        [InlineData("plain.txt")]
        public void A_plain_file_name_is_safe(string name)
        {
            Assert.True(PatchFilePathPolicy.IsSafeFileName(name));

            Assert.True(PatchFilePathPolicy.TryResolveFilePath(PatchDir, name, out string? full));
            Assert.NotNull(full);

            // It resolves to exactly <patchDir>/files/<name>.
            string expected = Path.GetFullPath(Path.Combine(PatchDir, "files", name));
            Assert.Equal(expected, full);

            // ...and never outside the files/ dir.
            string filesRoot = Path.GetFullPath(Path.Combine(PatchDir, "files"))
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(filesRoot, full!);
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
        [InlineData("/opt/wareborn/patch/files/../../secret")] // absolute + climb
        [InlineData("C:\\Windows\\win.ini")]     // drive-letter absolute
        [InlineData("C:evil")]                    // drive-relative (colon)
        [InlineData("evil:stream")]               // colon anywhere (NTFS ADS / drive)
        [InlineData("..%2f..%2fetc")]             // NOT decoded -> contains ".." -> still rejected
        [InlineData("foo..bar")]                  // embedded dot-dot, conservatively refused
        [InlineData(" ")]                          // whitespace only
        [InlineData("")]                          // empty
        [InlineData(" leading.dll")]              // untrimmed - not a clean segment
        [InlineData("trailing.dll ")]             // untrimmed - not a clean segment
        public void An_unsafe_name_is_rejected(string name)
        {
            Assert.False(PatchFilePathPolicy.IsSafeFileName(name));

            Assert.False(PatchFilePathPolicy.TryResolveFilePath(PatchDir, name, out string? full));
            Assert.Null(full);
        }

        [Fact]
        public void A_null_name_is_rejected()
        {
            Assert.False(PatchFilePathPolicy.IsSafeFileName(null));
            Assert.False(PatchFilePathPolicy.TryResolveFilePath(PatchDir, null, out string? full));
            Assert.Null(full);
        }

        [Fact]
        public void A_nul_byte_in_the_name_is_rejected()
        {
            // A NUL can truncate a path in the native open() below the managed
            // layer, so "foo.dll\0.png" must never open "foo.dll".
            Assert.False(PatchFilePathPolicy.IsSafeFileName("foo.dll\0.png"));
        }

        // ---- containment backstop ------------------------------------------

        [Fact]
        public void The_resolved_path_never_escapes_the_files_dir()
        {
            // Even a name that named a sibling dir of files/ ("files-evil") must
            // not be reachable: containment is checked with a trailing separator,
            // so a shared prefix is not enough.
            Assert.False(PatchFilePathPolicy.TryResolveFilePath(PatchDir, "..", out _));
            Assert.False(PatchFilePathPolicy.TryResolveFilePath(PatchDir, "../files-evil/x", out _));
        }

        [Fact]
        public void An_empty_patch_dir_is_rejected()
        {
            Assert.False(PatchFilePathPolicy.TryResolveFilePath("", "ok.dll", out string? full));
            Assert.Null(full);
        }
    }
}
