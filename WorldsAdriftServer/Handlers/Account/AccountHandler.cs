using Newtonsoft.Json.Linq;
using NetCoreServer;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Handlers.Admin;
using WorldsAdriftServer.Handlers.Social;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Portal;
using WorldsAdriftServer.Social;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Account
{
    /// <summary>
    /// The signed-in player's portal: <c>GET /account</c> and the seven posts
    /// behind it.
    ///
    /// GLUE ONLY, in the shape the rest of this server uses. Reading the forms is
    /// <see cref="PortalFormPolicy"/> and <see cref="EmblemFormPolicy"/>; deciding
    /// who may do what is <see cref="PortalPermissions"/>, which delegates to
    /// <see cref="AlliancePolicy"/> in the engine-free multiplayer project;
    /// assembling the page's data is <see cref="PortalViewBuilder"/>; the markup is
    /// <see cref="AccountPage"/>. What is left here is the part that needs a socket
    /// and a database: the cookie, the CSRF check, the roster check, the write and
    /// the redirect.
    ///
    /// THE PERMISSION IS PER CHARACTER, THE SESSION IS PER ACCOUNT, and the gap
    /// between those two is still the only interesting security question on this
    /// page - it is just wider now that there are seven posts instead of one. An
    /// account owns up to five characters; alliance membership, ranks and
    /// permissions all hang off a CHARACTER uid. So EVERY post that names a
    /// character checks it against THIS account's roster before it is used as an
    /// actor - otherwise anyone signed in could post somebody else's character uid
    /// and borrow their rank. See <see cref="OwnsCharacter"/>, which is the whole
    /// defence and is deliberately a separate, named step rather than a condition
    /// folded into the permission check.
    ///
    /// EVERY REFUSAL LOOKS THE SAME. A character that is not yours, an alliance
    /// that does not exist and a rank you may not grant all redirect with
    /// <c>denied</c>, so a signed-in player cannot use this page to learn whether
    /// somebody else's character or alliance exists.
    /// </summary>
    internal static class AccountHandler
    {
        private const string PagePath = "/account";
        private const string EmblemPath = "/account/alliance-emblem";
        private const string DetailsPath = "/account/alliance-details";
        private const string MemberPath = "/account/alliance-member";
        private const string RequestPath = "/account/alliance-request";
        private const string PasswordPath = "/account/password";
        private const string LogoutPath = "/account/logout";

        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            if (!Owns(path)) return false;

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

            if (path == PagePath || path == PagePath + "/")
            {
                if (request.Method != "GET") { Redirect(session, PagePath); return true; }
                Page(session, request, accountId.Value, token);
                return true;
            }

            // Everything else on this route CHANGES something, so nothing else on
            // it answers a GET. A GET that mutated would be a GET a link
            // prefetcher, a chat client's unfurler or a browser's history restore
            // could fire on the player's behalf.
            if (request.Method != "POST")
            {
                Redirect(session, PagePath);
                return true;
            }

            Dictionary<string, string> form = AdminHandler.ParseForm(request.Body);

            form.TryGetValue(PlayerAuthPolicy.CsrfField, out string? csrf);
            if (!PlayerAuthPolicy.VerifyCsrf(token, csrf))
            {
                // Said out loud. Refusing is correct, but a refusal that leaves
                // no trace is indistinguishable in the journal from a save that
                // never happened - which is exactly how this was first read.
                //
                // Not hypothetical: a crest builder left open across a session
                // rotation posts a CSRF derived from a session that no longer
                // exists. The player loses the crest they had just composed and
                // the only server-side evidence was the bare request line.
                //
                // The TOKEN is never printed. The fact of the mismatch, and
                // whether one was sent at all, is the whole diagnostic.
                Console.WriteLine("[info] " + path + " refused: the form's CSRF token"
                    + " does not belong to this session"
                    + (string.IsNullOrWhiteSpace(csrf) ? " (none was posted)" : " (stale form?)")
                    + ".");

                Done(session, PortalTabs.AfterPost(path), PortalNotices.Expired);
                return true;
            }

            // Where a redirect will land, decided ONCE from the route and handed
            // down, so every refusal and every success on this post comes back to
            // the tab the player was looking at.
            string tab = PortalTabs.AfterPost(path);

            try
            {
                switch (path)
                {
                    case LogoutPath: Logout(session, token); return true;
                    case PasswordPath: ChangePassword(session, accountId.Value, token, form, tab); return true;
                    case EmblemPath: SaveEmblem(session, accountId.Value, form, tab); return true;
                    case DetailsPath: SaveDetails(session, accountId.Value, form, tab); return true;
                    case MemberPath: ChangeMember(session, accountId.Value, form, tab); return true;
                    case RequestPath: AnswerRequest(session, accountId.Value, form, tab); return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] " + path + " failed: " + e);
                Done(session, PortalTabs.AfterPost(path), PortalNotices.Failed);
                return true;
            }

            Redirect(session, PagePath);
            return true;
        }

        private static bool Owns(string path) =>
            path == PagePath || path == PagePath + "/"
            || path == EmblemPath || path == DetailsPath || path == MemberPath
            || path == RequestPath || path == PasswordPath || path == LogoutPath;

        // ------------------------------------------------------------- the page

        private static void Page(
            HttpSession session, HttpRequest request, long accountId, string? token)
        {
            (string? notice, bool isError) = PortalNotices.For(PortalNotices.CodeFrom(request.Url));

            PortalView view;
            try
            {
                view = PortalViewBuilder.Build(
                    accountId, PlayerAuthPolicy.CsrfTokenForSession(token), notice, isError,
                    PortalTabs.Requested(request.Url));
            }
            catch (Exception e)
            {
                // A database that blinked must not turn the whole portal into a
                // 500. The player still learns they are signed in, and the page
                // comes back on the next load.
                Console.WriteLine("[warning] /account: could not build the portal: " + e.Message);

                view = new PortalView(
                    "traveller", "traveller", DateTimeOffset.UtcNow, null, "-", "-",
                    Array.Empty<CharacterCard>(),
                    PlayerAuthPolicy.CsrfTokenForSession(token),
                    "Your details are unavailable right now. Try again in a moment.", true);
            }

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", AccountPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(AccountPage.Render(view));
            session.SendResponseAsync(resp);
        }

        // ---------------------------------------------------------- the account

        private static void Logout(HttpSession session, string? token)
        {
            PlayerAuth.Sessions.Revoke(token);

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(302);
            resp.SetHeader("Location", "/login");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("Set-Cookie", PlayerAuthPolicy.BuildClearCookie());
            resp.SetBody(string.Empty);
            session.SendResponseAsync(resp);
        }

        /// <summary>
        /// Changes the account's password.
        ///
        /// THE CURRENT PASSWORD IS REQUIRED even though the caller already holds a
        /// live session. A cookie proves the browser was signed in at some point;
        /// it does not prove the person at the keyboard is the account's owner, and
        /// an unattended machine is the whole reason this box exists.
        ///
        /// Every other session is revoked afterwards, including the GAME client's
        /// long-lived token. That is the point of changing a password: if it was
        /// changed because somebody else had it, leaving their token alive would
        /// make the change cosmetic.
        /// </summary>
        private static void ChangePassword(
            HttpSession session, long accountId, string? token, IReadOnlyDictionary<string, string> form, string tab)
        {
            form.TryGetValue(PasswordChangePolicy.CurrentField, out string? current);
            form.TryGetValue(PasswordChangePolicy.NextField, out string? next);
            form.TryGetValue(PasswordChangePolicy.ConfirmField, out string? confirm);

            PasswordChangeFault fault = PasswordChangePolicy.Check(current, next, confirm);
            if (fault != PasswordChangeFault.None)
            {
                Done(session, tab, PortalNotices.CodeFor(fault));
                return;
            }

            AccountRecord? account = Accounts.Repository.FindById(accountId);
            if (account == null || Accounts.Repository.Verify(account.Username, current) == null)
            {
                Done(session, tab, PortalNotices.PasswordWrong);
                return;
            }

            if (!Accounts.Repository.ChangePassword(accountId, next!))
            {
                Done(session, tab, PortalNotices.Failed);
                return;
            }

            // The GAME tokens, in Postgres. The browser session set is separate
            // and in memory (see PlayerSessions), so the current tab is left
            // signed in - the player is standing right there and has just proved
            // they know the old password.
            int revoked = Accounts.Sessions.RevokeAllFor(accountId);

            Console.WriteLine("[info] /account: '" + account.Username
                + "' changed their password; " + revoked + " game session(s) revoked.");

            Done(session, tab, PortalNotices.PasswordChanged);
        }

        // --------------------------------------------------------- the alliance

        private static void SaveEmblem(
            HttpSession session, long accountId, IReadOnlyDictionary<string, string> form, string tab)
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(form);
            if (!outcome.Ok)
            {
                Done(session, tab, PortalNotices.Unreadable);
                return;
            }

            if (!Permitted(accountId, outcome.CharacterUid, outcome.AllianceId,
                    PortalAction.EditEmblem, null, out AllianceRecord? alliance))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            // The COLUMN takes a marker, not a URL - no schema change, and the
            // public host name stays in configuration. See EmblemUrlPolicy.
            Accounts.Alliances.SaveAlliance(alliance! with
            {
                EmblemUrl = EmblemUrlPolicy.Store(outcome.Artwork),
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            Console.WriteLine("[info] /account: alliance " + alliance!.Name + " ("
                + alliance.AllianceId + ") set crest " + outcome.Artwork.ToCode()
                + " via character " + outcome.CharacterUid + ".");

            Done(session, tab, PortalNotices.CrestSaved);
        }

        /// <summary>
        /// The description and the message of the day.
        ///
        /// ONE FIELD PER POST, and the permission is checked for the field that was
        /// SENT. The two carry different permissions - description is
        /// <c>edit_group</c>, the MOTD is <c>leader_chat</c>, which is the retail
        /// client's own bug reproduced on purpose - so a post carrying both would
        /// let somebody holding one of them overwrite the other field with whatever
        /// their page happened to be showing. That is exactly the trap
        /// <c>AllianceEndpoints.Update</c> documents for the game client's PATCH,
        /// where the client DOES send both; here the page sends one, and a post
        /// that carried both is answered per field anyway.
        /// </summary>
        private static void SaveDetails(
            HttpSession session, long accountId, IReadOnlyDictionary<string, string> form, string tab)
        {
            DetailsForm details = PortalFormPolicy.ReadDetails(form);
            if (!details.Ok)
            {
                Done(session, tab, PortalNotices.Unreadable);
                return;
            }

            bool wantsDescription = PortalFormPolicy.Sent(form, PortalFormPolicy.DescriptionField);
            bool wantsMotd = PortalFormPolicy.Sent(form, PortalFormPolicy.MotdField);

            PortalAction action = wantsDescription
                ? PortalAction.EditDescription
                : PortalAction.EditMessageOfTheDay;

            if (!Permitted(accountId, details.CharacterUid, details.AllianceId,
                    action, null, out AllianceRecord? alliance))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            // A post carrying BOTH fields is legal but has to clear BOTH gates.
            if (wantsDescription && wantsMotd
                && !Permitted(accountId, details.CharacterUid, details.AllianceId,
                        PortalAction.EditMessageOfTheDay, null, out _))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            string description = wantsDescription ? details.Description : alliance!.Description;
            string motd = wantsMotd ? details.MessageOfTheDay : alliance!.MessageOfTheDay;

            if (description == alliance!.Description && motd == alliance.MessageOfTheDay)
            {
                Done(session, tab, PortalNotices.NoChange);
                return;
            }

            Accounts.Alliances.SaveAlliance(alliance with
            {
                Description = description,
                MessageOfTheDay = motd,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            Done(session, tab, wantsDescription && description != alliance.Description
                ? PortalNotices.DescriptionSaved
                : PortalNotices.MotdSaved);
        }

        /// <summary>Move a member onto another rank, or throw them out.</summary>
        private static void ChangeMember(
            HttpSession session, long accountId, IReadOnlyDictionary<string, string> form, string tab)
        {
            MemberForm member = PortalFormPolicy.ReadMember(form);
            if (!member.Ok)
            {
                Done(session, tab, PortalNotices.Unreadable);
                return;
            }

            if (member.Verb == MemberVerb.Boot)
            {
                BootMember(session, accountId, member, tab);
                return;
            }

            SetRank(session, accountId, member, tab);
        }

        private static void SetRank(HttpSession session, long accountId, MemberForm form, string tab)
        {
            if (!OwnsCharacter(accountId, form.CharacterUid))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            AllianceMemberRecord? membership = Accounts.Alliances.MemberOf(form.TargetUid);
            if (membership == null || membership.AllianceId != form.AllianceId)
            {
                Done(session, tab, PortalNotices.Gone);
                return;
            }

            AllianceLedger ledger = AllianceLedgerBuilder.Build(
                Accounts.Alliances, Accounts.SocialInvites);

            AllianceVerdict verdict = PortalPermissions.MaySetRank(
                ledger,
                AllianceEndpoints.Key(form.CharacterUid),
                AllianceWire.Uid(form.AllianceId),
                AllianceEndpoints.Key(form.TargetUid),
                AllianceWire.Uid(form.RankId));

            if (verdict != AllianceVerdict.Ok)
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            // The rank has to belong to THIS alliance. The policy above resolves
            // the rank through the actor's own alliance so a foreign id already
            // answers NoSuchRank, but the row is written by id and the check is
            // cheap next to the consequence: a member pointing at a rank the
            // client cannot find THROWS in AllianceClient.TryGetRank, and that
            // throw takes out the whole Social Sheet, both tabs.
            AllianceRankRecord? rank = Accounts.Alliances.FindRank(form.RankId);
            if (rank == null || rank.AllianceId != form.AllianceId)
            {
                Done(session, tab, PortalNotices.Gone);
                return;
            }

            Accounts.Alliances.SaveMember(membership with
            {
                RankId = form.RankId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            Done(session, tab, PortalNotices.RankSet);
        }

        /// <summary>
        /// Throws a member out.
        ///
        /// The ledger decides succession and dissolve-at-last-member and the rows
        /// mirror whatever it decided - the same order
        /// <c>AllianceEndpoints.RemoveMember</c> uses, so the promotion rule exists
        /// in exactly one place. Booting cannot make the actor the last member
        /// standing (they are still in it), so the dissolve branch is unreachable
        /// from here; it is followed anyway rather than assumed away.
        /// </summary>
        private static void BootMember(HttpSession session, long accountId, MemberForm form, string tab)
        {
            if (!OwnsCharacter(accountId, form.CharacterUid))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            AllianceLedger ledger = AllianceLedgerBuilder.Build(
                Accounts.Alliances, Accounts.SocialInvites);

            string allianceId = AllianceWire.Uid(form.AllianceId);
            string targetKey = AllianceEndpoints.Key(form.TargetUid);

            AllianceVerdict verdict = PortalPermissions.May(
                ledger, PortalAction.BootMember,
                AllianceEndpoints.Key(form.CharacterUid), allianceId, targetKey);

            if (verdict != AllianceVerdict.Ok)
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            ledger.Remove(targetKey);
            Accounts.Alliances.RemoveMember(form.TargetUid);

            Console.WriteLine("[info] /account: character " + form.CharacterUid
                + " removed " + form.TargetUid + " from alliance " + form.AllianceId + ".");

            Done(session, tab, PortalNotices.MemberRemoved);
        }

        /// <summary>
        /// Accept an application, decline one, or withdraw an invitation the
        /// alliance sent.
        ///
        /// SEATING IS NOT REIMPLEMENTED HERE. Accepting goes through
        /// <see cref="AllianceEndpoints.Accept"/> - the same call the retail Social
        /// Sheet's accept endpoint makes - because it re-checks the join at accept
        /// time (an alliance can fill up or dissolve between the offer and the
        /// answer), finds the default member rank, and computes the join order. A
        /// second copy of that here would be a second answer to "who is in this
        /// alliance", and the two would diverge the first time either was fixed.
        /// </summary>
        private static void AnswerRequest(
            HttpSession session, long accountId, IReadOnlyDictionary<string, string> form, string tab)
        {
            RequestForm request = PortalFormPolicy.ReadRequest(form);
            if (!request.Ok)
            {
                Done(session, tab, PortalNotices.Unreadable);
                return;
            }

            if (!OwnsCharacter(accountId, request.CharacterUid))
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            SocialInviteRecord? invite = Accounts.SocialInvites.Find(request.InviteId);
            if (invite == null
                || invite.Status != SocialInviteStatus.New
                || invite.TargetType != SocialTargetType.Alliance
                || !string.Equals(invite.TargetId, AllianceWire.Uid(request.AllianceId), StringComparison.Ordinal))
            {
                Done(session, tab, PortalNotices.Gone);
                return;
            }

            // An APPLICATION has no inviter and an INVITE has one - the client's
            // own structural discriminator, not a convention of ours. Answering an
            // application is accept/decline; answering an invitation the alliance
            // sent is withdrawing it.
            bool isApplication = invite.InviterUid == null;
            if (isApplication == (request.Verb == RequestVerb.Rescind))
            {
                Done(session, tab, PortalNotices.Unreadable);
                return;
            }

            AllianceLedger ledger = AllianceLedgerBuilder.Build(
                Accounts.Alliances, Accounts.SocialInvites);

            AllianceVerdict verdict = PortalPermissions.May(
                ledger, PortalAction.AdmitOrRescind,
                AllianceEndpoints.Key(request.CharacterUid),
                AllianceWire.Uid(request.AllianceId));

            if (verdict != AllianceVerdict.Ok)
            {
                Done(session, tab, PortalNotices.Denied);
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (request.Verb == RequestVerb.Reject)
            {
                Accounts.SocialInvites.Resolve(invite.InviteId, SocialInviteStatus.Rejected, now);
                Done(session, tab, PortalNotices.ApplicantDeclined);
                return;
            }

            if (request.Verb == RequestVerb.Rescind)
            {
                Accounts.SocialInvites.Resolve(invite.InviteId, SocialInviteStatus.Cancelled, now);
                Done(session, tab, PortalNotices.InviteWithdrawn);
                return;
            }

            // Seat them first and only then resolve the offer: a join the policy
            // refuses must leave the application live rather than consuming it into
            // nothing. The same order SocialService.ResolveInvite uses.
            JObject seated = Endpoints().Accept(invite);
            if (!seated.Value<bool>("success"))
            {
                Done(session, tab, PortalNotices.Failed);
                return;
            }

            Accounts.SocialInvites.Resolve(invite.InviteId, SocialInviteStatus.Accepted, now);
            Done(session, tab, PortalNotices.ApplicantAdmitted);
        }

        /// <summary>
        /// The alliance endpoints, wired exactly as <see cref="SocialService"/>
        /// wires them - the same stores, the same name lookup, the same region -
        /// so a member seated from the portal is indistinguishable from one seated
        /// from the game.
        /// </summary>
        private static AllianceEndpoints Endpoints() => new AllianceEndpoints(
            Accounts.Alliances,
            Accounts.SocialInvites,
            uid => Accounts.Characters.Find(uid)?.Name,
            SocialHandler.Region);

        // ---------------------------------------------------------------- gates

        /// <summary>
        /// The two checks every alliance post makes, in the order they have to be
        /// made: is this character YOURS, and then may it do this.
        ///
        /// Both, and in that order. The permission check alone would let a
        /// signed-in player post the character uid of somebody who DOES hold the
        /// permission and act as them; the roster check alone would let them do
        /// anything with a character of their own. Neither is a substitute for the
        /// other, which is why they are two named steps and not one condition.
        /// </summary>
        private static bool Permitted(
            long accountId,
            Guid characterUid,
            Guid allianceId,
            PortalAction action,
            Guid? targetUid,
            out AllianceRecord? alliance)
        {
            alliance = null;

            // Every refusal below says WHICH gate closed - in the journal only.
            // The player keeps getting one undifferentiated "you do not have
            // permission to do that", because a signed-in player must not be
            // able to probe which characters or alliances exist by reading the
            // difference. The operator is not the attacker, and a refusal
            // nobody can explain is how the last crest defect survived.
            if (!OwnsCharacter(accountId, characterUid))
            {
                Console.WriteLine("[info] /account " + action + " refused: character "
                    + characterUid + " is not on this account.");
                return false;
            }

            alliance = Accounts.Alliances.FindAlliance(allianceId);
            if (alliance == null)
            {
                Console.WriteLine("[info] /account " + action + " refused: no alliance "
                    + allianceId + ".");
                return false;
            }

            AllianceLedger ledger = AllianceLedgerBuilder.Build(
                Accounts.Alliances, Accounts.SocialInvites);

            AllianceVerdict verdict = PortalPermissions.May(
                ledger,
                action,
                AllianceEndpoints.Key(characterUid),
                AllianceWire.Uid(allianceId),
                targetUid == null ? null : AllianceEndpoints.Key(targetUid.Value));

            if (verdict != AllianceVerdict.Ok)
            {
                Console.WriteLine("[info] /account " + action + " refused: character "
                    + characterUid + " may not do that in " + alliance.Name
                    + " (" + verdict + ").");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether this character is on this account's roster.
        ///
        /// The whole reason the portal can be trusted with a per-character
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

        // ----------------------------------------------------------------- glue

        /// <summary>
        /// POST-redirect-GET back to the portal with a notice code, ON THE TAB THE
        /// PLAYER WAS ACTING IN. Never the sentence itself - see
        /// <see cref="PortalNotices"/> for why a page must not render text a URL
        /// handed it.
        ///
        /// The tab is derived from the ROUTE that was posted to, not carried in a
        /// hidden field, so no form can forget it: a crest save that dumped the
        /// player back on the Account tab is precisely the small wrongness that
        /// makes a tabbed page feel broken.
        /// </summary>
        private static void Done(HttpSession session, string tab, string code) =>
            Redirect(session, PortalTabs.Url(PagePath, tab, code));

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
