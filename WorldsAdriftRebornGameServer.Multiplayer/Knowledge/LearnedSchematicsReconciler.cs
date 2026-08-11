using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// LEARNED SCHEMATICS ARE A DERIVED FUNCTION of the nodes a player has purchased.
    ///
    /// The 1334 handler learns a node's recipe at the MOMENT of purchase, which loses
    /// any recipe for a node that was unlocked BEFORE its alias existed (the bench
    /// stays empty for already-owned nodes). This reconciler makes learned schematics
    /// self-healing: given the player's purchased node set and what they have already
    /// learned, it returns the recipes that SHOULD be learned but are not yet - each
    /// purchased node's recipe(s) via <see cref="KnowledgeSpendPolicy.SchematicIdsFor"/>
    /// that resolve to a REAL catalogue recipe and are not already in the book.
    ///
    /// Pure: the catalogue-membership check is injected as a predicate (the game side
    /// passes <c>SchematicHelper.Get(id) != null</c>, mirroring the 1334 learn guard),
    /// so the derivation is unit-tested with no catalogue file, no entity id and no
    /// socket. Idempotent - running it again after applying the result yields nothing.
    /// </summary>
    public static class LearnedSchematicsReconciler
    {
        /// <summary>
        /// The catalogue recipes a player is entitled to by their purchases but has not
        /// learned yet, in a stable order, de-duplicated. Only ids the
        /// <paramref name="isCatalogueRecipe"/> guard accepts are returned, so nothing
        /// unlearnable (a raw node id, a slot node) can reach the client and NRE its
        /// crafting-list rebuild.
        /// </summary>
        /// <param name="purchasedNodeIds">The node ids the player has bought (prog.NodeUses keys).</param>
        /// <param name="alreadyLearned">The recipes already in 1079 learnedSchematics.</param>
        /// <param name="isCatalogueRecipe">True iff an id is a real recipe in the served catalogue.</param>
        public static IReadOnlyList<string> MissingRecipes(
            IEnumerable<string> purchasedNodeIds,
            IEnumerable<string> alreadyLearned,
            Func<string, bool> isCatalogueRecipe)
        {
            if (isCatalogueRecipe == null)
            {
                throw new ArgumentNullException(nameof(isCatalogueRecipe));
            }

            HashSet<string> have = new HashSet<string>(
                alreadyLearned ?? Enumerable.Empty<string>());
            HashSet<string> added = new HashSet<string>();
            List<string> missing = new List<string>();

            foreach (string nodeId in purchasedNodeIds ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(nodeId))
                {
                    continue;
                }

                foreach (string recipe in KnowledgeSpendPolicy.SchematicIdsFor(nodeId))
                {
                    if (string.IsNullOrEmpty(recipe))
                    {
                        continue;
                    }
                    if (have.Contains(recipe) || added.Contains(recipe))
                    {
                        continue;
                    }
                    if (!isCatalogueRecipe(recipe))
                    {
                        continue;
                    }

                    missing.Add(recipe);
                    added.Add(recipe);
                }
            }

            return missing;
        }
    }
}
