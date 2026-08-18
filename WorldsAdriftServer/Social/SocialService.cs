using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Crew;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The social API's behaviour: a resolved route plus an authorised caller in,
    /// a response envelope out.
    ///
    /// Deliberately not a handler. It takes repositories and a clock rather than
    /// an HttpRequest, so every endpoint in the reconstructed contract can be
    /// exercised without a socket - which matters because the contract was
    /// recovered by reading a decompiler's output and the shapes are the part most
    /// likely to be subtly wrong.
    ///
    /// The crew RULES are not reimplemented here. CrewPolicy and CrewLedger in
    /// WorldsAdriftRebornGameServer.Multiplayer already own "who may boot whom",
    /// "who succeeds a leaving leader" and the capacity bounds, they are unit
    /// tested there, and the game server drives the same crews through them over
    /// SpatialOS. A second copy of those rules living here would drift, and the
    /// two ends of one crew would start disagreeing about who is in it. So this
    /// class hydrates a ledger out of Postgres, asks the real policy, and writes
    /// the result back.
    /// </summary>
    internal sealed class SocialService
    {
        private readonly CharacterRepository characters;
        private readonly CrewRepository crews;
        private readonly SocialInviteRepository invites;
        private readonly string region;
        private readonly Func<DateTimeOffset> clock;

        internal SocialService(
            CharacterRepository characters,
            CrewRepository crews,
            SocialInviteRepository invites,
            string region,
            Func<DateTimeOffset>? clock = null)
        {
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.crews = crews ?? throw new ArgumentNullException(nameof(crews));
            this.invites = invites ?? throw new ArgumentNullException(nameof(invites));
            this.region = region ?? throw new ArgumentNullException(nameof(region));
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Answers one social request. Never throws for a request-shaped problem;
        /// every refusal comes back as an in-band error envelope, because a thrown
        /// exception would become a 500 and the client discards non-200 bodies.
        /// </summary>
        internal JObject Handle(SocialRoute route, Guid actor, string url, string? body)
        {
            switch (route.Kind)
            {
                case SocialRouteKind.CharacterMemberships:
                    return CharacterMemberships(route.Segments[0]);

                case SocialRouteKind.InvitesForCharacter:
                    return InvitesForCharacter(route.Segments[0]);

                case SocialRouteKind.CrewMembers:
                    return CrewMembers(route.Segments[0]);

                case SocialRouteKind.CrewInvites:
                    return CrewInvites(route.Segments[0]);

                case SocialRouteKind.GetCrew:
                    return GetCrew(route.Segments[1]);

                case SocialRouteKind.CharacterSearch:
                    return CharacterSearch(route.Segments[0]);

                case SocialRouteKind.CreateCrew:
                    return CreateCrew(actor);

                case SocialRouteKind.DisbandCrew:
                    return DisbandCrew(actor, route.Segments[1]);

                case SocialRouteKind.RemoveCrewMember:
                    return RemoveCrewMember(actor, route.Segments[0], route.Segments[1]);

                case SocialRouteKind.SendInvite:
                    return SendInvite(actor, body);

                case SocialRouteKind.AcceptInvite:
                    return ResolveInvite(actor, route.Segments[0], route.Segments[1], accept: true);

                case SocialRouteKind.RejectInvite:
                    return ResolveInvite(actor, route.Segments[0], route.Segments[1], accept: false);

                case SocialRouteKind.CancelInvite:
                    return CancelInvite(actor, route.Segments[0]);

                // Alliances are not implemented, and these two endpoints are the
                // only ones where "not implemented" and the truth coincide: this
                // server hosts no alliances, so an EMPTY LIST is an honest answer
                // rather than a stand-in for one. It leaves the alliance browser
                // rendering correctly and empty instead of throwing a dialog.
                // Every other alliance endpoint is refused - see SocialHandler.
                case SocialRouteKind.ListAlliances:
                    return SocialEnvelope.OkItems(new JArray());

                case SocialRouteKind.SearchAlliances:
                    return SocialEnvelope.OkBareList(new JArray());

                default:
                    return SocialEnvelope.Error(SocialErrorCodes.StoreUnavailable);
            }
        }

        // ---------------------------------------------------------------- reads

        /// <summary>
        /// GET memberships/character/{uid} - the response the whole Social Sheet
        /// hangs off.
        ///
        /// An absent <c>crew</c> or <c>alliance</c> key is the ONLY way this
        /// client is told "you are not in one". Refusing here, or answering 404,
        /// puts "Can't retrieve alliance or crew data" over the entire sheet
        /// including the crew tab - which is the bug this whole feature exists to
        /// remove.
        /// </summary>
        private JObject CharacterMemberships(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            CharacterRecord? character = characters.Find(uid);
            if (character == null)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            CrewMemberRecord? membership = crews.MemberOf(uid);
            JObject? crew = null;

            if (membership != null)
            {
                CrewRecord? record = crews.FindCrew(membership.CrewId);
                if (record != null)
                {
                    crew = SocialWire.CrewMembership(
                        SocialWire.Uid(uid),
                        character.Name,
                        record.CrewId,
                        membership.CreatedAt,
                        record.UpdatedAt);
                }
            }

            return SocialEnvelope.Ok(SocialWire.PlayerMemberships(
                SocialWire.Uid(uid), character.Name, crew, alliance: null));
        }

        private JObject InvitesForCharacter(string rawUid)
        {
            if (!Guid.TryParse(rawUid, out Guid uid))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            JArray items = new JArray();
            foreach (SocialInviteRecord invite in invites.ForCharacter(uid))
            {
                items.Add(Wire(invite));
            }

            return SocialEnvelope.OkItems(items);
        }

        /// <summary>
        /// GET memberships/crew/{crewUid} - a BARE array at <c>data</c>.
        ///
        /// Its neighbour one method down returns {"items": [...]}. That asymmetry
        /// is in the client (CrewServerImpl.cs:60 vs :76) and reproducing it is
        /// the point; wrapping this one would make every crew look empty.
        /// </summary>
        private JObject CrewMembers(string crewId)
        {
            CrewRecord? crew = crews.FindCrew(crewId);
            if (crew == null)
            {
                // A crew id the client is still holding after a disband. An empty
                // list is truthful and lets the panel fall back to its no-crew
                // state; an error would trap the player in a dialog.
                return SocialEnvelope.OkBareList(new JArray());
            }

            IReadOnlyList<CrewMemberRecord> roster = crews.MembersOf(crewId);

            // Clamped, not trusted. The invite cap stops a crew growing past what
            // the sheet can draw, but rows written before that cap existed do not
            // heal themselves, and the panel is destroyed - not degraded - by one
            // entry too many. See CrewRosterLimits.
            int emit = CrewRosterLimits.EmittableMembers(roster.Count);

            JArray members = new JArray();
            for (int i = 0; i < emit; i++)
            {
                CrewMemberRecord member = roster[i];
                members.Add(SocialWire.CrewMembership(
                    SocialWire.Uid(member.CharacterUid),
                    NameOf(member.CharacterUid),
                    crewId,
                    member.CreatedAt,
                    crew.UpdatedAt));
            }

            return SocialEnvelope.OkBareList(members);
        }

        /// <summary>
        /// GET memberships/invites/crew/{crewUid} - the crew's outstanding offers.
        ///
        /// Resolved invites are filtered out here even though the client filters
        /// again on status == "new" (CrewClient.cs:155), because the crew panel
        /// renders one UI slot per returned entry and a crew that had ever
        /// rejected anyone would otherwise report itself full.
        /// </summary>
        private JObject CrewInvites(string crewId)
        {
            List<SocialInviteRecord> live = new List<SocialInviteRecord>();
            foreach (SocialInviteRecord invite in invites.ForTarget(crewId))
            {
                if (invite.Status != SocialInviteStatus.New) continue;
                live.Add(invite);
            }

            // The client appends these to the member list and draws one widget per
            // entry, so members and invites share one fixed budget. Invites yield
            // first: a truncated pending list is a nuisance, a member missing from
            // their own crew panel is a bug report. See CrewRosterLimits.
            int emit = CrewRosterLimits.EmittableInvites(crews.MembersOf(crewId).Count, live.Count);

            JArray items = new JArray();
            for (int i = 0; i < emit; i++)
            {
                items.Add(Wire(live[i]));
            }

            return SocialEnvelope.OkItems(items);
        }

        private JObject GetCrew(string crewId)
        {
            CrewRecord? crew = crews.FindCrew(crewId);
            if (crew == null)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            return SocialEnvelope.Ok(CrewDataFor(crew));
        }

        /// <summary>
        /// GET screenname/find/{name} - and NOT in the standard envelope.
        ///
        /// The client's CharacterSearchResponseModel extends ResponseSchema, so
        /// <c>screenname</c> sits beside <c>success</c> rather than under
        /// <c>data</c>, and a failure is reported through <c>desc</c> - shown to
        /// the player verbatim - rather than through an errorCode lookup.
        /// </summary>
        private JObject CharacterSearch(string term)
        {
            string name = (term ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return SocialWire.CharacterNotFound("No name was given to search for.");
            }

            CharacterRecord? found = characters.FindByName(name);
            if (found == null || found.IsEmptySlot)
            {
                return SocialWire.CharacterNotFound("No player called " + name + " was found.");
            }

            return SocialWire.CharacterFound(
                SocialWire.Uid(found.CharacterUid), found.Name, found.SlotIndex, found.UpdatedAt);
        }

        // --------------------------------------------------------------- writes

        /// <summary>
        /// POST crews.
        ///
        /// The crew id is minted here as "crew:{guid}". The game server mints
        /// "crew:{n}" from a counter that restarts at 1 every boot, so a shared
        /// numeric space would eventually have the two processes name two
        /// different crews the same thing. A guid tail cannot collide with a
        /// decimal one, which is cheaper than coordinating a sequence between two
        /// processes that do not talk to each other.
        /// </summary>
        private JObject CreateCrew(Guid actor)
        {
            CharacterRecord? character = characters.Find(actor);
            if (character == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            if (crews.MemberOf(actor) != null)
            {
                return SocialEnvelope.Error(SocialErrorCodes.AlreadyAMember);
            }

            DateTimeOffset now = clock();
            string crewId = "crew:" + Guid.NewGuid().ToString("D");

            crews.SaveCrew(new CrewRecord(crewId, actor, CrewPolicy.DefaultSlots, now, now));
            crews.SaveMember(new CrewMemberRecord(actor, crewId, 0, null, now));

            CrewRecord? stored = crews.FindCrew(crewId);
            return stored == null
                ? SocialEnvelope.Error(SocialErrorCodes.StoreUnavailable)
                : SocialEnvelope.Ok(CrewDataFor(stored));
        }

        private JObject DisbandCrew(Guid actor, string crewId)
        {
            CrewRecord? crew = crews.FindCrew(crewId);
            if (crew == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            if (crew.LeaderUid != actor)
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            // Outstanding offers to join something that will not exist a moment
            // from now would otherwise sit in their invitees' lists forever.
            invites.CancelAllForTarget(crewId, clock());
            crews.DeleteCrew(crewId);

            return SocialEnvelope.OkNoData();
        }

        /// <summary>
        /// DELETE memberships/crew/{crewUid}/{characterUid} - both LEAVE and BOOT.
        ///
        /// The client sends the same request for both; which one it is depends
        /// entirely on whether the actor is removing themselves. So the ownership
        /// rule has to be decided here rather than inferred from the URL, and it
        /// is decided by the same CrewPolicy the game server uses.
        ///
        /// Note this one is answered WITH a data field. Its alliance twin is sent
        /// with dataFieldExpected:false but the crew one uses the default
        /// (CrewServerImpl.cs:89), so an empty envelope would throw
        /// "Data in server response was empty" at the player.
        /// </summary>
        private JObject RemoveCrewMember(Guid actor, string crewId, string rawTarget)
        {
            if (!Guid.TryParse(rawTarget, out Guid target))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            CrewLedger ledger = Hydrate();
            string actorKey = LedgerKey(actor);
            string targetKey = LedgerKey(target);

            Crew? crew = ledger.ById(crewId);
            if (crew == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            CrewVerdict verdict = actor == target
                ? CrewPolicy.MayLeave(ledger, actorKey)
                : CrewPolicy.MayBoot(ledger, actorKey, targetKey);

            if (verdict != CrewVerdict.Ok)
            {
                return SocialEnvelope.Error(VerdictCode(verdict));
            }

            // Ask the ledger to work out succession and disband-at-last-member,
            // then mirror whatever it decided into the tables. Doing it in that
            // order means the promotion rule exists in exactly one place.
            bool wasLeader = crew.IsLeader(targetKey);
            ledger.Remove(targetKey);
            crews.RemoveMember(target);

            Crew? after = ledger.ById(crewId);
            if (after == null)
            {
                invites.CancelAllForTarget(crewId, clock());
                crews.DeleteCrew(crewId);
            }
            else if (wasLeader)
            {
                Guid? successor = UidFromKey(after.LeaderUid);
                if (successor.HasValue)
                {
                    CrewRecord current = crews.FindCrew(crewId)!;
                    crews.SaveCrew(current with { LeaderUid = successor.Value, UpdatedAt = clock() });
                }
            }

            return SocialEnvelope.Ok(new JObject { ["removed"] = SocialWire.Uid(target) });
        }

        /// <summary>
        /// POST memberships/invite.
        ///
        /// Body is {targetId, character, targetType, inviter?, message?, region} -
        /// and note <c>character</c> and <c>inviter</c> are plain uid STRINGS on
        /// the way in while the response carries them as {uid,name} objects. Same
        /// names, different shapes per direction; that is the client's contract,
        /// not a simplification of ours.
        /// </summary>
        private JObject SendInvite(Guid actor, string? body)
        {
            JObject? payload = TryParse(body);
            if (payload == null) return SocialEnvelope.Error(SocialErrorCodes.JsonDeserialization);

            string? targetId = payload.Value<string>("targetId");
            string? rawInvitee = payload.Value<string>("character");
            string targetType = payload.Value<string>("targetType") ?? SocialTargetType.Crew;

            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(rawInvitee))
            {
                return SocialEnvelope.Error(SocialErrorCodes.EmptyUpdatePayload);
            }

            if (targetType != SocialTargetType.Crew)
            {
                // Alliance invites have no store here. Refusing is the honest
                // answer; accepting one would create a row nothing can ever act on.
                return SocialEnvelope.Error(SocialErrorCodes.StoreUnavailable);
            }

            if (!Guid.TryParse(rawInvitee, out Guid invitee))
            {
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);
            }

            if (invitee == actor) return SocialEnvelope.Error(SocialErrorCodes.SelfInvite);

            CharacterRecord? inviteeRecord = characters.Find(invitee);
            if (inviteeRecord == null) return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityId);

            CrewLedger ledger = Hydrate();
            CrewVerdict verdict = CrewPolicy.MayInvite(ledger, LedgerKey(actor), LedgerKey(invitee));
            if (verdict != CrewVerdict.Ok)
            {
                return SocialEnvelope.Error(VerdictCode(verdict));
            }

            Crew? crew = ledger.CrewOf(LedgerKey(actor));
            if (crew == null || crew.Id != targetId)
            {
                // The client derives targetId from its own cached crew data; a
                // mismatch means it is acting on a crew it is no longer in.
                return SocialEnvelope.Error(SocialErrorCodes.InvalidEntityPair);
            }

            DateTimeOffset now = clock();
            SocialInviteRecord invite = new SocialInviteRecord(
                InviteId: "invite:" + Guid.NewGuid().ToString("D"),
                TargetId: targetId,
                TargetType: SocialTargetType.Crew,
                CharacterUid: invitee,
                InviterUid: actor,
                Message: payload.Value<string>("message") ?? string.Empty,
                Status: SocialInviteStatus.New,
                CreatedAt: now,
                UpdatedAt: now);

            if (!invites.TryInsert(invite))
            {
                return SocialEnvelope.Error(SocialErrorCodes.ExistingInvite);
            }

            return SocialEnvelope.Ok(Wire(invite));
        }

        /// <summary>
        /// PUT memberships/invite/accept|reject/{inviteUid}/{characterUid}[/{region}].
        ///
        /// Only the INVITEE may answer their own invite, which is why the uid in
        /// the URL is checked against the authorised caller rather than trusted.
        /// </summary>
        private JObject ResolveInvite(Guid actor, string inviteId, string rawCharacter, bool accept)
        {
            if (!Guid.TryParse(rawCharacter, out Guid subject) || subject != actor)
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            SocialInviteRecord? invite = invites.Find(inviteId);
            if (invite == null || invite.CharacterUid != actor)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InviteNotFound);
            }

            if (invite.Status != SocialInviteStatus.New)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InviteNotFound);
            }

            if (!accept)
            {
                invites.Resolve(inviteId, SocialInviteStatus.Rejected, clock());
                return SocialEnvelope.OkNoData();
            }

            CrewLedger ledger = Hydrate();
            string actorKey = LedgerKey(actor);

            // Hydrate loads live invites now, so the offer being accepted is
            // already in the ledger; this used to re-inject it by hand because it
            // was not. Kept as an idempotent assertion rather than removed: the
            // invite was read straight from the store above, and MayAccept
            // answering NoSuchInvite for an invite we are holding in our hand
            // would be a maddening bug to chase.
            ledger.Invite(actorKey, invite.TargetId);

            CrewVerdict verdict = CrewPolicy.MayAccept(ledger, actorKey, invite.TargetId);
            if (verdict != CrewVerdict.Ok)
            {
                return SocialEnvelope.Error(VerdictCode(verdict));
            }

            IReadOnlyList<CrewMemberRecord> existing = crews.MembersOf(invite.TargetId);
            int joinOrder = 0;
            foreach (CrewMemberRecord member in existing)
            {
                if (member.JoinOrder >= joinOrder) joinOrder = member.JoinOrder + 1;
            }

            crews.SaveMember(new CrewMemberRecord(actor, invite.TargetId, joinOrder, null, clock()));
            invites.Resolve(inviteId, SocialInviteStatus.Accepted, clock());

            return SocialEnvelope.OkNoData();
        }

        /// <summary>
        /// PUT memberships/invite/cancel/{inviteUid}/{characterUid}/{region}.
        ///
        /// The client uses this for two different actors: the crew leader
        /// rescinding an offer, and the applicant withdrawing their own
        /// application. Both are allowed; anyone else is not.
        /// </summary>
        private JObject CancelInvite(Guid actor, string inviteId)
        {
            SocialInviteRecord? invite = invites.Find(inviteId);
            if (invite == null || invite.Status != SocialInviteStatus.New)
            {
                return SocialEnvelope.Error(SocialErrorCodes.InviteNotFound);
            }

            bool isInviter = invite.InviterUid == actor;
            bool isInvitee = invite.CharacterUid == actor;

            CrewRecord? crew = crews.FindCrew(invite.TargetId);
            bool isLeader = crew != null && crew.LeaderUid == actor;

            if (!isInviter && !isInvitee && !isLeader)
            {
                return SocialEnvelope.Error(SocialErrorCodes.AuthFailed);
            }

            invites.Resolve(inviteId, SocialInviteStatus.Cancelled, clock());
            return SocialEnvelope.OkNoData();
        }

        // --------------------------------------------------------------- helpers

        private JObject CrewDataFor(CrewRecord crew)
        {
            return SocialWire.CrewData(
                crew.CrewId,
                region,
                NameOf(crew.LeaderUid) is { Length: > 0 } leader ? leader + "'s crew" : crew.CrewId,
                string.Empty,
                SocialWire.Uid(crew.LeaderUid),
                NameOf(crew.LeaderUid),
                crew.CreatedAt,
                crew.UpdatedAt);
        }

        private JObject Wire(SocialInviteRecord invite)
        {
            CrewRecord? crew = invite.TargetType == SocialTargetType.Crew
                ? crews.FindCrew(invite.TargetId)
                : null;

            return SocialWire.ChangeRequest(
                invite.InviteId,
                invite.TargetId,
                crew == null ? string.Empty : NameOf(crew.LeaderUid) + "'s crew",
                invite.TargetType,
                SocialWire.Uid(invite.CharacterUid),
                NameOf(invite.CharacterUid),
                invite.InviterUid.HasValue ? SocialWire.Uid(invite.InviterUid.Value) : null,
                invite.InviterUid.HasValue ? NameOf(invite.InviterUid.Value) : null,
                invite.Message,
                invite.Status,
                invite.CreatedAt,
                invite.UpdatedAt);
        }

        private readonly Dictionary<Guid, string> names = new Dictionary<Guid, string>();

        /// <summary>
        /// A character's display name, or "" when it cannot be resolved.
        ///
        /// Empty rather than a placeholder: the client already has its own
        /// fallbacks for a blank name ("Unable to retrieve", or the raw uid via
        /// CrewMember.DisplayName), and inventing one here would hide a missing
        /// row behind text that looks deliberate.
        /// </summary>
        private string NameOf(Guid uid)
        {
            if (names.TryGetValue(uid, out string? cached)) return cached;

            string name = characters.Find(uid)?.Name ?? string.Empty;
            names[uid] = name;
            return name;
        }

        /// <summary>
        /// The whole crew ledger, hydrated from Postgres.
        ///
        /// Whole rather than scoped because the policy questions are not local -
        /// "may A invite B" depends on B's crew as well as A's - and a community
        /// server holds tens of crews, not millions. It is also exactly what the
        /// game server does at boot, so the two processes reconstruct the same
        /// object from the same rows.
        /// </summary>
        private CrewLedger Hydrate()
        {
            CrewLedger ledger = new CrewLedger();

            Dictionary<string, CrewRecord> byId = new Dictionary<string, CrewRecord>(StringComparer.Ordinal);
            foreach (CrewRecord crew in crews.AllCrews())
            {
                byId[crew.CrewId] = crew;
                ledger.Create(crew.CrewId, LedgerKey(crew.LeaderUid), crew.NumSlots);
            }

            foreach (CrewMemberRecord member in crews.AllMembers())
            {
                if (!byId.TryGetValue(member.CrewId, out CrewRecord? crew)) continue;
                if (crew.LeaderUid == member.CharacterUid) continue;  // Create already seated the leader

                ledger.Join(LedgerKey(member.CharacterUid), member.CrewId);
                if (member.Slot.HasValue) ledger.TakeSlot(LedgerKey(member.CharacterUid), member.Slot.Value);
            }

            // Live invites, without which the ledger is only half the truth. The
            // policy has to count a crew's outstanding offers to stop it offering
            // more seats than the Social Sheet can draw, and it cannot count what
            // was never loaded - so a leader alone in a crew could invite without
            // limit and destroy the panel. This is also why the accept path no
            // longer has to re-inject the invite it is acting on.
            foreach (SocialInviteRecord invite in invites.AllLive())
            {
                if (invite.TargetType != SocialTargetType.Crew) continue;
                if (!byId.ContainsKey(invite.TargetId)) continue;

                ledger.Invite(LedgerKey(invite.CharacterUid), invite.TargetId);
            }

            return ledger;
        }

        /// <summary>
        /// The ledger's key form for a character.
        ///
        /// The game server keys its ledger on "character:{guid}"
        /// (CrewPersistence.Key -> InventoryKey.ForCharacter), and CrewPolicy
        /// compares keys ordinally, so a ledger built here with bare guids would
        /// behave correctly in isolation and disagree with the game server the
        /// moment a crew crossed between them.
        /// </summary>
        internal static string LedgerKey(Guid uid) => "character:" + uid.ToString("D");

        internal static Guid? UidFromKey(string key)
        {
            int colon = key.LastIndexOf(':');
            string tail = colon >= 0 ? key.Substring(colon + 1) : key;
            return Guid.TryParse(tail, out Guid uid) ? uid : (Guid?)null;
        }

        /// <summary>
        /// Translates a CrewPolicy verdict into one of the client's own error
        /// codes.
        ///
        /// The mapping is lossy in one direction on purpose: CrewPolicy has
        /// verdicts the client's closed vocabulary has no word for, and inventing
        /// a code would print "Unknown error code: ..." in a dialog. Those fall
        /// back to the nearest true statement rather than to a new string.
        /// </summary>
        internal static string VerdictCode(CrewVerdict verdict) => verdict switch
        {
            CrewVerdict.CrewIsFull => SocialErrorCodes.CrewAtCapacity,
            CrewVerdict.AlreadyInThisCrew => SocialErrorCodes.AlreadyAMember,
            CrewVerdict.AlreadyInAnotherCrew => SocialErrorCodes.AlreadyAMember,
            CrewVerdict.AlreadyInvited => SocialErrorCodes.ExistingInvite,
            CrewVerdict.InviteLimitMet => SocialErrorCodes.InviteLimitMet,
            CrewVerdict.NoSuchInvite => SocialErrorCodes.InviteNotFound,
            CrewVerdict.NotTheLeader => SocialErrorCodes.AuthFailed,
            CrewVerdict.CannotInviteYourself => SocialErrorCodes.SelfInvite,
            CrewVerdict.UnknownPlayer => SocialErrorCodes.InvalidEntityId,
            CrewVerdict.SlotOutOfRange => SocialErrorCodes.InvalidEntityId,

            // NotInACrew, NotAMember, CannotBootYourself and SlotTaken all mean
            // "those two things do not go together", which is what
            // invalid_entity_pair says. The client has no closer word, and a
            // closer-sounding invented one would print as literal debug text.
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
