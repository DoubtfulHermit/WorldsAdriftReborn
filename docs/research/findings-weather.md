# FINDINGS 7 — THE WEATHER ECS "EXCEPTION STORM" (upstream issue #34)

Research only. No repository files were modified.

Paths: `S` = `/tmp/claude-1000/-home-ttanurhan-Documents-Claude-Projects/ff15d21e-990d-43c5-9a18-cdf8ff2884cf/scratchpad`,
`R` = `/home/ttanurhan/Games/WAReborn-src`.
Logs: `L1` = `~/Games/WorldsAdrift/BepInEx/LogOutput.log` (single client, 255,255 lines),
`L2` = `~/Games/WorldsAdrift-2/BepInEx/LogOutput.log` (3,876,499 lines).

---

## HEADLINE: the premise is wrong in a way that changes the whole answer

The brief, `S/multiplayer-dossier.md:40` and `R/docs/roadmap.md:67` all describe this as a
**NullReferenceException that unwinds the ECS tree every FixedUpdate**. All three claims are
false. Measured against the real logs:

1. **It is not an exception.** It is a `WALogger.Error` call — a plain log line — at
   `S/ecs/BossaECS.Framework.Systems/AddToIdComponentToEntityMapS.cs:63`. The "stack trace"
   under it is not an exception stack; it is a stack the *logger* captures for context
   (`S/acs/WAUnityLogger.cs:28`).
2. **It is not a NullReferenceException.** Of the 16,012 NRE blocks in `L1`, **zero** contain
   any `BossaECS` or `Weather` frame. They come from unrelated Unity `Update()` code:
   `ChararacterDrunk.SetDrunkLevel` (11,318), `ChararacterDrunk.Update` (1,381),
   `PlayerExternalDataVisualizer.IsEditingShip`/`IsDriving` (2,630),
   `PlayerMove.get_AccelerationFastestSprint` (681). These are a **separate, unrelated bug**
   that was conflated with the weather spam because both are `[Error : Unity Log]` lines
   interleaved in the same file.
3. **Nothing is aborted.** See Q2 — proven two independent ways.

The real defect is a **duplicate-key collision in an id-to-entity map that re-fires every tick
because the error branch forgets to mark the entity**. That half of the prior analysis is
correct and is confirmed below.

---

## Q1 — ROOT CAUSE

### The failing branch

`AddToIdComponentToEntityMapS<TComponent,TId>` keeps a map from a derived id to an entity
index, and marks each mapped entity with `InEntityMapLocalComponent<TComponent>` so it is
not reconsidered. Its filter is:

```
_filter = SystemBase.And(_idComponents.Has, SystemBase.Not(_inMapComponents.Has));
```
— `S/ecs/BossaECS.Framework.Systems/AddToIdComponentToEntityMapS.cs:30`

`Execute()` has four branches (`AddToIdComponentToEntityMapS.cs:33-75`):

| branch | line | marks entity? |
|---|---|---|
| id already maps to *this* entity | 43-51 | yes (`ReplaceComponent`, :47) |
| id maps to a *destroyed* entity | 52-60 | yes (`AddComponent`, :56) |
| id maps to **another live entity** | 61-64 | **NO — logs and falls through** |
| id not in map | 66-73 | yes (`AddComponent`, :69) |

The third branch is the bug. It calls
`WALogger.Error<...>($"Attempting to add existing id that points to another entity: ...")`
(`:63`) and does nothing else. The entity keeps `WeatherCellCoordsC` and still lacks
`InEntityMapLocalComponent`, so the filter at `:30` matches it again next tick, forever.
**This is a self-perpetuating log loop, not a crash loop.**

Note `:42` and `:45` are dead/pointless: `component2` is read but never used, and the `:45`
warning claims "A mapping already exists!" — that string appears **0 times** in both logs,
confirming only the `:63` branch fires.

### Why the id collides — the actual "why"

There is nothing null anywhere. The collision is a legitimate duplicate key.

**The key function.** The map is bound with a Cantor pairing of the cell coordinates:

```
container.Bind<IdComponentToEntityMap<WeatherCellCoordsC, uint>>().FromInstance(
    new IdComponentToEntityMap<WeatherCellCoordsC, uint>(
        (WeatherCellCoordsC c) => CantorPairUtils.GetCantorPairId(c.X, c.Z)));
```
— `S/wasys/WASystems/Installer.cs:25`

`GetCantorPairId(0, 0)` returns `0` (`S/.../WASystems.Util/CantorPairUtils.cs:28-35`:
`num=0, num2=0 -> (0+0)*(0+1)/2 + 0 = 0`). **Every entity at world X~0, Z~0 gets id 0.**

**Who writes the coords.** `AddWeatherCellCoordsS` derives them from the entity's transform:

```
_filter = SystemBase.And(_weatherCells.Has, _transforms.Has, SystemBase.Not(_weatherCellCoords.Has));
...
Vector3d position = _transforms.GetComponent(entityIndex).LocalPosition.ToNativeVector();
CantorPairUtils.GetFlooredCoords(position, weatherCellSpacing, out var x, out var z);
```
— `S/wasys/WASystems.Systems.Weather/AddWeatherCellCoordsS.cs:28, 38-39`

with `WeatherCellSpacing = 500f` (`S/wasys/WASystems.Systems.Weather/WeatherCellGenesisS.cs:22`).
So **any entity that has both `WeatherCellStateC` and a transform** gets weather-cell coords —
the filter does not check that the entity actually *is* a weather cell.

**Why several entities qualify — the WAReborn-specific cause.** In the real game, weather cells
are dedicated entities laid out on a grid by `WeatherCellGenesisS` at distinct positions
(`WeatherCellGenesisS.cs:51-63`), so their ids never collide. That system is a snapshot/world-gen
system and is **not in the client's FixedUpdate config** (see Q2) — on a live client, weather
cells arrive from the server.

WAReborn's server does not model weather cells. It manufactures a hardcoded
`WeatherCellState` for **any entity whose client asks for component 1139**:

```
else if(componentId == 1139)
{
    WeatherCellState.Data wcData = new WeatherCellState.Data(new WeatherCellStateData(1f, new Vector3f(0f, 0f, 0f)));
    obj = wcData;
}
```
— `R/WorldsAdriftRebornGameServer/Game/Components/ComponentsSerializer.cs:412-417`

reached from a blind loop over the client's requested interests:

```
for (int i = 0; i < interestCount; i++) { ...
    ComponentsSerializer.InitAndSerialize(destination, entityId, interests[i].ComponentId, &buffer, &len);
```
— `R/WorldsAdriftRebornGameServer/Networking/Wrapper/SendOPHelper.cs:79-83`,
driven by `R/WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.cs:656` and `:683`.

And every entity's transform defaults to the same place:

```
TransformStateData tInit = new TransformStateData(new FixedPointVector3(new List<long> { 0, 100, 0 }), ...
```
— `ComponentsSerializer.cs:59` — i.e. **X = 0, Z = 0** for every entity the server initialises.

So: *N* entities receive a fabricated `WeatherCellStateC` **and** a transform at X=0/Z=0 ->
all are assigned `WeatherCellCoordsC{X=0, Z=0}` -> all hash to Cantor id **0** -> the first one
wins the map and every other one hits the unmarked error branch every tick.

This matches the observed message **exactly and invariantly** — all 10,280 occurrences in `L1`
are byte-identical:

```
Attempting to add existing id that points to another entity: 0. Existing entity index: 0, entityIndex: 1.
```

id `0`, existing entity `0` — precisely `GetCantorPairId(0,0) == 0`.

### Log evidence that the collider count tracks the player count

- `L1` (one client): one colliding entity, `entityIndex: 1`, 10,280 errors.
- `L2` (two clients): **two** colliding entities. `grep -o 'entityIndex: [12]' | uniq -c` yields
  an opening run of **1,599 consecutive `entityIndex: 1`**, then **210,615 alternating
  single-occurrence runs** of `1,2,1,2,...`. Perfect strict alternation = both entities are
  processed in the same `Execute()` loop, in entity-index order, every tick. The opening run
  (~1,599 ticks ~ 32 s at 50 Hz) is the period before the second player entity existed.

So: **one error per colliding entity per FixedUpdate tick**, and colliding entities accrue as
players join. Answering the brief's sub-question directly: **yes, the real cause is that a
weather entity the server never legitimately provides is being faked into existence on entities
that are not weather cells.**

---

## Q2 — BLAST RADIUS: **REFUTED**, two independent ways

The hypothesis was that the exception unwinds the ECS tree and kills every system scheduled
after it. It does not, and could not.

### Reason 1 — there is no exception, and had there been one it would be caught and loud

`SystemBase.TryExecute()` wraps `Execute()` in a `try/catch` and routes to a handler:

```
try { Execute(); }
catch (Exception e) { _systemExceptionHandler.Handle(this, e); return false; }
```
— `S/ecs/BossaECS.Core.System/SystemBase.cs:165-173`

`CompositeSystem` calls **`TryExecute()`**, not `Execute()`, on each child
(`S/ecs/BossaECS.Core.System/CompositeSystem.cs:14`), and so does
`IdComponentToEntityMapWrapperSystem` (`.../IdComponentToEntityMapWrapperSystem.cs:26-33`).
Every node in the tree is individually guarded. The brief's claim that "composite wrappers have
no catch" is **wrong** — they don't need one; each child guards itself.

The bound handler on the online (client) path is `DisableSystemExceptionHandler`:

```
container.Bind<ISystemExceptionHandler>().To<DisableSystemExceptionHandler>().AsSingle();
```
— `S/sptr/SpatialTranslator/Installer.cs:34`, inside `InstallOnlineBindings`

reached via `EcsBootstrap.Init` -> `WASystems.Installer.InstallOnlineBindings`
(`S/acs/EcsBootstrap.cs:55` -> `S/wasys/WASystems/Installer.cs:23`). It **swallows and disables**;
it does not rethrow (`S/ecs/BossaECS.Core.System/DisableSystemExceptionHandler.cs:8-12`).
(`ThrowExceptionHandler`, which *would* rethrow and unwind, is bound only in
`InstallOfflineBindings` — `Installer.cs:20` — the offline/snapshot tool path, not the client.)

That handler logs a distinctive banner: `"Dear QA,\nTHIS IS A CRITICAL ERROR!!!!! Disabling: {type}"`
(`DisableSystemExceptionHandler.cs:10`).

> **`grep -c 'Dear QA'` = 0 in BOTH logs.**

This is a strong negative result well beyond weather: **no ECS system has ever thrown from
`Execute()` and been silently disabled in either session.** The "invisible force-disabled
system" failure mode the brief hoped to uncover **is not occurring**.

### Reason 2 — nothing is scheduled after it anyway

`EcsBootstrap` builds the FixedUpdate tree from an embedded JSON `TextAsset`
(`S/acs/EcsBootstrap.cs:61-62`). I extracted it from
`/home/ttanurhan/Games/WorldsAdrift/UnityClient@Windows_Data/sharedassets0.assets`
(saved to `S/ecs_config.json`). It is complete and short:

```json
{
  "FixedUpdate" : {
    "typeName" : "SpatialTranslator.Systems.SpatialRuntimeWrapperS, SpatialTranslator",
    "subSystems": [
      { "typeName": "AssignViewInstancesSystem, Assembly-CSharp" },
      { "typeName": "UpdateViewInstancesMapSystem, Assembly-CSharp" },
      { "typeName": "RemapTransformsCompositeSystem, Assembly-CSharp" },
      { "typeName": "BlightSharedCompositeSystem, Assembly-CSharp" },
      { "typeName": "BlightViewSystem, Assembly-CSharp" },
      { "typeName": "WASystems.Systems.Weather.AddWeatherCellCoordsS, WASystems" },
      { "typeName": "WASystems.Systems.Weather.UpdateWeatherCellCoordsMapS, WASystems" }
    ]
  },
  "OnDrawGizmos" : { "typeName" : "BossaECS.Core.System.CompositeSystem, BossaECS",
    "subSystems" : [ { "typeName" : "DrawWeatherCellGizmosS, Assembly-CSharp" } ] }
}
```

**`UpdateWeatherCellCoordsMapS` is the LAST entry.** Within the JSON-configured composite,
nothing whatsoever runs after it.

### What the throwing system *would* have taken down (the counterfactual)

This is worth recording because the hypothesis was *plausible* and its consequence would have
been severe. The JSON composite is only element **9 of 21** in the enclosing lifecycle wrapper
(`S/sptr/SpatialTranslator/Systems/SpatialRuntimeWrapperS.cs:45-73`); `ecs.CreateSystem<CompositeSystem>(array)`
is at `:60`. Twelve systems follow it:

| # | system | line | what it does |
|---|---|---|---|
| 10 | `AddSpatialEntityObjectsS` | :61 | binds new spatial entities to view objects |
| 11 | `UpdateAllSpatialEntityObjectsBuilder` | :62 | pushes ECS state into visualizers |
| 12 | `AddCreateEntityRequestsS` | :63 | entity creation requests |
| 13 | `IdComponentToEntityMapWrapperSystem<CreateSpatialEntityRequestC,uint>` + `ProcessCreateEntityResponsesS` | :64 | creation responses |
| 14 | `ProcessSpatialEntityDestroyRequestsSystem` | :65 | destroys |
| 15 | `AddDeleteSpatialEntityRequestsS` | :66 | delete requests |
| 16 | `IdComponentToEntityMapWrapperSystem<DeleteSpatialEntityRequestC,uint>` + `ProcessDeleteEntityResponsesS` | :67 | delete responses |
| 17 | **`SendAllSpatialUpdatesBuilder`** | :68 | **publishes all locally-authoritative component updates to the server — this is how the client sends its own `TransformState` (190602)** |
| 18 | `SendAllSpatialCommandRequestsBuilder` | :69 | outbound commands |
| 19 | `SendAllSpatialCommandResponsesBuilder` | :70 | outbound command responses |
| 20 | `ClearAllRemoteFlagsBuilder` | :71 | per-frame flag reset |
| 21 | `AllShortcircuitSystemsBuilder` | :72 | short-circuit pass |

plus `_ensureNoAllocatedFlagsSystem` after `base.Execute()`
(`S/ecs/BossaECS.Core.System/EcsLifecycleWrapperSystem.cs:29`).

Had the exception really unwound the tree, **#17 would never run and no client would ever
publish its position** — multiplayer movement would be dead outright. It manifestly is not
(`R/docs/multiplayer.md:1-4` records working two-client movement). That working movement is
itself independent proof the unwinding does not happen.

### The one real structural hazard (correct in the prior analysis, but not firing)

The **filter phase is genuinely unguarded**. In `TryExecute`, the filter block
(`SystemBase.cs:137-161`) sits in a `try/finally` with **no `catch`**; only `Execute()` has one
(`:165-173`). An exception thrown from `EntityFilter.Refresh()` (`:146`) would propagate through
every enclosing `TryExecute` — whose outer `try` also has only a `finally` (`:179-183`) — all the
way to `EcsBootstrap.FixedUpdate` (`S/acs/EcsBootstrap.cs:75`), aborting all 21 systems including
`SendAllSpatialUpdates`. **This is a real latent landmine and worth hardening, but there is no
evidence it has ever fired**, and it is unrelated to the weather spam.

### Sleeper finding, inverted

The brief hoped Q2 would reveal silently-dead features. The honest answer is the opposite, and
it is still valuable:

- **Nothing is silently disabled** (`Dear QA` = 0 in both logs).
- **Nothing is aborted** (weather is last; every node is individually `TryExecute`-guarded).
- The genuinely surprising finding is **how little the client ECS does**: the entire
  FixedUpdate gameplay config is *seven* systems, two of which are weather bookkeeping.
  Player movement, drunk state, grappling, sailing etc. are classic Unity MonoBehaviours, not
  ECS systems. Any theory that blames ECS scheduling for a gameplay outage is structurally
  unlikely.
- The **16,012 NREs are the real unexplained defect in these logs** and they are *not*
  weather. `ChararacterDrunk.SetDrunkLevel` alone throws 11,318 times (~54/s — also once per
  tick). That deserves its own investigation and is very likely a genuine
  missing-component/null-visualizer bug of the same family as the `PlayerExternalDataVisualizer`
  NREs the server already works around by injecting component 1109
  (`R/WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.cs:647-648`).
- Four of the six weather systems **never run at all** on the client — only
  `AddWeatherCellCoordsS` and `UpdateWeatherCellCoordsMapS` are in the config.
  `RecomputeWeatherCellStatesS`, `InterpolateWeatherCellsS`, `UpdateInterpolatedWeatherCellsS`
  and `InitialiseInterpolatedWeatherCellsS` (all present in `S/wasys/WASystems.Systems.Weather/`)
  are server/gsim-side. **Client-side weather can never be dynamic by design.**

---

## What this actually costs (measured, not assumed)

**Rate.** In `L1`, 10,280 errors spanning `2026-08-07T22:49:32` -> `22:52:59`, across 208 distinct
seconds, peaking at **51-52 per second**. Unity's default `fixedDeltaTime` is 0.02 s = 50 Hz.
That is **one error per FixedUpdate tick per colliding entity** — and, usefully, it means the
fixed-step loop is running at ~99% of nominal, i.e. **the storm is not collapsing the frame
budget**. In `L2` it is two per tick.

**Per-error cost.** The dominant cost is a full managed stack capture taken *unconditionally*,
before any log-level or destination filtering:

```
string stackTrace = ((e == null) ? GetStackTrace(3) : e.StackTrace);
```
— `S/acs/WAUnityLogger.cs:28`, where `GetStackTrace` is
`return new StackTrace(numFramesToPop).ToString();` (`WAUnityLogger.cs:129-131`).

The captured stack is 17 frames with fully-qualified generic type names (~1.9 KB of text per
error). Then `LogToLocal` formats and calls `UnityEngine.Debug.LogError`
(`WAUnityLogger.cs:93, 103-105`) — the format string at `:93`
(`"[{0}]{1}{2}\n{3}"` with `DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")` and `".."`)
matches the observed log lines exactly, confirming this is the emitting path.

**Log volume.** 10,280 x 17 lines = 174,760 of `L1`'s 255,255 lines — **68% of the log**.
In `L2`, 212,214 x 17 ~ 3.61M of 3.88M lines — **93% of the log**, and the bulk of a 333 MB file.
BepInEx writes this synchronously.

**Honest verdict on magnitude.** `R/docs/roadmap.md:67` calls this "the biggest single perf win
available". That is **not supported**. Two `StackTrace().ToString()` captures plus ~34 lines of
synchronous file I/O per tick at 50 Hz is real but modest — plausibly low single-digit percent,
and the ~99%-of-nominal tick rate argues against anything dramatic. The strongest concrete
benefits are **log legibility** (a 3x-14x smaller log, in which the *real* NRE bug becomes
visible) and removal of sustained disk churn and GC pressure. I would sell it as a
**diagnosability fix with a modest perf bonus**, not a headline perf win.

---

## Q3 — FIX OPTIONS, ranked by risk

Repo Harmony convention: attribute-based, `internal class <Type>_Patch` under
`WorldsAdriftReborn/Patching/<Area>/`, discovered by
`Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "com.WAR.com")`
(`R/WorldsAdriftReborn/WorldsAdriftReborn.cs:71`). Example: `R/WorldsAdriftReborn/Patching/Ecs/EcsBootstrap_Patch.cs`.

### RECOMMENDED — (d') Prefix `AddWeatherCellCoordsS.Execute`, lowest risk

**Target:** type `WASystems.Systems.Weather.AddWeatherCellCoordsS` (public, non-generic,
assembly `WASystems`), method `protected override void Execute()`
(`S/wasys/WASystems.Systems.Weather/AddWeatherCellCoordsS.cs:31`).
**Shape:** `[HarmonyPrefix]` returning `false` to skip the original.

Because no entity then ever receives `WeatherCellCoordsC`, the map system's filter
(`AddToIdComponentToEntityMapS.cs:30`) is permanently empty, and `TryExecute`'s required-filter
early-out (`SystemBase.cs:148-155`) returns `false` before `Execute()` is even entered.
**Error count goes to exactly zero, by construction.**

*Why this target rather than the map system:* it is public and non-generic, so the repo's
existing attribute-based patching works unmodified, and it sidesteps the generic-patching risk
described under option (a).

**Side effects — does weather still work?** Effectively unchanged, because **weather is already
non-functional**:
- `GlobalWeather.GetCellSampleAt` looks the map up by Cantor id and, on a miss, returns a
  hardcoded fallback `Wind = (1, 0, -2)`, `Pressure = 0.5f`
  (`S/acs/Assets.Visualizers.Weather/GlobalWeather.cs:57, 65-68`).
- Today the map contains exactly **one** entry — id `0` -> entity `0` — whose
  `WeatherCellStateC` is the server's fabricated constant `pressure = 1f`,
  `wind = (0,0,0)` (`ComponentsSerializer.cs:414`; field order confirmed by
  `S/gencode/Bossa.Travellers.Weather/WeatherCellStateData_Internal.cs:11-12` and
  `S/sptr/Bossa.Travellers.Weather/WeatherCellStateC.cs:234, 236`).
- So the *only* behavioural change is inside the single 500 m x 500 m cell at the world origin,
  where wind goes from **dead calm (0,0,0)** to the same light constant breeze (1,0,-2) as the
  entire rest of the world. Everywhere else is already the fallback and is bit-identical.
- Since the island sits at `Coordinates(0,0,0)` (`ComponentsSerializer.cs:432-433`), this cell
  covers the play area — so the visible effect is that a discontinuity at the origin
  **disappears**. Arguably an improvement.

**Who consumes weather** (so the change is scoped honestly): `SailBehaviour.cs:64`,
`SailVisualizer.cs:75`, `StormDebris.cs:82`, `WeatherTextureGenerator.cs:200`, and
`GlobalWeather.GetTurbulenceAt` (`GlobalWeather.cs:174-183`). All go through `GetWeatherAt`, so
all see the same "constant light breeze everywhere" both before and after.

**No other consumer of `WeatherCellCoordsC` exists.** A repo-wide grep finds only the writer
(`AddWeatherCellCoordsS.cs:19,40`), the map system (`UpdateWeatherCellCoordsMapS.cs:15`) and the
DI binding (`Installer.cs:25`). `DrawWeatherCellGizmosS` is in the `OnDrawGizmos` root and is
excluded at runtime by the `"Gizmos"` exclude-tag (`S/acs/EcsBootstrap.cs:60`).

**Risk:** low. **Reversibility:** trivial. **Fixes upstream #34 generally?** Yes for the
symptom, no for the underlying map bug.

### (a) Mark the entity on the duplicate branch — most correct, highest technical risk

**Target:** `BossaECS.Framework.Systems.AddToIdComponentToEntityMapS`2` (assembly `BossaECS`),
method `Execute()`. It is `internal` (`AddToIdComponentToEntityMapS.cs:12`) and **generic**, so
the patch must be applied *manually*, not by attribute:

```
var open   = AccessTools.TypeByName("BossaECS.Framework.Systems.AddToIdComponentToEntityMapS`2");
var closed = open.MakeGenericType(typeof(WASystems.Components.Weather.WeatherCellCoordsC), typeof(uint));
harmony.Patch(AccessTools.Method(closed, "Execute"), prefix: ...);
```

The fix itself: on the `:61-64` branch, also add
`InEntityMapLocalComponent<WeatherCellCoordsC>{ CurrentId = component }` to the entity so the
filter stops matching it — i.e. a transpiler/prefix reimplementation reaching the private
`_inMapComponents` store (`:18`) by reflection.

**UNVERIFIED, and the reason I do not recommend this first:** Harmony's ability to patch a
method on a **closed generic type over value-type arguments** in this Unity 5.x Mono runtime is
not something I could confirm statically. Mono does emit distinct specialised code for
value-type instantiations (so it is plausible), but this needs a runtime test before anyone
relies on it. Harmony's own documentation flags generics as unreliable.

**Side effects.** The entity becomes marked "in map" while genuinely *not* in the map — an
invariant violation. On destroy, `RemoveFromIdComponentToEntityMapS` will take the
`value != entityIndex` path and emit a **warning** (`.../RemoveFromIdComponentToEntityMapS.cs:43-46`)
once per destroyed entity — noisy but bounded, and it does **not** corrupt the map (it only
removes the local marker; `_map.Remove` is correctly skipped). Also, if the entity that *owns*
id 0 is ever destroyed, the marked entity never re-attempts, so cell (0,0) silently falls back
forever. Immaterial here since weather is already constant.

**Upside:** this is the genuinely general fix — it repairs the behaviour for *every*
`IdComponentToEntityMap`, not just weather, and is the closest thing to an upstream-quality
patch. **Weather still "works"** exactly as much as it does today.

### (c) Stop the server fabricating weather components — correct root-cause fix, server-side

**Target:** `R/WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.cs:656` and `:683`
(and `:650`, `:666`) — filter `ComponentId == 1139` out of the `InterestOverride` array before
calling `SendAddComponentOp`.

**Do NOT simply delete the `1139` branch at `ComponentsSerializer.cs:412-417`.** All four call
sites pass `failOnComponentInitError: true`, and `SendAddComponentOp` **aborts the entire
AddComponentOp send** when a component yields `len <= 0`
(`R/.../SendOPHelper.cs:85-94`). Removing the branch would break client setup wholesale. The
interest list must be filtered *before* the serialisation loop.

**Effect:** the client never receives `WeatherCellState`, so `_weatherCells.Has` is false
everywhere, `AddWeatherCellCoordsS`'s filter (`:28`) never matches, and the storm ends at the
source with **no client patch at all**.

**Side effects / risk:** medium and partly unverified — I could not confirm whether any client
visualizer has a `[Require]` gate on `WeatherCellState` that would break by its absence. Note
`R/docs/multiplayer.md:58-62` records that over-seeding components makes visualizers throw in
`OnEnable`; *under*-seeding a component the client explicitly asked for is the mirror risk and
should be tested. **Weather:** identical outcome to (d'). **Does not fix upstream #34** (which is
a client/BossaECS bug); it fixes WAReborn's trigger.

### (b) Guard the unguarded call site — **vacuous for this bug**

There is no unguarded call site throwing here: nothing throws at all, and every composite child
is already individually `TryExecute`-guarded (`CompositeSystem.cs:14`,
`IdComponentToEntityMapWrapperSystem.cs:26-33`). This option is **not a fix for the weather
storm** and should be dropped from the ranking as stated.

It *is* worth keeping as **independent hardening** for the real hazard in Q2: a
`[HarmonyFinalizer]` on `BossaECS.Core.System.SystemBase.TryExecute` (public — an easy,
non-generic target, `SystemBase.cs:127`) that swallows exceptions escaping the filter phase
would close the one path that could genuinely abort the 21-system chain. Risk: it changes
failure semantics globally and could mask real bugs; it should log loudly. Recommend only as a
separate, clearly-labelled change.

### (d) Disable the weather system entirely — blunt variant of (d')

**Target:** `WASystems.Systems.Weather.UpdateWeatherCellCoordsMapS.Execute()`
(`S/wasys/WASystems.Systems.Weather/UpdateWeatherCellCoordsMapS.cs:18`), prefix -> `false`.
Public and non-generic, so equally easy.

Slightly worse than (d'): `AddWeatherCellCoordsS` still runs and still assigns
`WeatherCellCoordsC` every time a new qualifying entity appears (wasted work, and the
components accumulate), and the map is never populated **at all** — so even cell (0,0) loses its
entry. (d') is strictly cleaner. Weather outcome: identical (constant fallback everywhere).

---

## Q4 — VERIFICATION

**Primary counter (definitive, zero new tooling).** The error string is unique and invariant:

```
grep -c 'Attempting to add existing id' ~/Games/WorldsAdrift/BepInEx/LogOutput.log
```
Baseline **10,280** (client 1) / **212,214** (client 2). Target after fix: **0**.
Also assert the absence of new noise the fix could introduce:
`grep -c 'Id removed, but the old id doesn.t point to this entity'` (option (a) only) and
`grep -c 'Dear QA'` (must remain 0 — proves nothing got force-disabled).

**Secondary — log volume.** Total line count and file size. Expect `L1`-shaped sessions to drop
~68% of lines and `L2`-shaped sessions ~93%. Cheap, unambiguous, and the practical benefit:
`grep NullReferenceException` becomes readable, which is how the *real* `ChararacterDrunk` bug
gets found.

**Frame-time signal without new tooling — three options, no code required:**

1. **The error rate is itself a tachometer.** Errors/second == FixedUpdate ticks/second per
   colliding entity. Extract with
   `grep -o '\[2026-[0-9T:-]*\]\.\.\[ERROR\].*AddToIdComponentToEntityMapS' | grep -o '2026-[0-9T:-]*' | uniq -c`.
   Baseline: mean 49.4/s, peak 51-52/s against a 50 Hz nominal. **Capture this before the fix**
   — it is the only free measurement of the loaded state, and it disappears once fixed.
   (Caveat: FixedUpdate ticks track *game* time, so this measures slowdown, not frame time.)
2. **`SystemBase` already self-instruments.** It maintains `_executeStopwatch` and
   `_filterStopwatch` and exposes `ExecuteElapsed` / `FilterElapsed`
   (`SystemBase.cs:25-27, 65-67`), with `ToString()` formatting
   `"{name}, Filter: {ms}ms, Execute: {ms}ms"` (`:204-207`). The systems are reachable from
   `EcsBootstrap.Instance.Ecs` (`S/acs/EcsBootstrap.cs:24, 26`), so a throwaway reflection dump
   from the existing mod prints per-system cost with **no new instrumentation built**.
3. **`Bossa.Travellers.Analytics/FPSLogging`** already exists in the client
   (`S/acs/Bossa.Travellers.Analytics/FPSLogging.cs`), as does the mod's own
   `R/WorldsAdriftReborn/Patching/Multiplayer/LocalPlayerTelemetry.cs`.

**Methodology caution.** Measure over a **fixed wall-clock window with a scripted, identical
route** (both clients idle at spawn is easiest and most reproducible). Do not compare raw totals
across sessions of different length — `L2`'s 20x larger count is mostly a longer session plus a
second colliding entity, not 20x worse behaviour. Normalise to errors **per second** and lines
**per second**.

**Expected magnitude — set expectations honestly.** Predicted saving is two
`new StackTrace().ToString()` calls plus ~34 log lines of synchronous I/O per tick. If the gain
measured this way is under ~1-2%, that is the true answer and the change should be justified on
log legibility and disk churn, not FPS.

---

## Q5 — UPSTREAM ASSESSMENT (no PR opened, per instruction)

**Would it be a clean, self-contained contribution? Yes — with an important caveat about how it
is framed.**

**Clean and self-contained:** option (d') is a single new file
(`WorldsAdriftReborn/Patching/Ecs/AddWeatherCellCoordsS_Patch.cs`, ~20 lines) matching the
existing convention exactly, with no dependencies on the `multiplayer` branch's work. Option (a)
is likewise one file but needs manual (non-attribute) registration, which is a small deviation
from the repo's `CreateAndPatchAll` pattern.

**The caveat — the issue title is misleading and the PR should say so.** Upstream #34 is
"WeatherCellCoordsC error spam", which is *exactly right*; it is this project's local notes
(`S/multiplayer-dossier.md:40`, `R/docs/roadmap.md:67`) that escalated it to an "NRE storm that
unwinds the ECS tree". A PR must not repeat that. It should state plainly: this is duplicate-key
**log** spam from a branch that fails to mark the entity
(`AddToIdComponentToEntityMapS.cs:61-64`), amplified by an unconditional stack capture per log
call (`WAUnityLogger.cs:28`); it aborts nothing.

**Which fix to upstream.** Option (a) is the most *upstream-appropriate* because it repairs the
generic map system for all component types and would benefit any fork. But it is a behavioural
patch to a third-party ECS with a real invariant trade-off, and its generic-patching viability is
**unverified**. Option (d') is the safer contribution and honest about being a suppression.
Option (c) is arguably the most *correct* fix for WAReborn specifically (stop fabricating weather
state on non-weather entities) and would be a natural, small server-side PR — but it is a
different codebase area and should be a separate PR.

**Recommendation:** propose (d') as the shippable fix, describe (a) in the PR body as the deeper
alternative pending a generic-patch feasibility test, and file (c) separately.

**Note on project rules:** `R/docs/roadmap.md:78-80` and the user's standing rule require an
explicit ask before any `gh pr create` on this project. None is implied here.

---

## UNVERIFIED / OPEN ITEMS (flagged explicitly)

1. **Harmony patching of a closed generic type over value types** in this Unity 5.x Mono
   runtime — plausible but untested. Blocks option (a). *This is the single most important thing
   to test before committing to (a).*
2. **Exactly which entities** carry the fabricated `WeatherCellStateC`. The mechanism is
   established (`ComponentsSerializer.cs:412-417` + `SendOPHelper.cs:79-83` + default transform
   at `ComponentsSerializer.cs:59`) and the collider-count-tracks-player-count evidence is
   strong, but I did not capture a server console log (none is persisted under
   `~/Games/WAReborn-servers/`) to name entity indices 0/1/2 definitively. Running the server
   with stdout redirected to a file would show
   `"[info] game requests components for entity id: N"` and
   `"[success] initialized and serialized componentId 1139"` and settle it in one session.
3. **Whether any client visualizer requires `WeatherCellState` to be present.** Blocks
   confidence in option (c).
4. **Absolute cost of `new StackTrace(3).ToString()`** on this runtime — not measured. My
   "low single-digit percent" estimate is inference from the ~99%-of-nominal tick rate, not a
   profile.
5. **The 16,012 NREs** (`ChararacterDrunk.SetDrunkLevel` x11,318, etc.) are **out of scope of
   this brief and unexplained**. They fire at a similar per-tick cadence and pay the same Unity
   exception + stack-trace cost. Given the measured evidence, **these are at least as likely to
   be the real per-frame cost as the weather spam is**, and they are a strong candidate for the
   next investigation.
6. **`L2` spans an unknown number of sessions** (3.88M lines). I used it only for the
   strict-alternation structural argument, which is robust to session boundaries; I did not use
   it for rate baselines.
