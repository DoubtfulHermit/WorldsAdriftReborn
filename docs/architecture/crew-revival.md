# Reviving crews

**Goal:** restore the retail crew system — invite, accept, boot, leave, slots —
driven by the client's own shipped crew UI, so it behaves as it did in Worlds
Adrift rather than as a Wareborn approximation. Grouped graduation from Haven to
Wilderness is then built ON TOP of a real crew, not a stand-in for one.

## The finding that makes this affordable

The first read said crews were unreachable: every crew action appears in the
schema as a SpatialOS **command**, and the command channel is unimplemented in
both directions —

| Export | State |
| --- | --- |
| `WorkerProtocol_Connection_SendCommandRequest` | `// TODO` stub |
| `WorkerProtocol_Connection_SendCommandResponse` | `// TODO` stub |
| `RegisterCommandRequestCallback` | `// TODO` stub |
| `RegisterCommandResponseCallback` | `// TODO` stub |
| Command ops in `OpList.h` | none exist |

That would have made crew a C++ protocol project before any gameplay appeared.

It is not needed. `CrewClientInterfaceState` (**6901**) exposes the crew actions
as **EVENTS**, not commands:

```csharp
Updater TriggerInvitePlayer(string player_id, string display_name);
Updater TriggerInvitePlayerWithSlot(string player_id, string display_name, int slot);
Updater TriggerLeaveCrew();
Updater TriggerAcceptInvite();
```

`Trigger*` returning `Updater` is the `.FinishAndSend()` component-update path,
and its schema confirms it: `CrewClientInterfaceState.Events` carries lists of
`InvitePlayer`, `InvitePlayerWithSlot`, `BootPlayer`, `LeaveCrew`,
`AcceptInvite`, `RejectInvite`, `SearchPlayer`, `UseCrewBeacon`. That is why
6901 has no data fields at all — it is an event-only component.

Component updates are the one transport this server fully implements, and at
least eight handlers already read client events off them
(`IslandResourceSpawnerClientState_Handler` is the closest model). So the whole
crew action surface is reachable today, through proven machinery.

**The command channel stays unimplemented.** It is still worth building one day
— it blocks other retail systems — but crews do not need it, and this document
does not touch it.

## The protocol surface

| Component | Direction | Carries |
| --- | --- | --- |
| **6901** `CrewClientInterfaceState` | client → server | the eight action events above. No data fields. |
| **6900** `CrewMembershipState` | server → client | `PlayerId`, `Name`, `CurrentCrewLeaderId`, `CrewMembers` (`List<CrewSlot>`), `InvitesReceived` (`Map<string,string>`), `NumSlots`, `BeaconCoolDown`; plus events `FeedbackTriggered` (`CrewManagementFeedback`) and `SearchResultsTriggered` (`SearchPlayerResult`). |
| **6923** `CrewClientState` | server → client | `MemberDetails`, `CrewDetails` — display only. |
| 6924 `AllianceNameState`, 6925 `AllianceAndCrewWorkerState` | server → client | already served as EMPTY STUBS today. |

`CrewSlot`: `PlayerId`, `Slot` (int), `Active` (bool), `DisplayName`.
`CrewManagementFeedback`: `Msg` (string), `Result` (bool) — one line of text and
a success flag, which is the entire error-reporting channel the UI has.

## Identity

Crew membership must key on the durable **character uid**, the same key the
inventory, progression and logout position already use
(`CharacterIdentity.UidFrom`, `InventoryKey.ForCharacter`). Entity ids are
allocated fresh every session and are useless across a relog.

That has one hard consequence: the uid does not arrive until the client
publishes 1088, AFTER checkout. A player whose uid never arrives can be shown a
crew but must never be written into one, exactly as their inventory is
session-scoped and never saved.

## Phases

Each phase is independently valuable and independently testable. Nothing after
phase 1 is worth starting until phase 1's rules are pinned by tests, because the
rules are the part that is easy to get subtly wrong and expensive to change once
a database and a wire format depend on them.

### Phase 1 — the pure crew domain

`WorldsAdriftRebornGameServer.Multiplayer/Crew/`: a dependency-free ledger and
policy. No ENet, no Postgres, no Unity.

- invite / accept / reject / cancel / boot / leave / request-slot
- leader rules: only the leader may boot; a leaving leader promotes the longest-
  standing member, and the crew disbands at the last member
- slot assignment and `NumSlots` bounds
- every rejection returns the feedback line the UI will show

Unit tested against the retail semantics above.

### Phase 2 — persistence

Schema **v6**: `crews` and `crew_members`, keyed by character uid with the same
`ON DELETE CASCADE` discipline as v2/v4/v5. Repository plus integration tests
run against a real PostgreSQL, as v5 was.

### Phase 3 — the wire

- Serve **6900** on each player entity with real membership.
- A **6901** handler reading the eight events, modelled on
  `IslandResourceSpawnerClientState_Handler`.
- Push 6900 updates to every affected member, not just the actor: an invite
  changes two players' state.
- Emit `FeedbackTriggered` for every rejection so the UI explains itself.

### Phase 4 — acceptance

The two-peer harness (`tools/relaybot/run-ship-acceptance.sh`) drives real ENet
peers and is the right place for a crew round trip: peer A invites, peer B
accepts, both observe the same crew, B leaves, A is alone. That gate can be
green before any human opens the client.

Visual acceptance is a separate, human step: does the retail crew UI actually
render and drive it.

### Phase 5 — grouped graduation (separate document)

Only once crews are real. Graduation writes the destination into the existing
`character_positions` table, so "you always return to your Wilderness island"
falls out of work already deployed, and a crew resolves to its leader's island.

## Constraints that bound this work

- **T1 districts must be registered** for graduation to have anywhere to send
  anyone. Production currently runs `WAREBORN_RELEASE_WORLD_DISTRICTS=C6`; the
  12 Tier-1 revival islands are in A2, A3, B2 and B3.
- **Never invent retail behaviour.** Where the decompile is silent, say so in
  the code rather than guessing; the schema above is evidence, the rest is not.
- Crew state is per-character and shared between players, so it is exactly the
  kind of ledger where a wrong ownership rule is a griefing vector. Boot,
  disband and slot changes are authority decisions and belong behind the policy.
