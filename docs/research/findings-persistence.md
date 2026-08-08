# FINDINGS — PERSISTENCE

(Recorded by the orchestrator; the agent was blocked from writing this file.)

## HEADLINE
- **Identity is already solved.** The mod publishes the selected character's JSON
  (uid + name) inside the 1088 update; `PlayerPropertiesState_Handler` already
  records every key. `Appearances.Get(entityId)["bossaNetCharacterData"]` contains
  the uid TODAY. Zero new wire/client/C++ work.
- **Storage:** plain JSON keyed by a real GUID `characterUid`. No SQLite (native
  e_sqlite3 in Wine), no LiteDB (new dep, opaque).
- **True blocker for inventory:** `InventoryModificationState_Handler` only LOGS
  1082 events instead of mutating the server-owned 1081. There is currently no
  inventory state to persist.

## Q1 CHARACTER
- Save POST: `POST {Rest}/character/{Build}/steam/1234/{uid}` — `steam`/`1234` are
  hardcoded client literals (`acs/Bossa.Travellers.CharacterSelection/CharacterSelectionHandler.cs:218`).
  uid is read from the BODY (`:207`). Body = plain `JsonConvert.SerializeObject` of
  `CharacterCreationData` → JSON keys are exact member names.
- `CharacterCreationData` fields (`acs/Travellers.UI.Login/CharacterCreationData.cs:10-28`):
  `Id, characterUid, Name, Server, serverIdentifier, Cosmetics, UniversalColors,
  isMale, seenIntro, skippedTutorial`. Cosmetics keys serialize as enum NAMES.
  The repo mirror already matches — `CharacterSaveHandler.cs:14` already
  deserializes correctly, it just discards.
- Sent only twice: creation confirm, and the `seenIntro` flip at Enter World.
  **Never on logout, never on appearance edit.**
- **No `CharacterListResponse` type exists client-side** — response parsed as raw
  JObject by `LobbySystem.RefreshCharactersFromJObject` (`:478-525`):
  `characterList` (`:481`), `unlockedSlots` (`:496`), `hasMainCharacter` via
  **GetValue** (`:515`, omitting it NREs), `havenFinished` (`:518`). `Id` is
  overwritten with the array index.
- **The SAME parser runs on the save response** (`:429-435`) and `ExitState` only
  fires if it returns true. Today the handler returns `"{}"` which FAILS at `:501`.
  **The save handler must return the full roster.**
- Empty-slot rule (`LobbySystem.cs:509`): uid non-empty AND `Cosmetics == null`.
  An empty `{}` is misclassified as real then NREs at
  `CharacterCustomisationVisualizer.cs:422`.
- **uid rules (#17):** character screen needs only non-empty, but
  `acs/Bossa.Travellers.Social/SocialHelper.cs:30-47` requires `Contains("-")` AND
  `new Guid(uid)` — the placeholder passes `:36` and THROWS at `:42`. Real rule:
  `Guid.NewGuid().ToString()`. Mandatory anyway because `Character.cs:51` gives all
  three characters the SAME literal uid. (1294 `UidState` is NOT the character uid.)

## Q2 INVENTORY
- **1081 is server-owned, full-state.** No `InventoryState.Writer` anywhere in acs;
  absent from `authoritativeComponents`. Every 1081 update rebuilds the whole UI
  (`InventoryVisualiser.LoadInventory:121-130`) — so a 1081 push at ANY time fully
  restores an inventory. No ordering constraints.
- Persist `InventoryStateData` (inventoryList, lockBoxItems, width/height/hasBelt/
  beltRow, allowedItems, jsonData, updateSequence) with all 14 per-item fields.
  **Hotbar, stash, worn slot, colours and item health are all inside 1081.**
  `slotType` must be the exact enum name or the client throws
  (`acs/InventorySlotData.cs:99`). Plus **1280 WearableUtilsState** — worn-utility
  durability is NOT in 1081.
- 1082 is a pure event bus (15 events, no state). The handler acts on ONE
  (`equipWearable`) and even that mutates a copy never written back (`:60-63`).

## Q3 SCHEMATICS — not the bottleneck
`SchematicSystem.cs:124-138` unions `defaultSchematics ∪ learnedSchematics` (1079);
server seeds both empty. Crafting has no server implementation
(`PlayerCraftingInteractionState_Handler` is a pure echo; 1004 unhandled).
**A static non-empty `defaultSchematics` makes crafting usable with zero
persistence.** Defer the rest.

## Q4 MUST vs CAN-DEFAULT
- **Must:** character roster; 1081; 1280.
- **Defer:** schematics, lore, knowledge.
- **Can default:** health 1077, buffs 4329, quests, `havenFinished`.
- **Position: defer.** 190602 is client-authoritative and published immediately
  while identity arrives late; re-seeding it is the documented sky-teleport bug.

## Q6 IDENTITY — already solved
Dead ends: `enet_host_connect(...,0)` carries nothing; `Connection.cpp:3-23` never
reads `parameters->Metadata` so the client's connect metadata is dropped; 1086 is
server-seeded hardcoded; 1268 is dead.
**But:** `CharacterSelectionScreen_Patch.cs:23-27` saves the selected character to
PlayerPrefs → `CharacterCustomisationVisualizer_Patch.cs:54-58` injects it as
`bossaNetCharacterData` → `:81-83` publishes it in the 1088 update →
`PlayerPropertiesState_Handler.cs:45-51` records every key. Lands AFTER 1081 is
seeded — harmless given Q2.

## Q5 STORAGE
Plain JSON, `.tmp` + `File.Move(overwrite:true)`. Root `WAREBORN_DATA_DIR`
defaulting to `<assembly dir>/../data`.
```
<data>/characters/roster.json      # exactly the /characterList response body
<data>/players/<characterUid>.json # inventory + wearables (+ later schematics/position)
```
**One roster, not per-account** — the client hardcodes `/steam/1234` and Steam auth
is stubbed, so the login server cannot distinguish two clients.

## PLAN (smallest correct first)
0. **Real GUID uids** (`Character.cs:51,69`). Unblocks everything, fixes the latent
   Social crash.
1. **Login-server roster persistence.** Headline result alone; testable with curl,
   no game server, no client.
2. **Identity, log-only.** Parse `bossaNetCharacterData`, `PlayerIdentityRegistry`
   + unit tests. Prove the riskiest assumption before building on it.
3. **Restore only.** Hand-write a profile, see it in game. Nothing corruptible yet.
4. **Inventory save.** 1082 → stored 1081, write back, autosave + on disconnect.
   One event at a time. Pure tested module, thin handler.
5. Optional: default schematics, learned schematics, lore, position.

## RISKS
Two clients share one roster → can pick the same character (reject a second peer
claiming a live uid). Identity publish is one-shot and fails silently on a bad cast
— log loudly when a peer disconnects with no identity.
`CharacterDataLoader.Load().ToArray()[0]` (`CharacterCustomisationVisualizer_Patch.cs:56`)
is OUTSIDE the try/catch → IndexOutOfRange if PlayerPrefs empty. itemId collisions
on restore (1-100 reserved). Disconnect may not fire → autosave mandatory.
Smoke-test Wine `File.Move`.

## NOT VERIFIED
Nothing was run. That the 1088 identity actually lands at runtime (line-by-line
verified, not observed — Step 2 exists to prove it). `debugDevMode` shipped value.
What the client sends as `platformId` under the mod (`ModSettings.steamUserId` is
dead config; wiring it is the upgrade path to per-account rosters). Whether the
post-seed 1081 push is visually clean. Position restore feasibility.
