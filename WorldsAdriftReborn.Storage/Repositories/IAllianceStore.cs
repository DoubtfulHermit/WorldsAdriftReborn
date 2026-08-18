using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Everything the alliance endpoints need from storage, as a port.
    ///
    /// This interface exists for one reason, and it is a test reason worth stating
    /// plainly. The crew half of this API was tested through
    /// <c>SocialServiceTests</c>, which take the concrete Postgres-backed
    /// repositories and are therefore <c>[PostgresFact]</c> - skipped on any
    /// machine without a database, which is most of them. Two shipped defects
    /// survived exactly that gap. The alliance rules and the alliance wire shapes
    /// are the part most likely to be subtly wrong, so they must be assertable on a
    /// machine with no database at all, against the REAL route parser and the REAL
    /// endpoint code rather than a re-implementation of it.
    ///
    /// So this is a port with two adapters: <see cref="AllianceRepository"/> for
    /// production, and an in-memory one in the test project. The Postgres suite
    /// still runs the constraint tests against the real server, because the
    /// constraints - one alliance per character, one name, one default rank of each
    /// kind - are the design and a fake that accepted what Postgres refuses would
    /// let a broken contract pass green.
    /// </summary>
    public interface IAllianceStore
    {
        /// <summary>Every alliance. Read whole to rebuild the ledger, because the
        /// rules are not local: "is this name free" spans all of them.</summary>
        IReadOnlyList<AllianceRecord> AllAlliances();

        /// <summary>Every rank, ordered so the founder sees them as they were built.</summary>
        IReadOnlyList<AllianceRankRecord> AllRanks();

        /// <summary>Every membership, ordered so join order - and therefore
        /// succession - is restored exactly.</summary>
        IReadOnlyList<AllianceMemberRecord> AllMembers();

        /// <summary>One alliance, or null. Null is a normal answer: a client can
        /// hold an alliance id from before it was disbanded.</summary>
        AllianceRecord? FindAlliance(Guid allianceId);

        /// <summary>The alliance this character belongs to, or null.</summary>
        AllianceMemberRecord? MemberOf(Guid characterUid);

        /// <summary>One alliance's membership, in join order.</summary>
        IReadOnlyList<AllianceMemberRecord> MembersOf(Guid allianceId);

        /// <summary>One alliance's ranks, in sort order.</summary>
        IReadOnlyList<AllianceRankRecord> RanksOf(Guid allianceId);

        AllianceRankRecord? FindRank(Guid rankId);

        /// <summary>
        /// Inserts a new alliance, or returns false when the name is already
        /// taken.
        ///
        /// The duplicate is caught by the unique index rather than by a
        /// read-then-write in the caller, so two founders racing from two sessions
        /// cannot both pass a check and both insert. False maps to the client's
        /// own <c>duplicate_alliance_name</c>.
        /// </summary>
        bool TryInsertAlliance(AllianceRecord alliance);

        /// <summary>Updates an existing alliance. Its name, region and creation
        /// time are fixed at founding; only the editable fields and the leader
        /// move.</summary>
        void SaveAlliance(AllianceRecord alliance);

        void SaveRank(AllianceRankRecord rank);

        bool DeleteRank(Guid rankId);

        void SaveMember(AllianceMemberRecord member);

        bool RemoveMember(Guid characterUid);

        /// <summary>Dissolves an alliance. Ranks and memberships go with it.</summary>
        bool DeleteAlliance(Guid allianceId);
    }
}
