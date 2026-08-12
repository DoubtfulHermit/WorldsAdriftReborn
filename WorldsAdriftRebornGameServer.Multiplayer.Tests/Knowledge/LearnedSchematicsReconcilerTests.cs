using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// Learned schematics are a DERIVED function of the purchased node set. These tests
    /// pin the reconcile that RETROACTIVELY grants recipes for nodes a player already
    /// owns (the core bug: a node unlocked before its recipe-alias existed never
    /// learned it, so the bench stayed empty). The reconciler walks the REAL
    /// production resolver (KnowledgeSpendPolicy.SchematicIdsFor), so these also lock in
    /// the node -> recipe wiring the reconcile depends on.
    /// </summary>
    public class LearnedSchematicsReconcilerTests
    {
        // A stand-in catalogue: the recipe ids these tests reference. The predicate
        // mirrors the game side's SchematicHelper.Get(id) != null membership guard.
        private static readonly HashSet<string> Catalogue = new()
        {
            "shipyard", "deck", "helm", "sail", "proceduralEngineDefault",
            "atlasSkyCore", "skyCoreAtlasEnhancer", "cannonball", "campFire",
        };

        private static bool InCatalogue(string id) => Catalogue.Contains(id);

        [Fact]
        public void Reconcile_grants_recipes_for_already_purchased_nodes()
        {
            // The player unlocked Engines and Shipbuilding earlier but has an EMPTY
            // book (the purchase-time learn never fired for them). Reconcile must
            // retro-grant every recipe those nodes entitle them to.
            string[] purchased = { "EnginesRootSchematic", "Shipbuilding" };

            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                purchased, System.Array.Empty<string>(), InCatalogue);

            // Engines -> the engine; Shipbuilding -> the functional-ship baseline.
            Assert.Contains("proceduralEngineDefault", missing);
            Assert.Contains("shipyard", missing);
            Assert.Contains("deck", missing);
            Assert.Contains("helm", missing);
            Assert.Contains("sail", missing);
        }

        [Fact]
        public void Reconcile_is_idempotent_when_the_book_is_already_complete()
        {
            string[] purchased = { "EnginesRootSchematic", "Shipbuilding" };
            string[] alreadyLearned =
            {
                "proceduralEngineDefault", "shipyard", "deck", "helm", "sail",
            };

            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                purchased, alreadyLearned, InCatalogue);

            Assert.Empty(missing);
        }

        [Fact]
        public void Reconcile_grants_only_the_recipes_not_yet_learned()
        {
            string[] purchased = { "Shipbuilding" };
            string[] alreadyLearned = { "shipyard", "deck" };

            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                purchased, alreadyLearned, InCatalogue);

            Assert.Equal(new[] { "helm", "sail" }, missing);
        }

        [Fact]
        public void Reconcile_drops_ids_that_are_not_catalogue_recipes()
        {
            // A slot / un-recovered node resolves (SchematicIdsFor fall-through) to its
            // own id, which is NOT a catalogue key: the guard must drop it so nothing
            // unlearnable reaches the client.
            string[] purchased = { "EnginesSlot1", "SomeTechnologyNode" };

            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                purchased, System.Array.Empty<string>(), InCatalogue);

            Assert.Empty(missing);
        }

        [Fact]
        public void Reconcile_deduplicates_across_nodes_and_is_order_stable()
        {
            // CannonsRootSchematic and CannonsSchematicBonus1 both grant cannonball;
            // it must appear once.
            string[] purchased = { "CannonsRootSchematic", "CannonsSchematicBonus1" };

            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                purchased, System.Array.Empty<string>(), InCatalogue);

            Assert.Equal(new[] { "cannonball" }, missing);
        }

        [Fact]
        public void Reconcile_grants_every_recipe_of_a_multi_recipe_node()
        {
            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                new[] { "Atlas Core Enhancer" }, System.Array.Empty<string>(), InCatalogue);

            // The Atlas root grants the basic core AND the enhancer.
            Assert.Contains("atlasSkyCore", missing);
            Assert.Contains("skyCoreAtlasEnhancer", missing);
        }

        [Fact]
        public void Reconcile_tolerates_null_and_empty_inputs()
        {
            IReadOnlyList<string> missing = LearnedSchematicsReconciler.MissingRecipes(
                null!, null!, InCatalogue);

            Assert.Empty(missing);
        }
    }
}
