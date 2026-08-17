using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The payload shapes, built by hand rather than by serialising our own
    /// records.
    ///
    /// That is deliberate. These are not our data structures; they are the field
    /// sets of the client's shipped DTOs, recovered by reading them
    /// (docs/research/findings-social-api.md, section 4). None of them carries a
    /// [JsonProperty] attribute, so the field names below ARE the wire names,
    /// verbatim and case-sensitive. Serialising a storage record and hoping the
    /// property names line up would couple the wire to our schema, and the first
    /// time somebody renamed a column the crew panel would quietly go blank.
    ///
    /// Everything here is pure: uids and names in, JObject out. No database, no
    /// clock, no request. That is what lets the whole reconstructed contract be
    /// pinned by unit tests.
    /// </summary>
    internal static class SocialWire
    {
        /// <summary>
        /// Timestamps go out as milliseconds since the Unix epoch.
        ///
        /// INFERRED, not proven. The client's DTOs type these as <c>long</c> and
        /// the only consumer that survives parsing is
        /// AllianceBasicInformation.Created, which nothing we serve displays -
        /// so the original unit left no trace in the decompile. Milliseconds is
        /// the convention every other epoch value in this codebase uses. If a
        /// future alliance UI renders a creation date and it reads as 1970,
        /// this is the line to change.
        /// </summary>
        internal static long Epoch(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

        /// <summary>
        /// The universal <c>{uid, name}</c> embed - the client's
        /// NameServerDataModel.
        ///
        /// It appears as <c>member</c>, <c>leaderCharacter</c>, <c>character</c>
        /// and <c>inviter</c>, and in every case the client prefers it over the
        /// flat id field beside it, falling back only when it is null. So filling
        /// it in is what puts a readable name in the UI instead of a raw uid.
        /// </summary>
        internal static JObject NameRef(string uid, string name)
        {
            return new JObject
            {
                ["uid"] = uid,
                ["name"] = name ?? string.Empty,
            };
        }

        /// <summary>
        /// CrewDataModel - the crew itself.
        ///
        /// <paramref name="leaderUid"/> is load-bearing beyond display: the client
        /// decides who the leader is by string-comparing each member's uid against
        /// this one (CrewMember's 7-arg constructor sets
        /// <c>IsLeader = characterId == leaderUid</c>), and then CrewScreen decides
        /// whether YOU are the leader by comparing that against
        /// SocialHelper.MyCharacterUid. Those three strings come from three
        /// different responses of ours, so they must be formatted IDENTICALLY -
        /// which is why every uid on this wire is written by
        /// <see cref="Uid"/> and never by an ad-hoc ToString.
        /// </summary>
        internal static JObject CrewData(
            string crewId,
            string region,
            string name,
            string description,
            string leaderUid,
            string leaderName,
            DateTimeOffset created,
            DateTimeOffset lastUpdated)
        {
            return new JObject
            {
                ["uid"] = crewId,
                ["region"] = region,
                ["name"] = name ?? string.Empty,
                ["description"] = description ?? string.Empty,
                ["leaderCharacterUid"] = leaderUid,
                ["leaderCharacter"] = NameRef(leaderUid, leaderName),
                ["created"] = Epoch(created),
                ["lastUpdated"] = Epoch(lastUpdated),
            };
        }

        /// <summary>
        /// CrewMembershipDataModel - one person's membership of one crew.
        ///
        /// <c>memberId</c> and <c>member.uid</c> carry the same value; the client
        /// reads <c>member.uid</c> when <c>member</c> is present and
        /// <c>memberId</c> otherwise, so both are filled rather than relying on
        /// which branch it takes.
        /// </summary>
        internal static JObject CrewMembership(
            string memberUid,
            string memberName,
            string crewId,
            DateTimeOffset created,
            DateTimeOffset lastUpdated)
        {
            return new JObject
            {
                ["memberId"] = memberUid,
                ["targetId"] = crewId,
                ["lastUpdated"] = Epoch(lastUpdated),
                ["created"] = Epoch(created),
                ["member"] = NameRef(memberUid, memberName),
            };
        }

        /// <summary>
        /// PlayerMembershipModel - "which groups is this character in".
        ///
        /// The single most important response we serve. It is the first request
        /// the Social Sheet makes, every crew read starts from it, and the whole
        /// "you are not in an alliance" state is expressed by it rather than by
        /// any alliance endpoint: the client's GetYourBasicAllianceInfo tests
        /// <c>alliance != null</c> and, when it is null, resolves null WITHOUT
        /// issuing a second request.
        ///
        /// So an absent group is an ABSENT KEY on a 200 with success:true. It is
        /// emphatically not a 404 and not success:false - both of those are
        /// transport errors to this client and put "Can't retrieve alliance or
        /// crew data" over the entire sheet, crew tab included.
        /// </summary>
        internal static JObject PlayerMemberships(
            string characterUid,
            string characterName,
            JObject? crew,
            JObject? alliance)
        {
            JObject model = new JObject
            {
                ["character"] = characterUid,
                ["member"] = NameRef(characterUid, characterName),
            };

            // Written only when present. The client distinguishes "no crew" from
            // "a crew" purely by null-ness of this key.
            if (crew != null) model["crew"] = crew;
            if (alliance != null) model["alliance"] = alliance;

            return model;
        }

        /// <summary>
        /// MembershipChangeRequestDataModel - an invite or an application.
        ///
        /// <paramref name="inviterUid"/> null is not "unknown", it is the
        /// client's own discriminator: CheckMembershipRequestType returns
        /// Application exactly when <c>inviter</c> is null and Invite otherwise.
        /// Sending an inviter object for something the player applied to would
        /// put it in the wrong list.
        ///
        /// <paramref name="status"/> and <paramref name="targetType"/> must come
        /// from the closed vocabularies in SocialInviteStatus / SocialTargetType.
        /// The client THROWS on an unrecognised value rather than skipping the
        /// entry, so one bad row breaks the whole list.
        /// </summary>
        internal static JObject ChangeRequest(
            string inviteId,
            string targetId,
            string targetName,
            string targetType,
            string characterUid,
            string characterName,
            string? inviterUid,
            string? inviterName,
            string message,
            string status,
            DateTimeOffset created,
            DateTimeOffset lastUpdated)
        {
            JObject model = new JObject
            {
                ["id"] = inviteId,
                ["targetId"] = targetId,
                ["targetName"] = targetName ?? string.Empty,
                ["character"] = NameRef(characterUid, characterName),
                ["targetType"] = targetType,
                ["message"] = message ?? string.Empty,
                ["status"] = status,
                ["created"] = Epoch(created),
                ["lastUpdated"] = Epoch(lastUpdated),
            };

            model["inviter"] = inviterUid == null
                ? JValue.CreateNull()
                : NameRef(inviterUid, inviterName ?? string.Empty);

            return model;
        }

        /// <summary>
        /// The character search response - the one endpoint that does NOT use the
        /// standard envelope.
        ///
        /// CharacterSearchResponseModel extends ResponseSchema and adds its own
        /// fields, so <c>screenname</c> is a SIBLING of <c>success</c>, not a
        /// child of <c>data</c>. Getting this wrong would leave
        /// <c>characterSearchResponse.screenname</c> null and NRE the invite flow
        /// one call later.
        ///
        /// Its failure convention is different too: the client surfaces
        /// <c>desc</c> verbatim to the player rather than looking up an
        /// <c>errorCode</c> (SocialRequest.CheckSearchResponseModelForErrors).
        /// </summary>
        internal static JObject CharacterFound(
            string characterUid,
            string name,
            int characterSlot,
            DateTimeOffset lastUpdated)
        {
            return new JObject
            {
                ["success"] = true,
                ["screenname"] = new JObject
                {
                    ["name"] = name,
                    ["displayName"] = name,
                    ["bossaId"] = 0L,
                    ["characterSlot"] = characterSlot,
                    ["lastUpdated"] = Epoch(lastUpdated),
                    ["characterUid"] = characterUid,
                    ["validated"] = true,
                },
                ["status"] = "ok",
            };
        }

        /// <summary>
        /// A failed character search. <paramref name="description"/> is shown to
        /// the player as-is, so it is a sentence, not a code.
        /// </summary>
        internal static JObject CharacterNotFound(string description)
        {
            return new JObject
            {
                ["success"] = false,
                ["status"] = "error",
                ["error"] = "not_found",
                ["desc"] = description,
            };
        }

        /// <summary>
        /// The one place a character uid becomes a string on this wire.
        ///
        /// Lowercase, hyphenated "D" form - the same form CharacterAdapter puts in
        /// the character list, which is where the client got the uid it compares
        /// ours against. The client's leadership and "is this me" checks are
        /// ordinal string comparisons, so a casing difference between two of our
        /// own responses would present as "the crew leader is nobody".
        /// </summary>
        internal static string Uid(Guid uid) => uid.ToString("D");
    }
}
