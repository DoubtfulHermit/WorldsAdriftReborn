# The Wilderness shrine — graduating from Haven

**Status:** implemented on `feat/wilderness-shrine`. Server-side only; no client
mod change. Never deployed by this work.

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
| Prefab | `HavenAncientRespawner` — the Haven Revival Chamber |
| Registration key | `wilderness-shrine` |
| Position | Haven island-local **(176.00, 4.90, 16.00)** |
| Seeded components | 190602 (transform), 1210 (interaction) |
| Spawn order | `AfterPlayer` |
| Kill switch | `WAREBORN_WILDERNESS_SHRINE=0` (default ON) |

**PROVED — the prefab is real and loadable.** `havenancientrespawner` is line 80
of `docs/research/world-data/prefab-keys.txt`, and it is line 80 of the *same*
census embedded at
`WorldsAdriftRebornGameServer.Multiplayer/Ship/client-entity-prefabs.txt` that
`ClientEntityPrefabs` loads at runtime to refuse prefabs a client could not
resolve. `docs/research/loop/data/prefab-names.tsv:81` records a client **and** a
worker prefab for it. Exact casing (`HavenAncientRespawner_unityclient`) comes
from `docs/research/world-data/haven/haven-prefabs2.json`.

**PROVED — nothing better exists.** The island-prop library's `shrine1` /
`shrine2` / `Plinth` (`.../haven/guidlut.json`) are *meshes baked into island
bundles*, resolved by GUID. They are not in the entity-prefab census, so they
cannot be a `WorldEntity.AssetName` at all. The only other monument-shaped
loadable entity prefab is `TerritoryControlBeacon`, which has nothing to do with
graduation.

**INFERRED — the placement.** Retail's own pad position is not recoverable:
everything Haven-specific was spawned by the GSim, and `findings-haven.md` gives
only a relative barrier/teleporter offset. The chosen point is derived from
Haven's extracted LOD0 surface table
(`docs/research/world-data/island-surfaces/1431299145.json`) under the same
landing rule used for the Wilderness islands, restricted to a walkable band
around the spawn point:

* normal `ny = 0.996`; all 8 neighbouring 8 m columns level within **1.42 m**
* **34.2 m** from the spawn point, **0.20 m** above it — same shelf, no climb
* nearest authored static prop **15.26 m** in 3D / 13.73 m horizontally; nothing
  authored within 5 m horizontally from 2 m below to 15 m above
* clear of everything the server itself puts on Haven (33 m from the databank,
  32 m from the static dev ship frame)

Why not closer: the spawn point sits *inside* the ruined metal camp, and every
flat sample within ~25 m has that camp's platforms overhead or the dev ship frame
on top of it. Moving out to x = 176 leaves the camp while heading **toward** the
island's local origin — which is where retail's own text puts the chamber, "at
the center of the island".

### How a player interacts with it

The 1210 / 1211 pair — the same proven path that already makes a placed
shipyard's console and a metal nugget interactive. The server seeds 1210
`InteractiveState` on the shrine; the client's `InteractiveObjectVisualizer`
shows an E prompt (radius 3 m, hold **1.5 s** — longer than the shipyard's 0.5 s
because this is the one action on Haven that cannot be undone in the next
second); the completed interaction arrives as a 1211 `InteractWithObject` event
on the *player's* entity, naming the shrine as its target.

**INFERRED, and hedged — the verb.** `InteractiveObjectVisualizer.OnEnable` does
`Interactions.FirstOrDefault(i => i.verb == Verb)` **once**, against the verb the
*prefab* baked. We have the class name and the quest text, not the prefab's
serialized `Verb`. A wrong single guess is not a degraded prompt — it is *no
prompt at all*, permanently, with nothing in any log to say why. So the shrine's
1210 seed carries **one entry per plausible verb**: `Activate` (the quest text
says "activate"), `Default` (the enum's zero, where an unset field lands) and
`Man` (retail has the player *stand on* a platform, which is the verb the helm
uses for taking a position). The visualizer takes the entry matching its own verb
and ignores the rest, so the extras are inert. `PickUp` is deliberately absent: a
monument is not portable and a PickUp prompt on it would be a lie.

The interact dispatcher selects on the **target's registration key**, not on the
verb, and short-circuits — so a helm interaction can never reach the shrine, and
a shrine interaction never falls through to the helm or mounted-part paths. It is
owner-only: using the shrine moves the sender's character and can write their
crewmates' home rows.

---

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
[info] requesting the game to load HavenAncientRespawner for world entity 'wilderness-shrine'...
[success] asset loaded for 'wilderness-shrine'. creating entity 11 at (17180.43, -313.769, -1118.167) m...
[info] seeding 190602 for entity 11 (World 'wilderness-shrine' HavenAncientRespawner) ...
[info] seeding 1210 for entity 11 ... with verb Activate/Default/Man (shrine hedge), available=True.
[warning] wilderness shrine stands on Haven but the Wilderness is CLOSED: no tier-1
          island is registered. ...
```

So: it is in the spawn plan, the asset request goes out before the AddEntity, the
entity is created at the derived position, and the **multi-entry 1210 seed
serializes and is sent without dropping the batch** — the one wire risk that a
unit test could not settle. The gate itself still PASSes (2 pilots, coherent
frames, legal re-entry).

**Still needs a live client:**

1. **Does `HavenAncientRespawner` render** when spawned as its own entity rather
   than by the GSim's Haven spawner? (The same open question `Databanks`
   carries.)
2. **Does the E prompt appear**, and on which of the three verbs? The multi-entry
   seed is designed so that *one* of them works; the 1211 log line names the verb
   the client actually sent back, so one session settles it and the other two
   entries can then be dropped.
3. **Is the 34 m walk from the spawn point actually clear** on the client's real
   collision, and is the shrine visible from the spawn point?
4. **Does the arrival land on solid ground** on each of the 46 islands. The
   evidence is strong and uniform, but "measured surface sample" is not "stood on
   it". A visual acceptance pass over the 46 pads is the honest follow-up.
5. **Does the crew feedback line render** for a message that is not about a crew
   action.
