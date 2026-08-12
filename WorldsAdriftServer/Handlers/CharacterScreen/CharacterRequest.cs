using NetCoreServer;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers.CharacterScreen
{
    /// <summary>
    /// The account check every character request starts with.
    ///
    /// Shared so that all three handlers refuse identically. The important part
    /// is what it does NOT do: there is no fallback roster for a request that
    /// cannot be attributed to an account. A fallback here is how one player
    /// ends up editing another player's characters, and it would look like a
    /// feature working rather than a hole.
    /// </summary>
    internal static class CharacterRequest
    {
        /// <summary>
        /// Returns the account behind the request, or null having already sent
        /// the refusal - callers just return when they get null.
        /// </summary>
        internal static AccountRecord? Authorize(
            HttpSession session,
            HttpRequest request,
            string what)
        {
            AccountRecord? account;

            try
            {
                account = Accounts.Resolve(request);
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] could not reach the account database for the "
                    + what + ": " + e);
                Refuse(session, 503);
                return null;
            }

            if (account == null)
            {
                Console.WriteLine("[info] refused a " + what
                    + " request: its Security header carries no live session.");
                Refuse(session, 401);
                return null;
            }

            return account;
        }

        private static void Refuse(HttpSession session, int status)
        {
            // The client surfaces a non-200 here through onRestServerError, which
            // shows a message. An empty object keeps its parser from throwing on
            // the way to that message.
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetBody("{}");
            session.SendResponseAsync(resp);
        }
    }
}
