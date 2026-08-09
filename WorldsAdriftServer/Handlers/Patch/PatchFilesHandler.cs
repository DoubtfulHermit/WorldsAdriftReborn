using System;
using System.IO;
using NetCoreServer;
using WorldsAdriftServer.Patch;

namespace WorldsAdriftServer.Handlers.Patch
{
    /// <summary>
    /// Serves the client-patch static bytes off disk: the manifest and each file
    /// under <c>&lt;patchDir&gt;/files/</c>. It exists because the public host's Caddy
    /// is a container that cannot read the host's patch dir, so the bytes have to
    /// come from this native process instead of a <c>file_server</c>.
    ///
    /// Glue only: it matches the two routes, asks <see cref="PatchFilePathPolicy"/>
    /// whether a requested name is allowed to be opened, and writes the response.
    /// The DLLs are served as raw bytes via <c>HttpResponse.SetBody(byte[])</c>,
    /// which is the binary-safe path (Content-Length is the byte count, no UTF-8
    /// round-trip) - a text body would corrupt a DLL.
    /// </summary>
    internal static class PatchFilesHandler
    {
        private const string ManifestRoute = "/patch/manifest.json";
        private const string FilesPrefix = "/patch/files/";

        /// <summary>
        /// Handles GET <c>/patch/manifest.json</c> and GET <c>/patch/files/&lt;name&gt;</c>.
        /// Returns true if it took the request so the router does not fall through.
        /// The <c>/patch</c> HTML index is NOT ours - it stays in the router.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            if (string.Equals(path, ManifestRoute, StringComparison.Ordinal))
            {
                if (request.Method != "GET")
                {
                    return false;
                }

                ServeManifest(session);
                return true;
            }

            if (path.StartsWith(FilesPrefix, StringComparison.Ordinal))
            {
                if (request.Method != "GET")
                {
                    return false;
                }

                // Everything after the prefix is the requested name. URL-decode it
                // FIRST so an encoded "..%2f" is seen as the "../" it is and gets
                // rejected by the policy, not passed through as opaque bytes.
                string rawName = path.Substring(FilesPrefix.Length);
                string name = Uri.UnescapeDataString(rawName);

                ServeFile(session, name);
                return true;
            }

            return false;
        }

        private static void ServeManifest(HttpSession session)
        {
            byte[] bytes;
            try
            {
                string manifestPath = PatchConfig.ManifestPath;
                if (!File.Exists(manifestPath))
                {
                    NotFound(session);
                    return;
                }

                bytes = File.ReadAllBytes(manifestPath);
            }
            catch (Exception)
            {
                // Missing, unreadable, racing - one clean 404, never a stack trace
                // and never a hint about what is on disk.
                NotFound(session);
                return;
            }

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", "application/json");
            // The manifest must never be cached stale: a player checking for an
            // update needs whatever was last published, not a CDN/browser copy.
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(bytes);
            session.SendResponseAsync(resp);
        }

        private static void ServeFile(HttpSession session, string name)
        {
            if (!PatchFilePathPolicy.TryResolveFilePath(PatchConfig.PatchDir, name, out string? fullPath)
                || fullPath == null)
            {
                // Rejected name (traversal, separator, drive, ...): same 404 as a
                // simply-missing file, so we never disclose whether a path outside
                // the dir exists.
                NotFound(session);
                return;
            }

            byte[] bytes;
            try
            {
                // Only regular files. A directory or special file that happens to
                // resolve inside files/ is not a patch file - 404 it.
                if (!File.Exists(fullPath))
                {
                    NotFound(session);
                    return;
                }

                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception)
            {
                NotFound(session);
                return;
            }

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            // These are DLLs and other binaries. Octet-stream + the byte-array
            // SetBody keeps them byte-exact; Content-Length is set to the byte
            // count by SetBody, proven by the sha256 round-trip test.
            resp.SetHeader("Content-Type", "application/octet-stream");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(bytes);
            session.SendResponseAsync(resp);
        }

        private static void NotFound(HttpSession session)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(404);
            resp.SetHeader("Content-Type", "text/plain; charset=utf-8");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody("Not found");
            session.SendResponseAsync(resp);
        }
    }
}
