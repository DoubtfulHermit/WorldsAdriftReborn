namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// What an inventory is filed under - and the single seam the whole
    /// persistence design hangs on.
    ///
    /// THE LOAD-BEARING UNCERTAINTY. An inventory has to be keyed by something
    /// that survives a relog. The only candidate is the selected character's
    /// uid, and at the time this was written the uid had never been OBSERVED
    /// arriving at the game server - only read out of code (see
    /// docs/research/loop/findings-harvest-transaction.md section 8). So this
    /// type exists to make the key a decision with two named outcomes rather
    /// than an assumption baked into a dictionary declaration:
    ///
    /// - <see cref="ForCharacter"/> is DURABLE. It is what gets written to
    ///   Postgres and what survives a relog.
    /// - <see cref="ForSession"/> is VOLATILE. It is keyed by entity id, and
    ///   <c>EntityIdAllocator</c> never reuses an entity id, so a session key is
    ///   a NEW EMPTY INVENTORY EVERY SESSION by construction. That is the whole
    ///   reason AppearanceStore's entityId key must not be copied here.
    ///
    /// The volatile key is the FALLBACK, not the design. Choosing it must be
    /// safe rather than silently wrong: an inventory under a session key still
    /// works perfectly for the length of a session (drags land, grants appear),
    /// it is simply never written to the database, and <see cref="IsDurable"/>
    /// is the one flag every persistence call site checks. If the uid turns out
    /// not to arrive, nothing silently persists under a key that is really a
    /// session id.
    /// </summary>
    public readonly struct InventoryKey : IEquatable<InventoryKey>
    {
        private InventoryKey(string value, bool durable)
        {
            Value = value;
            IsDurable = durable;
        }

        /// <summary>The opaque key text. Only ever used as a dictionary key.</summary>
        public string Value { get; }

        /// <summary>
        /// Whether this key survives a relog. False for a session key, and every
        /// call site that writes to the database must refuse to write when it is
        /// false - see the type docs for why.
        /// </summary>
        public bool IsDurable { get; }

        /// <summary>True for the uninitialised struct, which addresses nothing.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>
        /// The durable key: one character, one inventory, forever. Formatted
        /// "character:{uid:D}" so a log line says which of the two kinds it is
        /// without the reader having to know the flag exists.
        /// </summary>
        public static InventoryKey ForCharacter(Guid characterUid)
        {
            return new InventoryKey("character:" + characterUid.ToString("D"), durable: true);
        }

        /// <summary>
        /// The volatile fallback for a player whose character uid never arrived.
        /// Keyed by entity id, which is unique per session and never reused, so
        /// this deliberately cannot be mistaken for a durable identity.
        /// </summary>
        public static InventoryKey ForSession(long entityId)
        {
            return new InventoryKey("session:" + entityId.ToString(), durable: false);
        }

        /// <summary>
        /// The character uid this key names, or null for a session key. Lets the
        /// storage boundary recover the Guid without the caller carrying it
        /// alongside the key.
        /// </summary>
        public Guid? CharacterUid
        {
            get
            {
                if (!IsDurable || Value == null || !Value.StartsWith("character:"))
                {
                    return null;
                }

                return Guid.TryParse(Value.Substring("character:".Length), out Guid uid)
                    ? uid
                    : (Guid?)null;
            }
        }

        public bool Equals(InventoryKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is InventoryKey other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? "(none)";
    }
}
