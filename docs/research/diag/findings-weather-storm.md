# THE WEATHER ERROR STORM — root cause, cost, and why it is NOT the stall

**Investigation only.** Nothing was changed, launched, deployed or pushed.

## 1. EXACT ROOT CAUSE

Our server fabricates `WeatherCellState` (component **1139**) on every entity
that asks. The client turns that into `WeatherCellCoordsC{X,Z}` by flooring the
entity's position onto a **500 m** grid, and keys a dictionary on the Cantor pair
of the cell. All five of our entities stand on one 60 m island, so they land in
one cell and share one key. Four lose the race, every tick, forever.

* **Component/field:** `WASystems.Components.Weather.WeatherCellCoordsC {int X; int Z;}` — client-local, never sent.
* **Producer:** `AddWeatherCellCoordsS.Execute()`, filter
  `And(_weatherCells.Has, _transforms.Has, Not(_weatherCellCoords.Has))` — *any*
  entity with `WeatherCellStateC` + `TransformStateC` gets grid coords. Spacing
  is `WeatherCellGenesisS.WeatherCellSpacing = 500f`.
* **The pairing:** `CantorPairUtils.GetCantorPairId(x,z)`:
  `a = x<0 ? -2x+1 : 2x`, `b = z<0 ? -2z+1 : 2z`, `return (a+b)(a+b+1)/2 + b`.
  Cantor pairing is a **bijection**, so a duplicate id proves **identical cell
  coordinates** — never a hash accident.

Positions taken from the server's own seeding lines (Q52.12 ÷ 4096):

| ECS idx | entity | world (m) | cell | id |
|---|---|---|---|---|
| 0 | island `1431299145@Island` | (17004.43, −318.67, −1134.17) | (34,−3) | **2857** |
| 1 | player 1 | (17212.43, −311.97, −1130.17) | (34,−3) | **2857** |
| 2 | `ship-haven` ShipFrame | (17212.43, −313.37, −1118.17) | (34,−3) | **2857** |
| 3 | `tree-haven` Tree | (17212.43, −313.68, −1126.17) | (34,−3) | **2857** |
| 4 | player 2 | (17212.43, −311.97, −1130.17) | (34,−3) | **2857** |

Check: `x=34 -> a=68`; `z=-3 -> b=7`; `s=75`; `75*76/2 = 2850; +7 = 2857`.

**Why it never stops** — `ecs/BossaECS.Framework.Systems/AddToIdComponentToEntityMapS.cs`.
The filter at `:28` is `And(_idComponents.Has, Not(_inMapComponents.Has))`. Two
of the three duplicate-key branches mark the entity (`:45`, `:56`) so it drops
out of the filter. The third does not:

```csharp
else
{
    WALogger.Error<...>($"Attempting to add existing id that points to another entity: {...}, entityIndex: {entityIndex}.");
}
```

`:62-64` — no marker, no removal. The loser stays in the filter and is
re-evaluated next tick, forever. Driver is `UpdateWeatherCellCoordsMapS`, the
last entry of the client FixedUpdate tree (`ecs_config.json:14`).

**Citation caveat:** `WASystems.dll` is not in `WAReborn-decompiled/`, and the
publicised copy has every body as `throw null`. Bodies quoted here were
decompiled from the shipped assembly with `ilspycmd` against
`UnityClient@Windows_Data/Managed/WASystems.dll`.

### Two corrections to `docs/research/findings-weather.md:125-144`
* The "**all entities default to X=0/Z=0**, Cantor id 0" explanation is
  **stale**. Since `e947b51` the server seeds real Haven coordinates and the id
  moved `0 -> 2857`. The mechanism was never "defaults to zero" — it is
  **co-location**. A 60 m island fits inside a 500 m cell with 440 m to spare,
  so giving entities their true positions changed nothing.
* The "`[Error]`, not a plain line" worry is a **non-change** — the 08-07
  baselines carry the same `[Error : Unity Log]` prefix. It was always
  `WALogLevel.Error -> Debug.LogError` (`acs/WAUnityLogger.cs:104`). It does
  matter for suppression, see §5.
* Still **correct**: not an exception, aborts nothing. Ten
  `NullReferenceException` lines all session against 60,661 weather lines.

## 2. IS IT OURS? — ENTIRELY

The real game put `WeatherCellState` **only** on dedicated weather-cell
entities, laid out by `WeatherCellGenesisS` on a lattice spaced **exactly one
cell apart** — injective by construction, collision impossible. Its
`RemoveExistingWeatherCellEntities()` drops anything that
`Contains<WeatherCellState>()`, which proves the component was a *"I am a
weather cell"* marker.

We do this instead (`ComponentsSerializer.cs:563-567`):

```csharp
else if (componentId == 1139)
{
    WeatherCellState.Data wcData = new WeatherCellState.Data(
        new WeatherCellStateData(1f, new Vector3f(0f, 0f, 0f)));
    obj = wcData;
}
```

And the client asks for it on **everything** —
`[8065 Blueprint, 190602 TransformState, 1269 RadialStormState, 1139 WeatherCellState]`
arrives for island, both players, ship and tree.

**The architectural seam:** in SpatialOS a `ComponentInterest` means "tell me if
the entity has these". A real deployment would answer those four with three.
**Our server has no concept of "this entity does not have that component"** and
invents one. `1269` is fabricated the same way.

**Not-sending is not a one-line deletion.** Every interest call site passes
`failOnComponentInitError: true`, and one unseeded id drops the whole batch — so
deleting the branch would take `190602 TransformState` with it. It needs (i) an
explicit *known-absent* set that `InitAndSerialize` reports distinctly from
"unhandled id", and (ii) `SendAddComponentOp` skipping those without failing the
batch. **The client half is safe:** `GlobalWeather.GetCellSampleAt` already
returns a default on a map miss (`acs/Assets.Visualizers.Weather/GlobalWeather.cs:56-68`).

## 3. DID TONIGHT MAKE IT WORSE? — YES, IT SLIGHTLY MORE THAN DOUBLED

One line per losing entity per `FixedUpdate` (50 Hz). Confirmed to within 1%
across four real logs:

| session (UTC) | entities with 1139 | losers | errors | window | errors/s | per loser | id |
|---|---|---|---|---|---|---|---|
| 08-07 22:49 | 2 | 1 | 10,280 | 207 s | 49.7 | 49.7 | 0 |
| 08-07 20:11 | 3 | 2 | 7,052 | 80 s | 88.2 | 44.1 | 0 |
| **08-08 19:53** | **5** | **4** | **60,661** | 291 s | **198.0** | 49.5 | **2857** |

Arrival times name the culprits: `entityIndex 1` at 19:53:19 (player 1),
**`2` at 19:53:21 (ship-haven)**, **`3` at 19:53:22 (tree-haven)**, `4` at
19:53:59 (player 2).

**88 -> 198 lines/s; ~127 -> ~286 KB/s.** Every entity ever added to the world
costs another ~49.5 lines/s permanently. That scales with the content roadmap,
which is the real reason to fix it.

## 4. WHAT IT COSTS — UGLY LOG, NOT THE STALL

One error = 15 lines (message + **14 frames**), ~1,445 B.
`60,661 x 15 = 909,915` of 916,717 lines = **99.3% of the file**, and >99% of the
bytes. The frames are **not Unity's**: `WAUnityLogger.cs:27` does
`GetStackTrace(3)` -> `new StackTrace(3).ToString()` **unconditionally, before
any filter**, and `LogToLocal` concatenates it into the message text before
calling `Debug.LogError`.

### The finding that settles it

`coresdk-hermit.txt`, our own P/Invoke trace:

```
182,493  Connection_GetFlag
121,656  Connection_SendLogMessage      <- 2 x 60,661
 25,142  Connection_SendComponentUpdate
```

Log traffic outnumbers gameplay traffic **4.8 : 1** at the boundary — and then:

```cpp
void __cdecl WorkerProtocol_Connection_SendLogMessage(Connection* connection, LogMessage* log_message) {
    hook("WorkerProtocol_Connection_SendLogMessage");
    // TODO: Add method SendLogMessage to connection and call it here
}
```

`WorldsAdriftRebornCoreSdk/Exports.cpp:149-152` — **a no-op we wrote ourselves.
Not one byte reaches the wire.** The analogy to the server's 1,207 lines/s ENet
incident breaks at exactly the point that made that one lethal. Residual cost:
`hook()` -> `Logger::Debug` does four flushed writes per error (~750/s), and
there is **no separate ENet thread** in the CoreSdk, so this is straight-line
main-thread time rather than lock contention.

Measured (.NET 8 Release, 14 frames, 20k iterations): **12.7 µs per
`StackTrace(3).ToString()`**; the real one is ~1,400 chars, and Mono is typically
2-5x, so **30-100 µs**. At 198/s with two captures each: **~10 ms/s** of stack
traces, 286 KB/s in ~2,970 flushed writes/s, ~1.1 MB/s of garbage.
**≈1-3% of the main thread; an honest ceiling of 5-10% under Wine I/O.**

**Verdict: this is not what made players wait.** Positive evidence — each error
is one tick per loser, so the error rate *is* a tick-rate meter, and it reads
**49.5 Hz against a 50 Hz target**, p5 = 49.0 Hz, with no second where the ECS
fell behind.

**Caveat not papered over:** FixedUpdate rate is a weak frame-rate proxy (Unity
catches up within `maximumDeltaTime` 0.333 s), so it only bounds the client above
~3 fps. No per-frame marker exists. A future session should log `Time.deltaTime`
percentiles.

### Two things for the latency investigation instead

1. **A 15-second total ECS gap, 19:56:57 -> 19:57:12 — the only gap in the
   session, and it is a RECONNECT.** `LobbySystem BossaNet User/Screen Name:
   Hermit` appears **twice** (19:53:16 and 19:57:11), the full
   `WorkerConfiguration` dump is reprinted, and `ORIGIN heartbeat` through the
   gap reads `player=<none yet>`. That alone is fifteen seconds of "delay in
   seeing each other's actions".
2. Duplicate-entity errors around player 2, six times each: `Trying to add the
   entity '4' (with prefab 'Travelle...`, `Attempting to checkout entity that is
   already...`, `InvalidOperationException: Component WorldData added to entity
   5, but it already exists`. Plus the ship's 26-component batch dropped
   wholesale because `8062` has no seed.

## 5. OPTIONS

**5.1 Make the ids unique — REJECT.** Not available to us: the coords are
computed **on the client** from the transform, floored at 500 m. Distinct ids
need >500 m separation and they are on a 60 m island by design. Moving to real
Haven coordinates already proved it (`0 -> 2857`, collision unchanged).
*Corollary:* parenting ship and tree to the island shrinks their `LocalPosition`
into cell (0,0) — colliding with each other instead.

**5.2 Stop seeding 1139 — correct, but not tonight.** Deletes the fiction and
generalises to `1269`. Needs the two-part change in §2, touching
`InitAndSerialize`'s error contract and the all-or-nothing rule — the exact seam
that gained two entity types today. *Unverified:* whether `SpatialTranslator`
materialises an entity answered with 3 of 4 components.

**5.3 Suppress the log — and the shipped fix could never have caught this.** Two
independent reasons:
1. `QuietenOrdinaryLogStackTraces` is scoped to `LogType.Log` by design; these
   are `LogType.Error`.
2. **The `LogType.Error` equivalent would not work either.**
   `SetStackTraceLogType` controls the trace *Unity* attaches; these frames are
   **message text**, concatenated by `WAUnityLogger.LogToLocal`. **Proof from
   tonight's log:** line 17 is `[WAReborn] stack traces disabled for ordinary
   Debug.Log lines` (the patch applied), line 98 is a `LogType.Log` line, and
   line 99 is *still* `   at Improbable.Bootstrap.Start()`. **The frames survived
   the fix meant to remove them.** The shipped fix therefore bought less than its
   comment claims, and its "1,014,755 frames, ~200 per line" figure is worth
   re-measuring.

**5.4 RECOMMENDED — mark the losing entity.** Harmony-patch
`AddToIdComponentToEntityMapS.Execute`'s third branch to add
`InEntityMapLocalComponent<TComponent>` exactly as the other two branches do. The
entity leaves the filter; **60,661 lines become 4.** Verified safe:
`RemoveFromIdComponentToEntityMapS.cs:40-50` already anticipates a marked
non-owner — it removes only the marker and **does not evict the winner's map
entry**, at the cost of one `Warn` per entity on despawn. That settles the "real
invariant trade-off ... unverified" caveat `findings-weather.md` attached to this
option: the invariant holds. Trade-offs: patching a third-party **generic**
system likely needs a manual `Harmony.Patch` on the closed generic rather than
`CreateAndPatchAll` (**unverified**), and it fixes the symptom, not the fiction.

**Recommendation: 5.4 now, 5.2 next. Do not do 5.1; do not bother with 5.3
alone.**

## 6. NOT VERIFIED

1. Absolute CPU cost — 12.7 µs is .NET 8 here; the Mono factor, Unity's native
   walk and Wine I/O are estimates. "1-3%, ceiling 5-10%" is inference, not a
   profile.
2. Frame rate — never measured; §4 bounds the client above ~3 fps and no better.
3. Client tolerance of a missing `1139`.
4. Harmony reach into a closed-generic ECS system (§5.4).
5. Which two paths make the two `SendLogMessage` calls. The count is measured,
   the pair inferred. Immaterial — the destination is a no-op. **Latent upstream
   bug worth noting:** `LogInternalErrors`' re-entry guard is
   `logMsg.StartsWith("..")`, but `LogToLocal` emits `"[{utc}]..{msg}"` — the
   timestamp comes first, so the guard never matches its own output.
6. The 15-second reconnect's cause — identified, not diagnosed.
7. Whether client ECS indices 0-4 equal server entity ids 0-4. Creation order,
   count and arrival timestamps all align; not proven.

## REPRODUCING

`data/weather-storm-analysis.sh` reproduces every number here from a client log;
`data/cantor.py` encodes/decodes the pairing and prints the five-entity table.
