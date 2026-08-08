# FINDINGS — HAVEN, THE STARTER ISLAND

## LEAD: spawn on Haven — but understand what you are buying
`1431299145` is the right id and is genuinely Bossa-authored (`islandAuthor: "Bossa"`, and the
only one of 255 absent from the Steam-scraped community data). At **4.31 MiB vs 28.21 MiB**
for our current island — and **90 colliders vs 497** — it also loads far faster, which makes
the spawn-ordering risk *smaller*, not larger.

**But Haven's bundle contains NO Haven-ness whatsoever.** Structurally identical to a normal
island: **18 MonoBehaviours, all infrastructure, zero gameplay objects** (re-verified against
three other islands — all exactly 18). Everything that made Haven *Haven* — the teleporter,
the barrier dome, the ancient respawner, the ruined starter ship — was **server-spawned
entities from the GSim**, which is gone.

**You are spawning a small pretty island with a ruined metal camp on it, not a tutorial.**

## ⚠ THE COORDINATES BELOW ARE SUSPECT — READ findings-spawn.md FIRST
They were derived from `island-surfaces/1431299145.json`, and that table has since been shown
**wrong by ~25 m** on the one island we can check empirically: the extractor's `offs()` only
accumulates `m_LocalPosition` and ignores rotation and scale. **The X and Z are sound; the
ALTITUDE is not.** Re-derive after fixing the extractor, or validate empirically with
telemetry before trusting the Y value.

## PASTE-READY VALUES (X/Z sound, Y suspect)
`ToFixedPoint` is `(long)(d * 4096)` — **truncation toward zero, not rounding**.
```
ISLAND  1431299145@Island   instance #5   (17004.4300, -318.6693420, -1134.16748)
        190602  { 69650145, -1305269, -4645549 }

PLAYER  island-local (200.00, 3.96, 5.00) = world (17204.4300, -314.7093420, -1129.16748)
        190602  { 70469345, -1289049, -4625069 }

FALLBACK  island-local (192.00, 2.30, 16.00)  ny=0.999 dead flat, 9.37 m clearance
        190602  { 70436577, -1295848, -4580013 }
```
The chosen point is a **measured** LOD0 vertex from our committed `island-surfaces/`
(421 candidates), normal `ny = 0.914`, **nearest prop 9.53 m** — the clearest flat candidate
near the camp. The only things within 6 m horizontally are metal platforms **+22 m overhead**.
Verified to be the *top* surface (neighbours within 14 m read y 0.30–1.96; the island
underside is at y ≈ −47). The 2 m stand-off gives a short drop instead of possible capsule
interpenetration.
Instance #5 chosen as nearest world-centre in z (|z| = 1134 vs runner-up 1826). **Any of the
twelve is functionally identical — spawn only one.**

## ⭐ THE "FREE WIN" HYPOTHESIS IS DEAD
I had hoped the client derives Haven-ness from position (`x > xOfVerticalSeparator`), so that
spawning at the right coordinates would restore behaviour for free. **It does not.**
`xOfVerticalSeparator` has **exactly four references in the whole decompile, all in the World
Editor authoring tool**, and `GetXOfHavenSeparator()` merely returns the x of the first
draggable "Haven Wall" gizmo the designer placed. **It is a line on a level-design canvas**,
serialised so the tool could redraw it.
**Component 8055 `NewPlayerState` is the SOLE runtime source of truth for Haven-ness.**

## ⭐ RECOMMENDATION THAT REVERSES MY PLAN: seed 8055 = FALSE anyway
I wrote that spawning real Haven would make `isNewPlayer = true` truthful. **Do not do it.**
`true` is only honest if a player can also *leave*, and the exit is component **8056
`LeaveHavenRequest`** with a payloadless `FinishHaven` event — **zero references in the whole
client**, triggered and consumed entirely server-side, and **not implemented on our server at
all**. There is no path to ever update 8055: no handler exists, and it is correctly absent
from `AuthoritativeComponents` (the client has no writer).
So `true` is a **permanent prison** costing five working UI features forever.
Seeding `false` is completely silent — `NewPlayerVisualiser.OnNewPlayerChanged` only acts on
the `true→false` **edge**. It also removes the current contradiction with
`havenFinished = true` in `RosterPolicy.cs:182`.

**If you later want real Haven progression**, the trigger is server-side: watch for the
`RevivalChamberInterface` knowledge node, then push 8055 `false` — the bloom flash and quest
unlock fire for free.

### Correction to findings-progression
The biome banner suppression is **not respawn-only**. `DisplayBiomeNotification` is called
from `RespawnVisualizer.Update` on a **1-second biome poll**, so while `isNewPlayer == true`
**every** biome banner in the game is suppressed.

## `HavenIslandManager` — traced, and it does exactly one thing
Raise and lower a shield dome. `CheckIfGauntletInterfaceIsUnlocked` is a three-line guard on
one map key: **the barrier penning you inside Haven drops the moment you scan the Revival
Chamber interface.** That is the entire exit gate, client-side. ("Gauntlet" is Bossa's
internal name for the Haven tutorial run.)
It carries **no `[WorkerType]`** — it is stripped a different way, at prefab-export time by
`HavenTeleporterPreprocessor`. And it lives on the **`HavenAncientRespawner` entity prefab**,
not on the island. The barrier dome sits 13.09 m above and 18.95 m behind the teleporter pad.

## WHY TWELVE — physical copies for shard partitioning
A north-south column at x ≈ 16903–17174, z from −15775 to +16336, mean gap ≈ 2,919 m.
With `GSIMConfig "36x36"` and `IslandSpawningData{gsimNumber, numberOfGSimsPerSide}`, the read
is **one physical Haven per shard band** — real, simultaneously-existing copies in one shared
world, not instanced dungeons.

## SPAWN IS SERVER-SIDE, ALWAYS — and we have never sent the message
There is **no spawn marker on any island.** The client's entire spawn vocabulary is three
events on 1093, each carrying **one int** — the biome you *died* in, not where you go.
The server answers by writing **`TeleportRequestState` (190607)**
`{localPosition, localRotation, parent, request}`; the client copies it into its own 190602
and acks via 190606. **The Reborn server has never sent 190607.**

Red herrings ruled out: **`IslandState.teleportTarget`** (read only by generated plumbing,
zero client consumers, not in `islands.json` — its meaning is now ours to define) ·
`AncientRespawnerActivation._spawnPadTransform` (camera-shake maths only) ·
`IslandSurfaceData.FindPlace` (the *resource* placer, not players).
The login-spawn record was **`PersistentLocationState` (1144)**, whose **`isNew`** field is
what routed first-time players to Haven. Zero client references.

## WHAT IS ON HAVEN — 1,285 props, fully resolved, zero unresolved
Community data has **nothing** — `1431299145` is absent from `cardinal-guild-islands.json`
(expected: it is Bossa-authored, and that dataset scrapes Steam Workshop). **No tier, no
databank count, no metal table. Do not expect one.**

I resolved the prop lists in full by extracting the 1,347-entry `IslandProps/guidlut` table:
~430 rocks · ~370 foliage · **~200 metal ruins** · ~25 Saborian brick/statues · ~85 VFX emitters.
**No databanks. No crafting station. No ship. No respawner. No resource nodes.** Every one of
the 1,285 placements is generic scenery from the shared library every island draws from.

**The one real feature is a ruined metal camp** — ~178 metal platforms plus walkways, ladders,
pipes and girders at island-local `x 164..223, y −0.5..25.6, z −31..27`, centroid
`(205.3, 15.2, −0.8)`, independently corroborated by the `groups` TextAsset. The only
constructed area on the island, and with high confidence where the ancient respawner stood.
**Our recommended spawn puts the player ~8 m from that centroid** — opening their eyes inside
Haven's ruined structure, the closest thing to the authored experience that survives.

The furniture that made Haven work (`HavenAncientRespawner`, `HavenRuinedShipRespawner`,
`TeleportHelper`, `Barrier_Wall`, `RevivalChamber`) exists as prefabs in `resources.assets` but
ships in **no bundle** — `Assets/unity/` holds 255 files, all islands. **Recreating Haven as a
tutorial means spawning those as entities: a separate project, not a spawn-point change.**

## THREE THINGS TO GET RIGHT ALONGSIDE
1. **`InitAndSerialize` must become entity-aware for 190602 first** — otherwise island and
   player receive the same seed. Now **mandatory**, since Haven is one asset at twelve positions.
2. **Order matters** — the island AddEntity and its 90 colliders must land before the player
   transform is published, or the player falls through. Haven makes this *easier*.
3. **Seed 8055 = `false`.**

## COULD NOT DETERMINE
The original island-local position of the respawner/teleporter pad (GSim-authored, gone — the
camp centroid is my geometric inference). **The original player spawn coordinate** — same
reason; my recommendation is a flagged reconstruction. What `IslandState.teleportTarget` held.
Whether the twelve were assigned round-robin, by GSim load, or by account shard. Which of the
twelve was canonical — nothing distinguishes them.

## DATA COMMITTED (`world-data/haven/`)
`haven-props-resolved.json` (1,285 props with readable asset paths) ·
**`guidlut.json` (1,347-entry GUID→prefab table — reusable for ALL 255 islands; the most
valuable artefact here, and exactly what Phase 4 resource placement will want)** ·
`oldassetlut.json` · `haven-prefabs2.json` · `census-haven.json`
