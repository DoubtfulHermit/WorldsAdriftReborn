using NetCoreServer;

namespace WorldsAdriftServer.Handlers.PublicSite
{
    /// <summary>Finishes unclaimed requests instead of leaving their socket open.</summary>
    internal static class NotFoundHandler
    {
        internal const int StatusCode = 404;
        internal const string Body = "Not found.";

        internal static void Handle(HttpSession session, HttpRequest request)
        {
            HttpResponse response = new HttpResponse();
            response.SetBegin(StatusCode);
            response.SetHeader("Content-Type", "text/plain; charset=utf-8");
            response.SetHeader("Cache-Control", "no-store");
            response.SetHeader("X-Content-Type-Options", "nosniff");
            response.SetBody(string.Equals(request.Method, "HEAD", StringComparison.Ordinal)
                ? string.Empty
                : Body);
            session.SendResponseAsync(response);
        }
    }
}
