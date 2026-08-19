# perf tooling: stutter probe + thread profiler

Operational guide for the stutter investigation on `perf/stutter-deep-dive`.

---

## 2026-08-19: the CHOPPINESS is the worker pin, and the pin is still the lesser evil

Read this before the 2026-08-18 section below. That section is still correct
**for the conditions it was measured in** (an empty world, `ents=0`), and its
fix is still the right default - but it optimised *average* frame rate, and
average frame rate is not what the maintainer is complaining about.

### What players feel

With the world loaded (`ents=143`, `comps=1300`) and the workers pinned:

| | value |
|---|---|
| mean fps over 44 beats | 67.6 |
| frames carrying a >=25 ms main-thread block | **~35%** |
| median length of those blocks | **~38 ms** |
| longest measured | **74 ms** |
| the other ~65% of frames | **~11 ms** |

Measured over one aligned 100 s window: **1,705 blocks vs 4,444 frames
delivered (17.0 blocks/s against 49.3 fps)**. So the frame cadence is roughly
`11 11 38 11 11 38 ...`. The mean is a perfectly respectable 20 ms; the
*delivery* alternates between 90 fps and 26 fps several times a second. That is
the choppiness, and no average-fps number will ever show it.

### What the block is

The Unity main thread parks in **`futex_waitv` (syscall 449) on the job-queue
semaphore `0x1000ff440`**, with **0.00 ms of CPU and exactly ONE scheduler
switch inside the window** - i.e. a genuine single sleep, not many short waits
merged by the sampler (1,993 of 1,993 windows >=25 ms had <=2 switches).

It is waiting for the job system. The job system is 27 workers **serialised
onto cpu27**, which sits at ~75% busy. With a real world to cull, skin and
batch there is now genuine parallel work, and putting all of it through one
E-core means the main thread waits for it. Pinning converted a CPU-burn
problem into a **latency** problem.

The probe never attributed these because `Perf_SpikeThresholdMs` was 100 and
the blocks are ~38 ms. That is why every beat said `worst=60-80ms` with
`spikes=0`: the hitch was always under the threshold. Default is now 40.

### The A/B, taken under load, in one sitting

The maintainer's earlier "unpinning showed no regression" was **invalid**:
`pin-unity-workers.sh` is a *polling daemon*, not a one-shot. It re-applies the
affinity every 2 s, so a manual `taskset` is silently undone before the next
beat is even written. You must stop the daemon (it is a `/bin/sh` child of the
launcher) before the workers are actually free.

Same session, same scene, `ents=142`:

| workers | mean fps | worst frame | main thread |
|---|---|---|---|
| pinned to cpu27 (n=44 beats) | **67.6** | 101.8 ms mean | 30% CPU, blocked 55-61% in 38 ms chunks |
| **free on 0-27** (n=10 beats) | **42.2** | 127.5 ms mean | **97% CPU**, 3.2M ctxsw/s, ~zero long blocks |
| pinned again (n=4 beats) | 55.7 | 90.9 ms mean | back to blocking |

Unpinning **does** cure the judder - long main-thread blocks fall from
1,737 per 100 s to **1 per 150 s** - but it costs a third of the frame rate and
brings back the futex herd at **3.2 million ctxsw/s** (worse than the 1.83 M
originally documented, because there is more work to dispatch now).

**Recommendation: keep the pin.** It remains the better of the two, just by
1.3-1.6x rather than the 2.4x measured on an empty world. Both endpoints are
bad, though, and the real fix is neither.

### The fix that is actually indicated (needs a relaunch, NOT yet measured)

Cap the worker *count* instead of their concurrency, so there is neither a
27-way herd nor a 27-into-1 funnel: `run-client-topology.sh` sets
`WINE_CPU_TOPOLOGY=8`, and Unity 5.6 then spawns **7** workers instead of 27.
Seven workers on seven real cores can run concurrently without forming the
herd, and can absorb the job load without a one-core queue.

The 2026-08-18 curve (2 cpus 92 fps, 3 cpus 12.4 fps) does **not** predict
this: that curve varied how many CPUs *27* workers could spread over, which is
a different variable from how many workers exist. This is the one experiment
that has never been run, and it needs a relaunch, so it is a next-session job.

### Second, independent hitch: synchronous logging on the main thread

Separate from the pin, and the explanation for the big unattributed spikes:

```
spike dt=392.6ms ... spatial=340.9ms asset=0.0ms ents+0 comps+0 gc0+0
```

An external syscall watcher caught, in the same window, a **340.6 ms `write()`
to `CoreSdk_OutputLog.txt`** with `wchan=btrfs reserve_bytes`, during which
gfx, dxvk and every worker made zero progress - the whole engine stopped.

Every exported `WorkerProtocol_*` call runs on the Unity main thread, and
`Logger::Debug` wrote each line **twice** (`std::cerr`, unit-buffered, plus the
file with `std::endl`, which flushes). The per-op trail ran at **5.3 KB/s /
~100 lines/s**, and `/home` is btrfs at **93% full**, where a single delalloc
reservation can block for tens to hundreds of ms. Fixed by gating the per-op
lines behind `WAREBORN_CORESDK_TRACE=1` (default off) and buffering them; see
`WorldsAdriftRebornCoreSdk/Logger.h`. Lifecycle and error lines still flush per
line, so join-failure triage is unchanged.

This is a `spatial >> asset, all counters 0` signature - the row the decision
table said "needs its own field". It did not need a field; it needed the write
taken off the main thread.

### Ruled out, with measurements

- **Memory / swap.** Alarming on paper - zram swap 97% full, the client 1.72 GB
  swapped out - but `/proc/pressure/memory` reads `some avg10=0.02`. There is
  no memory stall. Do not chase this.
- **GC.** `gc0 == gc1 == gc2` always (Unity 5.6 uses Boehm, which is
  non-generational, so all three counters are the same number). It ticks about
  once per 63 s, not once per beat, and never on a spike frame.
- **GPU.** 36% busy. **Machine load.** 12% all-CPU. **DXVK.** genuinely loaded.

---

## SOLVED (2026-08-18): the in-world frame rate was Wine fsync futex churn

**Root cause.** Unity 5.6.4p1 sizes its job-worker pool at `visible CPUs - 1`.
On the host's i7-14700F that is **27 `Worker Thread`s**. Between jobs each one
blocks on the job-queue semaphore; under `WINEFSYNC=1` that is a `futex_waitv`
(syscall 449), and Wine's fsync semaphore **release wakes every waiter**. So
every job dispatch was a 27-way wakeup with a cross-core IPI per worker.

Measured on the live client, in-world, `ents=98`, 3440x1440, DXVK:

| | fps | proc CPU | ctxsw/s | GPU busy |
|---|---|---|---|---|
| workers free on all 28 cpus | **51.4** | 807% | **1,826,265** | 45% |
| workers confined to ONE cpu | **120.9** | 137% | 58,367 | 84% |

**1.83 million voluntary context switches per second — ~35,000 per rendered
frame.** It was not work. With the herd collapsed the 27 workers together drop
from **672% CPU to 26.6%**, and, decisively, the Unity **main thread's system
time drops from 72.5% to 3.4%**:

| thread | before (user/sys) | after (user/sys) |
|---|---|---|
| main (`vx_initialize`) | 26.3% / **72.5%** | 46.8% / **3.4%** |
| 27x `Worker Thread` | 113% / 559% | 22.5% / 4.1% |

The main thread was spending three quarters of every frame inside `FUTEX_WAKE`
dispatching jobs to a spinning herd. That is why the frame rate tracked entity
count (`ents=67 -> 52 fps`, `ents=98 -> 42 fps`): more entities meant more job
dispatches, and each dispatch cost 27 wakeups. The per-entity cost was
**sync amplification, not per-entity game logic and not draw calls** — the mod's
own counters (`asset=16ms`, `spikes=0`, `comps=990`) never moved, and GPU busy
was only 45%.

### The fix: confine the workers to ONE cpu

`pin-unity-workers.sh` — polls the game's threads and `taskset`s every thread
named exactly `Worker Thread` onto a single CPU (default: the last E-core, so
every P-core stays free for the main/render/dxvk threads). It is wired into
`~/Games/WAReborn-servers/run-client.sh` and `run-client2.sh`
(`WAREBORN_PIN_WORKERS=0` disables; backups `*.bak-preworkerpin`).

**One cpu, or none — never "a few".** The curve is violently non-monotonic,
because the herd only forms when workers can wake concurrently. Same scene,
same session:

| workers pinned to | fps |
|---|---|
| 1 cpu (cpu17 / cpu27, E-core) | **120.9** |
| 1 cpu (cpu1, P-core sibling) | 111.9 |
| 2 cpus | 92.2 |
| 3 cpus | **12.4** |
| 4 cpus | **14.0** |
| 28 cpus (free) | 51.4 |

Three or four CPUs is *far worse than leaving them free*: 27 oversubscribed
workers preempt each other while holding job-queue state (a convoy). One CPU is
the only safe point — the kernel serialises them, so a semaphore release has at
most one runnable waiter and the IPI storm cannot form.

Pinning the main/render/dxvk threads to dedicated physical P-cores on top of
this is worth +0.7 fps (120.6 -> 121.3), i.e. noise. Worker confinement is the
whole effect.

### Result

In-world **51.4 -> ~120 fps** (mod heartbeat, independent of the profiler:
`fps=119.2 ... ents=98`), worst frame 41-47ms -> 27-33ms, process CPU 807% ->
137%, GPU busy 45% -> 84%. Vsync is off and the display is 144Hz, so the
remaining ~24 fps to the refresh ceiling is now GPU/render-thread, not the job
system. **The 100 fps target is met.**

### Gotcha: you cannot find the client by `comm`

`/proc/<pid>/comm` of the game reads **`vx_initialize`** — the Vivox voice SDK
renames the process main thread during init. `lagprof-threads.sh` matched
`UnityClient*` against comm and therefore never once found the running game.
Match the **cmdline** instead, with `case` (not `grep`, which matches its own
argv) plus a `/proc/<pid>/exe` = `*wine*` guard. `comm` is still the right
thing for identifying the *worker threads*.

### Not proven

- **`WINE_CPU_TOPOLOGY` was never measured.** `run-client-topology.sh` reduces
  the worker *count* rather than their concurrency. Testing it needs a relaunch,
  and the client has no autologin — getting back in-world needs manual
  login/Play clicks, which the no-synthetic-input rule forbids. Given 7 free
  workers would still wake concurrently, and 2-4 concurrent cpus measured
  92/12/14 fps, it is *expected* to be worse than the one-cpu pin — but that is
  a prediction, not a measurement.
- The fix was validated on a **static scene** (player parked, `ents=98`). Its
  behaviour during heavy world streaming / load-in is untested; a single E-core
  may throttle genuine parallel job throughput there. `spikes=0` and a *lower*
  worst-frame time under the pin are encouraging but are not a load-in test.
- Draw-call count was never measured (`DXVK_HUD` needs a relaunch). It is now
  moot for the CPU-bound question — GPU busy rose to 84% while CPU fell 6x, so
  draw submission was not the limiter.

---

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
| `spatial` small, `gc` 0, `ents` 0 | not network/ECS/GC → engine or driver (render, shader compile, DXVK) | **run `block-depth.py` and `blocking-syscalls.py` first** - on this host it was the main thread asleep in `futex_waitv` waiting for the pinned job workers, not the driver |
| `spatial` ≈ `dt`, `asset` 0, all counters 0 | time inside the op pump doing no op work → a **blocking write on the main thread** from the CoreSdk logger | `blocking-syscalls.py <pid> 120 20` and read the resolved path; ensure `WAREBORN_CORESDK_TRACE` is unset |
| beats show `worst=60-80ms` every single beat but `spikes=0` | the hitch is UNDER `Perf_SpikeThresholdMs` and always was | lower `Perf_SpikeThresholdMs` (default is now 40) — **read at startup only, so relaunch** |
| `q=` growing across beats | client cannot drain AddEntity as fast as server paces | raise `WAREBORN_SPAWN_PACE_MS` |
| stutter persists in the topology A/B | worker spin is heat, not the stutter driver | keep topology for thermals; follow what the spike lines name |
| steady low fps, `spikes=0`, mod counters flat, GPU busy < 50%, process CPU ~8 cores | **Wine fsync futex herd across Unity's job workers** — see the SOLVED section at the top | check `ctxsw/s` (>1M = herd); confine `Worker Thread`s to ONE cpu via `pin-unity-workers.sh` |

## Files in this directory

| file | what it is |
|---|---|
| `fps-probe.sh` | fps / CPU / ctxsw from outside the game, ~8 s resolution, no relaunch. Start here. |
| `blocking-syscalls.py` | **names the stall.** `<pid> <secs> <min_ms>` - polls `/proc/<tid>/syscall`, reports every syscall the main thread sat in longer than `min_ms`, and resolves fd arguments to a FILE PATH. This is what caught the 340 ms `write()` to `CoreSdk_OutputLog.txt`. Read-only, no ptrace, safe on a live session. |
| `block-depth.py` | **proves a long wait is real.** `<pid> <secs> <min_ms>` - same polling, but also reads `schedstat`, so it reports how many scheduler switches happened INSIDE each window. `switches<=2` with `oncpu_ms=0` means one genuine sleep; a high count means your sampler merged many short waits and the "long block" is an artifact. Always confirm a stall with this before believing it. |
| `pin-unity-workers.sh` | **the fix.** Confines Unity's `Worker Thread`s to one cpu. Wired into the launchers. |
| `lagprof-threads.sh` | full per-thread CPU breakdown + hot-thread stacks (needs `elfutils`). |
| `run-client-topology.sh` | superseded, never run — see its header. |
| `run-client.sh.deployed`, `run-client2.sh.deployed` | **reference copies** of the launchers as deployed to `~/Games/WAReborn-servers/`, which is not a git repo. Read-only snapshots; edit the real ones there. Pre-change backups: `*.bak-preworkerpin`. |
| `lagprof-session1.out` | the original 60 s `top -H` capture that first showed the ~25%-each `Worker +` wall. |
