using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// The wipe-safety rules for enabling the database against a live server.
    ///
    /// The two scenarios the persistence workstream must never get wrong are
    /// spelled out as their own facts, by name, because they are the difference
    /// between "materials persist" and "materials are silently deleted the first
    /// time Postgres hiccups". The first block tests the pure decision in
    /// isolation; the second composes it with the real InventoryStore exactly as
    /// InventoryService.BindIdentity does, so the assertion is about the actual
    /// item list a player would be left holding, not a boolean.
    /// </summary>
    public class InventoryLoadPolicyTests
    {
        private static readonly Guid Alice = Guid.Parse("3f1a5e2c-8b40-4c19-9d77-0a2b6e5f1c30");

        private static IReadOnlyList<InventoryItem> OneItem() => new[]
        {
            InventoryTestData.Item(1500, "iron", 0, 0),
        };

        // --- the pure decision -------------------------------------------------

        [Fact]
        public void A_database_error_or_missing_row_never_replaces_the_session()
        {
            // The persistence layer collapses "no row", "database unreadable" and
            // "payload unparseable" to a null stored list. All three must keep
            // whatever the session holds - a null is never a wipe.
            Assert.False(InventoryLoadPolicy.ShouldApplyStored(7, null));
            Assert.False(InventoryLoadPolicy.ShouldApplyStored(0, null));
        }

        [Fact]
        public void An_empty_stored_inventory_never_overwrites_a_non_empty_session()
        {
            // A row that parses to zero items is indistinguishable from a
            // truncated write. Applying it over a non-empty session would delete
            // everything the player owns, so it is refused.
            Assert.False(InventoryLoadPolicy.ShouldApplyStored(7, Array.Empty<InventoryItem>()));
        }

        [Fact]
        public void A_stored_inventory_with_items_always_wins()
        {
            // The relog this whole workstream exists for: the durable row is
            // authoritative when it actually carries items.
            Assert.True(InventoryLoadPolicy.ShouldApplyStored(7, OneItem()));
            Assert.True(InventoryLoadPolicy.ShouldApplyStored(0, OneItem()));
        }

        [Fact]
        public void An_empty_restore_onto_an_empty_session_is_allowed()
        {
            // Nothing to protect, nothing to lose - applying empty over empty is
            // a harmless no-op, so the rule does not need to special-case it.
            Assert.True(InventoryLoadPolicy.ShouldApplyStored(0, Array.Empty<InventoryItem>()));
        }

        // --- composed with the real store, the way BindIdentity does ----------
        //
        // BindIdentity's merge is: current = Store.ForKey(key); if
        // ShouldApplyStored(current.Count, stored) then Store.Load(key, stored).
        // These tests run that exact composition of the two production functions
        // against a real InventoryStore, so what is asserted is the item list a
        // player is left holding. The only thing not exercised is the three lines
        // of logging glue in the game-server project, which references game types
        // and Npgsql and cannot run here.

        private static bool Merge(InventoryStore store, InventoryKey key, IReadOnlyList<InventoryItem>? stored)
        {
            int currentCount = store.ForKey(key)?.Items.Count ?? 0;

            if (InventoryLoadPolicy.ShouldApplyStored(currentCount, stored))
            {
                store.Load(key, stored!);
                return true;
            }

            return false;
        }

        [Fact]
        public void A_database_error_does_not_wipe_the_in_memory_inventory()
        {
            // A player has farmed twelve iron this session. The database read then
            // throws (stored == null). Their iron must still be there.
            InventoryStore store = new();
            InventoryKey key = InventoryKey.ForCharacter(Alice);

            InventoryModel live = store.Bind(7, key, InventoryTestData.Seeded);
            InventoryPolicy.TryGrant(live, 1200, "iron", 12, 0,
                new Dictionary<string, string>(), null, InventoryTestData.Footprints);
            int before = live.Items.Count;

            bool applied = Merge(store, key, stored: null);

            Assert.False(applied);
            Assert.Equal(before, live.Items.Count);
            Assert.NotNull(live.ById(1200));
        }

        [Fact]
        public void An_empty_stored_record_does_not_overwrite_a_non_empty_in_memory_inventory()
        {
            // The database returns a row that parses to zero items - a truncated
            // or half-written payload. The live, non-empty inventory must survive.
            InventoryStore store = new();
            InventoryKey key = InventoryKey.ForCharacter(Alice);

            InventoryModel live = store.Bind(7, key, InventoryTestData.Seeded);
            InventoryPolicy.TryGrant(live, 1200, "iron", 12, 0,
                new Dictionary<string, string>(), null, InventoryTestData.Footprints);
            int before = live.Items.Count;

            bool applied = Merge(store, key, stored: Array.Empty<InventoryItem>());

            Assert.False(applied);
            Assert.Equal(before, live.Items.Count);
            Assert.NotNull(live.ById(1200));
        }

        [Fact]
        public void A_stored_record_with_items_is_restored_over_the_fresh_seed()
        {
            // The normal relog: the durable row replaces the session's starter
            // kit, and the restored item is the one the player is left holding.
            InventoryStore store = new();
            InventoryKey key = InventoryKey.ForCharacter(Alice);

            InventoryModel live = store.Bind(7, key, InventoryTestData.Seeded);
            Assert.Null(live.ById(1500));

            bool applied = Merge(store, key, stored: OneItem());

            Assert.True(applied);
            Assert.Single(live.Items);
            Assert.NotNull(live.ById(1500));
        }
    }
}
