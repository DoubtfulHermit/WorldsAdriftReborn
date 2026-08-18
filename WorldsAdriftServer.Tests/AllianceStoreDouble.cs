using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// An in-memory <see cref="IAllianceStore"/>.
    ///
    /// It exists so the alliance contract can be exercised through the REAL route
    /// parser and the REAL endpoint code on a machine with no database, which is
    /// the gap that let two defects ship on the crew side: those tests take the
    /// concrete Postgres repositories, so they are <c>[PostgresFact]</c> and
    /// skipped almost everywhere.
    ///
    /// It reproduces the three constraints the schema enforces, and ONLY those, so
    /// it cannot quietly accept something the real server would refuse:
    ///
    ///   - one alliance per character (alliance_members' primary key);
    ///   - one alliance per name, case-insensitively (alliances_one_name);
    ///   - deleting an alliance takes its ranks and memberships (the cascades).
    ///
    /// It is not a substitute for the Postgres suite, which still checks that
    /// those constraints exist. It is a substitute for needing a database to ask
    /// what shape a response has.
    /// </summary>
    internal sealed class AllianceStoreDouble : IAllianceStore
    {
        private readonly Dictionary<Guid, AllianceRecord> alliances = new();
        private readonly Dictionary<Guid, AllianceRankRecord> ranks = new();
        private readonly Dictionary<Guid, AllianceMemberRecord> members = new();

        public IReadOnlyList<AllianceRecord> AllAlliances() =>
            alliances.Values.OrderBy(a => a.CreatedAt).ThenBy(a => a.AllianceId).ToList();

        public IReadOnlyList<AllianceRankRecord> AllRanks() =>
            ranks.Values.OrderBy(r => r.AllianceId).ThenBy(r => r.SortOrder).ToList();

        public IReadOnlyList<AllianceMemberRecord> AllMembers() =>
            members.Values.OrderBy(m => m.AllianceId).ThenBy(m => m.JoinOrder).ToList();

        public AllianceRecord? FindAlliance(Guid allianceId) =>
            alliances.TryGetValue(allianceId, out AllianceRecord? found) ? found : null;

        public AllianceMemberRecord? MemberOf(Guid characterUid) =>
            members.TryGetValue(characterUid, out AllianceMemberRecord? found) ? found : null;

        public IReadOnlyList<AllianceMemberRecord> MembersOf(Guid allianceId) =>
            members.Values.Where(m => m.AllianceId == allianceId).OrderBy(m => m.JoinOrder).ToList();

        public IReadOnlyList<AllianceRankRecord> RanksOf(Guid allianceId) =>
            ranks.Values.Where(r => r.AllianceId == allianceId)
                 .OrderBy(r => r.SortOrder).ThenBy(r => r.RankId).ToList();

        public AllianceRankRecord? FindRank(Guid rankId) =>
            ranks.TryGetValue(rankId, out AllianceRankRecord? found) ? found : null;

        public bool TryInsertAlliance(AllianceRecord alliance)
        {
            if (alliances.ContainsKey(alliance.AllianceId)) return false;

            string key = AllianceNamePolicy.UniquenessKey(alliance.Name);
            if (alliances.Values.Any(a => AllianceNamePolicy.UniquenessKey(a.Name) == key)) return false;

            alliances[alliance.AllianceId] = alliance;
            return true;
        }

        public void SaveAlliance(AllianceRecord alliance) => alliances[alliance.AllianceId] = alliance;

        public void SaveRank(AllianceRankRecord rank) => ranks[rank.RankId] = rank;

        public bool DeleteRank(Guid rankId) => ranks.Remove(rankId);

        public void SaveMember(AllianceMemberRecord member) => members[member.CharacterUid] = member;

        public bool RemoveMember(Guid characterUid) => members.Remove(characterUid);

        public bool DeleteAlliance(Guid allianceId)
        {
            if (!alliances.Remove(allianceId)) return false;

            foreach (Guid rankId in ranks.Values.Where(r => r.AllianceId == allianceId)
                                                .Select(r => r.RankId).ToList())
            {
                ranks.Remove(rankId);
            }

            foreach (Guid uid in members.Values.Where(m => m.AllianceId == allianceId)
                                               .Select(m => m.CharacterUid).ToList())
            {
                members.Remove(uid);
            }

            return true;
        }
    }

    /// <summary>
    /// An in-memory <see cref="ISocialInviteStore"/>, reproducing the one
    /// constraint that decides behaviour: the partial unique index that allows at
    /// most ONE LIVE offer per (character, target) while still permitting a fresh
    /// one after a rejection.
    /// </summary>
    internal sealed class InviteStoreDouble : ISocialInviteStore
    {
        private readonly Dictionary<string, SocialInviteRecord> rows = new(StringComparer.Ordinal);

        public SocialInviteRecord? Find(string inviteId) =>
            rows.TryGetValue(inviteId, out SocialInviteRecord? found) ? found : null;

        public IReadOnlyList<SocialInviteRecord> ForCharacter(Guid characterUid) =>
            rows.Values.Where(r => r.CharacterUid == characterUid)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.InviteId, StringComparer.Ordinal).ToList();

        public IReadOnlyList<SocialInviteRecord> ForTarget(string targetId) =>
            rows.Values.Where(r => string.Equals(r.TargetId, targetId, StringComparison.Ordinal))
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.InviteId, StringComparer.Ordinal).ToList();

        public IReadOnlyList<SocialInviteRecord> AllLive() =>
            rows.Values.Where(r => r.Status == SocialInviteStatus.New)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.InviteId, StringComparer.Ordinal).ToList();

        public bool TryInsert(SocialInviteRecord invite)
        {
            bool live = rows.Values.Any(r =>
                r.Status == SocialInviteStatus.New
                && r.CharacterUid == invite.CharacterUid
                && string.Equals(r.TargetId, invite.TargetId, StringComparison.Ordinal));

            if (live) return false;

            rows[invite.InviteId] = invite;
            return true;
        }

        public bool Resolve(string inviteId, string status, DateTimeOffset at)
        {
            if (!rows.TryGetValue(inviteId, out SocialInviteRecord? row)) return false;
            if (row.Status != SocialInviteStatus.New) return false;

            rows[inviteId] = row with { Status = status, UpdatedAt = at };
            return true;
        }

        public int CancelAllForTarget(string targetId, DateTimeOffset at)
        {
            int changed = 0;
            foreach (SocialInviteRecord row in ForTarget(targetId))
            {
                if (row.Status != SocialInviteStatus.New) continue;
                rows[row.InviteId] = row with { Status = SocialInviteStatus.Cancelled, UpdatedAt = at };
                changed++;
            }

            return changed;
        }
    }
}
