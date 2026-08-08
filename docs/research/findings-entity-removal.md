# FINDINGS — ENTITY REMOVAL / DYNAMIC WORLD

**Headline: the client-side removal path already exists and works.
Exactly one link is broken.** Effort ~1 day.

(Recorded by the orchestrator: the research agent was blocked from writing this
file directly, so this is its verbatim report.)

Bossa's client implements removal end to end:
`acs/Improbable.Unity.Core/DispatchEventHandler.cs:155` subscribes `OnRemoveEntity`;
`:386-398` `RemoveEntity` → `:400-409` `DestroyEntity` disposes visualizers, calls
`entityInterestedComponentsUpdater.RemoveEntity`, `universe.Remove`, then
`:411-444` `DelayDespawnCoroutine` deactivates and pool-returns the GameObject.
The managed SDK registers the native callback **unconditionally in its
constructor** (`sdk-decomp:845`, thunk at `:1117-1124`).
**Our DLL discards it** — `WorldsAdriftRebornCoreSdk/Exports.cpp:51-54` is the TODO.
That is the whole gap.

## Q1 — client side
`RemoveEntityOp` already exists native (`Structs.h:169-171`, one `long`) and managed
(`sdk-decomp:610-613`, `:3282-3285`) with no marshalling attributes — blittable, far
simpler than `AddEntityOp`. `EntityEventHandler.cs:59-72` already defers a removal
behind a pending add, so it survives our resend logic
(`WorldsAdriftRebornGameServer.cs:236-251`). Removals dispatch reverse-order
(`InvokeAllReverse`, `:1123`); callback exceptions are swallowed and logged
(`:2180-2230`).

Driving removal from the mod instead is **not viable**: `RemoveEntity`/`DestroyEntity`
are private on an `internal` class, and it still needs a wire message.

## Q2 — wire format
New `RemoveEntityOp.proto` using **`repeated int64 EntityId = 1;`**, mirroring
`AuthorityChangeOp` rather than `AddEntityOp`: `Connection::GetOpList` polls one
packet per call (`Connection.cpp:44`) and `OpList` has one slot per type, which would
otherwise cap removals at one per frame.

- `CH_RemoveEntityOp 5` in `enetLayer.h`; `REMOVE_ENTITY_OP = 5` in `EnetLayer.cs:8-15`
- `PB_EXP_RemoveEntityOp_Serialize` mirroring `enetLayer.cpp:296-328`
- `SendRemoveEntityOP` mirroring `SendOPHelper.cs:13-36`
- Always RELIABLE. No ack needed — the add-path ack (`Connection.cpp:66`) exists only
  to advance the join state machine (`WorldsAdriftRebornGameServer.cs:596`).

## Q3 — dispatcher wiring (~30 lines)
Mirror map: `Exports.cpp:51-54` ← `:46-50`; `Dispatcher.h`/`Dispatcher.cpp:6` register;
`Dispatcher::Process` loop ← the `authorityChangeOp` loop at `Dispatcher.cpp:38-49`;
`OpList.h:7` field; `Connection.cpp:92-109` demux branch.

**Two bugs found in the add path while mirroring — do NOT copy them:**
- It **leaks** a `std::string` per serialize (`:314-327`), per deserialize (`:336`),
  and the op itself (`Connection.cpp:59`; `OpList` has no destructor). Harmless at add
  frequency, **not** at harvest frequency.
- `PB_AddEntityOp_Deserialize` writes **one byte past** `new char[len]` (`:355-357`).

## Q4 — no interim option is worth building; all are hacks
- **Moving the entity away**: the server can't round-trip 190602 (documented at
  `SendOPHelper.cs:124-133`); no global culling (`CullAtDistance.cs:5-64` is a 1000 m
  per-prefab opt-in); islands render as unbounded imposters; ships stay on the TCB
  radar by entity id (`TerritoryControlBeaconScreen.cs:370-445`).
- **Disabling renderers**: colliders remain, and it still needs the same channel+proto
  — it only saves the *cheap* part.
- `DestroyedState` (1135) and `EnableState` (1038) exist in gencode but have **zero
  consumers** in the 3539-file client dump — flipping them is a no-op.
- `HackDespawn` (`CraftableSpawningVisualizer.cs:50-55`) is the nicest hack and is the
  right *animation* later, not a substitute.
- For ships, `ShipReclaimVisualizer.cs:75-106` shows Bossa had to fan out over children
  *and* disable colliders — more code than real removal.

## Q5 — the real risk: the 5-channel cap
Channel count is hard-capped at **5** in three places: `Locator.cpp:24`,
`Connection.cpp:10`, `WorldsAdriftRebornGameServer.cs:461`. ENet negotiates `min()`
(`protocol.c:325-330`), `enet_peer_send` returns `-1` for an out-of-range channel, and
**`ENet_Send` ignores the return** — so a channel-5 packet to an old client is
silently dropped and leaks.

There is **no version check at all**: `ValidateClientVersion` is Harmony-patched out
(`ConnectToNeededServersState_Patch.cs:10-15`), the mod is still `"0.0.1"`, and
distribution is hand-copied zips whose install scripts glob `*.dll`
(`setup.ps1:57-67`) — the same pattern that already lost `zlib1.dll`.
`CoreSdkDll.dll` ships inside the client pack and is the same binary both sides
(`docs/hosting.md:91-92`).

Recommendations:
1. Log + `enet_packet_destroy` when `enet_peer_send < 0` — highest value, one line.
2. Bump the limit to **16**, not 6, so channels 6-15 are free forever.
3. Piggyback a version byte on the existing `CH_AddEntityOp` ack, which the server
   reads by channel only — backward compatible both ways, no new channel.
4. Bump and log the plugin version plus the DLL hash.

## Effort and ordering
~1 day. The riskiest step is **not** the dispatcher wiring — it is the channel bump: a
lockstep protocol break with silent failure and no manifest. Second is the disconnect
teardown race at `WorldsAdriftRebornGameServer.cs:40-62`.

`RemotePlayerMirror.OnLeave` already emits `MirrorOp.RemoveEntity` (`:74-88`) — **no
policy change needed**. But removals must bypass `pendingMirrors` (which parks on
asset acks); targets are already in-world.

## Could not verify
Nothing was built or run; the MSVC proto registration path; end-to-end delivery in a
live client; whether the server tolerates extra ack bytes; why `Dispatcher.cpp:28`'s
struct copy is needed; whether `AssignViewInstancesSystem` leaving a stale
`ViewInstanceLocalComponent` (no unassign counterpart) matters under churn.
