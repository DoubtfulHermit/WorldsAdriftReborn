using System.IO;
using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Download;
using WorldsAdriftServer.Handlers.Authentication;
using WorldsAdriftServer.Patch;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Download
{
    /// <summary>
    /// The login-gated download surface: the page a signed-in player sees and the
    /// WAPatch binary behind its button. Every route here demands a live
    /// <c>wa_player</c> session (see <see cref="LoginHandler"/>); an unauthenticated
    /// visitor is bounced to /login and never shown the page or the bytes.
    ///
    /// The exe is served THROUGH this native process for the same reason the patch
    /// files are (<see cref="PatchFilesHandler"/>): the Caddy in front of the public
    /// host is a container that cannot read host paths. It is handed out as raw
    /// bytes via <c>HttpResponse.SetBody(byte[])</c> - the binary-safe path, no
    /// UTF-8 round-trip - and only after
    /// <see cref="DownloadFilePathPolicy"/> confirms the resolved path is contained
    /// under the downloads dir. The filename is fixed, so there is no
    /// attacker-controlled path, but the check is applied anyway.
    /// </summary>
    internal static class DownloadHandler
    {
        private const string PageRoute = "/download";
        private const string ExeRoute = "/download/WAPatch.exe";

        /// <summary>
        /// Handles GET <c>/download</c> (the page) and GET <c>/download/WAPatch.exe</c>
        /// (the file). Returns true if it took the request so the router does not
        /// fall through.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            bool isPage = path == PageRoute || path == PageRoute + "/";
            bool isExe = path == ExeRoute;

            if (!isPage && !isExe)
            {
                return false;
            }

            if (request.Method != "GET")
            {
                return false;
            }

            // The gate. No live session cookie means back to the login page, for
            // both the page and the file - the file 302s too rather than 401ing so
            // a curious deep-link lands somewhere useful.
            long? accountId = LoginHandler.ResolveAccountId(request);
            if (accountId == null)
            {
                Redirect(session, "/login");
                return true;
            }

            if (isExe)
            {
                ServePatcher(session);
                return true;
            }

            ServePage(session, accountId.Value);
            return true;
        }

        private static void ServePage(HttpSession session, long accountId)
        {
            (string version, string build) = ReadManifestVersionBuild();
            string username = ReadUsername(accountId);

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", DownloadPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(DownloadPage.Render(username, version, build));
            session.SendResponseAsync(resp);
        }

        private static void ServePatcher(HttpSession session)
        {
            if (!DownloadFilePathPolicy.TryResolveDownloadPath(
                    DownloadConfig.DownloadDir, DownloadConfig.PatcherFileName, out string? fullPath)
                || fullPath == null)
            {
                NotFound(session);
                return;
            }

            byte[] bytes;
            try
            {
                // Only a regular file counts. A directory or special file that
                // happens to resolve there is not the patcher - 404 it, the same
                // 404 a simply-missing file gets, so "missing" and "not allowed"
                // are indistinguishable from outside.
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
            // A Windows binary. Octet-stream + the byte-array SetBody keeps it
            // byte-exact; Content-Length is set to the byte count by SetBody, the
            // same binary-safe path the patch files use.
            resp.SetHeader("Content-Type", "application/octet-stream");
            resp.SetHeader("Content-Disposition", "attachment; filename=\"WAPatch.exe\"");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(bytes);
            session.SendResponseAsync(resp);
        }

        /// <summary>
        /// The version and build from the patch manifest, each a dash when the
        /// manifest cannot be read or lacks the field. Reuses
        /// <see cref="PatchConfig"/> for the dir, as the /patch page does, so the
        /// download page shows the same figures. A missing or corrupt manifest must
        /// degrade to a rendered page, never take the gated route down.
        ///
        /// Internal rather than private because the account portal shows the same
        /// two figures in its patcher section, and two readers of one manifest is
        /// two chances to disagree about which build is current.
        /// </summary>
        internal static (string version, string build) ReadManifestVersionBuild()
        {
            const string unknown = "-";
            try
            {
                string manifestPath = PatchConfig.ManifestPath;
                if (!File.Exists(manifestPath))
                {
                    return (unknown, unknown);
                }

                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                string version = manifest["version"]?.Value<string>() ?? unknown;
                string build = manifest["build"]?.Value<string>() ?? unknown;
                return (
                    string.IsNullOrWhiteSpace(version) ? unknown : version,
                    string.IsNullOrWhiteSpace(build) ? unknown : build);
            }
            catch (Exception)
            {
                return (unknown, unknown);
            }
        }

        /// <summary>
        /// The display name to greet, or "player" if the account cannot be read.
        /// The greeting is a nicety, not a gate: a database blip drops the name, it
        /// does not lock the player out of a page their cookie already earned.
        /// </summary>
        private static string ReadUsername(long accountId)
        {
            try
            {
                AccountRecord? account = Accounts.Repository.FindById(accountId);
                if (account != null && !string.IsNullOrWhiteSpace(account.DisplayName))
                {
                    return account.DisplayName;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] /download could not read account " + accountId + ": " + e.Message);
            }

            return "player";
        }

        private static void Redirect(HttpSession session, string location)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(302);
            resp.SetHeader("Location", location);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(string.Empty);
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
