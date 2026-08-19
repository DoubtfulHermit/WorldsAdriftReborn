using NetCoreServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Handlers.ServerStatus
{
    /// <summary>
    /// The operator-written greeting the client shows on arrival.
    ///
    /// Deliberately unauthenticated: it is a welcome message. Nothing behind it
    /// is per-player, per-account or per-session - it is the same string for
    /// everyone who asks, and it exists to be read before anybody has signed in
    /// to anything.
    ///
    /// The route's job is to be BORING. The caller is a game client on a startup
    /// path, so this must answer with a message under every condition, including
    /// a database that is not there; the router hands the fallback in rather
    /// than letting this class reach for storage itself, exactly as
    /// <see cref="DeploymentStatusHandler"/> does.
    /// </summary>
    internal static class WelcomeMessageHandler
    {
        /*
         * URL: /welcomeMessage
         *
         * Response: 200 application/json, {"message":"..."} with the newlines
         * escaped as \n by the JSON encoder. no-store, because the point of
         * making this server-driven is that an edit in the panel is live on the
         * next client launch without anything being redeployed or expired.
         */
        internal static void HandleWelcomeMessageRequest(HttpSession session, HttpRequest request,
            string message)
        {
            JObject body = new JObject
            {
                ["message"] = message ?? string.Empty,
            };

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", "application/json");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(body.ToString(Formatting.None));

            session.SendResponseAsync(resp);
        }
    }
}
