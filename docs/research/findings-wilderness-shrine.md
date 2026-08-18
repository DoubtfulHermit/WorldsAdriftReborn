# The Wilderness shrine — graduating from Haven

**Status:** server-side only; no client mod change. Never deployed by this work.
Revised twice on 2026-08-18 after live evidence. The interactable is no longer the
Revival Chamber (§2.1: its plate is at the bottom of a sealed well) but the Reviver
platform `Respawner01`; and the chamber is back as a SEPARATE scenery entity, buried
so its authored doorway meets the ground, with the platform standing in the middle of
its floor (§2.5). Two entities: a landmark you can see, and a plate you can use.

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
| Position | Haven island-local **(160.00, 4.18, 32.00)** — the centre of the chamber floor |
| The building around it | `wilderness-shrine-chamber`, `HavenAncientRespawner`, scenery only — see §2.5 |
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

### 2.4 The placement — three attempts, and what each one taught

| | outcome |
| --- | --- |
| `(176.00, 4.90, 16.00)`, Revival Chamber | nearest authored structure **13.7 m** — a 40 m prefab driven through the ruined metal camp. A player logged in inside it and was rescued with the admin teleport. |
| `(168.00, 4.47, 24.00)`, bare `Respawner01` | cleared the camp by 24.5 m, but a 1.2 m plate 45 m from spawn in an empty field. Live report: *"i cant find the teleporter now"*. |
| **`(160.00, 4.18, 32.00)`, inside the chamber** | the current design: the chamber is the landmark and the room, the plate stands at its centre. |

Terrain flatness was never the missing check. Haven's authored structures are now
embedded as data (`Resources/haven-structure-props.txt`, the 253
`Ruins (Miscellaneous)` and `Ruins (Saborian)` placements projected from
`haven-props-resolved.json`) and read by the pure `Islands.HavenStructures`, so a
placement is checked against what is already built there. Rocks, foliage, grass and
VFX emitters are deliberately excluded: a monument may overlap a shrub without
trapping anybody, and including them makes every spot on the island fail.

### 2.5 The chamber as the room — the "clean slot"

**PROVED.** Everything that made the chamber unusable as a *device* is fine once it
is only the *building*, provided it is buried to exactly the right depth.

**The doorway, measured.** Sectioning `respawner_exterior_LOD0` +
`respawner_interior_LOD0` at 0.1 m steps and casting 720 rays from the centre at
each height: every bearing blocked below **10.8**, exactly **23 of 720** bearings
(−5.5°…+5.5°) open from **10.9 to 15.2**, all blocked again at **15.3**. So the
sill is at prefab-local **10.85 ± 0.05**, the lintel at **15.25 ± 0.05**, and the
aperture is **4.40 m** tall. Scanning the free channel along the passage gives
|z| ≤ **1.85 m** for local x 14…19 — a **3.8 m wide** corridor running from the
outer face at x ≈ 21 to the room at x ≈ 13.

**The burial depth is derived, not chosen.** Put the sill on the ground the corridor
actually lands on:

```
chamber origin Y  =  corridor ground (3.99)  −  sill (10.85)  =  −6.86
```

i.e. **11.04 m below the (160, 32) surface vertex**. Everything under the sill — the
sealed drum, the 9.7 m internal drop, the unreachable plate — is then under Haven's
terrain mesh, where nobody can enter it or fall into it. What is left above ground is
**a walled room with one door whose floor is Haven's own terrain**.

Checks, all from the fine 2 m re-extraction of Haven's LOD0 surface (7,791 samples,
regenerated with the repo's own `tools/sweep_one.py`):

| | |
| --- | --- |
| corridor terrain (4 samples, local x 14.4…17.9, \|z\| ≤ 1.4) | **3.99 … 4.10** (spans 0.11 m) |
| doorway clear height at its tightest | **4.29 m** (needs 2.2) |
| interior terrain, radius ≤ 9 m | **4.04 … 4.49** → prefab-local 10.90…11.35 |
| interior floor vs the sill | **0.05…0.50 m ABOVE it** — you step in level |
| clear floor around the axis at the standing band | **10.0 m** (2.2 m capsule vs the prefab meshes, 1 m grid) |
| ceiling | prefab-local 24.7 → **~13 m of headroom** |
| terrain under the whole 40×36 m footprint | spans **1.65 m** |
| nearest authored structure to the footprint | **7.2 m** clear |
| authored rocks within 12 m of the centre | **0** |

**The step-in-level test is the one that decides a site.** A candidate at
`(188, 64)` faced the spawn almost head-on (3° off) and had better prop clearance —
and its interior terrain measured 1.6–3.1 m *below* the sill, i.e. a walled pit with
a door in its ceiling. Rejected. That is the same trap in a new costume.

**Facing.** The prefab has exactly one doorway, on its local +x, so the entity
carries a real yaw: **300°**, in the convention this server already flies ships in
(`ShipyardDockingPolicy.PackedYaw` builds a rotation about +Y; `FlightIntegrator`
turns that yaw into a heading of `(sin yaw, cos yaw)`, so local +x points at
`(cos yaw, −sin yaw)`). 300° puts the door at world (+0.50, +0.87) — about a quarter
turn from the line a player walks in on. Stated plainly because it is a real cost:
of 24 yaws at this vertex it is the only one whose corridor lands on ground there
are enough samples to certify *and* clears the authored props.

**A building clears its own ground.** Trees, nuggets, canisters, deposits and
databanks are scattered from the *same* measured surface table the chamber was sited
on, so `WorldEntities.Default` now skips any of them the chamber stands on
(`WildernessChamber.Covers`, a 22 m disc). Without it a tree grows through the roof
and a nugget sits on the floor — both happened, and the tests named them
(`the shrine is inside tree-46`, then `metal-12 stands inside the Revival Chamber`).
Skipped rather than moved: those tables are generated fields and a hand-nudged entry
would be a lie about where the ground is.

### 2.6 The slot

`Respawner01` stands at chamber-local **(0, 0)** — island-local
**(160.00, 4.18, 32.00)**, the same x/z as the chamber, pinned as an *equality* so
the two can never drift apart one edit at a time. That is where retail's own spawn
plate sits, 11 m further down under the terrain.

| | |
| --- | --- |
| to the nearest chamber geometry at standing height | **10.0 m** |
| to the entry corridor | 12.7 m (chamber-local +x) |
| to the ramps and both quest trigger boxes | further still, and 11 m below the floor, buried |
| prompt reach on the floor | `sqrt(4.5² − 0.20²) = 4.50 m` horizontally, inside a 9 m clear radius |
| walk from the spawn point | **55.6 m**, **0.52 m below** the spawn's ground vertex |

The walk is on one level. It was checked with a flood fill over Haven's contiguous
8 m surface grid that never climbs more than 2 m per 8 m cell — which also rules out
the site this document used to name at `(80, 29.57, 64)`: that one is 141 m away
behind a **147% slope**, i.e. a cliff.

### 2.7 "Register", and what the client actually sends (2026-08-18, live)

A player walked to it and the game showed the plate rendering, a yellow interactive
outline, and a prompt reading **"Hold E — Register"**. So `Respawner01` renders as a
standalone world entity, the prompt appears, and the placement, burial, radius, root
visualizer and layer are all CONFIRMED in a live client. Then: *"nothing happens when
i hold e"*.

**"Register" is not a verb.** `Bossa.Travellers.Interact.InteractVerb` decompiles to
exactly `{ Default, Activate, PickUp, Man, Inventory, Craft, Harvest, Forced, Design,
ReclaimShip, ShipBoost }` — there is no `Register`. It is a UI label chosen for the
object, and the object is retail's Reviver: `InteractiveObjectVisualizer.GetTutorialStep`
maps `Activate` + a non-null `RespawnerVisualizer` to `TutorialStep.MOUSE_OVER_REVIVER`.
I could NOT find the literal string in the shipped assets (`localization.bytes` is a
5 KB stub and `resources.assets` has no matching literal), so *where* the word is
composed is UNPROVEN — but it is not the verb.

**The verb the client sends is `Activate` (1). PROVED.** `Respawner01_unityclient` has
**no `InteractiveObjectVerbOverrider` anywhere in its hierarchy** (walked, every
GameObject, every MonoBehaviour), so `GetVerb(collider)` returns the root visualizer's
serialized field, and that field decodes to 1 — the same 48-byte layout that reads all
191 instances in `resources.assets` correctly. `WildernessShrine.Accepts(1)` is true,
so the verb is not the blocker and the three-entry hedge is not masking anything.

**The +10 s hold penalty is RULED OUT.** `InteractAgentObserver.CheckInteraction`
computes `time = flag2 ? interactTime + 10f : interactTime`, where `flag2` is true when
the object is a ship part on no friendly ship — and `Respawner01` *is* a ship part.
But `ShipPartVisualizer` carries **six `[Require]` readers**, starting with
`ShipRootState.Reader`, and this server seeds only 190602 + 1210. So the visualizer
never enables, never registers itself, and
`ShipPartVisualizer.GetShipPartVisualizer(entityId)` returns null — which makes
`flag` true, `flag2` false, and the hold exactly the **1.5 s** we seed. Worth writing
down because it was the best theory and it is wrong.

### 2.8 What was actually broken: nothing said anything

Measured on live production while the player held E: 1211 arriving at frame rate
(`rx 780 (1073:596 190602:166 1211:18)` in one 5 s window), and **zero** log lines
mentioning `graduation`. That is enough to say `WildernessGraduationService` never
ran. It is NOT enough to say the client sent an *interaction*: 1211 is a per-frame
look/slot stream and the rate counter cannot tell an update carrying a
`TriggerInteractWithObject` event from one carrying only "what am I looking at" — and
the handler returns early, in silence, when the event lists are empty.

So the three cases — **the client sent nothing**, **the client sent something we
ignored**, and **we matched and refused** — were indistinguishable in the log. That is
the bug behind the bug, and it is fixed:

* `Multiplayer.Wilderness.ShrineInteractRouting` is now a pure decision with a NAMED
  outcome (`NotTheShrine` / `NotOwner` / `WrongVerb` / `Use`) and a sentence for each.
* The handler logs **one line per completed interaction in the world**, naming the
  target id, the world-entity key it resolved to (or "not a world entity"), the verb
  and its numeric value, and ownership. Interact events are rare — the per-frame 1211
  stream returns long before this — so this is not a rate concern.
* Anything that named the shrine and was refused says which gate refused it, and does
  not fall through to the helm or mounted-part paths to pick up a second, more
  confusing line.
* `WildernessGraduationService.Use` logs on ENTRY, so "the dispatcher never called it"
  and "it ran and refused" can never look the same again.

One press on a live client now answers it. **Until that press happens, which of the
two remaining causes it is — the client not completing the hold, or our route missing
the event — is UNPROVEN, and this document does not guess.**

### 2.9 Where the tower goes: not a separate island — the shelf it is already on

Asked first for "one of the small empty floating islands around Haven", then, when
the user was standing there: *"look at where im standing this is a small island
attached to haven, empty the tree etc from it then place the tower here properly"*.

**There is no separate island to move to, and it would not be reachable if there
were.** Measured against the preserved Bossa MapFile
(`docs/research/world-data/wamap-islands.json`, 266 islands) and the 254-island
release catalogue, from Haven's origin `(17004.4, -318.7, -1134.2)`:

| distance | what it is |
| --- | --- |
| **2 962 m** | another **copy of Haven** (the 12-instance lane) |
| **3 098 m** | another copy of Haven |
| **3 845 m** | The Trades Challenge — 403 m across, 5 databanks, tier 3 |
| 3 961 / 4 160 m | The Old Military Academy / Anchorage Isle — 300-400 m across |

Nothing is small, nothing is empty, and nothing is closer than 3 km. A player on
Haven has no ship and the shrine is their only exit, so they cannot walk, fly or
grapple to any of them: moving the chamber there makes the teleporter unreachable,
which is strictly worse than it looking wrong. What is visible "around" Haven is
either those Haven copies at 3 km or the client-side distant-island silhouettes
(`Patching/SpatialOS/DistantIslandShells.cs`).

**The "small island attached to Haven" is the spawn shelf, and the chamber is already
standing on it.** Flooding the fine 2 m surface samples from the tree the user was
standing on — `tree-45` at island-local `(168.0, 4.52, 8.0)` — over a walkable step
(≤1.5 m per ≤5 m) gives **885 samples spanning x 105…257, z −46…76, y −1.81…12.00**,
and that region **contains the spawn point**. It is one broad low shelf, not a
detached islet, and `(160, 4.18, 32)` — where `tree-46` stood, and where the chamber
is — is in the middle of it.

So the chamber has NOT moved. What changed is the ground around it.

### 2.10 Clearing the ground, properly

The user asked for the trees to be **cleared**, and the previous build was skipping
them at registration — which worked but lied: the boot banner counted resources the
world never delivered. Now the keep-out happens at **generation**, so the placement
field never contains the point:

* `Resources.HavenSurface` gains a chamber keep-out applied to the tree, fuel and
  deposit configs through the generator's existing `PlacementExclusion` seam — the
  same mechanism that already keeps resources off the spawn and the ship.
* Two hand-written tables bypass the generator and had to be **edited**: the metal
  node `("iron", 4, 151.7, 4.00, 48.0)` (18.0 m from the axis, inside the walls) and
  the legacy fuel canister at `(152.0, 4.71, 0.0)` (33.0 m, on the cleared ground).
  Both are deleted with a comment recording why and that the user asked for it.
* The registration-time skip is KEPT as a safety net and is now a **no-op**, pinned
  by a test: if a future table puts something back inside the building, that test
  fails instead of the world quietly losing a resource.

**Two radii, because they answer different questions.**

| | |
| --- | --- |
| `ExclusionRadiusMetres` = **22 m** | the building's own above-ground collision. Nothing may stand inside it — including ore. |
| `ClearingRadiusMetres` = **35 m** | the cleared apron, for props a player looks at: trees and fuel canisters. |

35 m and not "the whole shelf" on purpose: the shelf contains the spawn point 55.6 m
away, and clearing it would strip the tutorial's own near-spawn wood. 35 m removes the
trees the user was standing among — the one they stood on is 25.3 m from the axis —
and leaves the spawn and the rest of the shelf wooded. Ore keeps its ground right up
to the walls: clearing metal to 35 m would have cost the starting island three of its
21 nodes to fix a look.

**Boot resource count: 1,722 → 1,726** on identical settings
(`WAREBORN_RELEASE_WORLD_DISTRICTS=tier1` with deposits, databanks, trees, metal and
fuel all on). It went UP, not down, because the tree and fuel fields fill to a target
count: excluding the apron does not delete those props, it re-seats them on other
measured ground, and the two hand-written deletions are more than offset. The number
that matters is that **nothing is skipped any more** — the registration guard now has
nothing to do, which the test asserts.

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

**PROVED on a real wire.** `tools/relaybot/run-ship-acceptance.sh` boots the native
server build and drives it with real ENet peers. Both entities go out, in order,
with the rotation:

```
[info] spawn plan (26 steps): ... -> RequestAsset wilderness-shrine-chamber
       -> AddEntity wilderness-shrine-chamber -> RequestAsset wilderness-shrine
       -> AddEntity wilderness-shrine -> ...
[info] requesting the game to load HavenAncientRespawner for world entity 'wilderness-shrine-chamber'...
[info] seeding 190602 for entity 11 (World 'wilderness-shrine-chamber' HavenAncientRespawner)
       at (17164.43, -325.529, -1102.167) m rot=535976447.
[info] requesting the game to load Respawner01 for world entity 'wilderness-shrine'...
[info] seeding 190602 for entity 12 (World 'wilderness-shrine' Respawner01)
       at (17164.43, -314.489, -1102.167) m.
[info] seeding 1210 for entity 12 (World 'wilderness-shrine' Respawner01)
       with verb Activate/Default/Man (shrine hedge), radius=5m, hold=1.5s, available=True.
```

The two are at the same x/z and exactly **11.04 m** apart in Y — the burial depth,
on the wire. The rotation is a real packed quaternion, not the 1023 identity
sentinel. The gate itself still PASSes.

**PROVED on a live client (2026-08-18), against earlier builds:** a world entity
spawned this way is checked out and its prefab resolves —
`[WAReborn] compiled entity template 'HavenAncientRespawner_unityclient'` in
`~/Games/WorldsAdrift/BepInEx/LogOutput.log`, emitted while the client processed the
shrine's own `AddEntityOp`. The same sessions gave us both failure reports: the
player stuck inside the shell, and then "i cant find the teleporter now".

**Still needs a live client — NOTHING in the current build has been walked to:**

1. **Does the chamber render, and does the doorway actually land on the ground?**
   The burial depth is derived from four fine surface samples in the corridor. Four
   is enough to bound the span at 0.11 m and it is the best evidence the extracted
   terrain can give, but it is four samples, and the real collision mesh is not the
   thinned sample set. If the door is buried or floating, this is the number to
   change — `WildernessChamber.CorridorGroundY` — and nothing else.
2. **Is the room actually walk-in-able?** The doorway is 3.8 m wide and 4.29 m clear
   at its tightest by measurement; whether a player capsule and the real terrain
   agree with that is unwitnessed.
3. **Does `Respawner01` render** as a standalone world entity, and does the prompt
   appear? Verb, root visualizer, layer and radius are all recovered or measured;
   the prompt has never been seen.
4. **Is the 55.6 m walk clear on real collision?** It is on one level (0.52 m of
   height difference) and reachable by a flood fill that never climbs more than 2 m
   per 8 m cell, but nobody has walked it.
5. **Does the teleport itself fire?** 1211 -> `WildernessGraduationService.Use` ->
   190607 has never run from a real client.
6. **Does the crew rule work end to end?** Unit-tested, unproven live, and narrower
   than the phrase suggests: not "everyone on the platform goes at once", but *each
   member who uses the shrine resolves to the same island*. Proving it needs two
   accounts in one crew.
7. **Does the arrival land on solid ground** on each of the 46 islands.
8. **Does the crew feedback line render** for a message that is not about a crew
   action.

## 9. Things deliberately NOT done, and why

* **No client-mod change.** Everything here is server-side; the patcher does not
  need updating.
* **The chamber is not made interactive.** Seeding 1210 on it would re-create the
  sealed-well bug with the plate now 11 m under the floor.
* **The scattered tables were not edited.** Trees, nuggets, canisters, deposits and
  databanks the chamber stands on are SKIPPED at registration, not moved. Those
  tables are generated from measured terrain and a hand-nudged entry in one would be
  a lie about where the ground is.
