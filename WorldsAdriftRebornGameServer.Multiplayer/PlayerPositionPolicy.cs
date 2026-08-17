namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>Why a stored logout position was or was not used.</summary>
    public enum PositionRestoreVerdict
    {
        /// <summary>Put the player back where they logged out.</summary>
        Restore,

        /// <summary>Nothing stored. First login for this character.</summary>
        NoStoredPosition,

        /// <summary>Stored, but below the world's deep safety net.</summary>
        BelowTheWorld,

        /// <summary>Stored, but outside the release world's 36 km box.</summary>
        OutsideTheWorld,

        /// <summary>Stored, but already the spawn point, so restoring is a no-op.</summary>
        AlreadyAtSpawn,
    }

    /// <summary>
    /// Whether a character's stored logout position may be used, and when a live
    /// position is worth writing down.
    ///
    /// A restore is the one operation that can put a player somewhere they cannot
    /// get out of, so it fails toward the spawn point rather than toward the
    /// stored value. The server has no terrain query - no raycast, no collider,
    /// no loaded height table - so "is this inside a rock" CANNOT be answered
    /// here, and this policy does not pretend to: it rejects only the coarse,
    /// catastrophic cases (under the world, outside the world) that are decidable
    /// from coordinates alone. Anything finer needs the fall rescue that already
    /// exists, which is why restoring below the deep net is refused outright.
    /// </summary>
    public static class PlayerPositionPolicy
    {
        /// <summary>
        /// Half-width of the release world plus generous slack. The authored map
        /// spans roughly -16.9 km to +14.7 km on both horizontal axes inside a
        /// 36 km boundary; anything beyond this is a corrupt or hand-edited row,
        /// not a place a player walked to.
        /// </summary>
        public const double WorldHalfWidthMetres = 20000.0;

        /// <summary>
        /// How far a player must move before a periodic save is worth a database
        /// write. Small enough that a disconnect loses only a few paces, large
        /// enough that standing still never writes.
        /// </summary>
        public const double SaveMovementThresholdMetres = 8.0;

        public static PositionRestoreVerdict Decide(
            FixedPointPosition? stored, FixedPointPosition spawn)
        {
            if (!stored.HasValue) return PositionRestoreVerdict.NoStoredPosition;
            FixedPointPosition position = stored.Value;

            if (FallPolicy.IsBelowDeepFloor(position)) return PositionRestoreVerdict.BelowTheWorld;
            if (!IsInsideTheWorldBox(position)) return PositionRestoreVerdict.OutsideTheWorld;
            if (position == spawn) return PositionRestoreVerdict.AlreadyAtSpawn;

            return PositionRestoreVerdict.Restore;
        }

        public static bool IsInsideTheWorldBox(FixedPointPosition position)
        {
            long limit = (long)(WorldHalfWidthMetres * FixedPointPosition.UnitsPerMetre);
            return position.X >= -limit && position.X <= limit
                && position.Z >= -limit && position.Z <= limit;
        }

        /// <summary>
        /// Whether a live position differs enough from the last written one to be
        /// worth saving. The first save of a session always counts.
        /// </summary>
        public static bool ShouldSave(FixedPointPosition? lastSaved, FixedPointPosition current)
        {
            if (!lastSaved.HasValue) return true;
            return MetresBetween(lastSaved.Value, current) >= SaveMovementThresholdMetres;
        }

        public static double MetresBetween(FixedPointPosition a, FixedPointPosition b)
        {
            double dx = (double)(a.X - b.X) / FixedPointPosition.UnitsPerMetre;
            double dy = (double)(a.Y - b.Y) / FixedPointPosition.UnitsPerMetre;
            double dz = (double)(a.Z - b.Z) / FixedPointPosition.UnitsPerMetre;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>An operator-facing reason, for the one line the server logs.</summary>
        public static string Explain(PositionRestoreVerdict verdict) => verdict switch
        {
            PositionRestoreVerdict.Restore => "restoring the stored logout position",
            PositionRestoreVerdict.NoStoredPosition => "no stored position; using the spawn point",
            PositionRestoreVerdict.BelowTheWorld =>
                "stored position is below the deep safety net; using the spawn point",
            PositionRestoreVerdict.OutsideTheWorld =>
                "stored position is outside the world box; using the spawn point",
            PositionRestoreVerdict.AlreadyAtSpawn => "stored position is the spawn point",
            _ => "unknown verdict",
        };
    }
}
