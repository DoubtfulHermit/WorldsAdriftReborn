# THE 2108 DROP — what it is, what caused it, and what it actually costs

**Investigation only.** No server or mod behaviour changed, no game launched,
nothing deployed.

## HEADLINE

1. **2108 is `ScannerToolState`.** Its only reader in the client build is
   `ReplicatedScannerToolVisualizer`, which lives on the **remote-avatar**
   prefab `Traveller_unityclient`.
2. **The coordinator's hypothesis is REFUTED.** That visualizer has **zero
   `[Require]` writers**, so nothing gates it — no grant can switch it on and no
   un-grant can switch it off. 2108 has been in that request since the first
   time two players ever saw each other.
3. **Tonight changed the drop POINT, not the drop.** Before `08fb983` the same
   21-id batch died at **2105** (id 8 of 21). Seeding 2105/2106/2002 moved the
   death to **2108** (id 11 of 21). Net effect on that batch: nil.
4. **The dropped batch is NOT the player's own inventory** — it is another
   player's remote avatar. Every player's own batch (the 37-id and 43-id ones,
   where `1081` and `1280` actually live) **succeeded**.
5. **Seeding terminates, but needs five seeds, not one:** 2108, then 1249, 1096,
   1023, 1092 are all unseeded in the same batch. After those the closure is
   provably closed at 21 ids — no visualizer cascade.
6. **The real TAB failure was found, and it is unrelated:**
   `LorePiecesCollectorVisualizer.GetKnownPieces()` NREs inside
   `LogbookUI.ProtectedInit()` while `CharacterSheetScreen` is constructed at
   world entry.

## 1. WHAT 2108 IS, AND WHO WANTS IT

`docs/component-ids.md:334` — **2108 = `ScannerToolState`**. Exactly two classes
in the decompile touch it:

| class | prefab | `[Require]` | role |
|---|---|---|---|
| `ScannerToolBehaviour` | `Traveller@Player_unityclient` (own player) | `ScannerToolStateWriter` | **writer** |
| `ReplicatedScannerToolVisualizer` | `Traveller_unityclient` (remote avatar) | `ScannerToolStateReader` | **reader** |

**Only readers enter an interest list.**
`VisualizerMetadataLookup.InitializeMetadataForMember`
(`acs/Improbable.Unity.Visualizer/VisualizerMetadataLookup.cs:135-159`) tests
`IsWriter` **first**; a `[Require]` field whose type carries `[WriterInterface]`
is filed as a writer and its `[ComponentId]` never reaches
`visualizerRequiredReaderStateIds`. So `ScannerToolBehaviour` contributes
nothing to interest — the reader on the remote prefab is the only source.

## 2. WHAT MADE THE CLIENT ASK FOR IT

`acs/Improbable.Unity.Internal/EntityInterestedComponentsUpdater.cs:96-100`:

```
interest = entityObject.Components.InterestedComponents      // STICKY
           UNION entityObject.Visualizers.RequiredComponents  // DERIVED
```

* `InterestedComponents` grows one id at a time in `SpatialOsComponentBase.Init`
  (`:99`) — every component the server has ever actually delivered onto that
  entity, never re-derived. This is why a client's own request grows 37 -> 43 as
  the server answers it.
* `RequiredComponents` is `CalculateRequiredComponents`
  (`EntityVisualizers.cs:312-330`): for each extracted visualizer, **if**
  `!IsMarkedAsDisabled` **and** `AllFieldWritersInjected`, add its reader ids.
  That gate is real — a new writer grant genuinely can switch a visualizer on.

**But it cannot have done so here, for two independent reasons.**

**(a) `ReplicatedScannerToolVisualizer` has no writers at all.**
`AllFieldWritersInjected` walks `GetRequiredWriters(type)`, which for this class
is the **empty list**, and `AllFieldsInjected` over an empty list returns `true`
unconditionally (`EntityVisualizers.cs:375-392`). Its reader 2108 is in
`RequiredComponents` from the first call — before any component has been
delivered and before any authority has been granted.

**(b) `ExtractedVisualizers` is fixed for the entity's whole life.** Assigned
once, in the `EntityVisualizers` constructor (`:56`), from
`GetComponentsInChildren<MonoBehaviour>(includeInactive: true)`. Nothing
anywhere assigns it again. Runtime-spawned equipment cannot add readers either.

### The prefab evidence, measured not reasoned

`docs/research/loop/data/prefab-component-census.tsv` only walks each prefab's
**root** `m_Component` list — which is why `req_player.tsv` shows 2108 as a
*writer* and shows no reader for it. The client walks the whole hierarchy, so
the census was re-run recursively (`data/player_hierarchy_census.py` ->
`data/traveller-hierarchy-census.tsv`, 1,617 rows, depths 0-8).

There are two client player prefabs:

```
Traveller@Player_unityclient   your OWN avatar   (PlayerMultitoolVisualizer,
                                                  ScannerToolBehaviour, InventoryVisualiser)
Traveller_unityclient          EVERYONE ELSE'S   (ReplicatedMultitoolVisualizer,
                                                  ReplicatedScannerToolVisualizer)
```

Neither replicated visualizer exists anywhere in the `Traveller@Player`
hierarchy. **The client log confirms it at runtime**: the mod's own rig dump
lists `ReplicatedScannerToolVisualizer` exactly once, on `'Traveller 4'` (the
remote player), never on the local rig.

### The batch, identified

`data/interest_closure.py` resolves every `[Require]` field in a prefab
hierarchy to reader/writer + component id. For `Traveller_unityclient` the
unconditional reader set is:

```
1023 1073 1077 1081 1086 1088 1092 1096 1098 1109 1249
2002 2105 2106 2108 4444 6910 6924 6925 190601 190602
```

The logged batch (`server.log:907`) is the **same 21 ids**. (The script prints
1160 where the log has 1077: `component-map.tsv` maps both to "HealthState";
the player's is 1077, the Creatures one is 1160 — the same trap
`TeleportPolicy.cs:101-103` already documents.)

The only gated reader on that prefab is 1247 via `FSimShotRequestProcessor`,
needing writer 1248 — an authority we never grant, and never would on someone
else's entity.

**So the 21-id batch is the remote-avatar prefab's fixed, ungated reader
closure. It does not depend on anything we grant. It never has.**

## 3. WAS IT CLEAN BEFORE TONIGHT? NO — IT FAILED THREE IDS EARLIER

`08fb983` ("One choppable tree on Haven") added 2105/2106/2002 both to
`AuthoritativeComponents` and as seed branches:

```
git show 08fb983^:.../ComponentsSerializer.cs | grep -cE 'componentId == (2105|2106|2002)\b'  ->  0
grep -cE 'componentId == (2105|2106|2002)\b' .../ComponentsSerializer.cs                      ->  3
```

The interest list is derived from the prefab and is independent of seeds, so
before `08fb983` the client asked for the **same 21 ids** and the send hit
**2105** at position 8 with no branch. Same log line, different number.
Corroborated by `docs/research/loop/data/harvest_wire_trace.tsv`, which recorded
2105/2106/2002 as "NOT GRANTED, no seed branch" before the tree work.

**Tonight's grant did not break this batch. The batch was already being dropped
in its entirety, three ids earlier.**

## 4. DOES SEEDING 2108 TERMINATE, OR CASCADE?

**It terminates. But 2108 alone fixes nothing — four more unseeded ids are
queued behind it in the same batch.**

| id | component | seed branch? |
|---|---|---|
| 1073, 190602, 1081, 1088, 1086, 6924, 6925, 2105, 2106, 2002 | the first ten, all serialize | yes |
| **2108** | ScannerToolState | **NO** |
| **1249** | PlayerPistolState | **NO** |
| **1096** | PistolState | **NO** |
| 6910, 4444, 1109, 1077 | | yes |
| **1023** | MusicalInstrumentState | **NO** |
| **1092** | RespawnState | **NO** |
| 190601, 190602, 1098 | | yes |

Seed 2108 and the batch dies at 1249, then 1096, then 1023, then 1092. That is
whack-a-mole — but **inside one batch**, not a runaway cascade, and exactly five
moles deep.

**Why it cannot cascade past that.** New ids enter interest by only two routes,
both closed: `InterestedComponents` can only contain ids the server actually
delivered (a subset of the 21 already requested), and `RequiredComponents` only
grows when a visualizer's writers are injected — which happens on authority, and
the server sends `AuthorityChangeOp` only on `isSendersOwnEntity`. An observing
client never holds authority on someone else's avatar.

No class in the remote prefab's reader set carries `[DontAutoEnable]`, and
`disabledVisualizers` starts empty. The 21-id closure is complete and stable.

**What the five seeds would switch on** — all five are inert on enable:
`ReplicatedScannerToolVisualizer` (2108, seven event subscriptions),
`PlayerPistolVisualizer` (1249+1096, subscriptions only),
`PlayerMusicalInstrumentVisualizer` (1023, subscriptions only), and
`NonLocalPlayerRespawnVisualizer` (1092) whose `OnEnable` has an **empty body**.
That last is *not* `RespawnVisualizer`, the one `TeleportPolicy.cs:96-100` warns
about — that one is on the own-player prefab and needs writers 1093 and 1072
which we never grant.

The one genuine hazard is the **replay-on-subscribe** foot-gun that
`TeleportPolicy` already documents for 190607: generated `*Updated` handlers
fire with the current value the instant a subscriber attaches. A 2108 seed must
be chosen so replaying it is a no-op — `isVisible=false`, `isLiftMode=false`,
`isLifterPowered=false`, `attachLocation` = invalid target
(`ScannerToolBehaviour.cs:19` names the `NoRelativeLocation` constant to copy).
Same discipline for the other four.

## 5. OPTIONS, AND WHICH TO TAKE

**(a) Seed 2108.** Correct as far as it goes, but on its own buys **zero**
observable change — the batch just dies at 1249. Only the full five restore
remote avatars, each with the replay trap.

**(b) Don't grant 2105/2106/2002 until harvesting is wired.** Does not help. The
batch dropped *before* those grants, at 2105, for the same reason.
**Reject — a fix aimed at a cause that has been ruled out.**

**(c) Split the batches by concern.** Right instinct, wrong lever *here*: this
batch is one prefab's reader closure and is already single-concern. Where it
genuinely applies is the **own-player** path
(`WorldsAdriftRebornGameServer.cs:1033-1064`), which runs `injectedEarly`, the
client's request, `InjectedComponents` and the authority grant as four
sequential `failOnComponentInitError:true` sends that `continue` out of the
whole setup — so an unseeded id anywhere in a 43-id request costs a player their
authority grants and their loading screen. Worth breaking up on its own merits.

**(d) Stop passing `failOnComponentInitError:true` on the non-setup path**
(`WorldsAdriftRebornGameServer.cs:1087`). **Take this one first.**

Best-effort is not a degradation here, it is the SDK's own semantics. A reader
field is injected only when its component arrives, and a visualizer activates
only when **all** its required fields are injected
(`EntityVisualizers.UpdateActivation:357-367`). Delivering 17 of 21 leaves
exactly the visualizers whose readers are missing switched off and every other
one working — which is what real SpatialOS does when an entity simply lacks a
component. Delivering 0 of 21 leaves *all* of them off. All-or-nothing is
strictly worse at every id count except 21.

The argument that put `true` there was diagnosability — but that argument is
about the **logging**, not the **dropping**, and the logging is unconditional
already: `[interest] ... wants N component(s)` prints the whole requested list
before anything is attempted, and every miss prints
`[error] failed to initialize component NNNN`. Keep both lines and send the 17.

**Recommendation: (d) now, (a)'s five seeds after, (c) when someone is next in
the own-player setup path.** Do not do (a) without (d), because (a) leaves the
same cliff-edge for the next unseeded id anybody's client asks for.

## 6. IS ALL-OR-NOTHING THE DEEPER BUG? YES — AND THE PREMISE NEEDS CORRECTING

Yes, unambiguously: one unknown component id costs an entire entity, and the id
that breaks it is chosen by the client's prefab, which we do not control and
cannot enumerate ahead of time.

**But the brief's symptom does not match.** It said the dropped 21-batch
contains `1081 InventoryState` and `1280 WearableUtilsState`, and that this is
why TAB does nothing.

* **1280 is not in the 21-batch at all.**
* **The dropped batch is somebody else's avatar.** Peer `0x7ffffe90f720` =
  entity 1, peer `0x7ffffe90f8e8` = entity 4. Each client's **own** entity is
  served the `Traveller@Player` batches — 2 ids `[1109,1207]`, 4, 19 + authority
  grant, then 37, then 43 — and **every one of those succeeded**, `1081` and
  `1280` included. The 21-id batch appears **only** when a client asks about the
  *other* player. Losing it costs the observer's view of the other player: no
  name, no appearance, no gear, no tool visuals.

### The real cause of the TAB failure

`CharacterSheetScreen` (the TAB window) **threw during construction** at
`InGameState` entry, and the exception was swallowed by `UIErrorHandler`:

```
client-hermit.log:2408  System.NullReferenceException
                :2409    at LorePiecesCollectorVisualizer.GetKnownPieces ()
                :2410    at LoreUI.RefreshLore (Boolean refreshSelected)
                :2412    at Travellers.UI.PlayerInventory.LogbookUI.ProtectedInit ()
                :2439    at ...CharacterSheetScreen:ProtectedInit()
                :2442    at Travellers.UI.Framework.HUDState:CreateScreens(...)
                :2446    at GameStateMachine.InGameState:OnEnterState()
```

Identical repeat in the second session. `ProtectedInit()` aborts partway, so the
screen never finishes initialising. In session A the window opened **once** and
immediately hit `[ERROR] [UI] [CraftingStationData] Schematic is null`; in
session B `ShowInventoryTab` never appears at all — the window never entered.
140 `KeyNotFoundException`s from `GearWearablesVisualizer.UpdateActiveWornItemsHealths()`
start ~23 s after that single open.

`GetKnownPieces()` is a one-liner, `return _serverState.KnownLore;`, and our
1240 seed passes a non-null list — so the NRE is `_serverState` itself being
null, i.e. the visualizer had not been injected when the UI constructed the
character sheet. **That is an ordering race between UI construction and
component delivery, not the 2108 drop.**

Aggravating factors visible in the same log: the mod's `ReferenceDataFakeLoad`
Harmony patch resolving schematics to null, and the dispatcher double-adding
every component on the local entity
(`InvalidOperationException: Component InventoryState added to entity 1, but it
already exists`).

Also noted while the topic was open: **entity 2 (the Haven ship) drops its 26-id
batch on `8062`**, three times. Same class of bug, different id, not
investigated.

## VERIFIED VS NOT VERIFIED

**Verified — read from code, shipped assets, or the logs:** 2108's identity and
its single reader/writer · interest = sticky ∪ derived, readers only ·
`ExtractedVisualizers` assigned once · the replicated visualizers live on the
remote prefab, confirmed by asset census *and* the runtime rig dump · the logged
21-id batch equals that prefab's unconditional closure, id for id · no seed
branches for 2105/2106/2002 before `08fb983` · 2108/1249/1096/1023/1092 unseeded,
the other sixteen seeded · every player's own batch succeeded including 1081 and
1280 · the non-setup path passes `true` and the setup path `continue`s out ·
1077 vs 1160 are different components · `CharacterSheetScreen.ProtectedInit()`
threw via `GetKnownPieces()`.

**NOT verified:**
* **That five seeds actually complete the batch.** Derived from code; nothing
  was run. The next unseeded id is only knowable from the wire.
* **That the proposed seed values are inert.** Replay-on-subscribe risk reasoned
  from each visualizer's `OnEnable`, not observed.
* **That `_provider.ScannerTool` is non-null when the replicated visualizer
  enables.** A null there turns a seed into an NRE.
* **That `_serverState` (not `KnownLore`) is the null in `GetKnownPieces`.**
  Inferred from the one-line body plus our non-null seed. The fix is not
  designed here.
* **That best-effort delivery is harmless in practice.** The SDK argument is
  from `UpdateActivation`; this server does not reproduce SpatialOS exactly.
* **Whether the entity-2 / `8062` drop shares a cause.** Noted, not investigated.

## REPRODUCING

```
cd docs/research/diag/data
uv run --with UnityPy python player_hierarchy_census.py traveller > traveller-hierarchy-census.tsv
python3 interest_closure.py traveller-hierarchy-census.tsv Traveller_unityclient
python3 interest_closure.py traveller-hierarchy-census.tsv Traveller@Player_unityclient
```

**Confidence.** ~95% that 2108 comes from the remote-avatar prefab and was not
caused by the multitool grants — three independent lines agree. ~90% that the
closure terminates at 21 ids after five seeds. ~90% that the TAB failure is the
`LorePiecesCollectorVisualizer` NRE (the stack trace is explicit), but only ~60%
on *why* `_serverState` was null, which was not chased.
