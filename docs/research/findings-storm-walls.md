# STORM WALLS AND WIND WALLS AS A SHIP-FACING SYSTEM

**2026-08-20. Branch `research/storm-walls`, off `research/storm-sky` @ `83d461f`. Research only — no server change, no client mod, production untouched.**

Companion to `findings-storm-sky.md`, which established the *rendering* half and the `1204` seam. This document establishes the *physics* half: what a wall does to a ship, whether the maintainer's weight memory is real, where damage authority sat, and what we can actually reach.

---

## 0. THE ANSWER IN EIGHT LINES

1. **The maintainer's weight memory is REAL, and I found the line.** `WindPhysicsVisualizer.ApplyDrag` scales all wall wind by `1 - Clamp01(mass/4000)*0.75`. A >=4000 kg ship is pushed **4x less** than a weightless one. The wiki says the same thing in prose. **But it is a soft 4:1 ramp, not a threshold** (§2.4).
2. **There is no velocity threshold and no weight threshold to "pass" a wall.** No gate, no barrier, no pass/fail test exists anywhere in the wall code. Crossing is a continuous tug-of-war between engine thrust and wind drag (§2.5, §3).
3. **Three separate forces**, all `ForceMode.Force`: wall **wind drag** (mass-cancelling, but mass-attenuated by the 4000 kg ramp), **gusts** (impulse at a point → shoves *and* spins), and a **yaw torque** that turns your bow to run parallel with the wall (§2).
4. **The physics radius is 400 m, not the 800 m visual radius.** `WallData.EffectiveDist = 400`, full strength inside 200 m. You see a wall from 800 m and only start being pushed at 400 m (§2.1).
5. **Ship damage: the client is a SENSOR, not a damage model.** Three writers report *exposure* to the server — sails report wall wind (5129), wings/engines report sandstorm intensity (1256), the hull reports "inside a storm" (1224). Every damage decision lived in Bossa's server. We have none of it (§4).
6. **Wind walls are a genuinely different, and much cheaper, thing.** They get **no yaw torque at all** (the torque key set has no `windRift` prefix), their gusts blow straight **down**, they render semi-transparent through a *separate* shader path, and they trigger no storm renderer, no debris and no lightning (§3).
7. **Serving `1204` alone gives the full visual wall and the complete ambient-lightning show, and ZERO ship physics** — for two independent reasons: `1229` is unserved so the gust/torque tables are empty, *and* `WallTorquePhysicsVisualizer` is `UnityWorker`-only, so it is not even on our hulls (§5).
8. **Subdivision: we do not need it.** `WallData.DistanceSqr` is distance-to-line-segment and `Add()` merges segments into their axial extent, so **one entity per wall** reproduces retail's distance field *exactly*. 44 entities, not thousands (§6).

---

## 1. THE THREE FORCE PATHS, AND WHERE THEY ATTACH

All three live on the **ship hull** and all three are added **only** in the `UnityWorker` branch of `acs/ShipPreprocessor.cs:48-73`:

| line | component | what it does with walls |
|---|---|---|
| `:54` | `WindPhysicsVisualizer` | wall **wind drag** on the hull |
| `:55` | `WallTorquePhysicsVisualizer` | drives **gusts** + **yaw torque** |

`WallTorquePhysicsVisualizer` (`acs/WallTorquePhysicsVisualizer.cs`, 44 lines, read in full) is the whole driver:

```csharp
protected void FixedUpdate() {
    if (_cachedRigidbody != null && !_cachedRigidbody.isKinematic) {
        WeatherWalls.GetSmallestWallsSqrDistancesAndForwardDirAt(pos, _wallDistanceQueryAux, _wallForwardsQueryAux);
        _gust.DoGustFixedUpdate(_cachedRigidbody, this, _wallDistanceQueryAux);
        _torque.DoConstantTorqueFixedUpdate(_cachedRigidbody, _wallDistanceQueryAux, _wallForwardsQueryAux);
    }
}
```

**PROVED, and load-bearing:** it has **no `[Require]`** and no `[WorkerType]` attribute — its only gate is the `ShipPreprocessor` switch. It needs a non-kinematic `Rigidbody` and nothing else. So it is not blocked by any component we fail to serve; it is blocked by **not being on the prefab on a client build**.

---

## 2. THE FORCE MODEL, RECOVERED

### 2.1 The falloff — 400 m, in two linear zones

`acs/WallData.cs:237-248`, the single function every force path multiplies through:

```csharp
public static float GetIntensityAt(float sqrDist) {
    if (sqrDist > 160000f) return 0f;      // > 400 m
    if (sqrDist < 40000f)  return 1f;      // < 200 m: FULL strength
    return 1f - (Mathf.Sqrt(sqrDist) - 200f) / 200f;
}
```

**RECOVERED constants** (`WallData.cs:24-38`, compile-time `const`, so decompile is authoritative here — no serialized override exists):

| constant | value |
|---|---|
| `DistForMaxStrength` | 200 m |
| `EffectiveDist` | **400 m** |
| `LightningStrikeDistance` | 300 m |
| `WorldMinY` / `WorldMaxY` | -1000 / 800 |

**This is a correction to the mental model `findings-storm-sky.md` left behind.** That document's 800 m (`StormRiftDist`, RECOVERED off `level0`) is the **visual** influence radius used by `WeatherTextureGenerator`. The **physics** radius is a completely separate, hard-coded 400 m. Distance is always measured in **XZ only** (`MathUtils.Vector3toXZ`) — walls are infinitely tall for force purposes, and y only matters to the cloud renderer's `_StormHeight ~ 3500` ceiling.

| you are | at | what happens |
|---|---|---|
| 800 m out | `g` starts rising | clouds thicken, you *see* the wall |
| 578 m out | | rain begins |
| **400 m out** | `intensity` leaves 0 | **forces begin** |
| 367 m out | `storm > 0.75` | opaque storm renderer, debris, audio |
| 300 m out | | hull reports `InsideStorm = true` → lightning eligible |
| **200 m and closer** | `intensity == 1` | **full force, and it does not get worse** |

### 2.2 Gusts — an impulse at a point, so they shove *and* spin

`acs/WallGustBehaviour.cs`. Per wall type, two independent timers (a "small" and a "big" gust). When one expires and its strength is non-zero:

```
magnitude = GetIntensityAt(sqrDist) * data.Strength          // :100-108
direction = GetGustForceUnit(type)                            // :110-122
cooldown  = Random.Range(data.MinTime, data.MaxTime)
```

The force is then applied by `ApplyGustRoutine` (`:73-98`) as a **triangular ramp over a hard-coded 0.5 s** — rising to a peak at 0.25 s, back to zero at 0.5 s, applied via `AddForceAtPositionSafe` at a point that is captured in world space, converted to the body's local frame, and re-transformed every tick so it **tracks the hull as it rotates**. Because it is `AddForceAtPosition`, a gust delivers **both linear and angular impulse** — this is the wiki's *"can even turn you completely around."*

`GetGustForceUnit` (`:110-122`) — **PROVED, and it is the crispest wind-wall finding in this document:**

```csharp
case StormRift: case SandStorm:
    return Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.right;  // random HORIZONTAL
case WindRift:
    return Vector3.down;                                                              // straight DOWN
default:
    return Vector3.zero;                                                              // Typhon, IceStorm, WorldEndWall
```

The wiki, independently: wind walls *"contain fairly strong winds gusting from all different directions **(Like down)**"*. Code and wiki agree exactly. **`WorldEndWall` reads its six gust keys off the wire and then produces `Vector3.zero`** — a shipped no-op.

### 2.3 The yaw torque — the wall turns your bow to run alongside it

`acs/WallConstantTorqueBehaviour.cs:54-99`. Per wall type, a slowly-wandering target torque, re-randomised every `Random.Range(minChangeTime, maxChangeTime)`, eased by `MathUtils.TimeLerp(..., lerpFactor)`, then:

```csharp
float dot   = Vector3.Dot(wallForward, shipForwardFlattened);
float num   = value.CalculateDotFactor(dot);          // 1 when misaligned, 0 when aligned
float num2  = _curTorques[key] * intensityAt * num;
Vector3 rhs = Quaternion.AngleAxis(-90f, Vector3.up) * wallForward;
bool flip   = Vector3.Dot(forward, rhs) < 0f;
rb.AddTorqueSafe(new Vector3(0f, flip ? -num2 : num2, 0f));   // YAW only
```

`CalculateDotFactor` (`:28-39`) returns `1` below `dampeningZoneStart`, `0` above `dampeningZoneEnd`, and an inverse-lerp between. Both are **dot products in [-1, 1]**, clamped on ingest (`:108, :117`) — they are alignment thresholds, **not distances**.

**Read plainly: the torque is strongest when your bow points across the wall and falls to zero as you come parallel to it.** It is a yaw-only, self-cancelling aligning torque that tries to make you fly *along* the wall instead of *through* it. That, not any barrier, is the mechanical reason crossing a wall is hard. The `flip` term picks the shorter way round.

Torque is applied with the default `ForceMode.Force` (`acs/RigidbodySafe.cs:41`), so **angular acceleration = torque / inertia tensor**: a big, heavy, spread-out hull resists it more. Emergent, not coded. `RigidbodySafe` is a NaN/overflow guard only (`sqrMagnitude < 1e24`) — it does no clamping that matters.

### 2.4 The wall wind — and THE WEIGHT MECHANIC

`acs/Assets.Visualizers.Weather/WindPhysicsVisualizer.cs`. This is the largest and most continuous of the three, and it is where the maintainer's memory lives.

`WallData.GetWindUnscaled` (`WallData.cs:207-230`) gives a per-type wind vector, which `WeatherWalls.GetWallWindAt` scales by `intensity`:

| type | wind |
|---|---|
| **WindRift** | horizontal component pointing **radially away from the wall's centreline**, x `WindRiftHorizontalWindMultiplier`; plus a constant vertical `WindRiftVerticalWindMultiplier` |
| StormRift | `Forward x StormWallWindMultiplier` (wire key is `stormRiftWindMultiplier`) |
| SandStorm | `Forward x SandstormWindMultiplier` |
| WorldEndWall | `Forward x WorldEndWallWindMultiplier` |
| IceStorm | `Vector3.down` (hard-coded, no multiplier) |
| Typhon | `Forward` (hard-coded, no multiplier) |

Then, `:93-171`:

```csharp
private Vector3 ApplyDrag(Vector3d pos, Rigidbody rb) {
    float num = Mathf.Clamp01(rb.mass / 4000f) * 0.75f;
    return ApplyWindDrag(pos, rb, 1f - num);            // <-- THE WEIGHT MECHANIC
}

// inside ApplyWindDrag:
Vector3 wind  = GetWind(pos) * windMultiplier;
Vector3 force = rb.mass * GetDrag(wind - rb.velocity, rb.mass, Time.deltaTime);
rb.AddForceSafe(force);                                 // ForceMode.Force
```

**The answer to objective 1, stated exactly:**

> `windMultiplier = 1 - Clamp01(mass / 4000) * 0.75`

- mass 0 kg → x1.00
- mass 1000 kg → x0.8125
- mass 2000 kg → x0.625
- mass 4000 kg → x0.25
- mass 40,000 kg → x0.25 (**saturated** — beyond 4000 kg, extra weight buys you nothing)

Note the force is `rb.mass * GetDrag(...)` and then applied as `ForceMode.Force`, so the mass in the numerator **cancels** against `a = F/m`. The *only* mass dependence in the wall wind is the deliberate `1 - Clamp01(mass/4000)*0.75` attenuation. Bossa wrote it in on purpose.

The wiki says the same thing in words (`pages/Weather.wikitext`, Storm Walls, verbatim):

> *"Also, heavier is better. The more your ship weighs, the less the wind will be able to toss it around, just make sure your Atlas Core is good enough to handle the weight."*

**WIKI and PROVED code agree.** The maintainer remembers a real mechanic. It is a 4:1 soft ramp saturating at 4000 kg, not a pass/fail weight gate.

**RECOVERED shipped tuning** for the drag curve — read off the serialized `ShipConfig` ScriptableObject in `resources.assets` (`Resources.Load<ShipConfiguration>("Configs/ShipConfig")`, `ShipConfiguration.cs:82`), method in §8. **The decompiled field defaults are wrong again, exactly as `findings-storm-sky.md` §7 warned:**

| field | decompiled default | **shipped** |
|---|---|---|
| `AirResistanceExponent` | 2.0 | **2.5** |
| `AirResistanceCoefficient` | 0.01 | **0.007** |

Confidence in the field mapping: the six neighbouring floats in the same serialized run (`SoftenCollisionsMaxDepenetrationVelocity` 1, `...MaxAngularVelocityDegrees` 10, `...Duration` 5, `AirBrakeMultiplier` 1, `ShipThrustMultiplier` 1, `MaxWingPowerSpeed` 10) **all match their declared defaults exactly**, which pins the alignment; only the two that matter differ.

WARNING: `ShipConfiguration` implements `RemoteConfigurationUpdater.IConfig` and registers itself as `"shipconfig"` (`:85`), so retail could override these at runtime. The 2.5 / 0.007 pair is what the client **ships with**, which is the best available answer.

### 2.5 Velocity — real dependence, no threshold

`GetDrag` (`:72-91`) operates on **relative wind**, `wind - rb.velocity`:

```csharp
float number = Mathf.Pow(magnitude, airResistanceExponent) * airResistanceCoefficient;
number = number.Clamp(0f, magnitude / deltaTime);
```

Two consequences, both PROVED by reading the function:

1. **Force grows superlinearly with your speed relative to the wind** — exponent 2.5. Charging a wall head-on at speed produces a *much* larger opposing force than drifting into it. This is genuine velocity dependence.
2. **`Clamp(0, magnitude/deltaTime)` means the drag can never overshoot the wind velocity.** A wall's wind asymptotically advects you toward its own velocity; it can never fling you faster than itself.

**There is no velocity threshold to cross a wall.** No comparison of speed against a constant exists in `WallData`, `WallGustBehaviour`, `WallConstantTorqueBehaviour` or the wall branch of `WindPhysicsVisualizer`. The only velocity comparison in that file is an unrelated sleep/docking early-out (`:55`, `:61`).

What *is* true is that crossing is an equilibrium: you cross when thrust beats drag. The wiki describes exactly this and never mentions a threshold — *"All that is required to pass through the wind walls is an **engine powerful enough** to pull you through the strong winds."*

---

## 3. WIND WALLS, DISTINCTLY — the largest group and the cheapest

20 of the 44 imported segments are Wind Rifts (103 km of the 276 km total). They differ from Storm Rifts on **every** axis:

| | Wind Rift (type 0) | Storm Rift (type 1) |
|---|---|---|
| **yaw torque** | **NONE** — `windRift` is absent from the torque prefix list (`GlobalWeatherDataVisualizer.cs:51-53`, prefixes are `stormRift`, `sandstorm`, `worldEndWall` only) | yes, aligning yaw torque |
| **gust direction** | straight **down** (`Vector3.down`) | random **horizontal** |
| **wind field** | radial, pushing you **away from the centreline**, plus a constant vertical term | along the wall's `Forward` |
| **texture channel** | `wallColor.r` | `wallColor.g` |
| **opaque storm renderer** | **no** — `CmdBufBaseStorm`, `InstancedStormDebris3D` and the `CmdBufClouds.enabled = false` swap are all keyed on `.g > 0.75` | **yes** |
| **rain** | **suppresses** it: `num *= Clamp01(1 - WindWallValue*2)` (`WeatherEffectHandler.cs:106`) | drives it |
| **ambient bolts** | **no** — `LightningVisualInstancesManager` draws only from `_stormRifts` | yes, free |
| **lightning eligibility** | **no** — `WeatherWalls.IsInsideStorm` scans `_stormRifts` only (`WeatherWalls.cs:139-151`) | yes, within 300 m |
| **see-through** | yes | no |

**The renderer is separate and semi-transparent.** `CmdBufClouds` carries a dedicated wind-wall shader path — `_WindWallsColor` (a time-of-day gradient, `:894`), `_WallOpac`, `_WallScale`, `_WallFThickness`, `_WallThickness`, `_WindWHeight` (`:952-957`). Decompiled defaults: opacity **0.6**, scale 0.007 (x0.3 on upload), both thicknesses 1.3, height **3500** — *the same ceiling as the storm wall.* The wiki matches: *"They are also the only weather walls that you can see through"* and *"cascading clouds that resemble a waterfall."*

WARNING: those six are **decompiled defaults, NOT recovered** — `CmdBufClouds`'s serialized `level0` layout is gradient-heavy and was not parsed here (same open item as `findings-storm-sky.md` §8). Given that two of two `ShipConfiguration` values under test turned out wrong, **treat them as unverified.**

**Cost verdict: wind walls are much cheaper to serve than storm walls.** Serving 20 Wind Rifts adds a translucent curtain, a wind field and a rain suppressor. Serving 11 Storm Rifts additionally swaps the entire cloud renderer for the opaque storm renderer inside 367 m, enables two `InstancedStormDebris3D` emitters, and starts a continuous ambient-bolt spawner. If a phased rollout is wanted, **wind walls first** is both the cheaper and the larger-coverage half.

---

## 4. SHIP DAMAGE — the client is a sensor; the model was Bossa's server

### 4.1 The three exposure sensors, and their exact wiki correspondence

This is the strongest single result in this document: three client writers, three wiki damage claims, exact one-to-one correspondence.

| sensor (all `UnityWorker`) | attached to | writes | wiki says |
|---|---|---|---|
| `WindReceiverBehaviour` | **sails** (`SailPreprocessor.cs:28`) | `5129 WindReceiverState.Wind` <- `WeatherWalls.GetWallWindAt`, every 1 s | wind walls *"can only damage the **sails** on your ship"* |
| `SandStormAffecteeBehaviour` | **wings + engines** (`WingPreprocessor.cs:15`, `ShipEnginePreprocessor.cs:21`) | `1256 SandStormAffecteePositionalState.SandstormIntensity` <- `WeatherWalls.GetIntensityAt(..., SandStorm)`, every `LateUpdate` | sandwalls *"deal damage over time to all your ship parts like the **wings and engine**"* |
| `LightningAttractorVisualizer` | **hull** (`ShipPreprocessor.cs:58`) | `1224 LightningAttractorPositionalState.InsideStorm` <- `WeatherWalls.IsInsideStorm` (300 m), every `Update` | storm walls *"contain lightning which can hit your ship and damage parts"* |

Every one of them **only reports a scalar or a bool upward**. Not one computes or applies a health delta. The damage arithmetic consumed these three components on Bossa's simulation workers and does not exist in any shipped client byte.

### 4.2 The strike loop, and what would have to exist

`LightningAttractorVisualizer` also *receives*: `1223 LightningAttractorTimerState.StrikeWithLightning` (`:18, :32`). On that event it picks a random child `LightningStrikableVisualizer`, a random collider on it, and calls `LightningGeneratorBehaviour.Strike` → `TriggerLightningStrikeHitEvent(entityId)` on `1222 LightningGeneratorState`, plus a visual bolt.

**The loop is: client reports `InsideStorm` → server decides and fires `StrikeWithLightning` → client picks a part and reports which entity was hit → server applies damage.** Both decision points are server-side and both are missing here. The only client-side consumer of the resulting `HitByLightning` on `1225 LightningStrikableState` is **`Play_Lightning_Strike_Impact`, an audio event** — `1225`'s payload struct is field-less.

### 4.3 What the client *does* own — and it is not storm damage

- **Part detachment is signalled, never performed, client-side.** `1235 DetachFromParentWhenUnderHealthThresholdState` carries one float, `field1_health_threshold`, and one event, `DetachFromTooMuchDamage`. Its only client handlers spawn a `PartBreak` particle (`JointDamageVisualizer.cs:17-25`) and a break sound. The re-parenting itself is done by whoever owns the health.
- **No `ConfigurableJoint` break path for ship parts.** `breakForce`/`breakTorque` in `acs/` appear only in the generic `ConfigurableJointProxy` (defaults `+Infinity`) and in player grab/climb joints. Ship parenting is SpatialOS `TransformState`/`TransformHierarchyState`, not Unity joints.
- **`DeteriorateVisualiser` is a *sinking* model, not a damage model, and it is NOT weather-driven.** Its collision layer filter is literally `LayerMask.NameToLayer("Terrain")`; after a 60 s `_terrainCollisionGracePeriod` a wreck at rest on an island sinks kinematically into it. Zero occurrences of any weather symbol in the file.
- **The one damage input the client authors** is `1232 RigidbodyCollisionReporterState` — raw impact point, `relativeVelocity.sqrMagnitude`, physics materials and both masses, gated on `impulse.sqrMagnitude > 25` with a 0.5 s cooldown. **Kinematics only, no damage number.**

### 4.4 Correction to a premise in the brief

The brief states `1235` / `1225` / `4323` are "known-absent". Precisely: they are listed in **our** `ComponentAbsencePolicy.KnownAbsentComponentIds` — a deliberate decision by this server not to serve them. **They are not absent from the client.** All three exist in gencode and `component-map.tsv`, and `1235` and `1225` are actively read by shipped client code on every ship part (`ShipPartPreprocessor.cs:20, :22`). `4323 ContactFixedDamageState` is creature contact damage, not ship-related. The "no `DamageService` / `ApplyDamage` / `TakeDamage`" claim **survives re-testing with controls** (§9.9).

### 4.5 Honest sizing

A wall damage model is **not a small addition to walls; it is its own workstream**, and it is the fourth thing in the chain, not the second:

1. Serve `1204` → walls exist and render. *(no ships needed)*
2. Ships Stage B lands → hulls get the `UnityWorker` component list → wall **forces** begin.
3. Serve `1229` with invented tuning → gusts and torque begin.
4. **Only then** could damage be built — and it needs a health model this server has never had (`1450 ItemHealthNormalizedState`, `1016 ItemHealthState`, per-part HP, repair, and the `1235` detach arbitration), plus `1223` strike scheduling and `1222`/`1225` plumbing.

The understorm plan already says *"Do NOT stack damage (S4/S5) onto any of this."* That judgement is correct and this research reinforces it.

---

## 5. WHAT SERVING `1204` ALONE ACTUALLY DELIVERS

`1204 WallSegmentState { int wallType, int wallId, Vector3d orientation, float length }` (`WallSegmentStateData.cs`, field order confirmed). Prefab and `[Require]` surface are exactly as `findings-storm-sky.md` §2.2 established — one `[Require]`, clean prefab, not in `ComponentAbsencePolicy`, no client mod, no schema migration.

**YOU GET (no other component, no ship work):**
- the wall itself, rendered — opaque billowing storm cloud for `StormRift`/`SandStorm`, the translucent waterfall curtain for `WindRift`
- rain, storm debris, the sand storm renderer, the audio mix (`AmbienceSoundController` registration is automatic — `WeatherWalls.Register` does it at `:103`)
- **free ambient lightning** along every Storm Rift, with no server involvement
- visibility falloff / whiteout inside a Storm Rift
- `RespawnVisualizer` correctly refusing to treat inside-a-wall as a valid biome (`:494`)

**YOU DO NOT GET — and for two INDEPENDENT reasons, either of which alone is sufficient:**

1. **`1229 GlobalWallDataState` is unserved.** `_gustData` and `_torqueData` are empty dictionaries with no initializer, so both `DoGustFixedUpdate` and `DoConstantTorqueFixedUpdate` iterate nothing. All five wind multipliers default to `0f`, so `GetWallWindAt` returns a zero wind vector for every type except `IceStorm`/`Typhon` (hard-coded, and both have zero segments in the release map). **Result: zero gusts, zero torque, zero wind.**
2. **`WallTorquePhysicsVisualizer` and `WindPhysicsVisualizer` are `UnityWorker`-only** (`ShipPreprocessor.cs:54-55`). They are not on a client-built hull at all. Even a perfectly populated `1229` would do nothing until ships Stage B's Patch A adds the `UnityWorker` list to client hulls.

**This is a feature, not a defect, for a first phase.** A visual-only wall is a coherent, shippable world feature that cannot destabilise ship flight, because it applies no force. And point 2 means walls and ship physics are **cleanly decoupled**: serving `1204` today can never perturb flight, no matter what.

### 5.1 `1229` is a trap, and I can now say how deep

**The 50 retail tuning values are UNRECOVERABLE.** They were authored in Bossa's world editor / server config and delivered only as a runtime `Map<string,float>`. No copy survives in the client, StreamingAssets, asset bundles, the world-data dumps, or any snapshot on this machine (controls in §9.2). **Any `1229` we serve is 100% WAREBORN TUNING, invented from scratch.**

The key set (`GlobalWeatherDataVisualizer.cs`, RECOVERED):
- **5 wind multipliers**: `windRiftVerticalWindMultiplier`, `windRiftHorizontalWindMultiplier`, **`stormRiftWindMultiplier`** (note: the C# field is named `_stormWallWindMultiplier` — the wire key and the field name disagree, an easy trap), `sandstormWindMultiplier`, `worldEndWallWindMultiplier`
- **24 gust keys**: prefixes `windRift`/`stormRift`/`sandstorm`/`worldEndWall` x `GustBigStrength`, `GustBigMinTime`, `GustBigMaxTime`, `GustSmallStrength`, `GustSmallMinTime`, `GustSmallMaxTime`
- **21 torque keys**: prefixes `stormRift`/`sandstorm`/`worldEndWall` **only** x `TorqueLerpFactor`, `TorqueChangeMinTime`, `TorqueChangeMaxTime`, `MinTorque`, `MaxTorque`, `TorqueDampeningZoneStart`, `TorqueDampeningZoneEnd`

Failure modes differ and both matter:
- **gusts**: a missing key → `Debug.LogError` **and** all three of that triple set to `0f`, then `UpdateGustData` is called anyway. Noisy, but survivable.
- **torque**: a missing key → the **entire call is skipped**, so that wall type gets no `_torqueData` entry at all. Silent.

Bounds the clamps give us for free: `lerpFactor` in [0.1, 1]; `dampeningZoneStart` in [-1, 1]; `dampeningZoneEnd` in [dampeningZoneStart, 1] — both dampening values are **dot products**, so anyone inventing them must not write metres.

**Two shipped bugs to know before authoring values:**
1. `WallGustBehaviour.UpdateGustData:142-146` builds the `Big` gust with `MinTime = gustSmallMinTime, MaxTime = gustSmallMaxTime`. **`*GustBigMinTime` and `*GustBigMaxTime` are read off the wire and then discarded.** Even a perfect retail dump would not change behaviour for those two keys. Do not spend effort tuning them.
2. The same method is wrapped in `if (!_gustData.ContainsKey(type))`, so every `UpdateGustData` after the first is a no-op — mitigated only because `GlobalWeatherDataVisualizer` calls `ClearGustData()` first (`:45`).

---

## 6. SUBDIVISION — ANSWERED: one entity per wall

`findings-storm-sky.md` §2.6 left this open, guessing at "per-segment granularity near the 800 m influence radius." **That guess was wrong, and the real answer is much better for us.**

`WallData.DistanceSqr` (`:185-188`) is `MathUtils.DistanceToLineSegmentSquared(point, _p1xz, _p2xz)` — the distance to **one line segment**, `P1`->`P2`. And `WallData.Add` (`:140-158`) does not keep a list of independent segments for distance purposes; it **extends `P1`/`P2` to the axial extent** of every registered segment via `CheckIfFurther`. The `_segments` list exists only for refcounting the unregister path and for `DebugInfo`.

**Therefore the distance field produced by N collinear segments sharing a `wallId` is bit-for-bit the field produced by ONE segment spanning the same extent.** Subdivision has no effect on the physics or the texture. It was purely an interest-management device.

Two facts to get right when serving:

- **`length` is a HALF-length.** `WallData`'s constructor (`:111-120`) is `P1 = position - forward*Length`, `P2 = position + forward*Length`. A 5 km wall is one entity at the midpoint with `length = 2500`.
- **Retail's subdivision spacing was bounded by SpatialOS checkout radius**, not by 400 m or 800 m — a segment must be *checked out* before `OnEnable` can register it, so segments had to be no further apart than a player's checkout diameter, or the wall would pop in late. (INFERRED — no retail segment dump exists to measure. This is the defensible bound the brief asked for, not a measurement.)

**Our recommendation, which sidesteps the question entirely: send all 44 as whole walls, ungated by interest.** Our production interest radius is **120 m** (`findings-island-fauna.md:361`) — far below even the 400 m physics radius — so interest-gating wall segments would guarantee they register too late. 44 permanent entities is negligible next to the resource population we already stream. The imported geometry supports this directly:

| type | n | total length | longest |
|---|---|---|---|
| 0 Wind Rift | 20 | 103.2 km | 9.9 km |
| 1 Storm Rift | 11 | 53.4 km | 7.9 km |
| 3 Sand Storm | 12 | 83.9 km | 10.5 km |
| 5 World End | 1 | 36.0 km | 36.0 km |
| **all** | **44** | **276.5 km** | |

WARNING: **one real consequence of whole-wall serving, and it is not obvious.** `LightningVisualInstancesManager` spawns ambient bolts at `_fakeLightningPerSecondPerKilometer x TotalStormWallLength / 1000` per second, and `WeatherWalls.EvaluateLength` sums **every registered** Storm Rift. Serving all 11 unconditionally makes `TotalStormWallLength ~ 53.4 km` **permanently, for every client, everywhere in the world** — whereas retail's interest-gated segments meant only nearby wall counted. Bolts are frustum-culled (`:169-190`), so most are discarded, but the spawn *rate* is set before culling. **Measure the bolt spawn cost in a soak before accepting this**; if it bites, the mitigation is to subdivide Storm Rifts only, and interest-gate them at a radius >= 800 m.

---

## 7. THE ATLAS CLIFF — does serving walls perturb it?

**Serving `1204` alone: NO, provably, and for a structural reason rather than a lucky one.** The three force paths are all in `ShipPreprocessor`'s `UnityWorker` branch and are therefore **not present on our hulls at all**. A wall cannot add a newton to a rigidbody that has no component reading the wall. There is no path from `1204` to `AtlasMultiplier`, `TotalLift`, `_massVisualizer.totalMass` or `UpdateVertical`.

**When ships Stage B lands, YES — there is a coupling, and it is on the same line the brief warns about.**

```csharp
// WindPhysicsVisualizer.cs:37
private bool IsFloatingShip => _shipLift != null && !_shipLift.IsOverloaded;

// WindPhysicsVisualizer.cs:55  — the early-out
if ((_rigidbody != null && velocity.sqrMagnitude < sleepThreshold^2 && !IsFloatingShip) || isParented)
    return;
```

Wall wind is gated on `IsOverloaded`, the *exact* predicate that gates vertical flight. And `ShipLiftVisualizer` (`:12-18`):

```csharp
public float TotalLift  => (_state == null) ? 0f : (EndOfTheWorldConfig.Instance.AtlasMultiplier * _state.TotalLift);
public bool  IsOverloaded => _massVisualizer.totalMass > TotalLift;
```

**A correction worth recording.** `AtlasMultiplier` is `0.0`, so `TotalLift = 0 * 1,000,000 = 0` — **our 1258 seed of 1,000,000 kg does not survive the multiplication and is not what keeps us flying.** `IsOverloaded` reduces to `totalMass > 0`. `ShipControlsBehaviour.cs:276-279` reaches it via `GetComponent<ShipLiftVisualizer>()`, which returns the component **even when disabled**, so "the visualizer is inert" does not prevent the read. What actually keeps vertical flight working is that **`ParentingMassAdderVisualizer.totalMass` is 0**, because `1257` is known-absent and we run no mass model. `0 > 0` is false. (The overload string, verified verbatim at `ShipControlsBehaviour.cs:283`, is *"Ship weighs more than its atlas sky core can lift."*)

Three consequences, and they are the real risk register for walls-plus-ships:

1. **The safety margin is zero, not 1,000,000.** Anything that makes `totalMass` non-zero — serving `1257`, a hull mass model, a `ParentingMassAdderVisualizer` that starts resolving — flips `IsOverloaded` to `true` **for every ship simultaneously** and blocks all vertical input. This is pre-existing and is not caused by walls, but walls are the first feature that would make anyone want a real ship mass.
2. **`totalMass = 0` also pins the weight mechanic at its most vulnerable setting.** `ApplyDrag` uses `rb.mass` (the Unity rigidbody's mass, not `totalMass`), so §2.4 keys off whatever the hull's rigidbody actually weighs. If that is small or synthetic, our ships will be tossed around like the lightest possible retail ship. **The maintainer's "heavier is better" memory will not reproduce until ship mass is real.** That is a genuine gameplay-fidelity dependency, and worth saying plainly: *walls will feel wrong until ship mass is modelled.*
3. **`IsFloatingShip` is only read in the sleep early-out**, so a mis-evaluation there costs a stationary ship its wind, not a crash. Minor.

**Verdict: serving `1204` today does not perturb the atlas arithmetic and cannot.** Serving `1229` *also* cannot, on its own — the behaviours that read it are not on our hulls. The coupling arrives with ships Stage B, and when it does, wall wind and vertical flight share the `IsOverloaded` predicate.

---

## 8. METHOD, so the negatives can be trusted

- **`grep` is ugrep and the shell is fish.** A first sweep here returned "no matches found" for nine symbols at once because fish glob-expanded an unquoted `--include=*.cs`. Every subsequent sweep quoted it (`'--include=*.cs'`). Binary sweeps used `-a`; `WASystems.dll`/`SpatialTranslator.dll` store strings **UTF-16**, so ASCII `grep -a` on them is a guaranteed false zero — those two needed `strings -el`.
- **UnityPy venv recreated** (the previous session's scratchpad was gone, as predicted). `wallscan.py` resolves `MonoScript` names out of `globalgamemanagers.assets` by raw `m_Script` PPtr and parses `get_raw_data()` past the 32-byte header (`m_GameObject` PPtr 12 + `m_Enabled` 4 + `m_Script` PPtr 12 + `m_Name` len 4), then the aligned name. That is how `AirResistanceExponent = 2.5` / `AirResistanceCoefficient = 0.007` were RECOVERED.
- **The UnityPy scan cross-validates `findings-storm-sky.md` independently.** Same command over `resources.assets` returned `WallSegmentVisualizer` = **2** (the `_unityclient` and `_unityworker` prefabs, exactly as that document's §2.2 says) and `WeatherTextureGenerator` = **0** (it is on `level0`, as that document says). Positive control passed on someone else's result.
- **Provenance labels** used throughout: PROVED (read in the shipped bytes), RECOVERED (read off a serialized asset), INFERRED, WIKI, WAREBORN TUNING.

---

## 9. NEGATIVE RESULTS, EACH WITH ITS CONTROL

Preserved so the next agent does not re-spend the day.

1. **No weight threshold, no velocity threshold, no barrier, anywhere in the wall system.** `WallData.cs`, `WallGustBehaviour.cs`, `WallConstantTorqueBehaviour.cs`, `WallTorquePhysicsVisualizer.cs` and `WindPhysicsVisualizer.cs` were each read **in full, end to end** — not grepped. The only mass reference is `ApplyDrag`'s `mass/4000` ramp; the only velocity references are the relative-wind drag and an unrelated sleep/docking early-out. There is no `if (mass < X) reject`, no collider, no `OnTriggerEnter`, no pass/fail.
2. **The retail `1229` tuning values do not exist on this machine.** 0 hits for `GustBigStrength`, `TorqueDampeningZoneStart`, `windRiftVerticalWindMultiplier`, `stormRiftWindMultiplier` across `resources.assets`, `sharedassets0/1.assets`, `globalgamemanagers`, `globalgamemanagers.assets`, `level0`, `level1`. *Control, same command:* `WallGustBehaviour` found at `globalgamemanagers.assets` offsets 590660/590704, `GlobalWeatherDataVisualizer` at 623756/623808 — both `MonoScript` type records, not data blobs. The class has **zero serialized fields** (all five floats are `private static`), so no prefab can carry them. `clientGameDB.bytes` is 2064 bytes of high entropy; `StreamingAssets/lpbundle` is a compressed lightprobe bundle; no SpatialOS `.snapshot` exists on this machine.
3. **`wamap-islands.json` carries no wall tuning.** `Walls` records are exactly `{x1, z1, x2, z2, Type}` — matching `WorldEditorWallData.WallStoreData`'s field list, so the retail wall serialization format provably had no tuning payload.
4. **`1229` has never been exercised against our server.** 0 occurrences of `Missing key` / `Missing torque data` / `Missing big gust` in `output_log.txt` and `output_log.txt.before-0922`, i.e. `GlobalWeatherDataVisualizer.UpdateValues` has never fired.
5. **`WindRift` has no torque, by construction.** The torque prefix array (`GlobalWeatherDataVisualizer.cs:51-53`) contains `stormRift`, `sandstorm`, `worldEndWall` and not `windRift`. *Control:* the gust prefix array immediately above (`:46-49`) **does** contain `windRift`.
6. **`WorldEndWall` gusts are a shipped no-op.** Its six gust keys are read; `GetGustForceUnit` returns `Vector3.zero` for it.
7. **Deterioration is not weather-driven.** Zero occurrences of `Deteriorate`, `Detach`, `Health` or `Damage` in `WeatherEffectHandler.cs` or `WeatherWalls.cs`. *Control, same sweep:* `StormValue` -> 10 hits in `WeatherEffectHandler.cs`. The only weather<->damage link in the client is the single line `LightningAttractorVisualizer.cs:28`.
8. **No client code writes ship health or deterioration.** `DeteriorateStateWriter`, `ItemHealthStateWriter`, `ItemHealthNormalizedStateWriter`, `HealthStateWriter`, `ShipPartStateWriter`, `LightningStrikableStateWriter` -> **0 hits each** over `acs`. *Control, same sweep:* `DeteriorateStateReader` -> **3**. Only `DeteriorateFsimStateWriter` (4334) is written, on `UnityWorker`.
9. **No `DamageService` anywhere**, re-confirmed with controls. `grep -rn "DamageService"` over `acs`/`ecs`/`sdk-decomp`/`gencode` -> 0; *control* `ShipPreprocessor` -> found. `grep -ac` over `Assembly-CSharp.dll`, `Generated.Code.dll`, `BossaECS.dll`, `WAUtilities.dll` -> 0 in all four; *control same command* `DamageDealer` -> 1/4/0/0. `strings -el WASystems.dll | grep -iE "damage|health|deteriorat|detach|salvage"` -> **0**; *control same pipeline* `weather|island|entity` -> 9 hits. **WASystems is a world-bootstrap / weather-cell / island generator, not a damage worker.** `ApplyDamage` exists only as the protobuf field `PelletInfo.field5_apply_damage`; `TakeDamage` hits are all player-side.
10. **`WallAmbientEffect.cs` has zero references** outside its own declaration. *Control, same command:* `WallGustBehaviour` -> 5 hits across 3 files. Likely dead, but it derives from `AmbientEffect`, which may be instantiated by name — **not chased, and not needed for anything here.**
11. **`BarrierWall.cs` is NOT a weather wall.** It is the shrine dome (`GameDBLocalization`, dome runes, `_maxDomeRuneDistance`), `[WorkerType(UnityClient)]`. Ruled out. Likewise `WorldEdgePushback` is the world-boundary pushback (`NegateVelocityDistance = 300`, `PushbackDistance = 100`), keyed on `WorldBoundsDataVisualizer`, **not** on `WeatherWalls` — it is the only hard barrier in the game and it is the map edge, not a storm wall.

---

## 10. OPEN, HONESTLY UNESTABLISHED

- **The six wind-wall render constants** (`_windWallOpcaity` 0.6, `_windWallScale` 0.007, both thicknesses 1.3, `_windWallHeight` 3500) are **decompiled defaults, not recovered.** Two of two `ShipConfiguration` values tested this session were wrong. Readable with the §8 method; the `CmdBufClouds` `level0` layout is gradient-heavy, same open item as `findings-storm-sky.md` §8.
- **Retail's actual segment subdivision spacing.** Bounded by checkout radius (§6) but never measured; no retail segment dump exists.
- **The ambient-bolt spawn cost of whole-wall serving** (§6) is derived from the formula, not measured. It needs a soak.
- **`ShipConfiguration` is remotely overridable** (`RemoteConfigurationUpdater`, key `"shipconfig"`). 2.5 / 0.007 is what the client ships with; whether retail's live server overrode it is unknowable from here.
- **`WallInfoProvider`** is a dev-console debug provider (`DebugInfoController.cs:164`, command `"wall"`); not examined in detail, no gameplay role.
- **`worldsadrift.com/blog/stormy-skies/`** — still not fetched. It is the wiki's ref `:0` and the primary source for **both** wall types' damage rules. It is the single highest-value remaining source for §4, and it is ten minutes of work.

---

## 11. WHAT WAS NOT DONE

No server code changed. No client mod built or installed. Nothing pushed, nothing deployed. Production not touched, not even read. `isLightningActive` not written anywhere. No test, no schema change. Only `docs/research/findings-storm-walls.md` added and a pointer appended to `docs/research/findings-storm-sky.md` §6.
