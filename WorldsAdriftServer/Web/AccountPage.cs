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
    /// THE CREST BUILDER IS THE ONE IT ALWAYS WAS. Same form, same fields, same
    /// <c>&lt;img&gt;</c> pointed at <c>/alliance-emblem/preview.png</c> - the real
    /// renderer, the same bytes the game gets, deliberately NOT a canvas drawing
    /// the same options a second time. It moved into the alliance card and lost a
    /// heading; nothing else about it changed.
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

        internal static string Render(PortalView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            StringBuilder body = new StringBuilder();

            if (view.Notice != null)
            {
                body.Append("  <p class=\"notice ")
                    .Append(view.NoticeIsError ? "bad" : "good")
                    .Append("\">")
                    .Append(AdminPage.HtmlEncode(view.Notice))
                    .Append("</p>\n");
            }

            AppendAccount(body, view);
            AppendDownload(body, view);

            if (view.Characters.Count == 0)
            {
                body.Append(@"  <section class=""card empty"" id=""characters"">
    <h2>Characters</h2>
    <p>You have not made a character yet. Run the patcher, start the game and
    create one - it will appear here with everything it learns.</p>
  </section>
");
            }

            foreach (CharacterCard character in view.Characters)
            {
                AppendCharacter(body, view.Csrf, character);
            }

            return Shell(view, body.ToString());
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
            if (card.Alliance != null) AppendAlliance(page, csrf, card.Alliance);

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
            page.Append("    <h3>Alliance</h3>\n");
            page.Append("    <p class=\"as\"><b>").Append(AdminPage.HtmlEncode(alliance.Name))
                .Append("</b> &middot; you are <b>")
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
            AppendAllianceEmblem(page, csrf, alliance);

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
        /// The crest builder, unchanged in everything that matters.
        ///
        /// The preview is an <c>&lt;img&gt;</c> on <c>/alliance-emblem/preview.png</c>
        /// - the SAME route and the same painter the game downloads from - and not
        /// a canvas drawing the options a second time. Two renderers of one picture
        /// drift silently; this repository already bought that lesson once with the
        /// map mirror, which now needs a 1e-9 parity test to hold two
        /// implementations together. The script computes the twelve-character code
        /// and nothing else.
        /// </summary>
        private static void AppendAllianceEmblem(StringBuilder page, string csrf, AllianceCard alliance)
        {
            if (!alliance.Rights.EditEmblem)
            {
                page.Append("    <h3>Crest</h3>\n");
                page.Append("    <div class=\"stage\"><img class=\"preview\" "
                    + "alt=\"Alliance crest\" src=\"")
                    .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                    .Append("\"></div>\n");
                // Only when the crest is the ONE thing they are short of. A rank
                // that carries nothing gets the single summary note at the foot of
                // the card instead; two notices saying the same thing in different
                // words reads as nagging rather than as an explanation.
                if (!alliance.Rights.Nothing)
                {
                    page.Append("    <p class=\"locked\">Changing the crest needs a rank that grants "
                        + "<code>").Append(AlliancePermissionName(PortalAction.EditEmblem))
                        .Append("</code>.</p>\n");
                }

                return;
            }

            page.Append("    <h3>Crest</h3>\n");

            if (alliance.ExternalEmblemUrl != null)
            {
                page.Append("    <p class=\"notice\">This alliance currently wears an image an "
                    + "operator set by hand (<code>")
                    .Append(AdminPage.HtmlEncode(alliance.ExternalEmblemUrl))
                    .Append("</code>). Saving a crest below replaces it.</p>\n");
            }

            page.Append("    <form method=\"post\" action=\"/account/alliance-emblem\" class=\"builder\">\n");
            Csrf(page, csrf);
            Hidden(page, EmblemFormPolicy.AllianceField,
                alliance.AllianceId.ToString("D", CultureInfo.InvariantCulture));
            Hidden(page, EmblemFormPolicy.CharacterField,
                alliance.ActingCharacterUid.ToString("D", CultureInfo.InvariantCulture));

            page.Append("      <div class=\"stage\">\n");
            page.Append("        <img class=\"preview\" alt=\"Alliance crest preview\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                .Append("\">\n");
            page.Append("        <p class=\"hint\">This is the picture the game downloads "
                + "&mdash; it is drawn by the server, not by this page.</p>\n");

            // The vector of the same crest. The game never sees this - it decodes
            // PNG and JPEG only - but a leader who wants their alliance's mark on a
            // banner, a sticker or a Discord header should not have to screenshot a
            // 256-pixel square to get it.
            page.Append("        <p class=\"hint\"><a class=\"vector\" download href=\"")
                .Append(AdminPage.HtmlEncode(
                    EmblemUrlPolicy.VectorUrl(alliance.AllianceId, alliance.Emblem)))
                .Append("\">Download as SVG</a> &mdash; the same crest as vector art, at any size.</p>\n");
            page.Append("      </div>\n");

            page.Append("      <div class=\"controls\">\n");
            Select(page, "Shape", EmblemFormPolicy.ShapeField,
                EmblemVocabulary.ShapeNames, (int)alliance.Emblem.Shape);
            Select(page, "Field pattern", EmblemFormPolicy.DivisionField,
                EmblemVocabulary.DivisionNames, (int)alliance.Emblem.Division);
            Select(page, "Device", EmblemFormPolicy.ChargeField,
                EmblemVocabulary.ChargeNames, (int)alliance.Emblem.Charge);
            Swatches(page, "Field colour", EmblemFormPolicy.FieldColourField, alliance.Emblem.FieldColour);
            Swatches(page, "Pattern colour", EmblemFormPolicy.DetailColourField, alliance.Emblem.DetailColour);
            Swatches(page, "Device colour", EmblemFormPolicy.ChargeColourField, alliance.Emblem.ChargeColour);
            page.Append("      </div>\n");

            page.Append("      <button class=\"plank\" type=\"submit\">Save crest</button>\n");
            page.Append("    </form>\n");
        }

        /// <summary>
        /// The permission literal an action needs, for the sentence a refused
        /// player reads. Taken from <see cref="PortalPermissions"/> rather than
        /// typed, so the page cannot name a permission the check does not use.
        /// </summary>
        private static string AlliancePermissionName(PortalAction action) =>
            AdminPage.HtmlEncode(PortalPermissions.PermissionFor(action));

        // -------------------------------------------------------------- pieces

        private static void Select(
            StringBuilder page, string label, string name, IReadOnlyList<string> options, int selected)
        {
            page.Append("        <label class=\"row\"><span>").Append(AdminPage.HtmlEncode(label))
                .Append("</span>\n          <select name=\"").Append(name).Append("\">\n");

            for (int i = 0; i < options.Count; i++)
            {
                page.Append("            <option value=\"")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (i == selected) page.Append(" selected");
                page.Append('>').Append(AdminPage.HtmlEncode(options[i])).Append("</option>\n");
            }

            page.Append("          </select>\n        </label>\n");
        }

        /// <summary>
        /// A colour picked from the palette as a grid of radio buttons.
        ///
        /// Radios rather than an <c>&lt;input type=color&gt;</c> because the value
        /// is an INDEX, not a colour: the palette is closed on purpose (see
        /// <see cref="EmblemVocabulary"/>), and a free colour picker would both
        /// widen the input and let a player choose the one value that makes their
        /// own crest illegible. Radios also degrade to something usable with no
        /// script at all, which the whole form does.
        /// </summary>
        private static void Swatches(StringBuilder page, string label, string name, int selected)
        {
            page.Append("        <fieldset class=\"row swatches\"><legend>")
                .Append(AdminPage.HtmlEncode(label)).Append("</legend>\n");

            for (int i = 0; i < EmblemVocabulary.ColourCount; i++)
            {
                string hex = "#" + EmblemVocabulary.Palette[i].ToString("x6", CultureInfo.InvariantCulture);

                page.Append("          <label class=\"sw\" title=\"")
                    .Append(AdminPage.HtmlEncode(EmblemVocabulary.PaletteNames[i]))
                    .Append("\" style=\"--sw:").Append(hex).Append("\">")
                    .Append("<input type=\"radio\" name=\"").Append(name)
                    .Append("\" value=\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (i == selected) page.Append(" checked");
                page.Append("><span></span></label>\n");
            }

            page.Append("        </fieldset>\n");
        }

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

        private static void Csrf(StringBuilder page, string csrf) =>
            Hidden(page, PlayerAuthPolicy.CsrfField, csrf);

        private static void Hidden(StringBuilder page, string name, string value) =>
            page.Append("<input type=\"hidden\" name=\"").Append(AdminPage.HtmlEncode(name))
                .Append("\" value=\"").Append(AdminPage.HtmlEncode(value)).Append("\">");

        private static void Password(StringBuilder page, string label, string name, string autocomplete) =>
            page.Append("        <label class=\"row\"><span>").Append(AdminPage.HtmlEncode(label))
                .Append("</span><input type=\"password\" name=\"").Append(name)
                .Append("\" autocomplete=\"").Append(autocomplete).Append("\" required></label>\n");

        private static void Fact(StringBuilder page, string key, string value) =>
            page.Append("      <dt>").Append(AdminPage.HtmlEncode(key)).Append("</dt><dd>")
                .Append(AdminPage.HtmlEncode(value)).Append("</dd>\n");

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

        private static string Shell(PortalView view, string body)
        {
            string name = AdminPage.HtmlEncode(view.DisplayName);

            StringBuilder nav = new StringBuilder();
            nav.Append("  <nav class=\"jump\"><a href=\"#account\">Account</a>")
               .Append("<a href=\"#download\">Patcher</a>");
            foreach (CharacterCard card in view.Characters)
            {
                nav.Append("<a href=\"#c").Append(Safe(card.Sheet.Uid)).Append("\">")
                   .Append(AdminPage.HtmlEncode(card.Sheet.Name)).Append("</a>");
            }
            nav.Append("</nav>\n");

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Your account - Worlds Adrift Reborn</title>
<style>
" + WebAssets.Read("account.css") + @"</style>
</head>
<body>
<main>
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Your account</h1>
  <p class=""greet"">Signed in as <b>" + name + @"</b></p>
" + nav + @"
" + body + @"
  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.<br>
    Alliance crests are a Wareborn addition &mdash; the original game had no way to change one.
  </footer>
</main>
<script>
" + WebAssets.Fill(
                WebAssets.Read("account.js"),
                ("emblemVersion", EmblemSpec.Version.ToString(CultureInfo.InvariantCulture)))
             + @"</script>
</body>
</html>
";
        }
    }
}
