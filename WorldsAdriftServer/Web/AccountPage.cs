using System.Globalization;
using System.Text;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The signed-in player's portal: the page a browser sign-in now lands on.
    ///
    /// WHAT IT IS FOR. Everything a player has that is not IN the game and not
    /// operator business: who they are signed in as, the patcher, and - per
    /// character - what that character knows, where it was last seen, what it is
    /// carrying, its crew, and its alliance with whatever of that alliance this
    /// character is permitted to change. Before this it was one form (the crest
    /// builder) reached from a link on the download page, and the download page
    /// was where sign-in dropped you; now sign-in drops you here and the patcher
    /// is a section rather than a destination. <c>/download</c> still answers on
    /// its own for anyone holding the old link.
    ///
    /// IT RENDERS A VIEW AND ASKS NOTHING. Every value arrives in
    /// <see cref="PortalView"/>, including every "may I" boolean, decided once by
    /// <see cref="PortalPermissions"/> against the ledger the handler will
    /// re-check the post against. A permission question in this file would be a
    /// second opinion, and a page and a handler that disagree is either a control
    /// that always fails or a control that should not have been there.
    ///
    /// IT IS TABBED, AND THE TABS ARE URLS. Only the panel named by
    /// <see cref="PortalView.Tab"/> is rendered - see <see cref="PortalTabs"/> for
    /// why that is navigation rather than show-and-hide. The consequence for this
    /// file is that <see cref="Render"/> is a switch and nothing else: each tab
    /// appends its own sections, and no section has to know whether it is visible.
    ///
    /// THE EMBLEM EDITOR LIVES IN <see cref="AccountEmblemEditor"/>. It is a
    /// three-column instrument with an object catalogue, a canvas and a layers
    /// panel, and it is nobody's business but its own; folding it in here would
    /// have made this the file everything on the portal is edited in.
    ///
    /// Every value stamped in is HTML-encoded through
    /// <see cref="AdminPage.HtmlEncode"/>, the escaper the rest of the console
    /// uses, so an alliance name, a character name or an item id cannot break out
    /// of the markup.
    /// </summary>
    internal static class AccountPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>The link the patcher button points at - the same route the
        /// standalone download page uses.</summary>
        private const string PatcherHref = "/download/WAPatch.exe";

        /// <summary>The portal's own route, so every in-page tab link is built
        /// from the same string the handler answers on.</summary>
        internal const string PagePath = "/account";

        internal static string Render(PortalView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            IReadOnlyList<PortalTab> tabs = PortalTabs.For(view);
            string active = PortalTabs.Resolve(view.Tab, tabs);

            StringBuilder body = new StringBuilder();

            if (view.Notice != null)
            {
                body.Append("  <p class=\"notice ")
                    .Append(view.NoticeIsError ? "bad" : "good")
                    .Append("\">")
                    .Append(AdminPage.HtmlEncode(view.Notice))
                    .Append("</p>\n");
            }

            switch (active)
            {
                case PortalTabs.Account:
                    AppendAccount(body, view);
                    if (view.Characters.Count == 0) AppendNoCharacters(body);
                    break;

                case PortalTabs.Patcher:
                    AppendDownload(body, view);
                    break;

                case PortalTabs.Alliance:
                    AppendAllianceTab(body, view);
                    break;

                case PortalTabs.Emblem:
                    AccountEmblemEditor.Append(body, view);
                    break;

                default:
                    AppendCharacterTab(body, view, active);
                    break;
            }

            return Shell(view, tabs, active, body.ToString());
        }

        private static void AppendNoCharacters(StringBuilder body) =>
            body.Append(@"  <section class=""card empty"" id=""characters"">
    <h2>Characters</h2>
    <p>You have not made a character yet. Run the patcher, start the game and
    create one - it will appear here with everything it learns.</p>
  </section>
");

        /// <summary>
        /// One character's own tab: its sheet and its crew.
        ///
        /// NOT its alliance. An alliance belongs to several characters at once and
        /// carries its own roster, applications and crest, so it has a tab of its
        /// own rather than being repeated inside each member's - which is what the
        /// single-page portal did.
        /// </summary>
        private static void AppendCharacterTab(StringBuilder body, PortalView view, string tab)
        {
            foreach (CharacterCard card in view.Characters)
            {
                if (!string.Equals(PortalTabs.CharacterId(card.Sheet.Uid), tab, StringComparison.Ordinal))
                {
                    continue;
                }

                AppendCharacter(body, view.Csrf, card);
                return;
            }

            AppendNoCharacters(body);
        }

        /// <summary>
        /// Every alliance any of this account's characters belongs to.
        ///
        /// PER CHARACTER, because that is what an alliance membership is: two
        /// characters on one account can be in two alliances, and each acts with
        /// its own rank. The card names which character it is acting as, for the
        /// same reason every form on it posts that uid.
        /// </summary>
        private static void AppendAllianceTab(StringBuilder body, PortalView view)
        {
            foreach (CharacterCard card in view.Characters)
            {
                if (card.Alliance == null) continue;

                body.Append("  <section class=\"card\" id=\"a")
                    .Append(Safe(card.Alliance.AllianceId)).Append("\">\n");
                body.Append("    <h2>").Append(AdminPage.HtmlEncode(card.Alliance.Name)).Append("</h2>\n");
                body.Append("    <p class=\"as\">acting as <b>")
                    .Append(AdminPage.HtmlEncode(card.Sheet.Name)).Append("</b></p>\n");

                AppendAlliance(body, view.Csrf, card.Alliance);

                body.Append("  </section>\n");
            }
        }

        // --------------------------------------------------------- the account

        private static void AppendAccount(StringBuilder page, PortalView view)
        {
            page.Append("  <section class=\"card\" id=\"account\">\n");
            page.Append("    <h2>Account</h2>\n");
            page.Append("    <dl class=\"facts\">\n");
            Fact(page, "Username", view.Username);
            Fact(page, "Display name", view.DisplayName);
            Fact(page, "Joined", Day(view.CreatedAt));
            Fact(page, "Last sign-in",
                view.LastLoginAt == null ? "this one" : Moment(view.LastLoginAt.Value));
            Fact(page, "Characters",
                view.Characters.Count.ToString(CultureInfo.InvariantCulture));
            page.Append("    </dl>\n");

            page.Append("    <details>\n      <summary>Change password</summary>\n");
            page.Append("      <form method=\"post\" action=\"/account/password\">\n");
            Csrf(page, view.Csrf);
            Password(page, "Current password", PasswordChangePolicy.CurrentField, "current-password");
            Password(page, "New password", PasswordChangePolicy.NextField, "new-password");
            Password(page, "New password again", PasswordChangePolicy.ConfirmField, "new-password");
            page.Append("        <button class=\"plank\" type=\"submit\">Change password</button>\n");
            page.Append("      </form>\n    </details>\n");

            // A POST, not a link. Signing out changes state, and a GET that does
            // is a GET a link prefetcher can fire on the player's behalf.
            page.Append("    <form method=\"post\" action=\"/account/logout\">\n");
            Csrf(page, view.Csrf);
            page.Append("      <button class=\"quiet\" type=\"submit\">Sign out</button>\n");
            page.Append("    </form>\n");
            page.Append("  </section>\n");
        }

        // -------------------------------------------------------- the patcher

        private static void AppendDownload(StringBuilder page, PortalView view)
        {
            page.Append("  <section class=\"card\" id=\"download\">\n");
            page.Append("    <h2>The patcher</h2>\n");
            page.Append("    <p class=\"as\">Version <b>")
                .Append(AdminPage.HtmlEncode(view.PatchVersion))
                .Append("</b> &middot; build <b>")
                .Append(AdminPage.HtmlEncode(view.PatchBuild))
                .Append("</b></p>\n");
            page.Append("    <a class=\"plank big\" href=\"").Append(PatcherHref)
                .Append("\">Download WAPatch.exe</a>\n");
            page.Append(@"    <table class=""rows"" style=""margin-top:1.2rem"">
      <tr><td class=""num"">1</td><td>Download and run <b>WAPatch.exe</b>.</td></tr>
      <tr><td class=""num"">2</td><td>Point it at your <b>Worlds Adrift</b> install folder.</td></tr>
      <tr><td class=""num"">3</td><td>Click <b>Patch</b>, then launch the game.</td></tr>
    </table>
");
            page.Append("  </section>\n");
        }

        // ------------------------------------------------------- one character

        private static void AppendCharacter(StringBuilder page, string csrf, CharacterCard card)
        {
            CharacterSheet sheet = card.Sheet;

            page.Append("  <section class=\"card\" id=\"c")
                .Append(Safe(sheet.Uid)).Append("\">\n");
            page.Append("    <h2>").Append(AdminPage.HtmlEncode(sheet.Name)).Append("</h2>\n");
            page.Append("    <p class=\"as\">slot ")
                .Append((sheet.SlotIndex + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" &middot; created ").Append(Day(sheet.CreatedAt)).Append("</p>\n");

            AppendKnowledge(page, sheet);
            AppendWhereabouts(page, sheet);
            AppendInventory(page, sheet);

            if (card.Crew != null) AppendCrew(page, card.Crew);

            if (card.Alliance != null)
            {
                page.Append("    <h3>Alliance</h3>\n");
                page.Append("    <p class=\"as\"><b>")
                    .Append(AdminPage.HtmlEncode(card.Alliance.Name))
                    .Append("</b> &middot; you are <b>")
                    .Append(AdminPage.HtmlEncode(card.Alliance.YourRank)).Append("</b> &middot; <a href=\"")
                    .Append(PortalTabs.Url(PagePath, PortalTabs.Alliance))
                    .Append("\">open the alliance</a></p>\n");
            }
            else
            {
                page.Append("    <h3>Alliance</h3>\n");
                page.Append("    <p>This character is in no alliance. Join or found one from the "
                    + "Social panel in game and it appears here.</p>\n");
            }

            page.Append("  </section>\n");
        }

        private static void AppendKnowledge(StringBuilder page, CharacterSheet sheet)
        {
            page.Append("    <h3>Knowledge</h3>\n");

            if (sheet.Knowledge == null)
            {
                page.Append("    <p>Nothing saved yet. Knowledge appears here after this "
                    + "character has been in the world.</p>\n");
                return;
            }

            SheetKnowledge k = sheet.Knowledge;

            page.Append("    <ul class=\"stats\">\n");
            Stat(page, k.Knowledge, "unspent");
            Stat(page, k.LifetimeKnowledge, "lifetime");
            Stat(page, k.Spent, "spent");
            Stat(page, k.Schematics.Count, "schematics");
            Stat(page, k.Scans, "scanned");
            Stat(page, k.NodeUsesTotal, "node uses");
            page.Append("    </ul>\n");

            if (k.Schematics.Count > 0)
            {
                page.Append("    <details>\n      <summary>")
                    .Append(k.Schematics.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" learned schematics</summary>\n      <ul class=\"chips mono\">\n");
                foreach (string schematic in k.Schematics)
                {
                    page.Append("        <li>").Append(AdminPage.HtmlEncode(schematic)).Append("</li>\n");
                }
                page.Append("      </ul>\n    </details>\n");
            }

            if (k.NodeUses.Count > 0)
            {
                page.Append("    <details>\n      <summary>Most-used knowledge nodes</summary>\n");
                page.Append("      <div class=\"scroll\"><table class=\"rows\">\n");
                page.Append("        <tr><th>Node</th><th>Uses</th></tr>\n");
                foreach (SheetTally use in k.NodeUses)
                {
                    page.Append("        <tr><td>").Append(AdminPage.HtmlEncode(use.Name))
                        .Append("</td><td class=\"num\">")
                        .Append(use.Count.ToString(CultureInfo.InvariantCulture))
                        .Append("</td></tr>\n");
                }
                page.Append("      </table></div>\n    </details>\n");
            }
        }

        private static void AppendWhereabouts(StringBuilder page, CharacterSheet sheet)
        {
            page.Append("    <h3>Last seen</h3>\n");

            if (sheet.Position == null)
            {
                page.Append("    <p>This character has not been placed in the world yet, "
                    + "so it will start at the spawn point.</p>\n");
                return;
            }

            SheetPosition p = sheet.Position;

            page.Append("    <dl class=\"facts\">\n");
            Fact(page, "Island", p.OnKnownTerrain ? p.Place : "open sky");
            Fact(page, "Position", Metres(p.MetresX) + ", " + Metres(p.MetresY) + ", " + Metres(p.MetresZ));
            Fact(page, "Saved", Moment(p.SeenAt));
            page.Append("    </dl>\n");
        }

        private static void AppendInventory(StringBuilder page, CharacterSheet sheet)
        {
            page.Append("    <h3>Carrying</h3>\n");

            if (sheet.Inventory == null)
            {
                page.Append("    <p>No inventory has been saved for this character yet.</p>\n");
                return;
            }

            SheetInventory inv = sheet.Inventory;

            page.Append("    <ul class=\"stats\">\n");
            Stat(page, inv.Stacks, "stacks");
            Stat(page, inv.Units, "items");
            Stat(page, inv.Worn, "worn");
            Stat(page, inv.Stashed, "stashed");
            page.Append("      <li><span class=\"n\">")
                .Append(inv.Width.ToString(CultureInfo.InvariantCulture)).Append("&times;")
                .Append(inv.Height.ToString(CultureInfo.InvariantCulture))
                .Append("</span><span class=\"k\">grid</span></li>\n");
            page.Append("    </ul>\n");

            if (inv.Top.Count == 0) return;

            page.Append("    <details>\n      <summary>What there is most of</summary>\n");
            page.Append("      <div class=\"scroll\"><table class=\"rows\">\n");
            page.Append("        <tr><th>Item</th><th>Held</th></tr>\n");
            foreach (SheetTally tally in inv.Top)
            {
                page.Append("        <tr><td>").Append(AdminPage.HtmlEncode(tally.Name))
                    .Append("</td><td class=\"num\">")
                    .Append(tally.Count.ToString(CultureInfo.InvariantCulture))
                    .Append("</td></tr>\n");
            }
            page.Append("      </table></div>\n    </details>\n");
        }

        // ------------------------------------------------------------ the crew

        private static void AppendCrew(StringBuilder page, CrewCard crew)
        {
            page.Append("    <h3>Crew</h3>\n");
            page.Append("    <p class=\"as\"><b>").Append(AdminPage.HtmlEncode(crew.Name))
                .Append("</b> &middot; ")
                .Append(crew.Members.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" of ").Append(crew.Slots.ToString(CultureInfo.InvariantCulture))
                .Append(" seats</p>\n");

            page.Append("    <div class=\"scroll\"><table class=\"rows\">\n");
            page.Append("      <tr><th>Member</th><th>Seat</th></tr>\n");
            foreach (CrewMemberRow member in crew.Members)
            {
                page.Append("      <tr><td")
                    .Append(member.IsYou ? " class=\"you\"" : string.Empty)
                    .Append('>').Append(AdminPage.HtmlEncode(member.Name));
                if (member.IsLeader) page.Append(" &mdash; captain");
                if (member.IsYou) page.Append(" (you)");
                page.Append("</td><td class=\"num\">")
                    .Append(member.Slot == null
                        ? "&mdash;"
                        : (member.Slot.Value + 1).ToString(CultureInfo.InvariantCulture))
                    .Append("</td></tr>\n");
            }
            page.Append("    </table></div>\n");

            page.Append("    <p class=\"locked\">Crews are run from the Social panel in game. "
                + "This portal shows yours but does not change it.</p>\n");
        }

        // -------------------------------------------------------- the alliance

        private static void AppendAlliance(StringBuilder page, string csrf, AllianceCard alliance)
        {
            page.Append("    <p class=\"as\">you are <b>")
                .Append(AdminPage.HtmlEncode(alliance.YourRank)).Append("</b>");
            if (alliance.YouAreTheFounder) page.Append(" (founder)");
            page.Append("</p>\n");

            if (alliance.YourPermissions.Count > 0)
            {
                page.Append("    <ul class=\"chips mono\">\n");
                foreach (string permission in alliance.YourPermissions)
                {
                    page.Append("      <li>").Append(AdminPage.HtmlEncode(permission)).Append("</li>\n");
                }
                page.Append("    </ul>\n");
            }

            AppendAllianceDetails(page, csrf, alliance);
            AppendAllianceRoster(page, csrf, alliance);
            AppendAllianceRequests(page, csrf, alliance);

            // The CREST, and only a way in to it. The editor is a three-column
            // instrument with a fifty-object catalogue; it has a tab.
            page.Append("    <h3>Emblem</h3>\n");
            page.Append("    <p class=\"crestline\"><img class=\"mark-sm\" alt=\"\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                .Append("\"><a href=\"").Append(PortalTabs.Url(PagePath, PortalTabs.Emblem))
                .Append("\">")
                .Append(alliance.Rights.EditEmblem ? "Open the emblem editor" : "Look at the emblem")
                .Append("</a></p>\n");

            if (alliance.Rights.Nothing)
            {
                page.Append("    <p class=\"locked\">Your rank carries no alliance permissions, "
                    + "so everything above is read-only. Ask whoever leads it for a rank that "
                    + "grants <code>edit_group</code>, <code>leader_chat</code> or "
                    + "<code>edit_members</code>.</p>\n");
            }
        }

        /// <summary>
        /// The description and the MOTD.
        ///
        /// TWO FORMS, NOT ONE, and that is the whole design of this block. The two
        /// fields carry DIFFERENT permissions - the description is
        /// <c>edit_group</c>, the MOTD is <c>leader_chat</c> - so a single form
        /// posting both would make somebody who holds one of them overwrite the
        /// other field with whatever their page happened to be showing. Separate
        /// forms post separate fields, and the handler applies only the field it
        /// was sent.
        /// </summary>
        private static void AppendAllianceDetails(StringBuilder page, string csrf, AllianceCard alliance)
        {
            if (alliance.Rights.EditDescription)
            {
                page.Append("    <form method=\"post\" action=\"/account/alliance-details\">\n");
                Actors(page, csrf, alliance);
                page.Append("      <label class=\"row\"><span>Description</span>"
                    + "<textarea name=\"").Append(PortalFormPolicy.DescriptionField)
                    .Append("\" maxlength=\"")
                    .Append(PortalFormPolicy.MaxTextLength.ToString(CultureInfo.InvariantCulture))
                    .Append("\">").Append(AdminPage.HtmlEncode(alliance.Description))
                    .Append("</textarea></label>\n");
                page.Append("      <button class=\"quiet\" type=\"submit\">Save description</button>\n");
                page.Append("    </form>\n");
            }
            else
            {
                ReadOnlyText(page, "Description", alliance.Description);
            }

            if (alliance.Rights.EditMessageOfTheDay)
            {
                page.Append("    <form method=\"post\" action=\"/account/alliance-details\">\n");
                Actors(page, csrf, alliance);
                page.Append("      <label class=\"row\"><span>Message of the day</span>"
                    + "<textarea name=\"").Append(PortalFormPolicy.MotdField)
                    .Append("\" maxlength=\"")
                    .Append(PortalFormPolicy.MaxTextLength.ToString(CultureInfo.InvariantCulture))
                    .Append("\">").Append(AdminPage.HtmlEncode(alliance.MessageOfTheDay))
                    .Append("</textarea></label>\n");
                page.Append("      <button class=\"quiet\" type=\"submit\">Save message</button>\n");
                page.Append("    </form>\n");
            }
            else
            {
                ReadOnlyText(page, "Message of the day", alliance.MessageOfTheDay);
            }
        }

        /// <summary>
        /// One of the two alliance texts, shown but not editable.
        ///
        /// Styled as the label the FORM would have carried rather than as a
        /// key/value fact, so a member who may edit one field and not the other
        /// sees one column of the same thing rather than a textarea beside a
        /// definition list.
        /// </summary>
        private static void ReadOnlyText(StringBuilder page, string label, string value) =>
            page.Append("    <div class=\"row readonly\"><span>")
                .Append(AdminPage.HtmlEncode(label))
                .Append("</span><p>")
                .Append(value.Length == 0 ? "&mdash;" : AdminPage.HtmlEncode(value))
                .Append("</p></div>\n");

        private static void AppendAllianceRoster(StringBuilder page, string csrf, AllianceCard alliance)
        {
            page.Append("    <h3>Members (")
                .Append(alliance.Members.Count.ToString(CultureInfo.InvariantCulture))
                .Append(")</h3>\n");
            page.Append("    <div class=\"scroll\"><table class=\"rows\">\n");
            page.Append("      <tr><th>Member</th><th>Rank</th><th></th></tr>\n");

            foreach (AllianceMemberRow member in alliance.Members)
            {
                page.Append("      <tr><td")
                    .Append(member.IsYou ? " class=\"you\"" : string.Empty)
                    .Append('>').Append(AdminPage.HtmlEncode(member.Name));
                if (member.IsFounder) page.Append(" &mdash; founder");
                if (member.IsYou) page.Append(" (you)");
                page.Append("</td><td>");

                if (member.MaySetRank && Offerable(alliance, member.RankId))
                {
                    AppendRankPicker(page, csrf, alliance, member);
                }
                else
                {
                    page.Append(AdminPage.HtmlEncode(member.RankName));
                }

                page.Append("</td><td class=\"act\">");

                if (member.MayBoot)
                {
                    page.Append("<form method=\"post\" action=\"/account/alliance-member\" "
                        + "class=\"inline\" data-confirm=\"Remove ")
                        .Append(AdminPage.HtmlEncode(member.Name))
                        .Append(" from ").Append(AdminPage.HtmlEncode(alliance.Name))
                        .Append("?\">");
                    Actors(page, csrf, alliance);
                    Hidden(page, PortalFormPolicy.ActionField, "boot");
                    Hidden(page, PortalFormPolicy.TargetField, member.CharacterUid.ToString("D", CultureInfo.InvariantCulture));
                    page.Append("<button class=\"quiet danger\" type=\"submit\">Remove</button></form>");
                }

                page.Append("</td></tr>\n");
            }

            page.Append("    </table></div>\n");
        }

        /// <summary>
        /// Whether the picker can even show this member's CURRENT rank.
        ///
        /// A <c>&lt;select&gt;</c> with no matching option does not render blank -
        /// it renders the FIRST option, which would have drawn the founder as a
        /// Deckhand on a page that is supposed to tell a player the truth about
        /// their alliance. The founder's rank is deliberately not offered as a
        /// destination (see <see cref="AppendRankPicker"/>), so on their row there
        /// is nothing the control could honestly display, and the name is shown
        /// instead. That the founder also cannot usefully be moved is a second
        /// reason and not the one this guard is about: any member holding an
        /// unofferable rank gets the same treatment.
        /// </summary>
        private static bool Offerable(AllianceCard alliance, Guid rankId)
        {
            foreach (AllianceRankRow rank in alliance.Ranks)
            {
                if (rank.IsDefaultLeader) continue;
                if (rank.RankId == rankId) return true;
            }

            return false;
        }

        private static void AppendRankPicker(
            StringBuilder page, string csrf, AllianceCard alliance, AllianceMemberRow member)
        {
            page.Append("<form method=\"post\" action=\"/account/alliance-member\" class=\"inline\">");
            Actors(page, csrf, alliance);
            Hidden(page, PortalFormPolicy.ActionField, "rank");
            Hidden(page, PortalFormPolicy.TargetField, member.CharacterUid.ToString("D", CultureInfo.InvariantCulture));
            page.Append("<select class=\"rank\" name=\"").Append(PortalFormPolicy.RankField).Append("\">");

            foreach (AllianceRankRow rank in alliance.Ranks)
            {
                // The founder's rank is never a destination: leadership is two
                // facts at once (the alliance's leader pointer AND the rank), and
                // handing out the rank alone leaves the alliance disagreeing with
                // itself about who leads it. AlliancePolicy.MaySetRank refuses it;
                // offering it here would be offering a choice that always fails.
                if (rank.IsDefaultLeader) continue;

                page.Append("<option value=\"")
                    .Append(rank.RankId.ToString("D", CultureInfo.InvariantCulture)).Append('"');
                if (rank.RankId == member.RankId) page.Append(" selected");
                page.Append('>').Append(AdminPage.HtmlEncode(rank.Name)).Append("</option>");
            }

            page.Append("</select>");
            // Shown only with script off - account.js hides it and submits on
            // change instead. Present in the markup so the control works either way.
            page.Append("<button class=\"quiet apply\" type=\"submit\">Set</button>");
            page.Append("</form>");
        }

        private static void AppendAllianceRequests(StringBuilder page, string csrf, AllianceCard alliance)
        {
            if (alliance.Applications.Count == 0 && alliance.Invitations.Count == 0) return;

            if (alliance.Applications.Count > 0)
            {
                page.Append("    <h3>Applications (")
                    .Append(alliance.Applications.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(")</h3>\n");
                AppendRequestTable(page, csrf, alliance, alliance.Applications, true);
            }

            if (alliance.Invitations.Count > 0)
            {
                page.Append("    <h3>Invitations sent (")
                    .Append(alliance.Invitations.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(")</h3>\n");
                AppendRequestTable(page, csrf, alliance, alliance.Invitations, false);
            }
        }

        private static void AppendRequestTable(
            StringBuilder page, string csrf, AllianceCard alliance,
            IReadOnlyList<RequestRow> rows, bool incoming)
        {
            page.Append("    <div class=\"scroll\"><table class=\"rows\">\n");
            page.Append("      <tr><th>Player</th><th>Message</th><th></th></tr>\n");

            foreach (RequestRow row in rows)
            {
                page.Append("      <tr><td>").Append(AdminPage.HtmlEncode(row.CharacterName))
                    .Append("</td><td>")
                    .Append(row.Message.Length == 0 ? "&mdash;" : AdminPage.HtmlEncode(row.Message))
                    .Append("</td><td class=\"act\">");

                if (alliance.Rights.ManageMembers)
                {
                    if (incoming)
                    {
                        RequestButton(page, csrf, alliance, row, "accept", "Accept", false, null);
                        RequestButton(page, csrf, alliance, row, "reject", "Decline", true,
                            "Turn down " + row.CharacterName + "?");
                    }
                    else
                    {
                        RequestButton(page, csrf, alliance, row, "rescind", "Withdraw", true, null);
                    }
                }

                page.Append("</td></tr>\n");
            }

            page.Append("    </table></div>\n");
        }

        private static void RequestButton(
            StringBuilder page, string csrf, AllianceCard alliance, RequestRow row,
            string action, string label, bool danger, string? confirm)
        {
            page.Append("<form method=\"post\" action=\"/account/alliance-request\" class=\"inline\"");
            if (confirm != null)
            {
                page.Append(" data-confirm=\"").Append(AdminPage.HtmlEncode(confirm)).Append('"');
            }
            page.Append('>');
            Actors(page, csrf, alliance);
            Hidden(page, PortalFormPolicy.ActionField, action);
            Hidden(page, PortalFormPolicy.InviteField, row.InviteId);
            page.Append("<button class=\"quiet").Append(danger ? " danger" : string.Empty)
                .Append("\" type=\"submit\">").Append(AdminPage.HtmlEncode(label))
                .Append("</button></form> ");
        }

        /// <summary>
        /// The permission literal an action needs, for the sentence a refused
        /// player reads. Taken from <see cref="PortalPermissions"/> rather than
        /// typed, so the page cannot name a permission the check does not use.
        /// </summary>
        internal static string AlliancePermissionName(PortalAction action) =>
            AdminPage.HtmlEncode(PortalPermissions.PermissionFor(action));

        // -------------------------------------------------------------- pieces

        private static void Actors(StringBuilder page, string csrf, AllianceCard alliance)
        {
            Csrf(page, csrf);
            Hidden(page, PortalFormPolicy.AllianceField,
                alliance.AllianceId.ToString("D", CultureInfo.InvariantCulture));

            // WHICH CHARACTER IS ACTING. The session is per account and alliance
            // permissions are per character, so every form has to say which of the
            // account's characters it is acting as - and the handler checks that
            // uid against this account's own roster before it is used as an actor.
            Hidden(page, PortalFormPolicy.CharacterField,
                alliance.ActingCharacterUid.ToString("D", CultureInfo.InvariantCulture));
        }

        internal static void Csrf(StringBuilder page, string csrf) =>
            Hidden(page, PlayerAuthPolicy.CsrfField, csrf);

        internal static void Hidden(StringBuilder page, string name, string value) =>
            page.Append("<input type=\"hidden\" name=\"").Append(AdminPage.HtmlEncode(name))
                .Append("\" value=\"").Append(AdminPage.HtmlEncode(value)).Append("\">");

        private static void Password(StringBuilder page, string label, string name, string autocomplete) =>
            page.Append("        <label class=\"row\"><span>").Append(AdminPage.HtmlEncode(label))
                .Append("</span><input type=\"password\" name=\"").Append(name)
                .Append("\" autocomplete=\"").Append(autocomplete).Append("\" required></label>\n");

        /// <summary>
        /// One key/value fact.
        ///
        /// THE PAIR IS WRAPPED, and that is what lets the list be a grid. A bare
        /// run of dt/dd can only be laid out as two columns - label, value,
        /// label, value, all the way down - which on a 54rem card left every
        /// value stranded in the middle of an otherwise empty row. Wrapping each
        /// pair makes the PAIR the grid item, so the facts flow across the card
        /// three-up and stack to one column on a phone with no breakpoint.
        ///
        /// A div is valid inside a dl and keeps the dt/dd association intact, so
        /// this costs a screen reader nothing.
        /// </summary>
        private static void Fact(StringBuilder page, string key, string value) =>
            page.Append("      <div class=\"fact\"><dt>").Append(AdminPage.HtmlEncode(key))
                .Append("</dt><dd>").Append(AdminPage.HtmlEncode(value)).Append("</dd></div>\n");

        private static void Stat(StringBuilder page, int number, string caption) =>
            page.Append("      <li><span class=\"n\">")
                .Append(number.ToString(CultureInfo.InvariantCulture))
                .Append("</span><span class=\"k\">").Append(AdminPage.HtmlEncode(caption))
                .Append("</span></li>\n");

        /// <summary>A guid as an HTML id fragment - letters and digits only, so it
        /// is a valid anchor target whatever the guid happens to contain.</summary>
        private static string Safe(Guid id) =>
            id.ToString("N", CultureInfo.InvariantCulture);

        private static string Day(DateTimeOffset at) =>
            at.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        private static string Moment(DateTimeOffset at) =>
            at.UtcDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";

        /// <summary>
        /// One coordinate, to the metre.
        ///
        /// Whole metres, not the stored fixed-point units: the units are the
        /// simulation's encoding and printing them would suggest a precision the
        /// player can do nothing with. The ROW is still the exact one the game
        /// server wrote - nothing here is stored back.
        /// </summary>
        private static string Metres(double value) =>
            value.ToString("0", CultureInfo.InvariantCulture);

        // --------------------------------------------------------------- shell

        private static string Shell(
            PortalView view, IReadOnlyList<PortalTab> tabs, string active, string body)
        {
            string name = AdminPage.HtmlEncode(view.DisplayName);

            // THE TAB STRIP IS LINKS. Every one is a real URL this server answers,
            // so it works with script off, it can be bookmarked, and the browser's
            // back button does what a person expects.
            StringBuilder nav = new StringBuilder();
            nav.Append("  <nav class=\"tabs\" aria-label=\"Sections\">");
            foreach (PortalTab tab in tabs)
            {
                bool current = string.Equals(tab.Id, active, StringComparison.Ordinal);

                nav.Append("<a href=\"").Append(PortalTabs.Url(PagePath, tab.Id)).Append('"');
                if (current) nav.Append(" class=\"on\" aria-current=\"page\"");
                nav.Append('>').Append(AdminPage.HtmlEncode(tab.Label)).Append("</a>");
            }
            nav.Append("</nav>\n");

            bool onEmblemTab = string.Equals(active, PortalTabs.Emblem, StringComparison.Ordinal);

            // THE SCRIPT ONLY WHERE THERE IS SOMETHING FOR IT TO DRIVE. A member
            // whose rank cannot change the emblem gets the picture and nothing
            // else, so shipping them a quarter of a megabyte of editor would be
            // shipping code that can only find no form and stop.
            bool editor = onEmblemTab && Editable(view);

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Your account - Worlds Adrift Reborn</title>
<style>
" + WebAssets.Read("account.css") + (onEmblemTab ? WebAssets.Read("emblem-editor.css") : string.Empty) + @"</style>
" + PublicSiteChrome.Style + PublicSiteChrome.PlayerStyle + @"
</head>
<body class=""wa-player wa-portal" + (onEmblemTab ? " wide" : string.Empty) + @""">
" + PublicSiteChrome.Header("account", true) + @"
<main>
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Your account</h1>
  <p class=""greet"">Signed in as <b>" + name + @"</b></p>
" + nav + @"
" + body + @"
  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.<br>
    Alliance emblems are a Wareborn addition &mdash; the original game had no way to change one.
  </footer>
</main>
<script>
" + WebAssets.Fill(
                WebAssets.Read("account.js"),
                ("emblemVersion", EmblemSpec.Version.ToString(CultureInfo.InvariantCulture)))
             + (editor ? EditorScript() : string.Empty)
             + @"</script>
</body>
</html>
";
        }

        /// <summary>
        /// The editor's script, with everything the server must decide filled in.
        ///
        /// Served ONLY on the emblem tab. It is the largest asset on the portal and
        /// nothing else on the page can use it, so a visit about a password should
        /// not pay for it.
        ///
        /// The four filled values are the four the script must not be free to
        /// choose for itself: where the object catalogue is, what the palette is,
        /// what the code's units and limits are, and which version to write. Same
        /// reason <c>account.js</c>'s emblem version is filled in rather than typed
        /// - a page building codes in a shape the parser has moved past produces
        /// saves that are silently refused.
        /// </summary>
        /// <summary>Whether any of this account's characters may actually change
        /// an emblem. Asked of the RIGHTS the view already carries, not re-decided
        /// here - see the note on this class.</summary>
        private static bool Editable(PortalView view)
        {
            foreach (CharacterCard card in view.Characters)
            {
                if (card.Alliance != null && card.Alliance.Rights.EditEmblem) return true;
            }

            return false;
        }

        private static string EditorScript() => WebAssets.Fill(
            WebAssets.Read("emblem-editor.js"),
            ("emblemCatalogueUrl", EmblemEditorData.CatalogueUrl),
            ("emblemPalette", EmblemEditorData.PaletteJson()),
            ("emblemLimits", EmblemEditorData.LimitsJson()),
            ("emblemRoute", EmblemUrlPolicy.RoutePrefix + EmblemUrlPolicy.PreviewId));
    }
}
