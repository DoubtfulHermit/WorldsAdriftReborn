# The Wilderness shrine — graduating from Haven

**Status:** server-side only; no client mod change. Never deployed by this work.
Revised 2026-08-18 after live evidence: the object changed from the Revival Chamber
(`HavenAncientRespawner`) to the Reviver platform (`Respawner01`) and the placement
moved out of the ruined metal camp — see §2.1 for why the chamber cannot work.

The mechanic in one line: **an interactable on Haven that sends a player to a
random Tier-1 island — except that a crew arrives together, on their leader's
island, and everyone keeps the island they land on.**

---

## 1. Why a teleport, and why it is the only option

Haven is a sealed onboarding corridor. Bossa instanced **one** tutorial island,
`1431299145`, **twelve** times in a 270 m wide, 32 km tall lane at x ~ 17,000 —
all of it *east* of the authored separator wall at `Haven.xOfVerticalSeparator =
15943.65`, while the entire playable map ends 1.2 km *west* of that wall. There
is nothing to fly to. The nearest Tier-1 island is **9.9 km** from the Haven
spawn point, against a 4 km terrain load radius, so a fresh Haven connect streams
zero Wilderness terrain.

Retail agreed. The shipped Act 1 quest chain ends at quest 105, *"Access the
Revival chamber located at the center of the island"*, and the shipped
instruction string is, verbatim:

> Interact with the platform inside the Revival Chamber to `<activate>` the
> Revival Chamber Interface, and teleport — `<together with other players on the
> platform>` — to The Wilderness.

That is, to the word, the mechanic asked for: **an interaction, on a device, that
teleports a GROUP to Tier 1**. So this is not an invented system; it is the
authored one, rebuilt on the transports this server actually has.
(`docs/research/loop/findings-first-hour.md`, `docs/research/findings-haven.md`.)

---

## 2. The object

| | |
| --- | --- |
| Prefab | `Respawner01` — retail's Reviver **platform** |
| Registration key | `wilderness-shrine` |
| Position | Haven island-local **(168.00, 4.47, 24.00)** |
| Seeded components | 190602 (transform), 1210 (interaction) |
| Interaction | verb `Activate`, radius **5 m**, hold **1.5 s** |
| Spawn order | `AfterPlayer` |
| Kill switch | `WAREBORN_WILDERNESS_SHRINE=0` (default ON) |

### 2.1 The Revival Chamber was tried, and it cannot work

The first two builds of this feature used `HavenAncientRespawner`, the actual Haven
Revival Chamber. It is the authentic object and it is unusable. **PROVED** by
measuring the shipped prefab's own collision meshes out of `resources.assets` with
UnityPy, and corroborated by a live player on 2026-08-18.

**The interaction is at the bottom of a sealed well.**

* The prefab's only `InteractiveObjectVisualizer` is on the deep child `SpawnPad`
  (`…> Ancient_Respawner > respawner_interior > SpawnPad`), the plate the retail
  quest text calls "the platform inside the Revival Chamber". Its `localPosition`
  is `(0, −2.704, 0)`; the plate's collision top is at prefab-local `+0.39`, and
  the decorative top plates reach `+0.50`.
* `respawner_exterior_LOD0`, its collision shell, is **closed on 360/360 bearings**
  from prefab-local `y = −1.0` continuously up to `y = 9.3`. Casting 360 rays from
  the plate centre at 1° resolution finds a wall on every single bearing at radius
  8.69–15 m, at every height in that band.
* The **only** aperture is a doorway on the `+x` bearing (±10°) whose sill is at
  `y = 9.35 ± 0.05` — closed at 9.3, open at 9.4. `Ramp01` (10.03–10.13) and
  `Ramp02` (10.57–10.66) step up to it, the `Barrier_Wall` sits at 11.17–13.17, and
  both `Access-Ancient-Respawner-Trigger` quest boxes bottom out at 9.09 and 11.06.
  Everything player-scale in this prefab is at `y ≈ 9–13`.
* Inside, a gallery runs at `y ≈ 9.67–10.24` for radius 9–14, and the chamber floor
  with the plate is at `y = 0.0`. A ring sweep at radii 5/7/9/11/13 over 48 bearings
  finds **no intermediate surface** between `y = 1` and `y = 9`: the gallery drops
  9.7 m straight to the floor.

So there is no height at which the plate is reachable:

* **Origin at ground** (what shipped) → the plate is a plate at grade, walled in by
  a continuous 9.35 m wall at radius 8.69 m. A courtyard with no gate.
* **Origin buried ~10 m** so the authored doorway meets the terrain → the player can
  walk in, but the plate is now 10 m *under* the terrain mesh, which fills the well
  and occludes it. The look raycast hits terrain, never the plate.

**Observed, 2026-08-18 (PROVED):** a player logged in, ended up inside the shell,
saw the interactive highlight, could not interact, and had to be rescued with the
admin dashboard teleport. That highlight is itself informative — `PlayerLookingAt`
sets `LookingAtInteractive` **and** paints the yellow `_interactiveColor` outline in
the *same* branch, gated on the *same* `InRange` test, so a yellow outline means the
range test passed. It passes near the buried plate and nowhere a player can normally
stand. (This corrects a natural guess: the outline and the prompt are **not** two
different gates.)

**It also does not fit.** The chamber's collision AABB is 40 m × 36 m in plan, and in
the body band above the authored grade it reaches 48 m across. Sweeping every
measured Haven LOD0 surface vertex for a spot flat enough to hold that footprint and
clear of the authored props returns **nothing** on the spawn shelf; the nearest
candidate is 141 m away and 25 m higher.

### 2.2 What is used instead

`Respawner01` — retail's **Reviver platform**. It keeps the authored vocabulary
("interact with the platform"), and it is the object
`InteractiveObjectVisualizer.GetTutorialStep` maps to
`TutorialStep.MOUSE_OVER_REVIVER` when the verb is `Activate`.

**PROVED — it is loadable.** `respawner01` is line 223 of the entity-prefab census
embedded at `WorldsAdriftRebornGameServer.Multiplayer/Ship/client-entity-prefabs.txt`,
the same file `ClientEntityPrefabs` loads at runtime to refuse prefabs a client could
not resolve. The client already precaches it: `PRECACHING: Respawner01` appears in
`~/Games/WorldsAdrift/BepInEx/LogOutput.log`.

**RECOVERED — its geometry is everything the chamber's was not.** Read from
`resources.assets`:

| | |
| --- | --- |
| `InteractiveObjectVisualizer` | on the prefab **ROOT**, offset `(0, 0, 0)` |
| serialized `Verb` | `Activate` (1) |
| root GameObject layer | 15 `Interactive` — inside `Layers.Interactables` |
| collision extent | x/z `−0.60 … +0.60`, y `0.00 … 0.20` |

The zero offset is the load-bearing property. The client measures interaction range
to the *visualizer's* transform, so a visualizer on the root means range is measured
to the entity origin — the same shape as the metal nugget's and the helm's, both of
which are live-proven to prompt on this server. It is a ship part, like the static
helm this server already stands up as its own world entity with the same
190602 + 1210 seed; that is the precedent it is placed on.

### 2.3 The reach — why the first build had NO prompt at all

The client offers a prompt only while (`Assets.Scripts.Player.PlayerLookingAt.InRange`,
decompile):

```
Vector3.Distance(visualizer.transform.position, player.transform.position) + 0.5f
    < visualizer.InteractRange          // == the radius on OUR 1210 entry
```

measured to the **visualizer's own transform**, wherever the prefab author put it.
The first build seeded the metal nugget's 3 m radius onto the chamber, whose
visualizer is 3.204 m below the plate — a reachable sphere of usable radius 2.5 m
centred 2.704 m underground, whose highest point is 0.204 m *below* the entity
origin. No standable point in the world satisfied it, and nothing in any log said so.

The rule now lives as a pure module, `Multiplayer.InteractReach`, so a radius can be
checked against a prefab's measured geometry in a unit test.

`Respawner01`'s radius is **5.0 m, RECOVERED rather than tuned**: it is the client's
own default for an Activate interaction —
`InteractiveObjectVisualizer._interaction` is field-initialised to
`new InteractionEntry(InteractVerb.Activate, 5f, …, 1f)` — and it is the radius this
server already serves for the mounted sail/lamp/horn Activate
(`Ship.PartInteractionPolicy.ActivateRadius`). With the visualizer on the origin that
leaves `sqrt(4.5² − 0.20²) = 4.50 m` of horizontal reach: the whole 1.2 m plate plus
a 3.9 m walk-up ring, which is what a player has to find it with.

The 1210 seed log line now prints the radius and the hold, because the radius is the
one field with no visible tell when it is wrong.

### 2.4 The placement, and the check that was missing

**(168.00, 4.47, 24.00)**, island-local — a measured LOD0 surface vertex, the same
source every other Haven placement on this server comes from.

The previous point, `(176.00, 4.90, 16.00)`, was chosen against the surface table
alone. Its nearest authored structure is **13.7 m** away: it is inside the ruined
metal camp's footprint, and a 40 m prefab standing on it was driven straight through
the camp's platforms. **Terrain flatness was never the missing check.**

Haven's authored structures are now embedded as data
(`Resources/haven-structure-props.txt`, the 253 `Ruins (Miscellaneous)` and
`Ruins (Saborian)` placements projected from `haven-props-resolved.json`) and read by
the pure `Islands.HavenStructures`, so a placement can be checked against what is
already built there. Rocks, foliage, grass and VFX emitters are deliberately excluded:
a monument may overlap a shrub without trapping anybody, and including them makes
every spot on the island fail.

The chosen vertex is the best of the 15 that clear all of:

| | |
| --- | --- |
| surface normal | `ny = 1.000` |
| 8 neighbouring 8 m columns level within | **0.43 m** |
| nearest authored structure | **24.5 m** (was 13.7 m) |
| authored structures overhead (8 m radius, −2 m … +25 m) | **0** |
| from the spawn point | **44.7 m** horizontally; 0.23 m above the spawn's own ground vertex (the 6.70 spawn seed stands the player 2 m clear of it) |
| from the Haven databank | 43.1 m |

The overhead check matters on its own: the camp is multi-storey and the Haven spawn
point itself sits under a platform 19.5 m up, so horizontal distance alone is not
clearance. `HavenStructuresTests` pins that the spawn point *does* have the camp over
it, which is what makes the shrine's zero meaningful.

The CHOICE of vertex is **WAREBORN TUNING**; the vertex and every clearance number
above are measured. Retail's own pad position is not recoverable — everything
Haven-specific was spawned by the GSim.

### How a player interacts with it

The 1210 / 1211 pair — the same proven path that already makes a placed shipyard's
console and a metal nugget interactive. The server seeds 1210 `InteractiveState`; the
client's `InteractiveObjectVisualizer` shows the prompt; the completed interaction
arrives as a 1211 `InteractWithObject` event on the *player's* entity, naming the
shrine as its target.

**RECOVERED — the verb is `Activate` (1)**, read from the prefab's serialized `Verb`
field. The same 48-byte MonoBehaviour decode reads all **191**
`InteractiveObjectVisualizer` instances in `resources.assets` and agrees with every
independently known one (`Helm01` = Man, `Sail01` = Activate, `Stove01` = Craft,
every container = Inventory), so the reading is cross-checked rather than asserted.
The Revival Chamber's `SpawnPad` bakes the same verb, so the two agree.

The seed nonetheless still carries **one entry per plausible verb** — `Activate`,
`Default`, `Man` — because `OnEnable` resolves
`Interactions.FirstOrDefault(i => i.verb == Verb)` **once** and `GetVerb(collider)`
can be overridden per-collider by an `InteractiveObjectVerbOverrider` anywhere in the
collider's parent chain. The extras cost two list elements and are inert. Drop them
once a live client has been seen to send a 1211 naming Activate. `PickUp` is
deliberately absent: the shrine is not portable and a PickUp prompt on it would be a
lie.

The interact dispatcher selects on the **target's registration key**, not on the
verb, and short-circuits — so a helm interaction can never reach the shrine, and a
shrine interaction never falls through to the helm or mounted-part paths. It is
owner-only: using the shrine moves the sender's character and can write their
crewmates' home rows.

## 3. Where it sends you — the rule

### Definitions

* **The Wilderness** = MapFile cell tier 1 = cells **A2, A3, B2, B3** = exactly
  **46 islands**, and those four cells contain nothing else.
  `ReleaseWorldTierSelectionTests` pins both halves of that equivalence. The
  catalogue is read by each record's own `cellTier`, never by a re-listed cell
  set, so a regeneration that moved an island between cells cannot leave the
  shrine pointing at a stale list.
* **Open** = the intersection of the Wilderness with the islands **registered on
  this boot**, ordered by island id. Knowing about an island is not the same as
  having spawned its terrain.
* **Home** = the Tier-1 island a character's **already-persisted logout position**
  stands on, read back with `IslandLocationPolicy`. *It is not a new table.*
  `character_positions` (schema v5) already persists per character, so "where you
  live" and "where you log back in" are the same row and cannot drift apart.

### The rule

**Crewless:**

1. Your own home, if you have one and it is open this boot -> `OwnHome`.
2. Otherwise draw one -> `FreshSoloIsland`, recorded for you.

**In a crew — resolve THE CREW'S ISLAND, in this order:**

1. The **leader's** home, if they have one that is open -> `CrewLeaderHome`.
2. Otherwise the home of the **earliest-joined member** who has one that is open,
   scanning the member list front to back and skipping the leader ->
   `CrewMemberHome`.
3. Otherwise **draw one** -> `FreshCrewIsland`, recorded for **every** member.

### Tie-breaks — all of them

* **"Earliest-joined"** is the member list's own order. `CrewLedger` maintains it
  as join order with the founder at index 0, and `Crew.Add` is idempotent, so the
  scan has a total order and nothing is left to break. **Promotion after a leader
  leaves does not reorder it** — `Crew.Promote` changes `LeaderUid` only — so a
  successor is scanned at the position they joined at. That is why clause 1 asks
  for the leader **by name** rather than by list position.
* **A home on an island that is not open tonight is treated as absent** at every
  step. Not an error, not a refusal: the character keeps that stored position and
  gets it back if the district is registered again. What must not happen is being
  sent to terrain that does not exist tonight.
* **The draw** is `pick(open.Count)` over the list *ordered by island id*, so the
  same index is the same island on any server with the same districts. An
  out-of-range answer is clamped rather than trusted — the picker is somebody
  else's code by definition.
* **Clause 3 can only fire when clauses 1 and 2 both failed**, i.e. when *no*
  member has an open home. So recording the drawn island "for the whole crew" can
  never overwrite anybody's existing Wilderness home — the case where it would
  have to cannot arise. This is what makes writing rows for *offline* crewmates
  safe.
* **The crew beats your own home.** A crewed player who already had an island
  goes to the crew's, not theirs. That is the point of the mechanic, and it is
  stable rather than order-dependent: whichever member goes first, later members
  resolve through the same leader-then-earliest-member scan and land on the same
  rock. Leaving the crew and using the shrine again returns you to your own home
  (which by then may be the crew's). Nothing is destroyed either way — the island
  you left is still there.
* **Crew membership may also change over HTTP** (branch `feat/social-api`). The
  policy takes a flat `CrewSnapshot` and the seam builds it from `CrewService`'s
  public surface only, so no internal of either path is depended on.

### Why it is sticky

Going through the shrine twice takes you to the **same** island. A Wilderness
island is where your ship, your shipyard and your stored logout position are;
re-rolling on every use would strand all three and turn a graduation device into
a way to lose your things. The randomness is a **world-spread mechanism** — it is
what stops every player piling onto one island — not a per-use thrill, so it is
spent **once per character** (or once per crew) and then remembered.

---

## 4. Landing points — 46 islands, no guessed coordinates

The server has **no terrain query**: no raycast, no collider, no height table. It
cannot answer "is there solid ground here" and never claims to. What it can do is
land people on an **evidenced surface sample**, which is exactly how the existing
named destinations (Haven's spawn, Trades Challenge, Mental Facility) were
derived — by hand, one at a time, with no committed script.

That gap is now closed. `tools/world-import/generate-release-runtime-catalog.py`
gains a `landing` field for **all 254** release islands, derived
deterministically from `docs/research/world-data/island-surfaces/<asset>.json`:

1. Collapse the samples to **columns** — the highest sample at each (x, z) — so
   an island's underside samples cannot pretend to be ground or to be a ceiling.
2. Reject any candidate with a column within **4 m** horizontally sitting **3 m
   or more above** it: no landing inside a cave or under an overhang.
3. Count **supporting columns** within **12 m** (which reaches the 8 m cardinals
   and the 11.31 m diagonals, and nothing in the next ring) whose height is
   within the rung's step tolerance.
4. Rank flattest first, then most upward, then broadest, then by the point's own
   coordinates. No RNG, no clock: the same surface table always yields the same
   pad.
5. A relaxation ladder (`0.98/8/2.5` -> ... -> `0.40/0/inf`) so a normal island is
   judged by the strictest rule and only genuinely poor meshes fall through.
   Every rung is still a **measured** sample; the ladder never invents a
   coordinate.

Result: **253 of 254** islands land on a normal of >= 0.98 with 8 level
neighbours. The one exception (Belial, 3 surface samples in total) is Tier 3 and
never reachable by the shrine. **All 46 Wilderness islands** clear
`ny >= 0.98`, `support >= 6`, `step <= 2.5 m`, asserted for every one of them.

Mental Facility's already-reviewed point (local `120.00, 34.26, -16.00`) is
**pinned** by the generator rather than out-voted, so the generated field cannot
contradict a coordinate `TeleportPolicy` already names. It is still *measured*
here — it reports 7 supporting columns and a 2.42 m step honestly, rather than
being exempt from the numbers everything else is judged by.

The player is placed **2.00 m** above the sample — the same stand-off every
hand-derived destination on this server already uses, because the sample is a
collision-mesh *vertex* and a capsule has to stand on top of it.

Regenerating the catalogue is byte-identical for every pre-existing field
(verified before the change was made), so `deposits`, `databankPoints`, `shell`
and `aabb` are untouched. Schema bumped 1 -> 2.

---

## 5. Safety — reusing the machinery, not going around it

The arrival is a **190607 `TeleportRequestState`** update, parentless, with a
request number from `TeleportRequestCounter`, acked on 1073 — the identical path
the operator trigger file, the fall rescue and the logout restore use. **190602
is never re-seeded on a live entity**; that is the out-of-world bug `SpawnPolicy`
and `MirrorSendPolicy` exist to prevent.

The per-player terrain gate was **extracted, not copied**, out of
`TeleportService.Execute` into `DispatchWithTerrainGate`, and both the operator
path and graduation now call it. A second copy of "ask, then wait, then give up"
is precisely how the logout restore once ended up with no gate at all.

For each graduating player it:

1. resolves the destination island from the destination's
   `RequiredWorldEntityKey` — **the load-bearing field**, not a label;
2. calls `IslandTerrainInterestService.RequestDestination`, which both asks *and
   pins* the island as that peer's forced destination. Without it a 9.9 km island
   past the 4 km load radius would never even be requested;
3. asks `IslandTerrainTeleportPolicy.Decide` and either **sends**, **defers**
   with a bounded deadline (asset-ack timeout + 5 s), or **refuses** with a
   stated reason. There is no unbounded wait;
4. on landing, pins that terrain as the player's confirmed ground via
   `ConfirmTeleportLanding`, so the streamer cannot unload it out from under
   them.

`landsOnLoadedGround` is **false** on a wilderness destination, and honestly so:
however well-evidenced a sample is, this server is not entitled to claim there is
ground on a particular client at a particular moment. What protects the arrival
is the terrain gate, not a boolean.

**Order of operations: decide -> record -> teleport.** A crash between record and
teleport leaves a stored Wilderness position, which the logout restore then
honours safely through the same gate. A teleport with no record would leave the
player somewhere the server has no memory of putting them, and their next login
would drag them back to Haven.

**Refusal.** If no Tier-1 island is registered the shrine refuses with *"The
Wilderness is closed: no Tier-1 island is running on this world."* — it does not
teleport anyone. The shrine still **stands** in that case: whether the Wilderness
is open is a question the shrine *answers*, not one that decides whether it
exists, and a missing door reads as a bug.

**The player-facing channel** is the 6900 `CrewManagementFeedback` line. That is
not obviously where a shrine's message belongs, but it is the *only* single-line
channel to a client this server has, the retail UI renders it verbatim, and
graduation is a crew mechanic in retail's own telling. Best-effort: a player
whose uid has not arrived gets nothing, and the server log is then the record.

---

## 6. What is deliberately NOT here

* **Revival chambers.** The prefab is the Revival Chamber, but no respawn-anchor
  system is implemented. Nothing here binds a respawn point.
* **Windwalls, storms, a tutorial-completion system.** Out of scope.
* **8052 `HavenTeleporterState`, the barrier dome, 8056 `LeaveHavenRequest`.**
  Retail's own Haven machinery. Not served; the object is used as the physical
  device and the interaction runs on 1210/1211.
* **6905 `AncientRespawnerState`.** It exists in the schema and is exactly the
  tempting id to add. It has **no `ComponentsSerializer` branch**, and the seed
  push is all-or-nothing, so adding it would drop the whole batch and spawn an
  inert entity at the world origin. `WildernessShrineTests` pins the seed set as
  an exact list to stop that.

### The seam for gating on tutorial completion

If the shrine should later be locked until the tutorial is done, the single place
to add it is `WildernessGraduationService.Use`, immediately after the uid is
resolved and **before** `WildernessGraduationPolicy.Decide` — returning a new
`WildernessVerdict` with its own one-line message. Nothing else needs to change:
the policy already has a refusal shape, the feedback channel already carries the
sentence, and the entity is already gated on a target key rather than a verb.
Retail's own gate is `nodes["RevivalChamberInterface"] != 0` in
`HavenIslandManager` — a knowledge node, which `ProgressionService` already
persists — so the check has an obvious authentic form when someone wants it.

---

## 7. Running it

```
WAREBORN_RELEASE_WORLD_DISTRICTS=tier1    # REQUIRED, or the shrine refuses
WAREBORN_INTEREST_RADIUS_M=120            # required by the release-world gate
WAREBORN_TERRAIN_INTEREST_ENABLED=1       # required by the release-world gate
WAREBORN_TERRAIN_LOAD_RADIUS_M=4000
WAREBORN_WILDERNESS_SHRINE=0              # optional kill switch; default ON
```

The startup banner says which of the three states it is in: off, standing but
closed, or open with a count of islands.

---

## 8. Evidence, and what still needs a live client

**PROVED on a real wire.** The two-peer acceptance harness
(`tools/relaybot/run-ship-acceptance.sh`) boots the native server build and
drives it with real ENet peers. Its log shows the shrine all the way through:

```
[info] spawn plan (24 steps): ... -> RequestAsset wilderness-shrine
       -> AddEntity wilderness-shrine -> ...
[info] requesting the game to load Respawner01 for world entity 'wilderness-shrine'...
[success] asset loaded for 'wilderness-shrine'. creating entity 11 at (...) m...
[interest] entity 11 wants 2 component(s): [190602, 1210] (ALL-OR-NOTHING: ...)
[info] seeding 190602 for entity 11 (World 'wilderness-shrine' Respawner01) ...
[info] seeding 1210 for entity 11 ... with verb Activate/Default/Man (shrine hedge),
       radius=5m, hold=1.5s, available=True.
[success] initialized and serialized componentId 1210
[warning] wilderness shrine stands on Haven but the Wilderness is CLOSED: no tier-1
          island is registered. ...
```

So: it is in the spawn plan, the asset request goes out before the AddEntity, the
entity is created at the derived position, and the **multi-entry 1210 seed
serializes and is sent without dropping the batch** — the one wire risk that a
unit test could not settle. The gate itself still PASSes (2 pilots, coherent
frames, legal re-entry).

**PROVED on a live client (2026-08-18), against the OLD build:** a world entity
spawned this way is checked out and its prefab resolves —
`~/Games/WorldsAdrift/BepInEx/LogOutput.log` recorded
`[WAReborn] compiled entity template 'HavenAncientRespawner_unityclient'` during
the Haven load-in, emitted from the `GetEntityTemplate` prefix, i.e. while the
client was processing the shrine's own `AddEntityOp`. The same session is where
the player ended up inside the chamber shell, saw the interactive highlight, could
not interact, and had to be rescued with the admin teleport (§2.1).

**Still needs a live client — NOTHING in the current build has been walked to:**

1. **Does `Respawner01` render** when spawned as its own standalone world entity
   rather than as a mounted ship part? It is precached (`PRECACHING: Respawner01`)
   and the static helm is the same shape of thing and does render, but that is a
   precedent, not a sighting. It carries a `Rigidbody`, `ShipPartVisualizer` and
   `PlacementRules`; if it behaves oddly loose, that is the first thing to look at.
2. **Does the prompt appear?** The verb is RECOVERED, the visualizer is on the
   root, and the radius is the client's own Activate default — but the prompt has
   never been seen. The 1211 log line names the verb the client actually sent back,
   so one session settles it and the other two hedge entries can then be dropped.
3. **Is the 44.7 m walk from the spawn point clear on real collision?** It is on
   the same shelf (0.23 m of height difference) and has nothing authored within
   24.5 m, but it leaves the ruined metal camp and no one has walked it.
   **Discoverability is a real risk**: this is now a 1.2 m plate 45 m from spawn,
   not a 38 m tower. If players cannot find it, a marker is the follow-up — not a
   bigger prefab.
4. **Does the teleport itself fire?** 1211 → `WildernessGraduationService.Use` →
   190607 has never run from a real client.
5. **Does the arrival land on solid ground** on each of the 46 islands. The
   evidence is strong and uniform, but "measured surface sample" is not "stood on
   it". A visual acceptance pass over the 46 pads is the honest follow-up.
6. **Does the crew rule work end to end?** It is unit-tested and unproven live, and
   it is worth being precise about what it is: not retail's "everyone standing on
   the platform goes at once", but *each member who uses the shrine resolves to the
   same island*. Proving it needs two accounts in one crew.
7. **Does the crew feedback line render** for a message that is not about a crew
   action.

## 9. If the Revival Chamber is wanted back

It is a genuinely better landmark and a genuinely unusable interactable. If it
should stand on Haven as **scenery** — a 190602 seed and no 1210 — the one measured
vertex that can hold its 44 m footprint is island-local **(80.00, 29.57, 64.00)**:
`ny = 0.990`, terrain span 3.68 m across the footprint, 21.9 m from the nearest
authored structure, 141 m from the spawn point and 24.9 m above it. Bury the origin
about 10 m so its authored doorway (sill at prefab-local 9.35) meets the terrain,
or it floats. That is a separate entity from the shrine and should stay one.
