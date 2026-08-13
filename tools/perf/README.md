# perf tooling: stutter probe + thread profiler

Operational guide for the stutter investigation on `perf/stutter-deep-dive`.

## The probe (runs on BOTH machines via the mod)

Grep `[WAR][perf]` in `BepInEx/LogOutput.log`:

- `probe armed threshold=100ms hooks=ops+ents+comps+spatial+tmpl+act` — boot.
  A missing hook name = that field reads 0 on that machine; a
  `hook 'X' NOT armed` line says why.
- `spike dt=..ms f=.. t=..s ents+E/ops+O comps+C tmpl+T spatial=..ms
  gc0+.. gc1+.. gc2+.. heapD=±..MB heap=..MB q=.. thr=..`
  — one line per frame over the threshold (rate-limited 6/5s). Counters are
  the LONG frame's own work (previous-frame attribution, see StutterProbe.cs).
- `beat t=..s ... fps=.. spikes=.. worst=..ms ents=.. comps=.. heap=..MB thr=..`
  — every 30 s, lifetime totals. The LAST beat before a crash is the crash's
  context.
- `activation isActive=True ... entsSoFar=..` — the loading screen released.
  Every `ents+` on spikes AFTER this line is an entity instantiating IN VIEW
  (= the load barrier leaked; that spike line names the moment).

Threshold: `[Perf] Perf_SpikeThresholdMs` in `BepInEx/config/WorldsAdriftReborn.cfg`.

## Next session (host)

1. Launch normally, play through load-in + a few minutes.
2. While it stutters: `tools/perf/lagprof-threads.sh 10` → save output.
   This resolves the ~40 truncated "Worker +" spinners to full thread names
   and per-thread CPU%; with elfutils installed it also stacks the hottest.
3. Collect `BepInEx/LogOutput.log`, grep `[WAR][perf]`.
4. Optional A/B: relaunch with `tools/perf/run-client-topology.sh`
   (WINE_CPU_TOPOLOGY=8 P-cores → Unity 5.6 spawns 7 job workers instead of
   27; taskset cannot do this — wine reports the machine's core count
   regardless of affinity). Compare beats (`fps=`, `thr=`) and spike counts.
   Change nothing else between runs.

## Colin's log (Windows)

Read in order: `probe armed` (which hooks armed) → the LAST `beat` → the
`spike` trail after it → whether `activation isActive=True` was ever reached
and whether `ents+` continued after it.

## Decision table over spike signatures

| Signature | Meaning | Next action |
|---|---|---|
| `ents+>0` before `activation` | world streaming behind the loading screen | none — barrier working as designed |
| `ents+>0` AFTER `activation` | barrier leaked; entities instantiate in view | cross-ref server `[interest]`/spawn-pace log at those timestamps; extend `LoadBarrierPolicy.IsInitialKey` |
| `spatial≈dt`, `ents+0`, `comps+>0` | AddComponent burst on live entities | find the sender in the server log; likely another unledgered re-serve (same class as the 190602 fix) |
| `gc0+`/`gc2+>0`, sawtooth `heapD` | Mono GC pause from allocation pressure | read heapD growth rate between spikes to name the allocator |
| `spatial` small, `gc` 0, `ents` 0 | not network/ECS/GC → engine or driver (render, shader compile, DXVK) | `DXVK_HUD=compiler` run; if Linux-only it is prefix-side |
| `q=` growing across beats | client cannot drain AddEntity as fast as server paces | raise `WAREBORN_SPAWN_PACE_MS` |
| stutter persists in the topology A/B | worker spin is heat, not the stutter driver | keep topology for thermals; follow what the spike lines name |
