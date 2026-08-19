using System.Globalization;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Portal;
using WorldsAdriftServer.Social;

namespace WorldsAdriftServer.Handlers.Account
{
    /// <summary>
    /// Reads one account's whole portal out of the database.
    ///
    /// Split from <see cref="AccountHandler"/> because they fail differently and
    /// are edited for different reasons: this is the READ side and it must degrade
    /// panel by panel - a character whose crew row is missing still has a
    /// knowledge panel - while the handler is the WRITE side, where a partial
    /// answer is not a thing that exists. Keeping them apart also keeps the
    /// handler from becoming the class that does everything on this route.
    ///
    /// NO RULES LIVE HERE either. Permissions are <see cref="PortalPermissions"/>,
    /// which delegates to <see cref="AlliancePolicy"/>; what a sheet shows is
    /// <see cref="CharacterSheetPolicy"/>. This assembles the view and does the
    /// I/O, and it asks each of those exactly once per alliance so two sections of
    /// one card cannot be drawn from two different ledgers.
    /// </summary>
    internal static class PortalViewBuilder
    {
        /// <summary>
        /// Builds the view for one signed-in account.
        ///
        /// <paramref name="notice"/> is whatever the last post left in the query
        /// string. It is passed through rather than derived here so the same
        /// sentence can be produced by a redirect that never reached this code.
        /// </summary>
        internal static PortalView Build(
            long accountId, string csrf, string? notice, bool noticeIsError)
        {
            AccountRecord? account = Accounts.Repository.FindById(accountId);

            (string version, string build) = Download.DownloadHandler.ReadManifestVersionBuild();

            List<CharacterCard> cards = new List<CharacterCard>();
            Dictionary<Guid, string> names = new Dictionary<Guid, string>();

            IReadOnlyList<CharacterRecord> roster = Accounts.Characters.ListForAccount(accountId);
            AllianceLedger? ledger = null;

            foreach (CharacterRecord character in roster)
            {
                // The trailing create-a-character row is a slot, not a person.
                if (character.IsEmptySlot) continue;

                names[character.CharacterUid] = character.Name;

                // Hydrated lazily and ONCE: an account with no alliance member
                // anywhere should not pay for a whole-world ledger, and an account
                // with two should not build two of them.
                ledger ??= AllianceLedgerBuilder.Build(Accounts.Alliances, Accounts.SocialInvites);

                cards.Add(new CharacterCard(
                    SheetFor(character),
                    CrewFor(character, names),
                    AllianceFor(character, ledger, names)));
            }

            return new PortalView(
                account?.Username ?? "traveller",
                account?.DisplayName ?? account?.Username ?? "traveller",
                account?.CreatedAt ?? DateTimeOffset.UtcNow,
                account?.LastLoginAt,
                version,
                build,
                cards,
                csrf,
                notice,
                noticeIsError);
        }

        // ------------------------------------------------------------- the sheet

        private static CharacterSheet SheetFor(CharacterRecord character)
        {
            string? progression = Read(() => Accounts.Progressions.Find(character.CharacterUid)?.DataJson);
            string? inventory = Read(() => Accounts.Inventories.Find(character.CharacterUid)?.DataJson);
            PositionRecord? position = Read(() => Accounts.Positions.Find(character.CharacterUid));

            return CharacterSheetPolicy.Build(character, progression, inventory, position, Locate);
        }

        /// <summary>
        /// Which island a stored position belongs to, answered by the game
        /// server's own <see cref="IslandLocationPolicy"/> against the WHOLE
        /// preserved world.
        ///
        /// The whole world rather than this boot's rollout, deliberately, and for
        /// the reason that policy already states: a character parked on an island
        /// the server is not hosting today is still standing on THAT island, and
        /// calling it open sky would be a worse answer than naming a place the
        /// player cannot currently reach.
        /// </summary>
        private static (string Place, bool OnKnownTerrain) Locate(long x, long y, long z)
        {
            try
            {
                IslandLocation location = IslandLocationPolicy.Locate(
                    new FixedPointPosition(x, y, z), IslandLocationPolicy.KnownWorld());

                return (location.Name, location.Kind == IslandLocationKind.OnKnownTerrain);
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] /account could not place a position: " + e.Message);
                return ("open sky", false);
            }
        }

        // -------------------------------------------------------------- the crew

        private static CrewCard? CrewFor(CharacterRecord character, Dictionary<Guid, string> names)
        {
            CrewMemberRecord? membership =
                Read(() => Accounts.Crews.MemberOf(character.CharacterUid));
            if (membership == null) return null;

            CrewRecord? crew = Read(() => Accounts.Crews.FindCrew(membership.CrewId));
            if (crew == null) return null;

            IReadOnlyList<CrewMemberRecord> members =
                Read(() => Accounts.Crews.MembersOf(crew.CrewId)) ?? Array.Empty<CrewMemberRecord>();

            List<CrewMemberRow> rows = new List<CrewMemberRow>();
            foreach (CrewMemberRecord member in members)
            {
                rows.Add(new CrewMemberRow(
                    NameOf(member.CharacterUid, names),
                    member.CharacterUid == crew.LeaderUid,
                    member.CharacterUid == character.CharacterUid,
                    member.Slot));
            }

            // "<leader>'s crew" - the same sentence SocialService.CrewDataFor sends
            // the game client, because a crew has no name of its own and the portal
            // must not invent a second one for the same rows.
            string leader = NameOf(crew.LeaderUid, names);
            string name = leader.Length > 0 ? leader + "'s crew" : crew.CrewId;

            return new CrewCard(crew.CrewId, name, crew.NumSlots, rows);
        }

        // ---------------------------------------------------------- the alliance

        private static AllianceCard? AllianceFor(
            CharacterRecord character, AllianceLedger ledger, Dictionary<Guid, string> names)
        {
            AllianceMemberRecord? membership =
                Read(() => Accounts.Alliances.MemberOf(character.CharacterUid));
            if (membership == null) return null;

            AllianceRecord? alliance = Read(() => Accounts.Alliances.FindAlliance(membership.AllianceId));
            if (alliance == null) return null;

            string allianceId = AllianceWire.Uid(alliance.AllianceId);
            string actorKey = AllianceEndpoints.Key(character.CharacterUid);

            IReadOnlyList<AllianceRankRecord> rankRows =
                Read(() => Accounts.Alliances.RanksOf(alliance.AllianceId))
                ?? Array.Empty<AllianceRankRecord>();

            Dictionary<Guid, AllianceRankRecord> ranksById = new Dictionary<Guid, AllianceRankRecord>();
            List<AllianceRankRow> ranks = new List<AllianceRankRow>();

            foreach (AllianceRankRecord rank in rankRows)
            {
                ranksById[rank.RankId] = rank;
                ranks.Add(new AllianceRankRow(
                    rank.RankId,
                    rank.Name,
                    rank.Editable,
                    !rank.Editable && rank.RankType == AllianceRank.TypeLeader,
                    AllianceWire.UnpackPermissions(rank.Permissions)));
            }

            AllianceRights rights = PortalPermissions.RightsFor(ledger, actorKey, allianceId);

            List<AllianceMemberRow> members = new List<AllianceMemberRow>();
            IReadOnlyList<AllianceMemberRecord> roster =
                Read(() => Accounts.Alliances.MembersOf(alliance.AllianceId))
                ?? Array.Empty<AllianceMemberRecord>();

            foreach (AllianceMemberRecord member in roster)
            {
                string targetKey = AllianceEndpoints.Key(member.CharacterUid);
                ranksById.TryGetValue(member.RankId, out AllianceRankRecord? rank);

                // Asked PER ROW rather than derived from the coarse "can you manage
                // members" flag. They differ exactly where it matters: the founder
                // cannot be booted and nobody can boot themselves, and a page drawn
                // from the coarse answer would offer two buttons that always fail.
                bool mayBoot = PortalPermissions.May(
                    ledger, PortalAction.BootMember, actorKey, allianceId, targetKey)
                    == AllianceVerdict.Ok;

                bool maySetRank = PortalPermissions.May(
                    ledger, PortalAction.SetMemberRank, actorKey, allianceId, targetKey)
                    == AllianceVerdict.Ok;

                members.Add(new AllianceMemberRow(
                    member.CharacterUid,
                    NameOf(member.CharacterUid, names),
                    rank?.Name ?? "member",
                    member.RankId,
                    member.CharacterUid == alliance.LeaderUid,
                    member.CharacterUid == character.CharacterUid,
                    mayBoot,
                    maySetRank));
            }

            (List<RequestRow> applications, List<RequestRow> invitations) =
                RequestsFor(allianceId, names);

            bool built = EmblemUrlPolicy.TryReadStored(alliance.EmblemUrl, out EmblemSpec spec);
            if (!built) spec = EmblemSpec.DefaultFor(alliance.AllianceId);

            // A non-empty column that is NOT one of our markers is an operator's
            // hand-set URL. Surfaced rather than hidden, so a player whose crest
            // does not change when they save one learns why instead of filing it
            // as a bug.
            string? external =
                !built && !string.IsNullOrWhiteSpace(alliance.EmblemUrl) ? alliance.EmblemUrl : null;

            ranksById.TryGetValue(membership.RankId, out AllianceRankRecord? yours);

            return new AllianceCard(
                alliance.AllianceId,
                character.CharacterUid,
                alliance.Name,
                alliance.Description,
                alliance.MessageOfTheDay,
                yours?.Name ?? "member",
                yours == null
                    ? Array.Empty<string>()
                    : AllianceWire.UnpackPermissions(yours.Permissions),
                alliance.LeaderUid == character.CharacterUid,
                members,
                ranks,
                applications,
                invitations,
                spec,
                built,
                external,
                rights);
        }

        /// <summary>
        /// The alliance's outstanding offers, split by direction.
        ///
        /// A null inviter is an APPLICATION and a set one is an INVITE. That is not
        /// a convention chosen here: the client's own
        /// <c>CheckMembershipRequestType</c> tests exactly that field, so splitting
        /// on anything else would be a second source of truth for the same rows.
        /// Only <c>new</c> rows are shown - accepted, rejected and cancelled ones
        /// are history, and there is nothing left to do to them.
        /// </summary>
        private static (List<RequestRow> Applications, List<RequestRow> Invitations) RequestsFor(
            string allianceId, Dictionary<Guid, string> names)
        {
            List<RequestRow> applications = new List<RequestRow>();
            List<RequestRow> invitations = new List<RequestRow>();

            IReadOnlyList<SocialInviteRecord> rows =
                Read(() => Accounts.SocialInvites.ForTarget(allianceId))
                ?? Array.Empty<SocialInviteRecord>();

            foreach (SocialInviteRecord row in rows)
            {
                if (!string.Equals(row.Status, SocialInviteStatus.New, StringComparison.Ordinal)) continue;
                if (!string.Equals(row.TargetType, SocialTargetType.Alliance, StringComparison.Ordinal)) continue;

                RequestRow request = new RequestRow(
                    row.InviteId, NameOf(row.CharacterUid, names), row.Message, row.CreatedAt);

                if (row.InviterUid == null) applications.Add(request);
                else invitations.Add(request);
            }

            return (applications, invitations);
        }

        // --------------------------------------------------------------- helpers

        /// <summary>
        /// A character's name, cached per request. Falls back to a short form of
        /// the uid: a member row with no name at all is a row nobody can act on,
        /// and "an unknown character" hides the fact that the row exists.
        /// </summary>
        internal static string NameOf(Guid uid, Dictionary<Guid, string> names)
        {
            if (names.TryGetValue(uid, out string? cached)) return cached;

            string name;
            try
            {
                name = Accounts.Characters.Find(uid)?.Name ?? string.Empty;
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            if (name.Length == 0)
            {
                name = "character " + uid.ToString("D", CultureInfo.InvariantCulture).Substring(0, 8);
            }

            names[uid] = name;
            return name;
        }

        /// <summary>
        /// One read, degrading to null if the database blinked.
        ///
        /// Per-panel rather than around the whole build: a portal that 500s because
        /// one character's inventory row could not be read is a portal that hides
        /// four other characters for no reason. The player sees the panel say
        /// nothing was saved, which is the same thing they would see if nothing had
        /// been - and the next load fixes it.
        /// </summary>
        private static T? Read<T>(Func<T?> read) where T : class
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] /account: a read failed: " + e.Message);
                return null;
            }
        }
    }
}
