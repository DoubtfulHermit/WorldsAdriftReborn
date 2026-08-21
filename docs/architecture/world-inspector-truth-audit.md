# World Inspector truth contract

Audited 2026-08-21 against stats schema 14 and the authenticated `/admin` console.
This document describes the implementation that exists now; it is not a design for
remote workers.

## What the inspector can truthfully show

| Surface | Status | Source and limit |
|---|---|---|
| Local authority topology | Working | `runtime.domains` is emitted by the game loop's `LocalDomainHost`. Domain id, kind, host, affinity, entity count, active flag, warnings and position are direct snapshot fields. |
| Ship authority and replication | Working | The ship row carries hull id, authority generation, replication sequence, cadence, delivery age, pilot, aboard entities and subscriber count. |
| Interaction shadow overlay | Working, observational | The schema-14 `simulation` section distinguishes absent, disabled, warming, observing and faulted states. Pressure is explicitly uncalibrated and is not authority. |
| Entity selection | Working | A runtime domain is selected by exact canonical `domainId`, never label or row number. Duplicate/blank ids are now dropped at the login-server boundary; selection clears if the id disappears on a later poll. |
| Authentication | Working | `/admin` and `/admin/api/stats` require a valid operator session. The inspector code and identity-bearing rows are absent from the public map. |
| Sanitization | Working | The login server rebuilds allowlisted JSON. Runtime strings are bounded, kinds allowlisted, counts/coordinates clamped, unknown fields discarded, and embedded bootstrap JSON uses HTML-safe escaping. Browser cells use `textContent`. |
| Payload retention | Working, bounded | Runtime/player/ship tables are capped at the stats reader. Simulation domains/interactions are capped by both writer and reader. Terrain lifecycle history is a 64-entry producer ring and is re-capped to 64 at the reader. These are payload caps; browser `slice()` calls remain presentation caps. |
| Old schemas | Working | Missing runtime, terrain, simulation and later sections retain explicit absent/default projections. Old ship telemetry may still feed the documented compatibility drawing, but does not create remote-worker facts. |
| Remote workers | Unavailable | The server reports `local-single-process` and `local:primary`. No remote process is connected or observed. |
| Authority migration | Unavailable | There is no transfer protocol or migration lifecycle. Shadow `authorityOwner` and `migrationGeneration` remain null/“not modelled”. |
| Domain sleep | Unavailable | No domain sleep/wake lifecycle exists. A ship shown as `resting` is an inactive flight cadence state, not a sleeping domain. |

## Acceptance checklist / demo

Use a normal local or deployed server that is already running. This procedure is
read-only; it does not issue operator commands.

1. Open `/admin` in a private browser window. Confirm the operator login is shown
   and that `/admin/api/stats` returns `401 {"error":"unauthenticated"}` before login.
2. Sign in, open **Simulation**, and confirm the badge says **local single-process**
   and the host identity is `local:primary`. Do not interpret multiple cards as
   multiple processes; they are domains on the one host.
3. Select Haven or a ship from **Authority topology**. Record its exact domain id.
   Wait for two 1.5-second browser polls. The same id must remain selected even if
   its label, counts or row order changes. If that domain disappears, the detail
   selection must clear rather than attach to a different row.
4. For a ship, compare its detail with the ship-domain diagnostics: authority
   generation, replication sequence, pilot and subscriber count must agree. Affinity
   is location context and must not be presented as authority ownership.
5. Inspect **Interaction shadow model**. With
   `WAREBORN_SIMULATION_MODEL` disabled it must say **observer off**. With it enabled,
   it may briefly say **warming up**, then **observing**. The panel must continue to
   call pressure **uncalibrated** and the overlay an observation rather than authority.
6. Inspect **Terrain checkout**. Its event list must contain no more than 64 retained
   transitions even after repeated travel. An older game server must say that terrain
   telemetry is absent rather than show zero loaded islands as a measured fact.
7. Confirm the Simulation introduction explicitly says remote workers, authority
   migration and domain sleep are unavailable. An inactive ship may say **resting**;
   it must not say sleeping, migrated or remotely owned.
8. Sign out and reload `/admin/api/stats`. It must return 401 again. Open `/map` and
   verify that domain inventory and shadow-interaction tables are not present.

Automated acceptance is covered by `WorldInspectorTruthContractTests`,
`SimulationStatsProjectionTests`, `SimulationInspectorPageTests`, and
`GameStatsReaderTests`.
