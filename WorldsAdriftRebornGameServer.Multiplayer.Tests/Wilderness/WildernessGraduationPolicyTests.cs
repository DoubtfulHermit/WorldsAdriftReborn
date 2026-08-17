using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// WHERE THE SHRINE SENDS YOU, asserted natively.
    ///
    /// The draw is injected, so "random" is a stated island here rather than
    /// something to run the server until it happens. Every case the mechanic was
    /// asked for is a test: a solo player lands on a registered Tier-1 island, a
    /// whole crew converges on their leader's island whichever member goes first, a
    /// returning character gets their own island back, and a world with no Tier-1
    /// district refuses instead of teleporting somebody into the void.
    /// </summary>
    public sealed class WildernessGraduationPolicyTests
    {
        private static readonly IReadOnlyList<WildernessDestination> Open =
            WildernessCatalog.Open(
                ReleaseWorldRolloutPolicy.Select("tier1").Select(record => record.Definition));

        private static IslandId IslandAt(int index) => Open[index].IslandId;

        /// <summary>A picker that always answers the same index, and counts its calls.</summary>
        private sealed class FixedPick
        {
            private readonly int _index;
            public FixedPick(int index) => _index = index;
            public int Calls { get; private set; }
            public int Pick(int count)
            {
                Calls++;
                return _index;
            }
        }

        private static Func<int, int> Never() => _ =>
            throw new InvalidOperationException("the draw must not be spent when a home already exists");

        private static WildernessGraduation Decide(
            string actor,
            CrewSnapshot? crew,
            IDictionary<string, IslandId> homes,
            Func<int, int> pick,
            IReadOnlyList<WildernessDestination>? open = null)
        {
            return WildernessGraduationPolicy.Decide(actor, crew, open ?? Open,
                uid => homes.TryGetValue(uid, out IslandId island) ? island : (IslandId?)null,
                pick);
        }

        // ---- solo -----------------------------------------------------------

        [Fact]
        public void A_new_solo_player_is_drawn_a_registered_tier_one_island()
        {
            FixedPick draw = new FixedPick(7);

            WildernessGraduation result =
                Decide("alice", null, new Dictionary<string, IslandId>(), draw.Pick);

            Assert.Equal(WildernessVerdict.Granted, result.Verdict);
            Assert.Equal(WildernessSource.FreshSoloIsland, result.Source);
            Assert.Equal(IslandAt(7), result.Destination.IslandId);
            Assert.Contains(result.Destination.IslandId, Open.Select(d => d.IslandId));
            Assert.Equal(new[] { "alice" }, result.RecordFor);
            Assert.Equal(1, draw.Calls);
        }

        [Fact]
        public void A_returning_solo_character_goes_back_to_the_island_they_already_have()
        {
            Dictionary<string, IslandId> homes = new() { ["alice"] = IslandAt(3) };

            WildernessGraduation result = Decide("alice", null, homes, Never());

            Assert.Equal(WildernessSource.OwnHome, result.Source);
            Assert.Equal(IslandAt(3), result.Destination.IslandId);
        }

        /// <summary>
        /// Stickiness is a PROPERTY, not a lucky draw: the same character through
        /// the shrine twice lands on the same rock even though the picker would
        /// happily hand out a different island the second time.
        /// </summary>
        [Fact]
        public void The_shrine_is_sticky_across_uses()
        {
            Dictionary<string, IslandId> homes = new();

            WildernessGraduation first =
                Decide("alice", null, homes, new FixedPick(11).Pick);
            foreach (string uid in first.RecordFor) homes[uid] = first.Destination.IslandId;
            WildernessGraduation second =
                Decide("alice", null, homes, new FixedPick(2).Pick);

            Assert.Equal(first.Destination.IslandId, second.Destination.IslandId);
            Assert.Equal(WildernessSource.OwnHome, second.Source);
        }

        /// <summary>
        /// A home on an island this boot did not register is treated as absent, not
        /// as an error and not as a destination. Sending somebody to terrain that
        /// was never spawned is the exact fall this whole path exists to prevent.
        /// </summary>
        [Fact]
        public void A_home_on_an_island_that_is_not_registered_tonight_is_ignored()
        {
            Dictionary<string, IslandId> homes = new() { ["alice"] = IslandAt(30) };
            IReadOnlyList<WildernessDestination> narrow = Open.Take(4).ToArray();
            FixedPick draw = new FixedPick(1);

            WildernessGraduation result = Decide("alice", null, homes, draw.Pick, narrow);

            Assert.Equal(WildernessSource.FreshSoloIsland, result.Source);
            Assert.Equal(narrow[1].IslandId, result.Destination.IslandId);
        }

        // ---- crews ----------------------------------------------------------

        private static CrewSnapshot Crew(string leader, params string[] members) =>
            new CrewSnapshot("crew-1", leader, members);

        [Fact]
        public void A_crew_member_joins_the_leaders_island()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob", "carol");
            Dictionary<string, IslandId> homes = new() { ["leader"] = IslandAt(5) };

            WildernessGraduation result = Decide("bob", crew, homes, Never());

            Assert.Equal(WildernessSource.CrewLeaderHome, result.Source);
            Assert.Equal(IslandAt(5), result.Destination.IslandId);
            Assert.Equal(new[] { "bob" }, result.RecordFor);
        }

        /// <summary>
        /// The heart of it. Whichever member touches the shrine first, and in
        /// whatever order the rest follow, the crew ends up on ONE island. Run for
        /// every possible first-mover so the property cannot hold by accident.
        /// </summary>
        [Theory]
        [InlineData("leader")]
        [InlineData("bob")]
        [InlineData("carol")]
        [InlineData("dave")]
        public void A_whole_crew_converges_on_one_island_whoever_goes_first(string firstMover)
        {
            string[] members = { "leader", "bob", "carol", "dave" };
            CrewSnapshot crew = Crew("leader", members);
            Dictionary<string, IslandId> homes = new();
            FixedPick draw = new FixedPick(19);

            WildernessGraduation first = Decide(firstMover, crew, homes, draw.Pick);
            foreach (string uid in first.RecordFor) homes[uid] = first.Destination.IslandId;

            Assert.Equal(WildernessSource.FreshCrewIsland, first.Source);
            // A fresh crew island is recorded for EVERYONE, so nobody is left to
            // draw a second island later.
            Assert.Equal(members.OrderBy(uid => uid, StringComparer.Ordinal),
                first.RecordFor.OrderBy(uid => uid, StringComparer.Ordinal));

            foreach (string uid in members.Where(uid => uid != firstMover))
            {
                WildernessGraduation later = Decide(uid, crew, homes, Never());
                Assert.Equal(first.Destination.IslandId, later.Destination.IslandId);
            }
            Assert.Equal(1, draw.Calls);
        }

        /// <summary>
        /// Clause 2: the leader has no home, so the EARLIEST-JOINED member who has
        /// one supplies the crew's island. Carol joined before dave, so carol's
        /// island wins even though dave also has one.
        /// </summary>
        [Fact]
        public void With_no_leader_home_the_earliest_joined_member_with_one_supplies_it()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob", "carol", "dave");
            Dictionary<string, IslandId> homes = new()
            {
                ["carol"] = IslandAt(8),
                ["dave"] = IslandAt(9),
            };

            WildernessGraduation result = Decide("bob", crew, homes, Never());

            Assert.Equal(WildernessSource.CrewMemberHome, result.Source);
            Assert.Equal(IslandAt(8), result.Destination.IslandId);
        }

        /// <summary>
        /// A promoted successor leads but keeps their JOIN position in the member
        /// list, and clause 1 asks for the leader BY NAME. So bob - promoted, but
        /// second in the list - still supplies the island ahead of carol.
        /// </summary>
        [Fact]
        public void A_promoted_leader_is_asked_by_name_not_by_list_position()
        {
            CrewSnapshot crew = Crew("bob", "bob", "carol");
            Dictionary<string, IslandId> homes =
                new() { ["bob"] = IslandAt(12), ["carol"] = IslandAt(13) };

            WildernessGraduation result = Decide("carol", crew, homes, Never());

            Assert.Equal(WildernessSource.CrewLeaderHome, result.Source);
            Assert.Equal(IslandAt(12), result.Destination.IslandId);
        }

        /// <summary>
        /// Crew coherence BEATS a member's own earlier home. This is the one place
        /// the rule deliberately overrides stickiness, and it is what the mechanic
        /// was asked for: you end up where your crew is.
        /// </summary>
        [Fact]
        public void A_crew_member_with_their_own_island_still_goes_to_the_crews()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob");
            Dictionary<string, IslandId> homes =
                new() { ["leader"] = IslandAt(1), ["bob"] = IslandAt(2) };

            WildernessGraduation result = Decide("bob", crew, homes, Never());

            Assert.Equal(WildernessSource.CrewLeaderHome, result.Source);
            Assert.Equal(IslandAt(1), result.Destination.IslandId);
        }

        /// <summary>
        /// The tie-break that makes "record it for the whole crew" safe: a fresh
        /// crew island can only be drawn when NOBODY had one, so writing it for
        /// every member can never overwrite an existing Wilderness home.
        /// </summary>
        [Fact]
        public void A_fresh_crew_island_is_only_drawn_when_no_member_has_one()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob", "carol");

            foreach (string holder in new[] { "leader", "bob", "carol" })
            {
                Dictionary<string, IslandId> homes = new() { [holder] = IslandAt(4) };
                WildernessGraduation result = Decide("carol", crew, homes, Never());
                Assert.NotEqual(WildernessSource.FreshCrewIsland, result.Source);
                Assert.Equal(IslandAt(4), result.Destination.IslandId);
            }
        }

        [Fact]
        public void A_leader_whose_home_is_closed_falls_through_to_the_next_member()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob");
            IReadOnlyList<WildernessDestination> narrow = Open.Take(3).ToArray();
            Dictionary<string, IslandId> homes = new()
            {
                ["leader"] = IslandAt(40),
                ["bob"] = narrow[2].IslandId,
            };

            WildernessGraduation result = Decide("leader", crew, homes, Never(), narrow);

            Assert.Equal(WildernessSource.CrewMemberHome, result.Source);
            Assert.Equal(narrow[2].IslandId, result.Destination.IslandId);
        }

        [Fact]
        public void An_actor_missing_from_the_member_list_is_still_recorded()
        {
            CrewSnapshot crew = Crew("leader", "leader", "bob");

            WildernessGraduation result =
                Decide("stranger", crew, new Dictionary<string, IslandId>(), new FixedPick(0).Pick);

            Assert.Equal(WildernessSource.FreshCrewIsland, result.Source);
            Assert.Equal(new[] { "stranger", "leader", "bob" }, result.RecordFor);
        }

        // ---- refusals -------------------------------------------------------

        [Fact]
        public void With_no_tier_one_island_registered_the_shrine_refuses()
        {
            WildernessGraduation result = Decide("alice", null, new Dictionary<string, IslandId>(),
                Never(), Array.Empty<WildernessDestination>());

            Assert.Equal(WildernessVerdict.WildernessClosed, result.Verdict);
            Assert.False(result.Ok);
            Assert.Empty(result.RecordFor);
            Assert.Contains("Wilderness is closed", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_character_with_no_uid_yet_is_refused_rather_than_recorded_under_nothing()
        {
            WildernessGraduation result = Decide("  ", null, new Dictionary<string, IslandId>(), Never());

            Assert.Equal(WildernessVerdict.UnknownCharacter, result.Verdict);
            Assert.Empty(result.RecordFor);
        }

        [Fact]
        public void A_draw_outside_the_list_is_clamped_rather_than_trusted()
        {
            Assert.Equal(Open[^1].IslandId,
                Decide("alice", null, new Dictionary<string, IslandId>(), _ => 9999)
                    .Destination.IslandId);
            Assert.Equal(Open[0].IslandId,
                Decide("alice", null, new Dictionary<string, IslandId>(), _ => -5)
                    .Destination.IslandId);
        }

        [Fact]
        public void Every_granted_outcome_carries_a_sentence_naming_the_island()
        {
            WildernessGraduation result =
                Decide("alice", null, new Dictionary<string, IslandId>(), new FixedPick(0).Pick);

            Assert.Contains(result.Destination.DisplayName, result.Message, StringComparison.Ordinal);
        }

        // ---- homes read out of the stored logout position --------------------

        /// <summary>
        /// "Home" is not a new table: it is the Tier-1 island the character's
        /// already-persisted logout position sits on. Round-tripping a destination
        /// through that reader is what makes graduation stick for free on the next
        /// login.
        /// </summary>
        [Fact]
        public void A_stored_position_at_a_landing_point_reads_back_as_that_island()
        {
            WildernessDestination destination = Open[6];

            Assert.Equal(destination.IslandId,
                WildernessGraduationPolicy.HomeIslandOf(destination.Position, Open));
        }

        [Fact]
        public void A_stored_position_in_open_sky_is_not_a_home()
        {
            Assert.Null(WildernessGraduationPolicy.HomeIslandOf(
                FixedPointPosition.FromMetres(0, 3000, 0), Open));
        }

        [Fact]
        public void A_stored_position_on_haven_is_not_a_wilderness_home()
        {
            Assert.Null(WildernessGraduationPolicy.HomeIslandOf(
                SpawnPolicy.PlayerSpawnPosition, Open));
        }

        [Fact]
        public void Nothing_stored_is_not_a_home()
        {
            Assert.Null(WildernessGraduationPolicy.HomeIslandOf(null, Open));
        }

        /// <summary>
        /// End to end over the pure surface: graduate, write the destination where
        /// the position store would, and read it back as the same island. This is
        /// the seam a live server closes with character_positions.
        /// </summary>
        [Fact]
        public void A_recorded_destination_reads_back_as_the_home_that_produced_it()
        {
            Dictionary<string, FixedPointPosition> stored = new();
            WildernessGraduation first = WildernessGraduationPolicy.Decide("alice", null, Open,
                uid => stored.TryGetValue(uid, out FixedPointPosition where)
                    ? WildernessGraduationPolicy.HomeIslandOf(where, Open)
                    : null,
                new FixedPick(21).Pick);
            foreach (string uid in first.RecordFor) stored[uid] = first.Destination.Position;

            WildernessGraduation second = WildernessGraduationPolicy.Decide("alice", null, Open,
                uid => stored.TryGetValue(uid, out FixedPointPosition where)
                    ? WildernessGraduationPolicy.HomeIslandOf(where, Open)
                    : null,
                Never());

            Assert.Equal(WildernessSource.OwnHome, second.Source);
            Assert.Equal(first.Destination.IslandId, second.Destination.IslandId);
        }
    }
}
