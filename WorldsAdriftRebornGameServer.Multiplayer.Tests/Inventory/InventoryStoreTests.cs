using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    public class InventoryStoreTests
    {
        private static readonly Guid Alice = Guid.Parse("3f1a5e2c-8b40-4c19-9d77-0a2b6e5f1c30");
        private static readonly Guid Bob = Guid.Parse("9c4d7a11-2e63-4f08-b5aa-71d3c8e0f942");

        private static InventoryModel Seed() => InventoryTestData.Seeded();

        [Fact]
        public void Binding_an_entity_creates_its_inventory_once()
        {
            InventoryStore store = new();
            int created = 0;

            InventoryModel first = store.Bind(7, InventoryKey.ForSession(7), () => { created++; return Seed(); });
            InventoryModel again = store.Bind(7, InventoryKey.ForSession(7), () => { created++; return Seed(); });

            Assert.Same(first, again);
            Assert.Equal(1, created);
        }

        [Fact]
        public void An_unbound_entity_has_no_inventory()
        {
            InventoryStore store = new();

            Assert.Null(store.ForEntity(7));
            Assert.Null(store.KeyOf(7));
            Assert.Null(store.AllocatorForEntity(7));
        }

        [Fact]
        public void A_first_login_carries_the_session_inventory_onto_the_character_key()
        {
            // Identity arrives after checkout, so the player has already been
            // playing under a session key. Nothing they did may be lost.
            InventoryStore store = new();

            InventoryModel session = store.Bind(7, InventoryKey.ForSession(7), Seed);
            InventoryPolicy.TryGrant(session, 1200, "iron", 12, 0,
                new Dictionary<string, string>(), null, InventoryTestData.Footprints);

            InventoryModel bound = store.Rebind(7, InventoryKey.ForCharacter(Alice), Seed);

            Assert.Same(session, bound);
            Assert.NotNull(bound.ById(1200));
            Assert.Equal(InventoryKey.ForCharacter(Alice), store.KeyOf(7));
        }

        [Fact]
        public void A_relog_keeps_the_stored_inventory_and_discards_the_fresh_seed()
        {
            // THE case this whole asymmetry exists for. Carrying the session's
            // freshly seeded starter kit across would overwrite everything the
            // player owns.
            InventoryStore store = new();

            // The inventory the database already holds for this character,
            // loaded under its durable key.
            InventoryModel stored = store.Bind(0, InventoryKey.ForCharacter(Alice), Seed);
            InventoryPolicy.TryGrant(stored, 1200, "iron", 12, 0,
                new Dictionary<string, string>(), null, InventoryTestData.Footprints);

            // A new session for the same character: entity 8 this time, which
            // starts out on a session key holding a freshly seeded starter kit.
            InventoryModel session = store.Bind(8, InventoryKey.ForSession(8), Seed);
            Assert.Null(session.ById(1200));

            InventoryModel rebound = store.Rebind(8, InventoryKey.ForCharacter(Alice), Seed);

            Assert.Same(stored, rebound);
            Assert.NotNull(rebound.ById(1200));
            Assert.Null(store.ForKey(InventoryKey.ForSession(8)));
        }

        [Fact]
        public void Rebinding_to_the_same_key_is_a_no_op()
        {
            InventoryStore store = new();

            InventoryModel bound = store.Bind(7, InventoryKey.ForCharacter(Alice), Seed);

            Assert.Same(bound, store.Rebind(7, InventoryKey.ForCharacter(Alice), Seed));
        }

        [Fact]
        public void Two_characters_never_share_an_inventory()
        {
            InventoryStore store = new();

            InventoryModel a = store.Bind(7, InventoryKey.ForCharacter(Alice), Seed);
            InventoryModel b = store.Bind(8, InventoryKey.ForCharacter(Bob), Seed);

            Assert.NotSame(a, b);
            Assert.Equal(2, store.Count);
        }

        [Fact]
        public void Loading_replaces_the_contents_of_the_model_the_caller_already_holds()
        {
            // Swapping the model instance instead would strand every reference
            // to it, and the next push would carry the state nobody mutated.
            InventoryStore store = new();

            InventoryModel held = store.Bind(7, InventoryKey.ForCharacter(Alice), Seed);

            Assert.True(store.Load(InventoryKey.ForCharacter(Alice), new[]
            {
                InventoryTestData.Item(1500, "iron", 0, 0),
            }));

            Assert.Same(held, store.ForEntity(7));
            Assert.Single(held.Items);
            Assert.NotNull(held.ById(1500));
        }

        [Fact]
        public void Loading_an_unknown_key_does_nothing()
        {
            InventoryStore store = new();

            Assert.False(store.Load(InventoryKey.ForCharacter(Alice), Array.Empty<InventoryItem>()));
        }

        [Fact]
        public void The_allocator_is_primed_from_whatever_was_loaded()
        {
            // Skipping this is how a restored item gets silently eaten by a
            // later grant that reuses its id.
            InventoryStore store = new();

            store.Bind(7, InventoryKey.ForCharacter(Alice), Seed);
            store.Load(InventoryKey.ForCharacter(Alice), new[]
            {
                InventoryTestData.Item(5000, "iron", 0, 0),
            });

            Assert.Equal(5001, store.AllocatorForEntity(7)!.Next());
        }

        [Fact]
        public void Forgetting_an_entity_drops_its_binding_and_returns_the_key()
        {
            InventoryStore store = new();

            store.Bind(7, InventoryKey.ForCharacter(Alice), Seed);

            Assert.Equal(InventoryKey.ForCharacter(Alice), store.Forget(7));
            Assert.Null(store.KeyOf(7));
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void Forgetting_an_unknown_entity_is_harmless()
        {
            Assert.Null(new InventoryStore().Forget(7));
        }
    }
}
