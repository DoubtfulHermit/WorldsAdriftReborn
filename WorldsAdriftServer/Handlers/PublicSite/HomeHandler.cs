using NetCoreServer;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.PublicSite
{
    /// <summary>The unauthenticated, read-only public landing page.</summary>
    internal static class HomeHandler
    {
        internal const string Route = "/";

        internal static bool Owns(string? path) =>
            string.Equals(path, Route, StringComparison.Ordinal);

        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            if (!Owns(request.Url))
            {
                return false;
            }

            bool headOnly = string.Equals(request.Method, "HEAD", StringComparison.Ordinal);
            if (!headOnly && !string.Equals(request.Method, "GET", StringComparison.Ordinal))
            {
                HttpResponse refused = new HttpResponse();
                refused.SetBegin(405);
                refused.SetHeader("Allow", "GET, HEAD");
                refused.SetHeader("Cache-Control", "no-store");
                refused.SetBody("Method not allowed.");
                session.SendResponseAsync(refused);
                return true;
            }

            HttpResponse response = new HttpResponse();
            response.SetBegin(200);
            response.SetHeader("Content-Type", HomePage.ContentType);
            response.SetHeader("Cache-Control", "public, max-age=300");
            response.SetHeader("X-Content-Type-Options", "nosniff");
            response.SetHeader("Referrer-Policy", "same-origin");
            response.SetBody(headOnly ? string.Empty : HomePage.Html);
            session.SendResponseAsync(response);
            return true;
        }
    }
}
