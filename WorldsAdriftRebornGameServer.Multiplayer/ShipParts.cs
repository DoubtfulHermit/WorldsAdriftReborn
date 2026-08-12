namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The SECONDARY, cosmetic parts that make the hull read as a whole ship once
    /// the walkable <see cref="Deck"/> and the interactable <see cref="Helm"/> are
    /// on it: a <c>ModularEngine</c> aft and a <c>Sail01</c> amidships.
    ///
    /// These follow the HELM's pattern exactly, not the deck's: they render from
    /// their BAKED prefab geometry and are linked to the hull by 8066, and the
    /// server seeds NONE of their special-visualizer components (12281
    /// ModularShipPartState, 1303 SailState). Those visualizers stay dormant - so
    /// there is no engine exhaust VFX and no sail cloth physics - but the parts
    /// still appear, which is all "reads as a complete ship" needs.
    ///
    /// ASSUMPTION, marked honestly and the reason these are env-gated OFF by
    /// default: that ModularEngine/Sail01 render from baked geometry WITHOUT their
    /// visualizer (as the helm demonstrably does) has NOT been checked against a
    /// running client. If either shows up invisible, its mesh is visualizer-built
    /// and it needs its 12281/1303 seed - a follow-up, not a regression, because
    /// best-effort interest means a missing branch just leaves that one part
    /// inert, never the deck or the helm.
    /// </summary>
    public static class ShipParts
    {
        /// <summary>The aft engine prefab. Bare name; client appends the worker suffix.</summary>
        public const string EngineAssetName = "ModularEngine";

        /// <summary>The engine's registration key.</summary>
        public const string EngineKey = "engine-haven";

        /// <summary>The amidships sail prefab. Bare name; client appends the worker suffix.</summary>
        public const string SailAssetName = "Sail01";

        /// <summary>The sail's registration key.</summary>
        public const string SailKey = "sail-haven";

        /// <summary>
        /// Metres aft (-Z) the engine sits from the hull centre. Behind the 4 m
        /// deck's rear edge (z = -2 at scale) so the exhaust would point off the
        /// stern. APPROXIMATE - the engine prefab's own pivot has not been eyeballed
        /// against a client.
        /// </summary>
        public const double EngineAftMetres = -1.5;

        /// <summary>
        /// Metres up the engine sits, mounted on the deck plane. Zero, same reason
        /// as <see cref="Deck.DeckUpMetres"/>.
        /// </summary>
        public const double EngineUpMetres = 0.0;

        /// <summary>
        /// Metres fore/aft the sail sits from the hull centre. Amidships (0), between
        /// the helm at +1 and the engine at -1.5, so the mast rises from the middle
        /// of the deck. APPROXIMATE.
        /// </summary>
        public const double SailForwardMetres = 0.0;

        /// <summary>Metres up the sail's base sits. Zero - the mast rises from baked geometry.</summary>
        public const double SailUpMetres = 0.0;

        /// <summary>The engine's global 190602 seed: hull registration plus the aft offset.</summary>
        public static FixedPointPosition EngineOnHull(FixedPointPosition hull)
        {
            return new FixedPointPosition(
                hull.X,
                hull.Y + (long)(EngineUpMetres * FixedPointPosition.UnitsPerMetre),
                hull.Z + (long)(EngineAftMetres * FixedPointPosition.UnitsPerMetre));
        }

        /// <summary>The sail's global 190602 seed: hull registration plus the amidships offset.</summary>
        public static FixedPointPosition SailOnHull(FixedPointPosition hull)
        {
            return new FixedPointPosition(
                hull.X,
                hull.Y + (long)(SailUpMetres * FixedPointPosition.UnitsPerMetre),
                hull.Z + (long)(SailForwardMetres * FixedPointPosition.UnitsPerMetre));
        }
    }
}
