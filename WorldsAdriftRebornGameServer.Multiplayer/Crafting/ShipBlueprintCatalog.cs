using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// One player's list of buildable ship blueprints - the strings served in the 1274
    /// <c>GsimShipBlueprintInteractionState.shipBlueprintList.availableBlueprints</c>
    /// that fill the SHIP BLUEPRINTS list. Clicking one fires
    /// <c>SetShipBlueprint(shipyard, thatString)</c> on 1270, so each entry IS a
    /// blueprintId the server maps to a recipe.
    ///
    /// Seeded on first touch with exactly one <see cref="DefaultBlueprintId"/> so the
    /// list is never empty and the user can select a cost bill without first saving a
    /// design. <see cref="Save"/> adds the player's edited design as a new named
    /// blueprint (dedup + rename-in-place on a repeat id). Engine-free and in-session
    /// only; disk persistence is a documented follow-on, same as
    /// <c>PlayerShipDesigns</c>.
    /// </summary>
    public sealed class PlayerShipBlueprints
    {
        /// <summary>The always-present starter blueprint the SHIP BLUEPRINTS list opens with.</summary>
        public const string DefaultBlueprintId = "Makeshift Ship";

        private readonly List<string> _blueprints = new List<string> { DefaultBlueprintId };

        /// <summary>The available blueprint ids, in insertion order (default first).</summary>
        public IReadOnlyList<string> Available => _blueprints;

        /// <summary>Whether an id is already a known blueprint.</summary>
        public bool Contains(string blueprintId) => _blueprints.Contains(blueprintId);

        /// <summary>
        /// SaveBlueprint(newId): add the current design as a buildable blueprint under
        /// <paramref name="newId"/>. A null/empty id is rejected (returns false); a
        /// duplicate id is a no-op that still returns true so the client's save resolves.
        /// Returns true when the list now contains the id.
        /// </summary>
        public bool Save(string newId)
        {
            if (string.IsNullOrEmpty(newId))
            {
                return false;
            }
            if (!_blueprints.Contains(newId))
            {
                _blueprints.Add(newId);
            }
            return true;
        }
    }

    /// <summary>Process-global registry of per-player blueprint catalogs, keyed by player entity id.</summary>
    public static class ShipBlueprintCatalogStore
    {
        private static readonly Dictionary<long, PlayerShipBlueprints> ByEntity =
            new Dictionary<long, PlayerShipBlueprints>();

        /// <summary>The player's catalog, created (seeded with the default blueprint) on first touch.</summary>
        public static PlayerShipBlueprints For(long entityId)
        {
            if (!ByEntity.TryGetValue(entityId, out PlayerShipBlueprints? c))
            {
                c = new PlayerShipBlueprints();
                ByEntity[entityId] = c;
            }
            return c;
        }

        /// <summary>Drop a player's catalog when their entity leaves.</summary>
        public static void Forget(long entityId) => ByEntity.Remove(entityId);
    }
}
