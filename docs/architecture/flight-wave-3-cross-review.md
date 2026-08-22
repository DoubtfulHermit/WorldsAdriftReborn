# Flight reconstruction wave 3: integration and cross-review

Status: complete locally on `integ/flight-wave3-crossreview`, based on Wave 1
reviewed head `fd5a21f`. Nothing in this branch has been pushed, merged to main,
deployed, enabled, restarted or added to a client manifest.

## Reviewed inputs

- Track 7 `0ebdddd`: hull-authored combustion demand and per-generator durable
  fuel.
- Track 8 `0847bf0`: pure deterministic vector wall/storm shadow policy. Its
  local prerequisite commit `4776574` was deliberately not cherry-picked because
  Wave 1 already contains the reviewed Track 3 primitives.
- Track 9 `1a47157`: inverse domain membership index, bounded transport-neutral
  worker contracts and deterministic scale model.

All three track discovery/review documents and the Wave 1 fixed-clock/snapshot
record were read before resolving the integration.

## Conflict and composition decisions

The only cherry-pick conflict was the using block in
`WorldStateSnapshot.cs`. Both additive persistence owners were retained:

- `BuiltShipRecord.FlightSnapshot` remains the Track 2 hull-flight checkpoint.
- `MountedPartRecord.GeneratorFuel` and `LoosePartRecord.GeneratorFuel` remain
  Track 7 per-part tank checkpoints keyed by stable `PartUid`.

Fuel was not flattened into the hull snapshot. `PropulsionDemandFor(hull)` remains
the one live combustion command and counts mounted engines. A dry metered hull
removes engine thrust only; sails and the physical control lever remain untouched.

At a process boundary, Track 2 intentionally preserves momentum but abandons the
connection-scoped pilot and neutralises input while advancing the ship-domain
generation. Therefore latched throttle burns fuel while the process is alive, but
does not resurrect combustion after restart. Per-generator levels survive exactly.
The Track 7 document was corrected where it previously implied otherwise.

Track 8 consumes Wave 1's single copies of `ShadowVector3`,
`ShadowForceAccumulator` and the fixed-step constant. It remains a pure evaluator:
there is no service construction, environment switch, live force/damage call or
client/wire change.

Track 9's worker authority is a protocol model only. The live single-process
`ShipDomain` remains the sole mutable authority/generation owner. No protocol type
is referenced by the game-server runtime, no worker is started and no migration or
failover is claimed.

## Independent review corrections

1. Track 7 originally reused the historically default-ON fuel switch, which would
   have activated new hull-demand, per-engine burn, engine-only gating and durable
   tank writes immediately on merge. `WAREBORN_FUEL_HULL_DEMAND` now gates only
   that new lifecycle and defaults OFF. OFF preserves the old input mirror,
   ship-level burn, dry-throttle clamp and JSON shape; persistence fails closed.
2. Worker idempotency originally treated the same command id and payload digest at
   a different sequence as a duplicate. An exact retry now requires command id,
   sequence and digest to agree; changed sequence or digest is a conflict.
3. Wall ship/target IDs were bounded but allowed control characters and ambiguous
   path delimiters into deterministic intent IDs. They now accept only printable
   alphanumeric identifiers plus `.`, `_`, `:`, and `-`.
4. `LocalDomainHost.Synchronize` silently removed duplicate live members before
   validation. It now rejects the malformed domain before changing either the
   forward or reverse ownership index.

## Combined verification

New cross-track tests prove:

- clean-dismount latched throttle burns two-engine demand before restart;
- a JSON round-trip retains two distinct generator levels and the durable flight
  checkpoint without flattening either;
- restart advances authority, preserves momentum/fuel and neutralises combustion;
- different poll jitter reaches the same fixed tick and produces identical wall
  intent IDs;
- domain capture/restore, reverse-index removal/re-registration and worker snapshot
  generations coexist without making the candidate protocol model a live owner;
- the runtime contains one copy of each shared primitive;
- the live game-server source contains no vector-wall damage or remote-worker
  protocol wiring.

Clean results:

- focused Wave 3 matrix: **87 passed, 0 failed**;
- full multiplayer suite: **4,703 passed, 0 failed**;
- login/admin suite: **1,228 passed, 26 intentional database-dependent skips,
  0 failed**;
- game Release build: **0 errors**, existing warnings only;
- login Release build: **0 errors**, existing warnings only;
- `git diff --check`: clean.

Three mutations were applied separately and restored before the clean run:

- ignoring sequence in retry identity failed the worker idempotency test;
- allowing arbitrary bounded wall IDs failed the intent-ID security test;
- restoring duplicate normalization in `Synchronize` failed the reverse-index
  integrity test.

## Residual risk and verdict

- Fuel's existing subsystem/thrust switches keep their historical defaults, while
  Track 7's new lifecycle and fixed-step authority are separate operational
  switches that remain OFF. Their first combined live restart acceptance still
  requires a disposable unoccupied hull and a world-state backup.
- Wall magnitudes and damage remain lost retail data. The current policy is safe
  precisely because it has no live wiring and default tuning is inert.
- Worker contracts have no transport authentication, canonical serializer,
  lease/consensus coordinator, durable outcome horizon or session-rebinding policy.
  `local:primary` must remain the only live authority.
- The reverse index improves membership refresh complexity but does not address the
  O(players²) avatar relay, per-peer ship scan or future 50 Hz physics budgets.

Verdict: **GO** to merge this stack later with every new authoritative behavior
default-off and Track 8/9 still inert. **NO-GO** for enabling fixed-step+fuel in
production without disposable-hull restart acceptance, for applying live wall
forces/damage, or for starting any remote worker/migration experiment.
