using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The ledger that stops the interest handler from re-ADDing a component the
    /// client already holds - the fix for the deck that is solid on first render
    /// and a fall-through ever after. Keyed by a plain int peer here; the server
    /// uses the native ENetPeerHandle, which the generic accepts unchanged.
    /// </summary>
    public class ServedComponentLedgerTests
    {
        private const long Deck = 4;

        [Fact]
        public void A_first_request_serves_everything_asked_for()
        {
            var ledger = new ServedComponentLedger<int>();
            var unserved = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.Equal(new uint[] { 1518, 1099, 190602 }, unserved);
        }

        [Fact]
        public void A_second_identical_request_serves_nothing_once_the_first_was_marked()
        {
            var ledger = new ServedComponentLedger<int>();
            var first = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099, 190602 });
            ledger.MarkServed(1, Deck, first);

            // The deck re-declares its whole set; none of it must go back out.
            var second = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.Empty(second);
        }

        [Fact]
        public void Only_the_components_actually_sent_are_remembered_so_a_miss_can_retry()
        {
            var ledger = new ServedComponentLedger<int>();
            // The client asked for three but the server could only seed two (the
            // third had no branch and was skipped). Only the two that were sent
            // are marked.
            ledger.MarkServed(1, Deck, new uint[] { 1518, 1099 });

            var next = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.Equal(new uint[] { 190602 }, next);
        }

        [Fact]
        public void A_repeat_id_within_one_request_is_offered_once()
        {
            var ledger = new ServedComponentLedger<int>();
            var unserved = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1518, 1099 });
            Assert.Equal(new uint[] { 1518, 1099 }, unserved);
        }

        [Fact]
        public void Request_order_is_preserved()
        {
            var ledger = new ServedComponentLedger<int>();
            var unserved = ledger.UnservedOf(1, Deck, new uint[] { 190602, 1099, 1518 });
            Assert.Equal(new uint[] { 190602, 1099, 1518 }, unserved.ToArray());
        }

        [Fact]
        public void Different_entities_are_tracked_independently()
        {
            var ledger = new ServedComponentLedger<int>();
            ledger.MarkServed(1, Deck, new uint[] { 1518, 1099 });

            // The hull (entity 2) sharing a component id with the deck must still
            // be served it - the ledger keys on the entity, not the id alone.
            var hull = ledger.UnservedOf(1, entityId: 2, new uint[] { 1099, 190602 });
            Assert.Equal(new uint[] { 1099, 190602 }, hull);
        }

        [Fact]
        public void Different_peers_are_tracked_independently()
        {
            var ledger = new ServedComponentLedger<int>();
            ledger.MarkServed(1, Deck, new uint[] { 1518, 1099, 190602 });

            // A second player checking the same deck out gets the full set.
            var otherPeer = ledger.UnservedOf(2, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.Equal(new uint[] { 1518, 1099, 190602 }, otherPeer);
        }

        [Fact]
        public void Forgetting_a_peer_lets_a_reused_handle_start_clean()
        {
            var ledger = new ServedComponentLedger<int>();
            ledger.MarkServed(1, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.True(ledger.HasServed(1, Deck, 1518));

            ledger.ForgetPeer(1);

            Assert.False(ledger.HasServed(1, Deck, 1518));
            var afterReuse = ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099, 190602 });
            Assert.Equal(new uint[] { 1518, 1099, 190602 }, afterReuse);
        }

        [Fact]
        public void A_spawn_seeded_shipyard_is_not_re_added_on_the_joiners_interest_recheckout()
        {
            // The placement crash: a boot-restored shipyard's seed set
            // {190602, 1205, 1206, 1210, 1004, 1005} is pushed once when the joining
            // client's spawn plan creates the entity. If that push is NOT recorded,
            // the client's later interest re-checkout (which re-declares its whole
            // set) re-ADDs every id - "Component ShipyardState added to entity 0, but
            // it already exists" - and the entity store throws. Recording the seed
            // push (what AddWorldEntity / BroadcastToPeer now do) makes the
            // re-checkout serve nothing.
            const long Shipyard = 7;
            uint[] seed = { 190602, 1205, 1206, 1210, 1004, 1005 };

            var ledger = new ServedComponentLedger<int>();

            // The spawn plan seeds the shipyard and records what it delivered.
            var seeded = ledger.UnservedOf(1, Shipyard, seed);
            Assert.Equal(seed, seeded);
            ledger.MarkServed(1, Shipyard, seeded);

            // The client re-declares interest in the whole set: none of it goes back.
            var recheckout = ledger.UnservedOf(1, Shipyard, seed);
            Assert.Empty(recheckout);
        }

        [Fact]
        public void Marking_served_is_additive_across_calls()
        {
            var ledger = new ServedComponentLedger<int>();
            ledger.MarkServed(1, Deck, new uint[] { 1518 });
            ledger.MarkServed(1, Deck, new uint[] { 1099 });

            Assert.True(ledger.HasServed(1, Deck, 1518));
            Assert.True(ledger.HasServed(1, Deck, 1099));
            Assert.Empty(ledger.UnservedOf(1, Deck, new uint[] { 1518, 1099 }));
        }

        [Fact]
        public void The_players_own_setup_never_adds_TransformState_twice()
        {
            // The duplicate-190602 bug, exactly as the client log showed it:
            // "InvalidOperationException: Component TransformState added to
            // entity 21, but it already exists". First-time setup sends three
            // AddComponent batches to the player's own entity - the early
            // {1109,1207} injection, the client's stage-1 request (which always
            // contains 190602), then MirrorSendPolicy.InjectedComponents (whose
            // authoritative tail ALSO contains 190602). Unfiltered, that batch
            // re-added 190602 within setup; unmarked, the client's later
            // re-declared interest re-added it AGAIN - and every re-add re-seeds
            // TransformState to the spawn position. This test walks the fixed
            // sequence against the real InjectedComponents list, so adding a new
            // injected id without ledger-gating it breaks here first.
            const long OwnPlayer = 21;
            var ledger = new ServedComponentLedger<int>();

            // 1. Early injection {1109, 1207}, sent and marked.
            var early = ledger.UnservedOf(1, OwnPlayer, new uint[] { 1109, 1207 });
            ledger.MarkServed(1, OwnPlayer, early);

            // 2. Stage-1: the client's own request always includes 190602 (and,
            // depending on the prefab, ids that overlap the early injection).
            uint[] stageOneRequest = { 190602, 1086, 1080, 1109 };
            var stageOne = ledger.UnservedOf(1, OwnPlayer, stageOneRequest);
            Assert.DoesNotContain(1109u, stageOne); // early injection deduped
            Assert.Contains(190602u, stageOne);     // first serve of TransformState
            ledger.MarkServed(1, OwnPlayer, stageOne);

            // 3. The injected batch, filtered through the ledger: 190602 must
            // NOT go out a second time.
            var injected = ledger.UnservedOf(1, OwnPlayer, MirrorSendPolicy.InjectedComponents);
            Assert.DoesNotContain(190602u, injected);
            ledger.MarkServed(1, OwnPlayer, injected);

            // 4. The client re-declares its whole interest set for its own
            // entity (SpatialCommunicator clears and resends). Nothing already
            // delivered - 190602 above all - may be re-added.
            var redeclared = ledger.UnservedOf(1, OwnPlayer, stageOneRequest);
            Assert.Empty(redeclared);
            var redeclaredInjected = ledger.UnservedOf(1, OwnPlayer, MirrorSendPolicy.InjectedComponents);
            Assert.Empty(redeclaredInjected);
        }
    }
}
