using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Operator;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The operator command surface: who a command names, where it sends them,
    /// which ship it brings, and the exact shape of the line that crosses between
    /// the login server and the game server.
    ///
    /// The property every one of these is really defending is the same: an
    /// operator command that cannot be resolved to EXACTLY ONE player must refuse,
    /// never act. These commands move other people's characters, and "it picked the
    /// first match" is indistinguishable from "it moved the wrong person" from the
    /// outside.
    /// </summary>
    public class OperatorTargetPolicyTests
    {
        private static readonly string UidA = "11111111-1111-1111-1111-111111111111";
        private static readonly string UidB = "22222222-2222-2222-2222-222222222222";

        private static OperatorPlayer Player(long entityId, string peerId, string uid) =>
            new OperatorPlayer(entityId, peerId, uid, true, 0, 0, 0);

        [Fact]
        public void A_bare_guid_is_a_durable_character_uid()
        {
            Assert.True(OperatorTargetPolicy.TryParse(UidA, out OperatorTarget target, out _));
            Assert.Equal(OperatorTargetKind.CharacterUid, target.Kind);
            Assert.Equal(UidA, target.Value);
        }

        [Fact]
        public void A_bare_positive_integer_is_an_entity_id_not_a_peer()
        {
            // The stats rows lead with entityId and the old bridge meant entity by
            // an unqualified number. Reading it as a peer handle instead would
            // resolve to a different player on a busy server rather than to nobody.
            Assert.True(OperatorTargetPolicy.TryParse("7", out OperatorTarget target, out _));
            Assert.Equal(OperatorTargetKind.EntityId, target.Kind);
            Assert.Equal("7", target.Value);
        }

        [Theory]
        [InlineData("uid:" + "11111111-1111-1111-1111-111111111111", OperatorTargetKind.CharacterUid)]
        [InlineData("entity:12", OperatorTargetKind.EntityId)]
        [InlineData("peer:0x7F", OperatorTargetKind.PeerId)]
        [InlineData("name:Captain Bligh", OperatorTargetKind.CharacterName)]
        public void Every_prefix_parses_to_its_own_kind(string selector, OperatorTargetKind expected)
        {
            Assert.True(OperatorTargetPolicy.TryParse(selector, out OperatorTarget target, out _));
            Assert.Equal(expected, target.Kind);
        }

        [Fact]
        public void A_bare_word_is_refused_rather_than_read_as_a_name()
        {
            // "Bob" and a mistyped uid look identical here. Guessing "name" makes a
            // typo into a command that moves somebody.
            Assert.False(OperatorTargetPolicy.TryParse("Bob", out _, out string error));
            Assert.Contains("name:", error);
        }

        [Theory]
        [InlineData("entity:0")]
        [InlineData("entity:-3")]
        [InlineData("entity:abc")]
        [InlineData("uid:not-a-guid")]
        [InlineData("peer:0xnothex")]
        [InlineData("mystery:1")]
        [InlineData("")]
        public void Malformed_selectors_refuse_with_a_reason(string selector)
        {
            Assert.False(OperatorTargetPolicy.TryParse(selector, out _, out string error));
            Assert.NotEqual(string.Empty, error);
        }

        [Fact]
        public void Selectors_round_trip_through_their_canonical_form()
        {
            // The wire always carries the prefixed form, so parse(format(x)) == x
            // has to hold or a command means something different at the far end.
            foreach (string selector in new[] { "uid:" + UidA, "entity:9", "peer:0x1f" })
            {
                Assert.True(OperatorTargetPolicy.TryParse(selector, out OperatorTarget parsed, out _));
                Assert.True(OperatorTargetPolicy.TryParse(
                    parsed.ToSelector(), out OperatorTarget again, out _));
                Assert.Equal(parsed, again);
            }
        }

        [Fact]
        public void A_uid_resolves_across_a_changed_entity_id()
        {
            // The point of the durable identity: the same character, a new session.
            IReadOnlyList<OperatorPlayer> roster = new[] { Player(41, "0x1", UidA) };
            OperatorTargetPolicy.TryParse("uid:" + UidA, out OperatorTarget target, out _);

            OperatorTargetResolution resolution = OperatorTargetPolicy.Resolve(target, roster);

            Assert.True(resolution.Resolved);
            Assert.Equal(41, resolution.Player.EntityId);
        }

        [Fact]
        public void Peer_selectors_match_regardless_of_hex_case_and_leading_zeros()
        {
            IReadOnlyList<OperatorPlayer> roster = new[] { Player(3, "0x00007fAB", UidA) };
            OperatorTargetPolicy.TryParse("peer:0X7fab", out OperatorTarget target, out _);

            Assert.True(OperatorTargetPolicy.Resolve(target, roster).Resolved);
        }

        [Fact]
        public void An_unmatched_target_refuses_instead_of_falling_back_to_anybody()
        {
            IReadOnlyList<OperatorPlayer> roster = new[] { Player(41, "0x1", UidA) };
            OperatorTargetPolicy.TryParse("uid:" + UidB, out OperatorTarget target, out _);

            OperatorTargetResolution resolution = OperatorTargetPolicy.Resolve(target, roster);

            Assert.False(resolution.Resolved);
            Assert.Equal(OperatorTargetFailure.NotFound, resolution.Failure);
        }

        [Fact]
        public void An_ambiguous_target_refuses_and_says_so_differently_from_not_found()
        {
            // Two entities carrying the same uid is what a stale ghost looks like.
            // Picking one of them is a coin flip over which body gets moved, and
            // the fix an operator needs ("say which") is not the fix NotFound needs
            // ("refresh"), so the two refusals must not be the same refusal.
            IReadOnlyList<OperatorPlayer> roster = new[]
            {
                Player(41, "0x1", UidA),
                Player(42, "0x2", UidA),
            };
            OperatorTargetPolicy.TryParse("uid:" + UidA, out OperatorTarget target, out _);

            OperatorTargetResolution resolution = OperatorTargetPolicy.Resolve(target, roster);

            Assert.False(resolution.Resolved);
            Assert.Equal(OperatorTargetFailure.Ambiguous, resolution.Failure);
            Assert.Contains("entity:", resolution.Reason);
        }

        [Fact]
        public void Players_with_no_uid_yet_do_not_silently_match_each_other()
        {
            // A blank uid is "no identity arrived", not "the identity is blank".
            // Two of them must not resolve as one player.
            IReadOnlyList<OperatorPlayer> roster = new[]
            {
                Player(41, "0x1", ""),
                Player(42, "0x2", ""),
            };
            OperatorTargetPolicy.TryParse("uid:" + UidA, out OperatorTarget target, out _);

            Assert.False(OperatorTargetPolicy.Resolve(target, roster).Resolved);
        }

        [Fact]
        public void A_name_never_resolves_on_the_game_server_side()
        {
            // The game server has no character table. Resolving a name there could
            // only be a guess, so it is a refusal with its own failure code.
            OperatorTargetPolicy.TryParse("name:Anyone", out OperatorTarget target, out _);
            OperatorTargetResolution resolution = OperatorTargetPolicy.Resolve(
                target, Array.Empty<OperatorPlayer>());

            Assert.False(resolution.Resolved);
            Assert.Equal(OperatorTargetFailure.NameNotResolvable, resolution.Failure);
        }
    }

    public class OperatorDestinationPolicyTests
    {
        [Fact]
        public void Home_and_spawn_are_recognised_without_a_prefix()
        {
            Assert.True(OperatorDestinationPolicy.TryParse("home", out OperatorDestinationSpec home, out _));
            Assert.Equal(OperatorDestinationKind.Home, home.Kind);

            Assert.True(OperatorDestinationPolicy.TryParse("Haven", out OperatorDestinationSpec haven, out _));
            Assert.Equal(OperatorDestinationKind.Spawn, haven.Kind);
        }

        [Fact]
        public void A_coordinate_keeps_full_precision_through_the_wire_form()
        {
            // A truncated metre here is a metre of terrain somebody lands inside.
            Assert.True(OperatorDestinationPolicy.TryParse(
                "coord:14321.44,-527.0027,-4647.39648", out OperatorDestinationSpec spec, out _));
            Assert.True(OperatorDestinationPolicy.TryParse(
                spec.ToSpec(), out OperatorDestinationSpec again, out _));

            Assert.Equal(spec, again);
            Assert.Equal(-4647.39648, again.Z);
        }

        [Theory]
        [InlineData("coord:1,2")]
        [InlineData("coord:1,2,3,4")]
        [InlineData("coord:x,2,3")]
        [InlineData("island:")]
        [InlineData("player:")]
        [InlineData("mystery:1")]
        [InlineData("")]
        public void Malformed_destinations_refuse_with_a_reason(string spec)
        {
            Assert.False(OperatorDestinationPolicy.TryParse(spec, out _, out string error));
            Assert.NotEqual(string.Empty, error);
        }

        [Fact]
        public void An_island_is_found_by_its_stable_id()
        {
            Assert.True(OperatorDestinationPolicy.TryFindIsland(
                IslandCatalog.MentalFacility.Id.Value, out IslandId island, out _));
            Assert.Equal(IslandCatalog.MentalFacility.Id, island);
        }

        [Fact]
        public void An_island_is_found_by_its_display_name_however_it_is_punctuated()
        {
            foreach (string spelling in new[]
                     { "Mental Facility", "mental facility", "MENTALFACILITY", "  Mental-Facility " })
            {
                Assert.True(
                    OperatorDestinationPolicy.TryFindIsland(spelling, out IslandId island, out string error),
                    spelling + ": " + error);
                Assert.Equal(IslandCatalog.MentalFacility.Id, island);
            }
        }

        [Fact]
        public void An_unknown_island_refuses_and_says_what_a_valid_one_looks_like()
        {
            Assert.False(OperatorDestinationPolicy.TryFindIsland(
                "Atlantis", out _, out string error));
            Assert.Contains("island id", error);
        }

        [Fact]
        public void Every_catalogued_island_has_a_landing_point_an_operator_can_use()
        {
            // The operator surface can name ANY island, not just the Tier-1 ones the
            // shrine draws from, so every record has to answer - otherwise "teleport
            // to X" works for most of the world and silently does not for the rest.
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                Assert.True(
                    OperatorDestinationPolicy.TryIslandDestination(
                        record.Definition.Id, null, "operator",
                        out TeleportDestination destination, out string error),
                    record.Definition.Id + ": " + error);
                Assert.Equal(record.Definition.WorldEntityKey, destination.RequiredWorldEntityKey);
            }
        }

        [Fact]
        public void An_island_destination_always_names_the_terrain_it_needs()
        {
            // This is the load-bearing field, not a label: it is what makes the
            // teleport path request that island's terrain for that peer and refuse
            // to send until the peer has it. A destination without it is how the
            // logout restore once dropped players through an island.
            Assert.True(OperatorDestinationPolicy.TryIslandDestination(
                IslandCatalog.MentalFacility.Id, null, "operator",
                out TeleportDestination destination, out _));

            Assert.Equal(IslandCatalog.MentalFacility.WorldEntityKey,
                destination.RequiredWorldEntityKey);
        }
    }

    public class OperatorCommandWireTests
    {
        private const string Uid = "33333333-3333-3333-3333-333333333333";

        [Fact]
        public void The_line_is_versioned_so_a_reader_can_tell_the_formats_apart()
        {
            OperatorTargetPolicy.TryParse("entity:5", out OperatorTarget target, out _);
            Assert.True(OperatorCommandWire.TryFormat(
                OperatorCommand.Teleport(target, OperatorDestinationSpec.SpawnSpec),
                out string line, out _));

            Assert.StartsWith("wa-op/1 ", line);
            Assert.True(OperatorCommandWire.IsOperatorLine(line));
            Assert.False(OperatorCommandWire.IsOperatorLine("reset-resources all"));
        }

        [Fact]
        public void Formatting_and_parsing_are_inverses_for_every_command_shape()
        {
            // The single reason this type exists: the writer and the reader are in
            // different processes and used to be two hand-rolled string formats.
            OperatorTargetPolicy.TryParse("uid:" + Uid, out OperatorTarget uid, out _);
            OperatorTargetPolicy.TryParse("entity:5", out OperatorTarget entity, out _);
            OperatorTargetPolicy.TryParse("peer:0x2ab", out OperatorTarget peer, out _);

            OperatorCommand[] commands = new[]
            {
                OperatorCommand.Teleport(uid, OperatorDestinationSpec.SpawnSpec),
                OperatorCommand.Teleport(uid, OperatorDestinationSpec.HomeSpec),
                OperatorCommand.Teleport(entity, OperatorDestinationSpec.OfIsland("mental-facility")),
                OperatorCommand.Teleport(peer, OperatorDestinationSpec.OfCoordinate(1.5, -2.25, 3.75)),
                OperatorCommand.Teleport(uid, OperatorDestinationSpec.OfPlayer("entity:9")),
                OperatorCommand.SummonShip(uid, OperatorHullSelector.OwnedByTarget),
                OperatorCommand.SummonShip(entity, OperatorHullSelector.Of(4242)),
            };

            foreach (OperatorCommand command in commands)
            {
                Assert.True(OperatorCommandWire.TryFormat(command, out string line, out string formatError),
                    formatError);
                Assert.True(OperatorCommandWire.TryParse(line, out OperatorCommand parsed, out string parseError),
                    line + ": " + parseError);
                Assert.Equal(command, parsed);
            }
        }

        [Fact]
        public void An_island_name_containing_a_space_survives_the_line()
        {
            // Fields are space-separated, so an unescaped display name would split
            // the line and turn a teleport into a parse error - or worse, into a
            // different command with the right number of fields.
            OperatorTargetPolicy.TryParse("entity:5", out OperatorTarget target, out _);
            OperatorCommand command = OperatorCommand.Teleport(
                target, OperatorDestinationSpec.OfIsland("Old Military Academy"));

            Assert.True(OperatorCommandWire.TryFormat(command, out string line, out _));
            Assert.DoesNotContain("Old Military", line);
            Assert.True(OperatorCommandWire.TryParse(line, out OperatorCommand parsed, out _));
            Assert.Equal("Old Military Academy", parsed.Destination.Value);
        }

        [Fact]
        public void A_field_cannot_forge_a_different_selector_kind_by_carrying_a_colon()
        {
            // The whole field including its prefix is escaped, so a character name
            // of "uid:0000..." arrives as a NAME and not as a uid.
            OperatorTargetPolicy.TryParse("entity:5", out OperatorTarget target, out _);
            OperatorCommand command = OperatorCommand.Teleport(
                target, OperatorDestinationSpec.OfIsland("coord:1,2,3"));

            Assert.True(OperatorCommandWire.TryFormat(command, out string line, out _));
            Assert.True(OperatorCommandWire.TryParse(line, out OperatorCommand parsed, out _));
            Assert.Equal(OperatorDestinationKind.Island, parsed.Destination.Kind);
        }

        [Fact]
        public void A_name_selector_is_refused_on_the_wire()
        {
            // Names are resolved to uids before dispatch. One on the wire means the
            // writing side skipped that step, and it fails here rather than at a
            // resolver with less context.
            string line = "wa-op/1 teleport " + Uri.EscapeDataString("name:Bligh") + " spawn";
            Assert.False(OperatorCommandWire.TryParse(line, out _, out string error));
            Assert.Contains("uid", error);
        }

        [Theory]
        [InlineData("wa-op/1 teleport entity%3A5")]
        [InlineData("wa-op/1 teleport entity%3A5 spawn extra")]
        [InlineData("wa-op/1 explode entity%3A5 spawn")]
        [InlineData("wa-op/2 teleport entity%3A5 spawn")]
        [InlineData("reset-resources all")]
        [InlineData("")]
        public void Malformed_lines_refuse_with_a_reason(string line)
        {
            Assert.False(OperatorCommandWire.TryParse(line, out _, out string error));
            Assert.NotEqual(string.Empty, error);
        }

        [Theory]
        [InlineData("owned", OperatorHullKind.Owned)]
        [InlineData("OWNED", OperatorHullKind.Owned)]
        [InlineData("hull:12", OperatorHullKind.Hull)]
        [InlineData("12", OperatorHullKind.Hull)]
        public void Hull_selectors_parse(string raw, OperatorHullKind expected)
        {
            Assert.True(OperatorCommandWire.TryParseHull(raw, out OperatorHullSelector hull, out _));
            Assert.Equal(expected, hull.Kind);
        }

        [Theory]
        [InlineData("hull:0")]
        [InlineData("hull:-1")]
        [InlineData("hull:abc")]
        [InlineData("")]
        public void Malformed_hull_selectors_refuse(string raw)
        {
            Assert.False(OperatorCommandWire.TryParseHull(raw, out _, out string error));
            Assert.NotEqual(string.Empty, error);
        }
    }

    public class OperatorSummonPolicyTests
    {
        private const string Owner = "44444444-4444-4444-4444-444444444444";
        private const string Stranger = "55555555-5555-5555-5555-555555555555";

        [Fact]
        public void Owned_resolves_when_the_character_has_exactly_one_ship()
        {
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.OwnedByTarget, Owner,
                new[] { new OperatorHull(70, Owner), new OperatorHull(71, Stranger) });

            Assert.True(choice.Ok);
            Assert.Equal(70, choice.HullEntityId);
        }

        [Fact]
        public void Owned_refuses_rather_than_picking_when_the_character_has_several()
        {
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.OwnedByTarget, Owner,
                new[] { new OperatorHull(70, Owner), new OperatorHull(71, Owner) });

            Assert.False(choice.Ok);
            Assert.Equal(OperatorSummonVerdict.OwnsSeveral, choice.Verdict);
            Assert.Contains("70", choice.Reason);
            Assert.Contains("71", choice.Reason);
        }

        [Fact]
        public void Owned_says_a_ship_must_be_built_rather_than_pretending_to_create_one()
        {
            // Summon RELOCATES; it does not conjure. A refusal that read like a
            // transient failure would send an operator looking for a bug.
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.OwnedByTarget, Owner,
                new[] { new OperatorHull(71, Stranger) });

            Assert.Equal(OperatorSummonVerdict.OwnsNothing, choice.Verdict);
            Assert.Contains("build", choice.Reason);
        }

        [Fact]
        public void Owned_refuses_when_the_target_has_no_durable_character_identity()
        {
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.OwnedByTarget, "",
                new[] { new OperatorHull(70, Owner) });

            Assert.Equal(OperatorSummonVerdict.NoCharacterIdentity, choice.Verdict);
            Assert.Contains("hull:", choice.Reason);
        }

        [Fact]
        public void An_exact_hull_that_is_not_built_refuses()
        {
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.Of(999), Owner,
                new[] { new OperatorHull(70, Owner) });

            Assert.Equal(OperatorSummonVerdict.NoSuchHull, choice.Verdict);
        }

        [Fact]
        public void Summoning_someone_elses_hull_is_allowed_but_flagged_as_not_a_transfer()
        {
            // Moving a hull never rewrites its owner. The flag exists because this
            // is also exactly what a mis-click looks like.
            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                OperatorHullSelector.Of(71), Owner,
                new[] { new OperatorHull(71, Stranger) });

            Assert.True(choice.Ok);
            Assert.True(choice.OwnershipMismatch);
            Assert.Contains("not transferred", choice.Reason);
        }
    }

    public class OperatorSafetyPolicyTests
    {
        [Fact]
        public void Teleporting_to_a_player_lands_beside_them_not_inside_them()
        {
            FixedPointPosition target = FixedPointPosition.FromMetres(100, 20, -30);
            FixedPointPosition beside = OperatorSafetyPolicy.BesidePlayer(target);

            Assert.NotEqual(target, beside);
            Assert.Equal(20, beside.MetresY, 3);
            Assert.Equal(-30, beside.MetresZ, 3);
            Assert.Equal(100 + OperatorSafetyPolicy.BesidePlayerMetres, beside.MetresX, 2);
        }

        [Fact]
        public void The_beside_offset_is_reproducible()
        {
            // Two operators sending two people to the same player must get the same
            // arrival point, or "where did they land" has no answer.
            FixedPointPosition target = FixedPointPosition.FromMetres(1, 2, 3);
            Assert.Equal(
                OperatorSafetyPolicy.BesidePlayer(target),
                OperatorSafetyPolicy.BesidePlayer(target));
        }

        [Fact]
        public void A_summon_names_the_bystanders_it_is_about_to_drop_a_hull_over()
        {
            FixedPointPosition drop = FixedPointPosition.FromMetres(0, 0, 0);
            IReadOnlyList<OperatorPlayer> roster = new[]
            {
                new OperatorPlayer(1, "0x1", "", true, 0, 0, 0),     // the target
                new OperatorPlayer(2, "0x2", "", true, 10, 0, 0),    // nearby
                new OperatorPlayer(3, "0x3", "", true, 500, 0, 0),   // far away
                new OperatorPlayer(4, "0x4", "", false, 0, 0, 0),    // position unknown
            };

            IReadOnlyList<long> near = OperatorSafetyPolicy.BystandersNear(drop, 1, roster);

            Assert.Equal(new long[] { 2 }, near);
        }

        [Fact]
        public void A_coordinate_teleport_always_warns_that_nothing_gated_the_arrival()
        {
            IReadOnlyList<string> warnings = OperatorSafetyPolicy.TeleportWarnings(
                OperatorDestinationKind.Coordinate,
                landsOnLoadedGround: false,
                namesRequiredTerrain: false,
                targetIsAboardAShip: false);

            Assert.Contains(warnings, w => w.Contains("terrain-readiness gate"));
        }

        [Fact]
        public void An_island_teleport_with_registered_terrain_warns_about_nothing()
        {
            IReadOnlyList<string> warnings = OperatorSafetyPolicy.TeleportWarnings(
                OperatorDestinationKind.Island,
                landsOnLoadedGround: false,
                namesRequiredTerrain: true,
                targetIsAboardAShip: false);

            Assert.Empty(warnings);
        }

        [Fact]
        public void Pulling_a_player_off_a_ship_is_allowed_and_said_out_loud()
        {
            IReadOnlyList<string> warnings = OperatorSafetyPolicy.TeleportWarnings(
                OperatorDestinationKind.Spawn,
                landsOnLoadedGround: true,
                namesRequiredTerrain: false,
                targetIsAboardAShip: true);

            Assert.Contains(warnings, w => w.Contains("aboard a ship"));
        }
    }
}
