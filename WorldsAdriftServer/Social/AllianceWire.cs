using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The alliance payload shapes, built by hand for the same reason
    /// <see cref="SocialWire"/>'s are: these are not our data structures, they are
    /// the field sets of the client's shipped DTOs, and none of them carries a
    /// <c>[JsonProperty]</c> - so the names below ARE the wire names, verbatim and
    /// case-sensitive.
    ///
    /// Everything here is pure: values in, JObject out. No database, no clock, no
    /// request. That is what lets the recovered contract be pinned by tests that
    /// need no server at all.
    /// </summary>
    internal static class AllianceWire
    {
        /// <summary>
        /// AllianceDataModel - the alliance itself.
        ///
        /// Emitted by <c>alliance/find/{region}/{characterUid}</c>,
        /// <c>alliance/{region}/{uid}</c>, <c>POST alliance</c>,
        /// <c>PATCH alliance/{region}/{uid}</c>, and as each element of
        /// <c>alliances/{region}</c> and the batch fetch.
        ///
        /// <paramref name="uid"/> must be a bare hyphenated GUID. The client puts
        /// every alliance id it later sends back through
        /// <c>SocialHelper.SanitizeGuid</c>, which throws a FormatException on
        /// anything else - so an id shaped like a crew's would be rejected inside
        /// the client, with no request and no way for the player to tell why.
        ///
        /// <paramref name="emblemUrl"/> is the crest, and it is a URL rather than
        /// an index or a pattern: <c>AllianceClient.GetEmblem</c> hands it to
        /// <c>SpriteDownloader.GetSpriteFromUrl</c>, which does a plain GET with no
        /// auth headers and decodes the bytes as a texture. Empty is normal and
        /// leaves the client's own placeholder sprite in place.
        /// </summary>
        internal static JObject AllianceData(
            string uid,
            string region,
            string name,
            string description,
            string messageOfTheDay,
            string leaderUid,
            string leaderName,
            string emblemUrl,
            int memberCount,
            DateTimeOffset created,
            DateTimeOffset lastUpdated)
        {
            return new JObject
            {
                ["uid"] = uid,
                ["region"] = region,
                ["name"] = name ?? string.Empty,
                ["description"] = description ?? string.Empty,
                ["messageOfTheDay"] = messageOfTheDay ?? string.Empty,
                ["leaderCharacterUid"] = leaderUid,
                ["leaderCharacter"] = SocialWire.NameRef(leaderUid, leaderName),
                ["created"] = SocialWire.Epoch(created),
                ["lastUpdated"] = SocialWire.Epoch(lastUpdated),
                ["emblemUrl"] = emblemUrl ?? string.Empty,
                ["memberCount"] = memberCount,
            };
        }

        /// <summary>
        /// AllianceMembershipDataModel - one person's membership of one alliance.
        ///
        /// This is also the value of <c>alliance</c> inside
        /// <c>memberships/character/{uid}</c>, where <c>targetId</c> is the only
        /// route the client has to its own alliance id
        /// (<c>SocialClient.GetMyMembershipData</c> sets
        /// <c>AllianceUid = memberDataModel.alliance.targetId</c>). An absent or
        /// wrong <c>targetId</c> there does not degrade the panel, it makes every
        /// subsequent alliance call address nothing.
        ///
        /// <paramref name="rankId"/> must name a rank that
        /// <c>ranks/{allianceUid}</c> will also return. <c>AllianceClient.TryGetRank</c>
        /// THROWS <c>AllianceRankNotFoundException</c> on a rank id it cannot find,
        /// and that throw lands in the shared alliance-and-crew exception handler -
        /// so one member pointing at a deleted rank takes out the whole Social
        /// Sheet, crew tab included.
        ///
        /// The two notes are named for the READ side. The client PATCHes the public
        /// one as <c>publicOfficerNote</c> and reads it back as <c>officerNote</c>;
        /// the private one keeps its name in both directions. Then the view model
        /// maps <c>officerNote</c> onto <c>PublicNote</c> and
        /// <c>privateOfficerNote</c> onto <c>OfficerNote</c>
        /// (SocialGroupParsers.cs:109). Three names for two values, and all of it
        /// is retail's.
        /// </summary>
        internal static JObject AllianceMembership(
            string memberUid,
            string memberName,
            string allianceUid,
            string rankId,
            string officerNote,
            string privateOfficerNote,
            DateTimeOffset created,
            DateTimeOffset lastUpdated)
        {
            return new JObject
            {
                ["memberId"] = memberUid,
                ["targetId"] = allianceUid,
                ["rankId"] = rankId,
                ["lastUpdated"] = SocialWire.Epoch(lastUpdated),
                ["created"] = SocialWire.Epoch(created),
                ["member"] = SocialWire.NameRef(memberUid, memberName),
                ["officerNote"] = officerNote ?? string.Empty,
                ["privateOfficerNote"] = privateOfficerNote ?? string.Empty,
            };
        }

        /// <summary>
        /// RankDataModel.
        ///
        /// <paramref name="editable"/> and <paramref name="rankType"/> are read
        /// together and decide more than they look like they do:
        ///
        ///     isDefaultLeaderRank = rankType == "leader" &amp;&amp; !editable
        ///     isDefaultMemberRank = rankType == "member" &amp;&amp; !editable
        ///
        /// (SocialGroupParsers.cs:126-127). <c>AllianceRankInformation.CreateLookup</c>
        /// fills its <c>Leader</c> and <c>BasicMember</c> fields from exactly those
        /// two booleans, so a leader rank sent as editable leaves
        /// <c>rankInfo.Leader</c> null and the founder disappears from their own
        /// alliance.
        ///
        /// <paramref name="target"/> is the alliance uid. The client sends it when
        /// it creates a rank and never reads it back, but it is part of the model
        /// and omitting a field the DTO declares is how a shape drifts.
        /// </summary>
        internal static JObject RankData(
            string target,
            string uid,
            string name,
            bool editable,
            string rankType,
            IReadOnlyList<string> permissions)
        {
            JArray granted = new JArray();
            foreach (string permission in permissions ?? Array.Empty<string>())
            {
                granted.Add(permission);
            }

            return new JObject
            {
                ["target"] = target,
                ["uid"] = uid,
                ["name"] = name ?? string.Empty,
                ["editable"] = editable,
                ["rankType"] = rankType,
                ["membershipType"] = AllianceRank.MembershipType,
                ["permissions"] = granted,
            };
        }

        /// <summary>
        /// The comma-separated storage form of a permission set, and back.
        ///
        /// Sanitised on the way in, not on the way out: an unknown permission
        /// stored is an unknown permission emitted forever, invisible in the UI but
        /// real to any check that spelled it the same way. Filtering at the
        /// boundary keeps what is stored and what a player can see the same set.
        /// </summary>
        internal static string PackPermissions(IEnumerable<string?>? permissions) =>
            string.Join(",", AlliancePermissions.Sanitize(permissions));

        internal static IReadOnlyList<string> UnpackPermissions(string? packed)
        {
            if (string.IsNullOrEmpty(packed)) return Array.Empty<string>();

            return AlliancePermissions.Sanitize(
                packed!.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// The one place an alliance or rank id becomes a string on this wire.
        ///
        /// Lowercase hyphenated "D" form, matching <see cref="SocialWire.Uid"/> -
        /// the client compares ids ordinally across separate responses (its own
        /// alliance id from <c>memberships/character</c> against the one in
        /// <c>alliance/find</c>, its rank id against the rank list), so a casing
        /// difference between two of our own answers presents as "this alliance has
        /// no leader" or as a thrown rank lookup.
        /// </summary>
        internal static string Uid(Guid uid) => uid.ToString("D");
    }
}
