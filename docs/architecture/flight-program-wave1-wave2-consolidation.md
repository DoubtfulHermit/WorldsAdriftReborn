# Flight program consolidation: waves 1 and 2

Status: historical Wave 1+2 hold point on `integ/flight-program-consolidation`.
The reviewed Wave 3 head was subsequently integrated and is recorded in
`retail-flight-program-final-integration.md`. Nothing in this worktree has been
pushed, merged to main, deployed, enabled, restarted, or added to a client manifest.

## Dependency base and replay

The consolidation starts at Wave 1 final `fd5a21f`, including its normalized
quaternion and defined-part-kind fail-closed hardening. Wave 2 had been reviewed
on the earlier Wave 1 head `bda1f15`, so its branch was not merged wholesale.
`git cherry` and per-commit diffs identified exactly four unique Wave 2 commits,
which were replayed in dependency order:

1. `0159c86` - authentic lift, gravity, and overload shadow policy;
2. `bc1d65a` - deterministic collision shadow primitives;
3. `b82368c` - authentic docking lifecycle and exact-pair registry semantics;
4. `da109eb` - pure force/lift/collision integration and reviewed reconciliation.

The replay produced no conflicts. In particular, the Wave 2 integration commit
does not modify `VectorRigidBodyShadow` or its tests, so Wave 1's `fd5a21f`
hardening remains intact rather than being replaced with the older `bda1f15`
version.

## Authority and default-state audit

- `FixedFlightClock` remains the only outer accumulator. Wave 2 does not reference
  `FlightSession` or `ShipFlightService`.
- `IntegratedFlightShadow`, vector force/torque, lift/gravity, and collision are
  value-returning comparison policies. No game loop, packet writer, component
  writer, persistence writer, configuration switch, or wall clock calls them.
- `AuthenticDockingLifecycle` and `ShipDockRegistry` are isolated in-memory policy
  objects. They are not constructed by the running server and do not publish or
  persist docking state.
- No Wave 2 code changes the durable scalar snapshot or schema-18 projection.
- No Wave 2 code changes the production world-boundary evaluator, `FlightSession`,
  `ShipFlightService`, or server startup/configuration.
- Fixed stepping and durable moving-flight restore remain default OFF under
  `WAREBORN_FLIGHT_FIXED_STEP`. The already-deployed bounds switch retains its
  independent behavior and its legacy 240 ms / twelve-reference-slice path.
- No packet/component format, client DLL, or manifest changed.

Consequently the combined branch is safe only as default-OFF/PURE-SHADOW code.
It is not approval to enable fixed stepping or make vector motion, collision,
damage, or docking authoritative.

## Verification

- Focused Wave 1+2 tests: **132 passed, 0 failed**.
- Full multiplayer suite: **4,711 passed, 0 failed**.
- Login/server schema-18 projection: **5 passed, 0 failed**.
- Multiplayer Release build: **0 errors** (8 existing warnings).
- Login/server Release build: **0 errors** (16 existing warnings).
- `git diff --check fd5a21f..HEAD`: clean.
- Runtime seam diff for boundary evaluation, `FlightSession`,
  `ShipFlightService`, and server startup: empty.

The unreadable-JSON messages printed by the test runner are expected corruption
recovery fixtures; those tests passed and moved only their temporary fixture files.

## Hold point (satisfied later)

Do not push, merge, deploy, restart services, change production switches, or cut a
client manifest. Wave 3 was integrated only after its reviewed final head was
provided, followed by patch-equivalence review and the combined acceptance gates
recorded in the final integration report.
