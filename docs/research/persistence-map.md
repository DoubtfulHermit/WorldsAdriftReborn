# Persistence map — what survives a restart today, and what does not

Branch: `feat/persistence` (off `feat/shipbuild-integ`). Date: 2026-08-11.

This is the STEP-1 survey the persistence workstream is built on: every piece of
game state, where it lives, and whether it survives a game-server restart or a
player relog.

## Two persistence mechanisms already exist

1. **Postgres `Storage` library** (`WorldsAdriftReborn.Storage`, Npgsql).
   Owns `accounts`, `sessions`, `characters`, `character_inventories`,
   `server_config`. Keyed by **character UID (Guid)**. Turned on only when the
   `WAREBORN_DB` env var is set (`Db.IsConfigured`); otherwise every path is a
   silent no-op and state is in-memory for the session. The **login server**
   (`WorldsAdriftServer`) writes accounts/characters; the **game server** only
   writes `character_inventories`.

2. **`JsonFileStore`** (`WorldsAdriftServer/Persistence/JsonFileStore.cs`).
   Atomic (`temp + File.Move(replace)`) JSON files under `WAREBORN_DATA_DIR`
   (default `data/` next to the binary), corrupt-file quarantine to `.broken`.
   Used by the **login server** only. This is the pattern the new game-server
   world persistence mirrors (via `System.Text.Json`, zero new dependency).

## Player identity across sessions

- The game server speaks **ENet** to an already-authenticated client and **no
  packet carries an account**. The single crossing point is the mod's 1088
  `PlayerPropertiesState` customisation map, which carries key
  `bossaNetCharacterData` → JSON → `characterUid` (a real Guid our login server
  fills). Parsed by `CharacterIdentity.UidFrom` (Multiplayer/Inventory).
- `InventoryService.BindIdentity(entityId, customisation)` (called from
  `PlayerPropertiesState_Handler`, the 1088 handler) is where an entity is
  **rebound from a volatile session key onto the durable character key**, and
  where stored state is loaded. `InventoryService.Forget(entityId)` (called on
  disconnect) is the save-then-drop seam.
- **This is the only durable per-player key available**, and any per-player
  persistence MUST key on it, not on the transient entity id.

## State inventory

| State | Lives in | Persists today? | Key |
|---|---|---|---|
| **Inventory** (farmed materials) | `InventoryService` + `InventoryStore` → `InventoryPersistence` → Postgres `character_inventories` | **YES, but only if `WAREBORN_DB` is set.** No DB configured ⇒ in-memory only, lost on restart. | character UID |
| **Placed deployables** (shipyard) | `PlacedShipyards` (`Dictionary<long,Seed>`), spawned via `PlacementService.SpawnPlacedDeployable` | **NO** — in-memory, keyed by transient entity id. Lost on restart ⇒ must re-place. THE #1 pain. | entity id (session) |
| **Built ships** | `BuiltShips` (`Dictionary<long,byte[]>` hulls + deck id set), spawned via `BuiltShipSpawner.Spawn` | **NO** — in-memory. Lost on restart. | entity id (session) |
| **Ship frame designs** | `ShipDesignStore` → `PlayerShipDesigns` (seeded with `StarterFrame`) | **NO** — in-memory, keyed by entity id. | entity id (session) |
| **Ship blueprints** | `ShipBlueprintCatalogStore` → `PlayerShipBlueprints` | **NO** — in-memory. | entity id (session) |
| **Live blueprint builds** | `ShipBlueprintBuildStore` (shipyard,player) | Transient by design (a build in progress) — not persisted, correct. | pair (session) |
| **Knowledge / progression** | `ProgressionStore` → `PlayerProgression` (knowledge, node uses, learned schematics, scanned ledger) | **NO** — in-memory, keyed by entity id. | entity id (session) |
| **Player position** | `SpawnPolicy.PlayerSpawnPosition` seeds every player's 190602/TransformState at connect; logoff position never captured | **NO** — always spawns at the fixed spawn point. | — |
| **Appearance** | `Appearances` store, from 1088 | In-memory (re-published by the client every join, so effectively fine). | entity id |

## Re-spawn mechanism for shared world entities (the key finding)

Shared, server-owned world entities (island, static test ship, trees, and any
**boot-restored** placed shipyard / built ship) reach every client through the
**connect-time spawn plan**: `SpawnPlan.For(WorldEntities)` is computed **once**
in `Main` (line ~2195) from whatever is registered in the `WorldEntities`
(`WorldEntityRegistry`). Anything registered **before** that line is served to
every joining client, in order, exactly like the static ship.

Therefore the correct way to make a placed shipyard / built ship survive
restart is **not** to re-broadcast at runtime (there are no peers at boot) but to
**register the persisted entities into `WorldEntities` at boot, before the spawn
plan is built, and seed their ledgers (`PlacedShipyards` / `BuiltShips`)** — which
yields entities byte-identical to the runtime-placed ones (same asset, same seed
component sets, same serializer branches). Entity ids need only be consistent
across clients **within** a session, so re-allocating fresh ids on each boot is
fine; the ledgers key on the freshly allocated id.

Runtime placement and boot restore share the same monotonic sequence counters
(`PlacementService._placedSequence`, `BuiltShips.NextSequence()`), so restored
keys (`placed-shipyard:0..`, `built-ship:0..`) and later runtime keys never
collide.

## What this workstream implements vs defers — see the code and the final report

- **Implemented:** shared-world persistence + boot re-spawn for **placed
  deployables** and **built ships**, on a new atomic `System.Text.Json` game
  file store (mirrors `JsonFileStore`). Full round-trip + atomic-write +
  seed-set-parity tests.
- **Verified:** inventory persists via Postgres when `WAREBORN_DB` is set (and
  is silently in-memory otherwise — the biggest live caveat).
- **Deferred (documented seam):** per-character ship designs, ship blueprints,
  knowledge/progression, and logoff position. All four are entity-keyed stores
  that need the character-UID rebinding the inventory path already models
  (`BindIdentity` / `Forget`); they are a clean follow-on on the same seam.
