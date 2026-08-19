using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The alliance half of the reconstructed Bossa social API.
    ///
    /// Seventeen endpoints, plus the four shared membership-change calls that
    /// alliances put alliance-shaped rows through. Everything the retail Social
    /// Sheet's ALLIANCE tab does lands here: founding, the alliance page, the
    /// member list, invitations, applications, the message of the day, the
    /// description, ranks and their permissions, leaving, booting and disbanding.
    ///
    /// Split out of <see cref="SocialService"/> rather than added to it, for two
    /// reasons that pull the same way. The first is size - the crew side is six
    /// endpoints and this is seventeen, and one class answering twenty-three
    /// endpoints is the god class this repository has a rule against. The second is
    /// testability, and it is the more important one: this takes PORTS
    /// (<see cref="IAllianceStore"/>, <see cref="ISocialInviteStore"/>, and a name
    /// lookup) rather than the concrete Postgres repositories, so the whole
    /// contract can be driven through the REAL route parser with no database at
    /// all. The crew endpoints could not be, their tests are
    /// <c>[PostgresFact]</c>-skipped on most machines, and two defects shipped
    /// through that gap.
    ///
    /// No rules live here. "May this player boot that one", "who inherits", "is
    /// that name allowed" all belong to <see cref="AlliancePolicy"/> and
    /// <see cref="AllianceLedger"/> in the engine-free multiplayer project, which
    /// is where they can be asserted exhaustively. This class hydrates a ledger,
    /// asks, and writes the answer down.
    ///
    /// The contract is in docs/research/findings-social-api.md with file:line
    /// citations into the decompile.
    /// </summary>
    internal sealed class AllianceEndpoints
    {
        private readonly IAllianceStore alliances;
        private readonly ISocialInviteStore invites;
        private readonly Func<Guid, string?> nameOf;
        private readonly string region;
        private readonly Func<DateTimeOffset> clock;
        private readonly string emblemBaseUrl;

        /// <param name="nameOf">
        /// A character's display name, or NULL when no such character exists. The
        /// null is load-bearing - it is also how "is this a real player" is asked -
        /// so a lookup that returned "" for a missing row would let an invite be
        /// written to nobody.
        /// </param>
        /// <param name="emblemBaseUrl">
        /// The origin to build crest URLs from, normally the one the CALLER
        /// reached this server on - see <see cref="Emblems.EmblemOrigin"/> for why
        /// that and not a configured host name. Null falls back to the configured
        /// <see cref="Emblems.EmblemImages.BaseUrl"/>.
        /// </param>
        internal AllianceEndpoints(
            IAllianceStore alliances,
            ISocialInviteStore invites,
            Func<Guid, string?> nameOf,
            string region,
            Func<DateTimeOffset>? clock = null,
            string? emblemBaseUrl = null)
        {
            this.emblemBaseUrl = string.IsNullOrWhiteSpace(emblemBaseUrl)
                ? Emblems.EmblemImages.BaseUrl
                : emblemBaseUrl!;

            this.alliances = alliances ?? throw new ArgumentNullException(nameof(alliances));
            this.invites = invites ?? throw new ArgumentNullException(nameof(invites));
            this.nameOf = nameOf ?? throw new ArgumentNullException(nameof(nameOf));
            this.region = region ?? throw new ArgumentNullException(nameof(region));
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>Every route this type owns. False means it is not ours.</summary>
        internal static bool Owns(SocialRouteKind kind) => kind switch
        {
            SocialRouteKind.CreateAlliance => true,
            SocialRouteKind.FindAllianceForCharacter => true,
            SocialRouteKind.GetAlliance => true,
            SocialRouteKind.UpdateAlliance => true,
            SocialRouteKind.DisbandAlliance => true,
            SocialRouteKind.AllianceBatch => true,
            SocialRouteKind.ListAlliances => true,
            SocialRouteKind.SearchAlliances => true,
            SocialRouteKind.AllianceMembers => true,
            SocialRouteKind.AllianceInvites => true,
            SocialRouteKind.ApplyToAlliance => true,
            SocialRouteKind.UpdateAllianceMembership => true,
            SocialRouteKind.RemoveAllianceMember => true,
            SocialRouteKind.AllianceRanks => true,
            SocialRouteKind.CreateAllianceRank => true,
            SocialRouteKind.UpdateAllianceRank => true,
            SocialRouteKind.DeleteAllianceRank => true,
            _ => false,
        };

        internal JObject Handle(SocialRoute route, Guid actor, string url, string? body)
        {
            switch (route.Kind)
            {
                case SocialRouteKind.CreateAlliance:
                    return Create(actor, body);

                case SocialRouteKind.FindAllianceForCharacter:
                    return FindForCharacter(route.Segments[1]);

                case SocialRouteKind.GetAlliance:
                    return Get(route.Segments[1]);

                case SocialRouteKind.UpdateAlliance:
                    return Update(actor, route.Segments[1], body);

                case SocialRouteKind.DisbandAlliance:
                    return Disband(actor, route.Segments[1]);

                case SocialRouteKind.AllianceBatch:
                    return Batch(body);

                case SocialRouteKind.ListAlliances:
                    return List();

                case SocialRouteKind.SearchAlliances:
                    return Search(SocialRoute.QueryValue(url, "term"));

                case SocialRouteKind.AllianceMembers:
                    return Members(route.Segments[0]);

                case SocialRouteKind.AllianceInvites:
                    return Invites(actor, route.Segments[0]);

                case SocialRouteKind.ApplyToAlliance:
                    return Apply(actor, body);

                case SocialRouteKind.UpdateAllianceMembership:
                    return UpdateMembership(actor, route.Segments[0], route.Segments[1], body);

                case SocialRouteKind.RemoveAllianceMember:
                    return RemoveMember(actor, route.Segments[0], route.Segments[1]);

                case SocialRouteKind.AllianceRanks:
                    return Ranks(route.Segments[0]);

                case SocialRouteKind.CreateAllianceRank:
                    return CreateRank(actor, body);

                case SocialRouteKind.UpdateAllianceRank:
                    return UpdateRank(actor, route.Segments[0], body);

                case SocialRouteKind.DeleteAllianceRank:
                    return DeleteRank(actor, route.Segments[0]);

                default:
                    return SocialEnvelope.Error(SocialErrorCodes.StoreUnavailable);
            }
        }

        // ---------------------------------------------------------------- create

        /// <summary>
        /// POST alliance - the request that used to be answered "not implemented"
        /// and reached the player as the client's generic unknown-error dialog,
        /// E00001.
        ///
        /// Body is <c>{leaderCharacterUid, name, description?, messageOfTheDay?,
        /// region}</c>. The two optional fields are OMITTED, not sent empty, when
        /// the player left them blank (<c>CreateAllianceFillOptionalFields</c>), so
        /// an absent key means "" rather than being an error.
        ///
        /// <c>leaderCharacterUid</c> is read but not trusted: the founder is the
        /// authenticated caller. The client always sends its own uid, so a mismatch
        /// means somebody is founding an alliance in another player's name.
        /// </summary>
        private JObject Create(Guid actor, string? body)
        {
            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            string? claimed = payload.Value<string>("leaderCharacterUid");
            if (claimed != null && (!Guid.TryParse(claimed, out Guid claimedUid) || claimedUid != actor))
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            string? name = payload.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return SocialEnvelope.Error(SocialErrorCodes.InvalidName);

            if (nameOf(actor) == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayCreate(ledger, Key(actor), name);
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            DateTimeOffset now = clock();

            // A fresh GUID rather than a counter. The crew side learned this the
            // expensive way: a bare counter that restarted at 1 every boot handed a
            // brand new crew the id of a RESTORED one, and the ON CONFLICT write
            // silently gave somebody else's crew away. A GUID is collision-proof
            // against the restored ledger by construction rather than by remembering
            // a high-water mark, which is the property that actually matters - the
            // thing a new id must not collide with is every id that exists, not
            // every id this process has issued. It is also required here for an
            // unrelated reason: the client runs alliance ids through SanitizeGuid.
            Guid allianceId = NewIdNotIn(ledger);
            Guid leaderRankId = Guid.NewGuid();
            Guid memberRankId = Guid.NewGuid();

            AllianceRecord record = new AllianceRecord(
                allianceId,
                region,
                name!,
                payload.Value<string>("description") ?? string.Empty,
                payload.Value<string>("messageOfTheDay") ?? string.Empty,
                // No emblem. The client never sends one and has no UI that could -
                // see the crest note in docs/research/findings-social-api.md.
                string.Empty,
                actor,
                now,
                now);

            // The unique index is the arbiter, not the ledger check above: two
            // founders racing from two sessions both pass a read and only one may
            // insert.
            if (!alliances.TryInsertAlliance(record))
            {
                return SocialEnvelope.Error(SocialErrorCodes.DuplicateAllianceName);
            }

            // Both default ranks, immediately. They are not decoration: the client
            // fills AllianceRankInformation.Leader and .BasicMember by scanning the
            // rank list for rankType+editable, then dereferences them, and
            // AllianceClient.TryGetRank THROWS if the founder's own rankId is not
            // among the ranks we serve. An alliance without them is an alliance
            // that exists and cannot be opened.
            alliances.SaveRank(new AllianceRankRecord(
                leaderRankId, allianceId, "Leader", Editable: false,
                AllianceRank.TypeLeader, AllianceRank.MembershipType,
                AllianceWire.PackPermissions(AlliancePermissions.DefaultLeader), 0));

            alliances.SaveRank(new AllianceRankRecord(
                memberRankId, allianceId, "Member", Editable: false,
                AllianceRank.TypeMember, AllianceRank.MembershipType,
                AllianceWire.PackPermissions(AlliancePermissions.DefaultMember), 1));

            alliances.SaveMember(new AllianceMemberRecord(
                actor, allianceId, leaderRankId, string.Empty, string.Empty, 0, now, now));

            AllianceRecord? stored = alliances.FindAlliance(allianceId);
            return stored == null
                ? SocialEnvelope.Error(SocialErrorCodes.StoreUnavailable)
                : SocialEnvelope.Ok(Wire(stored));
        }

        // ----------------------------------------------------------------- reads

        /// <summary>
        /// GET alliance/find/{region}/{characterUid}.
        ///
        /// The third segment is a CHARACTER, not an alliance - the one endpoint
        /// where <c>alliance/...</c> is addressed by the person rather than by the
        /// group. Reached only after <c>memberships/character</c> already said the
        /// player is in one (<c>AllianceClient.GetYourBasicAllianceInfo</c> tests
        /// that first and resolves null without asking otherwise), so "not in an
        /// alliance" here means our own two answers disagree, and it is refused
        /// rather than papered over.
        /// </summary>
        private JObject FindForCharacter(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceMemberRecord? membership = alliances.MemberOf(uid);
            if (membership == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            AllianceRecord? alliance = alliances.FindAlliance(membership.AllianceId);
            return alliance == null
                ? SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId)
                : SocialEnvelope.Ok(Wire(alliance));
        }

        private JObject Get(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceRecord? alliance = alliances.FindAlliance(uid);
            return alliance == null
                ? SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId)
                : SocialEnvelope.Ok(Wire(alliance));
        }

        /// <summary>
        /// GET alliances/{region} - the browser, in <c>data.items</c>.
        ///
        /// This used to answer an honestly empty list, which was true while we
        /// hosted none. It is now the real list, and every alliance is public: the
        /// client's browser is how a player finds one to apply to, and an alliance
        /// nobody can see is an alliance nobody can join.
        /// </summary>
        private JObject List()
        {
            JArray items = new JArray();
            foreach (AllianceRecord alliance in alliances.AllAlliances())
            {
                items.Add(Wire(alliance));
            }

            return SocialEnvelope.OkItems(items);
        }

        /// <summary>
        /// GET alliance/search/{region}?term= - a BARE ARRAY of alliance UID
        /// STRINGS, not of alliances.
        ///
        /// The client takes the ids and issues a SECOND call, POST
        /// <c>alliance/{region}/batch</c>, to fetch them. Two round trips for one
        /// search is the original service's design, not ours, and the shape has to
        /// match on both halves or the search silently returns nothing.
        ///
        /// Note the client does <c>model.data as JArray</c> with NO null guard and
        /// then reads <c>.Count</c> (AllianceServerImpl.cs:57-58), so an object at
        /// <c>data</c> here is a NullReferenceException inside the client. An empty
        /// array is the correct "no matches" - the client short-circuits on
        /// <c>Count == 0</c> and never sends the batch call.
        /// </summary>
        private JObject Search(string? term)
        {
            string needle = (term ?? string.Empty).Trim();

            JArray ids = new JArray();
            foreach (AllianceRecord alliance in alliances.AllAlliances())
            {
                // Substring, case-insensitive. The client offers a free-text box
                // and filters nothing itself, so anything stricter reads to the
                // player as a search that does not work.
                if (needle.Length > 0
                    && alliance.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                ids.Add(AllianceWire.Uid(alliance.AllianceId));
            }

            return SocialEnvelope.OkBareList(ids);
        }

        /// <summary>
        /// POST alliance/{region}/batch, body <c>{"batch":[uids]}</c> - a BARE
        /// ARRAY of alliances at <c>data</c>, unlike its sibling
        /// <c>alliances/{region}</c> which uses <c>data.items</c>. That asymmetry
        /// is in the client and reproducing it is the job.
        ///
        /// Ids that no longer resolve are SKIPPED rather than refused. The list the
        /// client is holding came from a search that may be seconds old, and one
        /// disbanded alliance in it must not empty the whole result.
        /// </summary>
        private JObject Batch(string? body)
        {
            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            JArray results = new JArray();
            if (payload["batch"] is JArray requested)
            {
                foreach (JToken token in requested)
                {
                    if (!Guid.TryParse(token.Value<string>(), out Guid id)) continue;

                    AllianceRecord? alliance = alliances.FindAlliance(id);
                    if (alliance != null) results.Add(Wire(alliance));
                }
            }

            return SocialEnvelope.OkBareList(results);
        }

        /// <summary>
        /// GET memberships/alliance/{allianceUid} - the roster, in
        /// <c>data.items</c>.
        ///
        /// Emitted WHOLE, with no clamp. That is the opposite of the crew twin,
        /// which truncates to <c>CrewRosterLimits</c>, and the difference is real
        /// rather than an oversight: the crew panel pre-builds a fixed set of five
        /// widgets and indexes past the end on the sixth entry, while the alliance
        /// list instantiates one widget per member through <c>UIObjectFactory</c>
        /// behind a <c>ScrollPaginator</c> (AllianceMembersList.CreateListObjects).
        /// There is no fixed budget to overrun, so clamping here would only hide
        /// members from their own alliance.
        /// </summary>
        private JObject Members(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            JArray items = new JArray();
            foreach (AllianceMemberRecord member in alliances.MembersOf(uid))
            {
                items.Add(WireMembership(member));
            }

            return SocialEnvelope.OkItems(items);
        }

        /// <summary>
        /// GET memberships/invites/alliance/{allianceUid} - BOTH directions in one
        /// list, in <c>data.items</c>.
        ///
        /// The client splits it into the INVITATIONS and APPLICATIONS sections
        /// itself, by whether <c>inviter</c> is null
        /// (<c>CheckMembershipRequestType</c>), and filters to
        /// <c>status == "new"</c>. Resolved rows are dropped here as well, because
        /// an alliance that had ever rejected anybody would otherwise carry that
        /// history in every refresh forever.
        ///
        /// This is one of the two endpoints whose client parser does NOT null-check
        /// <c>data["items"]</c> before iterating it (AllianceServerImpl.cs:158), so
        /// an empty alliance must still receive <c>{"items":[]}</c>.
        /// </summary>
        private JObject Invites(Guid actor, string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            string allianceId = AllianceWire.Uid(uid);

            // An alliance's pending list names players who have not joined it and
            // may not want it known. Members only.
            AllianceMemberRecord? membership = alliances.MemberOf(actor);
            if (membership == null || membership.AllianceId != uid)
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            JArray items = new JArray();
            foreach (SocialInviteRecord invite in invites.ForTarget(allianceId))
            {
                if (invite.Status != SocialInviteStatus.New) continue;
                items.Add(WireRequest(invite));
            }

            return SocialEnvelope.OkItems(items);
        }

        /// <summary>GET ranks/{allianceUid} - a BARE array at <c>data</c>.</summary>
        private JObject Ranks(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            IReadOnlyList<AllianceRankRecord> ranks = alliances.RanksOf(uid);
            if (ranks.Count == 0)
            {
                // The client has a word for exactly this, and it is the only
                // situation it describes. An empty rank list is not a survivable
                // answer: AllianceRankInformation would hold a null Leader and a
                // null BasicMember, and every member's rank lookup would throw.
                return SocialEnvelope.Error(SocialErrorCodes.NoRanksFoundInAlliance);
            }

            JArray items = new JArray();
            foreach (AllianceRankRecord rank in ranks) items.Add(Wire(rank));

            return SocialEnvelope.OkBareList(items);
        }

        // ---------------------------------------------------------------- writes

        /// <summary>
        /// PATCH alliance/{region}/{allianceUid} - body
        /// <c>{messageOfTheDay, description}</c>, and nothing else. Name, region
        /// and emblem are not patchable from this client.
        ///
        /// The two fields carry DIFFERENT permissions - description is
        /// <c>edit_group</c>, the MOTD is <c>leader_chat</c> - and the client sends
        /// both keys on every edit whichever box the player typed in, because
        /// <c>UpdateYourAllianceBasicInfo</c> serialises the whole view model. So a
        /// blanket permission check would let someone with one permission overwrite
        /// the other field with their stale copy. Each field is therefore applied
        /// only if it CHANGED and the actor may change it, and a payload that
        /// changes nothing they are allowed to change is refused with the client's
        /// own <c>empty_update_payload</c>.
        /// </summary>
        private JObject Update(Guid actor, string rawUid, string? body)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            AllianceRecord? alliance = alliances.FindAlliance(uid);
            if (alliance == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            AllianceLedger ledger = Hydrate();
            string allianceId = AllianceWire.Uid(uid);
            string actorKey = Key(actor);

            string description = alliance.Description;
            string motd = alliance.MessageOfTheDay;
            bool changed = false;

            string? wantedDescription = payload.Value<string>("description");
            if (wantedDescription != null && wantedDescription != alliance.Description)
            {
                AllianceVerdict may = AlliancePolicy.MayEditDescription(ledger, actorKey, allianceId);
                if (may != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(may));

                description = wantedDescription;
                changed = true;
            }

            string? wantedMotd = payload.Value<string>("messageOfTheDay");
            if (wantedMotd != null && wantedMotd != alliance.MessageOfTheDay)
            {
                AllianceVerdict may = AlliancePolicy.MayEditMessageOfTheDay(ledger, actorKey, allianceId);
                if (may != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(may));

                motd = wantedMotd;
                changed = true;
            }

            if (!changed)
            {
                // A no-op edit is still refused rather than answered OK, because
                // the client only sends this when the player typed something, and
                // silently succeeding on a change that did not happen is how a
                // permission problem looks like a save that did not stick.
                AllianceVerdict standing = AlliancePolicy.MayEditDescription(ledger, actorKey, allianceId);
                return standing == AllianceVerdict.Ok
                    ? SocialEnvelope.Error(SocialErrorCodes.EmptyUpdatePayload)
                    : SocialEnvelope.Error(VerdictCode(standing));
            }

            AllianceRecord updated = alliance with
            {
                Description = description,
                MessageOfTheDay = motd,
                UpdatedAt = clock(),
            };

            alliances.SaveAlliance(updated);
            return SocialEnvelope.Ok(Wire(updated));
        }

        /// <summary>
        /// DELETE alliance/{region}/{allianceUid}. Founder only - see
        /// <see cref="AlliancePolicy.MayDisband"/>.
        ///
        /// Answered with an EMPTY envelope: the client sends this with
        /// <c>dataFieldExpected: false</c> (AllianceServerImpl.cs:106) and ignores
        /// whatever comes back.
        /// </summary>
        private JObject Disband(Guid actor, string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayDisband(ledger, Key(actor), AllianceWire.Uid(uid));
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            // Outstanding offers to join something that will not exist a moment
            // from now would otherwise sit in their invitees' lists forever.
            invites.CancelAllForTarget(AllianceWire.Uid(uid), clock());
            alliances.DeleteAlliance(uid);

            return SocialEnvelope.OkNoData();
        }

        /// <summary>
        /// DELETE memberships/alliance/{allianceUid}/{characterUid} - both LEAVE
        /// and BOOT, exactly as its crew twin is. Which one it is depends entirely
        /// on whether the actor is removing themselves, so the rule is decided here
        /// rather than inferred from the URL.
        ///
        /// Note the path order: alliance FIRST, character second - the reverse of
        /// <c>memberships/character/{characterUid}/{allianceUid}</c>, which is
        /// eight lines away in the same client file.
        /// </summary>
        private JObject RemoveMember(Guid actor, string rawAlliance, string rawTarget)
        {
            if (!Guid.TryParse(rawAlliance, out Guid allianceUid)
                || !Guid.TryParse(rawTarget, out Guid target))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceLedger ledger = Hydrate();
            string allianceId = AllianceWire.Uid(allianceUid);
            string actorKey = Key(actor);
            string targetKey = Key(target);

            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            // The alliance in the PATH has to be the one the actor is acting on.
            // Both policy questions below resolve the alliance from the actor
            // rather than from the URL, so without this an actor in alliance X
            // sending a path naming alliance Y would be answered about X - a boot
            // that succeeds against a group the caller never named. The client
            // derives this segment from its own cached alliance data, so a
            // mismatch means it is acting on one it is no longer in.
            if (!alliance.Holds(actorKey))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityPair);
            }

            AllianceVerdict verdict = actor == target
                ? AlliancePolicy.MayLeave(ledger, actorKey)
                : AlliancePolicy.MayBoot(ledger, actorKey, targetKey);

            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            // Let the ledger work out succession and dissolve-at-last-member, then
            // mirror whatever it decided. Doing it in that order means the
            // promotion rule exists in exactly one place.
            string leaderBefore = alliance.LeaderUid;
            ledger.Remove(targetKey);
            alliances.RemoveMember(target);

            Alliance? after = ledger.ById(allianceId);
            if (after == null)
            {
                invites.CancelAllForTarget(allianceId, clock());
                alliances.DeleteAlliance(allianceUid);
                return SocialEnvelope.OkNoData();
            }

            if (!string.Equals(after.LeaderUid, leaderBefore, StringComparison.Ordinal))
            {
                Guid? successor = UidFromKey(after.LeaderUid);
                if (successor.HasValue) Persist(after, successor.Value);
            }

            return SocialEnvelope.OkNoData();
        }

        /// <summary>
        /// Writes back a leadership change the ledger decided: the alliance's
        /// leader pointer AND the new founder's rank.
        ///
        /// Both, because leadership in this client is two independent facts -
        /// <c>leaderCharacterUid</c> on the alliance and the rank the member holds -
        /// and moving one without the other produces a founder with no permissions
        /// or an ordinary member the panel draws as leader.
        /// </summary>
        private void Persist(Alliance alliance, Guid successor)
        {
            if (!Guid.TryParse(alliance.Id, out Guid allianceUid)) return;

            AllianceRecord? record = alliances.FindAlliance(allianceUid);
            if (record == null) return;

            DateTimeOffset now = clock();
            alliances.SaveAlliance(record with { LeaderUid = successor, UpdatedAt = now });

            AllianceRank? leaderRank = alliance.DefaultLeaderRank;
            AllianceMemberRecord? membership = alliances.MemberOf(successor);
            if (leaderRank == null || membership == null) return;
            if (!Guid.TryParse(leaderRank.Id, out Guid leaderRankId)) return;

            alliances.SaveMember(membership with { RankId = leaderRankId, UpdatedAt = now });
        }

        /// <summary>
        /// PATCH memberships/character/{characterUid}/{allianceUid}.
        ///
        /// THREE mutually exclusive single-key payloads, each from its own client
        /// helper: <c>{"rankUid": ...}</c>, <c>{"publicOfficerNote": ...}</c> and
        /// <c>{"privateOfficerNote": ...}</c>. They carry different permissions, so
        /// they are handled separately rather than merged into one update.
        ///
        /// Note <c>rankUid</c> on the way IN against <c>rankId</c> on the way OUT.
        /// The names differ by direction; that is the client's contract, not a
        /// simplification of ours.
        /// </summary>
        private JObject UpdateMembership(Guid actor, string rawTarget, string rawAlliance, string? body)
        {
            if (!Guid.TryParse(rawTarget, out Guid target) || !Guid.TryParse(rawAlliance, out Guid allianceUid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            AllianceMemberRecord? membership = alliances.MemberOf(target);
            if (membership == null || membership.AllianceId != allianceUid)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityPair);
            }

            AllianceLedger ledger = Hydrate();
            string actorKey = Key(actor);
            string targetKey = Key(target);

            string? rankUid = payload.Value<string>("rankUid");
            if (rankUid != null)
            {
                if (!Guid.TryParse(rankUid, out Guid rankId))
                {
                    return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
                }

                AllianceVerdict may = AlliancePolicy.MaySetRank(
                    ledger, actorKey, targetKey, AllianceWire.Uid(rankId));
                if (may != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(may));

                membership = membership with { RankId = rankId, UpdatedAt = clock() };
                alliances.SaveMember(membership);
                return SocialEnvelope.Ok(WireMembership(membership));
            }

            string? publicNote = payload.Value<string>("publicOfficerNote");
            string? privateNote = payload.Value<string>("privateOfficerNote");

            if (publicNote == null && privateNote == null)
            {
                return SocialEnvelope.Error(SocialErrorCodes.EmptyUpdatePayload);
            }

            AllianceVerdict permitted = AlliancePolicy.MayEditOfficerNote(ledger, actorKey, targetKey);
            if (permitted != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(permitted));

            membership = membership with
            {
                // Named for the read side: publicOfficerNote on the way in becomes
                // officerNote on the way out.
                OfficerNote = publicNote ?? membership.OfficerNote,
                PrivateOfficerNote = privateNote ?? membership.PrivateOfficerNote,
                UpdatedAt = clock(),
            };

            alliances.SaveMember(membership);
            return SocialEnvelope.Ok(WireMembership(membership));
        }

        // ----------------------------------------------------------------- ranks

        /// <summary>
        /// POST rank - body carries the alliance in <c>target</c>, because the
        /// path does not.
        ///
        /// <c>rankType</c> and <c>editable</c> are taken from the client but not
        /// trusted: it always sends <c>"member"</c> and <c>true</c>
        /// (SocialGroupParsers.CreateRankPayload hardcodes both), and a rank
        /// created as a non-editable leader would displace the alliance's real
        /// leader rank in the client's lookup. So every rank made through this
        /// endpoint is an editable member rank, full stop.
        /// </summary>
        private JObject CreateRank(Guid actor, string? body)
        {
            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            string? rawTarget = payload.Value<string>("target");
            if (!Guid.TryParse(rawTarget, out Guid allianceUid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            string? name = payload.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name)) return SocialEnvelope.Error(SocialErrorCodes.InvalidName);

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayEditRanks(
                ledger, Key(actor), AllianceWire.Uid(allianceUid));
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            IReadOnlyList<AllianceRankRecord> existing = alliances.RanksOf(allianceUid);
            int sortOrder = 0;
            foreach (AllianceRankRecord rank in existing)
            {
                if (rank.SortOrder >= sortOrder) sortOrder = rank.SortOrder + 1;
            }

            AllianceRankRecord created = new AllianceRankRecord(
                Guid.NewGuid(),
                allianceUid,
                name!,
                Editable: true,
                AllianceRank.TypeMember,
                AllianceRank.MembershipType,
                AllianceWire.PackPermissions(Permissions(payload)),
                sortOrder);

            alliances.SaveRank(created);
            return SocialEnvelope.Ok(Wire(created));
        }

        /// <summary>
        /// PUT rank/{rankUid} - rename and re-permission. The alliance comes from
        /// the stored rank, not from the body, so a rank cannot be moved between
        /// alliances by editing it.
        /// </summary>
        private JObject UpdateRank(Guid actor, string rawRank, string? body)
        {
            if (!Guid.TryParse(rawRank, out Guid rankId))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            AllianceRankRecord? stored = alliances.FindRank(rankId);
            if (stored == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            // The two default ranks are structural - the client fills its Leader
            // and BasicMember slots from them - so they keep their type and their
            // permissions.
            if (!stored.Editable) return SocialEnvelope.Error(SocialErrorCodes.UneditableRank);

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayEditRanks(
                ledger, Key(actor), AllianceWire.Uid(stored.AllianceId));
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            string name = payload.Value<string>("name") ?? stored.Name;
            if (string.IsNullOrWhiteSpace(name)) return SocialEnvelope.Error(SocialErrorCodes.InvalidName);

            AllianceRankRecord updated = stored with
            {
                Name = name,
                Permissions = AllianceWire.PackPermissions(Permissions(payload)),
            };

            alliances.SaveRank(updated);
            return SocialEnvelope.Ok(Wire(updated));
        }

        /// <summary>
        /// DELETE rank/{rankUid}.
        ///
        /// Answered WITH a data field, unlike the alliance and membership DELETEs
        /// beside it: the client sends this one with the DEFAULT
        /// <c>dataFieldExpected</c>, which is true (AllianceServerImpl.cs:132), so
        /// an empty envelope throws "Data in server response was empty" at the
        /// player. That inconsistency is retail's and is reproduced rather than
        /// tidied - the same trap the crew member-removal endpoint carries in the
        /// opposite direction.
        ///
        /// Everyone holding the rank moves to the default member rank first. A
        /// member left pointing at a deleted rank makes the client's own
        /// <c>TryGetRank</c> throw, and that throw destroys the whole Social Sheet.
        /// </summary>
        private JObject DeleteRank(Guid actor, string rawRank)
        {
            if (!Guid.TryParse(rawRank, out Guid rankId))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceRankRecord? stored = alliances.FindRank(rankId);
            if (stored == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayDeleteRank(
                ledger, Key(actor), AllianceWire.Uid(rankId));
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            AllianceRankRecord? fallback = null;
            foreach (AllianceRankRecord rank in alliances.RanksOf(stored.AllianceId))
            {
                if (!rank.Editable && rank.RankType == AllianceRank.TypeMember) fallback = rank;
            }

            if (fallback == null)
            {
                // No default member rank to fall back to. Deleting would strand
                // every holder on an id the client throws on, so it is refused.
                return SocialEnvelope.Error(SocialErrorCodes.NoRanksFoundInAlliance);
            }

            DateTimeOffset now = clock();
            foreach (AllianceMemberRecord member in alliances.MembersOf(stored.AllianceId))
            {
                if (member.RankId != rankId) continue;
                alliances.SaveMember(member with { RankId = fallback.RankId, UpdatedAt = now });
            }

            alliances.DeleteRank(rankId);
            return SocialEnvelope.Ok(new JObject { ["uid"] = AllianceWire.Uid(rankId) });
        }

        // -------------------------------------------- shared membership-change

        /// <summary>
        /// POST memberships/join - an APPLICATION to an alliance.
        ///
        /// Same body shape as an invite, minus <c>inviter</c>: the client's
        /// <c>PlayerSendApplicationToAlliance</c> passes an empty inviter, and
        /// <c>CreateInviteOrApplicationPayload</c> then OMITS the key entirely. A
        /// null inviter is not "unknown", it is the client's structural
        /// discriminator between an application and an invite
        /// (<c>CheckMembershipRequestType</c>), so it is stored null and emitted
        /// null.
        /// </summary>
        private JObject Apply(Guid actor, string? body)
        {
            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            string? rawTarget = payload.Value<string>("targetId");
            if (!Guid.TryParse(rawTarget, out Guid allianceUid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            // The applicant is the authenticated caller, whatever the body says.
            string? claimed = payload.Value<string>("character");
            if (claimed != null && (!Guid.TryParse(claimed, out Guid claimedUid) || claimedUid != actor))
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            string allianceId = AllianceWire.Uid(allianceUid);
            AllianceLedger ledger = Hydrate();

            AllianceVerdict verdict = AlliancePolicy.MayApply(ledger, Key(actor), allianceId);
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            DateTimeOffset now = clock();
            SocialInviteRecord application = new SocialInviteRecord(
                InviteId: "invite:" + Guid.NewGuid().ToString("D"),
                TargetId: allianceId,
                TargetType: SocialTargetType.Alliance,
                CharacterUid: actor,
                InviterUid: null,
                Message: payload.Value<string>("message") ?? string.Empty,
                Status: SocialInviteStatus.New,
                CreatedAt: now,
                UpdatedAt: now);

            return invites.TryInsert(application)
                ? SocialEnvelope.Ok(WireRequest(application))
                : SocialEnvelope.Error(SocialErrorCodes.ExistingInvite);
        }

        /// <summary>
        /// The alliance branch of POST memberships/invite - the shared endpoint
        /// <see cref="SocialService"/> owns, delegating here when
        /// <c>targetType</c> is <c>alliance_member</c>.
        /// </summary>
        internal JObject SendInvite(Guid actor, JObject payload)
        {
            string? rawTarget = payload.Value<string>("targetId");
            string? rawInvitee = payload.Value<string>("character");

            if (!Guid.TryParse(rawTarget, out Guid allianceUid) || !Guid.TryParse(rawInvitee, out Guid invitee))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            if (invitee == actor) return SocialEnvelope.Error(SocialErrorCodes.SelfInvite);
            if (nameOf(invitee) == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            string allianceId = AllianceWire.Uid(allianceUid);
            AllianceLedger ledger = Hydrate();

            AllianceVerdict verdict = AlliancePolicy.MayInvite(ledger, Key(actor), Key(invitee));
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            // The client derives targetId from its own cached alliance data; a
            // mismatch means it is acting on an alliance it is no longer in.
            Alliance? mine = ledger.AllianceOf(Key(actor));
            if (mine == null || !string.Equals(mine.Id, allianceId, StringComparison.Ordinal))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityPair);
            }

            DateTimeOffset now = clock();
            SocialInviteRecord invite = new SocialInviteRecord(
                InviteId: "invite:" + Guid.NewGuid().ToString("D"),
                TargetId: allianceId,
                TargetType: SocialTargetType.Alliance,
                CharacterUid: invitee,
                InviterUid: actor,
                Message: payload.Value<string>("message") ?? string.Empty,
                Status: SocialInviteStatus.New,
                CreatedAt: now,
                UpdatedAt: now);

            return invites.TryInsert(invite)
                ? SocialEnvelope.Ok(WireRequest(invite))
                : SocialEnvelope.Error(SocialErrorCodes.ExistingInvite);
        }

        /// <summary>
        /// Seats somebody whose alliance invite or application has just been
        /// accepted. Called by <see cref="SocialService"/> from the shared
        /// accept endpoint.
        ///
        /// The join is re-checked here rather than trusted from when the offer was
        /// made: an alliance can fill up, dissolve, or take the player in by the
        /// other route in between.
        /// </summary>
        internal JObject Accept(SocialInviteRecord invite)
        {
            if (!Guid.TryParse(invite.TargetId, out Guid allianceUid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            AllianceLedger ledger = Hydrate();
            AllianceVerdict verdict = AlliancePolicy.MayJoin(
                ledger, Key(invite.CharacterUid), invite.TargetId);
            if (verdict != AllianceVerdict.Ok) return SocialEnvelope.Error(VerdictCode(verdict));

            AllianceRankRecord? memberRank = null;
            foreach (AllianceRankRecord rank in alliances.RanksOf(allianceUid))
            {
                if (!rank.Editable && rank.RankType == AllianceRank.TypeMember) memberRank = rank;
            }

            if (memberRank == null)
            {
                // Joining onto a rank that does not exist would put the new member
                // one refresh away from throwing the client's rank lookup.
                return SocialEnvelope.Error(SocialErrorCodes.NoRanksFoundInAlliance);
            }

            IReadOnlyList<AllianceMemberRecord> existing = alliances.MembersOf(allianceUid);
            int joinOrder = 0;
            foreach (AllianceMemberRecord member in existing)
            {
                if (member.JoinOrder >= joinOrder) joinOrder = member.JoinOrder + 1;
            }

            DateTimeOffset now = clock();
            alliances.SaveMember(new AllianceMemberRecord(
                invite.CharacterUid, allianceUid, memberRank.RankId,
                string.Empty, string.Empty, joinOrder, now, now));

            return SocialEnvelope.OkNoData();
        }

        /// <summary>
        /// May this player answer an alliance's incoming applications and rescind
        /// its outgoing invites - i.e. act FOR the alliance rather than for
        /// themselves?
        ///
        /// Asked by <see cref="SocialService"/> from the two shared endpoints that
        /// serve both directions. It is a RANK permission question rather than a
        /// "are you the leader" one, which is precisely why it cannot be answered
        /// beside the crew version: an alliance founder can hand
        /// <c>edit_members</c> to an officer, and the client shows that officer the
        /// APPLICATIONS tab on the strength of it
        /// (YourAllianceManagementButtons.SetForPermissions). A server that only
        /// let the founder accept would show a button that always failed.
        /// </summary>
        internal bool MayAdmit(Guid actor, string allianceId)
        {
            AllianceLedger ledger = Hydrate();
            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null || !alliance.Holds(Key(actor))) return false;
            if (alliance.IsLeader(Key(actor))) return true;

            AllianceRank? rank = alliance.RankOf(Key(actor));
            return rank != null && rank.Grants(AlliancePermissions.EditMembers);
        }

        // -------------------------------------------------------- shared reads

        /// <summary>
        /// The <c>alliance</c> key inside <c>memberships/character/{uid}</c>, or
        /// null when this character is in none.
        ///
        /// Null means the key is OMITTED, not emitted as a JSON null - that absence
        /// is the only way the client is told "you have no alliance", and it
        /// short-circuits <c>GetYourBasicAllianceInfo</c> without a second request.
        /// </summary>
        internal JObject? MembershipFor(Guid characterUid)
        {
            AllianceMemberRecord? membership = alliances.MemberOf(characterUid);
            return membership == null ? null : WireMembership(membership);
        }

        /// <summary>
        /// An alliance's name, for the <c>targetName</c> on an invite or
        /// application. Empty when it no longer exists - the client prints it and
        /// has its own fallbacks, and inventing a placeholder would hide a missing
        /// row behind text that looks deliberate.
        /// </summary>
        internal string NameOfAlliance(string allianceId)
        {
            if (!Guid.TryParse(allianceId, out Guid uid)) return string.Empty;
            return alliances.FindAlliance(uid)?.Name ?? string.Empty;
        }

        // --------------------------------------------------------------- helpers

        private JObject Wire(AllianceRecord alliance) => AllianceWire.AllianceData(
            AllianceWire.Uid(alliance.AllianceId),
            alliance.Region,
            alliance.Name,
            alliance.Description,
            alliance.MessageOfTheDay,
            SocialWire.Uid(alliance.LeaderUid),
            nameOf(alliance.LeaderUid) ?? string.Empty,
            // The COLUMN holds a marker ("wareborn:emblem:<code>"), an operator's
            // hand-set external URL, or nothing; the WIRE always gets an absolute
            // image URL. Resolving here rather than at save time keeps the public
            // host name in configuration instead of baked into every row, and
            // gives an alliance that never opened the builder a crest of its own
            // rather than the client's shared grey placeholder. See
            // WorldsAdriftServer.Emblems.EmblemUrlPolicy. The ORIGIN comes from
            // the request being answered rather than from configuration, because
            // the game client's TLS stack tops out at TLS 1.0 and cannot fetch an
            // https crest at all - see Emblems.EmblemOrigin.
            Emblems.EmblemUrlPolicy.Resolve(
                emblemBaseUrl, alliance.AllianceId, alliance.EmblemUrl),
            alliances.MembersOf(alliance.AllianceId).Count,
            alliance.CreatedAt,
            alliance.UpdatedAt);

        private JObject Wire(AllianceRankRecord rank) => AllianceWire.RankData(
            AllianceWire.Uid(rank.AllianceId),
            AllianceWire.Uid(rank.RankId),
            rank.Name,
            rank.Editable,
            rank.RankType,
            AllianceWire.UnpackPermissions(rank.Permissions));

        /// <summary>
        /// One membership, with the rank id RESOLVED against the ranks that will
        /// actually be served.
        ///
        /// A stored rank id that no longer exists falls back to the default member
        /// rank. That is not tidiness: <c>AllianceClient.TryGetRank</c> throws
        /// <c>AllianceRankNotFoundException</c> on a rank id absent from
        /// <c>ranks/{allianceUid}</c>, and the throw lands in the shared
        /// alliance-and-crew handler - so one stale row takes out the entire Social
        /// Sheet, crew tab included. The sheet must not be destructible by data.
        /// </summary>
        private JObject WireMembership(AllianceMemberRecord member)
        {
            Guid rankId = member.RankId;

            IReadOnlyList<AllianceRankRecord> ranks = alliances.RanksOf(member.AllianceId);
            bool resolves = false;
            AllianceRankRecord? fallback = null;

            foreach (AllianceRankRecord rank in ranks)
            {
                if (rank.RankId == rankId) resolves = true;
                if (!rank.Editable && rank.RankType == AllianceRank.TypeMember) fallback = rank;
            }

            if (!resolves && fallback != null) rankId = fallback.RankId;

            return AllianceWire.AllianceMembership(
                SocialWire.Uid(member.CharacterUid),
                nameOf(member.CharacterUid) ?? string.Empty,
                AllianceWire.Uid(member.AllianceId),
                AllianceWire.Uid(rankId),
                member.OfficerNote,
                member.PrivateOfficerNote,
                member.CreatedAt,
                member.UpdatedAt);
        }

        private JObject WireRequest(SocialInviteRecord invite) => SocialWire.ChangeRequest(
            invite.InviteId,
            invite.TargetId,
            NameOfAlliance(invite.TargetId),
            invite.TargetType,
            SocialWire.Uid(invite.CharacterUid),
            nameOf(invite.CharacterUid) ?? string.Empty,
            invite.InviterUid.HasValue ? SocialWire.Uid(invite.InviterUid.Value) : null,
            invite.InviterUid.HasValue ? nameOf(invite.InviterUid.Value) ?? string.Empty : null,
            invite.Message,
            invite.Status,
            invite.CreatedAt,
            invite.UpdatedAt);

        private static IEnumerable<string?> Permissions(JObject payload)
        {
            if (payload["permissions"] is not JArray requested) yield break;

            foreach (JToken token in requested) yield return token.Value<string>();
        }

        /// <summary>
        /// The whole alliance ledger, rebuilt from the store.
        ///
        /// Whole rather than scoped because the questions are not local: "is this
        /// name free" spans every alliance, and "may A invite B" depends on B's
        /// alliance as much as A's. A community server holds tens of these.
        /// </summary>
        private AllianceLedger Hydrate() => AllianceLedgerBuilder.Build(alliances, invites);


        /// <summary>
        /// A fresh alliance id that the ledger does not already hold.
        ///
        /// A GUID collides with nothing in practice, so the loop is a formality -
        /// but it is the SHAPE the crew side had to be corrected into after a bare
        /// counter silently overwrote a restored crew, and it is checked against
        /// the LEDGER, which is what a create would actually collide with, rather
        /// than against a remembered high-water mark this process happens to hold.
        /// </summary>
        private static Guid NewIdNotIn(AllianceLedger ledger)
        {
            while (true)
            {
                Guid candidate = Guid.NewGuid();
                if (ledger.ById(AllianceWire.Uid(candidate)) == null) return candidate;
            }
        }

        /// <summary>
        /// The ledger's key form for a character - the same durable string the
        /// crew ledger, the inventory and progression use, so a player is one
        /// player across all of them.
        /// </summary>
        internal static string Key(Guid uid) => "character:" + uid.ToString("D");

        internal static Guid? UidFromKey(string key)
        {
            int colon = key.LastIndexOf(':');
            string tail = colon >= 0 ? key.Substring(colon + 1) : key;
            return Guid.TryParse(tail, out Guid uid) ? uid : (Guid?)null;
        }

        /// <summary>
        /// Translates a policy verdict into one of the client's own error codes.
        ///
        /// Lossy in one direction on purpose: <see cref="AlliancePolicy"/> makes
        /// distinctions the client's closed vocabulary has no word for, and
        /// inventing a code does not produce a slightly-wrong message - it prints
        /// "Unknown error code: whatever_we_invented" in a dialog box. Those fall
        /// back to the nearest TRUE statement rather than to a new string.
        /// </summary>
        internal static string VerdictCode(AllianceVerdict verdict) => verdict switch
        {
            AllianceVerdict.NameNotAllowed => SocialErrorCodes.InvalidName,
            AllianceVerdict.NameTaken => SocialErrorCodes.DuplicateAllianceName,
            AllianceVerdict.AlreadyInThisAlliance => SocialErrorCodes.AlreadyAMember,
            AllianceVerdict.AlreadyInAnotherAlliance => SocialErrorCodes.AlreadyInAlliance,
            AllianceVerdict.AtCapacity => SocialErrorCodes.AllianceAtCapacity,
            AllianceVerdict.RequestLimitMet => SocialErrorCodes.InviteLimitMet,
            AllianceVerdict.AlreadyRequested => SocialErrorCodes.ExistingInvite,
            AllianceVerdict.CannotInviteYourself => SocialErrorCodes.SelfInvite,
            AllianceVerdict.NoSuchAlliance => SocialErrorCodes.InvalidEntityId,
            AllianceVerdict.NoSuchRank => SocialErrorCodes.InvalidEntityId,
            AllianceVerdict.UnknownPlayer => SocialErrorCodes.InvalidEntityId,
            AllianceVerdict.RankNotEditable => SocialErrorCodes.UneditableRank,

            // NotPermitted, CannotBootTheLeader and CannotBootYourself are all
            // "you may not do that", and auth_failed is the only word this client
            // has for it. NotInAnAlliance, NotAMember and
            // RankBelongsToAnotherAlliance are all "those two things do not go
            // together", which is what invalid_entity_pair says.
            AllianceVerdict.NotPermitted => SocialErrorCodes.AuthFailed,
            AllianceVerdict.CannotBootTheLeader => SocialErrorCodes.AuthFailed,
            AllianceVerdict.CannotBootYourself => SocialErrorCodes.AuthFailed,

            _ => SocialErrorCodes.InvalidEntityPair,
        };

        private static JObject? TryParse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return JObject.Parse(body);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
