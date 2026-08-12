# FINDINGS — SHIPS

**Verdict: viable on this client.** The pilot can hold authority over the ship's
motion components and publish, while everyone else dead-reckons. Cost: 3 small
Harmony patches, one authority rule, and scalar (non-physics) state synthesis
server-side. No server-side physics simulation required.

(Recorded by the orchestrator: the research agent was blocked from writing this
file directly, so this is its verbatim report.)

## Q3 (the crux) — ship motion is gated by AUTHORITY, not worker type

- `SSPDeadReckoningBehaviour` — the publisher — requires `SSPPredictedMotionStateWriter`
  (1130) + `TransformStateWriter` (190602) and carries **no `[WorkerType]` attribute**
  (`acs/Bossa.DeadReckoning.Improbable/SSPDeadReckoningBehaviour.cs:18-24`).
  It publishes at `:135-136` (190602) and `:161-162` (1130).
- Writers are injected **only** by
  `EntityVisualizers.OnAuthorityChanged(..., bool hasAuthority, ...)`
  (`acs/Improbable.Unity.Internal/EntityVisualizers.cs:235-259`). No platform check.
- The receive half already works in the stock client: `SSPDeadReckoningVisualizer`
  needs readers only, is added on **every** platform (`acs/ShipPreprocessor.cs:112`),
  and at `:83-93` implements exactly the split we want — *no authority ⇒
  `PathFollower.enabled = true`*. It even handles pilot handover
  (`:110-115`, `IgnoreControlPointsUntil`).

Only **three** classes in the ship physics stack are `[WorkerType(UnityWorker)]`:
`ShipControlVisualizer` (`acs/Assets.Visualizers/ShipControlVisualizer.cs:16`),
`SelfRighteningVisualizer` (`:8`), `ShipAbandonedBehaviour` (`:13`).
No attribute ⇒ compatible with all platforms
(`acs/Improbable.Unity.Assets/BehaviourWorkerCompatibilityCache.cs:48`).

### Three client patches
- **A** — Harmony postfix on `ShipPreprocessor.ExportProcess` to also add the
  `UnityWorker` list (`acs/ShipPreprocessor.cs:50-73, 108-111`) when running as
  client. Runs at runtime (`PrefabCompiler.cs:26-41` ←
  `PreprocessingGameObjectLoader.cs:17-31`); the repo already does this manoeuvre
  for the player prefab
  (`WorldsAdriftReborn/Patching/SpatialOS/WorkerSpecificAssetDatabaseTemplateProvider_Patch.cs:40-45`).
  Do **not** just run `PrefabCompiler(UnityWorker)` — it destroys all
  ParticleSystems and client visuals (`PrefabCompiler.cs:28-36, 43-53`).
- **B** — postfix `BehaviourWorkerCompatibilityCache.IsCompatibleBehaviour`
  whitelisting exactly those three types.
- **C** — postfix `ShipPhysicalityVisualizer.ClientDynamic()`, a hardcoded
  `return false` (`acs/ShipPhysicalityVisualizer.cs:71-74`), to return
  `FSimDynamic()`'s expression (`:56-59`), keyed on
  `_motionState.IsAuthoritativeHere`. Non-authoritative clients then stay
  kinematic automatically. Do **not** patch `WorldsAdrift.IsFSIM` (100+ call sites).

### Authority grant
Ship entity: 1130 + 190602 to the pilot. Player entity: 1111 to its owner.
`SendAuthorityChangeOp` is already generic over entityId
(`Networking/Wrapper/SendOPHelper.cs:229-242`).

### Server must synthesise (scalar bookkeeping, no physics)
No client code writes these — only 1111 and 1130/190602 exist client-side:
**1113** ShipControlState (near-identity of 1111), **1116** ShipEngineState,
**1258** ShipLiftState (0 lift ⇒ ship can't hold itself up),
**1257** ParentingMassAdderState (0 mass ⇒ pathological forces),
**1109** PilotState. Plus a relay rule copying the pilot's player-entity 1111 onto
the ship's 1111.

## Q4 — players do NOT ride ships via TransformState.Parent

They ride via **`ClientAuthoritativePlayerState` (1073)** — fields `relativeTo` /
`positionRelative` / `rotationRelative` / `relativeBias`, blended in
`PlayerVisualizer.FixedUpdate:115-130`. The hierarchy branch (`:131-135`) is a
*mutually exclusive alternative* (`!Parent.HasValue` at `:115`). The sender assigns
`relativeTo` from a **ground raycast**, not a trigger volume
(`acs/ClientAuthoritativePlayerMovement.cs:336-353`, ship detection at `:56`).

We already grant and seed 1073, so this is mostly *unblocking*:
1. `PlayerVisualizer_Patch.cs:72` returns `false`, suppressing **both** the ship
   branch and the hierarchy branch. Stop suppressing.
2. **LIVE BUG** — the seed at `ComponentsSerializer.cs:375-385` sets
   `relativeTo = EntityId(2), relativeBias = 1f`, which would drive every remote
   avatar into the ship branch against a bogus entity. Set invalid/0.
3. **LIVE LATENT BUG** — `RemoteRigMover.cs:112-116` applies
   `RemapGlobalToUnityVector` unconditionally; its own header comment at `:21-24`
   promises a Parent guard **that no longer exists**.
4. Ship must have the same entity id on all clients, and must actually move
   (relay 1130).

Ship **parts** do use `Parent` + 190601
(`acs/Assets.Scripts.Visualisers.Ship/ShipPartVisualizer.cs:22-32, 114`) — hull
assembly comes free once that works.

## Q1/Q2 — anatomy and construction

- A ship is **one root entity + one entity per part**, linked by `ShipRootState`
  (8066): `shipRoot: Option<EntityId>` + `isRoot: bool`.
- **The hull is an opaque `byte[]` blob** — `CustomShipHullState.hullData` (1209),
  decoded client-side by `acs/CustomShipFrameVisualizer.cs:34-52` into a `ShipPlan`
  and mesh-generated with **real colliders** (`acs/MeshGenerator.cs:400,407`).
  Format is a plain `BinaryWriter` stream (`acs/ShipPlan.cs:82-132`); smallest legal
  ship is one cell (`MakeDefault()`, `:134-137`). **Designs can be stored and
  replayed verbatim, and one can be synthesised in ~30 lines.**
- Prefabs `ShipFrame01/02` are real and already precached by the live client
  (`UnityClient@Windows_Data/output_log.txt:2571-2583`). **Context is ignored** for
  non-`Traveller` names (`acs/Improbable.Unity.Core/DispatchEventHandler.cs:342-345`)
  — none of the Player/Default trouble.
- **Zero SpatialOS commands in the entire ship-construction flow** (verified across
  all of gencode) — events + component updates only. But the server must *originate*
  reply events (`ShipHullRequestResponse{id, success, ownerId}`) and, critically,
  must flip `GsimShipBlueprintInteractionState.busy` (1274) back to false or **the
  blueprint UI hard-locks after the first click**
  (`acs/PlayerShipBlueprintInteractionBehaviour.cs:76-85`).
- No prebuilt ship designs ship with the client; schematics arrive at runtime as
  JSON over 1097 with `hullData` as a **Base64 string**
  (`acs/SchematicData.cs:139-141, 181`).

## Q5 — the staircase

**A. Static ship you can see and stand on.** AssetLoad + AddEntity `ShipFrame01`
under one shared id, seed ~8 components incl. 1209 with a synthesised blob (plus
`SalvageAndRepairState`, which `CustomShipFrameVisualizer` also requires).
**Zero client patches.** Reuses the island machinery.

**B. Piloted ship.** Patches A/B/C + authority grant on 1130/190602 + relay + the
1111 copy + scalar synthesis. This is where the thesis is proven.

**C. Player-built ships.** Blueprint event round-trip, per-part entities,
persistence of the blob.

## Q6 — dependencies
Stages A and B need **nothing** from persistence, resources, or entity removal.
Stage C needs all three.

## Top risks
1. **`fsimIdHash` echo suppression** — receivers drop control points stamped with
   their own `WorkerId.GetHashCode()` (`SSPDeadReckoningVisualizer.cs:102-105`).
   If our clients share a `WorkerId`, **every client silently drops every ship
   update**. Unverified; check this FIRST.
2. `1130` must be seeded with a *valid* control point or the publisher errors on
   enable (`SSPDeadReckoningBehaviour.cs:48-59`).
3. Did not open a ship prefab to confirm `ShipPreprocessor` survives into the client
   bundle. Evidence strong but indirect. If stripped, Patch A becomes "AddComponent
   the list ourselves" — more code, same outcome.
4. `RemoteRigMover` forces root rigidbodies kinematic — must be scoped to player
   rigs so it doesn't fight `ShipPhysicalityVisualizer`.
5. Unverified: how the client signals a helm interaction (Stage B can bypass with a
   debug command).
