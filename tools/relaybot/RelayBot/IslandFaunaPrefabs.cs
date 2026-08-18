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
        /// The identity components a creature prefab's visualisers read, plus the
        /// transform. Retail split identity by kind (1182 SpeciesState for the
        /// manta, 4322 BasicCreatureState for every jelly - see
        /// docs/research/findings-island-fauna.md), and the manta additionally
        /// carries the VARIANT PAIR its tail-picker requires: 1177 GenderState and
        /// 4326 MantaRayVariantState. Requesting those here is what turns the
        /// soak into a wire-level check that the variant fix serializes.
        ///
        /// 1166 AgeState IS IN THE MANTA SET BECAUSE THE REAL CLIENT ASKS FOR IT.
        /// The shipped MantaRay prefab's [Require] closure is fourteen components
        /// and AgeVisualizer's AgeStateReader is one of them, so a client checking
        /// out a manta requests 1166 on every checkout - it simply used to come
        /// back "[ToDo] unhandled". This harness sends a hand-written interest
        /// set rather than a prefab-derived one, so anything omitted here is a
        /// branch the soak cannot see: without this line a juveniles run would
        /// report a confident FLAT while never once serializing a calf's age.
        /// </summary>
        public static uint[] InterestSetFor(string prefabName) =>
            prefabName == "MantaRay"
                ? new[] { 190602u, 1182u, 1177u, 4326u, 1166u }
                : new[] { 190602u, 4322u };
    }
}
