namespace RelayBot
{
    /// <summary>
    /// Which prefab names the server serves as island fauna, so the harness can
    /// recognise a creature checkout without guessing. The list mirrors
    /// IslandFaunaPolicy.PrefabNameFor on the server: one manta prefab and the
    /// jelly prefabs (one generic today; the four retail species -
    /// SeedPodJelly, FlowerPodJelly, DesertPod, DesertPodB - are listed ahead of
    /// the server serving them so the gate keeps seeing fauna the day it does).
    /// </summary>
    public static class IslandFaunaPrefabs
    {
        public static bool IsCreature(string prefabName) =>
            prefabName is "MantaRay" or "JellyFish"
                or "SeedPodJelly" or "FlowerPodJelly" or "DesertPod" or "DesertPodB";

        /// <summary>
        /// The identity component a creature prefab's visualisers read: 1182
        /// SpeciesState for the manta, 4322 BasicCreatureState for every jelly
        /// (retail split them by kind - see docs/research/findings-island-fauna.md).
        /// </summary>
        public static uint IdentityComponentFor(string prefabName) =>
            prefabName == "MantaRay" ? 1182u : 4322u;
    }
}
