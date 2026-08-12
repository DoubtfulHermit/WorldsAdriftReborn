# PROVEN AT RUNTIME — ENet survives deinit/init inside one Wine process

Date: 2026-08-08. **This is the first runtime-verified result in the whole
research corpus.** Everything else in docs/research/ is static analysis.

## The question
`findings-robustness.md` rated this the killer risk of the reconnect design:

> "Biggest risk: `ENet_Deinitialize` -> `ENet_Initialize` on the second connect
> under Wine (`Connection.cpp:29`, `Locator.cpp:19`) - untested; fallback is to
> leak the host."

A fresh client launch cannot answer it: that is a fresh process. It required a
disconnect and a reconnect **inside one process**, which nothing in our tooling
could trigger.

## Why the game's own path could not be used
The intended rehearsal was ESC -> Logout, which reaches `SpatialOS.Disconnect()`.
**It is dead here:** `LogoutBehaviour.RequestLogout` throws a
NullReferenceException before it gets there, so the countdown panel never opens.
Stack observed in `BepInEx/LogOutput.log`:
```
LogoutBehaviour.RequestLogout (.RequestLogoutCallback, .InterruptLogoutCallback)
LogoutCountdownPanel.SetLogoutMode (Mode mode)
Travellers.UI.InGame.InGameMenuScreen.ShowCountdownPanel (Mode mode)
Travellers.UI.InGame.InGameMenuScreen.ShowLogoutCountdownPanel ()
```
Cause not yet diagnosed. Worked around with `ReconnectProbe` (F9 ->
`SpatialOS.Disconnect()` by reflection), added to the mod as a diagnostic.

## RESULT: PASS
`~/Games/WorldsAdrift/CoreSdk_OutputLog.txt`, one process, one session:
```
  27: Trying to connect to game server at 62.171.161.19
  28: SUCCESS!
5261: Trying to connect to game server at 62.171.161.19
5262: SUCCESS!
```
with, between them, in `BepInEx/LogOutput.log`:
```
[WAReborn] reconnect probe: F9 pressed, IsConnected=True - calling SpatialOS.Disconnect().
```

The second connect at line 5261 is a full `ENet_Initialize` + `enet_host_create`
+ `enet_host_connect` after the first `Connection` was destroyed and ran
`ENet_Deinitialize`. It succeeded.

## What this settles
- **`WSAStartup`/`WSACleanup` refcounting behaves under Wine.** The predicted
  reason it would work (both are per-process refcounted, and Mono holds a
  reference so the count never reaches zero) is confirmed by observation.
- **`Connection` and `Dispatcher` are recreated cleanly**, so the disconnect
  latch starts fresh automatically.
- **Mod static state survives a second connect** - no manual reset was needed.
- **The server tolerates a same-IP second peer** while the first is still alive.
- The proposed `enetLayer.cpp` refcount guard is therefore **hardening, not a
  prerequisite**. The "never deinitialize" fallback is not needed.

## Consequence
Reconnect drops from "~40 lines plus an unknown that might invalidate it" to a
straightforward build: deliver `DisconnectOp` from `Connection::GetOpList`, wire
`Exports.cpp:26-29`, fix `IsConnected()`, and guard the 3-second teardown wait.
The game supplies the entire RETRY/QUIT -> character select -> reconnect flow
itself, and the server needs no change.

## Also observed in the same session
- Component setup completes and the world loads with the eleven fabricated-value
  corrections in place - they did not break entry.
- **No `[probe] IDENTITY` line and no `recorded appearance` line appeared at
  all**, i.e. the client never published a 1088 update. This supports
  `verify/identity-claim.md`'s refutation: identity is NOT "already solved".
  Do not build inventory persistence on it until this is understood.
