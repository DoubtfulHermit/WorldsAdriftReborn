# Testing: what the suite covers, and what it does not

Everything in `docs/multiplayer.md` was learned by launching two game clients
and looking at them. This suite exists so the parts of it that are *pure logic*
stop needing a human and two clients. It does not replace that human. Read the
"still needs two clients" list at the bottom before you trust a green run.

## Running it

```sh
dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests
```

No Wine, no game install, no `DevEnv.targets`, no network. The library under
test (`WorldsAdriftRebornGameServer.Multiplayer`) deliberately references
nothing — not ENet, not the game's assemblies, not the server project — which is
what makes this runnable natively on Linux in under a second.

The full build gate, which does need `DevEnv.targets` pointing at a real game
install:

```sh
dotnet build WorldsAdriftRebornGameServer -c Release   # net6.0 server
dotnet build WorldsAdriftReborn         -c Release     # net35 BepInEx mod
dotnet test  WorldsAdriftRebornGameServer.Multiplayer.Tests
```

Note that building the mod writes straight into
`$(WorldsAdriftGameDir)BepInEx/plugins/WorldsAdriftReborn/`. To build without
touching a live install, override the output:

```sh
dotnet build WorldsAdriftReborn -c Release -p:PluginOutputDirectory=/tmp/modout/
```

Neither multiplayer project is in `WorldsAdriftReborn.sln`; name them
explicitly as above.

## Multiplayer ship acceptance ladder

The Colin-session failures are no longer represented only by small, unrelated
unit tests. `Ship/TwoPeerShipAcceptanceTests.cs` runs their complete sequence as
one deterministic headless scenario:

1. two independent peers check out the same hull, deck, helm and sail;
2. the first player pilots for 120 authoritative 240 ms frames;
3. every frame has one authority generation/sequence and every member is gated
   behind the hull timeline;
4. a raw `relativeTo=-1`, bias-zero collider seam is held while the canonical
   aboard relationship survives, preventing the remote avatar from switching
   to stale world coordinates and trailing or leading the ship;
5. helm authority transfers to the second player, the new generation restarts
   at sequence one, and delayed input from the old generation is rejected;
6. one peer unloads without changing the other peer's checkout; and
7. the returning peer receives root first and all current members afterwards.

Run that gate and its supporting policies with:

```sh
dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests -c Release \
  --filter 'FullyQualifiedName~TwoPeerShipAcceptanceTests|FullyQualifiedName~ShipDomainTests|FullyQualifiedName~AboardRelayPolicyTests|FullyQualifiedName~ShipDomainInterestPolicyTests|FullyQualifiedName~FlightSessionTests'
```

This is tier 1: deterministic authority, timeline, membership and checkout
acceptance. It catches the server regressions that produced a split hull,
stale pilot input, a floating passenger, five-second steering wake-up and a
peer-specific missing ship.

Tier 2 is a real-wire acceptance gate. It builds the current native server and
CoreSDK shim, creates a fresh temporary world containing one disposable hull
and mounted helm, starts that server on an alternate UDP port, then connects two
real ENet peers using the production protobuf envelopes and generated component
codecs:

```sh
tools/relaybot/run-ship-acceptance.sh       # default isolated UDP 17779
tools/relaybot/run-ship-acceptance.sh 18779 # optional alternate port
```

The runner refuses an occupied port and never reads the operator's world state.
It proves join/ack/authority, Man -> 1111 -> 1130 flight, root-plus-mounted-member
wakes on both peers, the aboard contact-seam hold, helm handoff with a stale old
pilot write, peer-independent checkout, channel-5 member-first/root-last removal,
and asset-requested root-first/member-last re-entry. Spatial interest is enabled
explicitly in the disposable process, so this also prevents a test from silently
passing in the fail-open "send everything" mode. Logs and temporary data stay in
`tools/relaybot/run/`.

Tier 3 is deliberately small and visual: two Unity clients confirm camera/IK,
animation and interpolation presentation. Relaybot cannot execute Unity's
`PlayerVisualizer`, `PathFollower`, physics or rendering, so it cannot prove
that interpolation looks smooth on screen. Once tiers 1 and 2 pass, a session
with Colin is confirmation of presentation, not the first time the protocol or
state machine is exercised.

## The relay soak gate, and its two questions

```sh
tools/relaybot/run-soak.sh 10 7807          # ten minutes on an isolated port
SOAK_FAUNA=1 tools/relaybot/run-soak.sh 10 7807   # production world + fauna
```

Two headless bots join, circle the spawn and measure the end-to-end staleness of
each other's relayed movement. The run then answers **two independent
questions**, and both must pass:

**1. Did it get worse while it ran?** End-window minus start-window median
(`drift`) and a least-squares trend, both against 20 ms. This is the original
check and it is unchanged. It exists for the pathology it was built for: a queue
that drains as fast as it fills, so the packet RATE stays flat while its contents
age. Failing it prints `GROWING`.

**2. Is the level defensible at all?** Failing this prints `REGRESSED`. It was
added on 2026-08-19 after a soak reported a confident `FLAT` at 93.3% delivery
and a median staleness of a whole emit interval, sitting next to runs of the same
harness at 100% and sub-millisecond. A run that starts bad and stays bad is
perfectly flat, and merged content lands as a *step*, not a slope — so the drift
check, on its own, cannot gate content at all.

It judges two numbers, and deliberately not the staleness percentiles. Which side
of the emitter's grid a sender's publishes land on is decided when that peer
joins and then holds for the whole session, and it moves the percentiles by a
whole emit interval with no code change: repeated runs on one unchanged tree
produced overall medians of 0.3 ms and 50.4 ms with nothing else different. A
percentile ceiling would be a coin toss wearing a threshold's clothes. The two
gated numbers are the ones that state a contract instead:

- **missed ticks** — the share of delivered samples that waited longer than one
  whole emit interval. The emitter's contract is that a sample waits at most
  until its next tick, so this should be ~0. Ceiling 5%
  (`SOAK_MISSED_TICK_CEILING_PCT`).
- **delivery** — the share of published samples that arrived at all. The bots
  publish at 18 Hz into a 20 Hz emitter, so every sample has a slot of its own
  with two a second to spare; one that never arrives was coalesced away by an
  emit window that held two publishes. Floor 97% (`SOAK_DELIVERY_FLOOR_PCT`),
  which sits below the measured 99.6–100% spread of good runs and well above the
  93.3% that went unnoticed.

> **This check is currently RED on roughly two runs in five, and that is the
> finding, not a flaw in the check.** The relay settles at join into one of two
> states and stays there: either it forwards a position within a millisecond, or
> it holds every position for one whole emit interval first — 50 ms, half the
> client's entire 100 ms interpolation budget — while still emitting at exactly
> 20 Hz with no drops and no cadence skips. Measured server-side, the pending
> position's age is ~0.03 ms in the good state and ~50.2 ms in the bad one
> (`WAREBORN_RELAY_TRACE=1`). It reproduces on trees months old, so it is not
> anything that merged; it is an open defect this gate exists to make visible.
> Until it is fixed, `SOAK_MISSED_TICK_CEILING_PCT=100` disables *only* that half
> if an unrelated change has to be gated — the drift check, the delivery floor
> and the baseline comparison all still run.

On top of those absolute limits the run compares itself to a **recorded
baseline**, `tools/relaybot/baselines/soak-levels.json`, keyed by world recipe
(`haven-spawn`, `tier1-island`) because two different worlds are not comparable.
That is what catches a cost that is real but still inside the contract — "409
more entities took delivery from 100% to 97.5%" breaks no limit and should still
be argued about rather than absorbed. Re-recording is always explicit
(`SOAK_WRITE_BASELINE=1`) and the file is committed, so moving the bar is a diff
a reviewer sees instead of a drift nobody notices. A missing baseline prints a
line and steps aside; the absolute limits still judge.

The judgement itself is pure and unit-tested in `SoakLevelPolicyTests` — the
harness only measures and prints. A threshold nobody can unit-test is a threshold
that quietly stops meaning anything.

**Reading a failure.** Two server-side numbers say whether the server or the host
is responsible, and neither needs a bot:

- `[relay-stats] ... cadenceSkips=N` counts emit intervals that went by without
  the main loop coming back at all. Rising is the server falling behind; flat
  while delivery is short is something else.
- `WAREBORN_RELAY_TRACE=1` logs the first 400 emits with the gap since the
  previous one and the **age of the position it carried**. That age is the whole
  question: ~0 means the relay forwards what it just received, ~50 ms means it is
  in the state described above, and a rising age with a flat gap is the original
  "rate flat, contents ageing" pathology the drift check watches for.

## Looking at the weather walls (`WAREBORN_WALLS`)

There is no headless check for this one and there cannot be. Relaybot proves the
44 entities and their two components arrive; **only a Unity client on a real GPU
can prove a wall is drawn**, because everything downstream of `1204` —
`WeatherTextureGenerator`, `CmdBufClouds`, the storm renderer swap, the debris
emitters, the ambient bolts — is client-side rendering the harness cannot
execute. So this is a look-at-it script, ordered cheapest-first.

**Start the server with walls on.** Nothing else changes; with `WAREBORN_WALLS`
unset no wall is registered at all and the wire is byte-identical to a build
without the feature.

```sh
WAREBORN_WALLS=1 <however you normally start the game server>
```

**1. Confirm the server thinks they exist**, before launching a client. One
line, and it names the cost:

```
[info] weather walls: ON, 44 of 44 served (11 storm rift(s), 53.4 km of storm
wall -> that many km drive the world-wide ambient-bolt rate). Visual only: ...
```

**2. Confirm a client is told about them.** Join, then in the server log:

```sh
grep -c "queued AddEntityOp for world entity 'wall-"                      <log>   # expect 44
grep "seeding 190602 for entity .* WallSegment"                           <log> | head -1
grep -iE "failed to initialize component|DROPPING the whole AddComponent" <log>   # expect NOTHING
```

**3. THE CHEAPEST LOOK: fly WEST from spawn.** The single `WorldEndWall` is a
north-south curtain at `x = 15943.65` running the world's full 36 km, and the
player spawn is `(17212, -312, -1130)` — **1.27 km due east of it**. Fly west; it
enters visual range ~800 m out, so after roughly 500 m of travel a translucent,
see-through "waterfall of air" curtain should fill the horizon north to south.
*It is a WIND-type wall, so expect the translucent curtain, NOT dark cloud* — it
drives `wallColor.r` and never `.g` (`findings-storm-walls.md` §11a). If this is
not there, nothing below will work either, and the fault is delivery rather than
geometry.

**4. THE PAYOFF: the nearest Storm Rift is `wall-28`** — dark opaque billowing
cloud, rain, storm debris, an audio shift and free ambient lightning.

- Cross the world-end curtain heading west, then turn to **bearing ≈ 344°**
  (north, slightly west) and fly **4.6 km**, to about **`(15944, 3338)`**. That
  is `wall-28`'s eastern end; it runs 4.7 km west from there to `(11270, 3869)`.
- What to expect on the way in (`findings-storm-walls.md` §2.1): **~758 m**
  clouds visibly thicken · **~578 m** rain starts · **~367 m** full storm, the
  volumetric cloud renderer is *swapped out* for the opaque one and the debris
  emitters switch on · **~336 m** whiteout, ~40 m visibility.
- **`CmdBufClouds.enabled = false` inside ~367 m is EXPECTED.** If someone
  reports "the normal clouds vanished near the storm", that is the documented
  renderer swap, not a bug.
- Ambient bolts should flicker along the wall with no server involvement. At
  most two are alive at once (`_randomLightningSlots = 2`, RECOVERED).

**5. A sand storm, for the third renderer:** `wall-12`, bearing ≈ 217°, 6.5 km
from spawn, midpoint `(13305, -6292)`.

**What a PASS looks like, and what it does not include.** A wall you can see, fly
into and fly out of, with no log errors. **It will not push your ship at all**,
and that is correct: the three wall force paths live in `ShipPreprocessor`'s
`UnityWorker` branch and are not on our hulls, so `1204` applies zero newtons.
"The wall did nothing to my ship" is not a failure of this feature.

**If a wall renders in the wrong PLACE**, suspect ordering rather than geometry:
`WallSegmentVisualizer` captures `transform.position` once at `OnEnable`, so a
wall that registered before its `190602` landed stays where it was instantiated,
forever and silently. The seed order (`190602` then `1204`) is what prevents
that, and `WallPolicyTests` pins it.

## The post-deploy check for the game server

```sh
tools/check-game-server.sh              # the last 60 minutes
tools/check-game-server.sh 6h           # any journalctl --since expression
tools/check-game-server.sh --since-boot # since the unit last started
```

Read-only: it ssh's to production and greps. It starts nothing, stops nothing and
touches no database, so it is safe to run while people are playing and is meant
to be.

**Why it exists.** On 2026-08-19 the live server was found to have been logging
`[error] failed to initialize component NNNN` continuously **since at least
2026-08-08** without anyone noticing. The only thing ever run after a game deploy
was the snippet in the handover's first-15-minutes list:

```sh
journalctl -u wareborn-game -o cat --no-pager --since '10 min ago' | tail -100
```

That window is clean **by construction**. The game server spawns nothing on its
own; every one of those errors is produced by a CLIENT checking an entity out. A
window right after a restart, with nobody logged in, sees zero and prints green
no matter how broken the server is. It is not a weak check, it is a check that
cannot fail.

So this one is built around the two things that window could not do:

1. **It has a denominator.** Errors are counted per component-interest BATCH, not
   per minute, and a window containing zero batches exits `INCONCLUSIVE` (status
   2) rather than passing. "Nobody played, so nothing was wrong" is the exact lie
   that hid this for eleven days, and it is now a distinct, visible outcome.
2. **It compares against a committed ledger**, `tools/game-server-error-baseline.txt`,
   which names every id known to fail and says what a player loses. An id that is
   NOT in that file fails the check at **count one** — every new entity type this
   server has grown announced itself as an id nobody had a branch for, and that is
   cheap to catch on the first occurrence and expensive on the ten-thousandth. On
   top of that a rate ceiling (`WAREBORN_ERROR_CEILING`, default 25 per 100
   batches) catches a *known* id that has started firing an order of magnitude
   more often, which is what a regression inside an existing branch looks like
   from outside.

The baseline is a ledger of debt, not a mute button: an id that gets fixed is
DELETED from it, and an entry whose consequence has not been established says
`UNINVESTIGATED` rather than implying it is harmless.

**Where it belongs in a deploy.** There is no `tools/deploy-game.sh` — the game
server holds live progression and a restart is session-ending, so it is
deliberately a hand operation. Run this **before** a deploy over a window that
had players in it, to get the number you are changing from, and again a few hours
**after**, over a window that had players in it. Two minutes after a restart is
not one of those windows and the script will tell you so.

## The storage suite

```sh
dotnet test WorldsAdriftReborn.Storage.Tests
```

Split in two by what it needs:

- The **policy and migrator tests** are pure — usernames, PBKDF2, session
  tokens, expiry arithmetic, which schema scripts a version still needs. They
  need no database, no network and no setup, and they always run.
- The **repository, constraint and schema tests** need a real PostgreSQL
  server, because what they assert is that the schema *refuses* bad rows. A
  fake that accepted rows the real server rejects would be worse than no test.
  They **skip with a printed reason** when `WAREBORN_DB` is unset, so a green
  run on a machine with no database is honest rather than misleading.

To run the whole suite, point `WAREBORN_DB` at any PostgreSQL server you do not
mind being written to:

```sh
WAREBORN_DB='Host=127.0.0.1;Port=5432;Database=wareborn;Username=wareborn' \
  dotnet test WorldsAdriftReborn.Storage.Tests
```

Each test creates its own throwaway **schema** (`wareborn_test_<guid>`),
migrates it, and drops it on the way out — never a database, so the role needs
no `CREATEDB`, and nothing already in that database is touched.

Verified against PostgreSQL 18 (local) and 16 (the deployment target).

## How it is arranged

Rules are tested through **pure policy types**, and production calls those same
types. Nothing here asserts on a mock's call count: where a rule is about a
value that must never go on the wire (a prefab context or any duplicate mirror
operation) the test asserts on the value itself.

| Type | Owns |
| --- | --- |
| `PlayerRegistry` | peer↔entity ownership, relay target sets, the `Owns` gate |
| `RemotePlayerMirror` | join/relay/leave intents |
| `AppearanceStore` | per-entity customisation |
| `MirrorSendPolicy` | prefab contexts, remote seed, authority set, single-shot creation, relay readiness/reliability |
| `EntityIdAllocator` | entity ids, the one shared island id |
| `ClientRigPolicy` | local-vs-remote rig discrimination, keep-first singleton claiming |

`ClientRigPolicy` is client-side logic living in a server-side library. That is
deliberate: the BepInEx mod is `net35` and cannot reference a `net6.0` assembly,
so `WorldsAdriftReborn.csproj` **links the source file** (`<Compile Include=...
Link=...>`). The mod and the tests therefore compile the same code — a copy
would have gone stale silently. Keep that one file `net35` / C# 7.3 clean: no
`IReadOnly*` generics, no LINQ, no nullable annotations, no target-typed `new`.

The suite is mutation-checked: 12 hand-written mutations (prefab context flipped
to `"Player"`, a mirror op made resendable, `1073` reclassified as
reliable, `1072` added to the remote seed, `190602` dropped from the authority
set, the island id re-allocated per call, `CameraProxy` dropped from the
local-only markers, keep-first turned into last-wins, `Owns` weakened to "any
registered peer", …) were all caught. It is not vacuous.

## Rule-by-rule coverage

Numbering follows `docs/multiplayer.md`.

| # | Rule | Status |
| --- | --- | --- |
| 1 | Packets must carry their sender (48-byte explicit layout) | **Not covered.** Native struct layout. A `Marshal.SizeOf`/`OffsetOf` test is possible but would force the test project to reference the server exe and therefore a real game install. Left alone on purpose. |
| 2 | Remote players use prefab context `"Default"`, never `"Player"` | **Covered** — `MirrorSendPolicyTests`, asserted as a value plus a not-equal against the local context. |
| 3 | The mirror is two-phase (asset request → park → one flush on ack) | **Covered as policy and real wire.** `MirrorScheduleTests` pins time-based parking/flush and production's zero-resend default; tier 2 rejects duplicate remote-player AddEntity. The ack payload itself remains opaque. |
| 4 | All clients get the SAME island entity id | **Covered** — `EntityIdAllocatorTests`, including that the id is a real allocation rather than a constant and can never collide with a player entity. |
| 5 | Grant each client authority over its own TransformState (190602) | **Covered** — set membership for `190602` and `1073`, plus a no-duplicates check. |
| 6 | First-time setup + authority only against the sender's OWN entity | **Covered** — `PlayerRegistry.Owns`, including that an unregistered peer does not own entity `0`. |
| 7 | Remote seed is exactly `{190602, 1086, 1081, 1088, 1073, 6910, 1098}` | **Covered** — exact-set assertion, plus explicit "never `1072`", "never `1109`", and a size ceiling so a widened seed has to come through this test. |
| 8 | Never read `ComponentDatabase.MetaclassMap` before the game populates it | **Not testable.** Static-initialiser ordering inside the game client. Two clients and a human. |
| 9 | Unity `[Require]` gating only affects OnEnable/Update; keep-first singleton guards | **Partly covered.** The keep-first *decision* is now `ClientRigPolicy.ShouldClaimSingleton` and is tested (first claims, a live owner is never taken, the owner may re-claim, a destroyed owner may be replaced). Whether Unity actually runs `Awake` on a mirrored rig is live-client. |
| 10 | The plain rig has no `CharacterTransformVisualizer`; `RemoteRigMover` fills the gap | **Not covered.** Reflection against Unity/SDK types, per-frame, coordinate remapping. Live-client. |
| 11 | `LocalPlayer` is a scene object — identify "my rig" by components, never by name | **Covered** — `ClientRigPolicyTests`: a rig *named* like the local player but carrying no local-only component is remote, and a rig *named* like a remote but carrying `LocalPlayerInit` is local. Each of the five markers is tested individually. **One violation remains in production — see the bug below.** |
| 12 | BepInEx `WriteUnityLog = false` hides all mod logging | **Not testable.** Environment configuration. |

Rules that were only in the code, not in `docs/multiplayer.md`, and are now
covered:

- **Only `AddEntity` may be resent; never `AddComponents`.** Resending
  `AddComponents` re-applies the default seeded `TransformState` to a live
  player and launches them into the sky. Covered by `MirrorSendPolicy.MayResend`
  tests, including a sweep over every `MirrorOp`.
- **The fall floor** (rule 17). `FallPolicyTests` pins where the floor is
  against the two measured numbers it is derived from (Haven's world y and the
  local AABB minimum of `island-surfaces/1431299145.json`), asserts that nothing
  anyone can stand on and no safe destination is below it, and that the fall to
  it takes single-digit seconds. `FallWatch` is tested for the behaviour that
  cannot be observed by falling off an island once: one rescue per fall across
  50 packets, a retry when a rescue produces no ack, a give-up said exactly once,
  re-arming the moment the player is level with the island, per-entity
  independence, and that a parented transform — whose position is local, not
  world — is never judged, including across the later updates that do not
  mention `parent` at all. What is **not** covered is whether the client applies
  the 190607 that results; that is the teleport path, and its only evidence is
  the 1073 ack.
- **Relay reliability.** `190602` and `1073` unreliable (superseded every tick;
  reliable ordering head-of-line stalls on loss), *everything else* reliable
  (a dropped one-shot such as appearance never comes back). Covered by a sweep
  over ids `0..1999`, not a hand-picked list.
- **Per-peer cleanup on disconnect** for everything the multiplayer library
  owns — `PeerCleanupTests` drives the same call sequence the server's
  connect/update/disconnect paths use and asserts the departed peer is gone from
  the registry, the appearance store and every relay target set, that a later
  joiner is never told about the departed avatar, that other players are
  untouched, and that a reused ENet peer slot inherits nothing.
- **Entity ids are never reused**, so a stale cross-client reference can never
  resolve to a different player.

## Known bug, found while writing this and deliberately NOT fixed

`WorldsAdriftReborn/Patching/Multiplayer/PlayerVisualizer_Patch.cs` decides
"is this the local rig?" as

```
rootName.StartsWith("Traveller@Player") || <component-based check>
```

The name clause is exactly the rule-11 discrimination that cost a test round
everywhere else in the mod. It is **unreachable today** — mirrored remotes spawn
from prefab context `"Default"`, so their roots are named `Traveller N` and
never `Traveller@Player` — but it means the patch would hand a *remote* rig to
the game's own `FixedUpdate`, whose Parent branch is what previously dropped a
rig ~90km away and through the map.

The failing test is written and **skipped**:
`ClientRigPolicyTests.PlayerVisualizer_decides_local_vs_remote_by_components_only_and_never_by_name`.
Verified to fail when unskipped. The fix is to delete the `FullRigRootPrefix`
clause from `ClientRigPolicy.TreatAsLocalForPlayerVisualizer` and unskip.

Related, not a bug but worth knowing: `RemoteRigSweeper` also gates on
`root.name.StartsWith("Traveller")` / `"Traveller@Player"` before consulting
`IsLocalRig`. There it is a cheap pre-filter *in front of* the component check
rather than an override of it, so it is safe as written — but it is the same
smell and the same failure mode if it is ever reordered.

## What was deliberately left alone

- **The two-phase mirror queue** (`pendingMirrors`, `pendingMirrorTick`,
  `mirrorResends`, `mirrorResendTick`, `mirrorResendsLeft`, `FlushStaleMirrors`,
  `ResendMirrors`, `FlushPendingMirrors`). Extracting it is a ~120-line rewrite
  of the main loop, which is not a "test + extraction" pass.
- **`PeerIdentity`, `PeerManager`** and everything keyed on `ENetPeerHandle`.
- **`ComponentsSerializer` / `ComponentUpdateManager`**, which need the game's
  generated component assemblies.

Consequence, stated plainly: **the ENet-side per-peer maps are not covered by
`PeerCleanupTests`.** That test proves the *multiplayer library* forgets a
departed peer. It proves nothing about the main loop's dictionaries.

## Still needs Unity clients

No pure test in this repo can tell you any of the following. If you changed
anything near them, launch two clients (one person may operate both local
clients) and look. The tier-2 ship extension above should remove protocol and
state-machine discovery from this list, but it cannot remove visual acceptance.

1. **That a remote avatar is actually visible** — spawned, skinned, on a layer
   the camera draws, not culled, not at the default seed position 90km away.
2. **That the camera and local-player identity stay with the right rig** when a
   second player joins. The keep-first *decision* is tested; whether Unity's
   `Awake`/`Start` ordering still puts the local rig first is not.
3. **That movement is visually smooth and correctly positioned** — tier 2 proves
   both peers receive the authoritative ship timeline and the passenger retains
   its hull-relative coordinate frame, but only Unity can exercise its
   interpolators, `PlayerVisualizer`, `PathFollower`, camera and rendered pose.
4. **That the two-phase asset flush visually instantiates the Unity rig.** Tier
   2 proves one AddEntity and one complete seed land over real ENet, but only a
   Unity client proves the prefab rendered. The ack payload is still opaque.
5. **That the fallback flush timing suits an idle in-world player.** Creation is
   deliberately single-shot now: duplicate AddEntity can recreate/split a live
   rig and is rejected by policy and tier 2.
6. **That nobody gets launched into the sky** on a second player's join. Tier 2
   rejects seed-timeline regressions; the rendered physics outcome remains visual.
7. **That appearance, glider wings and the grapple rope render on a remote.**
8. **That a departed player's avatar visually disappears.** Channel 5,
   `RemoveEntityOp`, the SDK dispatcher callback and server cleanup are now
   implemented; visual destruction still needs one Unity confirmation.
9. **Anything about latency, packet loss or bandwidth** over a real internet
   path. The reliable/unreliable classification is tested as a classification
   only.
10. **That the mod loads at all** under BepInEx, and that its Harmony patches
    still bind after any game or SDK change.
