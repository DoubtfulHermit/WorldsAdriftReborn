# Island resource interest: why an island looked empty

Diagnosis and fix, 2026-08-18. This is the resource half of the same bug the
fauna work fixed two days earlier; `findings-island-fauna.md` is its sibling and
should be read alongside it.

## The report

A player used the wilderness shrine, arrived on **Mount Spero**
(`release-887053661`, tier 1, zone A2) and found the island **empty** - no trees,
no mineable deposits - while **manta rays were plainly visible**.

That asymmetry is the whole diagnosis. Fauna had already been moved to
island-keyed interest at 600 m; resources had not. They still checked out per
NODE against the player's own position at the global
`WAREBORN_INTEREST_RADIUS_M` (120 m load, 155 m unload in production).

## The measurement - PROVED

Landing point, global: `(-8694.589, -64.124, -3915.242)`.
Live position minutes later: `(-8902.284, -73.686, -3987.665)`.

Derived from the shipped release catalogue's own extracted AABB and node
positions (`WorldsAdriftRebornGameServer.Multiplayer/Islands/release-runtime-catalog.json`):

| quantity | value |
| --- | --- |
| Mount Spero AABB | **735 x 320 x 598 m** |
| resource nodes on it | 19 (14 deposits + 5 databanks + **0 trees**) |
| player distance to the island ENVELOPE | **0.0 m** - unambiguously standing on it |
| player distance to the landing point | 220 m |
| nodes within 120 m of the PLAYER | **2** |
| nodes within 120 m of the LANDING POINT | 3 |

The production log for that visit reports **"net keys still held on that island:
2"**. The catalogue-derived number and the live server's number are the same 2,
arrived at independently. `Islands/MountSperoResourceCheckoutTests.cs` pins that
derivation against the shipped catalogue so it cannot silently drift.

So the island was never "not sent". It was sent two nodes out of nineteen, and
walking swapped which two: the same visit logged **6 deposit additions and 6
deposit removals in six minutes**, with 99 additions and 113 removals across the
peer. The server was doing the work and immediately undoing it.

Across all 46 tier-1 islands, standing at the landing point, the 120 m bubble
held a median of **6** nodes against a median island content of **13**. Four of
the 254 islands had **zero** nodes in range at their own landing point.

## Why it is the fauna bug's mirror image

| | fauna | resources |
| --- | --- | --- |
| what moves | the ANIMAL orbits across the boundary | the PLAYER walks across the island |
| what stands still | the player | the node |
| symptom | creatures pop in and out | the island empties as you explore |

Both produce remove/re-add churn from a distance that changes for reasons
unrelated to whether the player is on the island, and neither is a tuning
problem. A radius that covered a 735 m island would have to be ~370 m even to
reach its corners from the centre, and it would then apply to every island in the
world at once.

## The fix - island-keyed, and deliberately the same code as fauna

`Islands/IslandInterestAdmissionPolicy.cs` now owns the one admission rule that
both features use. A peer holds an island's **whole** content while it is within
**600 m of that island's ENVELOPE**, retained to **800 m**. A node's own distance
to the player decides only the ORDER work is done in - nearest first - and never
whether it is done at all.

The property that matters is **structural, not numeric**: envelope distance is
zero everywhere on an island and changes only when the PLAYER travels, so nothing
on an island can flicker while the player is there, and no future change to where
entities sit or how they move can bring the churn back.

Three rules, unchanged from the fauna version and now shared:

1. hysteresis - held islands survive to the unload radius, new ones are only
   considered inside the load radius;
2. retention first - an island already held is admitted ahead of any newcomer, so
   a newly approached island can never evict the one under the player's feet;
3. whole island or nothing - a per-peer budget is spent in whole islands, because
   spending it entity by entity is the original bug rebuilt through the back door.

### Why 600 m, and not a number of its own - WAREBORN TUNING, measured

1. It is already measured. Against all 254 release islands' extracted AABBs, a
   peer standing on any island's landing point has **exactly one** island within
   600 m of its envelope. The world is not dense enough for a second to reach.
2. It sits far inside the terrain gate (`WAREBORN_TERRAIN_LOAD_RADIUS_M`, 4000 m
   in production), so terrain is always long since checked out by the time
   resources are admitted. The terrain gate is a CORRECTNESS requirement - never
   send a resource for ground the client has not loaded - and it is untouched.
3. **Diverging from fauna would recreate the reported symptom.** The bug was
   reported as "manta rays but no resources". Any radius below fauna's reproduces
   exactly the band of distances where one streams and the other does not.

## What it costs - measured, not claimed

The send cadence is **unchanged**: one lifecycle action per peer per 120 ms, and
an addition costs two of them (an AssetLoadRequest, then the AddEntity a full
cadence later). That is a structural ceiling of **8.33 actions/s per peer** that
no radius can move, and it is why this change does not touch the number the soak
gate measures.

What grows is how much a peer HOLDS and how long the queue takes to drain. From a
headless tier-1 boot of this branch:

```
[resource-interest] island-keyed checkout: 1723 resource(s) across 47 island(s);
  load 600 m / unload 800 m to the island ENVELOPE; per-peer budget 512 entities;
  largest island 125; worst-case 30.0 s to stream one island at 8.3 action/s.
```

| | before | after |
| --- | --- | --- |
| peak resources held by one peer | 49 (measured max inside 120 m at a landing point) | 125 (largest tier-1 island) |
| worst simultaneously-holdable pair | n/a | 170 (Crimson Paradise 88 + The Land that Man Forgot 82, from the catalogue AABBs at the 800 m unload radius) |
| time to fully dress the largest island | never - it dressed 2 nodes and re-dressed them forever | 30 s, nearest nodes first |
| per-peer wire rate | <= 8.33 actions/s | unchanged |

`WAREBORN_ISLAND_RESOURCE_PEER_MAX` defaults to **512**, which is a CEILING and
not a target: the measured worst case a peer can reach is 170, so there is room
for the world to roughly triple. The headroom is not decoration - admission is
whole-island, so a budget below an island's own content admits **nothing** for
that island, which is indistinguishable from the bug being fixed. That failure
mode is why `IslandResourceCheckoutPolicy.BudgetWarning` names any oversized
island at boot rather than letting it surface in a player report.

## The A/B that proves it end to end

The soak harness CAN exercise island-keyed checkout, on **Haven** - and this is
new. The documented "the bots never check out island terrain" gap only blocks
OPTIONAL release-island terrain; Haven is `unconditional` in
`IslandTerrainInterestService.IsTerrainReady`, so a bot standing on Haven passes
the terrain gate and the resource path runs for real. Two identical three-minute
runs, same harness, same bots, same island, `WAREBORN_INTEREST_RADIUS_M=120`:

| | base `0a8c1e8` | this branch |
| --- | --- | --- |
| distinct resource entities checked out to one bot | **15** | **106** |
| `[resource-interest] added` lines | 30 | 212 |
| `[resource-interest] removed` lines | 0 | 0 |
| relay staleness p50 | 50.54 ms | 50.58 ms |
| soak verdict | FLAT (drift +0.04 ms) | FLAT (drift 0 ms) |

Fifteen entities is the 120 m bubble around the Haven spawn. A hundred and six is
Haven's resource field. The relay is unmoved, which is the multiplayer-safety
claim stated as a measurement rather than an argument.

## Alternatives considered and rejected

- **Raise `WAREBORN_INTEREST_RADIUS_M`.** It gates the connect-time spawn plan for
  every peer as well as continuous checkout. Raising it to cover a 735 m island
  would apply that to every island simultaneously, which is the cost this design
  exists to avoid.
- **Widen the 35 m hysteresis.** The player is not dithering across a boundary,
  they are walking hundreds of metres past it. Widening the band enough to matter
  is the same thing as raising the radius.
- **Keep the active-island filter and just drop the radius.** That is close to
  what shipped, but the old `IslandResourceInterestPolicy.ReconcileSet` keyed on
  the ONE island the peer's 1073 named, with no hysteresis and no budget. It has
  been removed rather than left as a second, divergent answer to the same
  question.

## Gates

- Build: 0 errors across `WorldsAdriftRebornGameServer.Multiplayer`,
  `WorldsAdriftRebornGameServer`, `WorldsAdriftServer`, `WorldsAdriftReborn.Storage`
  and the three test projects. (`WorldsAdriftRebornCoreSdk.vcxproj` is C++ and its
  MSB4019 at the solution root is pre-existing on Linux.)
- `Multiplayer.Tests` **3248 passed / 0 failed / 0 skipped** (baseline on
  `0a8c1e8` was 3216/0/0; the delta is new tests for the two new pure modules plus
  the Mount Spero reproduction, and two tests rewritten onto the replacement API).
- `WorldsAdriftServer.Tests` **326 / 0 / 21 skipped** and `Storage.Tests`
  **52 / 0 / 122 skipped** - both unchanged from baseline.
- `tools/relaybot/run-soak.sh 10 7811` with the tier-1 release world enabled and
  both bots stood at Mount Spero's landing point: **VERDICT FLAT**, drift
  -0.07 ms, 21607 sends / 21608 matched relays (100% delivered), 0 gaps, 0
  disconnects, 0 decode errors, 0 1073 timeline violations.
- `tools/relaybot/run-ship-acceptance.sh 17781`: **PASS**.

Re-run after merging `fix/tier1-resource-coverage`, which gives every tier-1
island wood and takes the world from 1723 to **3390** boot resource entities:

```
[resource-interest] island-keyed checkout: 3390 resource(s) across 47 island(s);
  load 600 m / unload 800 m to the island ENVELOPE; per-peer budget 512 entities;
  largest island 127; worst-case 30.5 s to stream one island at 8.3 action/s.
```

- builds 0 errors; `Multiplayer.Tests` **3253 / 0 / 0**,
  `WorldsAdriftServer.Tests` 326/0/21, `Storage.Tests` 52/0/122.
- `run-soak.sh 10 7816` on that world, both bots at Mount Spero: **VERDICT FLAT**,
  drift -0.04 ms, trend +0.02 ms, 21606 sends / 21608 matched relays (100%
  delivered), 0 gaps, 0 disconnects, 0 decode errors.
- `run-ship-acceptance.sh 17783`: **PASS**.
- No budget warning: the largest island is 127 against a 512 ceiling, so doubling
  the world's resource count did not bring the whole-island rule anywhere near
  its bound.

The two changes complete each other. Thirty seconds is only an acceptable
whole-island stream because additions are ordered NEAREST-FIRST, and the tree
generator deliberately seats four trees and two deposits within 60 m of each
island's landing pad. On Mount Spero specifically that is now **60 oak** (an
INFERRED tier-1 wood, stamped `woodSource: inferred-tier`) and 14 deposits, with
the nearest deposit **17.9 m** and the nearest tree **28.8 m** from where the
shrine puts the player - both inside the first four seconds of the stream. The
island is 93 resource entities and the peer holds all of them for the whole
visit.

## What is still unproven

- **RESOLVED, 2026-08-18: the "terrain never ready" blocker was an artifact of
  the TEST environment, not a bug.** The earlier revision of this document
  reported that with the release world on, `IsTerrainReady` stayed false for
  every non-Haven island forever, and flagged it as unreconciled with
  production. The reconciliation: production runs **`WAREBORN_LOAD_BARRIER=1`**
  (it lives only in a systemd dropin on the VPS, which is why a locally-composed
  env missed it), and `LoadBarrier.Prime` - called at
  `WorldsAdriftRebornGameServer.cs:4066`, BEFORE the
  `IslandTerrainInterestService` constructor at 4129 - deliberately calls the
  ALLOCATING `registry.EntityIdFor(entity)` on **every** registration:
  "Allocate/bind the shared id now so it can be named before its AddEntity
  runs." With the barrier on, every island terrain id is bound at construction
  time, candidacy is `Managed = true`, and the terrain gate works. A bisect
  confirms `WAREBORN_LOAD_BARRIER=1` ALONE flips the boot from
  `50 world registration(s) have no entity id yet` to `unowned=0`.

  **The full chain is now proven under the exact production environment** (the
  complete WAREBORN_* set from the live unit, minus the DB), ten minutes, both
  bots at Mount Spero's landing point:

  - `[terrain-interest] added release-887053661 terrain 3224 to peer ...` -
    OPTIONAL island terrain checked out to a bot (via the bounded ack fallback,
    since bots do not send correlated asset acks);
  - **166 resource additions, every one of them Mount Spero's**, including
    `tree-release-887053661-*` from the new tier-1 tree coverage - **83 distinct
    keys x 2 peers, each added EXACTLY ONCE per peer. Zero removes and zero
    re-adds on the island: no churn**;
  - the only 30 removals were the connect-time Haven set being correctly
    unloaded after the bots' interest centre moved to Spero;
  - the soak also exercised FAUNA checkout for the first time (20 creature
    checkouts, 40,611 pose updates received), closing the harness gap
    `findings-island-fauna.md` documents;
  - **VERDICT FLAT**, drift -0.1 ms, 0 gaps, 0 disconnects, 0 decode errors.
    Delivery read 95.9% matched (vs 100% in the quiet-world runs) with the
    fauna pose stream sharing the wire; staleness p50 50.48 ms, in line with
    every other barrier-on run.

  Consequence for operators: island terrain interest - and therefore island
  resource checkout on release islands - REQUIRES the load barrier's boot-time
  id binding. A deployment that enables `WAREBORN_TERRAIN_INTEREST_ENABLED=1`
  without `WAREBORN_LOAD_BARRIER=1` reproduces the dead-terrain state described
  above. No code changed for this finding; whether terrain candidacy should
  bind its own ids instead of inheriting the barrier's side effect is a
  separate hardening question, deliberately not folded into this fix.

- **NO RETAIL CLIENT HAS SEEN THE FIX.** The prod-env soak above proves the
  whole server path on a RELEASE island - terrain checkout, island-keyed
  admission, the full 83-key set streamed once per peer, no churn - and the
  Haven A/B proves the before/after (15 entities becomes 106). What remains
  unproven is only the retail CLIENT side: a player walking a fully dressed
  island in the real renderer.

- **THE 600 m RADIUS AND THE 512 BUDGET ARE WAREBORN TUNING.** The measurements
  behind them are real; the choice of where to sit relative to those measurements
  is ours. Retail's own interest configuration did not survive.
