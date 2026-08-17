namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// The up-front realizability gate for a STATION craft: may this craft
    /// consume materials at all, given the loose part it would spawn?
    ///
    /// THE RULE IT ENFORCES - "no craft may eat materials it cannot show":
    /// a station craft's output is a world entity the client instantiates from a
    /// prefab name. If the client cannot resolve that name, the entity is
    /// created server-side, the materials are gone, and the player sees NOTHING
    /// (MissingComponentException client-side, no world object) - the recurring
    /// "crafted X, resources eaten, nothing appears" bug. The only honest moment
    /// to stop that is BEFORE the consume, so the handler calls this first and a
    /// refusal costs the player nothing.
    ///
    /// The check runs on the EFFECTIVE prefab (after the WAREBORN_PART_PREFAB__*
    /// env overrides), because the override is applied at spawn time and a typo
    /// there is exactly the case the catalogue's compile-time pins cannot see.
    ///
    /// Pure: the census membership test is a delegate so this is testable
    /// without the embedded resource; the server passes
    /// ClientEntityPrefabs.CanResolve.
    /// </summary>
    public static class StationCraftOutputGate
    {
        /// <summary>
        /// Whether a station craft whose output would spawn with
        /// <paramref name="effectivePrefabName"/> is allowed to consume materials.
        /// On refusal, <paramref name="reason"/> is the wire-safe, player-facing
        /// string for CraftingValidationFailed.
        /// </summary>
        public static bool CanRealize(string? effectivePrefabName, Func<string?, bool> clientCanResolve, out string reason)
        {
            if (clientCanResolve == null)
            {
                throw new ArgumentNullException(nameof(clientCanResolve));
            }

            if (string.IsNullOrWhiteSpace(effectivePrefabName))
            {
                reason = "this part has no output prefab; crafting it would eat your materials for nothing";
                return false;
            }

            if (!clientCanResolve(effectivePrefabName))
            {
                reason = "output prefab '" + effectivePrefabName
                    + "' is not a loadable client asset; crafting refused so your materials are not lost";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
