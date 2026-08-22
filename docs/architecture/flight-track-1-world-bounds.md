# Flight Track 1: world bounds and deterministic reference stepping

Status: implementation and local verification complete on 2026-08-22. This
track is isolated from the general fixed-clock accumulator, 6DOF, collision and
client protocol work. Nothing in this track has been pushed, merged, deployed
or enabled by the track itself.

## Period 1 — discovery

### Evidence audited

- Production was read-only checked on 2026-08-22. `wareborn-game` and
  `wareborn-login` were active with zero service restarts; the game process had
  `WAREBORN_FLIGHT_WORLD_BOUNDS=1` and `WAREBORN_FLIGHT_FORCES=1`. Its startup
  journal reported the 36,000 m edge. Population was zero and the journal had no
  live `[flight-bounds]` evidence, so configuration is proved but operator flight
  acceptance is not.
- The deployed schema-v17 path was followed end to end: `FlightSession` →
  `ShipFlightService.WorldBoundsStatFor` → `StatsSnapshot` → login-server
  allowlist → authenticated ship inspector. It exposes exact configured
  thresholds, last-cadence signed distance, applied delta-v, clamp/quarantine
  flags and reference-slice count.
- The preserved retail source
  `/home/ttanurhan/Games/WAReborn-decompiled/acs/WorldEdgePushback.cs` confirms
  strict global-coordinate comparisons, positive Y only, symmetric X/Z edges,
  quadratic `50 * t²` inward velocity change and damping only after `t > .25`.
  The release MapFile confirms `WorldEdgeLength=36000`.
- Configuration remains fail-safe: code default OFF, explicit production
  drop-in ON, malformed or undersized edge overrides fall back to 36 km.

### Findings

1. **Correctness defect:** an unmanned at-rest hull beyond a push threshold was
   excluded by `FlightSession`'s `live` gate. Retail evaluates every active
   non-kinematic rigidbody, so an illegally restored/parked hull could remain
   outside the envelope forever.
2. **Availability defect:** the cadence adapter used `while (remaining > ...)`.
   A future `+Infinity` or enormous service interval could run without bound.
   The current caller always passes 0.24 s, so this was latent rather than a
   remotely triggerable player exploit.
3. **Acceptance gap:** the inspector reports the latest cadence only. A hard
   clamp can clear before an operator captures it, leaving no durable evidence.
4. **Numerical seam, preserved deliberately:** bounds-enabled flight subdivides
   the *whole* reconstructed integrator, not only edge pushback. With default
   smoothing, one legacy 240 ms velocity update consumes `0.24/0.6 = 0.400` of
   the target gap; twelve 20 ms updates consume
   `1-(1-0.02/0.6)^12 ≈ 0.334`. Attitude similarly changes from `0.480` to
   `1-(1-0.02/0.5)^12 ≈ 0.387`. Acceleration ramps remain additive, but position,
   exponential smoothing and force/drag integration are not legacy-parity.
   This is the already deployed deterministic-reference semantics and is not
   changed here. The fixed-clock track must treat it as the current baseline,
   measure it explicitly and avoid a second silent feel change.
5. There was no discrepancy in axis signs, threshold ordering, release extent,
   hard-limit coordinates, NaN fallback, stats schema or rollback switch.

## Period 2 — coding

The bounded change set does only three things:

1. A pure `RequiresEvaluation` policy wakes a parked hull beyond any strict
   retail push threshold, including a non-finite hull. Interior parked hulls
   retain the existing quiet path.
2. The cadence-local adapter accepts at most 64 reference slices (1.28 s;
   production is 12 slices/0.24 s). Non-finite, non-positive or oversized
   intervals do not integrate or loop. A corrupt state is still quarantined.
   This ceiling is defensive validation, not the future accumulator/catch-up
   policy.
3. `ShipFlightService` writes edge-triggered `[flight-bounds]` journal entries
   when a hull enters or clears pushback, hard clamp or invalid-state quarantine.
   Continuous pushback does not spam one line per cadence. Existing schema-v17
   inspector telemetry remains unchanged.

No environment default, configured threshold, force magnitude, packet,
component, persistence format, schema version or client artifact changed.

## Period 3 — coding review

### Semantic and numerical review

- The 20 ms loop is still cadence-local and deterministic. The ordinary 0.24 s
  service call produces exactly 12 evaluations. It is not represented as a
  wall-clock accumulator and cannot perform catch-up.
- The 64-slice ceiling prevents accidental CPU spirals. It cannot be selected by
  client input; the only caller uses the compile-time 0.24 s cadence.
- The parked-hull wake uses strict `> / <` comparisons matching retail. Exactly
  X/Z ±17,600 m and Y=800 m remain outside the push region; the first value past
  each threshold wakes.
- Positive X/Z receive negative delta-v; negative X/Z receive positive delta-v;
  positive Y receives negative delta-v. There is intentionally no lower-Y
  boundary in the recovered component.
- Non-finite candidates quarantine to an at-rest previous finite pose, clamped
  inside the hard envelope; if both states are corrupt, origin is deterministic.
  Disabled-mode behavior remains the bit-for-bit legacy no-op, including its
  lack of sanitation.
- Edge-trigger log state is per hull and is removed with the domain. It is
  bounded by active hull count, adds only constant-time hash operations per
  active flight cadence and contains entity IDs/physics numbers, not account or
  credential data.

### Mutation review

Three representative mutations were applied one at a time and restored before
the clean run:

- reversing the positive-X wake comparator made **2/2 selected tests fail**;
- deleting the parked-boundary wake made **1/1 selected test fail**;
- reversing the cadence ceiling comparator made **2/6 selected cases fail**
  (ordinary 12-slice cadence and oversized-interval rejection).

The broader focused suite also directly covers enabling by default, malformed
edge length, all X/Z push directions, axis symmetry, quadratic push, damping
threshold, hard clamp, NaN quarantine and disabled-path parity.

### Rollback

Operational rollback remains `WAREBORN_FLIGHT_WORLD_BOUNDS=0` plus a game
restart at an approved empty-player window. It restores the exact legacy
single-240-ms integration path and disables both boundary intervention and
boundary-only sanitation. Code rollback is the single track commit. No database
or client rollback is required.

## Period 4 — testing

Completed local gates:

- focused bounds policy/session tests: **29 passed**;
- full Multiplayer suite: **4,578 passed, 0 failed, 0 skipped**;
- full login/admin suite: **1,227 passed, 0 failed, 26 intentional
  database-dependent skips**;
- game Release build: **0 errors** (62 pre-existing warnings);
- login Release build: **0 errors** (1 net6-EOL warning);
- `git diff --check`: clean;
- non-template JavaScript assets: `node --check` clean; template-bearing assets
  are covered after composition by the full login/admin suite.

The first game build attempt used `--no-restore` in a fresh worktree and failed
because its project assets did not exist yet. The required restore was run and
the subsequent Release build passed; this was workspace setup, not a source or
build defect.

### Disposable-hull live acceptance procedure

Do not use a valued production ship. Run only after the reviewed commit is
merged/deployed and the operator has confirmed the game may remain online.

1. Record build/schema/config and hull ID in the admin Infrastructure ship
   inspector. Start a journal capture:
   `journalctl -u wareborn-game -f -o cat | grep '[flight-bounds]'`.
2. **Interior baseline:** at least 500 m from every horizontal push threshold
   and below Y=700, test idle, forward, reverse, steering, sail-only wake,
   voluntary dismount with latched throttle, neutral stop and relog. Expect 12
   reference slices while live, finite state, positive boundary distance and no
   `[flight-bounds]` transition.
3. **Positive horizontal edge:** use operator-assisted placement of the
   disposable hull just inside X=17,600 m, facing +X. Cross 17,600 m slowly.
   Expect `pushback-entered`, negative X delta-v and decreasing outward speed.
   Repeat once on a negative X or Z edge to prove the inward sign reverses.
4. **Hard horizontal limit:** approach +17,700 m under controlled low power.
   Expect position never above 17,700 m, `hard-clamp-entered`, finite state and
   no passenger separation. Stop applying outward input and expect matching
   `hard-clamp-cleared` and `pushback-cleared` events as the hull recovers.
5. **Parked recovery regression:** operator-place the unpiloted at-rest hull at
   X=17,650 m. Without touching its helm, expect it to wake, publish motion and
   move inward. This is the defect corrected by this track.
6. **Vertical band:** from below Y=800, climb slowly through 800 m. Expect
   negative Y delta-v and `pushback-entered`. Attempt 1,000 m; expect no pose
   above 1,000 m and a hard-clamp transition. Descend and observe clear events.
7. **Durability observation only:** neutralize and relog/re-checkout. Confirm a
   legal finite pose restores. Moving-flight durability belongs to the next
   track, so do not claim velocity continuity here.
8. Export the inspector snapshot and journal lines, then recall/destroy the
   disposable hull according to the normal operator procedure. Confirm no new
   errors and that ownership remains `unowned=0, duplicates=0`.

Go/no-go: local merge is safe after all gates pass. Production enablement should
remain as configured, but this track is not live-accepted until steps 1–8 above
produce captured evidence.
