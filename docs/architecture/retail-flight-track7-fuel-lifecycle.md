# Retail flight Track 7: fuel and propulsion lifecycle

Status: implementation branch `feat/flight-track7-fuel`; no push, merge, deploy,
restart or client-manifest change.

## Discovery

The live propulsion command already belongs to `FlightSession.Input`. A clean
dismount deliberately reduces the input to `LatchedThrottleOnly`; disconnect and
abandon neutralise it. Fuel instead maintained a second per-player 1111 mirror
and treated an empty seat as zero. The result was free unmanned engine thrust.
Re-manning without a throttle delta could also leave flight and fuel disagreeing.

The generator is the retail tank. Capacity **100 per generator is recovered**
from the archived Power Generator page and `FuelGaugeVisualizer.SetFuelAmount(0,
100)`. Retail pooled generator tanks at the ship root. `ShipEngineState` 1116
separately carries throttle, power, spin-up and consumption, while the archived
engine record identifies fuel efficiency and spin-up as per-engine statistics.
The actual consumption rates and server transfer loop are lost with GSim, so
`WAREBORN_FUEL_BURN_RATE` remains explicitly **WAREBORN TUNING**.

Scenarios traced: piloted held throttle; clean unmanned latch; abandoned helm;
re-man with a diff-suppressed 1111; zero/one/multiple engines; zero/one/multiple
generators; empty fuel; mount/lift/transfer/salvage; loose and mounted restart;
legacy and corrupt JSON; and a capacity configuration change.

## Coding

- `HullPropulsionDemand` is the single combustion demand: authoritative session
  throttle plus live mounted-engine count. Sails and lift are deliberately absent.
- Burn is proportional to absolute throttle, time and engine count. No engine
  means no burn. Generator count increases capacity, not thirst.
- A dry metered hull removes only `EngineThrustNewtons` in the force model. It
  does not rewrite the physical lever or suppress sails, ambient wind or lift.
- `GeneratorFuelSnapshot` is a versioned additive DTO on both loose and mounted
  part records, keyed by stable `PartUid`. Explicit zero survives restart; null
  retains the legacy full-on-first-registration rule.
- Levels persist immediately on refuel, empty and detach, and at a bounded two-
  second cadence while powered. Invalid/future data fails closed at empty.
- Duplicate stable part identities are skipped during restore, preventing a
  corrupt mounted+loose pair from duplicating a fuel-bearing generator.

## Review

- Free thrust: closed for clean dismount because burn reads `FlightSession.Input`.
- Reman/delta suppression: no second input merge exists in fuel.
- Multiple engines/generators: engine count scales consumption; generators pool
  range in deterministic mount order.
- Invalid fuel: negative, non-finite, unknown-version and over-capacity values
  cannot grant fuel. Current configured capacity wins and saved level clamps.
- Disconnect/restart: abandon neutralisation remains flight-owned. Track 2 stores
  the pre-restart lever as evidence but deliberately restores neutral input while
  retaining momentum and advancing authority. Generator fuel survives unchanged;
  no unmanned combustion demand is resurrected after a process restart.
- Transfer/duplication: fuel follows generator entity in memory and `PartUid` on
  disk; duplicate stable identities restore once.
- Detach/ownership/security: the existing checked-out, distance, ownership and
  carry/mount gates remain ahead of the fuel hooks. Fuel adds no client authority.
- Rollback: fields are additive and older binaries ignore them. Rolling back does
  lose enforcement and may apply the older full-on-restore behavior; back up world
  state before any production rollback.

## Testing and integration gate

Tests cover throttle-dismount burn, re-man without a delta, zero/two engines,
multiple generators, empty cutoff policy, sail force with zero engine force,
explicit-empty restart, legacy null, corruption, capacity changes, transfer and
duplicate identity rejection. Full suite/build evidence is recorded with the
branch handoff.

Track 2 integration is required for process-restart continuation: retain
`PropulsionDemandFor(hull)` after applying `53d3b56`, backed by the current
`ShipDomain.Flight.Input`. Track 2 intentionally neutralises that input at a
process boundary; only momentum and per-generator fuel resume. Reapply the two
additive `GeneratorFuel` properties beside Track 2's
`BuiltShipRecord.FlightSnapshot` changes. Track 3 has no direct dependency; its
shadow evaluator should consume the same engine-powered decision when it
graduates to live authority.
