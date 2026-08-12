namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The PURE, engine-free rule that turns "which player owns this entity" into the
    /// per-player values the client's identity gates read out of 1086 PlayerName.
    ///
    /// WHY playerId == characterUid. The client keys its ownership gates on TWO nominally
    /// different axes - PlayerId (1086 field2) and CharacterUid (1086 field3) - but one
    /// live gate crosses them: the "attach part to owned ship" quest calls
    /// <c>ShipVisualizer.IsShipOwner(LocalPlayer.PlayerId)</c>
    /// (ShipPartAttachedToOwnedShipCondition.cs:85-92) and <c>IsShipOwner</c> compares its
    /// argument against the ship's owner CHARACTER uid (8062/4349). For that to ever be
    /// true, the value the client exposes as <c>PlayerId</c> must equal the durable
    /// character uid. So this policy serves field2 and field3 as the SAME per-player value:
    /// the durable character GUID. Then every gate lines up:
    ///   - GATE A shipyard build-access: registered list contains the owner's playerId
    ///     (== owner characterUid); each checker compares its own PlayerId; owner passes,
    ///     others fail.
    ///   - GATE B ship ownership: 8062/4349 hold the owner characterUid; the client's
    ///     SelectedCharacterUid matches it; unchanged and still correct.
    ///   - logout (ShipVisualizer/LogoutBehaviour) compares field3 CharacterUid against the
    ///     ship owner uid; equal for the owner's own ship.
    ///   - PlayerNameSystem dedups labels by field3 CharacterUid; distinct per player, so
    ///     two peers no longer collapse to one label.
    ///
    /// VOLATILE PLAYERS. When the durable character uid never arrived (the 1088 round trip
    /// did not happen this session, <see cref="Game"/> CharacterOwnership returns ""), the
    /// player owns nothing - exactly as their inventory is never persisted. We still give
    /// them a DISTINCT per-entity synthetic id so two such players do not share one label
    /// or accidentally satisfy an ownership Contains() check; the synthetic id matches no
    /// stored owner uid, so they own nothing, which is correct.
    ///
    /// FLAG-GATED. The whole per-player behaviour is behind <see cref="EnvVar"/>; when it is
    /// off the caller serves the legacy stubs byte-identically. This is deliberately
    /// reversible: per-player identity changes 2-player ownership SEMANTICS and must be
    /// proven in a 2-player soak (owner passes, non-owner denied, no desync) before it is
    /// trusted, and rolled back instantly by clearing one env var if the uid round trip is
    /// not landing in production.
    /// </summary>
    public static class PlayerIdentity
    {
        /// <summary>The env var that turns per-player identity on. Off/absent = legacy stubs.</summary>
        public const string EnvVar = "WAREBORN_PER_PLAYER_IDENTITY";

        // The legacy stub values, kept byte-identical to the pre-fix 1086 serve so the
        // flag-off path cannot drift. LegacyPlayerId is the same string as
        // LocalPlayerIdentity.PlayerId.
        public const string LegacyDisplayName = "sp00ktober";
        public const string LegacyPlayerId = "id";
        public const string LegacyCharacterUid = "cUid";
        public const string LegacyBossaToken = "bossaToken";
        public const string LegacyBossaId = "bossaId";

        /// <summary>
        /// The per-player identity value served in BOTH 1086 field2 (playerId) and field3
        /// (characterUid): the durable character uid when it arrived, else a distinct
        /// per-session synthetic that owns nothing. Total; never throws.
        /// </summary>
        public static string IdFor(string? durableCharacterUid, long entityId)
        {
            return string.IsNullOrEmpty(durableCharacterUid)
                ? "session:" + entityId.ToString()
                : durableCharacterUid!;
        }

        /// <summary>
        /// A DISTINCT display name for a player. The server has no real account name (no
        /// login-name channel crosses the ENet wire), so this derives a stable suffix from
        /// the player's id: its only job is to keep two peers' labels from reading as one.
        /// Cosmetic - the client's label dedup keys on characterUid, not on this string.
        /// </summary>
        public static string DisplayNameFor(string? durableCharacterUid, long entityId)
        {
            return "Traveller-" + ShortTag(IdFor(durableCharacterUid, entityId));
        }

        /// <summary>
        /// The owner's playerId to put in a shipyard's 1205 registration and its 1206
        /// ownerPlayerId. Because playerId == characterUid, this is the owner's character
        /// uid itself; an empty (unowned) uid stays empty so nobody is registered. Does NOT
        /// take an entityId: an owned yard's owner uid is non-empty by definition, so the
        /// synthetic-session fallback never applies to an owner.
        /// </summary>
        public static string OwnerPlayerId(string? ownerCharacterUid)
        {
            return string.IsNullOrEmpty(ownerCharacterUid) ? "" : ownerCharacterUid!;
        }

        /// <summary>
        /// Parses the flag from a raw env string. Truthy: 1/true/yes/on (case- and
        /// whitespace-insensitive); everything else, including null/empty, is OFF.
        /// </summary>
        public static bool ParseEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value!.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Reads <see cref="EnvVar"/> from the real environment. The one impure call.</summary>
        public static bool EnabledFromEnvironment()
        {
            return ParseEnabled(System.Environment.GetEnvironmentVariable(EnvVar));
        }

        /// <summary>
        /// A stable 8-hex FNV-1a tag of an id, so distinct ids get distinct display
        /// suffixes without exposing the raw uid in the label.
        /// </summary>
        private static string ShortTag(string id)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in id)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash.ToString("x8");
            }
        }
    }
}
