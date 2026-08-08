# WHERE THE REMOTE-PLAYER DELAY COMES FROM

**Investigation only. Nothing changed, launched or deployed.** Written against
the session of 2026-08-08 19:51-19:59, in which the user watched the other
player's stream side by side with their own screen and saw a major delay between
him acting and it appearing locally. Both saw each other move, so the relay
works — it is late, not dead.

**[V]** = verified by reading code or measuring the log. **[I]** = mechanism
verified, magnitude for this session not measured. Do not promote an [I] without
running the experiment in §7.

## 0. THE ONE-PARAGRAPH ANSWER

The most likely cause is **not on the relay path at all**: the other player was
probably running a `CoreSdkDll.dll` from before `7735a87`, in which **every**
client-to-server packet went out with ENet's unreliable flag. That makes his
uplink lossy *and* subject to ENet's sender-side throttle, which discards
unreliable packets on his own machine before they reach the wire —
one-directional, bursty, multi-second. Unconfirmed, and the evidence for it (F9
doing nothing) is weaker than it looks. Behind it is a real second cause we own:
the observer's client writes **265 KB/s of stack traces**, ran its simulation
clock at ~96% of wall-clock with a **14-second freeze mid-play**, and the game's
remote positioner is a **timestamp-driven interpolator whose playback clock only
advances when FixedUpdate runs** — so an observer-side stall is rendered directly
as "he is late". Tonight's two new per-loop server calls are **eliminated**.

## 1. THE RELAY PATH, HOP BY HOP

Seventeen hops from his keypress to her screen. The ones with a verified defect:

- **Hop 3** [V] `Connection.cpp:166-197` hardcodes RELIABLE for every client
  publish, including the transform stream.
- **Hop 5** [V] transmission happens **only** inside `enet_host_service`, and
  that function returns from the dispatch step at `protocol.c:1756-1777`
  **before** it ever reaches `enet_protocol_send_outgoing_commands` at `:1788`.
- **[V] Nothing in this codebase ever calls `enet_host_flush`.** `ENet_Flush` is
  exported, the C# server declares the import and never calls it, and the
  client-side C++ never calls it either.
- **Hop 7** [V] `ENet_Poll(server, 50, ...)` returns **one** event per call.
- **Hop 13** [V] the receiver's `GetOpList` likewise returns one packet per call,
  with one `OpList` slot per type.
- **Hop 14** [V] a per-packet `Logger::Debug` with **two synchronous flushes** on
  the receiver's main thread.
- **Hops 16-17** [V] the interpolator — see §4.

## 2. WAS THE REMOTE CLIENT ON TONIGHT'S BUILD?

### 2.1 What a pre-`7735a87` client does [V]

The fix corrected a numbering collision: `ENet_Send`'s flag mirrors the C# enum
`RELIABLE=0, UNRELIABLE=1`, but all four call sites passed
`ENET_PACKET_FLAG_RELIABLE`, which is **1** — UNRELIABLE in that numbering, and
specifically `UNRELIABLE_FRAGMENT`. Three consequences:

1. **No retransmission.** Wire loss is permanent.
2. **[V] ENet destroys unreliable packets at the SENDER under throttle.**
   `enet_protocol_send_unreliable_outgoing_commands` increments
   `packetThrottleCounter` by 7 mod 32 per packet and, if it exceeds
   `peer->packetThrottle`, **frees the packet without sending it**
   (`protocol.c:1489-1508`). `enet_peer_throttle` (`peer.c:62-90`) subtracts 2/32
   on every acknowledgement whose RTT exceeds
   `lastRoundTripTime + 2*variance`, and can drive `packetThrottle` to **0** — at
   which point 100% of that peer's unreliable traffic is discarded locally.
   Recovery is +2 per good ack out of 32.
3. `UNRELIABLE_FRAGMENT` discards the whole message if any fragment is lost.

An old client therefore produces exactly the reported signature: his avatar still
moves (some packets survive), but late, jumpy, in multi-second bursts, **in one
direction only**, and nothing on our side looks wrong.

### 2.2 Can the artefacts we hold tell us? — NO [V]

- **ENet records the sender's reliability and we throw it away.** On receipt ENet
  sets `packet->flags` from the wire command: `ENET_PACKET_FLAG_RELIABLE` for a
  reliable send (`protocol.c:451`), **`0`** for unreliable (`:519`).
  **`ENetPacket_Wrapper` has no `flags` field** and `ENet_Poll` never copies it.
  One field and one log line from definitive; invisible today.
- `server.log` carries no per-peer packet metadata at all.
- The client logs are the **wrong machine** — they are the user's own process.
  `coresdk-hermit.txt` has 54 distinct line patterns and returns **0** for every
  one of `enet|queue|drop|throttl|bandwidth|channel|reliab|rtt|latency|mtu|peer|packet|flush`.
- **No wire-visible mod-version discriminator exists.** Everything in
  `WorldsAdriftReborn/` since `7735a87` is local-only — log formatting, an NRE
  guard, two probes. The only binary difference an un-updated player carries is
  `CoreSdkDll.dll` itself.
- The `fallback flush for idle peer` line is **not** evidence — it is documented
  expected behaviour for an idle in-world player *and* what a dropped unreliable
  asset ack would look like. Consistent with both, therefore evidence for
  neither.

### 2.3 The F9 evidence is weaker than it looks [V]

The probe **visibly failed on the user's own machine too**:

```
622609: reconnect probe: disconnect threw: TargetInvocationException
        ---> InvalidOperationException: Disconnect called while not connected.
```

Its only feedback is a log line. A player who does not read `LogOutput.log`
cannot distinguish "not installed" from "installed and threw" from "window not
focused".

### 2.4 TWO UNAMBIGUOUS CHECKS

1. Ask him for the string `reconnect probe` in his BepInEx log.
2. **Ask him the byte size of his `CoreSdkDll.dll`.** The committed artefact was
   not rebuilt at the fix commit: `git ls-tree -l` gives **337,557 bytes** at
   both `7735a87^` and `7735a87`, and **338,140** only from `3df464d` onward.
   **337,557 = broken. 338,140 = fixed.**

## 3. THROUGHPUT, CADENCE OR BLOCKING? — CADENCE PLUS AN OBSERVER STALL

**The old server bug is definitively gone** [V]: `server.log` for the whole
session is **1,323 physical lines**. `ServerLog.Trace` is doing its job.

**Tonight's two new per-loop calls are free** [V]: `Teleports.PollTrigger()`
returns on a `Stopwatch` comparison *before* any file I/O;
`TickTreeHarvest()` returns `Array.Empty<>()` when no latches exist — no
allocation, no work. **Both eliminated as suspects.**

Measured cadence:

| Quantity | Value |
|---|---|
| Session wall-clock | 446 s |
| In-play span | 331 s |
| 190602 updates processed | ~11,750 |
| -> both players | **~35 /s** |
| -> **per player** | **~18 /s** |
| Client updates sent | 25,142 (**56 /s**) |
| Client updates received | 17,834 (**40 /s**) |

A remote position arrives about **18 times a second**, one sample per ~55 ms. Low
but not catastrophic — not seconds of delay on its own.

**The sent/received deficit is NOT packet loss** [V] — the server log shows the
user was alone in the world for part of the session and rejoined as a new entity
mid-way.

**Stream composition** [I]: 56 sends/s cannot contain a 50 Hz raycast stream *and*
the transform stream *and* the bone stream. This is strong evidence that
**1231 `SalvagerAimerState` was NOT publishing at raycast rate this session** —
most likely the multitool was never equipped.

## 4. THE MECHANISM WE ACTUALLY OWN

### 4.1 The remote positioner is a delayed, timestamp-driven interpolator [V]

`RemoteRigMover` yields to `PlayerVisualizer`, so positioning is entirely
`PositionInterpolator.GetInterpolatedValue(Time.deltaTime)`:

- `DEFAULT_INTERPOLATION_DELAY_SECONDS = 0.1f` — a deliberate 100 ms playback
  delay on top of the ~55 ms sample interval.
- `pendingValues` is a `CircularFifoQueue` of **capacity 5** — a larger backlog is
  silently overwritten.
- values are keyed on the **sender's** timestamp, not arrival time.
- `GetInterpolatedValue(delta)` does `CurrentTime += delta`. **The playback clock
  advances only when FixedUpdate runs.**
- The only catch-up is `ApplyTimeDriftCorrection`: `CurrentTime += gap * 0.1f`,
  **10% per call, and only when pending values exist**.

**Consequence [V]: any interval in which the observer's FixedUpdate does not run
is added directly to how far behind the remote avatar renders, then repaid at 10%
per FixedUpdate.** A 14-second observer freeze leaves the remote avatar visibly
lagging for two to three seconds afterwards, gliding rather than snapping. That
is exactly the reported symptom, produced entirely locally.

### 4.2 The observer's client was stalling [V]

- The client log is **99.29% one repeating 15-line error block** — 87.1 MB of
  87.7 MB. Strip it and the whole session is **622 KB**.
- Sustained **2,769 lines/s and 265 KB/s**, written by BepInEx's disk listener on
  the main thread.
- The client's clock ran at **~95.6% of wall-clock**.
- **A 14-second gap in wall-clock timestamps mid-play**, 19:56:57 -> 19:57:11,
  while the session otherwise emitted 2,769 lines/s.

### 4.3 A frame-rate estimate that does NOT close, and is flagged rather than fudged [I]

The `local pos` beat is quantised to render frames, giving **~22 fps**. But the
client received 17,834 packets over 446 s = 40/s, and `GetOpList` yields at most
one packet per call [V] — so it was called **at least 40 times a second**. Either
the fps estimate is wrong or `GetOpList` is pumped more than once per frame. The
logs cannot separate them.

### 4.4 The client's transmit path is starvable by its own receive backlog [I]

Because `enet_host_service` returns from dispatch before reaching send [V], and
nothing calls `enet_host_flush` [V], **a `GetOpList` call that finds a non-empty
dispatch queue transmits nothing**. Over a burst of `k` queued incoming packets
the client transmits on 1 of `k` pumps. This couples the observer's *uplink* to
its *downlink backlog* — genuinely surprising, and worth knowing whatever
tonight's cause turns out to be. Unmeasurable: nothing records queue depth.

### 4.5 Per-packet double-flushed logging on the client's main thread [V]

`Logger::debug` writes to **both** `std::cerr` and an `ofstream`, each with
`std::endl` — two synchronous flushes per line — once per received
ComponentUpdateOp, inside `GetOpList`, on the main thread. The same bug
`ServerLog.cs` documents, unfixed, on the client side.

**And the error storm pays a second tax through it**: of the coresdk log's
348,957 lines, **87.2% (304,149) are `GetFlag` and `SendLogMessage` generated by
the storm forwarding Unity errors** — ratios to the error count of 2.9957 and
1.9970, near-exact integers.

## 5. DID TONIGHT'S CHANGES CONTRIBUTE?

**(a) The two new per-loop calls — NO [V].** Eliminated.

**(b) The two new entities — NO, not to steady-state latency [V].** Spawn-time
work only; nobody chopped. They did each add a *failure* (the `8062` and `2108`
batch drops), which is a correctness problem, not a latency one.

**(c) The widened grants — PARTLY, less than feared.** `08fb983` added five ids
to `AuthoritativeComponents`, so all five became new client->server streams.
`IsRelayedToOtherPlayers` filters 1231 and 1037 from the relay, correctly and
*before* the byte copy — **but the filter does not stop them arriving**: each is
still a full ENet event and a full `HandleComponentUpdate`. 2105/2106/2002 **are**
relayed, and relayed **RELIABLE**, on channel 4, the same channel as movement.
The code's "low-rate" comment is **not verified anywhere**. The packet arithmetic
says the worst case did not materialise this session, but it is armed and will
fire the first time someone equips the multitool.

**A structural hazard tonight makes worse [V].** ENet only dispatches an
unreliable packet when its `reliableSequenceNumber` matches the channel's
(`peer.c:724`). Reliable and unreliable traffic on the **same channel** are
coupled: one lost reliable packet on channel 4 holds up **every subsequent
unreliable movement packet** until it is retransmitted. Channel 4 now carries
unreliable 190602 + 1073 alongside reliable 1088, 6910, 1098 and, as of tonight,
2105/2106/2002. Moving the high-rate unreliable streams to their own channel
would decouple them.

## 6. TWO SMALLER SERVER DEFECTS FOUND WHILE TRACING

Neither explains seconds of delay; both violate the codebase's own contracts.

**[V] `HandleComponentUpdate` does reflection per packet** — a linear scan of
vtables, `GetType().GetMethods()` plus two LINQ `Where`s, a **full linear scan of
443-entry `MetaclassMap`**, then `MakeGenericMethod` and `Invoke`. The hash is a
pure function of `componentId`, recomputed from scratch every packet. A
`Dictionary<uint, ulong>` built once removes all of it.

**[V] `ServerLog.Trace`'s call-site contract is violated at the relay** —
`WorldsAdriftRebornGameServer.cs:485-486` pre-builds a concatenation with two
`Describe` allocations that run **whether or not `Verbose` is set**, which is
exactly what `ServerLog`'s own docblock forbids.

## 7. RANKED CAUSES AND THE CHEAPEST DECISIVE EXPERIMENT

| # | Cause | Contribution | Confidence |
|---|---|---|---|
| 1 | **Remote client on a pre-`7735a87` SDK** — uplink all unreliable, plus sender-side throttle drops | Explains it **completely and one-directionally** | Mechanism **high [V]**; applied that night **unconfirmed** |
| 2 | **Observer-side stall rendered as remote lag** — 265 KB/s traces, ~96% clock, 14 s freeze, into an interpolator that repays gaps at 10%/call | Seconds of visible lag after each stall | Mechanism **high [V]**, magnitude **[I]** |
| 3 | **~18 Hz cadence + 100 ms interpolation delay** | A steady ~150-200 ms floor | **High [V]** |
| 4 | **Client transmit starved by its own receive backlog** | Bursty uplink | Mechanism **high [V]**, magnitude unknown |
| 5 | **Mixed reliable/unreliable on channel 4** | Sub-second bursts on a lossy link | Mechanism **high [V]**, frequency unknown |
| 6 | **Per-packet double-flushed logging on the client** | Feeds #2 and #4 | **High [V]** |
| 7 | Server per-packet reflection + relay string build | Negligible tonight | Verified **small** |
| 8 | ~~Tonight's two new per-loop calls~~ | **None** | **Eliminated [V]** |
| 9 | ~~Server logging on the ENet thread~~ | **None** — 1,323 lines all session | **Eliminated [V]** |

### The cheapest decisive experiment

**Add a per-peer, per-5-second counter carrying the received packet's ENet
reliability flag.** Add `enet_uint32 flags` to `ENetPacket_Wrapper`, copy
`event.packet->flags` in `ENet_Poll`'s RECEIVE branch, mirror the field in the C#
layout, and print:

```
[peer-rate] peer 0x...  190602=N  1073=N  other=N  reliable=N  unreliable=N
```

~15 lines, no behaviour change, and it settles **three** questions at once:
which build he is on (old = `flags == 0`, verified at `protocol.c:451` vs `:519`);
whether his uplink is sparse; and whether tonight's grants are firing.

**Zero-cost, and do it FIRST: ask the other player whether HE saw HER late.**
Cause #1 is strictly one-directional; causes #2-#6 are symmetric. That single
answer splits the top of the table.

## 8. WHAT IS NOT VERIFIED

- **That the remote client ran an old build.** Unconfirmed; F9 is weak evidence
  and no artefact we hold can settle it.
- **Any packet loss, RTT, jitter, bandwidth, queue depth or throttle value for
  this session.** Nothing records them. Every statement about ENet's throttle and
  head-of-line behaviour is about the *code*, never about the night.
- **The client's frame rate.** The ~22 fps figure rests on an unverified Unity
  5.6 assumption and contradicts the >=40 Hz floor implied by packet counts.
- **Whether `GetOpList` is pumped once or several times per frame.**
- **The publish rate of 2105/2106/2002.** The "low-rate" claim is asserted, never
  measured.
- **How much of the 14 s gap was a hard freeze** versus a pause in the storm —
  only the storm's timestamps mark time.
- **The direction of the observed lag.** We know she saw him late. We do not know
  whether he saw her late. **That single fact is worth more than anything else in
  this document.**
