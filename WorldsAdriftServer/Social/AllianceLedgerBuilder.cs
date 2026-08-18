using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// Rebuilds the whole alliance ledger from the stores.
    ///
    /// This was <c>AllianceEndpoints.Hydrate</c> and it moved out unchanged the
    /// moment a SECOND caller appeared - the account page's emblem builder, which
    /// has to ask exactly the same permission questions about exactly the same
    /// alliances. A ledger is not a cheap thing to be almost-right about: it is
    /// what every <see cref="AlliancePolicy"/> answer is computed from, so a
    /// second hydration that skipped, say, the live invites would have produced a
    /// server where one surface believed a player was permitted and the other did
    /// not. One builder, both callers.
    ///
    /// Whole rather than scoped because the questions are not local: "is this name
    /// free" spans every alliance, and "may A invite B" depends on B's alliance as
    /// much as A's. A community server holds tens of these.
    /// </summary>
    internal static class AllianceLedgerBuilder
    {
        internal static AllianceLedger Build(IAllianceStore alliances, ISocialInviteStore invites)
        {
            if (alliances == null) throw new ArgumentNullException(nameof(alliances));
            if (invites == null) throw new ArgumentNullException(nameof(invites));

            AllianceLedger ledger = new AllianceLedger();

            Dictionary<Guid, List<AllianceRankRecord>> ranksByAlliance = new();
            foreach (AllianceRankRecord rank in alliances.AllRanks())
            {
                if (!ranksByAlliance.TryGetValue(rank.AllianceId, out List<AllianceRankRecord>? bucket))
                {
                    bucket = new List<AllianceRankRecord>();
                    ranksByAlliance[rank.AllianceId] = bucket;
                }

                bucket.Add(rank);
            }

            HashSet<Guid> built = new HashSet<Guid>();
            foreach (AllianceRecord alliance in alliances.AllAlliances())
            {
                if (!ranksByAlliance.TryGetValue(alliance.AllianceId, out List<AllianceRankRecord>? ranks))
                {
                    // An alliance with no ranks cannot be represented - the ledger
                    // needs both defaults to answer any permission question - and
                    // it cannot be opened by the client either. Skipped rather than
                    // half-built, so it behaves as "no such alliance" everywhere at
                    // once instead of differently per endpoint.
                    continue;
                }

                AllianceRank? leaderRank = null;
                AllianceRank? memberRank = null;
                List<AllianceRank> others = new List<AllianceRank>();

                foreach (AllianceRankRecord rank in ranks)
                {
                    AllianceRank pure = Pure(rank);
                    if (pure.IsDefaultLeader) leaderRank = pure;
                    else if (pure.IsDefaultMember) memberRank = pure;
                    else others.Add(pure);
                }

                if (leaderRank == null || memberRank == null) continue;

                Alliance seated = ledger.Create(
                    AllianceWire.Uid(alliance.AllianceId),
                    AllianceEndpoints.Key(alliance.LeaderUid),
                    alliance.Name,
                    leaderRank,
                    memberRank);

                foreach (AllianceRank other in others) seated.AddRank(other);
                built.Add(alliance.AllianceId);
            }

            foreach (AllianceMemberRecord member in alliances.AllMembers())
            {
                if (!built.Contains(member.AllianceId)) continue;

                ledger.Join(
                    AllianceEndpoints.Key(member.CharacterUid),
                    AllianceWire.Uid(member.AllianceId),
                    AllianceWire.Uid(member.RankId));
            }

            // Live offers, without which the ledger is only half the truth: nothing
            // could count how many seats an alliance has already promised, and a
            // player could hold two live applications to the same one.
            foreach (SocialInviteRecord invite in invites.AllLive())
            {
                if (invite.TargetType != SocialTargetType.Alliance) continue;
                if (ledger.ById(invite.TargetId) == null) continue;

                ledger.Request(AllianceEndpoints.Key(invite.CharacterUid), invite.TargetId);
            }

            return ledger;
        }

        private static AllianceRank Pure(AllianceRankRecord rank) => new AllianceRank(
            AllianceWire.Uid(rank.RankId),
            rank.Name,
            rank.Editable,
            rank.RankType,
            AllianceWire.UnpackPermissions(rank.Permissions));
    }
}
