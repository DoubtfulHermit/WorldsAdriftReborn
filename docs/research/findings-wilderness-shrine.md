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
| Prefab | `CraftingStation` — the placed Assembly Station's own prefab, live-proven here |
| Registration key | `wilderness-shrine` |
| Position | Haven island-local **(156.00, 4.16, 28.00)** — the centre of the chamber floor |
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

### 2.2 What is used instead — and the rule that eliminated the alternatives

**THE SHIP-PART RULE. PROVED on a live client, 2026-08-18.** With the player standing
at the shrine (Haven-local `161.3, 4.3, 31.3`), every E press threw, seven times in
one session:

```
NullReferenceException: Object reference not set to an instance of an object
  ShipPartVisualizer.IsShipPartInFriendlyShip (String characterUid, ShipPartVisualizer)
  InteractAgentObserver.CheckInteraction (InteractiveObjectVisualizer, Collider)
  InteractAgentObserver.Update ()
```

`CheckInteraction` aborts before `StartInteraction` — no hold, no ring, no 1211,
nothing at the server. The client does:

```csharp
ShipPartVisualizer spv = ShipPartVisualizer.GetShipPartVisualizer(entityId);   // NON-null
flag = !(spv != null) || ShipPartVisualizer.IsShipPartInFriendlyShip(playerId, spv);
```
```csharp
Option<EntityId> shipRoot = shipPartVisualiser._shipRootStateReader.Data.shipRoot;  // reader is NULL
```

`ShipPartVisualizer` registers itself (so the lookup returns non-null) but carries
**six `[Require]` readers**; a standalone world entity seeded with 190602 + 1210
leaves them unsatisfied, so the visualizer never enables and the reader is null. The
client dereferences it unconditionally.

**There is no escape for a ship-part prefab.** Seed the six readers and the visualizer
enables — then `IsShipPartInFriendlyShip` returns false for a part on no ship, which
sets `flag2 = true` and makes the hold `interactTime + 10f`, i.e. **11.5 s**. Disabled
gives an NRE; enabled gives an 11.5-second hold. So:

> **Any prefab carrying a `ShipPartVisualizer` is unusable as a standalone
> interactable on this server.**

**The respawner family, swept against that rule.** The user asked, reasonably, "im
pretty sure there is a respawner item why not use that?" — and they are right that one
exists. It cannot carry a prompt:

| prefab | ship part? | `InteractiveObjectVisualizer` | verdict |
| --- | --- | --- | --- |
| `AncientRespawner` | no | **none** | no interaction component at all |
| `AncientRespawnerDouble` / `Triple` / `PoolWarmer` | no | **none** | as above |
| `HavenRuinedShipRespawner` | no | **none** | as above |
| **`KiokiRevivalChamberA`** — the "respawner item" (`Deployables` `personalReviver`) | no | **none** | a 31 × 49 × 41 m building shell; retail drove it from components/UI we do not serve |
| `KiokiRevivalChamberB` | no | **none** | as above |
| `HavenAncientRespawner` | no | on `SpawnPad`, depth 4, offset `(0, −2.704, 0)` | the sealed well of §2.1 |
| `Respawner01` | **YES** | root, offset 0, Activate | the NRE trap above |
| `TerritoryControlBeacon` | no | on `metal_ruin_beak`, offset `(0, +5.68, 0)` | Activate, but 5.68 m up a 29 m girder tower — too tall for the chamber |

So of ten respawner/reviver-family prefabs, exactly **two** have any interaction
component, and both are disqualified. **There is no non-ship-part respawner that can
show a prompt.** That is the honest answer to the user's question.

**What is used: `CraftingStation`.** Not a guess — it is the SAME prefab the placed
Assembly Station uses (`Placement.Deployables` `"assemblyStation"`), and that
station's Craft prompt is **live-proven on this server**: the user has built ships
through it.

| | |
| --- | --- |
| `ShipPartVisualizer` | **none** — clears the rule |
| `InteractiveObjectVisualizer` | on the prefab **ROOT**, offset `(0, 0, 0)` |
| serialized `Verb` | **Craft** (5) |
| root layer | **15 Interactive** |
| collider extent | x −1.16…1.16, y −0.57…1.26, z −1.16…0.99 — a 2.3 m console |
| seed | `{190602, 1004, 1005, 1210}` — byte-for-byte the Assembly Station's proven `TransformAndCraftingStation` |

1004 + 1005 satisfy `CraftingStationBehaviour`'s two `[Require]` readers so the seed
is the proven configuration; 1005 is seeded IDLE and this server never echoes the 1005
`PlayerStartCrafting` for the shrine, so **no crafting UI opens** — the E press becomes
a 1211 and nothing else. `Craft` is added to the advertised verbs; that is safe
because the dispatcher selects on the **target key** first and short-circuits, so a
Craft on the shrine can never reach the placed-station path or vice versa.

**Residual risk, stated:** `HasCraftingStationButUseForbidden` gates Craft behind the
client's `_isShipBuildingAware`, which defaults false until a UI event sets it. The
placed Assembly Station uses this prefab and works here, so the gate does not bite our
players — but a brand-new Haven player who has never become shipbuilding-aware could
get *"no crafting yet"* instead of the prompt. `IsCraftingStation` reads a serialized
field, not a reader, so it cannot NRE. If that gate does bite, `MakeshiftStorage`
(non-ship-part, root visualizer at offset 0, layer 15, verb Inventory, no crafting
gate) is the fallback with no gate at all.

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

### 2.5 The chamber as the room — the "clean slot" *(RETIRED 2026-08-19 — see 2.9.2)*

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

### 2.6 The slot *(SUPERSEDED 2026-08-19 — the shrine now stands at the tower's foot, see 2.9.3)*

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

### 2.9 Where the tower goes — measured, and the constraint that decided it *(SUPERSEDED by 2.9.3)*

The user asked three times and was finally MEASURED. Standing on the spot they
meant, the server read it off the entity carrying them
(`carry-echo ... relativeTo 98` = `deposit-16`): **Haven-local (168.00, 4.52, 8.00)**.
They had pointed at the same physical coordinate twice — it was `tree-45` the first
time and `deposit-16` the second only because the tree field regenerated and keys are
positional.

**The building cannot stand on that spot, and here is exactly why.** Measured against
the resolved Haven prop list:

| from (168, 8) | authored structures |
| --- | --- |
| within 12 m | **0** — the immediate ground is clear |
| 12.4 m | first structure: a camp pipe at y 13.85 (9.3 m overhead) |
| within 22 m | **14** |
| within 26 m | **33** |
| within 35 m | **78** |

The chamber's collision footprint reaches **21.85 m** from its axis and it rises to
**24.1 m** above ground, while the camp pieces within 40 m span **y 0.5 … 26.3**. A
40 × 36 m building there overlaps the ruined metal camp on the ground *and* punches
through its platform deck overhead — the exact failure that trapped a player at the
very first placement. **Usable clear radius at the user's spot: 12.4 m, against the
~22 m the building needs. Overhang: 9.5 m on its long side.**

So it went to the closest point that genuinely works, chosen by sweeping every fine
(2 m) surface sample within 70 m of their spot against all 24 yaws — **317 workable
(site, yaw) combinations** — ranked by distance to the spot and then by how squarely
the doorway faces it.

**Chamber: Haven-local (156.00, −6.45, 28.00), yaw 45°. Shrine: (156.00, 4.16, 28.00).**

| | |
| --- | --- |
| distance to the user's measured spot | **23.3 m** (nearest workable site of any yaw was 20.0 m — those 2.7 m bought the doorway) |
| doorway aiming | **0.97**, about **14° off** pointing straight at them. The previous placement pointed **132° away**: from where they stood they were looking at the back wall. |
| corridor terrain | 4.40 → burial `origin Y = 4.40 − 10.85 = −6.45` |
| doorway clear height | **4.40 m** against a 2.20 m player |
| interior floor vs sill | **+0.07 m** — you step in dead level |
| terrain under the footprint / inside the room | **1.81 m** / **0.40 m** (the flattest of any candidate) |
| nearest authored structure to the footprint | **4.1 m** clear |
| walk from spawn | **57.3 m**, 0.54 m below the spawn's ground vertex — one level |

**Caveat, thinner than last time:** only **one** fine surface sample falls in the entry
corridor here (the previous site had four). The doorway has 2.2 m of margin over a
player so a sample or two of error is absorbed, but if the door lands buried or
floating, `WildernessChamber.CorridorGroundY` is the one number to change.

### 2.9.2 The tower was half in the ground, and no site could fix that (2026-08-19)

The user looked at the result and said: *"put the tower in Haven somewhere else where
it makes sense, not where it is right now — it's half in the ground, it's ridiculous."*

**They were right to the metre.** `respawner_exterior_LOD0` — the mesh a player sees —
spans prefab-local y **−7.36 … +30.49**, i.e. **37.85 m** of building. The doorway sill
is **10.85 m** up that wall, so putting the sill on the ground puts
`10.85 + 7.36 = 18.21 m` of the mesh underneath it. Measured at (156, −6.45, 28)
against Haven's 2 m LOD0 surface (441 probes on the footprint):

| | |
| --- | --- |
| terrain under the whole 36 × 40 m footprint | 3.83 … 10.46 (**6.64 m**, not the 1.81 m recorded before) |
| mesh bottom / roof, island-local | −13.81 / 24.04 |
| mean terrain | 4.78 |
| **buried** | **18.59 m of 37.85 m = 49%** |
| exposed height at the perimeter | 18.1 … 19.8 m |

**And it is the doctrine, not the site.** All 3,863 flat fine surface samples were
re-swept against all 24 yaws under the buried rules (door on the ground, floor at or
above the sill, footprint clear of authored props): **821 workable (site, yaw)
combinations, and the best of them stands 50.9% proud.** Every burial anywhere on
Haven is roughly half a building. Moving it could only ever have bought centimetres.

**Two measurement bugs found on the way:**

1. **The doorway is on prefab-local −z, not +x.** The prefab's own collider tree:
   `Ramp01` is a box at x −1.81…1.81, **z −14.17…−12.97**; `Ramp02` at x −1.81…1.81,
   **z −14.72…−14.16**; `Light By Door` hangs at (−0.09, 14.20, −5.96); and the entry
   lobe of `respawner_interior_LOD0` reaches **z = −29.6** at y 9…17. The old record —
   "free channel |z| ≤ 1.9 for local x 13…21" — is the same numbers with the two axes
   transposed. `CorridorGroundY` was therefore sampled against a **solid wall**. It did
   not bite (the ground was flat on both bearings: 4.36…4.72 along the real corridor
   against a 4.21 corridor floor) but the yaw was being aimed by the wrong face.
2. **The ramps never reached the ground.** Both ramp boxes live at y 9.50…10.66,
   *inside* the corridor. There was never a way in from the terrain — only a way in
   from a terrain raised to meet the door.

### 2.9.3 Standing it up — the placement that shipped instead

`GroundLineLocalY = 0` — the prefab's own ground line, where the foundation spike has
finished widening (r 8…11 m at y −7.4, r 12…16 by y −1) and the authored interior floor
sits. Only the footing is buried.

**Chamber: Haven-local (156.00, 4.46, 20.00), yaw 240°. Shrine: (176.78, 4.60, 32.00).**

Chosen by re-sweeping the same 3,863 samples × 24 yaws under stood-up rules — the base
ring seats, the footprint is flat and clear on the ground and overhead, the shrine's
slot at the foot is walkable — giving **145 workable (site, yaw) combinations over 39
distinct sites**, then filtering on the two clearance pins the tests already hold.

| | |
| --- | --- |
| seat: terrain on the wall ring (72 probes, r = 11/14/16 m) | 4.07 … 5.45, median **4.46** |
| dug in at the worst bearing / standing off at the best | **0.98 m** / **0.39 m** |
| terrain under the whole 36 × 40 m footprint (399 probes) | 4.01 … 6.16, spanning **2.15 m** |
| mesh bottom / roof | −2.90 / 34.95 |
| **exposed height** | **29.51 … 30.88 m — it stands 78.0% … 81.6% proud** |
| nearest authored structure to the axis | **29.3 m** (6.37 m clear of the footprint rectangle) |
| authored structures inside the footprint / overhead below the roof | **0** / **0** |
| doorway sill | island y 15.31 — **9.87 m above the ground at its foot** |
| to the spot the user measured out (168, 8) | **17.0 m** (was 23.3 m) |
| from the spawn point | 54.4 m |

**The cost, stated plainly: the room is gone.** The one aperture is now ~10 m up a sheer
wall, so the shrine cannot stand inside it. It stands at the tower's **foot** instead —
prefab-local (0, −24), on the −z face the doorway looks down, rotated by the chamber's
own yaw so the two can never drift apart. Haven-local (176.78, 4.60, 32.00):

| | |
| --- | --- |
| ground across its whole 4.5 m prompt ring | 4.28 … 4.76 (**0.48 m**) |
| out from the tower's axis | 24.0 m — 7.5 m clear of the wall at ground level, 2 m clear of the front lobe overhead |
| nearest authored structure | **22.4 m** |
| from the spawn point | **41.9 m**, and only 25° off the straight line from spawn to the tower |
| flood fill spawn → pad, ≤ 2 m rise per 6 m cell | **REACHABLE, 45.3 m walk** |

**Why the yaw is 240° and not the 300° that aims the front dead at the approach:** the
ruined metal camp lies between this site and the spawn, so every square-on yaw lands the
pad 8 – 11 m from the camp's raised platform decks — under a deck, which is the failure
that trapped a player at the very first placement. 240° trades 47° of facing for 22.4 m
of clearance.

**The test that was missing.** Every check the buried placement passed was about the
doorway or the room; none asked what the building looks like from outside.
`WildernessChamberTests.The_building_stands_proud_of_the_ground_it_is_on` reads the
terrain out of the embedded Haven surface table and requires **≥ 70%** of the mesh above
it. Re-scored against that same table, every placement that has ever shipped fails:

| placement | proud |
| --- | --- |
| (176, 16) | 41.0% |
| (168, 24) | 46.2% |
| (160, 32) | 48.1% |
| (156, 28) — the one the user complained about | 49.2% |
| **(156, 20), stood up** | **78.4%** |

**Still unproven until somebody walks to it:** how the 30 m tower reads in the client at
this seat (the seat is a median over 72 probes, so up to 0.98 m of terrain climbs the
wall on one bearing and 0.39 m falls away on another), and whether the doorway 9.87 m up
reads as a ruin's high entrance or as a mistake. Nothing can be reached through it on
foot, which is deliberate: a player who got in would be in a sealed drum with a 10.85 m
drop and no way out.

### 2.9.1 The topography, corrected

An earlier round concluded "the small island IS the spawn shelf, so there is nothing
to move to". That was wrong in the way that mattered, and the flood-fill threshold was
why: **2 m per 8 m is a 25% grade and bridges saddles**. Redone on the fine 2 m samples
with a 4 m neighbour radius, Haven's low ground does separate:

| step threshold | tree-45's lobe contains the spawn? |
| --- | --- |
| 0.25 m | **no** |
| 0.50 m | **no** |
| 0.75 m | **no** |
| **1.00 m** | yes — the neck bridges here |
| 1.50 m | yes |

So the split is real and sits between **0.75 m and 1.00 m** of rise per 4 m. At ≤0.5 m
Haven resolves into 2,977 components, and the relevant two are:

| lobe | samples | extent | largest solid rectangle | 40×36 fits? |
| --- | --- | --- | --- | --- |
| **A** — holds the user's spot AND the chamber | 368 | x 124…188 (64 m), z −46…68 (114 m) | **44 × 60 m** | **yes** |
| **S** — the SPAWN's own lobe | 47 | x 196…216 (**20 m**), z −10…32 (42 m) | 20 × 20 m | **no** |

Worth recording: the *spawn* sits on the small lobe; the user's spot and the chamber
are both on the large one. So the chamber was always on the same landform the user was
standing on — what was wrong was 25 m of offset and a doorway pointing the other way.

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
