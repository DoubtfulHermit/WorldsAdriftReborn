using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The live-session accumulator behind the operator dashboard. The rules
    /// that matter: counts follow reality rather than events (idempotent
    /// connect, guarded disconnect), the peak is a high-water mark that never
    /// falls, and a departed peer stops being counted as online.
    /// </summary>
    public class ServerStatsTests
    {
        private static readonly DateTimeOffset Boot =
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        private static ServerStats Fresh() => new ServerStats(Boot);

        private const ulong PeerA = 0x2f00;
        private const ulong PeerB = 0x3a00;

        [Fact]
        public void A_fresh_server_has_nobody_on_and_no_history()
        {
            ServerStats s = Fresh();

            Assert.Equal(0, s.CurrentOnline);
            Assert.Equal(0, s.PeakOnline);
            Assert.Equal(0, s.TotalConnects);
            Assert.Equal(0, s.TotalDisconnects);
            Assert.Equal(Boot, s.BootTime);
        }

        [Fact]
        public void A_connect_shows_up_as_online_and_in_the_total()
        {
            ServerStats s = Fresh();
            s.OnConnect(PeerA, Boot);

            Assert.Equal(1, s.CurrentOnline);
            Assert.Equal(1, s.TotalConnects);
            Assert.Equal(1, s.PeakOnline);
        }

        [Fact]
        public void A_duplicate_connect_for_the_same_peer_is_not_double_counted()
        {
            ServerStats s = Fresh();
            s.OnConnect(PeerA, Boot);
            s.OnConnect(PeerA, Boot.AddSeconds(5));

            Assert.Equal(1, s.CurrentOnline);
            Assert.Equal(1, s.TotalConnects);
            // The first connect time is kept, not overwritten by the duplicate.
            Assert.Equal(Boot, s.ConnectedAt(PeerA));
        }

        [Fact]
        public void A_disconnect_drops_current_but_keeps_history()
        {
            ServerStats s = Fresh();
            s.OnConnect(PeerA, Boot);
            s.OnDisconnect(PeerA);

            Assert.Equal(0, s.CurrentOnline);
            Assert.Equal(1, s.TotalConnects);
            Assert.Equal(1, s.TotalDisconnects);
            Assert.Null(s.ConnectedAt(PeerA));
        }

        [Fact]
        public void A_disconnect_for_an_untracked_peer_does_nothing()
        {
            ServerStats s = Fresh();
            s.OnDisconnect(PeerA);

            Assert.Equal(0, s.TotalDisconnects);
            Assert.Equal(0, s.CurrentOnline);
        }

        [Fact]
        public void Peak_is_a_high_water_mark_that_does_not_fall()
        {
            ServerStats s = Fresh();
            s.OnConnect(PeerA, Boot);
            s.OnConnect(PeerB, Boot);
            Assert.Equal(2, s.PeakOnline);

            s.OnDisconnect(PeerA);
            s.OnDisconnect(PeerB);

            Assert.Equal(0, s.CurrentOnline);
            Assert.Equal(2, s.PeakOnline);
            Assert.Equal(2, s.TotalConnects);
            Assert.Equal(2, s.TotalDisconnects);
        }

        [Fact]
        public void Connected_at_is_the_instant_that_was_passed_in()
        {
            ServerStats s = Fresh();
            DateTimeOffset when = Boot.AddMinutes(3);
            s.OnConnect(PeerA, when);

            Assert.Equal(when, s.ConnectedAt(PeerA));
        }
    }
}
