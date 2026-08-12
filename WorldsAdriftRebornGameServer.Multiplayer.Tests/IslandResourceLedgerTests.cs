using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class IslandResourceLedgerTests
    {
        private static ResourceReplyItem Metal(double x, double y, double z)
            => new ResourceReplyItem(x, y, z, SpawnReplyPlan.MetalMetadata, "metal_deposit_composite_light_01");

        [Fact]
        public void RequestedCount_is_clamped()
        {
            Assert.Equal(IslandResourceHandshake.MaxMetalCount, new IslandResourceLedger(999999).RequestedCount);
            Assert.Equal(0, new IslandResourceLedger(-3).RequestedCount);
        }

        [Fact]
        public void MarkRequestSent_is_idempotent()
        {
            var ledger = new IslandResourceLedger(40);
            Assert.False(ledger.RequestSent);
            Assert.True(ledger.MarkRequestSent());
            Assert.True(ledger.RequestSent);
            Assert.False(ledger.MarkRequestSent());
            Assert.False(ledger.MarkRequestSent());
        }

        [Fact]
        public void Admit_records_spawned_and_assigns_monotonic_indices()
        {
            var ledger = new IslandResourceLedger(40);
            var first = ledger.Admit(new[] { Metal(1, 0, 0), Metal(2, 0, 0) });
            Assert.Equal(new[] { 0, 1 }, first.Select(d => d.Index));
            Assert.Equal(2, ledger.SpawnedCount);

            var second = ledger.Admit(new[] { Metal(3, 0, 0) });
            Assert.Equal(new[] { 2 }, second.Select(d => d.Index));
            Assert.Equal(3, ledger.SpawnedCount);
        }

        [Fact]
        public void Admit_dedups_across_calls_idempotent_resend()
        {
            var ledger = new IslandResourceLedger(40);
            var batch = new[] { Metal(1, 0, 0), Metal(2, 0, 0) };
            var first = ledger.Admit(batch);
            Assert.Equal(2, first.Count);

            // The SAME reply arrives again (client re-send, or a second client that
            // happened to sample the same two vertices): nothing new spawns.
            var again = ledger.Admit(batch);
            Assert.Empty(again);
            Assert.Equal(2, ledger.SpawnedCount);
        }

        [Fact]
        public void Admit_never_exceeds_requested_count()
        {
            var ledger = new IslandResourceLedger(3);
            var items = Enumerable.Range(0, 10).Select(i => Metal(i, 0, 0)).ToArray();
            var got = ledger.Admit(items);
            Assert.Equal(3, got.Count);
            Assert.True(ledger.Satisfied);

            // A second flood admits nothing.
            var more = ledger.Admit(Enumerable.Range(100, 10).Select(i => Metal(i, 0, 0)).ToArray());
            Assert.Empty(more);
            Assert.Equal(3, ledger.SpawnedCount);
        }

        [Fact]
        public void Admit_two_clients_partial_overlap_clamps_and_dedups()
        {
            var ledger = new IslandResourceLedger(5);
            // Client A replies with 3 distinct vertices.
            var a = ledger.Admit(new[] { Metal(1, 0, 0), Metal(2, 0, 0), Metal(3, 0, 0) });
            Assert.Equal(3, a.Count);
            // Client B replies with two overlaps and three new -> only 2 admitted (budget 5-3=2),
            // and the overlaps are never among them.
            var b = ledger.Admit(new[] { Metal(2, 0, 0), Metal(3, 0, 0), Metal(4, 0, 0), Metal(5, 0, 0), Metal(6, 0, 0) });
            Assert.Equal(2, b.Count);
            Assert.Equal(5, ledger.SpawnedCount);
            Assert.DoesNotContain(FixedPointPosition.FromMetres(2, 0, 0), b.Select(d => d.Position));
            Assert.DoesNotContain(FixedPointPosition.FromMetres(3, 0, 0), b.Select(d => d.Position));
        }

        [Fact]
        public void Admit_zero_request_admits_nothing()
        {
            var ledger = new IslandResourceLedger(0);
            Assert.Empty(ledger.Admit(new[] { Metal(1, 0, 0) }));
            Assert.True(ledger.Satisfied);
        }

        // ------------------------------------------------------------------
        // The bounds-guarded ledger, the deadline latch and the fallback latch.
        // ------------------------------------------------------------------

        private static ResourceReplyItem OnHaven(double lx, double ly, double lz)
        {
            FixedPointPosition p = MetalNodes.IslandLocalToWorldFixed(SpawnPolicy.IslandPosition, lx, ly, lz);
            return new ResourceReplyItem(p.MetresX, p.MetresY, p.MetresZ,
                SpawnReplyPlan.MetalMetadata, MetalDeposits.DefaultVariantId);
        }

        private static IslandResourceLedger Guarded(int count)
            => new IslandResourceLedger(count, IslandBounds.Haven());

        [Fact]
        public void A_guarded_ledger_admits_real_on_island_placements()
        {
            var ledger = Guarded(5);
            var got = ledger.Admit(new[] { OnHaven(216, 4.57, 8), OnHaven(200, 4.27, 0) });
            Assert.Equal(2, got.Count);
            Assert.Equal(2, ledger.SpawnedCount);
        }

        [Fact]
        public void A_guarded_ledger_admits_nothing_from_an_out_of_frame_reply()
        {
            var ledger = Guarded(5);
            var admission = ledger.AdmitDetailed(new[] { Metal(216, 4.57, 8), Metal(0, 0, 0) });
            Assert.Empty(admission.Admitted);
            Assert.Equal(2, admission.Outcome.OutOfBounds);
            Assert.Equal(0, ledger.SpawnedCount);
            // ...which is exactly the state that must trigger the fallback.
            Assert.True(IslandResourceFallback.ShouldFallBack(ledger.SpawnedCount, ledger.FallbackFired));
        }

        [Fact]
        public void The_deadline_is_armed_only_once()
        {
            var ledger = Guarded(5);
            Assert.True(ledger.MarkDeadlineArmed());
            Assert.False(ledger.MarkDeadlineArmed());
            Assert.False(ledger.MarkDeadlineArmed());
        }

        [Fact]
        public void The_fallback_latch_fires_only_once()
        {
            var ledger = Guarded(5);
            Assert.False(ledger.FallbackFired);
            Assert.True(ledger.MarkFallbackFired());
            Assert.True(ledger.FallbackFired);
            Assert.False(ledger.MarkFallbackFired());
        }

        [Fact]
        public void After_the_fallback_fires_no_client_reply_is_admitted()
        {
            // Otherwise a reply landing one second late would stack forty client-placed
            // deposits on top of the twenty-odd hand-placed ones.
            var ledger = Guarded(5);
            ledger.MarkFallbackFired();

            var admission = ledger.AdmitDetailed(new[] { OnHaven(216, 4.57, 8) });
            Assert.Empty(admission.Admitted);
            Assert.True(admission.RefusedBecauseFallbackFired);
            Assert.Equal(0, ledger.SpawnedCount);
        }

        [Fact]
        public void A_reply_before_the_fallback_stops_it_from_firing()
        {
            var ledger = Guarded(5);
            Assert.Single(ledger.Admit(new[] { OnHaven(216, 4.57, 8) }));
            Assert.False(IslandResourceFallback.ShouldFallBack(ledger.SpawnedCount, ledger.FallbackFired));
        }

        [Fact]
        public void A_guarded_ledger_is_still_idempotent_across_replies()
        {
            var ledger = Guarded(10);
            var first = ledger.Admit(new[] { OnHaven(216, 4.57, 8), OnHaven(200, 4.27, 0) });
            var second = ledger.Admit(new[] { OnHaven(216, 4.57, 8), OnHaven(200, 4.27, 0) });
            Assert.Equal(2, first.Count);
            Assert.Empty(second);
            Assert.Equal(2, ledger.SpawnedCount);
        }
    }
}
