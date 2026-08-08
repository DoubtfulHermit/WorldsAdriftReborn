# PLAN — accounts, starter island, resources, usable tools

Goal, in the user's words: *make accounts a thing, switch the spawn island to the starter
island, make sure all resources are there and tools are usable.*

The four goals are not independent, and the ordering below is chosen so that nothing has to
be built twice. Two facts drive it:

1. **`ComponentsSerializer.InitAndSerialize` switches on `componentId` alone.** It receives
   `entityId` and ignores it (only the 1088 appearance branch uses it). **Every entity in the
   world therefore gets the same transform seed.** Haven is ONE asset placed at TWELVE
   positions, and resources are N entities at N positions — so **both** goals are blocked by
   the same 50-line change. It is also the real fix for the weather log storm (rule 15).
2. **Accounts must land before persistence has anything worth persisting.** Inventory,
   progression and learned schematics all key on a player identity. Building the loop first
   and re-keying it afterwards is rework; building identity first costs nothing extra.

Everything below is buildable server-side. Only Phase 3 touches the client mod, and only
Phase 1 may need a client change at all (TBD by research).

---

## PHASE 0 — Foundations. Blocks all four goals.

| # | Work | Why it blocks |
|---|---|---|
| 0.1 | **Per-entity `InitAndSerialize`** — a side table keyed by entityId, mirroring the existing `AppearanceStore` precedent at `ComponentsSerializer.cs:109` | Haven (1 asset → 12 positions) AND every resource node AND the correct player spawn |
| 0.2 | **Global entity-id allocation** for non-player entities, from the shared `EntityIdAllocator` | Cross-client references resolve **by id**. Per-client ids = one node per player, and the island bug all over again |
| 0.3 | **`failOnComponentInitError: false` for non-player entities** | Today one unhandled component id aborts the ENTIRE AddComponent batch, so an entity arrives fully rendered and completely inert **with no symptom**. Turns silent failure into a named log line |
| 0.4 | **Remaining log hygiene** — the per-component `SendAddComponentOp` lines are still unconditional | The server already stalled its own position relays once this way. Node spawning multiplies the volume |
| 0.5 | **`SendOPHelper.cs:222`** passes `updates.Count` instead of `cupdates.Count` | Reads past the array whenever any component fails to serialise — goes live the first time we batch two components |

**~3–4 days.** Almost all of it is unit-testable with no game running.

---

## PHASE 1 — Accounts

**The current state:** in-world identity is **runtime-proven** — the client publishes its
character's real GUID `characterUid` in the 1088 update and it is **stable across
reconnection** (observed: three reconnects, same uid). What is missing is upstream: the
client hardcodes `steam/1234` in the save URL, Steam auth is stubbed, and **one roster serves
the whole deployment**, so two clients can pick the same character and publish the same uid.

| # | Work |
|---|---|
| 1.1 | Make each client send a **distinct account id** (research in flight — the options run from a mod config value to patching the URL construction; `ModSettings.steamUserId` is currently dead config) |
| 1.2 | **Per-account rosters** on the login server, following the shipped `RosterPolicy`/`CharacterRepository` pattern exactly — pure policy, thin repository, unit tests |
| 1.3 | **Reject a second peer claiming a live `characterUid`** on the game server, and define what the client sees when rejected |
| 1.4 | **Migration** — the existing roster.json, existing characters, and the friend's already-installed client must not break |

**Security posture, stated plainly:** this is a friends-and-family server with stubbed auth.
Accounts here mean *"the server can tell two players apart and keep their stuff separate"*,
**not** *"a player cannot impersonate another"*. A modified client can claim anything. We
build the former and deliberately not the latter — no security theatre.

**~3–5 days**, depending on whether 1.1 needs a client change.

---

## PHASE 2 — The starter island

**Currently we spawn `949069116` — "Shattered Mausoleum", a tier-3 mid-game island — and drop
the player at the world origin.**

Haven is `1431299145`, instanced **twelve times** at x≈17000, all beyond
`Haven.xOfVerticalSeparator = 15943.65`. It is the only asset used more than once, which is
exactly why it needs Phase 0.1.

| # | Work |
|---|---|
| 2.1 | Swap the seeded island to Haven at one chosen instance position (research in flight) |
| 2.2 | **A real player spawn coordinate on the surface**, from the extracted `island-surfaces/` tables. Highest-risk item in the plan — three debugging rounds have already been lost to players teleporting or falling |
| 2.3 | Ordering: island AddEntity **and its colliders** must land before the player is placed, or they fall through the world |

**A free win:** we seed `8055 NewPlayerState = true`, which currently means *"the player is
still in Haven"* — **a lie about a Haven that does not exist**, costing four working UI
features. Spawn the real Haven and that seed becomes **truthful**. Whether the client derives
Haven-ness from position (x > separator) is being checked; if it does, some behaviour comes
back for free.

**~2–4 days**, dominated by 2.2.

---

## PHASE 3 — Usable tools

Two steps, and **the first is one array element**.

| # | Work |
|---|---|
| 3.1 | **Add `1211 InteractAgentState` to `AuthoritativeComponents`.** Already seeded; no serializer work. It resolves `InteractAgentObserver`'s `[Require]` writer, which is what enables player input handling at all. **Test: the yellow interaction outline appears. Today it appears on nothing, ever** |
| 3.2 | **A relay id filter, in the same commit.** The remote rig carries no `InteractAgent*` component; relaying 1211 to it addresses a component the receiving client never checked out. (Risk is lower than first thought — 1211 suppresses a send entirely when no field changed, so it is near-zero traffic when the camera is still) |
| 3.3 | **The beam:** seed + grant 2105, 2106, 2002, 1231. **`maxBoltDistance` must be non-zero** or the range check rejects every hit and the wire goes completely silent — a failure indistinguishable from "the grant didn't work" |
| 3.4 | A handler for the shot event |

**Note 2002 (repair) is required even for pure harvesting** — it is a `[Require]` on the same
behaviour that publishes the salvage shot. You cannot skip it.

**~2–3 days.** 3.1 alone is an afternoon and is independently verifiable.

---

## PHASE 4 — Resources present

Positions are **already solved and extracted**: 14.4M surface vertices across all 255 islands,
verified to be the exact geometry the runtime collides against, committed under
`docs/research/world-data/island-surfaces/`. **No client handshake is needed.**

| # | Work |
|---|---|
| 4.1 | Node registry + spawn from the extracted tables (needs 0.1, 0.2) |
| 4.2 | **Node state broadcast.** Much smaller than one report proposed: node state is server-authored, and `SendComponentUpdateOp` already takes an explicit entityId, so this is a fan-out loop, not a relay rewrite |
| 4.3 | Depletion, and **late-join replay: a destroyed node must STAY in the registry and be spawned for the joiner in its destroyed state.** Omit that and late joiners see intact rocks everyone else has mined |
| 4.4 | Which nodes: **metal, not trees.** Metal is the only line where spawn, aim, hit, deplete and collect all have live client implementations. `MetalNugget` is the cheapest — 4 components, baked geometry, renders with no visualiser init |

**~4–6 days.**

---

## PHASE 5 — Closing the loop

| # | Work |
|---|---|
| 5.1 | **`InventoryStore` keyed by entity** — today `InitAndSerialize` rebuilds a fresh 1081 on every seed, so **a second interest request silently resets the inventory to defaults and any granted item is gone** |
| 5.2 | An **item-id allocator** — none exists; duplicate ids are resolved by first-match **silently**, and removal deletes both |
| 5.3 | **`stacksize`** — unset for every item, so counts never render **and pulling a material out of a crafting slot ADDS material** |
| 5.4 | Yield → inventory push, plus the **`8060` salvage-feedback event** which makes the game render **"Salvaged Iron x12"** natively. Already seeded, no grant needed |
| 5.5 | Persist inventory + progression per account (Phase 1 is the prerequisite) |

**~4–6 days.**

---

## INTERLEAVED, AND WORTH DOING EARLY: RECONNECT

**~2 days.** Not for its user-facing value — because it turns a two-client testing window
from "one build, then relaunch both clients through login and character select" into "press
RETRY, iterate again". **The scarcest resource in this project is a testing window with a
human present**, and this is the only item that multiplies the rate at which everything else
can be validated. The riskiest part (ENet re-init inside one Wine process) is already
**runtime-proven**.

---

## TOTAL AND SEQUENCING

**~18–28 working days**, plus 3–5 two-client testing windows.

```
Phase 0 ──┬── Phase 1 (accounts) ──┐
          ├── Phase 2 (Haven)      ├── Phase 5 (loop closes)
          └── Phase 4 (resources) ─┘
              Phase 3 (tools) ─────┘
Reconnect ── anywhere early; do it first if a testing window is near
```

Phase 3.1 is an afternoon and is independently verifiable — **worth doing first as a morale
and confidence check**, since it is the one change that turns a dead subsystem on.

## THE STANDING CAVEAT
Roughly a third of confident static-analysis conclusions in this project have turned out
wrong when checked. Every phase above has at least one assumption that has never been run.
The largest: **the extracted coordinate chain has never been validated against a running
client** — rigorous where measured, inferred where not, which is precisely the shape of the
error it was correcting. Phase 2.2 is where that gets tested, and it should be tested with one
hardcoded coordinate before anything depends on it.
