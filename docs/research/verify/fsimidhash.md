# VERIFY — fsimIdHash echo suppression (ships risk #1)

**VERDICT: SHIPS VIABLE.** Risk #1 does not exist. Risk #2 was mis-stated.

(Round 2 adversarial verification of `findings-ships.md`. Recorded by the
orchestrator; the agent was blocked from writing this file.)

## The mechanism
`acs/Bossa.DeadReckoning.Improbable/SSPDeadReckoningVisualizer.cs:95-124`:
```csharp
if (value.fsimIdHash == SpatialOS.Configuration.WorkerId.GetHashCode()) { return; }
```
The publisher stamps it at `SSPDeadReckoningBehaviour.cs:161`:
```csharp
ShipControlPoint shipControlPoint = controlPoint.ToEventData(SpatialOS.Configuration.WorkerId.GetHashCode());
```
`ToEventData(int fsimIdHash)` (`ControlPoint.cs:53-56`) takes the hash as an explicit
parameter, so the wire value is always the publisher's. The debug values 1/2/3/4
scribbled into `ControlPoint.FsimIdHash` at `acs/DeadReckoningSender.cs:104`
**never reach the wire** — they are overridden at the `ToEventData` call.

## WorkerId is a fresh GUID per PROCESS
`acs/Improbable.Unity.Configuration/WorkerConfiguration.cs:171`:
```csharp
WorkerId = GetObsoleteAndCurrentCommandLineValue("workerId", "engineId", text + Guid.NewGuid());
```
`text` = `"UnityClient"` (`:165`). Not a constant, not config-driven, not derived from
the connection. It flows outward to the connection at
`ConnectionLifecycle.cs:242` — the connection is named *from* the id, not the reverse.

Our mod never touches it: `WorkerConfiguration_Patch.cs` patches only `ProjectName`
(`:17-30`), `LocatorHost` (`:35-42`) and `Port` (`:44-50`).
`WorldsAdriftRebornCoreSdk/Structs.h:305` merely declares `const char* WorkerId;` as a
passthrough.

## PROVEN AT RUNTIME, from our own two clients
`WorkerConfiguration.cs:203-206` prints the config on non-editor builds:
- `~/Games/WorldsAdrift/UnityClient@Windows_Data/output_log.txt:2405`
  → `WorkerId = UnityClient993f8ed1-761d-4cb9-a448-bdc5dfa1617d`
- `~/Games/WorldsAdrift-2/UnityClient@Windows_Data/output_log.txt:2513`
  → `WorkerId = UnityCliente5dfbde4-cee1-4c74-affa-e55754e008d1`

Command lines are bare, no `+workerId` (`:2362`, `:2470`). Distinct GUIDs ⇒ distinct
hashes ⇒ the suppression never fires on a peer's points. **Holds even from one shared
install directory, since the id is minted per process, not per install.**

The suppression is load-bearing *for* us: `SSPDeadReckoningVisualizer` needs readers
only and runs on every platform, so the pilot runs it too — `:102-105` is exactly what
stops the pilot dead-reckoning its own echo.

## The pattern does NOT appear on anything we already relay
The complete set of files referencing `FsimIdHash` is three: `DeadReckoningSender.cs`,
`SSPDeadReckoningVisualizer.cs`, `ControlPoint.cs`. `PathFollower.cs` and
`SplineInterpolator.cs` contain zero references — no second gate downstream.
Structurally impossible elsewhere: neither 190602 `TransformStateData` nor 1073
`ClientAuthoritativePlayerStateData` carries a worker id (field lists verified in
gencode). **So this explains no observed symptom and is costing us nothing today.**

## Risk #2 — the finding was WRONG
An unseeded 1130 does **not** error the publisher.
`SSPDeadReckoningBehaviour.cs:43-59` has no `throw` and no early `return`; it logs and
falls through to a fallback:
```csharp
else {
    WALogger.Error<SSPDeadReckoningBehaviour>("Checked out a ship without a control point!");
    _sender.RigidBody.position = _transformState.LocalPosition.RemapGlobalToUnityVector();
    _sender.RigidBody.rotation = _transformState.LocalRotation.ToUnityQuaternion();
}
```
`WALogger.Error` was decompiled out of `WAUtilities.dll` and confirmed a pure log.
**Consequence is positional, not fatal:** a bad/zero 190602 seed puts the ship at the
origin. Seed both.

## What a valid 1130 seed must contain
`SSPPredictedMotionStateData(bool extrapolate, Option<ShipControlPoint> latestControlPoint)`
(`gencode/.../SSPPredictedMotionStateData.cs:9-17`; id at `SSPPredictedMotionState.cs:451`).
`ShipControlPoint` (`gencode/.../ShipControlPoint.cs:9-25`):
```
long timestamp;        // MILLISECONDS SINCE EPOCH (ControlPoint.cs:41)
Coordinates position;
Quaternion32 rotation;
Vector3f velocity;
int fsimIdHash;
```
`latestControlPoint` must be a **present** Option. To survive
`ControlPoint.ValidateControlPoint` (`:78-111`) position, velocity and rotation must be
finite and non-NaN, timestamp non-NaN. Set `fsimIdHash = 0` — safe, and already the
gencode default.

## TWO CONSTRAINTS TO CARRY INTO THE BUILD
1. **Rate gate.** `ControlPoint.ValidateControlPoints` (`:113-126`) drops any point whose
   delta is `< desiredInterval * 0.95`, with `SendInterval = 0.24`
   (`acs/ShipConfiguration.cs:24`). A relay that duplicates or re-broadcasts 1130 faster
   than ~0.228 s apart has the extras **silently discarded**. Relay 1130 one-for-one.
2. **Handover blackout.** On an `fsimIdHash` change (pilot handover) `:110-115` discards
   that point and calls `IgnoreControlPointsUntil(t + 0.5)`
   (`ShipConfiguration.cs:10`). Expect a half-second freeze on every pilot change —
   correct behaviour, but budget for it visually.

**1130 may be UNRELIABLE and should be for a continuous flight stream.** Every update
contains a complete absolute latest point (timestamp, global pose and velocity), so a
later point supersedes a lost one. The earlier claim that a widened interval could trip
the 0.228 s gate had the inequality backwards: `ValidateControlPoints` rejects only a
gap *smaller* than 0.228 s. Losing one 0.24 s point produces a valid 0.48 s gap, after
which `PathFollower` spline-corrects from its extrapolated pose. Reliable delivery made
loss much worse in the 2026-08-14 two-player session: two moving ships accumulated
49 KB in flight and 6.8 s RTT, delaying interaction traffic behind obsolete motion.

## NOT VERIFIED
Whether a `+workerId` could arrive via Steam launch options — only that the two observed
runs had bare command lines (not load-bearing; you would have to deliberately pass the
same id to both). Whether `LatestControlPointUpdated` fires on initial checkout or only
on subsequent updates — affects seed delivery timing only, not the verdict.
