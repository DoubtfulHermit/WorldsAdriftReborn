namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pure policy for WHERE a freshly-crafted loose part materialises and what
    /// registration key it carries - the loose-part counterpart of
    /// <see cref="BuiltShipPlacement"/>. Engine-free so the offset arithmetic and
    /// the key format are asserted natively (LoosePartPlacementTests), not by
    /// staring at a running client.
    ///
    /// A loose part is spawned NEXT TO the crafting station that made it: a short
    /// step to one side (so it does not spawn inside the console geometry) and a
    /// small height above the station's own registered Y (so it rests visibly at
    /// hand height rather than clipping the ground). It is NOT hull-relative - a
    /// loose part belongs to no ship yet, so its 190602 is seeded WORLD-ABSOLUTE
    /// (the 190602 branch does this for any entity whose key is not a bolted-part
    /// key, which a loose-part key is not).
    /// </summary>
    public static class LoosePartPlacement
    {
        /// <summary>
        /// The registration-key prefix a loose part's shared entity id is allocated
        /// from. A per-spawn sequence number and the part's schematic id are appended
        /// so every crafted part gets its own stable id on every peer and the key is
        /// self-describing in logs.
        /// </summary>
        public const string KeyPrefix = "loose-part";

        /// <summary>This loose part's registration key for spawn number <paramref name="sequence"/>.</summary>
        public static string Key(int sequence, string schematicId)
        {
            return KeyPrefix + ":" + sequence + ":" + schematicId;
        }

        /// <summary>
        /// Whether <paramref name="key"/> names a loose part. Additive to the
        /// bolted-part test in <see cref="WorldEntities.IsBoltedPartKey"/>: a loose
        /// part is deliberately NOT a bolted key, so its 190602 seeds world-absolute.
        /// </summary>
        public static bool IsLoosePartKey(string? key)
        {
            return key != null && (key == KeyPrefix || key.StartsWith(KeyPrefix + ":"));
        }

        /// <summary>Metres to one side (+X) of the station the part spawns, clear of the console.</summary>
        public const double BesideMetres = 2.0;

        /// <summary>Metres above the station's registered Y the part rests, at roughly hand height.</summary>
        public const double AboveMetres = 1.0;

        /// <summary>
        /// Where a part crafted at <paramref name="station"/> materialises: a short
        /// step to +X and <see cref="AboveMetres"/> up, so it sits beside the station
        /// at hand height. A pure function of the station position so the spawn and
        /// any later re-derivation agree and the arithmetic is unit-tested.
        /// </summary>
        public static FixedPointPosition NextTo(FixedPointPosition station)
        {
            return new FixedPointPosition(
                station.X + (long)(BesideMetres * FixedPointPosition.UnitsPerMetre),
                station.Y + (long)(AboveMetres * FixedPointPosition.UnitsPerMetre),
                station.Z);
        }
    }
}
