# UNDERSTORM S1 — LIVE FIELD REPORT AND HANDOVER

**Written 2026-08-20, immediately after the first live understorm on production.**
Repo: `/home/ttanurhan/Games/wareborn-main` · Production: `root@62.171.161.19`

---

## 0. THE HEADLINE

**The understorm works on production and was visually accepted by the
maintainer.** Lightning fired, on the live server, watched by a human. That is
the first time a storm has ever run on this server.

Two defects were found by watching it, and neither is a crash:

| # | defect | root cause | status |
|---|---|---|---|
| 1 | Resources came back **~3.5 minutes late**, long after the local storm had passed | KNOWN — S1 fires ONE world-wide reset at the LAST island's storm end | root-caused, fix specified below |
| 2 | **The sky never went dark or cloudy.** The maintainer remembers retail going "super cloudy and dark, and THEN lightning" | NOT established. Two concrete leads, neither confirmed | needs RE |

There is also one **unconfirmed observation** that must be checked before it is
either fixed or dismissed — see §4.

---

## 1. WHAT WAS DEPLOYED, AND WHERE THINGS STAND

- Branch `feat/understorm-s1` (off `feat/storms`), head `024e321`.
- Published `linux-x64` self-contained, rsynced to
  `/opt/wareborn/WorldsAdriftRebornGameServer-native/`, service restarted
  2026-08-20 10:44:53 CEST. `NRestarts=0`, `Result=success`.
- **No schema migration. No client mod. No patcher release.** The diff touches
  only `.Multiplayer` and the game server.
- Live config, drop-in `/etc/systemd/system/wareborn-game.service.d/storms.conf`:
  `WAREBORN_STORMS=1`, `CADENCE_SECONDS=900`, `DURATION_SECONDS=45`,
  `JITTER_FRACTION=0.2`.
  **900 is an acceptance value, not the authentic one.** The RECOVERED figure is
  **6300** (105 min, `TreeHarvest.UnderstormCadence`). Put it back when done.
- `WAREBORN_FLIGHT_WIND_SPEED` was reverted `4.0 → 2.236` in the same restart
  (see `flight.conf` for the reasoning).
- **Rollback:** `/opt/wareborn/backups/pre-storm-s1-20260820T084246Z/{game,live-data}`,
  or simply `WAREBORN_STORMS=0` — off is byte-identical on the wire.

⚠ `wareborn-game.service: Failed with result 'exit-code'` appears on every
restart. It is the OLD process exiting non-zero on SIGTERM, it has occurred **62
times in three days**, and it long predates this work. Not a crash loop — but the
shutdown path does not exit cleanly, which matters because the architecture audit
found a restart is a save-or-lose event for player position. Unowned.

---

## 2. DEFECT 1 — THE RESET IS ~3.5 MINUTES LATE. Root cause known.

### The evidence, from the production journal

| time (CEST) | event |
|---|---|
| 10:44:56 | boot: `understorms: ON over 47 island(s)` |
| 10:59:27 | first `(Telegraph)` — the 30 s warning staircase begins |
| **10:59:57** | **first `(Active)`** — first island's storm starts |
| … | 47 `(Active)` transitions total, staggered by the 0.2 jitter |
| **11:03:29** | **`[storm] understorm reset: Reset 3 tree(s), 1 metal node(s), and 0 fuel canister(s).`** |

The maintainer had cut **2–3 trees** and mined **1 metal node**. The reset line
says **3 trees, 1 metal node**. The mechanism is correct and complete — it
restored exactly what was harvested. **Only the timing is wrong.**

### Why

`ResetHarvestResources()`
(`WorldsAdriftRebornGameServer.cs:1727`) is **global** — it walks every tree,
node and canister in the world. S1 therefore fires it **once per generation, at
the LAST island's storm end**, rather than per island at that island's own storm
end. This was a *deliberate, declared* simplification (roadmap §14.10 S1 item 4,
and §14.10 S2 is the fix), because firing a global reset at each of 47 islands'
storm ends would reset the whole world 47 times per cadence, 46 of them while the
island in question was calm.

The consequence nobody predicted is the **player-facing** one: with 47 islands
and a 0.2 jitter on a 900 s cadence, the storm front takes ~2 m 47 s to sweep the
world, and the reset lands ~3 m 32 s after the first storm starts. A player
standing on an island that storms early watches their storm end, sees nothing
come back, and only gets their trees minutes later under a clear sky. **The
cause-and-effect the whole feature exists to convey is broken by the delay.**

At the authentic 6300 s cadence this gets *worse in absolute terms*: the sweep
scales with cadence, so the gap would be ~15–20 minutes.

### The fix — this is PHASE S2, and it is now the top priority

Roadmap §14.10 S2 already specifies it: per-island scope. It needs

- a public accessor over `ResourceInterestService._resourceIslands`,
- a **per-island** variant of `ResetHarvestResources()`,
- `WorldAdminResult.cs:54` already has the targeted result slot.

Reset each island's own resources when **that island's** storm ends. The global
reset then disappears entirely, along with the "last island" special case in
`IslandStormPolicy.WorldResetAt` (`:459`) / `DueWorldResetGeneration` (`:469`)
and `IslandStormWire` (`:73`).

**Acceptance:** stand on one island, cut a tree, and see it return **at the moment
that island's bolts stop** — not minutes later.

---

## 3. DEFECT 2 — NO DARK, CLOUDY SKY. Not established. Two leads.

### The report

> *"I remember the sky getting super cloudy and dark and then lightning
> happening."* — maintainer, on the live storm

Bolts appeared. The sky did not change. In retail the darkening was apparently
the **telegraph** — the thing that told you a storm was coming — which makes this
more than cosmetic: it is the warning channel, and our 30 s audio rumble is
currently carrying that load alone.

**Provenance: WIKI/recollection.** Before building anything, confirm the retail
behaviour actually existed and in what form. Do not design from the memory alone.

### ⚠ RESOLVED 2026-08-20 — Lead A is DEAD, and the real mechanism is found

**Everything below in Lead A was investigated and the answer is negative.** Kept
because the negative result is load-bearing: it stops the next agent spending a
day on it. The real mechanism is in **Lead D**, added at the end.

**The three "unknown" fields resolved** off
`gencode/Bossa.Travellers.Loot/IslandLightningTimerStateData.cs` (**RECOVERED**):

| field | real name | we send |
|---|---|---|
| 3 | `nextLightningTimestamp` (long) | `1234` |
| 4 | `lightningEndTimestamp` (long) | `1234` |
| 7 | `entitiesToInformOfStormStart` (List\<EntityId\>) | `{2}` |

**All three have ZERO consumers in the entire shipped client. PROVED**, with a
positive control so the method is not itself a false zero:

| symbol | Generated.Code | Assembly-CSharp | WASystems | SpatialTranslator | BossaECS |
|---|---|---|---|---|---|
| `get_EstimatedMilliTillLightningEnd` *(control)* | 1 | **1** | 0 | 0 | 0 |
| `get_NextLightningTimestamp` | 1 | 0 | 0 | 0 | 0 |
| `get_LightningEndTimestamp` | 1 | 0 | 0 | 0 | 0 |
| `get_EntitiesToInformOfStormStart` | 1 | 0 | 0 | 0 | 0 |
| `EntitiesToInformOfStormStartUpdated` | 1 | 0 | 0 | 0 | 0 |

The control field — the one the visualiser demonstrably reads — lands in
`Assembly-CSharp` exactly as it must. The three unknowns appear **only in the
generated schema** and in no consumer. This search **included `WASystems.dll` and
`SpatialTranslator.dll`**, the two assemblies missing from the decompile tree, by
grepping the shipped binaries with `-a`. So this is a real absence, not the usual
false zero. They are declared-but-never-read schema. **Sending real timestamps or
a populated entity list would change nothing on screen.**

**And the understorm visualiser has no sky code at all.**
`acs/IslandLightningTimerVisualizer.cs` is 277 lines and contains bolts
(`LightningStrike`/`LightningPathCreator`), the rumble loop
(`Play_IslandRespawn_Start`) and `AmbientCameraShake` — and **no** reference to
fog, ambient light, skybox, cloud or colour. **PROVED** by reading the whole
file. The understorm path never darkened anything.

### Lead D — THE REAL MECHANISM: the cloud shader has a `storm` channel

`acs/CmdBufClouds.cs:19-28` — the cloud renderer's per-sample weather struct:

```csharp
public struct weathInfo
{
    public float wall;
    public float storm;    // <-- the overcast channel
    public float biome;
    public float edge;
}
```

It samples `GlobalWeatherTextures.weatherTex` / `wallInfoTex`, which are built by
`acs/WeatherTexGenCpu.cs:147-171` — a texture encoding weather **per world
position**, including `GetStormWall(p)` (`:109`) written into the green channel
of `EncodeWalls` (`:125`).

**So the dark stormy sky is real, it is a per-position weather-texture channel,
and it is driven by the WEATHER/WALL system — not by 1254.** The maintainer's
memory is correct; it simply belongs to a different subsystem than the one S1
built.

Two consequences that change the roadmap:

1. **`WeatherTextureGenerator` was filed as "purely cosmetic" (§14.4.1).** That
   classification is **wrong in importance**, even if right about the dependency.
   If the overcast is the storm's *telegraph*, it is the warning channel, not
   decoration — and our 30 s audio rumble is currently carrying that load alone.
2. `WeatherTexGenCpu` is visibly a **dev/test generator**, not the shipped path:
   `EncodeWeather` computes an Fbm and then throws it away with a hardcoded
   `num = 0.2f;` (`:133`). Treat its numbers as placeholders; what matters is the
   **channel layout**, which is real.

**Next step, and it is RE not implementation:** establish what fed `weatherTex`
in the shipped GPU path, and whether any part of the `storm` channel can be
driven without the forbidden 1139 lattice — the storm-WALL half
(`GetStormWall`/`wallInfoTex`) looks authored-geometry-driven, and **44 typed
wall segments are already imported**. If the overcast rides the wall texture
rather than the cell lattice, it may be reachable. Do not assume either way.

### ⚠ RESOLVED 2026-08-20 — Lead D is ANSWERED. See `docs/research/findings-storm-sky.md`

Full chain traced producer → rendered sky. The five-line answer:

1. The shipped `weatherTex` producer is **`WeatherTextureGenerator`** (1 instance
   on `level0`). `WeatherTexGenCpu` has **0 instances** anywhere — dead test code,
   as suspected. Its numbers mean nothing; only the channel layout was real.
2. **The sky does not read `weatherTex` at all.** The cloud renderer samples only
   **`wallInfoTex`**. `_WeatherTex` occurs **0 times** in the shipped shader
   bundle; `_WallInfoTex` occurs 3 (positive control).
3. The overcast channel `wallInfoTex.g` is written **purely from authored wall
   geometry** (`StormRift`/`SandStorm`), never touching `GetWeatherAt` — so
   **no 1139**. The lattice's `Pressure` lands in `weatherTex.b/.a` and is read by
   nothing in the cloud or storm path. **Lead B's inference survives, strengthened.**
4. **YES, reachable server-side:** the seam is **`1204 WallSegmentState`** on the
   shipped `WallSegment` prefab — **one `[Require]`**, clean prefab, not in
   `ComponentAbsencePolicy`, no client mod, no schema migration.
5. **But 1204 gives a storm WALL** — a line, permanent, biome-scale, ~800 m
   influence — **not an island-local telegraph.** And §5 of that document finds
   **no wiki or client evidence that an understorm ever darkened the sky**; the
   recollection best matches flying toward a storm wall.

**So Lead D found a large missing world feature, not a fix for defect 2.**
Recommended split: (A) serve the 44 static walls as its own phase; (B) only then
consider a transient-`StormRift` understorm telegraph, labelled honestly as
WAREBORN TUNING. Roadmap correction: §14.4.1's "`WeatherTextureGenerator` needs
1139: YES" is true only of the **wind** half — the sky half is lattice-free.

### Lead A (superseded — the original reasoning, kept for the record)

`ComponentsSerializer.cs:1798-1805` constructs `IslandLightningTimerStateData`
with **seven** fields, and S1 only ever drives three of them:

```csharp
new IslandLightningTimerStateData(
    storm?.MillisTillNextLightning ?? 50 * 1000,   // driven
    storm?.MillisTillLightningEnd  ?? 0,           // driven
    1234,                                          // ??? HARDCODED
    1234,                                          // ??? HARDCODED
    false,                                         // isLightningActive - NEVER true (see hazards)
    (int)(storm?.Generation ?? 1),                 // driven
    new Improbable.Collections.List<EntityId> { new EntityId(2) })  // ??? HARDCODED
```

**Two integer fields are literal `1234` placeholders and one is an EntityId list
hardcoded to `{2}`.** Nobody has ever established what they mean. If the client's
storm-sky ramp is driven by any of them, the darkening is one field away and has
been the whole time.

**Do this first, it is cheap:** resolve the real field names off the shipped
component schema, then find every client-side reader. `IslandLightningTimerState`
readers beyond `IslandLightningTimerVisualizer` are the interesting ones.

### Lead B — the weather system, which is a much bigger and possibly closed door

The WIND agent established (**PROVED**, read off shipped bytes) that the shipped
`WeatherCell` blueprint grants `EntityReadAccess: ["social","physics"]` — **no
`"visual"`** — while the `Blight` blueprint sitting beside it asks for `"visual"`
explicitly. Inference: retail's own Unity client never read a weather cell, so
`GetWeatherAt` returned the `(1,0,-2)` fallback for everyone.

If that holds, the darkening **cannot** have come from the weather-cell lattice,
and 1139/1269 remain correctly forbidden (`ComponentAbsencePolicy` — attaching
1139 to gameplay entities produced a MEASURED 31,144 errors in 158 s). So the
sky effect is probably **island-local and driven by 1254**, which points straight
back to Lead A.

### Lead C — the client may already own the effect

Check whether a storm-sky, cloud or skybox ramp is baked into the island prefabs
or the global managers, waiting on a value we never send. The WIND agent found
the client already renders `WindTrail`, `WindControl`, `FlagWind` and full sail
luffing with no mod at all. **Assume the client is richer than the server, not
poorer.** Use UnityPy (§5) — `grep` cannot see inside bundles.

---

## 4. UNCONFIRMED — "a new rock appeared somewhere else"

The maintainer observed that after the reset, a mineable rock appeared but
**seemingly in a different location** than the one they had mined.

**This contradicts the code as written.** S1 declares placement **RESTORED, not
re-rolled** (a known divergence from retail, which §14.6.3 proves re-rolled;
closing it is S3). `ResetHarvestResources()` calls `Nodes.ResetAll()` and
broadcasts `BroadcastNodeReset(nodeId)` per node — a restore path, with no
re-seeding.

**Three possibilities, in order of likelihood, none verified:**
1. **Observer error.** Easy to misplace a node from memory, especially minutes
   later. Most likely, and must be ruled out first.
2. A *different, already-existing* deposit was mistaken for the reset one.
3. Something in the node reset path genuinely moves the entity — which would be
   a real bug and would also mean the "RESTORED not re-rolled" claim in the
   roadmap is false.

**Do not fix this until it is reproduced.** Method: record the node's entity id
and world position before mining (the boot log prints deposit positions), mine
it, wait for the reset, and compare. `BroadcastNodeReset` is at
`WorldsAdriftRebornGameServer.cs:1767`.

Note for context: `WAREBORN_SPAWN_METAL=0` and `WAREBORN_METAL_HANDSHAKE=0` on
production, so the client-side re-sample handshake that S3 would use is **off**
today. That is also S3's unrecorded prerequisite.

---

## 5. METHOD — THE ERROR CLASS THAT HAS COST THIS PROJECT THE MOST

An agent searches for a thing, does not find it, and **DESIGNS AROUND ITS
ABSENCE.** Known instances: fuel built per-hull when the tank IS the power
generator; gauges forced onto railings when BAR PIPES existed in this repo's own
`valid-icons.txt`; flight believed to need engines when the SKY CORE makes a hull
mobile; "BASHER is unreferenced" when it is wired end to end.

**Four mechanisms produce false negatives here, all in the same direction:**

1. **`grep` is ugrep.** On BINARY files it silently returns 0 matches and exit 1
   **unless you pass `-a`.** Any binary sweep without `-a` produced a false zero.
2. **The decompile tree is INCOMPLETE.** `/home/ttanurhan/Games/WAReborn-decompiled/`
   does **not** contain `WASystems.dll` or `SpatialTranslator.dll`. That false
   zero is what produced Phase 7's wrong "may force a client mod" blocker.
3. **Asset bundles are compressed — `grep` cannot see inside them at all.**
   Blueprints are TextAssets *inside* `resources.assets` (one at byte offset
   567382447); a `find` for `*blueprint*` filenames returns nothing and means
   nothing.
4. **`grep` here also silently skips gitignored files while exiting 0.**

**Use UnityPy.** It is not installed system-wide. A working venv and two scripts:

```
/tmp/claude-1000/-home-ttanurhan-Documents-Claude-Projects-AvatarServer/\
90e66b62-f8e7-4521-b491-26f295d4d837/scratchpad/
    unityenv/bin/python      # UnityPy 1.25.3
    scan_island.py           # enumerate MonoScripts per island bundle
    read_fields.py           # read serialized fields (bundles SHIP TYPE TREES)
```

These settled two questions the roadmap had filed as "only a live client can
answer", in about two minutes:
`IslandLightningTimerVisualizer` is on **255/255** island bundles, and the
prefab's strike cadence is `_min = 0.0` / `_max = 1.0`.

**Prefer that venv over any new tooling, and if it is gone, recreate it — the
project has no other way to read its own assets.**

**PROVENANCE LABELS on every non-obvious claim:** PROVED / RECOVERED / INFERRED /
WIKI / WAREBORN TUNING. Inventing balance numbers is fine and expected.
Inventing provenance is not.

---

## 6. HAZARDS

⚠ **NEVER write `isLightningActive = true`.** `IslandLocalTransformBehaviour`
teleports the island toward Y −250…−1500 on that flag. S1 verified the behaviour
is on **0 of 255** island bundles, so it cannot currently fire — but the rule
stands unrelaxed: `IslandStormUpdate` has no bool field at all, and two tests
(one reflective, one source-reading) go red if that changes. Drive storms through
the **int** fields.

⚠ **THE ATLAS CLIFF.** `AtlasMultiplier` is Bossa's shutdown doomsday clock and
evaluates to `0.0`, so `TotalLift` is zero and `UpdateVertical` returns early when
overloaded. Vertical flight works ONLY because `ShipLiftVisualizer` is inert on
our hulls; 1258 is seeded at a flat 1,000,000 kg so the overload rule cannot fire.
The overload string is exactly *"Ship weighs more than its atlas sky core can
lift."* (`ShipControlsBehaviour.cs:283`) — an agent once FABRICATED alternative
wording and committed it. Never invent client strings.

⚠ **SILENT `[Require]` FAILURE.** A Unity visualiser does not enable until EVERY
`[Require]` resolves, and it fails with **no log line** — a visible, dead prop.
Enumerate every requirement a component's presence could newly satisfy. Has
bitten five times.

⚠ **THE COUNTDOWN IS A STAIRCASE, NOT A CLOCK.** `TimeEstimationSmoother
.StepAndSmooth()` computes a smoothed countdown and **returns it without storing
it**; the client only updates on a push that jumps more than 7 seconds. The 30 s
warning exists **only** because the server pushes every 8 s. This is why
`WAREBORN_STORM_COUNTDOWN_REFRESH_SECONDS` has a **floor, not a ceiling**. A
single push at storm start yields a storm with no warning at all — **and every
test would still be green.**

⚠ **THE RELAY DEFECT.** The soak gate is legitimately red ~40% of runs: either a
position forwards in <1 ms, or every position is held one full 50 ms emit
interval while still emitting at 20 Hz with no drops. **Run the soak 3+ times.**
Diagnose by the number: a p50 of ~50.3 ms with near-zero drift is the defect; a
p50 of ~0.27 ms is healthy. See `docs/testing.md`.

⚠ **`/tmp` is not writable as root on production** (observed this session). Do
not stage there over ssh.

---

## 7. HARD RULES

1. **Own worktree, branched from current `feat/understorm-s1`** (or main once
   merged). `git submodule update --init WorldsAdriftRebornCoreSdk/enet` before
   any soak, or the native shim build dies looking like a missing source file.
2. **DO NOT BUILD OR INSTALL THE CLIENT MOD.** `WorldsAdriftReborn`'s
   `OutputPath` IS the maintainer's live plugin directory. Two agents have
   overwritten it mid-testing. If a client change is needed, **say so and stop** —
   it is also a patcher release.
3. **Production is READ-ONLY to agents.** Recommend; the orchestrator applies.
   Never restart the game server (it holds live progression), never change an env
   var, never touch the database.
4. **Never `pkill -f`**, and never a `pgrep -f`-derived pid list without excluding
   your own pid — an agent killed its own shell that way. Verify via
   `/proc/<pid>/cmdline`. Never kill the maintainer's game client or Blender.
5. **No synthetic input (no xdotool).** The maintainer tests actively and
   responds — end your report with an exact, ordered, copy-pasteable test script.
6. **No push, no PRs, no deploy.** Commit locally.
7. **No schema migration.** If one is genuinely needed, isolate it and say so
   LOUDLY: game and login servers must then deploy together, and a split deploy
   once destroyed a character's progression.
8. **No GitHub Actions CI.** Gates run locally:
   `...Multiplayer.Tests` baseline **4195/0** (S1 raised it from 4132),
   `WorldsAdriftServer.Tests` **1192 passed / 26 skipped**.
9. **MUTATION-TEST YOUR GUARDS.** This repo has twice shipped a green suite over
   an unplugged feature (tree felling shipped green and was shown to nobody for
   days). Break the production wiring, confirm exactly the right test goes red,
   and report what you broke and what caught it. Expect escapes on the first
   attempt.

---

## 8. RECOMMENDED ORDER

1. **S2 — per-island reset.** Root cause known, fix specified, closes the defect
   the maintainer actually felt. Highest value, lowest risk.
2. **The 1254 field archaeology.** Two `1234` placeholders and an `EntityId{2}`.
   Cheap, and it may hand you the dark sky for free.
3. **Reproduce or dismiss the moved rock** (§4). Do not fix on one observation.
4. **The storm sky proper**, once §3's leads resolve.
5. Restore `WAREBORN_STORM_CADENCE_SECONDS=6300` once the cycle is accepted.

**Do NOT stack damage (S4/S5) onto any of this.** There is no server-side damage
model at all (PROVED: no `DamageService`/`ApplyDamage`/`TakeDamage` anywhere;
1235/1225/4323 known-absent), and a storm that damages ship parts interacts with
the atlas lift arithmetic. Size it on its own.
