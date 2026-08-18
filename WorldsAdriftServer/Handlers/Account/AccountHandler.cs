using NetCoreServer;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Handlers.Admin;
using WorldsAdriftServer.Handlers.Authentication;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Social;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Account
{
    /// <summary>
    /// The signed-in player's account area: <c>GET /account</c> and the one thing
    /// it can change, <c>POST /account/alliance-emblem</c>.
    ///
    /// GLUE ONLY, in the shape the rest of this server uses. Reading the form is
    /// <see cref="EmblemFormPolicy"/>; deciding who may re-crest an alliance is
    /// <see cref="AlliancePolicy.MayEditEmblem"/> in the engine-free multiplayer
    /// project; what gets stored and what goes on the wire is
    /// <see cref="EmblemUrlPolicy"/>; the markup is <see cref="AccountPage"/>.
    /// What is left here is the part that needs a socket and a database: the
    /// cookie, the roster query, the ledger, the save and the redirect.
    ///
    /// THE PERMISSION IS PER CHARACTER, THE SESSION IS PER ACCOUNT, and the gap
    /// between those two is the only interesting security question on this page.
    /// An account owns up to five characters; alliance membership, ranks and
    /// permissions all hang off a CHARACTER uid. So the posted character is
    /// checked against THIS account's roster before it is used as an actor -
    /// otherwise anyone signed in could post somebody else's character uid and
    /// borrow their rank. See <see cref="OwnsCharacter"/>, which is the whole
    /// defence and is deliberately a separate, named step rather than a condition
    /// folded into the permission check.
    /// </summary>
    internal static class AccountHandler
    {
        private const string PagePath = "/account";
        private const string EmblemPath = "/account/alliance-emblem";

        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            if (path != PagePath && path != PagePath + "/" && path != EmblemPath)
            {
                return false;
            }

            string? token = PlayerAuthPolicy.TokenFromCookieHeader(HeaderValue(request, "Cookie"));
            long? accountId = PlayerAuth.Sessions.Resolve(token, DateTimeOffset.UtcNow);

            if (accountId == null)
            {
                // Bounced to the sign-in page rather than refused, because the
                // only way to reach this page is a link and the only fix is to
                // sign in. Same treatment the download gate gives.
                Redirect(session, "/login");
                return true;
            }

            if (path == EmblemPath)
            {
                if (request.Method != "POST")
                {
                    Redirect(session, PagePath);
                    return true;
                }

                SaveEmblem(session, request, accountId.Value, token);
                return true;
            }

            if (request.Method != "GET")
            {
                Redirect(session, PagePath);
                return true;
            }

            Page(session, request, accountId.Value, token);
            return true;
        }

        // ------------------------------------------------------------- the page

        private static void Page(
            HttpSession session, HttpRequest request, long accountId, string? token)
        {
            string username = ReadUsername(accountId);

            (string? notice, bool isError) = NoticeFor(request.Url);

            IReadOnlyList<AccountPage.Target> targets;
            try
            {
                targets = TargetsFor(accountId);
            }
            catch (Exception e)
            {
                // A database that blinked must not turn the whole account page
                // into a 500 - the player can still see they are signed in, and
                // the builder comes back on the next load.
                Console.WriteLine("[warning] /account: could not read alliances: " + e.Message);
                targets = Array.Empty<AccountPage.Target>();
                notice ??= "Alliance details are unavailable right now. Try again in a moment.";
                isError = true;
            }

            string html = AccountPage.Render(
                username,
                PlayerAuthPolicy.CsrfTokenForSession(token),
                targets,
                notice,
                isError);

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", AccountPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(html);
            session.SendResponseAsync(resp);
        }

        /// <summary>
        /// Every alliance this account may re-crest, one row per character that
        /// may do it.
        /// </summary>
        private static IReadOnlyList<AccountPage.Target> TargetsFor(long accountId)
        {
            IReadOnlyList<CharacterRecord> roster = Accounts.Characters.ListForAccount(accountId);
            List<AccountPage.Target> targets = new List<AccountPage.Target>();

            if (roster.Count == 0) return targets;

            AllianceLedger ledger = AllianceLedgerBuilder.Build(
                Accounts.Alliances, Accounts.SocialInvites);

            foreach (CharacterRecord character in roster)
            {
                if (character.IsEmptySlot) continue;

                AllianceMemberRecord? membership = Accounts.Alliances.MemberOf(character.CharacterUid);
                if (membership == null) continue;

                AllianceRecord? alliance = Accounts.Alliances.FindAlliance(membership.AllianceId);
                if (alliance == null) continue;

                string allianceId = AllianceWire.Uid(alliance.AllianceId);
                string actor = AllianceEndpoints.Key(character.CharacterUid);

                if (AlliancePolicy.MayEditEmblem(ledger, actor, allianceId) != AllianceVerdict.Ok)
                {
                    continue;
                }

                bool built = EmblemUrlPolicy.TryReadStored(alliance.EmblemUrl, out EmblemSpec spec);
                if (!built) spec = EmblemSpec.DefaultFor(alliance.AllianceId);

                // A non-empty column that is NOT one of our markers is an
                // operator's hand-set URL. Surfaced rather than hidden, so a
                // player whose crest does not change when they save one learns
                // why instead of filing it as a bug.
                string? external =
                    !built && !string.IsNullOrWhiteSpace(alliance.EmblemUrl) ? alliance.EmblemUrl : null;

                targets.Add(new AccountPage.Target(
                    alliance.AllianceId,
                    alliance.Name,
                    character.CharacterUid,
                    character.Name,
                    spec,
                    built,
                    external));
            }

            return targets;
        }

        // ------------------------------------------------------------- the save

        private static void SaveEmblem(
            HttpSession session, HttpRequest request, long accountId, string? token)
        {
            Dictionary<string, string> form = AdminHandler.ParseForm(request.Body);

            form.TryGetValue(PlayerAuthPolicy.CsrfField, out string? csrf);
            if (!PlayerAuthPolicy.VerifyCsrf(token, csrf))
            {
                Redirect(session, PagePath + "?e=csrf");
                return;
            }

            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(form);
            if (!outcome.Ok)
            {
                Redirect(session, PagePath + "?e=form");
                return;
            }

            try
            {
                if (!OwnsCharacter(accountId, outcome.CharacterUid))
                {
                    // Same refusal as "you are not permitted": a signed-in player
                    // posting a character uid that is not theirs learns nothing
                    // about whether that character exists.
                    Redirect(session, PagePath + "?e=denied");
                    return;
                }

                AllianceRecord? alliance = Accounts.Alliances.FindAlliance(outcome.AllianceId);
                if (alliance == null)
                {
                    Redirect(session, PagePath + "?e=denied");
                    return;
                }

                AllianceLedger ledger = AllianceLedgerBuilder.Build(
                    Accounts.Alliances, Accounts.SocialInvites);

                AllianceVerdict verdict = AlliancePolicy.MayEditEmblem(
                    ledger,
                    AllianceEndpoints.Key(outcome.CharacterUid),
                    AllianceWire.Uid(alliance.AllianceId));

                if (verdict != AllianceVerdict.Ok)
                {
                    Redirect(session, PagePath + "?e=denied");
                    return;
                }

                // The COLUMN takes a marker, not a URL - no schema change, and the
                // public host name stays in configuration. See EmblemUrlPolicy.
                Accounts.Alliances.SaveAlliance(alliance with
                {
                    EmblemUrl = EmblemUrlPolicy.Store(outcome.Spec),
                    UpdatedAt = DateTimeOffset.UtcNow,
                });

                Console.WriteLine("[info] /account: alliance " + alliance.Name + " ("
                    + alliance.AllianceId + ") set crest " + outcome.Spec.ToCode()
                    + " via character " + outcome.CharacterUid + ".");

                Redirect(session, PagePath + "?ok=1");
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] /account/alliance-emblem failed: " + e);
                Redirect(session, PagePath + "?e=server");
            }
        }

        /// <summary>
        /// Whether this character is on this account's roster.
        ///
        /// The whole reason the account page can be trusted with a per-character
        /// permission. Compared against the roster the database returns rather
        /// than against anything the form said about itself.
        /// </summary>
        private static bool OwnsCharacter(long accountId, Guid characterUid)
        {
            foreach (CharacterRecord character in Accounts.Characters.ListForAccount(accountId))
            {
                if (character.CharacterUid == characterUid && !character.IsEmptySlot) return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- glue

        private static (string?, bool) NoticeFor(string url)
        {
            int q = url.IndexOf('?');
            string query = q >= 0 ? url.Substring(q + 1) : string.Empty;

            if (query.Contains("ok=1", StringComparison.Ordinal))
            {
                return ("Crest saved. It appears in game the next time the alliance panel loads.", false);
            }

            if (query.Contains("e=csrf", StringComparison.Ordinal))
            {
                return ("That form had expired. It has been reloaded - try again.", true);
            }

            if (query.Contains("e=form", StringComparison.Ordinal))
            {
                return ("Those crest choices were not readable.", true);
            }

            if (query.Contains("e=denied", StringComparison.Ordinal))
            {
                return ("That character may not change that alliance's crest.", true);
            }

            if (query.Contains("e=server", StringComparison.Ordinal))
            {
                return ("The crest could not be saved. Try again shortly.", true);
            }

            return (null, false);
        }

        private static string ReadUsername(long accountId)
        {
            try
            {
                AccountRecord? account = Accounts.Repository.FindById(accountId);
                return account?.Username ?? "traveller";
            }
            catch (Exception)
            {
                return "traveller";
            }
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

        private static string? HeaderValue(HttpRequest request, string name)
        {
            for (int i = 0; i < request.Headers; i++)
            {
                (string header, string value) = request.Header(i);
                if (string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return null;
        }
    }
}
