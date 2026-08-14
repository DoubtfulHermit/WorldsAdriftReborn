using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Where a teleport goes, and which request number carries it.
    ///
    /// The two facts these tests exist to pin down are the ones the client
    /// enforces silently: the seed request must be 0 or every player teleports
    /// at checkout, and every real request must be strictly greater than BOTH
    /// what we last sent and what the client last acked or the teleport is a
    /// no-op that looks exactly like a wire bug.
    /// </summary>
    public class TeleportPolicyTests
    {
        // ------------------------------------------------------------------
        // The destination table
        // ------------------------------------------------------------------

        [Fact]
        public void Home_is_exactly_where_players_already_spawn()
        {
            // If these ever drift apart, "teleport me home" puts you somewhere
            // you have never woken up - which on this server means somewhere
            // with no verified ground under it.
            Assert.True(TeleportPolicy.TryResolve(TeleportPolicy.HavenName, out TeleportDestination haven));
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, haven.Position);
        }

        [Fact]
        public void The_table_reaches_an_island_that_is_not_the_one_we_spawn()
        {
            // The whole point of the feature: the world stops being one rock.
            Assert.True(TeleportPolicy.TryResolve(TeleportPolicy.MausoleumName, out TeleportDestination other));
            Assert.NotEqual(SpawnPolicy.IslandPosition, other.Position);

            // 949069116 "Shattered Mausoleum" sits 4.4 km from Haven #5. Assert
            // the order of magnitude, not the decimals: this catches a
            // copy-paste of Haven's coordinates, which is the realistic mistake.
            double metres = Distance(other.Position, SpawnPolicy.PlayerSpawnPosition);
            Assert.InRange(metres, 4000.0, 5000.0);
        }

        [Fact]
        public void Haven_north_is_the_same_island_local_offset_on_the_next_copy_up_the_column()
        {
            // Haven is ONE asset at TWELVE world positions, so the same local
            // offset is the same patch of ground on every copy. If this stops
            // holding, the "cheapest second place to stand" is no longer a place
            // to stand at all.
            Assert.True(TeleportPolicy.TryResolve(TeleportPolicy.HavenNorthName, out TeleportDestination north));

            // wamap-islands.json entry 259: (17003.416, -212.325027, 1826.00183).
            // Compared with a millimetre tolerance rather than Assert.Equal's
            // decimal-place rounding, which trips on exact .xx5 boundaries.
            AssertMetres(17003.416 + TeleportPolicy.HavenSpawnLocalOffset.X, north.Position.MetresX);
            AssertMetres(-212.325027 + TeleportPolicy.HavenSpawnLocalOffset.Y, north.Position.MetresY);
            AssertMetres(1826.00183 + TeleportPolicy.HavenSpawnLocalOffset.Z, north.Position.MetresZ);

            // And it really is the nearest island: 2962 m, closer than the
            // Mausoleum's 4425 m.
            double metres = Distance(north.Position, SpawnPolicy.PlayerSpawnPosition);
            Assert.InRange(metres, 2800.0, 3100.0);
        }

        [Fact]
        public void The_haven_spawn_offset_is_the_one_that_actually_produced_the_spawn_point()
        {
            // HavenSpawnLocalOffset is only meaningful if it is the offset
            // SpawnPolicy's two constants already encode. Derive it and compare,
            // so a change to either constant is caught here rather than by a
            // player falling out of the world on another Haven copy.
            AssertMetres(
                TeleportPolicy.HavenSpawnLocalOffset.X,
                SpawnPolicy.PlayerSpawnPosition.MetresX - SpawnPolicy.IslandPosition.MetresX);
            AssertMetres(
                TeleportPolicy.HavenSpawnLocalOffset.Y,
                SpawnPolicy.PlayerSpawnPosition.MetresY - SpawnPolicy.IslandPosition.MetresY);
            AssertMetres(
                TeleportPolicy.HavenSpawnLocalOffset.Z,
                SpawnPolicy.PlayerSpawnPosition.MetresZ - SpawnPolicy.IslandPosition.MetresZ);
        }

        [Fact]
        public void Only_Haven_and_the_guarded_PR3_island_claim_ground()
        {
            // This server spawns ONE island entity. Any second destination
            // claiming solid ground would be a lie that ends in an endless fall,
            // because there is no fall damage and no world-edge pushback here.
            int landable = 0;
            foreach (TeleportDestination destination in TeleportPolicy.Destinations)
            {
                if (destination.LandsOnLoadedGround)
                {
                    landable++;
                }
            }

            Assert.Equal(2, landable);
            Assert.Equal(TeleportPolicy.HavenName, TeleportPolicy.SafeDestination.Name);
        }

        [Fact]
        public void Trades_challenge_uses_a_flat_extracted_surface_and_requires_its_registered_terrain()
        {
            Assert.True(TeleportPolicy.TryResolve(
                TeleportPolicy.TradesChallengeName, out TeleportDestination destination));

            Assert.Equal(global::WorldsAdriftRebornGameServer.Multiplayer.Islands
                    .IslandCatalog.TradesChallenge.WorldEntityKey,
                destination.RequiredWorldEntityKey);
            Assert.True(destination.LandsOnLoadedGround);
            AssertMetres(13253.5547 - 64.0, destination.Position.MetresX);
            AssertMetres(-193.321426 + 0.45 + 2.0, destination.Position.MetresY);
            AssertMetres(-1972.03845 - 64.0, destination.Position.MetresZ);

            Assert.False(TeleportPolicy.RequiredTerrainIsRegistered(destination, _ => false));
            Assert.True(TeleportPolicy.RequiredTerrainIsRegistered(destination,
                key => key == destination.RequiredWorldEntityKey));

            Assert.True(TeleportPolicy.TryResolve(
                TeleportPolicy.HavenName, out TeleportDestination haven));
            Assert.True(TeleportPolicy.RequiredTerrainIsRegistered(haven, _ => false));
        }

        [Fact]
        public void Destination_names_are_unique_and_lower_case()
        {
            // Lookup is by lower-cased key, so an upper-case entry would be
            // unreachable and a duplicate would shadow silently.
            HashSet<string> seen = new HashSet<string>();
            foreach (TeleportDestination destination in TeleportPolicy.Destinations)
            {
                Assert.Equal(destination.Name.ToLowerInvariant(), destination.Name);
                Assert.True(seen.Add(destination.Name), "duplicate destination name " + destination.Name);
                Assert.True(TeleportPolicy.TryResolve(destination.Name, out _));
            }
        }

        [Fact]
        public void Every_destination_is_inside_the_world()
        {
            // WorldEdgeLength 36000, so ±18000 on X and Z; the real world's Y
            // spans only -527..+357. A destination outside that is a typo, and
            // WorldEdgePushback never runs on this server to catch it.
            foreach (TeleportDestination destination in TeleportPolicy.Destinations)
            {
                Assert.InRange(destination.Position.MetresX, -18000.0, 18000.0);
                Assert.InRange(destination.Position.MetresZ, -18000.0, 18000.0);
                Assert.InRange(destination.Position.MetresY, -1000.0, 1000.0);
            }
        }

        [Theory]
        [InlineData("HAVEN")]
        [InlineData("  haven  ")]
        [InlineData("Haven")]
        public void Destination_lookup_forgives_case_and_whitespace(string typed)
        {
            // The name arrives from a human typing into a file under echo.
            Assert.True(TeleportPolicy.TryResolve(typed, out TeleportDestination destination));
            Assert.Equal(TeleportPolicy.HavenName, destination.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("atlantis")]
        public void An_unknown_destination_resolves_to_nothing(string? typed)
        {
            Assert.False(TeleportPolicy.TryResolve(typed, out _));
        }

        // ------------------------------------------------------------------
        // The trigger-file grammar
        // ------------------------------------------------------------------

        [Fact]
        public void A_bare_destination_means_everyone()
        {
            Assert.True(TeleportPolicy.TryParseCommand("mausoleum", out TeleportCommand command, out _));
            Assert.Equal(TeleportPolicy.MausoleumName, command.Destination.Name);
            Assert.Null(command.EntityId);
        }

        [Fact]
        public void A_destination_with_an_entity_id_means_just_that_player()
        {
            Assert.True(TeleportPolicy.TryParseCommand("haven 7", out TeleportCommand command, out _));
            Assert.Equal(TeleportPolicy.HavenName, command.Destination.Name);
            Assert.Equal(7L, command.EntityId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("# haven")]
        [InlineData(null)]
        public void Blank_and_comment_lines_are_nothing_to_do_not_errors(string? line)
        {
            // The distinction matters: the trigger file is polled, so treating
            // an empty file as an error would fill the log with it.
            Assert.False(TeleportPolicy.TryParseCommand(line, out _, out string error));
            Assert.Equal(string.Empty, error);
        }

        [Theory]
        [InlineData("atlantis", "unknown destination")]
        [InlineData("haven notanumber", "not an entity id")]
        [InlineData("haven 1 2", "expected")]
        public void Garbage_is_rejected_with_a_reason(string line, string expectedFragment)
        {
            Assert.False(TeleportPolicy.TryParseCommand(line, out _, out string error));
            Assert.Contains(expectedFragment, error);
        }

        [Fact]
        public void Parsing_tolerates_tabs_and_runs_of_spaces()
        {
            Assert.True(TeleportPolicy.TryParseCommand("\thaven-north\t \t42  \n", out TeleportCommand command, out _));
            Assert.Equal(TeleportPolicy.HavenNorthName, command.Destination.Name);
            Assert.Equal(42L, command.EntityId);
        }

        // ------------------------------------------------------------------
        // The request counter - the rule the client enforces silently
        // ------------------------------------------------------------------

        [Fact]
        public void The_seed_request_is_zero_or_everyone_teleports_at_checkout()
        {
            // TeleportRequestState's generated RequestUpdated event replays the
            // CURRENT value the instant the visualizer subscribes, in OnEnable.
            // The guard is strictly greater-than against a 1073 field that also
            // defaults to 0, so 0 is the only seed that cannot fire.
            Assert.Equal(0, TeleportPolicy.SeedRequest);
        }

        [Fact]
        public void The_first_real_request_is_one_never_the_seed_value()
        {
            TeleportRequestCounter counter = new TeleportRequestCounter();
            Assert.Equal(1, counter.Next(entityId: 5));
            Assert.NotEqual(TeleportPolicy.SeedRequest, counter.Next(entityId: 6));
        }

        [Fact]
        public void Requests_climb_even_when_nothing_is_ever_acked()
        {
            // A client that never acks must not pin the counter: the next
            // teleport would carry a number it has already ignored.
            TeleportRequestCounter counter = new TeleportRequestCounter();
            Assert.Equal(1, counter.Next(1));
            Assert.Equal(2, counter.Next(1));
            Assert.Equal(3, counter.Next(1));
        }

        [Fact]
        public void A_client_reporting_a_higher_number_than_we_sent_still_gets_beaten()
        {
            // 1073 is client-owned and re-published every tick. If anything ever
            // puts a larger lastExecutedRequest in there, counting from our own
            // sends alone would make every future teleport a silent no-op.
            TeleportRequestCounter counter = new TeleportRequestCounter();
            counter.Next(1);                    // we sent 1
            counter.RecordAck(1, 900);          // the client claims 900

            Assert.Equal(901, counter.Next(1));
        }

        [Fact]
        public void An_ack_that_goes_backwards_never_lowers_the_next_request()
        {
            // A reconnect re-seeds 1073 to 0. That must not make us re-send a
            // number the (possibly still live) client already executed.
            TeleportRequestCounter counter = new TeleportRequestCounter();
            counter.Next(1);
            counter.Next(1);                    // high-water 2
            Assert.True(counter.RecordAck(1, 2));
            Assert.False(counter.RecordAck(1, 0));

            Assert.Equal(3, counter.Next(1));
        }

        [Fact]
        public void Only_a_new_ack_reports_as_news()
        {
            // The client publishes 1073 every tick; the ack field rides along
            // unchanged. Logging a landing per tick would bury the log.
            TeleportRequestCounter counter = new TeleportRequestCounter();
            Assert.True(counter.RecordAck(1, 4));
            Assert.False(counter.RecordAck(1, 4));
            Assert.False(counter.RecordAck(1, 3));
            Assert.True(counter.RecordAck(1, 5));
        }

        [Fact]
        public void Counters_are_per_entity()
        {
            TeleportRequestCounter counter = new TeleportRequestCounter();
            Assert.Equal(1, counter.Next(1));
            Assert.Equal(1, counter.Next(2));
            Assert.Equal(2, counter.Next(1));
            Assert.Equal(2, counter.Next(2));
        }

        [Fact]
        public void A_teleport_is_outstanding_until_its_own_number_comes_back()
        {
            TeleportRequestCounter counter = new TeleportRequestCounter();
            Assert.Null(counter.Outstanding(1));

            int sent = counter.Next(1);
            Assert.Equal(sent, counter.Outstanding(1));

            counter.RecordAck(1, sent - 1);     // a stale ack does not clear it
            Assert.Equal(sent, counter.Outstanding(1));

            counter.RecordAck(1, sent);
            Assert.Null(counter.Outstanding(1));
        }

        [Fact]
        public void Forgetting_an_entity_drops_both_of_its_counters()
        {
            // Entity ids are handed out monotonically so a stale record is only
            // wasted memory - but the disconnect path is supposed to drop EVERY
            // piece of per-peer state in one place, and this is now one of them.
            TeleportRequestCounter counter = new TeleportRequestCounter();
            counter.Next(1);
            counter.RecordAck(1, 1);

            counter.Forget(1);

            Assert.Null(counter.LastSent(1));
            Assert.Null(counter.LastAcked(1));
            Assert.Equal(1, counter.Next(1));
        }

        [Theory]
        [InlineData(0, 0, 1)]
        [InlineData(3, 0, 4)]
        [InlineData(0, 3, 4)]
        [InlineData(3, 3, 4)]
        [InlineData(-5, -9, 1)]     // garbage clamps to the seed, so we start at 1
        public void The_next_request_beats_both_the_last_send_and_the_last_ack(int lastSent, int lastAcked, int expected)
        {
            Assert.Equal(expected, TeleportRequestCounter.NextRequest(lastSent, lastAcked));
        }

        [Fact]
        public void The_counter_saturates_rather_than_wrapping_negative()
        {
            // Unreachable in practice, but wrapping to a negative number would
            // permanently disable teleport for that entity - the client's guard
            // would never be satisfied again.
            Assert.Equal(int.MaxValue, TeleportRequestCounter.NextRequest(int.MaxValue, 0));
            Assert.True(TeleportRequestCounter.NextRequest(int.MaxValue, int.MaxValue) > 0);
        }

        // ------------------------------------------------------------------
        // Authority - the reason this is the cheap path
        // ------------------------------------------------------------------

        [Fact]
        public void Teleport_needs_no_authority_grant_we_do_not_already_make()
        {
            // TeleportTransformVisualizer acks on 1073, which we already grant.
            // That is the entire reason the parentless path is hours rather than
            // days: no new grant, no client patch.
            Assert.Contains(TeleportPolicy.AckComponentId, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void The_client_is_never_granted_authority_over_the_teleport_request()
        {
            // 190607 is server-written only. Granting it would let any client
            // teleport itself anywhere in the world.
            Assert.DoesNotContain(TeleportPolicy.TeleportRequestStateComponentId, MirrorSendPolicy.AuthoritativeComponents);
        }

        /// <summary>
        /// Metres compared to the millimetre. One Q52.12 unit is 0.24 mm, so this
        /// is four times the encoding's own resolution - tight enough to catch a
        /// wrong coordinate, loose enough not to trip on the round-trip.
        /// </summary>
        private static void AssertMetres(double expected, double actual)
        {
            Assert.InRange(actual, expected - 0.001, expected + 0.001);
        }

        private static double Distance(FixedPointPosition a, FixedPointPosition b)
        {
            double dx = a.MetresX - b.MetresX;
            double dy = a.MetresY - b.MetresY;
            double dz = a.MetresZ - b.MetresZ;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
