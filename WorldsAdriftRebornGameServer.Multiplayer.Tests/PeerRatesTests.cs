using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The per-peer wire accumulator behind the 5 s [rates] log line.
    ///
    /// The rules under test are the ones a debugging session will lean on when
    /// the next peer dies quietly: a window reports every interval EVEN when
    /// empty (the silent peer is the interesting one), sends and receives never
    /// bleed into each other, the top-5 is deterministic, and a departed peer
    /// stops reporting entirely.
    /// </summary>
    public class PeerRatesTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const ulong Peer = 0x2f00;
        private const ulong OtherPeer = 0x3a00;

        private const uint Transform = 190602;
        private const uint Bones = 1073;

        private static (PeerRates, FakeClock) Fresh()
        {
            FakeClock clock = new FakeClock();
            return (new PeerRates(clock), clock);
        }

        // ------------------------------------------------------------------
        // WHEN A REPORT IS DUE
        // ------------------------------------------------------------------

        [Fact]
        public void Nothing_is_due_before_the_interval()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);

            clock.Advance(PeerRates.DefaultInterval - TimeSpan.FromMilliseconds(1));
            Assert.Empty(rates.DueReports());
        }

        [Fact]
        public void One_report_per_peer_once_the_interval_elapses()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            rates.RecordSend(OtherPeer, Transform);

            clock.Advance(PeerRates.DefaultInterval);
            IReadOnlyList<PeerRateReport> due = rates.DueReports();

            Assert.Equal(2, due.Count);
            Assert.Contains(due, r => r.PeerId == Peer);
            Assert.Contains(due, r => r.PeerId == OtherPeer);
        }

        [Fact]
        public void Reporting_resets_the_window()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);

            clock.Advance(PeerRates.DefaultInterval);
            Assert.Single(rates.DueReports());

            // Immediately after a report, nothing is due again...
            Assert.Empty(rates.DueReports());

            // ...and the next report contains only NEW traffic.
            rates.RecordReceive(Peer, Bones);
            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport next = Assert.Single(rates.DueReports());
            Assert.Equal(1, next.ReceiveTotal);
            Assert.Equal(Bones, next.TopReceives[0].Key);
        }

        [Fact]
        public void A_peer_that_went_silent_still_reports_all_zeros()
        {
            // THE point of the interval-not-traffic trigger: the peer that
            // stopped talking is the one about to time out, and its report is
            // the evidence.
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);

            clock.Advance(PeerRates.DefaultInterval);
            Assert.Single(rates.DueReports());

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport silent = Assert.Single(rates.DueReports());
            Assert.Equal(0, silent.ReceiveTotal);
            Assert.Equal(0, silent.SendTotal);
            Assert.Empty(silent.TopReceives);
        }

        [Fact]
        public void The_window_length_is_reported_as_measured_not_assumed()
        {
            // A busy loop can be late; rate arithmetic done on the line must
            // divide by what actually elapsed.
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);

            clock.Advance(TimeSpan.FromSeconds(7));
            PeerRateReport report = Assert.Single(rates.DueReports());
            Assert.Equal(TimeSpan.FromSeconds(7), report.Window);
        }

        // ------------------------------------------------------------------
        // WHAT A REPORT CONTAINS
        // ------------------------------------------------------------------

        [Fact]
        public void Sends_and_receives_are_counted_apart()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            rates.RecordReceive(Peer, Transform);
            rates.RecordSend(Peer, Bones);

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport report = Assert.Single(rates.DueReports());

            Assert.Equal(2, report.ReceiveTotal);
            Assert.Equal(1, report.SendTotal);
            Assert.Equal(Transform, Assert.Single(report.TopReceives).Key);
            Assert.Equal(Bones, Assert.Single(report.TopSends).Key);
        }

        [Fact]
        public void Peers_never_bleed_into_each_other()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            rates.RecordReceive(OtherPeer, Bones);

            clock.Advance(PeerRates.DefaultInterval);
            IReadOnlyList<PeerRateReport> due = rates.DueReports();

            PeerRateReport first = due.Single(r => r.PeerId == Peer);
            Assert.Equal(Transform, Assert.Single(first.TopReceives).Key);
        }

        [Fact]
        public void The_top_list_is_capped_at_five_biggest_first()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            for (uint id = 1; id <= 7; id++)
            {
                for (uint n = 0; n < id; n++)
                {
                    rates.RecordReceive(Peer, id);
                }
            }

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport report = Assert.Single(rates.DueReports());

            Assert.Equal(PeerRates.TopCount, report.TopReceives.Count);
            Assert.Equal(7u, report.TopReceives[0].Key);
            Assert.Equal(7, report.TopReceives[0].Value);
            Assert.Equal(3u, report.TopReceives[4].Key);
            // But the TOTAL still counts everything, including the tail.
            Assert.Equal(1 + 2 + 3 + 4 + 5 + 6 + 7, report.ReceiveTotal);
        }

        [Fact]
        public void Ties_in_the_top_list_break_by_key_so_output_is_deterministic()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Bones);
            rates.RecordReceive(Peer, Transform);

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport report = Assert.Single(rates.DueReports());

            Assert.Equal(Bones, report.TopReceives[0].Key);
            Assert.Equal(Transform, report.TopReceives[1].Key);
        }

        // ------------------------------------------------------------------
        // CHANNEL KEYS
        // ------------------------------------------------------------------

        [Fact]
        public void Channel_keys_cannot_collide_with_component_ids()
        {
            // Component ids in this game are 1036..190607; channel keys live
            // behind the high bit.
            Assert.True(PeerRates.ChannelKey(0) > 190607u);
            Assert.NotEqual(PeerRates.ChannelKey(0), PeerRates.ChannelKey(4));
        }

        [Fact]
        public void Keys_print_as_component_ids_or_channels()
        {
            Assert.Equal("190602", PeerRates.DescribeKey(Transform));
            Assert.Equal("ch0", PeerRates.DescribeKey(PeerRates.ChannelKey(0)));
            Assert.Equal("ch4", PeerRates.DescribeKey(PeerRates.ChannelKey(4)));
        }

        // ------------------------------------------------------------------
        // THE LINE ITSELF, AND FORGETTING
        // ------------------------------------------------------------------

        [Fact]
        public void The_line_is_greppable_and_complete()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            rates.RecordReceive(Peer, Transform);
            rates.RecordReceive(Peer, PeerRates.ChannelKey(0));
            rates.RecordSend(Peer, Bones);

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport report = Assert.Single(rates.DueReports());

            Assert.Equal("peer 0x2f00: rx 3 (190602:2 ch0:1), tx 1 (1073:1) in 5.0s", report.Describe());
        }

        [Fact]
        public void An_empty_window_prints_without_parentheses()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            clock.Advance(PeerRates.DefaultInterval);
            rates.DueReports();

            clock.Advance(PeerRates.DefaultInterval);
            PeerRateReport silent = Assert.Single(rates.DueReports());
            Assert.Equal("peer 0x2f00: rx 0, tx 0 in 5.0s", silent.Describe());
        }

        [Fact]
        public void A_forgotten_peer_never_reports_again()
        {
            (PeerRates rates, FakeClock clock) = Fresh();
            rates.RecordReceive(Peer, Transform);
            rates.Forget(Peer);

            clock.Advance(PeerRates.DefaultInterval + PeerRates.DefaultInterval);
            Assert.Empty(rates.DueReports());
        }
    }
}
