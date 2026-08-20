# LEAD D — THE STORM SKY: what darkens it, and can we reach it

**2026-08-20. Branch `research/storm-sky`, off `feat/understorm-s1` @ `1c6b552`. Research only.**

## 0. THE ANSWER IN FIVE LINES

1. The shipped `weatherTex` producer is **`WeatherTextureGenerator`** (1 instance on the `level0` boot scene). `WeatherTexGenCpu`, `AuthoredWeatherTest`, `DrawnWallTexturesTest` are **not instantiated anywhere in the shipped game**.
2. But the sky **does not read `weatherTex` at all.** The cloud renderer samples only **`wallInfoTex`**. `_WeatherTex` appears **0 times** in the shipped shader bundle; `_WallInfoTex` appears 3 (same file, same method — positive control).
3. The overcast channel is `wallInfoTex.g`, written **purely from authored wall geometry** (`StormRift` / `SandStorm` segments), with no `GlobalWeather.GetWeatherAt` and therefore **no 1139**.
4. **YES: the storm overcast is reachable server-side without 1139.** The seam is **`1204 WallSegmentState`** on the shipped **`WallSegment`** prefab — **exactly one `[Require]`**, plus a transform stack we already serve.
5. **But it does not give an understorm telegraph, and retail probably never had one.** 1204 gives a *storm wall* — a line, permanent, biome-scale. No wiki or client source says an understorm darkened anything (§5).

## 1. THE FULL CHAIN, PRODUCER → RENDERED SKY

### 1.1 Producer — `WeatherTextureGenerator` (PROVED it is the shipped one)

`acs/WeatherTextureGenerator.cs`, 328 lines. `Awake` finds `CmdBufClouds`, allocates two 128×128 ARGB32 `Texture2D`s, publishes them as `GlobalWeatherTextures.wallInfoTex` / `.weatherTex` (`:131`, `:142`). `Update` regenerates **one of 32×32 = 1024 chunks per frame** (`:110`), re-centring on the local player each full sweep (`:159-176`). `mapSize = 12000` (`GlobalWeatherTextures.cs:13`) → one texel ≈ 93.75 m.

**Instantiation, PROVED by UnityPy over shipped `level0`** (method §7):

| MonoBehaviour | instances in `level0` |
|---|---|
| `WeatherTextureGenerator` | **1** |
| `CmdBufClouds` *(positive control)* | **1** |
| `WeatherEffectHandler` | 1 |
| `CmdBufBaseStorm` / `CmdBufSandStorm` / `CmdBufDissolverStorm` | 1 each |
| `SSPlanarRain`, `uSkyManager`, `LightningVisualInstancesManager` | 1 each |
| `CloudTransitionVisuals`, `CloudAlphaBlendManager`, `InstancedBlightDebris` | 1 each |
| `InstancedStormDebris3D` | 2 |
| **`WeatherTexGenCpu`** | **0** |
| **`AuthoredWeatherTest`** | **0** |
| **`DrawnWallTexturesTest`** | **0** |
| **`WeatherTexGen`** (`nimitz.Effects`, the one that sets `_WeatherTex`) | **0** |

Same scan over `level1` and `resources.assets`: 0 additional instances of any. The findings doc's suspicion was right — `WeatherTexGenCpu` is dead test code.

**RECOVERED shipped tuning**, read off the serialized `level0` MonoBehaviour (decompiled defaults are wrong):

| field | decompiled default | **shipped** |
|---|---|---|
| `WindRiftDist` | 400 | **800** |
| `StormRiftDist` | 400 | **800** |

The other six inspector fields (`CloudDensity` 0.048, `BadWeather` 1.0, `StormWalls` 0.109, `Biome` 0.009, `WindRift` 0.02, `WindRift2` 0.0) are **declared and never read by any method** — dead sliders. Do not read meaning into them.

### 1.2 What each channel holds — `ComputePoint` (`:198-223`)

```
weatherColor = ( -wind.x*0.1+0.5 , -wind.z*0.1+0.5 , Lerp(0.01,1,1-P) , Lerp(0,0.95,1-P) )
wallColor    = ( Max(worldEnd, windRift) , Max(stormRift, sandStorm) , worldEnd , sandStorm*0.25 )
```

`P = GlobalWeather.GetWeatherAt(coords).Pressure`; each wall term is `Clamp01(1 - dSqr_type/(Dist*Dist)) * 0.95`, `Dist = 800`, `dSqr` from `WeatherWalls.GetSmallestWallsSqrDistancesAt`.

**Only the `weatherColor` side touches `GetWeatherAt`. The `wallColor` side is 100% wall geometry.** Note `:220-221`: the world-edge term from `CalculateDistanceToEdge` is **overwritten** by the `WorldEndWall` proximity term, so even the edge channel ends up wall-driven.

### 1.3 Consumer — the cloud renderer reads `wallInfoTex` and nothing else

`CmdBufClouds.GetWeather` (`:1357-1369`), the CPU mirror of the shader:

```csharp
Color px = GlobalWeatherTextures.wallInfoTex.GetPixelBilinear(...);
result.wall  = px.r;
result.storm = px.g;     // <-- the overcast channel
result.edge  = px.b;
result.biome = px.a;
```

`weatherTex` is **never bound** to the cloud material. `CloudRender` binds `_WallInfoTex` (`:1254-1262`, redundantly twice) and passes only the scalar `_weatherTexScale` (`:1253`). **PROVED on the shipped shader blobs** — `grep -a` over `sharedassets0.assets`, which contains `nimitz/SSClouds4`:

| uniform | occurrences |
|---|---|
| `_WallInfoTex` | 3 |
| `_StormColor` / `_StormColor2` / `_StormColor3` | 3 each |
| `_StormHeight`, `_StormDemarc`, `_CloudCover`, `_BlightVal`, `_SandColor` | 3 each |
| **`_WeatherTex`** | **0** |

`weatherTex`'s only shipped readers are wind/rain incidentals: `WindTrail.cs:86` (discards the weather colour), `WeatherEffectHandler.cs:93` (**wind only**), `SandEffect.cs:146`, `SSRain.cs:77` (`rain = weath.a`), `LineBolts.cs:108`, and the `DebugWallsCommand` PNG dump.

**Consequence: the `Pressure` the weather-cell lattice would supply lands in `weatherTex.b`/`.a` and is read by nothing in the cloud or storm path.**

### 1.4 What `storm` does to the picture

`CmdBufClouds.Cloudmap` (`:1529-1567`), with `num = Clamp01(storm - 0.1)`:

- `:1544` — base Fbm multiplied by a `_StormDemarc`-weighted ramp: a sharp wall edge, not a gradient.
- `:1545` — the global `_CloudCover` term is scaled by `(1-wall)*(1-num)`: **inside a storm the normal cloud cover stops mattering**.
- `:1556` — `num6 = Max(num6, num * 1.7f * (fbm*0.8+0.65))`: storm **forces** density to a high floor.
- `:1558` — storm cross-fades in a second, coarser Fbm: the "billowing" look.
- `ShapeStorm` (`:1569`) subtracts `Smoothstep(_StormHeight-500, _StormHeight+500, y)`, `_stormWallHeight = 3500` — **ceiling at ~y 3500–4000**.

Colour comes from `_StormColor`/`_StormColor2`/`_StormColor3`, fed per time-of-day from `_stormWallGradient`/`_stormShadeGradient`/`_stormTransitionGrad` (`:173-179`, set `:902-904`). **INFERRED (strong):** these make it *dark* rather than merely thick — the density maths is achromatic and these are the only storm-specific colour inputs. The shader is compiled; not PROVED.

`E2pVisibility` (`:1576-1600`) is unambiguous on opacity: `if (weather.storm > 0.8f) return 1f - exp(-dist*0.025f);` — visibility falls off exponentially over ~40 m. This is the wiki's *"impossible to see through"*.

### 1.5 The effect switchboard — `WeatherEffectHandler`

`getWeather` (`:90-109`) reads **`nfo` (wallInfoTex) for everything that matters**, `weath` only for wind:

```csharp
weatherInfo.WindWallValue = nfo.r;
weatherInfo.StormValue    = nfo.g;   // storm
weatherInfo.StormMetric   = nfo.b;
weatherInfo.BiomeValue    = nfo.a;
weatherInfo.RainValue     = Smoothstep(thr-0.2, thr+0.2, nfo.g + 0.1);
weatherInfo.Wind          = new Vector3(weath.r, 0.5f, weath.g) * 2f - Vector3.one;
```

**RECOVERED shipped tuning** (serialized `level0`): `_stormThreshold = 0.75`, `BaseTestRate = 0.5`, `rainNearStormOffset = 0.1`, `_rainValueInStorms = 0.3`, `_debugMode = false`.

`LateUpdate` (`:147-237`), on `StormValue`:

| condition | action |
|---|---|
| `ProcessedRain > 0` | `SSPlanarRain.enabled = true` |
| `> 0.75` and not sand | `CmdBufBaseStorm.enabled = true` + both `InstancedStormDebris3D` on |
| `> 0.75` and sand | `CmdBufSandStorm.enabled = true` |
| **`> 0.75`** | **`CmdBufClouds.enabled = false`** — volumetric clouds *swapped out* for the opaque storm renderer |
| always | `AudioPlayer.SetGlobalParameter("Rain", …)` / `("StormType", …)` |

The storm channel drives **five** subsystems: cloud density, cloud colour, rain, storm debris, audio mix.

### 1.6 Distances, with the shipped 800 m

`g = 0.95 * Clamp01(1 - d²/640000)`, `d` = XZ distance to the nearest `StormRift`/`SandStorm` segment:

| effect | needs g > | needs d < |
|---|---|---|
| clouds begin to thicken | 0.10 | ~758 m |
| rain begins | ~0.45 | ~578 m |
| **full storm — opaque, `CmdBufBaseStorm`, debris** | **0.75** | **~367 m** |
| whiteout `E2pVisibility` | 0.80 | ~336 m |

Everything above y ≈ 4000 is exempt (`ShapeStorm`).

## 2. THE SERVER SEAM — `1204 WallSegmentState`

### 2.1 The registry has exactly one input, and it is a component

`acs/WeatherWalls.cs` (239 lines, read end to end) is a **pure static registry**. Its only mutators are `Register(WallSegmentVisualizer)` / `Unregister(...)` (`:85-129`). It **never calls `GlobalWeather.GetWeatherAt`** — PROVED by reading the file; the only `GetWeatherAt` in the texture path is `WeatherTextureGenerator.cs:200`, on the `weatherTex` side.

`acs/WallSegmentVisualizer.cs` in full is 37 lines:

```csharp
[Require] private WallSegmentStateReader state;
public WorldEditorWallData.WallType Type => (WorldEditorWallData.WallType)state.WallType;
public int   WallId => state.WallId;
public float Length => state.Length;
protected void OnEnable()  { transform.forward = state.Orientation.ToUnityVector();
                             WeatherWalls.Register(this); }
protected void OnDisable() { WeatherWalls.Unregister(this); }
```

**One `[Require]`.** `1204 WallSegmentState = { int wallType, int wallId, Vector3d orientation, float length }` (`gencode/Bossa.Travellers.Weather/WallSegmentStateData.cs`; id at `WallSegmentState.cs:21`). `WallType` (`Assets.Scripts.UI.WorldEditor/WorldEditorWallData.cs:11-19`): `0 WindRift, 1 StormRift, 2 Typhon, 3 SandStorm, 4 IceStorm, 5 WorldEndWall`. **Type 1 `StormRift` is the overcast.**

Register/Unregister are on **`OnEnable`/`OnDisable`**, so a wall appears/disappears with entity checkout. Storm state is recomputed within one full texture sweep — 1024 frames ≈ 17 s at 60 fps, or immediately in the chunk under the player. **INFERRED:** a spawned wall fades in over up to ~17 s rather than popping. Not measured.

### 2.2 The prefab exists and is clean — PROVED, read out of `resources.assets`

| GameObject | components |
|---|---|
| **`WallSegment_unityclient`** | Transform, `WallSegmentVisualizer`, `TransformNature`, `TransformOffsetsRegistry`, `TransformParentHierarchyBehaviour`, `TransformChildHierarchyBehaviour`, `StaticGlobalTransformBehaviour`, `StaticLocalTransformBehaviour` |
| `WallSegment_unityworker` | same + `ParentEntityBehaviour` |

No renderer, no collider, no audio, no other visualiser. **The silent-`[Require]` hazard surface is the transform stack we already serve, plus 1204.** The cleanest entity seam in the weather system. `docs/research/loop/data/prefab-names.tsv:314` already lists `WallSegment` as resolvable.

### 2.3 A wall with no `1229` is visual-only — and that is a feature

`GlobalWeatherDataVisualizer` (`[Require] GlobalWallDataStateReader`, **1229**) is the **only** writer of `WallGustBehaviour._gustData` and `WallConstantTorqueBehaviour`'s torque table. With 1229 unserved those dictionaries stay empty and both behaviours iterate `_gustData.Keys` — an empty loop. **So 1204 alone gives dark sky, rain, debris and ambient bolts with no wind gusts and no ship torque.** For a telegraph that is the right trade.

1229 itself carries only wind/gust/torque scalars (`Map<string,float>`) — **no visual channel**, and it `Debug.LogError`s per missing key, so half-populating it is worse than not serving it.

### 2.4 Free bonus: ambient bolts inside the wall

`LightningVisualInstancesManager` (1 instance in `level0`) spawns cosmetic bolts at `_fakeLightningPerSecondPerKilometer × TotalStormWallLength/1000` per second, positioned by `WeatherWalls.RandomPointOnStormWall`, frustum-culled (`:169-190`). Both inputs come from registered `StormRift` segments. **A served storm wall lights itself up with no server involvement.**

### 2.5 Nothing here is forbidden

`1204` is **not** in `ComponentAbsencePolicy` (`KnownAbsentComponentIds` = `{1139, 1269, 1225, 1235, 1306, 1259, 1304, 4323}`). The 44 typed segments are already imported: `docs/research/world-data/wamap-islands.json` → `Walls`, 44 entries of `{x1,z1,x2,z2,Type}`, distribution **Wind Rift 20, Storm Rift 11, Sand Storm 12, World End 1, Typhon 0, Ice Storm 0** (matches roadmap §14.4).

### 2.6 What we would have to send

Per segment, one new entity:
- `8065 Blueprint` = `"WallSegment"`
- `190602 TransformState` — position at the segment midpoint (the visualizer sets only `transform.forward`; **position comes from the transform component**)
- `1204 WallSegmentState { wallType, wallId, orientation, length }` — `orientation` = unit direction along the segment, `length` = its length, `wallId` groups segments into one wall (`WeatherWalls` keys `WallData` by it)

`WallData.DistanceSqr` treats a wall as its set of registered segments, so a long wall is many short entities sharing a `wallId`. The 44 imported records are **whole walls** (endpoint pairs), not segments — how retail subdivided them is **not established**; `WallData.GetRandomPointOnWall` and `LengthSquared` suggest per-segment granularity near the 800 m influence radius, but that is a guess.

## 3. THE 1139 VERDICT, RE-EXAMINED

**Lead B's inference survives, and this work strengthens it.** Three independent facts found here are consistent with it and with nothing else:

1. `Pressure` reaches only `weatherTex.b`/`.a`, and `weatherTex` is **never bound to the cloud shader**. Retail could not have darkened the sky from a weather cell **even if the cells had been readable**.
2. With no cells, `GetWeatherAt` returns a spatially **uniform** `Pressure 0.5` (`GlobalWeather.cs:55-69`), so `weatherTex.b = 0.505`, `.a = 0.475` everywhere. Every downstream reader (chiefly `SSRain`) behaves as a flat constant — a coherent world, not a broken one.
3. The entire visible storm system routes through `wallInfoTex`, whose inputs are authored geometry. A studio with a live weather lattice would not have built the storms on wall segments.

**The darkening did not come from the cell lattice; 1139/1269 stay correctly forbidden.** Nothing here requires touching either.

**One roadmap correction.** §14.4.1 marks `WeatherTextureGenerator` as "needs 1139: **YES**" (`:200`). True only of the **wind** half. The generator runs fine with no lattice, and the half of its output the sky actually consumes — every channel of `wallInfoTex` — is lattice-free. More precisely: **the wind is cosmetic and lattice-bound; the sky is lattice-free.**

## 4. LEAD C — is the client already richer than the server?

**Globally: emphatically yes. On islands: no.**

`level0` — present on every client with no mod — already carries the whole storm stack live: `CmdBufClouds`, `WeatherTextureGenerator`, `WeatherEffectHandler`, `CmdBufBaseStorm`, `CmdBufSandStorm`, `CmdBufDissolverStorm`, `SSPlanarRain`, two `InstancedStormDebris3D`, `InstancedBlightDebris`, `LightningVisualInstancesManager`, `uSkyManager`, `CloudTransitionVisuals`, `CloudAlphaBlendManager`. All idle today for exactly one reason: **`WeatherWalls` has zero registered segments**, so `wallInfoTex` is uniformly `Color.clear` and `storm` is 0 everywhere.

**Negative, worth preserving:** across **30 island bundles**, the only weather/cloud/sky/lightning/fog MonoBehaviour baked onto an island is `IslandLightningTimerVisualizer` — 30/30, the positive control (S1 established 255/255). **No cloud, sky, fog or overcast component on island prefabs.** No island-local darkening ramp is waiting on a value we never send.

`CmdBufClouds._cloudCover` / `_currentCloudCoverOffset` is a genuine global overcast knob — but `_cloudCover` is a private `[SerializeField]` with no setter, and the offset is driven **only** by `LocalPlayer.CurrentPlayerBiome` through four `AnimationCurve`s (`:1197-1241`). **No network path to either.** Reaching them would be a client mod.

## 5. PROVENANCE — did retail darken the sky before an understorm?

**NOT CONFIRMED, and the evidence points the other way.**

`pages/Storm_Wall.wikitext` is a redirect to `Weather#Storm Walls`. `pages/Weather.wikitext:12`, **verbatim**:

> *"The storm walls look like dark grey billowing thunderstorm clouds that are completely opaque."*

and

> *"Instruments are extremely helpful in keeping your ship on course since it is impossible to see through the thick clouds."*

That is a precise description of `storm = wallInfoTex.g` — thick, billowing, opaque, `E2pVisibility` exponential falloff at `storm > 0.8`.

The **Understorms** section (`Weather.wikitext:24-26`), the only understorm description in the 425-page dump:

> *"These storms occur with varying frequency all across the map beneath the islands… They usually last for less than a minute but affect various things on the island in the process. When the bottom of an island is struck by lightning from the understorms any loose components… will slowly begin to fall into the island."*

**Nothing about sky, clouds, darkness or a warning.** Neither does `IslandLightningTimerVisualizer` (established by the Lead A kill). Neither does anything on the island bundles (§4).

**Assessment (INFERRED):** the maintainer's *"sky getting super cloudy and dark and then lightning happening"* is most consistent with a memory of **flying toward a storm wall** — exactly that experience, and something our server does not render because it serves no wall segments. It is **not** evidence that the understorm had a sky telegraph.

**Not checked:** the dev blog the wiki cites as ref `:0`, `worldsadrift.com/blog/stormy-skies/`, is the primary source for both wall types and might settle it. Worth ten minutes before anyone designs an understorm telegraph.

## 6. THE DECISION THIS LEAVES

**Reachable without 1139 — YES**, with a caveat that changes the plan.

**Reachable:** the dark, opaque, rain-bearing, self-illuminating storm sky, via `1204` on `WallSegment` entities of type `StormRift`. One `[Require]`, clean prefab, geometry already imported, nothing forbidden, no client mod, no schema migration, no lattice.

**Caveat:** that is a **storm wall** — a line, permanent, biome-scale, ~800 m influence, ceiling at y 3500. Not an island-local timed telegraph, and §5 says retail never had one.

So Lead D found **a large missing world feature**, not a fix for defect 2.

**(A) Serve the 44 static walls.** The retail feature, what the maintainer's memory actually describes, and the highest visual return-per-component in the weather system: eleven Storm Rifts turn on dark opaque clouds, rain, storm debris, an audio mix and ambient lightning from one four-field component. A separate, cheap phase, as roadmap §14.4 already argues. Prerequisite: decide segment subdivision (§2.6) and **soak it** — 44+ new streamed entities world-wide is a new entity class.

**(B) Only then consider an understorm telegraph.** The only mechanism the shipped client offers is spawning transient `StormRift` segments around the island for the storm's duration. Be honest about what that is: **a WAREBORN TUNING invention** using a retail component in a way retail did not, with three unmeasured risks — the up-to-17 s texture sweep latency, the visible seam where the 800 m influence ends, and `CmdBufClouds.enabled = false` swapping the renderer for every player within ~367 m.

**Cheapest alternative if (B) is rejected:** nothing in the sky is cheaper. The honest options are non-sky — lengthen/shape the existing 30 s rumble, or use the countdown the server already pushes every 8 s. Both server-only. **Do not invent a client string for either.**

## 7. METHOD, so the negatives can be trusted

- **Binary greps used `-a`**, over `UnityClient@Windows_Data/Managed/` **including `WASystems.dll` and `SpatialTranslator.dll`**. Control: `WeatherTextureGenerator`, `WallSegmentVisualizer`, `WeatherWalls`, `CmdBufClouds`, `GlobalWeatherTextures` each land in `Assembly-CSharp.dll` and nowhere else, as they must.
- **Bundles were read with UnityPy, not grep.** The §5 venv was **gone** (only the scripts survived); recreated with `scan5.py` (raw `m_Script` resolution), `wallpf2.py`, `isl.py`, `wtg.py`.
- **Typetrees are NOT shipped for most MonoBehaviours in this build.** `read_typetree()` fails on **341 of 370** `level0` MonoBehaviours with `ValueError: Expected to read N bytes, but only read 32`. **This is the trap that would have produced a false zero here** — a naive scan resolves 28 of 370 components and reports no weather system at all. Fix: parse `get_raw_data()` directly — `m_GameObject` PPtr(12) + `m_Enabled` int(4) + `m_Script` PPtr(12) + `m_Name` length(4) = **32-byte header**, then serialized fields. `MonoScript` names live in `globalgamemanagers.assets` (5084), not in `level0`.
- **Serialized values read the same way** — that is how `StormRiftDist = 800` (not 400) and `_stormThreshold = 0.75` were RECOVERED.

## 8. NEGATIVE RESULTS AND OPEN ITEMS

**Established negatives:**

1. `WeatherTexGenCpu`, `AuthoredWeatherTest`, `DrawnWallTexturesTest`, `nimitz.Effects/WeatherTexGen` — **0 instances** across `level0`, `level1`, `resources.assets`. Dead test code; its numbers (`num = 0.2f` etc.) mean nothing.
2. **`_WeatherTex` does not exist in the shipped shader bundle.** Any plan routed through `weatherTex` is dead on arrival.
3. **`Pressure` has no visual consumer.**
4. **No island prefab carries any sky/cloud/fog/storm component** — 30 bundles, `IslandLightningTimerVisualizer` only (30/30 control).
5. **`1226 PocketOfLightningWallDataState` / `1227 PocketOfLightningState` are dead.** `1226`'s payload struct is **empty**. Both appear only in `Generated.Code.dll` (3 lines each) and in **no** consumer — `Assembly-CSharp`, `WASystems`, `SpatialTranslator`, `BossaECS` all 0, with `WeatherTextureGenerator`=1-in-`Assembly-CSharp` as the control in the same sweep. Same shape as the Lead A kill. Confirms roadmap §14.4.1.
6. **`1229 GlobalWallDataState` is not a visual lever** — wind multipliers, gust timings, torque only; `Debug.LogError`s per missing key.
7. **`_CloudCover` is not network-reachable.** Client mod or nothing.
8. **The Blight overcast path is closed for us.** `CmdBufClouds.GetBlightStorm` (`:1377`) feeds `Cloudmap` at `:1560-1561` and is a real second overcast channel — but its input is `GlobalBlightModel.ClosestBlightParams`, written by Blight ECS systems that all require `1269` (forbidden) **and** `BlightLocalComponent`.

**Open, honestly unestablished:**

- **The `_stormWallGradient`/`_stormShadeGradient`/`_stormTransitionGrad` key colours** — serialized on `CmdBufClouds` in `level0`; not parsed (long gradient-heavy layout, presentational not decisional). Readable with the §7 raw-offset method.
- **`nimitz/SSClouds4` was not decompiled.** The claim that `storm` *darkens* rather than only *thickens* rests on the `_StormColor*` uniforms + `E2pVisibility` + the wiki text — **INFERRED, strong, not PROVED.**
- **Wall segment subdivision** (§2.6).
- **Fade-in latency** of a newly registered wall (~17 s) is derived, not measured.
- **`worldsadrift.com/blog/stormy-skies/`** not fetched.

## 9. WHAT WAS NOT DONE

No server code changed. No client mod built or installed. Nothing pushed, nothing deployed. Production not touched, not even read. `isLightningActive` not written anywhere. No new test, no schema change.
