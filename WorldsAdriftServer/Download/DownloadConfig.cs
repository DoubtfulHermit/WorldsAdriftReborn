using System.IO;

namespace WorldsAdriftServer.Download
{
    /// <summary>
    /// Where the downloadable client binaries live on disk. The WAPatch installer
    /// is served THROUGH this login server (a native host process that can read the
    /// path) for the same reason the patch files are: the Caddy that fronts the
    /// public host is a container that cannot see host paths, so a static
    /// <c>file_server</c> from this dir would 404 and this process is the only
    /// thing that can hand the bytes out.
    ///
    /// Default is the VPS layout <c>/opt/wareborn/downloads</c> (with
    /// <c>WAPatch.exe</c> directly under it); override with
    /// <see cref="DirVariable"/> so a dev box or a test can point at a temp dir,
    /// exactly how <see cref="WorldsAdriftServer.Patch.PatchConfig"/> reads
    /// <c>WAREBORN_PATCH_DIR</c>.
    /// </summary>
    internal static class DownloadConfig
    {
        /// <summary>The environment variable that overrides the downloads directory.</summary>
        internal const string DirVariable = "WAREBORN_DOWNLOAD_DIR";

        /// <summary>The default downloads directory on the VPS.</summary>
        internal const string DefaultDir = "/opt/wareborn/downloads";

        /// <summary>The one file this page hands out. Fixed - never taken from the URL.</summary>
        internal const string PatcherFileName = "WAPatch.exe";

        /// <summary>The downloads directory, from the env var or the default.</summary>
        internal static string DownloadDir
        {
            get
            {
                string? configured = System.Environment.GetEnvironmentVariable(DirVariable);
                return string.IsNullOrWhiteSpace(configured) ? DefaultDir : configured;
            }
        }

        /// <summary>Absolute path to the patcher binary.</summary>
        internal static string PatcherPath => Path.Combine(DownloadDir, PatcherFileName);
    }
}
