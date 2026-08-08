# VERIFY — the four NRE families (round 2)

The 16,015 NullReferenceExceptions in the client logs, root-caused. They had never
been examined; findings-weather.md only established they were NOT the weather issue.

**Headline: only one of the four matters, and it is not the one that looked guilty.**

## Verified counts

| family | L1 | L1 line range | L1 span | L2 |
|---|---|---|---|---|
| `ChararacterDrunk.SetDrunkLevel` | **11,318** | 40,699 → EOF | 198 s, *all of it* | **94,615** |
| `ChararacterDrunk.Update` | 1,380 | 1,511 → 40,697 | 19 s | 5 |
| `PlayerExternalDataVisualizer.IsDriving` | 1,264 | 1,784 → 15,072 | startup | **0** |
| `PlayerExternalDataVisualizer.IsEditingShip` | 1,366 | 15,267 → 40,435 | 10 s | **0** |
| `PlayerMove.get_AccelerationFastestSprint` | 681 | 15,426 → 40,443 | 10 s | **2** |

L1 total 16,015 (the brief said 16,012). **The line ranges are strictly disjoint and
sequential — this is a four-phase cascade, not four independent bugs.**

## THE ONLY REAL VISIBLE BUG — IsDriving / IsEditingShip
`PlayerExternalDataVisualizer.cs`: `IsAlive()` null-guards its `[Require]` reader
(`:23`); `IsDriving()` (`:33`) and `IsEditingShip()` (`:38`) do NOT.
`PlayerExternalData.CanMove()` (`:16`) evaluates them left-to-right with `&&`:
```csharp
return !IsDriving() && !IsEditingShip() && !IsUIFocused() && IsAlive();
```
That short-circuit IS the observed phase handover, and is the decisive evidence:
- **Phase A** (1,264): `_pilotState` null → `IsDriving()` throws first.
- **Phase B** (1,366): our early injection of 1109 lands → `IsDriving()` returns false →
  evaluation advances to `IsEditingShip()` → `_hullEditState` still null → throws.
- **Phase C**: the client's second-stage interest request arrives, 1207 gets served,
  family stops dead.

**The throw escapes `UserControlCharacter.Update()` and `GrapplingHookNew.Update()`, so
Unity aborts the whole Update for that frame: no movement, no jump, no grapple for
~2,630 frames (~25 s) after the world appears.** Under real SpatialOS the components
arrived with the entity and the window was zero frames; our packet-driven delivery
widens it.

**This is the "delay" the user reported. It is NOT the jitter.**

The server already named this bug in a comment at `WorldsAdriftRebornGameServer.cs:647`
— and the fix was **half-applied**: 1109 went into `injectedEarly`, 1207 did not.

### FIXED (this commit)
1207 added to `injectedEarly`. No serializer work needed — `ComponentsSerializer.cs:251`
already handles it, so `failOnComponentInitError:true` will not abort.
The remaining 1,264 phase-A throws cannot be fixed server-side (they precede the
client's first interest request); closing that needs a client-side Harmony guard,
sketched below but NOT applied, since it requires redistributing the mod.

## ChararacterDrunk — 99.99% of the noise, and silent
`ChararacterDrunk.cs:18-46`. `_lastDrunkLevel = drunkLevel` is the **last** statement
(`:45`), so a throw anywhere above leaves it at `-1`; `Update()` (`:65`) sees the
mismatch again next frame and re-calls. **One unhandled exception per rendered frame,
forever** — structurally the same self-perpetuating shape as the weather branch.

The null is `:22`, `:24` or `:28`; `:39`/`:43` are unreachable at `drunkLevel == 0`,
which itself proves the throw is above them. Most likely `:24`
`DrunkEffectCamera.Instance` — a plain `public static` assigned only in its own `Awake`,
and the ONLY two references to that type in the whole client are its declaration and
this line. Nothing instantiates it; it is a camera image effect that must be
prefab-attached, and we build the camera path through `CameraProxy_Patch` /
`CameraBinder_Patch`. **Not resolved statically — one Debug.Log would settle it.**

**Functionally silent.** The server seeds `PlayerBuffState` (4329) with an empty buff
list (`ComponentsSerializer.cs:337-341`), so `PlayerBuffBehaviour.DrunkLevel` is pinned
at 0 and can never change. The subsystem is inert by construction; nothing is lost by
turning it off. Fix is a 2-prefix Harmony patch — **not applied here** because it
requires shipping a new mod DLL.

**Free bonus: the drunk NRE is an exact frame-rate tachometer** (one per rendered
frame). L1 runs 50–63 FPS (mean 57.2); L2 runs 37–49 FPS (mean ~43). Neither shows the
periodic stalls you would expect if exception handling caused hitches.

## get_AccelerationFastestSprint — the obvious suspect is a RED HERRING
The only call site that throws is a **dead anti-cheat assertion**:
`ClientAuthoritativePlayerMovement.Update()` (`:289-295`) contains *only*
`MaxSprintAssert.AssertEquals(...)`, whose failure path fires a `PlayerAnalytics` event
— and analytics is already stubbed by `Patching/BypassPlayerAnalytics/`. Aborting it
loses nothing. Real movement reads the same property from `PlayerMove.UpdateState`
(`:1226`, `:1235`), which **never appears in any stack in either log**. 2 occurrences in
3.88M lines. **Do nothing.**

Root cause shared with `ChararacterDrunk.Update`: both null-check
`LocalPlayer.PlayerBuffBehaviour` *after* dereferencing to reach it; the null is
`LocalPlayer._visualizers`, not built until `LocalPlayerVisualizers`' ctor runs. The
client's own guard `LocalPlayer.Exists` (`:238`) exists and is unused by both sites.

## A CORRECTION TO OUR OWN BRIEF
The premise that each NRE pays `new StackTrace(3).ToString()` is **wrong for these**.
That line (`WAUnityLogger.cs:28`) is on the `WALogger.Error` path — the **weather** spam.
Unity's unhandled-Update exceptions enter via
`Application.logMessageReceivedThreaded` → `WAUnityLogger.LogInternalErrors`
(`:43-54`), which receives Unity's already-built string and never calls
`GetStackTrace`; its only sink is gated on `SpatialOS.IsConnected`, permanently false
here. **`LogInternalErrors` is effectively a no-op.**
The StackTrace charge belongs entirely to weather — and is ~an order of magnitude more
expensive per event.

## COST — estimated, not measured (flagged honestly)
L1: 57.2 drunk NRE/s + 49.4 weather errors/s ≈ **126 error events/s**, log growth
1,227 lines/s = 91 KB/s. L2: ~43 + ~100/s, 333 MB.
Drunk family estimated at **0.5–3% of wall clock** (0.1–0.5 ms on a 17.5 ms frame).
**This is the one number in the report that is estimated rather than counted.** The
strongest evidence against it mattering is the tachometer: a smooth 50–63/s with no
dropouts is what a healthy frame loop looks like.

Sell the drunk fix as *removing 99.99% of NRE noise so the log becomes usable*, with a
small perf bonus — **not** as a frame-rate fix.

## NONE OF THIS EXPLAINS THE JITTER — but a new lead did turn up
All four are startup transients or silent. **New lead, better than anything here:**
`ClientAuthoritativePlayerMovement.cs:156,158` declare `MAX_UPDATE_RATE = 20` and
`MIN_UPDATE_RATE = 10` and **never reference them anywhere in the class**. If anyone
assumed a 20 Hz position-send throttle was in effect, **it is not** — the client
publishes at FixedUpdate rate (50 Hz) with no interpolation buffer. That is where the
jitter investigation should go.

## ALSO FIXED (this commit)
`ComponentsSerializer.cs` had **two** `componentId == 1109` branches — `:227`
(`EntityId(0)`) and `:518` (`EntityId(10)`) — in the same else-if chain, so the second
was unreachable. That was lucky: **EntityId(10) is a VALID entity id**, and
`IsDriving()` is `IsValidEntityId(DrivingEntityId)`, so a reorder would have left every
player permanently "driving" and unable to move. Removed rather than left as a trap.

## NOT VERIFIED
Which line in `SetDrunkLevel` is null (`:22` vs `:24`) — one Debug.Log settles it.
The wall-clock cost estimate. L2 contains 5 BepInEx sessions in one file; it was used
only for per-family totals and a windowed rate sample, both robust to session
boundaries.
