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
    }
}
