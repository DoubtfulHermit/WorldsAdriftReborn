# FINDINGS — NODE RELAY & MULTIPLAYER SYNC

**LEAD: our relay physically CANNOT address a non-player entity, and relaying node state
through it today would write node bytes into a player's component slot.** Four things
must exist first (~600–900 lines, of which ~450 is pure policy + xUnit that runs on Linux
with no Wine and no game). **~5–6 days.** All of it is buildable, deployable and
observable in a two-client session **before a single line of harvesting exists.**

Milestone: *both clients see the same rock; a third joining later sees it already cracked.*

## WHY THE RELAY CANNOT CARRY NODE STATE — three independent bakes, each fatal alone
The entity id is deserialized at `WorldsAdriftRebornGameServer.cs:697`, passed to the
update handler at `:707`, and **dropped on the floor at `:712`** — it is not a parameter:
```csharp
private static unsafe void RelayToOtherPlayers(ENetPeerHandle sender, uint componentId, byte* data, int dataLength)
```
The relay then **reinvents** an entity id from the sender's player registration
(`RemotePlayerMirror.cs:55-65`), and that reinvented id is what goes on the wire.

- **(a) Subject substitution.** Any relayed update is addressed to *the sender's avatar*.
  A tree-cut on entity 57 would arrive as an update to the sender's Traveller.
  **Active corruption, not a missing feature.**
- **(b) The registry has no room for a node.** `PlayerRegistry` is a single
  `Dictionary<ulong,long> _entityByPeer` (`:13`); every accessor is keyed by peer. A node
  has no peer. **No expression in this type can name an unowned entity.**
- **(c) The fan-out set is backwards.** `PeersExcept(peerId)` (`:71-82`) excludes the
  origin because a player's own client knows where it walked. **For a node the opposite
  holds** — the harvester's client did not compute the new `shotPoints`, the server did.
  **The harvester needs it MORE than anyone.**

Corollary: `MirrorIntent` carries **no prefab** (`:28-55`), so the flush site supplies
`"Traveller"/"Default"` as a constant (`:286`). Everything this machinery can spawn is a
player rig.

## THE LATE-JOINER MECHANISM ALREADY EXISTS — no new wire message needed
The client, on AddEntity, **asks** for the components it needs, unprompted:
`DispatchEventHandler.cs:373-376` → `EntityObject.cs:41` →
`EntityInterestedComponentsUpdater.cs:82-116` → `SpatialCommunicator.cs:133-141`
`connection.SendComponentInterest(...)`, landing at `WorldsAdriftRebornGameServer.cs:605-688`
and answered by `ComponentsSerializer.InitAndSerialize` **per peer, at that peer's own
checkout moment.**

> **`InitAndSerialize` IS the replay point.** No snapshot queue, no join-time state dump.

The precedent is already in the tree — the 1088 appearance branch at
`ComponentsSerializer.cs:109-118` reads `Appearances.Get(entityId)`. A node does the same
one level up: an early `if (Nodes.IsNode(entityId)) { seed from registry; return; }`.
A joiner arriving after a tree is half-cut gets `sectionMask` **as it currently is**.

## FIVE WAYS TO GET LATE JOIN WRONG
1. **Fresh entity ids per joiner.** Node graphs cross-reference by `EntityId`
   (`MetalRockCrustStateData.cs:10,12`, `MetalRockStateData.cs:14`,
   `MetalDepositStateData.cs:10`) and resolve **on the receiving client by id** — this is
   rule 4 / the shared-island-id bug generalised. Allocate **once**, at spawn, from the
   shared `EntityIdAllocator`, replay byte-identically. Get it wrong and a late joiner
   sees a crust with a dangling `depositId`, and `MetalDepositCrustVisualiser.IsInitialised`
   (`:46`) depends on `_deposit.IsInitialised`, so it **never initialises at all**.
2. **Ordering.** Nodes after the island entity; within a graph **parent-first**
   (deposit → core → crust).
3. **Replaying EVENTS instead of STATE.** Bossa split these deliberately, visible in one
   file: `MetalRockCrustState.cs:67 TriggerShot(Vector3f offset)` is a **transient** impact
   VFX (`MetalDepositCrustVisualiser.cs:85-92`), while `shotPoints`/`exploded`
   (`MetalRockCrustStateData.cs:14,16`) are **persistent state** replayed via
   `InitialiseVisuals:94-106`, which calls the client's own late-join API
   `SimulatePastShot`. **Store state; relay events live and never replay them.** Replay
   the events and a late joiner sees five explosions in one frame.
4. **Omitting depleted nodes.** There is no `RemoveEntityOp`, but depletion is state-based
   anyway. A destroyed node must **stay in the registry and be spawned for the late joiner
   in its destroyed state**. Skip it and late joiners see intact trees where everyone else
   sees stumps. **The most counter-intuitive rule here.**
5. **Fixed point.** Store Q52.12 longs (rule 13): world (900,−120,0) is
   `{3686400,−491520,0}`. A metres registry mis-sites every node by 4096×.

## RELIABILITY — correct today, but BY ACCIDENT
`MirrorSendPolicy.RelayReliabilityFor` (`:160-166`) is an **allowlist of two unreliable
ids** (190602, 1073); every node id falls through to Reliable. Nothing breaks on day one.
Four things to add:
- **(a) Encode the reason, not the id** — two named sets, asserted disjoint:
  `SupersededEveryTick = {190602,1073}` vs
  `CumulativeState = {12283,1016,1099,1032,1035,1036,2103}`. The failure this guards is
  someone adding a salvage-progress id to the unreliable set without noticing
  `shotPoints` is a **list that GROWS** (`MetalRockCrustStateData.cs:14`), not a value
  that is overwritten.
- **(b) Reliable is necessary but NOT sufficient.** ENet guarantees delivery to a peer
  *that has the entity* — nothing about a peer whose node AddEntity was silently dropped
  (`DispatchEventHandler.cs:374-378` has no else branch). **The structural fix:**
  > mutate the registry FIRST, then relay; and **never relay to a peer that has not been
  > sent the node.**
  That converts "lost packet = permanent divergence" into "that peer will receive current
  state on checkout". **Stronger than any reliability flag.**
- **(c) Include the origin** — fan out to every peer holding the node, harvester included.
- **(d) Channel coupling (flag, not fix):** node updates share channel 4 with the
  unreliable transform stream. Sequencing is per-channel, so a lost reliable node packet
  delays subsequent **reliable** traffic (a joiner's AddComponent, the 1081 fan-out).

## AUTHORITY — server-authoritative by accident, needs pinning
`SendAuthorityChangeOp` has exactly one call site and it is already gated on
`Players.Owns` (`:622-623, :673`), which returns false for every node forever.
**Nodes are server-authoritative by construction under the existing gate — but only as an
emergent consequence of rule 6. Pin it with a test.**

Withholding authority costs nothing visually: no 1036 grant → no writer →
`TreeFsimVisualizer` never enables. Reader-side visualizers are unaffected
(`TreeClientVisualizer.cs:7,12-16` needs two Readers; `Salvageable.cs:9-10` needs one).

**But nothing stops a MODIFIED client writing node state.** `FinishAndSend` does not check
authority (`MetalRockCrustState.cs:417-427`); the gate is upstream at `[Require]`
resolution. And the server would not reject it, because `InitAndSerialize:555-583`
registers every seeded component into `ComponentMap`, so
`ComponentUpdateManager.cs:129`'s existence check **passes**. It is a no-op today only
because no handler is registered for node ids — **luck that expires the moment harvesting
adds one.** Required in both places:
1. Before `HandleComponentUpdate` (`:707`) — drop updates where `!Players.Owns(sender, entityId)`.
2. Before the relay (`:712`) — never forward a client-originated node update. Without
   this **one modified client repaints the world for everyone with one packet.**

## SCALE — steady state is noise; the SPAWN path breaks
Relay: M players harvesting at `period` 0.5 s → 2M² packets/s; **at M=8 that is 128/s**,
against a baseline already driving "hundreds of loop iterations a second". Noise.

It breaks, in order:
1. **Logging — the same self-inflicted stall at N× volume.** The per-component seed path
   is **still unconditional `Console.WriteLine`**: `SendOPHelper.cs:87,96,113`,
   `ComponentsSerializer.cs:117,540`. N=200 nodes × ~6 components = **1,200 synchronous
   journald writes at every join**, on the ENet thread, plus every resend. **The most
   likely way node spawning re-breaks movement. 20 lines to prevent. Do it first.**
2. **Join burst** — the whole batch flushes in one loop iteration and arms 3 resends →
   **4N reliable sends per joiner**, 800 at N=200, each a prefab instantiation in one
   client frame. Needs `MaxPerFlush`.
3. `InitAndSerialize` linear-scans 443 vtables per component (`:45-47`) — N×k×M scans at
   join, on the ENet thread. A dictionary makes it free.
4. **`GameState.ComponentMap` is never cleaned** — `ForgetPeer` (`:67-108`) cleans
   playerState, clientSetupState, Schedule, Appearances, PeerIdentity and **never touches
   ComponentMap**. Leaks ~40 native refs per disconnect today; ×N with nodes.

## THE DESIGN — pure policy + thin glue (house pattern)
**Pure, in `WorldsAdriftRebornGameServer.Multiplayer/` (which has NO references by design):**
- `NodeRegistry.cs` — `NodeSnapshot` of primitives only (no gencode, no ENet): entityId,
  kind, prefab, Q52.12 position, islandEntityId, parentEntityId, isDestroyed, sectionMask,
  health/maxHealth, shotPoints list, exploded. Plus `MetresToFixed`.
- `NodeSpawnPlan.cs` — `Plan(nodes, joiner, alreadyHas)` → intents + asset keys.
  **AddEntity only**, parent-first, deduped by asset.
- `NodeRelayPolicy.cs` — the two named sets, `MayClientMutate`, `MayReceiveNodeUpdate`,
  `SeedComponentsFor(kind)`.
- Changes: `MirrorIntent` + prefab/context (defaulting to the Traveller constants so the
  player path is byte-identical); `MirrorSchedule.Park` first-seen per **(peer, assetKey)**
  — today it is per-peer (`:143`), so **a batch with two prefabs requests only the first**;
  plus `MaxPerFlush`.

**Thin glue, in the server project:** `NodeComponentSeeder` (the ONLY place gencode meets
node state), the `InitAndSerialize` early branch, `RelayToOtherPlayers` gaining `entityId`,
`NodeRelay.Broadcast`.

**`MirrorSchedule` is already generic and reusable unchanged** — it mentions neither
players nor Traveller, and its two hard-won properties (real clock; `Forget` empties
everything) transfer verbatim. Node spawning *increases* event rate, so the clock fix is a
load-bearing prerequisite.

## THE ACK PROBLEM MATTERS MORE FOR NODES
The ack carries identity (`DispatchEventHandler.cs:358-361` sends AssetType/Name/Context)
and **the server never reads it** — it flushes on any packet on that channel (`:590-593`).
Tolerable for players (one asset in flight); **wrong by construction with several node
prefabs**: the `Tree` AddEntity flushes on the `MetalDeposit` ack, `MakeEntity` asks for a
template never prepared, and the AddEntity is **silently lost** (no else branch at
`:374-378`). Design the intent to carry an `AssetKey` now so the fix is one line later.

## TESTS WORTH WRITING (house pattern, cf. MirrorSendPolicyTests)
`A_node_keeps_the_same_entity_id_for_every_joiner` (the one that matters most) ·
`A_shot_is_APPENDED_to_shotPoints_not_replacing_them` ·
`A_destroyed_node_stays_in_the_registry_so_late_joiners_see_the_stump` ·
`Position_is_stored_in_Q52_12_and_900m_is_3686400` ·
`Every_distinct_prefab_gets_its_own_asset_load_request` ·
`A_deposit_graph_is_planned_parent_first_so_depositId_resolves` ·
`The_unreliable_set_is_exactly_the_two_superseded_streams` (asserts the SET, so adding a
third forces an argument) · `A_node_update_goes_to_every_peer_INCLUDING_the_harvester` ·
`A_client_may_never_mutate_a_node_component_even_on_an_entity_it_owns`.
**Regression guard:** all 1,520 existing test lines must pass **unmodified** — that is the
proof the player path is untouched.

## ORDERED PLAN
| # | What | Cost | Testable how |
|---|---|---|---|
| 0 | Per-component logging → `ServerLog.Trace` | 0.5 d | count log lines at join |
| 1 | `NodeRegistry` + Q52.12 **and** per-entity `InitAndSerialize` | 1 d | **xUnit, no game** |
| 2 | Generalise spawn: prefab on intent, per-asset first-seen, `MaxPerFlush` | 1 d | xUnit + 1,520 existing lines green unchanged |
| 3 | `NodeSpawnPlan` + glue; spawn one hardcoded node after the island | 1 d | **two clients see the same rock; a third joining sees it too** |
| 4 | Node relay + `NodeRelay.Broadcast` + extended reliability | 1 d | **server timer appends a shotPoint every 10 s; both crack together; a late joiner sees it cracked** |
| 5 | Ownership hardening; pin "no node ever gets an AuthorityChangeOp" | 0.5 d | xUnit |
| 6 | Parse the asset-load ack and match the flush | 1 d | only needed once >1 node prefab is in flight |

**Everything through step 5 needs NO harvesting** — the driver is a server-side timer.
That is deliberate: it decouples "does state reach every client, including late ones" from
"can the beam fire", which is a separate and larger prerequisite.

## COULD NOT DETERMINE
**The `RequiredComponents` set of any node prefab** — computed at runtime from prefab
visualizers (`EntityInterestedComponentsUpdater.cs:101`); prefabs are in bundles, not the
decompile. **Must be observed once (step 3).** This gates whether
`failOnComponentInitError: true` aborts a node's whole component send — **one unhandled
id aborts the ENTIRE AddComponentOp for that entity; the node gets nothing, not most of
it.**
Whether channel 5 can be added from C# alone. Whether requests and responses share the
asset channel. Whether 12283 alone renders a crust (`IsInitialised:46` depends on a second
entity). The damage→yield formula. Nothing was executed.
