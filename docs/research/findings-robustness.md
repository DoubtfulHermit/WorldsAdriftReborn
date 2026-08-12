# FINDINGS — ROBUSTNESS / RECONNECT

(Recorded by the orchestrator; the agent was blocked from writing this file.)

## HEADLINE
The ghost session is a **four-link chain of defects in the client shim** — and the
game **already ships a complete, working reconnect UX** ("RETRY / QUIT" dialog →
character select → fresh ENet connection) that those defects prevent from firing.
Fix ≈ **40 lines of C++**. No new wire message, no new channel, **no server change**.

## Q1 — ENet works; our layer discards the result
ENet 1.3.17 (`enet.h:26-28`). Defaults ping 500 ms, timeoutMin 5000, timeoutMax
30000, limit 32 (`enet.h:229-232` → `peer.c:413-416`). An idle peer IS pinged
(`protocol.c:1607-1615`) and the ping is reliable (`peer.c:453`), arming
`enet_protocol_check_timeouts` (`protocol.c:1334-1363`) — **dead-server detection
does not depend on app traffic.** Practical ~6–10 s, bounded 5 s–~31.5 s.
`enet_host_service(host,event,0)` still runs everything (`protocol.c:1783-1791`).

Server passes both callbacks (`WorldsAdriftRebornGameServer.cs:566`).
**Client passes `NULL` for both** — `Connection.cpp:44` — so `enetLayer.cpp:168-174`
consumes and DISCARDS the DISCONNECT, returning `NULL`: indistinguishable from
"no traffic".
Bonus: `enetLayer.cpp:141` tests `== 0`, so a fatal `-1` is silently ignored.

## Q2 — client-side detection (highest value)
1. Event discarded (`Connection.cpp:44`).
2. **`IsConnected()` returns `this->peer != NULL`** (`Connection.cpp:35-38`); `peer`
   set once in ctor (`:10`), cleared only in dtor (`:31`) → **true forever**. Game
   polls it every frame (`Exports.cpp:145-148`, `SpatialOS.cs:265`).
3. `WorkerProtocol_Dispatcher_RegisterDisconnectCallback` is an empty TODO
   (`Exports.cpp:26-29`) though the game always registers one (`sdk-decomp:839`).
4. The mod patches out the game's 65 s watchdog
   (`HeartbeatVisualizer_Patch.cs:9-16`).

**Do NOT un-patch link 4** — `HeartbeatVisualiser` is `[Require]`-gated on components
the server never seeds (`:16-20`) and refreshes only on traffic never sent (`:37-40`).
The patch was correct. **A pure-C# mod watchdog cannot work either**: the server
sends nothing unprompted, so a solo player has no signal. The native fix is
*necessary*, not preferable.

Payoff chain verified end to end: `ConnectionLifecycle.cs:48` → `sdk-decomp:1046-1053`
→ `SpatialOS.cs:399-403` → `ConnectionLifecycle.cs:118-121` → `SpatialOS.cs:427-428`
→ `InGameState.cs:73-84` → `UnrecoverableErrorState.cs:76` (RETRY/QUIT) → `LobbyState`
→ `ConnectToWorldState.cs:53` → `Bootstrap.cs:143` + `SpatialOS.cs:312` pass → new
`ConnectionLifecycle` → new ENet connection.

Fix: null/`peer->state` check in `IsConnected()`; pass a real `callbackD`; add
`DisconnectOp*` to `OpList`, a `Dispatcher` branch, wire `Exports.cpp:26-29`.
**Use a reason string that is NOT `"Disconnect was called by the user."`** — that
literal routes to a silent lobby return (`InGameState.cs:76-79`), not the dialog.

## Q3 — reconnect
Smallest workable reconnect = the above. **Server needs no change.** A returning
client replays `GameState.WorldState` from step 0 and gets a new entity id
(`:428-434`); island id is stable (`:416-426`). Don't reuse ids.
Sole gap: each reconnect leaves a **stale avatar** (removal has no wire
representation). Ship reconnect first; don't block on removal.
Note: ENet gives the new connection a fresh peer slot while the ghost lingers, so the
old peer's DISCONNECT arrives AFTER the new CONNECT — harmless today (keyed by
pointer), **fatal if anyone ever keys by address**.

## Q4 — identity
Today identity is the raw `ENetPeer*` (`PeerIdentity.cs:27-30`). Smallest durable
key: **1086 PlayerName already carries `characterUid`**
(`gencode/.../PlayerNameData_Internal.cs:12`) and is already seeded (`:396`), but the
server fabricates it (`ComponentsSerializer.cs:97`). Granting the client authority
over 1086 delivers it over the existing channel — zero protocol change.
**Unverified:** whether the client actually publishes 1086 when granted. Test first.
(Superseded in practice by the persistence pass's 1088 route — see
findings-persistence.md.)

## Q5 — hygiene
- **LIVE BUG:** `loopTick` is used as a wall clock ("~2s"/"~3s", `:146-150`,
  `:190-192`) but advances per *event*, not per 50 ms — under load the mirror
  timeouts collapse to a fraction of a second. **Strong candidate for the
  "one-at-a-time" symptom.** Use a real clock.
- **Leak:** five mirror dictionaries (`:141,144,186,187,188`) never cleaned in
  `OnClientDisconnected` (`:38-75`).
- `playerEntityIDs` (`:345`) write-only and unbounded — delete.
- `ENetPeerHandle.ReleaseHandle` is a no-op only by accident; `SetHostHandle` would
  arm a 3 s cross-thread blocking ENet call from the finalizer. Delete it.
- Logging: peer ids at `:34/:74/:43/:578`, log departing entity id, periodic
  collection sizes.

## Q6 — operations
`Restart=always` + start-limit (shutdown is clean, `:438-441`); note `script` masks
exit status. Replace the port-bound check with a journal heartbeat carrying the
player count. **Rewrite `docs/hosting.md:66-69`**: a restart becomes a ~30 s
recoverable interruption, not a session-ending event.

## ORDERED PLAN
1. `IsConnected()` consults `peer->state`; pass a real `callbackD`. (~10 lines)
2. Deliver `DisconnectOp` → **this alone delivers the whole reconnect.** (~30 lines)
3. Server hygiene commit (real clock, `ForgetPeer`, deletions, logs) + unit tests.
4. Ops: restart policy, heartbeat, doc rewrite.
5. Identity: test the 1086 grant first.
6. Entity removal (separate pass).

## RISKS / UNVERIFIED
Requires redistributing `CoreSdkDll.dll` (not a protocol change; mixed clients
interoperate). **Biggest risk: `ENet_Deinitialize` → `ENet_Initialize` on the second
connect under Wine** (`Connection.cpp:29`, `Locator.cpp:19`) — untested; fallback is
to leak the host. Also a ~3 s main-thread stall at teardown
(`enetLayer.cpp:105-116`); the `loopTick` change touches the most test-expensive
subsystem, so commit it separately; reconnect MULTIPLIES stale avatars until removal
lands. Unverified: typical ENet latency; whether the client publishes 1086; whether
the game destroys `Dispatcher`/`Connection` on disconnect; whether mod statics
self-heal; whether "one-at-a-time" is fully explained.
