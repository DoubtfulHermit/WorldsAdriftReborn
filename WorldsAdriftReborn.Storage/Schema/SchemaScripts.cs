namespace WorldsAdriftReborn.Storage.Schema
{
    /// <summary>
    /// The schema, one script per version, in order.
    ///
    /// APPEND ONLY. A script that has ever shipped has already run against a real
    /// database; editing it means the schema on disk depends on when the operator
    /// last updated, which is the one failure mode a migration system exists to
    /// prevent. To change something, add the next script.
    ///
    /// The constraints are not decoration. Each one makes a specific, documented
    /// client misbehaviour unrepresentable - at eight players the database's speed
    /// is irrelevant and its ability to refuse bad data is the entire reason there
    /// is a schema at all. The comments name the misbehaviour.
    /// </summary>
    public static class SchemaScripts
    {
        /// <summary>
        /// Real characters per account. Mirrors RosterPolicy.MaxCharacters in
        /// WorldsAdriftServer, which this library cannot reference (it must name
        /// no game type). The roster the client receives is this plus one
        /// trailing empty slot, so slot indices run 0..MaxCharacters.
        /// </summary>
        public const int MaxCharacters = 5;

        /// <summary>
        /// Advisory lock key held while migrating, so two servers starting
        /// together do not both try to create the schema. An arbitrary constant;
        /// its only requirement is that nothing else in this database uses it.
        /// </summary>
        public const long MigrationLockKey = 7716183509442057L;

        /// <summary>
        /// Bootstrap, run before every migration check and outside the versioned
        /// sequence: the thing that records which version we are at cannot itself
        /// be versioned.
        ///
        /// Postgres has no equivalent of the single integer an embedded database
        /// keeps in its header, so this is a table - shaped so that a second row,
        /// which would make "the version" ambiguous, cannot exist.
        /// </summary>
        internal const string VersionTable = @"
CREATE TABLE IF NOT EXISTS schema_version (
    only_row BOOLEAN NOT NULL PRIMARY KEY DEFAULT TRUE CHECK (only_row),
    version  INTEGER NOT NULL CHECK (version >= 0)
);

INSERT INTO schema_version (only_row, version)
VALUES (TRUE, 0)
ON CONFLICT (only_row) DO NOTHING;
";

        /// <summary>
        /// Every script, oldest first. Index i takes the database from version i
        /// to version i+1, so <c>All.Count</c> is the current version.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = new[] { V1, V2, V3, V4, V5, V6 };

        /// <summary>
        /// v1 - accounts, sessions, characters.
        ///
        /// Inventory and progression are deliberately absent: they belong to the
        /// game server, which is not deployed in the same step as the login
        /// server (restarting the login server is safe; restarting the game
        /// server orphans every connected client).
        /// </summary>
        internal const string V1 = @"
CREATE TABLE accounts (
    account_id      BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- The lookup key: username lowercased. A normalised column rather than
    -- CITEXT so that nothing depends on an extension being installed in a
    -- database somebody else's Docker container owns. The CHECK is what keeps it
    -- honest: a call site that inserts the typed form here would otherwise make
    -- 'Timu' and 'timu' two accounts, and only for players who capitalise.
    username_key    TEXT        NOT NULL UNIQUE,
    username        TEXT        NOT NULL,

    -- screenName on the /authenticate response. Read unconditionally on the
    -- password path (BossaNetBootstrap:409) with no null guard: absent or empty
    -- and the client throws, catches, and shows the 'Connection Error ... QUIT'
    -- dialog. An empty display name is therefore a dead menu, so it is refused
    -- here rather than at whichever call site forgets.
    display_name    TEXT        NOT NULL,

    -- pbkdf2$sha256$210000$<salt>$<hash>, produced by AccountPolicy. The shape
    -- check below is not cryptography; it refuses a cleartext password written
    -- into this column by a future call site that forgot to hash.
    password_hash   TEXT        NOT NULL,

    -- The 64-bit SteamID, once a password login has opportunistically linked it.
    -- NULL until then, and NULL forever for a player with no Steam client.
    steam_user_key  TEXT        NULL,

    created_at      TIMESTAMPTZ NOT NULL,
    last_login_at   TIMESTAMPTZ NULL,

    CONSTRAINT accounts_username_key_is_lowercase
        CHECK (username_key = lower(username_key) AND length(username_key) > 0),
    CONSTRAINT accounts_username_not_blank
        CHECK (length(btrim(username)) > 0),
    CONSTRAINT accounts_display_name_not_blank
        CHECK (length(btrim(display_name)) > 0),
    CONSTRAINT accounts_password_hash_is_hashed
        CHECK (length(password_hash) >= 8 AND position('$' IN password_hash) > 0),
    CONSTRAINT accounts_steam_user_key_not_blank
        CHECK (steam_user_key IS NULL OR length(btrim(steam_user_key)) > 0),
    CONSTRAINT accounts_last_login_after_created
        CHECK (last_login_at IS NULL OR last_login_at >= created_at)
);

-- Two accounts claiming one SteamID is the mid-session token swap that would
-- look like corruption: the 28-minute refresh re-authenticates Steam-only, and
-- if that resolved to a different account the player's roster identity would
-- flip mid-session. Partial, because NULL is the normal state and any number of
-- accounts may be unlinked.
CREATE UNIQUE INDEX ux_accounts_steam_user_key
    ON accounts (steam_user_key)
    WHERE steam_user_key IS NOT NULL;

CREATE TABLE sessions (
    -- 32 random bytes, base64url. A bearer credential, not a routing key: keep
    -- it out of logs.
    token         TEXT        NOT NULL PRIMARY KEY,

    account_id    BIGINT      NOT NULL REFERENCES accounts (account_id) ON DELETE CASCADE,

    issued_at     TIMESTAMPTZ NOT NULL,
    last_seen_at  TIMESTAMPTZ NOT NULL,

    -- Sliding, 30 days. A failing token refresh is silent and terminal on the
    -- client - the no-linked-account callback is an empty delegate and no
    -- further refresh is scheduled - so a token that expires inside a session
    -- produces a player who is simply stuck, with nothing on screen to say why.
    expires_at    TIMESTAMPTZ NOT NULL,

    CONSTRAINT sessions_token_long_enough
        CHECK (length(token) >= 32),
    CONSTRAINT sessions_expiry_after_issue
        CHECK (expires_at > issued_at),
    CONSTRAINT sessions_last_seen_after_issue
        CHECK (last_seen_at >= issued_at)
);

CREATE INDEX ix_sessions_account_id ON sessions (account_id);

CREATE TABLE characters (
    -- UUID, not text. Bossa's SocialHelper checks for a '-' and then calls
    -- new Guid(uid); the upstream placeholder 'valid-UIDs-have-at-least-one-'
    -- passes the first check and throws on the second. As a uuid column that
    -- placeholder is not storable at all - the type does the work a CHECK
    -- constraint would otherwise have to.
    character_uid  UUID        NOT NULL PRIMARY KEY,

    account_id     BIGINT      NOT NULL REFERENCES accounts (account_id) ON DELETE CASCADE,

    -- Shown in the character list. Empty renders a nameless row the player
    -- cannot tell apart from the create-new slot.
    name           TEXT        NOT NULL,

    -- Position in the roster the client receives. It renumbers these by array
    -- index anyway, so the value's job is to make the order stable across
    -- restarts, not to be authoritative.
    slot_index     INTEGER     NOT NULL,

    -- The client decides a slot is empty by Cosmetics == null, and an entry with
    -- a non-null but empty Cosmetics dictionary is read as a real character and
    -- then dereferenced (an NRE in CharacterCustomisationVisualizer). Storing
    -- the emptiness as its own column keeps that decision out of JSON parsing.
    is_empty_slot  BOOLEAN     NOT NULL DEFAULT FALSE,

    -- Appearance and cosmetics, round-tripped byte-faithfully. Deliberately not
    -- relational and deliberately TEXT rather than JSONB: nothing queries or
    -- constrains it, the client is the only thing that understands it, and JSONB
    -- would reorder keys and normalise numbers, which is the opposite of
    -- byte-faithful. An empty string here means an unguarded TryGetValue on
    -- every icon update.
    data_json      TEXT        NOT NULL,

    created_at     TIMESTAMPTZ NOT NULL,
    updated_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT characters_name_not_blank
        CHECK (length(btrim(name)) > 0),
    CONSTRAINT characters_slot_in_range
        CHECK (slot_index >= 0 AND slot_index <= 5),
    CONSTRAINT characters_data_json_not_empty
        CHECK (length(data_json) > 0),
    CONSTRAINT characters_updated_after_created
        CHECK (updated_at >= created_at)
);

-- Two characters in one slot renders one of them and silently loses the other.
CREATE UNIQUE INDEX ux_characters_account_slot
    ON characters (account_id, slot_index);

-- The client only ever shows one create-a-character slot; a second one is a row
-- the player can select and then cannot use.
CREATE UNIQUE INDEX ux_characters_account_empty_slot
    ON characters (account_id)
    WHERE is_empty_slot;

CREATE INDEX ix_characters_account_id ON characters (account_id);
";

        /// <summary>
        /// v2 - character inventories.
        ///
        /// v1's comment said inventory belongs to the game server and therefore
        /// not here. That reasoning was about DEPLOYMENT - restarting the login
        /// server is safe, restarting the game server orphans every connected
        /// client - and it argued against coupling the two, not against storing
        /// the data. Appending a table the login server never writes couples
        /// nothing: the game server owns every row, and the only thing the login
        /// server contributes is the cascade that removes a deleted character's
        /// inventory along with the character.
        ///
        /// The alternative on the table was a JSON file next to the game server,
        /// in the shape of WorldsAdriftServer's JsonFileStore. It was rejected
        /// because the key is a character uid, the thing that says a character
        /// uid is real is the characters table, and a file has no way to enforce
        /// that - a typo'd or absent uid would create a file that looks like an
        /// inventory and belongs to nobody.
        /// </summary>
        internal const string V2 = @"
CREATE TABLE character_inventories (
    -- One inventory per character, and the foreign key is the point rather than
    -- decoration: the game server derives this uid from a JSON blob a client
    -- published, so it is the one key in this database that arrives from
    -- outside. A uid that names no character is refused here instead of
    -- creating an inventory that belongs to nobody and that no login can ever
    -- find again.
    --
    -- CASCADE because a deleted character's inventory is unreachable by
    -- definition: nothing but the character uid can address it.
    character_uid  UUID        NOT NULL PRIMARY KEY
                               REFERENCES characters (character_uid) ON DELETE CASCADE,

    -- The item list, written by the game server's InventorySnapshot and
    -- understood by nothing else. TEXT rather than JSONB for the same reason as
    -- characters.data_json: nothing queries it, and JSONB would reorder keys and
    -- normalise numbers.
    --
    -- The CHECK is not paranoia. An empty payload restores as an inventory with
    -- no grid, and the client reads width and height EXACTLY ONCE, at
    -- InventoryVisualiser.OnEnable - so a zero-sized grid cannot be corrected by
    -- any later update, only by another checkout.
    data_json      TEXT        NOT NULL,

    created_at     TIMESTAMPTZ NOT NULL,
    updated_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT character_inventories_data_json_not_empty
        CHECK (length(btrim(data_json)) > 0),
    CONSTRAINT character_inventories_updated_after_created
        CHECK (updated_at >= created_at)
);
";

        /// <summary>
        /// v3 - operator server configuration.
        ///
        /// A key-value table, not a column on a one-row table, because settings
        /// arrive one at a time and a KV shape lets the next one (a MOTD, a
        /// player cap) be an INSERT rather than a migration. It is written only
        /// by the login server's admin panel and read on every /deploymentStatus,
        /// so it belongs to the login server outright - the game server never
        /// touches it.
        ///
        /// The single row that matters today is 'server_name', the string the
        /// in-game server browser shows. Its value used to be a hardcoded literal
        /// at the call site; storing it here is what lets the operator change it
        /// without a redeploy. The CHECKs mirror ServerConfigPolicy so a value
        /// the panel would refuse cannot be written by any other path either.
        /// </summary>
        internal const string V3 = @"
CREATE TABLE server_config (
    key        TEXT        NOT NULL PRIMARY KEY,
    value      TEXT        NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT server_config_key_not_blank
        CHECK (length(btrim(key)) > 0),
    -- An empty value renders a nameless browser row indistinguishable from a
    -- server that is not reporting. Refused here as well as in the panel.
    CONSTRAINT server_config_value_not_blank
        CHECK (length(btrim(value)) > 0)
);
";

        /// <summary>
        /// v4 - character knowledge / progression.
        ///
        /// The exact sibling of v2's character_inventories, and appended for the
        /// same reasons: the game server is the only writer, the login server
        /// contributes nothing but the ON DELETE CASCADE that removes a deleted
        /// character's progression with them, and the payload is opaque JSON the
        /// database stores but does not understand.
        ///
        /// Knowledge (spendable points, the lifetime tally, purchased tree nodes,
        /// learned schematics and the scanned-databank ledger) lived only in the
        /// game server's in-memory ProgressionStore until now and was lost on
        /// every restart. It is keyed by character uid exactly like the inventory,
        /// so it survives a relog and a restart the same way.
        /// </summary>
        internal const string V4 = @"
CREATE TABLE character_progression (
    -- One progression per character; the foreign key is the point, not
    -- decoration. The game server derives this uid from a JSON blob a client
    -- published, so a uid that names no character is refused here rather than
    -- creating progression that belongs to nobody.
    --
    -- CASCADE because a deleted character's progression is unreachable by
    -- definition: nothing but the character uid can address it.
    character_uid  UUID        NOT NULL PRIMARY KEY
                               REFERENCES characters (character_uid) ON DELETE CASCADE,

    -- Knowledge totals, node uses, learned schematics and the scanned ledger,
    -- written by the game server's ProgressionSnapshot and understood by nothing
    -- else. TEXT rather than JSONB for the same reason as the other payloads:
    -- nothing queries it, and JSONB would reorder keys and normalise numbers.
    data_json      TEXT        NOT NULL,

    created_at     TIMESTAMPTZ NOT NULL,
    updated_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT character_progression_data_json_not_empty
        CHECK (length(btrim(data_json)) > 0),
    CONSTRAINT character_progression_updated_after_created
        CHECK (updated_at >= created_at)
);
";

        /// <summary>
        /// v5 - where a character logged out.
        ///
        /// Until now every player was placed at one compile-time constant, so a
        /// relog put you back on Haven while your ship stayed where you left it.
        ///
        /// Unlike v2/v4 this payload is NOT opaque JSON: it is three fixed-point
        /// world coordinates, stored as the exact Q52.12 integers the simulation
        /// uses. Writing them as columns rather than a blob is deliberate - a
        /// stored position is the one piece of per-character state an operator may
        /// genuinely need to inspect or correct by hand when a player reports being
        /// stuck, and a float round-trip would move them.
        ///
        /// Deliberately NOT stored: which ship they stood on. Hull entity ids are
        /// allocated at boot and are not durable across a restart, so a ship-
        /// relative restore needs a durable ship identity that does not exist yet.
        /// A hull that has not moved is landed on correctly by world position alone.
        /// </summary>
        internal const string V5 = @"
CREATE TABLE character_positions (
    -- Same key and same CASCADE as the inventory and progression tables: a
    -- position that names no character is unreachable and is refused here.
    character_uid  UUID        NOT NULL PRIMARY KEY
                               REFERENCES characters (character_uid) ON DELETE CASCADE,

    -- Q52.12 fixed point, the simulation's own units, NOT metres. Stored exactly
    -- so a save/restore round trip cannot drift a player through the floor.
    x              BIGINT      NOT NULL,
    y              BIGINT      NOT NULL,
    z              BIGINT      NOT NULL,

    created_at     TIMESTAMPTZ NOT NULL,
    updated_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT character_positions_updated_after_created
        CHECK (updated_at >= created_at)
);
";

        /// <summary>
        /// v6 - crews.
        ///
        /// The first table in this database describing a relationship BETWEEN
        /// characters rather than state belonging to one, which is why it is two
        /// tables and not a JSON blob on the leader: a crew has to be queryable
        /// from either end. When a player connects the server must answer "which
        /// crew is this character in", and when a crew changes it must answer "who
        /// else must be told", and a blob keyed by leader answers neither without
        /// scanning every row.
        ///
        /// Invites are deliberately NOT persisted. They are transient social
        /// offers held on the invitee's live component; losing them on restart
        /// costs a player one click and keeps this schema to the durable facts.
        /// </summary>
        internal const string V6 = @"
CREATE TABLE crews (
    crew_id        TEXT        NOT NULL PRIMARY KEY,

    -- The leader is a member like any other, so this is a denormalised pointer
    -- INTO crew_members rather than a separate role. It is not a foreign key to
    -- crew_members because the two rows are written in one transaction and the
    -- ordering would make either direction impossible to insert first.
    leader_uid     UUID        NOT NULL
                               REFERENCES characters (character_uid) ON DELETE CASCADE,

    num_slots      INT         NOT NULL,

    created_at     TIMESTAMPTZ NOT NULL,
    updated_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT crews_num_slots_sane CHECK (num_slots BETWEEN 1 AND 8),
    CONSTRAINT crews_updated_after_created CHECK (updated_at >= created_at)
);

CREATE TABLE crew_members (
    -- The PRIMARY KEY is the CHARACTER, not (crew, character). That is the whole
    -- point: a character can be in at most one crew, and making that a key means
    -- the database refuses a double membership rather than trusting every code
    -- path that ever writes here to check first.
    character_uid  UUID        NOT NULL PRIMARY KEY
                               REFERENCES characters (character_uid) ON DELETE CASCADE,

    crew_id        TEXT        NOT NULL
                               REFERENCES crews (crew_id) ON DELETE CASCADE,

    -- Join order, which is load-bearing: leadership succession reads the
    -- longest-standing remaining member straight off it. An integer rather than a
    -- timestamp so two members who joined in the same tick still have an order.
    join_order     INT         NOT NULL,

    -- The seat in the crew UI, or NULL for a member who has not taken one.
    slot           INT         NULL,

    created_at     TIMESTAMPTZ NOT NULL,

    CONSTRAINT crew_members_join_order_not_negative CHECK (join_order >= 0),
    CONSTRAINT crew_members_slot_not_negative CHECK (slot IS NULL OR slot >= 0),

    -- One character per seat per crew. A slot collision is a UI-visible bug, so
    -- it is refused here too rather than only in the policy.
    CONSTRAINT crew_members_slot_unique UNIQUE (crew_id, slot)
);

CREATE INDEX crew_members_by_crew ON crew_members (crew_id);
";
    }
}
