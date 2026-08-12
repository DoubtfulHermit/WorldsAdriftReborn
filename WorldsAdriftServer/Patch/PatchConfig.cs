using System.IO;

namespace WorldsAdriftServer.Patch
{
    /// <summary>
    /// Where the client-patch bytes live on disk. The manifest and every file are
    /// served THROUGH this login server (a native host process that can read the
    /// path), because the Caddy that fronts the public host is a container that
    /// cannot see host paths - so a static <c>file_server</c> from the patch dir
    /// 404s and this process is the only thing that can hand the bytes out.
    ///
    /// Default is the VPS layout <c>/opt/wareborn/patch</c> (with
    /// <c>manifest.json</c> and <c>files/</c> under it); override with
    /// <see cref="DirVariable"/> so a dev box or a test can point at a temp dir,
    /// exactly how <c>WAREBORN_DB</c> and <c>WAREBORN_DATA_DIR</c> are read.
    /// </summary>
    internal static class PatchConfig
    {
        /// <summary>The environment variable that overrides the patch directory.</summary>
        internal const string DirVariable = "WAREBORN_PATCH_DIR";

        /// <summary>The default patch directory on the VPS.</summary>
        internal const string DefaultDir = "/opt/wareborn/patch";

        /// <summary>The patch directory, from the env var or the default.</summary>
        internal static string PatchDir
        {
            get
            {
                string? configured = System.Environment.GetEnvironmentVariable(DirVariable);
                return string.IsNullOrWhiteSpace(configured) ? DefaultDir : configured;
            }
        }

        /// <summary>Absolute path to the manifest file.</summary>
        internal static string ManifestPath => Path.Combine(PatchDir, "manifest.json");
    }
}
