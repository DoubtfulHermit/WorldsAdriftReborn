# Wareborn Engineering Handover

**Canonical entry point for a new maintainer or coding agent**

**Snapshot:** 2026-08-15, Europe/Berlin

**Repository:** `DoubtfulHermit/WorldsAdriftReborn`

**Active integration worktree:** `/home/ttanurhan/Games/wareborn-loading`

**Active branch at this snapshot:** `feat/island-identity`

**Deployed code baseline at this snapshot:** `489517f` (`Fix ship steering,
passenger coherence, and re-entry`). Production and the checked-out gameplay
code match at this revision; later documentation-only commits do not imply a
different production binary.

This file is the current operational and architectural handover. Start here,
then follow the narrower documents it links. Do not treat old roadmap entries,
downloaded design briefs, branch names, or chat summaries as proof that code is
implemented.

## 1. First 15 minutes

1. Work in `/home/ttanurhan/Games/wareborn-loading` unless the user
   explicitly selects another worktree.
2. Run `git status --short`, `git branch --show-current`, and
   `git log -10 --oneline` before editing. There are many historical worktrees;
   changes in one do not appear in another.
3. Read this file, then [hosting.md](hosting.md), [testing.md](testing.md), and
   [the research index](research/README.md).
4. For client behavior, inspect the retail decompile at
   `/home/ttanurhan/Games/WAReborn-decompiled/acs` before inventing behavior.
5. Run the baseline gate:

   ```bash
   dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests -c Release
   dotnet build WorldsAdriftRebornGameServer -c Release
   dotnet build WorldsAdriftReborn -c Release
   ```

   At this snapshot the Multiplayer suite passes **2342/2342**, and both server
   and client builds succeed. Existing nullable/obsolete/net6-EOL warnings are
   known. Do not run the test and server builds concurrently: both write the
   Multiplayer output and can cause a harmless file-lock retry.
6. Check production read-only before any deployment:

   ```bash
   ssh root@62.171.161.19 'systemctl is-active wareborn-game wareborn-login'
   tools/check-game-server.sh 6h     # error rate per interest batch, vs. the ledger
   curl -fsS https://wareborn.ratlabs.cc/patch/manifest.json | jq '{version,build}'
   ```

   **Do not judge the game server by a short window after a restart.** This list
   used to say `--since '10 min ago' | tail -100`, and that window is clean by
   construction: the server spawns nothing on its own, so every component-init
   error needs a player to be online to exist. It printed green for eleven days
   while the server logged `[error] failed to initialize component` continuously.
   `tools/check-game-server.sh` counts per interest BATCH and exits
   `INCONCLUSIVE`, not `PASS`, when nobody played in the window. See
   [testing.md](testing.md).

7. Never restart the game server while players are connected unless the user
   explicitly says they have disconnected. A restart is still session-ending.

## 2. Source-of-truth hierarchy

When sources disagree, use this order:

1. current checked-out code and tests;
2. a current live log or packet trace;
3. the shipped retail decompile and asset census;
4. this handover;
5. focused findings under `docs/research/`;
6. `README.md`, `docs/hosting.md`, and `docs/roadmap.md`;
7. downloaded handover/roadmap proposals and old chat summaries.

Superseded planning documents live under `docs/archive/`. They remain useful for
historical citations but must not override the current roadmap or handover.

## 3. Repository and runtime map

The worktree cleanup on 2026-08-14 removed 94 clean historical checkouts while
retaining every branch and commit. Thirteen worktrees remain: this integration
tree, `wareborn-main`, the original dirty checkout, and dirty research/diagnostic
trees. Do not remove those remaining trees until their uncommitted files are
reconciled explicitly.

| Area | Purpose | Primary entry points |
| --- | --- | --- |
| `WorldsAdriftReborn/` | BepInEx client mod, Harmony patches, client diagnostics | `Plugin.cs`, `Patching/` |
| `WorldsAdriftRebornCoreSdk/` | Native client/server protocol shim and ENet transport | `Connection.cpp`, `Dispatcher.cpp`, `enetLayer.cpp`, `OpList.h` |
| `WorldsAdriftRebornGameServer/` | Authoritative game server and main poll loop | `WorldsAdriftRebornGameServer.cs`, `Game/`, `Networking/` |
| `WorldsAdriftRebornGameServer.Multiplayer/` | Engine-free policies, ledgers, catalogues, geometry | resource, inventory, placement, ship and flight types |
| `WorldsAdriftRebornGameServer.Multiplayer.Tests/` | Fast native regression suite | 2295 tests at this snapshot |
| `WorldsAdriftServer/` | Login, accounts, roster and patch-file HTTP service | request handlers, storage integration |
| `WorldsAdriftReborn.Storage/` | PostgreSQL models/repositories/migrations | storage tests require `WAREBORN_DB` for integration cases |
| `tools/patcher/` | WAPatch and manifest release pipeline | `README.md`, `build-manifest.sh` |
| `tools/relaybot/` | Native shim builder, protocol/load diagnostics and isolated two-peer ship wire acceptance | `build-coresdk-native.sh`, `run-ship-acceptance.sh` |
| `docs/research/` | Evidence and protocol reconstruction | `README.md` index |

Important local external inputs:

- Retail decompile: `/home/ttanurhan/Games/WAReborn-decompiled`
- Local game/client: `/home/ttanurhan/Games/WorldsAdrift`
- Extracted Haven surface data:
  `docs/research/world-data/island-surfaces/1431299145.json` when present in the
  relevant research checkout, plus the generated `Resources/HavenSurface.cs`.

## 4. Production snapshot

### Services and endpoints

- VPS: `62.171.161.19`
- Game: native Linux x64, UDP `7779`, systemd unit `wareborn-game`
- Login/REST: native Linux x64, TCP `8085`, systemd unit `wareborn-login`
- Public signup/patch host: `https://wareborn.ratlabs.cc`
- PostgreSQL: loopback-only on VPS port `5434`
- Live native game directory:
  `/opt/wareborn/WorldsAdriftRebornGameServer-native`
- Live patch directory: `/opt/wareborn/patch`
- Old Wine game deployment remains rollback-only.

The native game unit is represented by
`deploy/wareborn-game-native.service`. It loads `libCoreSdkDll.so`; build that
shim with `tools/relaybot/build-coresdk-native.sh` whenever native protocol code
changes.

### Exact deployed revisions

**LOCAL FLIGHT/WALL CONSOLIDATION 2026-08-21 — NOT DEPLOYED.** Commits
`8686986`, `6b4d855` and `48bf9dd` are integrated on
`integ/wind-storm-architecture-deploy`. They add lifecycle-gated sail
activation, neutral-edge helm takeover, corrected serialized ShipConfig drag
(`0.007`, exponent `2.5`), evidence-anchored WAReborn tuning of 1400 N/engine
and 420 N/(m/s)/sail, schema-15 admin flight diagnostics, and opt-in release-map
wall resistance across the recovered +/-400 m physical band (+/-200 m full
strength). Wall magnitudes remain OFF by default because retail's 1229 values
are lost. Combined verification passed **4517/0** Multiplayer and **1209**
login/admin tests with 26 expected skips; integrated game-server and client
builds both passed with zero errors. Production and the installed client remain
on the revisions described below.

**FLIGHT/WALL FOLLOW-UP 2026-08-21.** Game server `b46f242` is deployed.
Canvas-driven motion now carries the full ambient relative-wind velocity even
with the helm lever centred; the former gate incorrectly let sail thrust move
the hull while making the drag equation see still air. Sail power remains at
the previous WAReborn tuning value 30 (retail's server-authored value is lost),
wind remains `2.236`, and varying wind remains off.
The new closed-form 3094 kg/two-sail regression test was mutation-checked and
the full Multiplayer suite passed **4484/0**. `WAREBORN_WALLS=1` is now live:
all 44 release walls are served, including 11 storm rifts, as visuals only with
zero wall force. Managed-DLL staged/live SHA-256 matched, all persistence
surfaces restored, and boot contained no error/fatal/persistence-off line. The
post-deploy component-interest checker remains INCONCLUSIVE until a player
connects. Rollback binaries are in
`/opt/wareborn/backups/pre-b46f242-20260821-1842/`.

**DEPLOYMENT 2026-08-21.** Game and login/admin were deployed together from
`3528d5c` after consolidating `feat/understorm-s1` (including S2/S3, wall
visuals and the simulation shadow model), `feat/wind`, and
`docs/architecture-audit`. Multiplayer tests passed **4483/0**; login/admin
tests passed **1209/0 with 26 expected skips**; both self-contained Linux-x64
publishes succeeded. Staged and live executable SHA-256 hashes matched. The
pre-deploy backup is
`/opt/wareborn/backups/pre-3528d5c-20260821-153408/`. Production restored 9/9
deployables, 5/7 ships (two tombstones), 21/21 mounted parts and 4/4 loose
parts; all four Postgres persistence surfaces are ON. Understorms remain ON
for the 47 Tier-1 islands at the existing 15-minute production cadence. S2
now scopes the reset to each island and S3 re-rolls its deposit placement.
`WAREBORN_WALLS` and `WAREBORN_FLIGHT_WIND_FIELD` remain unset/off pending
visual acceptance. The post-deploy checker is INCONCLUSIVE, not failed: zero
players connected after restart, so no component-interest batch was exercised.
Run it again after a real client session. No client patch or schema migration
was involved.

**SESSION HANDOVER 2026-08-20.** `main` = `ee86213`, in sync, clean, 172
commits over two days. Both services active. Live flags:
`WAREBORN_FLIGHT_FORCES=1`, `WAREBORN_FLIGHT_WIND_SPEED=4.0`,
`WAREBORN_SPAWN_LOOT=1`, `WAREBORN_FUEL_GATES_THRUST=0`,
`WAREBORN_RELEASE_WORLD_DISTRICTS=tier1`.

**Start here:** `docs/plans/feature-roadmap.md` **§0.0** lists everything that
shipped, what is awaiting a live check, the research written, and the live
defects still open. It supersedes that document's own §2 status tables.

**Three tasks were scoped but never dispatched** (the session hit its subagent
limit): **wind** as a varying system plus visualisation, **storms** and the
island-node refresh cycle, and an **architecture audit** of this server design.
Full briefs were handed to the maintainer in chat.

**The five open defects, ranked:** 118 of 228 knowledge nodes take payment and
grant nothing; 13 more grant the wrong thing; five recipes are learnable and
uncraftable (the Territory Control Tower costs 5000 knowledge for a recipe the
server always refuses); the relay two-state defect costs 50 ms on 40% of
sessions; and the database credential is still in the systemd environment.

**Awaiting the maintainer in-game:** that a Power Generator prompts "Refuel"
and the needle climbs; that a bar pipe mounts and rides a flying ship without
jitter; that the belt divider now sits at row 14; that flight feels right with
forces on.



- **`feat/wind` (NOT DEPLOYED, server + admin page only, no client mod, nothing
  on the wire, so no soak was required or run).** Multiplayer **4182/0**
  (baseline 4132), `WorldsAdriftServer.Tests` **1194/26 skipped** (baseline 1192).
  **18 mutations applied one at a time; the first pass caught 15 and THREE
  ESCAPED, all three now closed** — see §12.6a of the roadmap on that branch.
  * **THE HEADLINE, and it inverts a standing assumption: retail's own players
    never saw a weather cell either.** The shipped `WeatherCell` blueprint (a
    TextAsset inside `resources.assets`, not a file on disk) grants
    `EntityReadAccess: [ "social", "physics" ]` — **`"visual"` is absent**, while
    the `Blight` blueprint beside it asks for it explicitly. So `GetWeatherAt`
    returned the `(1,0,-2)` fallback in retail too. **`2.236 m/s` is not a
    placeholder for an absent system; it is the only ambient wind a player ever
    had.** What varied in retail was WALL wind.
  * **The client is already drawing wind and we have never fed it.** `WindTrail`
    (the wiki's "windtrails in the sky"), `WindControl` (foliage sway, all cloth,
    the global shader wind rotation), `FlagWind` (a flag IS a working
    weathervane), `SailVisualizer`/`SailControlVisuals` (fill, luff, belly side,
    ripple — direction only, magnitude is discarded). No client mod needed for
    any of it. There is **no windsock ship part**: searched the decompile and
    `resources.assets` with `grep -a`; the only hit is a scrap-item icon.
  * **Wind walls are NOT blocked on weather** — the roadmap said they were.
    `WallSegmentVisualizer` has one `[Require]` (`1204`) and `GetWeatherAt` lerps
    wall wind over cell wind. **But 1204 without a complete `1229` makes every
    wind wall DEAD CALM** (the multipliers default to `0f`) and log-spams ~40
    missing keys. They land together or not at all, and that pairing needs a soak.
  * **`5129` and `1202`/`1203` do not work**, and the roadmap recommends the first.
    5129 is a worker-side *report* channel with no client reader; 1202/1203
    register into a `_modifiers` set nothing ever enumerates.
  * Shipped: `WindField` as the one answer to "what is the wind here", plus
    **`WAREBORN_FLIGHT_WIND_FIELD` (0..1, DEFAULT 0 = today, bit-identical)**,
    which makes wind vary by place/time and aims the bare-hull drift downwind;
    and an **operator-only admin map wind layer** that re-evaluates the server's
    own closed form in the browser rather than drawing an illustration.
  * **RECOMMENDED: set `WAREBORN_FLIGHT_WIND_SPEED` back to `2.236`** (it is 4.0).
    At 4.0 the wind streaks a player steers by say 2.24 while their hull drifts at
    4.0, and no server-side change can fix that. If a bare hull is then too slow,
    raise `WAREBORN_FLIGHT_SAIL_POWER` — that moves canvas only.
  * ⚠ **METHOD:** `WAReborn-decompiled/` contains **no `WASystems.dll` and no
    `SpatialTranslator.dll`**, so any "no consumer found" drawn from grepping that
    tree alone is a possible false zero.
  * **Wants a live look, and both are free:** are wind streaks visible in the sky
    at all, and does a mounted flag point SSE? Neither needs a code change.

Entries below are a LOG, newest first. Each records what was true on its own
date, so only the newest entry describes production now - a reader who takes an
older entry's "production still runs X" as current state will be wrong. The
authority for live configuration is the box itself:
`systemctl show wareborn-game -p Environment`.

- **`feat/fuel-generators` (NOT DEPLOYED, server-only, no client mod).** **The
  fuel tank moved onto the POWER GENERATOR**, which is where the shipped client
  has always said it is. `PowerGenerator01_unityclient` bakes an
  `InteractiveObjectVisualizer` with `Verb = Activate` and a `TutorialHelper`
  pointing at `MOUSE_OVER_GENERATOR`, whose overlay asset carries one control
  reading **`Name: "Refuel", Hold: true`**. The old "there is no fuel tank prefab,
  so fuel is per-hull" finding searched the 349-name census for *fuel tank*; the
  prefab is line 219, `powergenerator01`.
  * **Capacity 100 per generator, RECOVERED** (wiki + `FuelGaugeVisualizer.cs:56`'s
    own `SetFuelAmount(0f, 100f)` default), replacing the invented 250-per-hull.
    **`WAREBORN_FUEL_CAPACITY` changed meaning** — it is now ONE generator's
    capacity, not a ship's.
  * **Generators pool by summation**: two on a hull is twice the range. Not
    `1106.subtanks` — nothing in the client reads that field.
  * **Fuel travels with the generator** when it is lifted off and re-bolted.
  * **Refuel = hold E on the generator.** The bunker drain is **deleted**; it only
    existed because there was no honest prompt, and it walked every container on
    every burning hull once per canister.
  * **No ship can become unflyable.** Metering strictly shrinks: it keyed on the
    sky core and now keys on the generator, which nobody has built, so the metered
    set is empty on deploy and every ship reverts to unmetered — full static
    gauge, no burn, no gate, i.e. exactly the pre-fuel behaviour.
  * **Nothing new is served.** `1106` stays unserved on purpose: `FuelVisualizer`
    is its only reader, it is on ship ROOTS only (UnityPy-confirmed: ShipFrame,
    ShipFrame01, ShipFrame02, no part prefab), and `GetFuelPercent()` has zero
    callers. `1258` untouched — the `AtlasMultiplier = 0.0` cliff is not
    approached. Full write-up: `docs/plans/feature-roadmap.md` §13.11.
  * **Needs a live check** before deploy: that the "Refuel" prompt appears on a
    mounted generator, that the hold completes, and that the needle climbs.

- **CONFIRMED IN A LIVE CLIENT, 2026-08-20 (late 19th session):** a ship
  container **opens on a ship**. That closes the "It's locked." defect and, with
  it, the third identity gate - `InteractAgentObserver` reading
  `LocalPlayerInit.PlayerId` (the literal string `"id"`) against a list of
  character UUIDs, so an owned hull read as hostile to its own owner.
  The same gate was adding **+10 s to the hold time of the sail, lamp and horn**,
  which nobody had reported because the client draws no progress bar below
  0.001 s and we serve `timeToUse = 0` - so the tax was invisible until the
  maintainer saw a bar appear on the sky core and let go of it. Those three
  should now respond instantly; unconfirmed at time of writing.
  Also confirmed live earlier the same session: the fuel gauge NEEDLE moves (it
  is a dial with no verb, which is correct and not a defect), and the altimeter
  now TARGETS a railing, which was SC3's open question.
  Still failing live: the altimeter preview is **red** after the parent-walk fix
  (a different gate from the blue it replaced) and its orientation is wrong. See
  the bar-pipe note below.

  **OPEN QUESTION THAT SHOULD BE SETTLED BEFORE MORE PLACEMENT WORK.** The
  Worlds Adrift wiki's Flight Instruments page says instruments mount on **bar
  pipes**, a dedicated part. `LoosePartCatalogue` has `railing` and
  `railingCorner` and **no pipe of any kind**. So our `GetTag`/`GetCurrentMask`
  client patch may be forcing instruments onto railings when retail had a
  purpose-built mount we simply never implemented. Prefer implementing the part
  and DELETING the patch.

  **This is the second instance of one error class today**, and it is worth
  naming: an agent searches the decompile for a thing, does not find it, and
  designs around its absence. Fuel was built per-hull because no "fuel tank"
  prefab exists - the tank is the **Power Generator** under a different name.
  The decompile cannot tell you the name of a thing you have not thought to
  search for. **Use community sources to learn WHAT to look for, then the
  decompile to establish HOW it works.**

- **Game server:** `92a4002`, **login server:** `92a4002`, both at 2026-08-19
  17:20 CEST. **Client manifest `2026.08.19-4`.** The afternoon, in one entry.
  * **Ship containers work** - trunk, mountedBox, storageContainer,
    shippingContainer open real inventories; 4 of 37 interactable became 7. They
    needed BOTH `1081 + 1236` seeded AND the `Inventory` verb; the generic
    `PickUp` we served is a verb the prefab never looks for.
  * **Real flight forces**, behind `WAREBORN_FLIGHT_FORCES`, **OFF** - engineless
    legacy hulls cannot move under it, so enabling it is a judgement call.
  * **Fuel works** *(both the tank location and the door in this line have since
    been superseded twice — see the `feat/fuel-generators` entry above)*: per-hull
    tank, refuel by holding E on the ATLAS SKY CORE,
    throttle-proportional burn, and a gauge whose needle moves. Thrust gating is
    deliberately **OFF** (`WAREBORN_FUEL_GATES_THRUST=0`) until the low-fuel
    warning exists - the refuel prompt renders with no text, so a stranded
    player would have no way to discover the fix.
  * **Emblem editor**: mirror bit and grid snap; **portal redesigned** on a
    design-token layer; **283 emblem objects**.
  * **Relay diagnostics** (`WAREBORN_RELAY_TRACE=1`, off) and the strengthened
    soak gate.
  Post-deploy checks passed on every step: zero `persistence is OFF`, zero
  `[error]`/`[fatal]`, world activating 2475 trees / 409 loot / 368 deposits /
  216 databanks / 110 atlas / 24 fuel.

  **THE THING TO READ BEFORE TOUCHING SHIP LIFT — CORRECTED 2026-08-20.** The
  warning that stood here was right about the danger and **wrong about why**, and
  the wrong reason was the more alarming one, so it is restated rather than
  quietly edited.

  What is true: `AtlasMultiplier` **is** Bossa's shutdown doomsday clock, it
  **would** evaluate to 0.0 today, and `ShipControlsBehaviour.UpdateVertical`
  **does** return early when overloaded, so on an *unmodified* client every ship
  would be permanently overloaded and unable to climb.

  What was wrong: *"vertical flight works ONLY because `ShipLiftVisualizer` is
  currently inert on our hulls."* It is not inert, and nothing here depends on it
  being inert. Climbing works because of **two live, deliberate mechanisms**:

  1. **`WorldsAdriftReborn/Patching/Flight/EndOfTheWorld_Patch.cs`** pins
     `AtlasMultiplier` at `1f` with a Harmony prefix — shipped in commit
     `a44aebb`, *2026-08-13*, in response to the live "can't go up and down"
     report, and verified present in the installed
     `BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll`. The apocalypse
     is already cancelled. **The audit that produced the old warning was written
     six days after this patch landed and did not know about it.**
  2. **We seed `1258` at a flat 1,000,000 kg** against a hull mass in the
     hundreds, so `Load` is ~0.001 and `IsOverloaded` is false with enormous
     margin.

  Why the correction matters rather than being pedantry: the old explanation
  says the safety comes from something being *absent*, which invites a future
  agent to "complete" the sky core's `[Require]` set and think they are fixing a
  gap. The real safety comes from those two mechanisms being *present*. **Do not
  remove either.** The live danger is now precisely one thing: serving a
  realistic `1258` (i.e. `MaterialCatalog.SkyCoreLiftKg`, ~1000 kg for a bare
  core) while hulls weigh 500–1700 kg, which would overload real ships for real
  reasons. That is roadmap F2's job and it is a balance decision, not a cliff.

  **What overload actually did, and what it can do HERE.** The message is
  `"Ship weighs more than its atlas sky core can lift."` — VERIFIED as the
  literal passed to `OSDMessage.SendMessage` at
  `acs/ShipControlsBehaviour.cs:283`. (Commit `4bfa35c`, 2026-08-20, replaced
  this with a different wording on the strength of an unverified claim and
  asserted the original "appears in no source". Both were false; reverted in
  `edc670c`. The decompile is the authority for anything the client prints, and
  **a game string is checkable in one grep — check it.** Recorded rather than
  quietly fixed because this is the third time in three days a finding here has
  been wrong for want of one search.)

  Overload was not benign in retail. Players describe an overweight ship being
  blocked from undocking, and the mass gauge reading `current / max` in kg — the
  widely-quoted *"noob cap"* being `950/1000kg` for a hull carrying two wings,
  two sails, a cannon and a barrel (**WIKI**, player-measured). That last figure
  independently corroborates our own arithmetic: a starter hull plus a handful of
  parts really did sit right on the 1000 kg line, which is why the sail cliff
  lands where it does.

  **But an overloaded ship cannot sink on this server, and the reason is
  structural rather than lucky.** The sinking is `ShipControlVisualizer
  .UpdateFloating`, which clamps lift to `[0, GetMaxLift()]` and lets gravity win
  — and that class is `[WorkerType(WorkerPlatform.UnityWorker)]`, i.e. it only
  ever ran on Bossa's FSIM physics worker, never on a player's machine.
  `ShipPhysicalityVisualizer.ClientDynamic()` hardcodes `false`, so a ship's
  rigidbody is permanently kinematic on a client and integrates nothing. A ship's
  altitude here is whatever our `1130` stream says it is.

  So the *client-side* consequence of overload is exactly one thing:
  `ShipControlsBehaviour.UpdateVertical` (which **is** a client behaviour)
  returns early, the client stops sending vertical input, and the OSD spams. The
  ship holds its altitude. **Sinking becomes possible only when F2 implements
  weight and lift server-side — at which point we would be the ones making it
  sink, deliberately and in a change we control.** Worth stating precisely,
  because "waking 1258 sinks every ship" would be a frightening and false reason
  to avoid work that is actually safe.
  Related standing warning, still current: serving `1106` on the hull
  would wake `FuelVisualizer`, which `ShipPreprocessor` attaches to every ship
  root. `feat/fuel-generators` re-ran that enumeration and still does not serve
  1106 — on a generator part it would satisfy no reader at all.

  **Corrections made today to earlier claims in this file and in the audit:**
  * The staleness step was **NOT** today's content and **not a regression** - it
    reproduces on `2bd3113` from 00:27, and both runs that showed it used the
    default Haven world, so the 409 containers were never in the world measured.
  * Retail's flight model is **NOT lost**: the physics shipped in the same
    `Assembly-CSharp` as the client, split by one runtime boolean.
  * Our flight WAS largely faked - flat 12 m/s for every ship, engines never
    consulted, sails a throttle multiplier - and `FlightTuning.cs` said so in an
    opening HONESTY NOTE. Hull mass was the one real part.
  * Unfurled sails DID move a stationary ship in retail. Throttle and velocity
    appear nowhere in the sail force.
  * `v_top = 10 * sqrt(thrust/mass)`, and retail set **no speed cap anywhere**.
  * Rudders, keels and ballast **never existed**.
  * Retail's placement default ALLOWED mounting on placed objects, proved by an
    opt-out marker. `RailingStraight` carries colliders on layer `Default`, which
    is inside `Layers.Environment` - SC3 was never blocked.

- **DEPLOYED** 2026-08-19 13:21 CEST (branch `feat/scrap-salvage`, merged).
  **Scrap salvaging** (resource-economy Phase 5): the SALVAGE button the client
  has always drawn on `scrapItem-*` now pays out the reward block itemData.json
  has always carried. This is the missing half of the loot-container loop that
  went live at 12:41 - a player could pick scrap up and do nothing with it.
  Server-only. **No schema migration**, no new component, no new message: `1082
  tryToConsume` already arrived and was already refused, `1081` already answers,
  and the toast is the same `8060` a mined rock fires.
  * The payout is RECOVERED verbatim from the 134 `rewards` blocks - material,
    amount and quality are never scaled or rolled. Metals, woods and fuel only;
    nothing may be added to that table.
  * **CORRECTION to the plan, and to anyone reading the table:** a `.1`/`.2`
    reward key is a SECOND YIELD AT THE SAME TIER, not a sub-tier. All 23 rows
    that carry one also carry its base key and the materials always differ, so
    both are paid. The plan's original "highest key whose integer part is n"
    rule would have silently deleted 23 base yields.
  * Tier comes from `meta["sourceTier"]`, stamped onto every item a loot
    container is stocked with, and clamped into the tiers that item has rows
    for. The clamp and the split of a 400-unit payout into 99-unit piles are
    WAREBORN TUNING; the totals are not.
  * **Two itemData.json defects fixed.** `scrapItemselenistswoodenorrery` was
    missing its hyphen, so its tier-4 `palm` x140 q10 was unreachable by any
    player (the client gates SALVAGE on `StartsWith("scrapItem-")`). And
    `scrapItem-woodenbowl` was listed TWICE with the rewardless copy last -
    `ItemHelper.AllItems` and the client's own `itemDict` are both last-wins, so
    the Wooden Bowl had no name and no yield. Both were the only ones of their
    kind in the file, and both classes now have a disk-reading test.
  * Gates: Multiplayer **3872 passed / 0** (baseline 3818; 54 added),
    `WorldsAdriftServer.Tests` **1175 / 26 skipped**, unchanged.
    **Relay soak FLAT** despite the plan saying none was needed - drift
    -0.02 ms, trend -0.02 ms against a 20 ms threshold, 21,606 sends 100%
    delivered, 0 gaps, 0 disconnects, 0 decode errors, 0 timeline violations
    (`tools/relaybot/run/soak-20260819-130629.csv`). The claim was checked
    rather than taken: the SEND CADENCE IS UNCHANGED, because `tryToConsume`
    already counted into the request tally and so already triggered exactly one
    1081 push per click. What is new is one `8060` toast per yield per
    deliberate click, and about twenty bytes of `meta` on a looted item inside
    an already-sent full-state 1081. Neither is a high-rate relayed component,
    which is the class that caused the desync spiral.
  * **Mutation-tested, because this repo has twice shipped a green suite over an
    unplugged feature.** TWELVE deliberate breakages of the production wiring
    were applied one at a time and every one was caught by the intended test -
    deleting the handler dispatch, no-oping the service call, removing the
    ownership gate, dropping the toast, cutting the service off from the policy,
    dropping the container tier stamp at its point of USE while leaving every
    other trace of it in place, making consume-then-grant non-atomic, parsing a
    tier key with a culture-sensitive decimal parse, renaming a RewardRow field
    to a C#-shaped name (which binds it to nothing, silently), reinstating the
    plan's wrong sub-tier rule, and putting both data defects back.
  * **Proved offline against the real data**, because no unit test exercises the
    JSON binding or the loot-to-salvage chain: a tier-4 container roll stocked
    the way `BindContainer` does it, taken into a bag the way a cross-inventory
    move does it, then salvaged. All four rolled relics paid, each carrying
    `sourceTier=4`, and `scrapItem-marimbiannosepipe` paid BOTH of its yields -
    50x aluminium q9 AND 350x hemlock q9, the hemlock landing as 99+99+99+53.
  * **Unverified until the maintainer salvages something in-world:** that the
    SALVAGE button appears on a looted relic, that the materials land in the
    panel, and that the "Salvaged <material> xN" toast fires. Headless bots run
    no inventory UI, so all three rest on unit tests and the decompile. The
    lines to watch are `[salvage] entity N salvaged item M at tier T -> ...`
    and, if the toast is silent, the `salvage feedback ... reached no peer`
    warning.

- **Game server:** `8eb4639`, deployed and restarted at 2026-08-19 12:41 CEST.
  **Login server:** `8068a0b`, same afternoon. Four merges landed between them;
  none carried a schema migration, which is the only reason the two were allowed
  to move independently.
  Carries, in merge order:
  * **log grounding** (`2cc9f02`) - a felled log rests on the slope instead of
    floating or clipping. Half the bug had nothing to do with slopes: a tree's
    origin is on its trunk AXIS, so a 90-degree topple buried the lower half of
    every log even on flat ground. `WAREBORN_TREE_FALL_LIFT` retunes the 0.4 m
    clearance without a rebuild - the trunk radius is RECONSTRUCTED, not measured.
  * **resource economy phases 1-3** (`9c25c81`) - mined metal carries its node's
    quality, deposits draw from their island's table, a tree cut pays plant fibre
    and berries. The deposit model is PROVED: `MetalRockStateData` carries
    `metalTypeId` and `quality`, so a deposit is a generic rock and the metal is
    data on the node.
  * **200 traced emblem objects** (`05798a6`) - catalogue 83 -> 283.
  * **loot containers phase 1** (`8eb4639`) - 409 activated on tier-1, gated by
    `WAREBORN_SPAWN_LOOT=1` (drop-in `loot.conf`, added this deploy).
  Post-deploy checks PASSED: zero `persistence is OFF`, zero `[error]`/`[fatal]`,
  and the world activates 2475 trees / 409 loot / 368 deposits / 216 databanks /
  110 atlas / 24 fuel.
  Soak run before each of the two game-server merges: **FLAT** both times.
  Rollbacks: `/opt/wareborn/backups/pre-loground-20260819T101448Z/game`,
  `pre-economy-20260819T102629Z/game`, `pre-loot-20260819T104115Z/game`.

  **Corrections to earlier entries and to the resource audit, all evidence-backed:**
  * "Every deposit is iron" was WRONG as a general claim. The 328 release-world
    nodes were always stamped with per-node metal and quality; only Haven's 40 are
    hardcoded iron, deliberately, so a new player's nearest rock is the starter
    metal. What was broken on all 368 was QUALITY - and worse than "defaults to
    zero", because the yield table is keyed by metal NAME, so two iron nodes
    overwrote each other.
  * "The per-island metal table is unused" was WRONG - it is reachable, because
    production runs `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1`.
  * "Scrap salvages into cloth/leather/glass/pigment" is WRONG. All 133
    salvageable `scrapItem-*` rows yield metals, woods and fuel ONLY. The Update
    27 economy therefore has no recovered bootstrap; anything we add there is
    ours and must be labelled WAREBORN TUNING.
  * "1081 InventoryState is the single blocker for containers" was INCOMPLETE.
    `InWorldInventoryVisualiser` requires BOTH `1081` and `1210`, and a Unity
    visualiser does not enable until every requirement resolves - the same bug
    shape as the loom's unseeded `1264`. The general case: **16 of 18 deployables
    seed only a transform.** Nobody owns that audit yet.
  * `WAREBORN_METAL_COUNT` is VESTIGIAL - read only by the disabled handshake
    path. The variable that matters is `WAREBORN_DEPOSIT_COUNT`, and if it were
    ever unset it defaults to 1 and Haven would show one deposit.
  * `WAREBORN_BUILD` had drifted to `f212e70` while the binary was current. It is
    now set from the deployed commit at deploy time. It is a LABEL - trust the
    binary's mtime over it.

  **Trap for whoever serves `1081` next:** `InventoryService.ForEntity` falls back
  to `InventoryWire.DefaultModel`, the player starter kit, and `Bind` runs its
  factory once - so serving it on a non-player entity without a specific model
  gives that entity a permanent inventory full of gauntlets.

  **CONFIRMED IN A LIVE CLIENT 2026-08-19 ~13:30 CEST** by the maintainer, with a
  screenshot: a felled tree pays plant fibre and berries alongside the wood, the
  fall itself reads correctly, and **a loot chest shows its prompt and opens with
  its contents in a CHEST panel beside the player's inventory**. That last one
  matters most: it was the single thing the container work could NOT verify,
  because headless soak bots run no visualisers, so the whole 1210 + 1081 serve
  and the Interact echo rested on unit tests and the decompile. It works.

  **SALVAGE ALSO CONFIRMED** in the same session: right-clicking a relic looted
  from a chest and choosing SALVAGE paid its materials. So the full loop -
  container spawns, streams, opens, yields scrap; scrap salvages into metal, wood
  or fuel - is verified end to end in a live client, not just in tests.

  **Still unverified:** that mined metal shows real quality and varies away from
  Haven (Haven is deliberately all-iron, so this must be tested on a tier-1
  island); that a log on a genuinely steep slope lies along it rather than flat.
  For the latter, the felling line now reports its own decision - `rest=... deg
  lift=... m (measured)` versus `(flat)`.

- **Game server:** `5a69250`, deployed and restarted at 2026-08-19 10:59 CEST.
  First game-server deploy of the day; the login server had moved four times
  without it, which was safe because none of those carried a migration and this
  one does not either.
  Carries the tree-fall visibility fix: the felled log was being built and shown
  to NOBODY because `FallingLogService` gated it on the send ledger, while every
  release-world tree is streamed by `ResourceInterestService`, which keeps its
  own `Loaded` set and never writes that ledger. Also: a cut now pays only for
  the section under the beam, so the trunk breaks up piece by piece instead of
  paying out the whole tree at once.
  Deployed with **0 players connected** (the maintainer logged out at 10:30), so
  nobody was dropped by the restart.
  Post-deploy check PASSED - all four persistence lines report ON (inventory,
  knowledge, logout-position, crew), listening on UDP 7779, `NRestarts=0`,
  `libCoreSdkDll.so` still in place beside the executable.
  Soak gate before merge: **FLAT** (drift -0.03 ms, trend -0.04 ms against a
  20 ms threshold; 21,606 sends, 100% delivered, 0 gaps, 0 disconnects).
  Rollback: `/opt/wareborn/backups/pre-treefall-20260819T085855Z/game`.
  **Unverified until the maintainer cuts a tree in-world:** that the log renders
  and topples, that the beam registers on the fallen trunk, and that pieces
  split off where struck. The line to watch is `[tree-fall] ... shown to N
  peer(s)` - **N must be >= 1**. It was 0 on five of the last six live cuts.

- **Client manifest `2026.08.19-2`** published 2026-08-19 09:55 CEST, build
  label "fixes PLAY hanging forever after the Steam removal". 54 payloads, no
  forbidden files. **This unbreaks the patcher**: both `2026.08.18-6` and
  `2026.08.19-1` shipped the connect defect, so every player who patched got the
  infinite load.
  The pack was assembled FROM THE MAINTAINER'S OWN WORKING INSTALL, driven by
  the previous manifest's destPath list, so the shipped set is one that has been
  observed to reach the world rather than a fresh unverified compile. Plugin
  sha256 `beace8da...87e553e6`, identical to the installed DLL.
  Verified after publishing the way WAPatch does it: manifest 200 reporting
  `2026.08.19-2`, payload downloads at the manifest's exact size and hash, and
  the shipped DLL contains the forced-Locator patch.
  Rollback: `/opt/wareborn/backups/pre-patch-2026.08.19-2-20260819T075451Z/patch`
  - but note that rolling back reintroduces the hang, so it is not a real
  rollback target for this one.

- **Login server:** `0fc418e`, deployed and restarted at 2026-08-19 09:48 CEST.
  Portal refusals now log WHY at the two choke points (the CSRF check and
  `Permitted`), because a refused crest save left no trace but the request line.
  Player-facing text is unchanged and still undifferentiated by design.

- **Login server:** `91fc33d`, deployed and restarted at 2026-08-19 09:37 CEST.
  `/patchnotes` is now GENERATED from the commit log by
  `tools/patchnotes/build-changelog.sh` rather than hand-written. Verified live:
  510 commit rows, strip reads "510 commits", zero external references; `/map`,
  `/account`, `/patch/manifest.json`, `/deploymentStatus` all unaffected.

- **OUTAGE 2026-08-19, ~08:00-09:35 CEST: PLAY hung forever. Client-side.**
  ROOT CAUSE: `f460087` ("the client no longer needs Steam to start") forced
  `Bootstrap.UseSteam` false. That is correct in itself, but the Steam branch in
  `LobbySystem.ConnectToGameServer` was the ONLY writer of
  `SpatialOS.Configuration.SteamToken`, and
  `ConnectionLifecycle.ShouldGetDeploymentList()` is
  `LoginToken || SteamToken` (decompile
  `acs/Improbable.Unity.Core/ConnectionLifecycle.cs:111`). We have never had a
  LoginToken, so the Locator path was being chosen purely as a SIDE EFFECT of
  Steam being on. Without it the SDK takes the receptionist path, and
  `WorkerProtocol_ConnectAsync` is still a stub that builds a Connection with a
  NULL host - it opens no socket and logs nothing. Hence: no packet ever left
  the client, the game server logged no connection attempt, and the only symptom
  was `!SpatialOS.IsConnected - not creating ECS!`.
  FIX `a99926a`: force the Locator path
  (`WorldsAdriftReborn/Patching/ContinueBootstrap/ConnectionLifecycle_Patch.cs`),
  and LOG the chosen path once per session - that decision being invisible is
  why this read as a server fault for an hour.
  **Both published payloads `2026.08.18-6` and `2026.08.19-1` contain the
  defect**, so any player who ran the patcher got the hang. CONFIRMED FIXED in a real client at 09:42:18 CEST: the client logs
  `connect path: LOCATOR (forced)`, `!SpatialOS.IsConnected` is absent, and the
  game server logged `peer connected. players now: 1`. Shipped as
  `2026.08.19-2`.
  Cleared with evidence, so do not re-investigate: the firewall (7779/udp
  allowed), the network (UDP probes reached the VPS), Harmony patching (97/87),
  the game server (never restarted, `NRestarts=0`), and the splash-text change.

- **Client manifest `2026.08.19-1`** published 2026-08-19 09:05 CEST, build
  label "landing screen: splash + welcome copy + patch notes". 54 payloads,
  no `steam_api64.dll` / `winhttp.dll`. Supersedes `2026.08.18-6`.
  This was held back while `/patchnotes` did not exist, because the client's
  PATCH NOTES button opens it; that page shipped in `566d7e6`, so the block is
  gone.
  The shipped `WorldsAdriftReborn.dll` is byte-identical (sha256
  `86f36f7b...284d8c55`) to the one in the maintainer's own install, i.e. the
  build that has actually been run, not a fresh unverified compile.
  Verified after publishing: manifest 200 reporting `2026.08.19-1`, and the
  plugin payload downloads over the public URL at the manifest's exact size
  and hash - the same check a player's WAPatch performs.
  Rollback: `/opt/wareborn/backups/pre-patch-2026.08.19-1-20260819T070453Z/patch`.

- **Login server:** `9855edd`, deployed and restarted at 2026-08-19 09:02 CEST.
  Login-only again, and again because there is no migration: the portal opens
  the three game-owned tables READ-ONLY and `EnsureSchema` leaves the database
  at v9.
  Signing in now lands on `/account` rather than `/download`. `/download` is
  untouched and still bounces an unauthenticated visitor to `/login`, so old
  links and the patcher flow are unaffected - verified live after the deploy,
  not assumed.
  Rollback: `/opt/wareborn/backups/pre-portal-20260819T070155Z/login`. No
  database action is needed to roll back, because nothing was migrated.
  Verified from outside: `/account` unauthenticated 302 -> `/login`; `/login`
  200; `/download` 302 -> `/login`; `/map`, `/patchnotes`,
  `/patch/manifest.json`, `/welcomeMessage` all 200; the plain-http emblem
  still 200 `image/png`. `NRestarts=0`.
  The one journal line matching "failed" is the OLD process taking SIGTERM
  during the restart, not the new one.

- **Login server:** `566d7e6`, deployed and restarted at 2026-08-19 09:00 CEST.
  Login-only: the game server was deliberately NOT moved, because the only
  storage change is additive (`ServerConfigRepository.Delete`) and no schema
  version changed. The split-deploy rule applies to MIGRATIONS, and there is
  none here.
  This deploy carries the public `/patchnotes` page plus its admin editor, and
  merges forward the welcome-message editor and the client PATCH NOTES redirect
  that were already on main. `feat/patchnotes` was cut at `bcbbfd3`, so it had
  to take a merge from main before it could land; both sides had added a card to
  the admin panel's System column.
  Caddy gained `handle /patchnotes*` proxying `127.0.0.1:8085`, validated before
  reload; backup at `/root/Avatar/Caddyfile.bak-patchnotes`.
  Verified from outside: `/patchnotes` 200 (72,593 b, zero external references,
  zero `<script>` tags), `/patchnotes/source` 200, `/patchnotes/nope` 404, and
  `/map`, `/welcomeMessage`, `/patch/manifest.json` all still 200.
  **Current world config: `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1`** - the
  Wilderness is OPEN and the shrine moves people. This supersedes the `C6` note
  in the `b652034` entry below, which was true on 2026-08-18 and is not now.

- **Game server:** `b652034`, deployed and restarted at 2026-08-18 08:05 CEST.
  **Login/admin server:** `b652034`, same pass. **Client manifest
  `2026.08.18-2`**, 54/54 public payloads verified, one payload changed.
  This deploy carries: asynchronous island bundle loading (the real cause of the
  approach stutter - our own offline-asset patch had made retail's async loader
  blocking), the reconstructed Bossa social/crew HTTP API, spawn terrain
  preloading, tree felling, material-driven ship mass, tier-1 world activation,
  inferred island metals (354 -> 1930 deposits), 13,266 trees, the Wilderness
  shrine, the pure fauna core, and stock knowledge values.
  It **migrated the production database from v6 to v7**, adding `social_invites`;
  verified after restart as `version = 7` with the table present and the other ten
  unchanged. Dumped first to `pre-b652034-20260818T060351Z/wareborn-db-pre-v7.sql`.
  Boot reports all four per-character stores ON, restore unchanged at 4/4
  deployables, 5/7 hulls, 16/16 mounted and 3/3 loose, `owned=543 unowned=0
  duplicates=0`, and zero errors.
  **The relay soak gate was repaired in this pass and passes FLAT** - see
  `tools/relaybot/run-soak.sh`. It had been unable to measure anything since the
  native migration because it ran the server under Wine against a Windows shim
  predating `ENet_EXP_PeerChannelCount`, so it reported "setup failed" rather than
  a verdict. First green run: 21,606 sends, 100% delivered, drift 0 ms, trend
  -0.01 ms, zero disconnects or timeline violations.
  Production still runs the temporary `WAREBORN_RELEASE_WORLD_DISTRICTS=C6`
  config, so **the Wilderness is CLOSED and the shrine refuses with a message**
  rather than moving anyone. Set `tier1` to open it.
  Rollback: `/opt/wareborn/backups/pre-b652034-20260818T060351Z/{game,login,patch,live-data}`
  plus the SQL dump. v7 is additive; rolling the binary back needs no database action.
  The previous deployment was `958c8e1` at 2026-08-18 00:07 CEST.
  **Login/admin server:** `958c8e1`, same pass. This deploy carries the solid
  hazed compact island shell, the revived CREW system, and the client authority
  grant without which crews are silently unreachable. It **migrated the
  production database from schema v5 to v6**, adding `crews` and `crew_members`;
  verified after restart as `version = 6` with both tables present and the other
  eight unchanged, and the database was `pg_dump`ed first to
  `pre-958c8e1-20260817T220636Z/wareborn-db-pre-v6.sql`. Boot reports all four
  per-character stores ON (inventory, knowledge, logout position, crew), restore
  counts unchanged at 4/4 deployables, 5/7 hulls, 16/16 mounted and 3/3 loose,
  `owned=349 unowned=0 duplicates=0`, terrain 4000 m load / 4800 m unload, and
  zero errors in the first four minutes.
  Crews are **deployed but never exercised against a live client**: the rules and
  persistence are tested exhaustively, but whether the retail crew UI renders and
  drives them is unproven. The panel is the **CREW tab of the Social Sheet**
  (`InputButtons.OpenSocial`, "Open Social Sheet" in the controls list); the
  default key is not recoverable from the decompile or from PlayerPrefs, so read
  it off the in-game controls screen. Silence in the log on a crew click means
  the event never arrived.
  Rollback: `/opt/wareborn/backups/pre-958c8e1-20260817T220636Z/{game,login,patch,live-data}`
  plus the SQL dump. The v6 tables are additive, so rolling the binary back needs
  no database action.
  The previous deployment was `c31e8be` at 2026-08-17 21:55 CEST.
  **Login/admin server:** `c31e8be`, deployed and restarted in the same pass.
  This deploy carries the ownership-bootstrap crash fix, the corrected compact
  island shell, and logout-position persistence. It also **migrated the
  production database from schema v4 to v5**, adding `character_positions`;
  verified after restart as `version = 5` with the table present and the other
  seven unchanged. The database was `pg_dump`ed first to
  `pre-c31e8be-20260817T195325Z/wareborn-db-pre-v5.sql` (68 KB). Boot reported
  all three per-character stores ON (inventory, knowledge, logout position),
  restore counts unchanged at 4/4 deployables, 5/7 hulls, 16/16 mounted and 3/3
  loose, `owned=349 unowned=0 duplicates=0`, and zero errors in the first five
  minutes.
  **Production is still running the temporary `WAREBORN_RELEASE_WORLD_DISTRICTS=C6`
  visual-acceptance config** from the drop-in
  `/etc/systemd/system/wareborn-game.service.d/release-world.conf`: 16 terrains,
  compact-outline shell fidelity, Mental Facility NOT registered. Remove that
  file and restart to return to the bounded one-terrain topology.
  Rollback: `/opt/wareborn/backups/pre-c31e8be-20260817T195325Z/{game,login,patch,live-data}`
  plus the SQL dump above. Note the v5 table is additive, so rolling the binary
  back does not require touching the database.
  The previous deployment was `ccfb138` at 19:10 CEST.
  This is the merged retail-LOD shell preference plus the admin map provenance
  labelling. The rollout switch `WAREBORN_RELEASE_WORLD_DISTRICTS` is NOT set,
  so production remains the bounded one-terrain topology; the deploy is a
  behaviour-preserving baseline, not the release-world rollout. Boot proved the
  fix directly: the startup line reads `[island-shell] distant non-physical
  island visuals: ON; fidelity=retail LOD (v1 preferred: the managed terrain
  set is bounded, so the island bundle prefetch is affordable)`, which is the
  branch that would have silently downgraded Mental Facility and The Trades
  Challenge to compact outlines had the fidelity stayed keyed on catalogue
  membership. Restore counts matched the previous boot: 4/4 placed deployables,
  5/7 hulls with the two salvaged tombstones correctly skipped, mounted and
  loose parts intact. `[world-directory]` classified 256 registrations
  (global=1, region=181, ship=74 across 5 hull roots). Terrain reported schema
  6, mode `on`, 3 islands, enabled, zero warnings and zero errors. The
  deployed game managed DLL is SHA-256
  `ff0d69007465253818c66159484d93f79b02ca9a7228e896a9c73a692117aae2` and the
  login/admin managed DLL is
  `58098d8af571c23511aa19dcdecb4a40bfa0239ef7ceddebacf4f51ded5fdc16`; both
  match the staged publish exactly. `WorldsAdriftRebornCoreSdk` did not change,
  so no native shim was rebuilt and the production `libCoreSdkDll.so` remains
  `0121219a138a07f345103f83cc5647f993ecb0282a0172c7bf19a54b78a252f7`.
  Coordinated rollback:
  `/opt/wareborn/backups/pre-ccfb138-20260817T170732Z/{game,login,patch,live-data}`.
  NOTE for future deploys: `WorldsAdriftRebornGameServer-native/data` is a
  SYMLINK to `../WorldsAdriftRebornGameServer/data`, which is where the live
  `world-state.json` actually lives. Back up that real directory (the backup
  above keeps it as `live-data`), and never rsync a staging tree that contains
  its own `data/` entry without excluding it, or the symlink is replaced and
  persistence is orphaned. Restarted with zero players connected and zero
  connects recorded for the whole prior uptime. Not visually accepted.
  The previous deployment was game `3d64a7f` at 13:52 CEST and login
  `2994db3` at 2026-08-17
  15:25 CEST. Stats schema 6 reports terrain checkout, runtime
  topology and authoritative player world positions, and the admin
  console exposes its one-island acceptance run. Production remains bounded to
  `WAREBORN_FIRST_REGION_TERRAIN_COUNT=1` and reports 3 island domains, 5 ship
  domains, 255 owned entities, one explicit global, zero unowned entities and
  zero ownership inconsistencies. Terrain checkout is enabled with the existing
  120 m resource-interest prerequisite; its defaults are 1200 m load / 1600 m
  unload. Opt-in distant island shells are also enabled: after login the server
  prefetches managed optional-island bundles and the matching client builds a
  non-physical last-retail-LOD silhouette, reveals it only after its generated
  material is ready, hides it while full terrain is checked out, and restores it
  after full terrain removal. Collision, resources and databanks remain
  exclusively on physical checkout. This retail-LOD (v1) shell is the preferred
  fidelity and remains what the bounded configuration requests: shell fidelity is
  chosen by `IslandShellFidelityPolicy` from whether the complete release-world
  rollout is active, not from release-catalogue membership, so embedding the
  254-island catalogue does not change what production sends. Live visual
  acceptance is still required.
  The authenticated Simulation Fabric also embeds an allowlisted projection of
  the preserved release MapFile (266 islands, 20 tier/biome cells and 44 typed
  weather-wall segments). Eighteen cells retain their authored district IDs;
  the two Tier-4 cells whose district is explicitly null are visibly signed as
  unassigned rather than invented as E1/E2 or merged into E3. Its layered SVG
  cartography shows the exact 36 km
  world boundary and the authored x=15,943.6523 m separator, explicitly shades
  the corridor containing all 12 preserved Haven placements, and overlays the
  current ship and player positions without permanent marker labels. The
  operator can inspect live authoritative XYZ values on selection. The browser
  refreshes on a four-second cadence over the game server's three-second stats
  snapshots. Missing player position is shown as
  unknown, never placed at a fabricated origin. The panel is now labelled for
  its provenance, because a 266-island map beside a 3-island table reads as a
  broken panel when it is only a configured one: the map is titled preserved
  release-world map and signed as static embedded MapFile evidence whose
  geometry, tier cells, walls and boundary are not read from the running game
  server, while the ship, player and simulated-island-domain marks are signed
  as the live overlay with their refresh cadence stated there. The Terrain
  checkout island inventory is signed as the authoritative live set of islands
  the game server is actually simulating. Both panels print one shared
  reconciliation line, "N islands on the preserved release map / M currently
  simulated", where N comes from the embedded projection and M is read from the
  live terrain section; if the stats file is missing, stale or predates terrain
  telemetry the line states that condition instead of a count, so a degraded
  snapshot can never render as a real zero. Which individual map glyphs are
  simulated is NOT claimed: the live ring already drawn at each simulated
  island domain's reported position is legended as the live mark, and no static
  glyph is restyled, because no exact island-id-to-MapFile-record mapping is
  available on the page and name matching would be a guess. Validation passed 2,586/2,586
  Multiplayer tests and 183/183 admin/login tests;
  game, login and client Release builds had zero errors. The coordinated
  rollback copy is
  `/opt/wareborn/backups/pre-069a372-20260817T093253Z/{game,login,patch}`; the
  immediate pre-fix game rollback is
  `/opt/wareborn/backups/pre-b52f504-20260817T100136Z`; the immediate
  pre-`1aa9fe4` rollback is
  `/opt/wareborn/backups/pre-1aa9fe4-20260817T104346Z`; the immediate
  pre-`7fab2e2` rollback is
  `/opt/wareborn/backups/pre-7fab2e2-20260817T111125Z`; the coordinated
  pre-`7c99dac` game/patch rollback is
  `/opt/wareborn/backups/pre-7c99dac-20260817T113001Z`; the coordinated
  pre-live-map game/login rollback is
  `/opt/wareborn/backups/pre-3d64a7f-20260817T115238Z`; the immediate
  pre-SVG-map login rollback is
  `/opt/wareborn/backups/login-before-svg-map-20260817T130929Z`; the immediate
  pre-zone-signage login rollback is
  `/opt/wareborn/backups/login-before-zone-signage-20260817T132536Z`.
- **Public client manifest:** `2026.08.17-7`, build label
  `solid hazed island shells + crews (958c8e1)`. Managed client DLL SHA-256
  `09c14ad11a43e8126e1a2e2802fd5ef60dcc245c74d97fd101ceec430fc1742e`. Exactly one
  payload changed against `-6`; all 54 public payloads matched their published
  hashes. It carries the shell fixes: side walls and keel now wind OUTWARD (both
  were inverted, so the flanks and the underside were backface-culled and the
  shell read as blown glass), hard rim edges, and self-hazing by distance -
  necessary because the client reports `scene fog=False`, so Unity's built-in fog
  is not what makes retail's distance haze and a fog-aware shader bought nothing.
  NOTE: distant shells are currently DISABLED in production
  (`WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=0`), because the 4 km terrain radius
  makes real islands the near-field answer, so these fixes ship but do not render.
  The previous manifest was `2026.08.17-6`, build label
  `island shell underside fix + logout position (c31e8be)`. It carries the keel
  winding fix: the compact shell's keel fan copied the top cap's winding, but it
  faces DOWN and so must wind the opposite way. The whole underside of every
  island was therefore backface-culled, the silhouette read as a shape with a
  piece cut out, and what remained looked like a flat plate. `-5` shipped that
  defect, so `-6` repairs a live regression rather than iterating on taste.
  Managed client DLL SHA-256
  `58de4eb816f7e24ee04cf3f83163bb80f0c3cf539efeff4235d9ee3208c2851d`. Exactly
  one payload changed against `-5`, the other 53 were byte-identical, and all 54
  public payloads matched their published hashes. NOT visually accepted; the
  taper profile and the two colours remain judgement calls.
  Manifest `2026.08.17-5` shipped the first shell shape/outline/material pass.
  Before it, `2026.08.17-4` carried build label
  `retail-LOD island shell preference (ccfb138)`. Cut from a freshly assembled
  pack (3 plugin + 51 gameroot = 54 files). Before shipping, every file was
  diffed against the live `-3` manifest: exactly ONE payload changed,
  `BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll`, and the other 53
  were byte-identical, so this release carries only the rebuilt plugin. All 54
  public payloads were then re-fetched over HTTPS and matched their published
  hashes. `tools/patcher/build-manifest.sh` had a dead `DEFAULT_PACK` pointing
  at a deleted session scratchpad, which failed with a confusing "no plugin/
  under pack"; `--pack` is now required and the error prints the assembly
  recipe.
  The previous manifest was `2026.08.17-3`, build label
  `activate distant island shell waiter (bede97e)`. The first `-2` live pass
  proved both bundles cached but exposed an activation-order defect: Unity
  rejected the material-ready coroutine while its shell object was inactive,
  leaving all renderers hidden. `bede97e` activates the already-inert object
  before arming the waiter. It ships the marked,
  correlated asset-loaded acknowledgement required for safe optional-terrain
  unload/re-entry plus the non-physical low-LOD shell lifecycle. All 54 public
  payloads matched their published hashes.
- **Managed client DLL SHA-256:**
  `1c6f278ee886ad09805fa30807b1f510fde5fa0ba4b8cf97b8bf15e572c92863`
  (the public payload was re-downloaded and matched this exactly).
- **Windows CoreSDK DLL SHA-256:**
  `26b5ce1568abec2ca06d488e3aadaaf725c92a89e1e2482571e27ad31986c354`.
- **Server state:** active on native Linux, UDP 7779. Boot restored 4/4 placed
  deployables, 5/7 ships (two tombstones), 16/16 mounted parts and 3/3 loose
  parts. Stats report schema 6, build `3d64a7f`, host mode
  `local-single-process`, and terrain mode
  `on`. Staged/live game, login and production-built Linux CoreSDK hashes match;
  the Linux shim is SHA-256
  `0121219a138a07f345103f83cc5647f993ecb0282a0172c7bf19a54b78a252f7`.
  The deployed game managed DLL is SHA-256
  `f2c9c288c2266f08448e2f50664abb1e693b11ac3cc0585e2c42914de9602973`;
  the login/admin managed DLL is
  `a4277318cd097e44142a344adc675be02b9381b766b9036ddd779c5e0523f80c`.

### Latest multiplayer incident

- **Server revision:** `ab9bc94`, building on the replication corrections in
  `489517f`. The 2026-08-14 two-player session proved three related failures:
  runtime-created yards/ships were absent from the boot-frozen plan on relog;
  distant ships and mounted parts broadcast motion globally; and absolute ship
  control points bypassed the unreliable-stream policy, building a reliable
  retransmit queue (observed peak 49 KB in flight and 6.8 s RTT).
- The revision adds paced runtime-entity catch-up, a per-peer AddEntity ledger,
  distance/checkout-gated ship motion with pilot/passenger overrides, current-pose
  registry relocation, superseding/unreliable 1130 delivery, idempotent duplicate
  helm Man events, serializer-buffer cleanup, and removal of per-update log spam.
- Validation: Multiplayer tests `2311/2311`; Release game-server build succeeded;
  deployed managed binary hashes matched the local publish exactly. A two-player
  relog/ship-flight/re-entry acceptance test remains required.
- Local follow-up `a5bed13` keeps the normal 20 Hz avatar relay but, after every
  accepted 240 ms ship-domain root frame, immediately relays the latest aboard
  avatar sample to each peer that actually received that root. This gives the
  legacy protocol hull-first ordering on the same server-loop turn without
  pretending cross-entity packets are atomic or reducing avatar movement to
  4.17 Hz. The admin page now reads schema-v2 runtime telemetry for real local
  ship domains: authority generation, replication sequence/frame age, pose,
  pilot/aboard membership, structural counts and checkout subscribers. It is
  explicitly labelled local single-process and exposes no fictional workers,
  migrations or authority controls. Validation: Multiplayer `2322/2322`,
  admin/login `155/155`, both Release builds zero errors.
- `18d89b3` expands `/admin` into World / Simulation / Operations / System.
  Existing player recovery tools remain, and three exact allowlisted world
  operations now complete on the game-server poll loop: reset all damaged
  trees/metal/fuel, recall an uncrewed hull beside a selected connected player,
  and permanently delete an uncrewed exact hull plus its persisted structure.
  Recall/delete reject a piloted or occupied hull. Delete durably tombstones the
  ship before runtime removal and requires typed `DELETE` plus browser
  confirmation. Session-derived CSRF now protects commands, logout and server
  naming. The UI separately renders login-server queue acceptance and the game
  server's atomic completion receipt; it never calls dispatch a successful
  gameplay action. Validation: Multiplayer `2336/2336`, admin/login `168/168`,
  both Release builds zero errors. This is server/admin-only, needs no patcher
  manifest change, and is deployed in `ab9bc94`.

- **Open defect, characterised 2026-08-19: the relay settles into one of two
  states at join and stays there for the session.** Either it forwards an
  accepted position within a millisecond, or it holds every position for one
  whole emit interval - 50 ms, half the client's entire 100 ms interpolation
  budget - before sending it. Both states emit at exactly 20 Hz with zero
  ingest drops, zero backpressure skips and a lifetime cadence-skip count in
  single digits, which is why every existing statistic said the relay was
  healthy. The discriminator is the age of the position in the pending slot:
  `WAREBORN_RELAY_TRACE=1` reports a median of 0.15 ms in the good state and
  50.20 ms in the bad one, measured server-side with no bot involved.
  In fifteen soaks across two trees the bad state occurred six times (40%),
  including on `2bd3113` - a tree predating all of 2026-08-19's merges - so it
  is **not** anything that landed today, and it is not entity count: the runs
  that produced it carried the default Haven world of 127 registrations with
  spatial interest off and no tier-1 island at all.
  The mechanism below that - what decides, at join, which state a session gets -
  is NOT established. Moving `Relay.Tick` below the packet drain was tried and
  measured and does **not** remove it (1 of 4 runs still landed in the bad
  state, against 2 of 4 without the change); that change was reverted rather
  than shipped on a hunch. The plausible next step is a per-sender emit gate
  instead of one global grid, which cannot be done without also deriving the
  synthetic timeline's step from real elapsed time - see the hazard note in
  `RelayCadencePolicy` - and therefore wants its own branch and a two-client
  presentation check, not a drive-by.
  The soak's new level gate (`docs/testing.md`) fails on the bad state, so this
  is now visible rather than inferred. Expect it red on roughly two runs in five
  until the defect is fixed.

Do not put database passwords, session tokens, account records, or private
connection strings in documentation, commits, commands whose output is pasted
into chat, or issue reports. In particular, avoid printing the full systemd
`Environment=` property. A production database credential currently exists in
a systemd drop-in; rotate it and move it to a root-only `EnvironmentFile` when
doing the next security/operations pass.

### Deployment discipline

`docs/hosting.md` now contains only the current native Linux deployment flow;
the mixed Wine/native instructions are preserved under `docs/archive/` for
rollback history. The safe native flow is:

1. build/test the selected commit;
2. publish the game server for `linux-x64` self-contained into a fresh staging
   directory;
3. build and include `libCoreSdkDll.so` if native shim sources changed;
4. preserve/backup the remote `data/` directory and current native deployment;
5. sync staged files without deleting persistent data;
6. restart only after the user confirms every player is disconnected;
7. inspect boot restore counts, resource-interest banner, hull metrics, and
   first connection logs.

The production VPS is Ubuntu 24.04 with protobuf 3.21 (`libprotobuf.so.32`).
Do not deploy a native shim built on the Arch/CachyOS development host: that
currently links protobuf 35 plus its newer Abseil libraries and will fail to
load on production. The proven PR4 rollout copied the shim sources to an
isolated VPS build directory and ran `tools/relaybot/build-coresdk-native.sh`
there, then verified `ldd` and `ENet_EXP_PeerChannelCount` before installation.

Update `docs/hosting.md` with the exact proven command during the next server
deployment rather than guessing it here.

## 5. Client release flow

Building `WorldsAdriftReborn` normally copies the DLL directly into the local
game plugin folder. A running game keeps the already-loaded assembly, so every
client change requires a full game close/relaunch.

Public release procedure is documented in `tools/patcher/README.md`:

1. assemble a pack containing `plugin/` and `gameroot/`;
2. put the newly built `WorldsAdriftReborn.dll` in `plugin/`;
3. run `tools/patcher/build-manifest.sh` with a new version/build label;
4. `rsync -av --delete tools/patcher/dist/` to
   `/opt/wareborn/patch/` (the patch directory is generated and may be deleted;
   this exception does **not** apply to server deployment directories);
5. fetch the public manifest and payload and compare SHA-256.

WAPatch now writes the four public connection keys itself. Players should not
be told to manually edit `BepInEx/config/WorldsAdriftReborn.cfg` for the public
server.

## 6. What this session shipped

The important integrated chain is `13a5303` through `f837c5a`, followed by the
client-only panel iterations through `7c3e6c4`.

### Whole-island Haven resources

- Haven is populated deterministically from extracted collision-surface data,
  not a tiny hand-authored spawn patch.
- The live starter-biome profile is intentionally birch/iron weighted rather
  than a random species/material assortment.
- Current full tables: 81 birches (including the proven starter anchor), 40
  iron deposits, fuel pods, databanks, and atlas shard companions.
- Full deposits now use all three shipped `MetalDepositVisuals` shapes in a
  deterministic 01/02/03 placement-index cycle. Variant 03 is the tall formation
  seen in historical footage; biome controls material while metal type/quality do
  not select shell geometry. `WAREBORN_DEPOSIT_VARIANT` remains a global test
  override. The adjacent boulder, nugget, scrap, tree, databank and fuel-pod
  boundaries are recorded in
  `docs/research/findings-resource-visual-variants.md` rather than guessed into
  the same contract.
- The old 1010/1011 idea is not viable with the player client: retail's island
  resource sampler lived in server-side Unity workers and is absent from the
  shipped player binary. Offline generation is the correct current fallback.
- Core files:
  `Resources/SurfacePlacementGenerator.cs`, `Resources/HavenSurface.cs`,
  `MetalDeposits.cs`, `Trees.cs`, `FuelPods.cs`, and
  `Game/Gathering/WorldResourceActivation.cs`.

### Resource interest and authority

- Nearby resources join the loading barrier; distant resources are not sent at
  login.
- Connect-time ship interest now applies the ship domain's 800 m load radius to
  the hull root and makes one decision for its hull, deck panels and mounted
  parts. Remote ships are not instantiated and immediately removed during login;
  live ship interest adds them root-first when approached. Free loose parts stay
  outside this rule so they cannot become unreachable.
- First live acceptance of `718d926` exposed a second visibility owner: generic
  runtime catch-up re-sent the remote built ships/mounted parts after the plan
  had correctly skipped them, producing a large send/remove burst. `9143c5a`
  excludes ship-managed entities from generic catch-up and adds a headless
  connect -> catch-up -> approach test. Validation is 2,316/2,316 tests and a
  zero-error Release server build; it is deployed as part of `ab9bc94`.
- Movement component 1073 drives a 500 ms per-peer reconciliation.
- Adds are nearest-first and paced at 120 ms with asset request then AddEntity.
- Runtime deposits/shards enter interest through explicit `RegisterRuntime`.
- Component-interest is guarded by per-peer checkout state so a late request
  cannot resurrect an unloaded entity.
- Dynamic resources are activated with the same authoritative harvest state as
  boot resources; this fixed the prior symptom where streamed trees/rocks were
  visible but yielded nothing.
- Native channel 5 carries `RemoveEntity`; the Windows x64 `long`/`int64_t` ABI
  mismatch was fixed in `fc4efec`.
- PR4 makes the 1073 coordinate frame island-aware. A terrain `relativeTo`
  selects the peer's stable `IslandId`; aboard-ship and teleport positions use
  global coordinates directly.
- Resources are assigned to an owning island. Reconciliation includes the
  active island plus old loaded entries long enough for hysteresis removal;
  never-visited distant-island resources remain unloaded.
- Remove capability is now explicit: the native shim exposes the peer's
  negotiated ENet channel count. Six-channel peers unload through channel 5;
  older peers retain visited resources without risking inert re-adds.
- The Trades Challenge carries only its recovered profile: five Aluminium Q4
  deposits and five databanks, with no invented trees, fuel or ore assortment.
  See `docs/research/findings-multi-island-resource-interest-pr4.md`.

This matters to the earlier report that one friend crashed during loading while
the host only lagged: initial radius gating reduces the boot burst, but retained
visited resources can still accumulate. No final Colin-specific crash diagnosis
was established from the one available log.

### Spawn/load reliability

- `SpawnAckTimeoutPolicy` prevents one lost acknowledgement parking the rest of
  the spawn plan forever.
- The connect-time interest boundary no longer sends the final gated AddEntity
  without its asset request.
- Global biome data, placed deployables, loose parts, hulls/decks and other
  load-bearing entities are in the initial barrier where appropriate.
- World prefabs are precached; client rescue now acknowledges the associated
  request.
- See `LoadBarrierPolicy`, `SpawnAckTimeoutPolicy`, `SpawnPlan`, and
  `Patching/SpatialOS/AssetLoadAck_Patch.cs`.
- The 2026-08-14 post-PR4 crash audit found no duplicate *world-resource*
  AddEntity and no post-activation server packet burst in the second failed
  run. The installed client DLL was byte-identical to the one used by an
  82-minute known-good run. The actual server-side regression was the coupling
  of the 120 m roaming radius to the unpaced loading-barrier initial set: after
  fixing the old producer race, more in-radius resources correctly moved into
  synchronous connect-time instantiation. Keep connect radius, live radius and
  the settle window separate; do not "fix" this by re-enabling concurrent spawn
  producers. A later real-wire two-peer audit did expose the separate remote
  avatar mirror retrying AddEntity three times and racing live 1073 ahead of its
  seed. Production mirror creation is now single-shot, movement is held until
  AddEntity plus both 1073/190602 seeds are served, and tier 2 fails on either a
  duplicate avatar or non-monotonic timestamp. Departed avatars are removed on
  channel 5 instead of being left as ghosts. Runtime rendering still requires a
  live client confirmation.

### Stations and placement

- Shipyard and assembly station placement is authoritative, persistent, shared,
  and restored before spawn-plan snapshotting.
- Station pickup returns the inventory item; the client hides the static
  placed root only after the authoritative interaction-enabled transition.
- Empty shipyards can capture an existing persisted ship again, enabling
  sequential builds once the prior hull has departed.
- The dock registry is bidirectional. First non-neutral piloted input undocks
  the hull, clears persistence, and updates yard/hull dock components.
- Core files: `Game/Placement/PlacementService.cs`, `PlacedShipyards.cs`,
  `PlacedCraftingStations.cs`, `ShipDockRegistry.cs`, and
  `Game/Crafting/BuiltShipSpawner.cs`.

### Flight, helm and sails

- The server owns ship flight integration and publishes 1130/190602 state.
- Voluntary helm release latches forward/reverse throttle and clears transient
  pitch/yaw/roll/vertical axes. Explicit zero settles the ship. Disconnect uses
  a separate emergency-neutral `Abandon()` path.
- Re-manning seeds the delta ledger from the latched state so an omitted field
  cannot reset the lever.
- Helm entry snaps the local body/camera to the authored `#PilotPosition` anchor.
- Sails are functional. The current reconstruction adds linear forward
  speed/acceleration per unfurled sail, bounded by flight speed policy. It is
  not yet retail wind/tacking/rigidbody torque simulation.
- Ship-part interaction holds are clamped to a short consistent duration.
- Core files: `Ship/Flight/`, `Game/ShipFlightService.cs`, `Sails.cs`,
  `Patching/Flight/PilotBodyAnchor_Patch.cs`, and
  `Patching/Flight/HelmInteractTime_Patch.cs`.

### Crafting and ship-part materialization

- Craft reservations return excess material instead of consuming the entire
  dragged stack.
- Every current loose-part catalogue row now has the component state required
  to materialize: panels/windows, decks, modular engine/wing parts, utility
  items, helm, sail, sky core, lights, storage, etc.
- Crafted loose outputs occupy deterministic non-overlapping slots; a persisted
  overlap migration separates old coincident outputs.
- Attachments use normalized placement/interaction policy rather than one-off
  fixes per lamp/helm/sail.
- Core files: `LoosePartCatalogue.cs`, `LoosePartDefinition.cs`,
  `LoosePartPlacement.cs`, `Game/Crafting/LoosePartSpawner.cs`, and component
  branches in `ComponentsSerializer.cs`.

### Ship persistence and salvage

- Built hulls, decks, mounted parts, loose parts and dock relationships persist.
- The deck restore path avoids applying hull rotation twice (`c7e71c8`).
- Shipyard UI frame salvage removes the docked frame transactionally, refunds
  recipe materials, and drops attached parts.
- The salvage weapon can dismantle mounted parts only inside an owned shipyard,
  refunding their recipe materials and removing them from world/persistence.
- The generic policy covers the complete catalogue rather than only helm/light.
- Core files: `ShipSalvageService.cs`, `MountedPartSalvageService.cs`,
  `ShipSalvagePolicy.cs`, `ShipPartSalvagePolicy.cs`, and
  `WorldStatePersistence.cs`.

## 7. Active issue at handover: panel exterior placement

This remains **visually unaccepted**. Do not report it as solved without a new
in-game screenshot and trace.

User expectation: a medium panel aimed at an upper frame rail should sit above
the visible outer frame, not intersect an inner member or hang beneath it.

History:

- `a224cd7`: first exterior recast, but panel detection missed inactive phantom
  children, so it never ran.
- `171a2e5`: detects inactive phantom/original panel correctly.
- `b2204c1`: probes six exterior directions, but the generated `ShipSideHull`
  has roof holes and could return no upward SRC hit.
- `7c3e6c4`: for a vertically struck rail, measures live rendered hull bounds,
  places the panel 6 cm above the hull envelope, forces ship-up normal, and logs
  successful projection or fallback.
- Live traces from that build proved the general side path still applied a
  `0.00 m` correction: the exterior recast found the same beam skin and left
  the pivot there, embedding the inner half of the 0.10 m panel thickness.
- `355d842`: moves the pivot 0.06 m along the sign-corrected sloped exterior
  normal (5 cm half-thickness plus 1 cm clearance), and logs actual rendered
  and collider projection ranges relative to the selected hull skin.
- Public client containing the last change: WAPatch `2026.08.14-7`.

Next acceptance steps:

1. fully close and relaunch the client (the running process cannot load a new
   assembly);
2. lift a fresh/recovered medium panel and aim at the same upper rail;
3. capture a screenshot before confirming;
4. inspect:

   ```bash
   rg '\[WAR\]\[ship-panel\]' \
     /home/ttanurhan/Games/WorldsAdrift/BepInEx/LogOutput.log | tail -30
   ```

5. For the pictured side rail, the expected trace contains
   `SRC exterior ... pivot clearance 0.06 m`, followed by a `[geometry]` line
   with `pivotFromSkin 0.060 m`. Renderer/collider minima should be zero or
   positive; a negative minimum is measured penetration.
6. If visually correct, place it, reconnect, and verify the persisted pose.
7. If wrong, use the logged skin, pivot, renderer/collider ranges, original
   local point/normal and result; do not add
   another blind constant.

## 8. Persistence model and safety

The game server is a single poll loop. Most ledgers intentionally are not
thread-safe. World state writes are atomic JSON transactions.

Key persistence entry points:

- `Game/Persistence/WorldStatePersistence.cs`
- `Multiplayer/Persistence/WorldStateSnapshot.cs`
- `Game/Inventory/InventoryPersistence.cs`
- `Game/Knowledge/ProgressionPersistence.cs`
- account/roster persistence in `WorldsAdriftServer` and
  `WorldsAdriftReborn.Storage`

Before a persistence migration or destructive gameplay test:

1. resolve the actual `WAREBORN_DATA_DIR`/live file;
2. copy the specific file to a timestamped backup;
3. verify restore counts after restart;
4. never delete the whole deployment or data directory;
5. record whether a rollback loses post-backup player progress.

## 9. Elastic Simulation Runtime and world expansion

Three external design documents informed discussion:

- `/home/ttanurhan/Downloads/Telegram Desktop/WAREBORN_ELASTIC_SIM_RUNTIME_CODEX_HANDOVER_V2.md`
- `/home/ttanurhan/Downloads/WAREBORN_CODEX_HANDOVER_PR1_ISLAND_IDENTITY.md`
- `/home/ttanurhan/Downloads/WAREBORN_WORLD_EXPANSION_ROADMAP.md`

They are **design inputs, not implementation status**. At this snapshot:

- there is no `SimulationCore` project;
- there is no `SimulationEntityId`, `SimulationDomainId`, domain scheduler,
  authority generation, gateway/worker split, or migration protocol;
- PR1 stable `IslandId`, `IslandDefinition` and `IslandRegistry` are implemented;
- the preserved WAMap importer, production Trades Challenge terrain and
  island-aware resource interest are implemented and deployed through the
  staged resource-login server revision;
- Phase 1 region topology now exists as dependency-free `RegionId`,
  `RegionDefinition` and `RegionRegistry`. It maps both proven islands exactly
  once but is deliberately not connected to runtime behavior yet.

### Agreed strategic direction

- The client must continue to see one server/gateway and the existing protocol.
- Do not build multi-process meshing now.
- First make boundaries describable inside one process and one poll loop.
- Natural future authority units are islands and whole ships. Never distribute
  a hull, helm, sails, mounted parts and aboard players across independent
  authorities.
- Strong physical interactions (later grapples/ship collisions) imply temporary
  domain affinity or merging.
- Authority generations are required before any migration so stale-worker
  writes can be rejected.
- A ship capture/destroy/restore/resume experiment is the best later proof of
  domain snapshot completeness.

### Current architecture sequence

The accepted phased plan is
[`architecture/elastic-runtime-phases.md`](architecture/elastic-runtime-phases.md).
Phase 1 stable region topology and Phase 2's read-only world directory are
implemented. The first whole-ship portion of Phase 4 is now implemented locally:
`ShipDomain` owns a hull's flight session, pilot authority, generation, deck and
mounted membership, aboard peers and a versioned resumable snapshot. Live helm
input carries an authority token and stale-generation input is rejected.

Replication now evaluates interest once for the whole ship and emits each
flight frame in root-first order: hull 1130, optional hull 190602 wake, then the
mounted-member 190602 wakes. The legacy ENet operations remain ordered rather
than atomic, because the shipped client protocol has no multi-entity update op.
The server logs sampled `[ship-domain]` generation/sequence/delivery counters.

Whole-ship checkout is per viewing peer and uses ship-specific island-scale
radii (800 m load / 1,000 m unload by default) plus channel-5 RemoveEntity.
These are deliberately separate from the much tighter resource radii. An empty
ship may unload for Colin while remaining checked out and moving for a nearby
observer; checkout never parks, freezes, migrates or deletes its `ShipDomain`.
Unmanned/uncrewed ships leave member-first/root-last
and return root-first/member-last on a 120 ms cadence. Pilot/aboard protection is
revalidated at send time. Because remote player entities are still globally
relayed, any crew or active pilot temporarily pins the complete ship globally;
otherwise a far observer could retain a floating avatar after its ship unloaded.
Older clients without RemoveEntity retain both the ship and its motion rather
than freezing a ghost. Late component-interest is rejected after unload.

Passenger carry keeps the exact raw contact entity required by the legacy
client while canonicalizing hull/deck/part membership to one ship root. A one-second
grace absorbs collider-seam `relativeTo=-1` flicker; real island/non-ship leaves
remain immediate. The first two-player production acceptance on `6a2273f`
failed in three bounded ways: remote avatars ran ahead of/behind their moving
ship, a small helm turn after an idle period took exactly five seconds to become
visible, and a removed ship did not reliably re-checkout on return. The exact
five-second delay is now proven to be the retail client's slow spline correction
after our manned-idle 1130 stream went quiet; the local fix keeps a 240 ms stream
while manned and primes it before enabling controls. The avatar divergence was
raw `relativeTo=-1`/bias-zero collider-seam churn being relayed before canonical
aboard state; the local fix holds only those coordinate-frame edges while the
canonical ship survives its measured grace. The re-checkout loop discarded an
in-flight asset request every 500 ms reconcile; the local fix carries a still-
valid head request and revalidates every Add/Remove at send time. All three fixes
are deployed in `ab9bc94` but are not yet visually accepted. Phase 4 is therefore
deployed as a foundation but is **not visually accepted**.

The protocol/state-machine portion now has a repeatable two-peer acceptance
gate at `tools/relaybot/run-ship-acceptance.sh`. It creates a disposable world
and alternate-port native server, then drives two real ENet peers through
flight, mounted-member wakes, passenger contact-seam suppression, authority
handoff with stale input, independent whole-domain removal, and legal re-entry.
The 2026-08-15 run passed every assertion. This replaces Colin as the first-line
server regression test; it does not run Unity visualizers, interpolation,
camera/IK or rendering, so the phase remains visually unaccepted until a short
two-client presentation check.

Phase 5 has a pure capture/restore/resume proof, but not yet the full live
destroy/recreate/no-visible-teleport acceptance test. Phase 6 has ship authority
generations, but no in-process gateway seam yet.

### First tier-1 B3 terrain expansion (local, off by default)

The next release-world terrain cluster joins the preserved Bossa MapFile to the
final Cardinal survey. The complete Saborian tier-1 B3 district contains twelve
islands; its first four staged entries remain Mental Facility, Betrayal of the
Copper King, Highlands Hills and The Land that Man Forgot.
`WAREBORN_FIRST_REGION_TERRAIN_COUNT=0..12` selects a bounded terrain-only
prefix; zero is the default. Geographically closer C6 islands are
tier 3 and are intentionally deferred. All runtime topology consumers share
the same configured island/region registries, so spawn, resource routing,
directory ownership, local domains, databank parent resolution and admin stats
cannot disagree about which islands exist. Build `069a372` is deployed with the
bounded rollout set to exactly one terrain (`Mental Facility`); it is not yet
visually accepted. Mental Facility has the first guarded named landing destination,
`mental-facility`, derived from its extracted top surface; both the game server
and admin page refuse it unless at least the first tier-1 terrain is registered.
Do not jump to the complete district at once. Continuous distance checkout is integrated into
`feat/island-identity` at `7cbb376`, with exact cold-asset ACKs,
terrain/resource ordering and safe teleport deferral. It is deployed and enabled
for the one-island run, but is not visually accepted. All twelve bundles total
roughly 116.5 MiB compressed; the
original four-island acceptance prefix is roughly 42.5 MiB. Release-map origins,
terrain envelopes and joined survey profiles (databanks, revival chambers,
trees, turret/danger flags and metal tables) are pinned for all twelve, but no
new dynamic resource population is enabled by terrain registration alone.
See `docs/research/findings-first-region-terrain.md`.

Production verification after the `069a372` restart: stats schema 5 reported
`firstRegionTerrainCount=1`; the directory classified Mental Facility into
`tier1-b3-region`; the local host reported 3 island domains, 5 ship domains,
255 owned entities, 0 unowned entities and 0 ownership issues. The count-one
setting is a runtime systemd test override and therefore intentionally disappears
on VPS reboot unless promoted after visual acceptance.

### Complete release-world rollout (local, off by default)

Steps 1–5 of the release-world expansion are implemented locally behind
`WAREBORN_RELEASE_WORLD_DISTRICTS`. `all` selects all 254 ordinary MapFile
islands; an exact comma-separated cell list such as `B3,C6` enables a staged
district rollout. Startup refuses the rollout unless both
`WAREBORN_INTEREST_RADIUS_M` and `WAREBORN_TERRAIN_INTEREST_ENABLED=1` are also
valid, preventing an accidental all-world connect plan. Haven remains its one
active #5 placement; the other eleven preserved Haven positions remain map
evidence only.

The embedded generated catalogue contains 254 unique definitions, all 254
collision AABBs, one 16-point compact shell outline per island, the exact survey
profiles, 1,930 surface-derived deposits, and all 1,233 surveyed databanks.
The full registry is 255 terrains grouped into the exact 20 MapFile cells plus
Haven. The two null Tier-4 cells retain stable `unassigned-t4-*` internal ids;
no E1/E2 labels are invented. Holy Ruins deliberately retains both conflicting
facts: Tier 3 in the final community survey and location in Bossa's Tier-2 A4
cell. The source generator is
`tools/world-import/generate-release-runtime-catalog.py`.

The v2 shell's shape and data were corrected on 2026-08-17; the fixes are local
and **not visually accepted**.

- **It was drawn in the wrong place.** The mesh spanned `MinY` to
  `MinY + 45%` of the envelope - the BOTTOM 45% - so it showed the island's
  underside and omitted the plateau its own outline was sampled from. The
  silhouette sat a median **121 m** (up to **411 m**) below the terrain it stood
  in for, so an island read as hanging too low and then jumped when the physical
  terrain replaced it. The mesh is now a plateau cap at the measured `MaxY` with
  the underside tapering to a keel at the measured `MinY`. Only the taper profile
  (a ring at 45% height inset to 72%) is invented; rim radius, rim height and
  keel depth are all measured.
- **12 islands were pinched into spikes.** An empty angular bin in
  `shell()` emitted a UNIT vector, placing a 1 m radius point between neighbours
  hundreds of metres out - 83 points, the worst 1 m against a real 599 m extent.
  The first repair reused a neighbour's RADIUS at the missing angle and overshot
  the other way, putting 66 points outside their own island, the worst by 383 m.
  The shipped fix interpolates the POSITION along the chord between the two
  nearest measured samples, which is inside their convex hull by construction and
  is the same rule the deposit/databank filler already used. The regenerated
  catalogue has zero degenerate points and zero points outside their island, with
  the 254/1233 counts unchanged (deposits were 354 at the time; see below).
- **It read as a flat cut-out.** `Unlit/Color` ignores scene lighting AND
  distance fog, so the shell was pasted over the sky exactly where atmosphere
  should dissolve it. It is now a lit, fog-aware material in two submeshes so the
  plateau and the rock beneath it read differently, which at this distance is
  most of the shape cue. The two colours are a judgement call and are the part
  most likely to need adjusting on sight.

A per-angle top height would follow the real skyline instead of a flat rim, but
that needs a v3 marker carrying a height per outline point; it is not done.

Under the full rollout distant visuals use the v2 procedural shell: the server
sends the compact outline only for islands within 9 km, and the client builds a
non-physical mesh without loading the terrain bundle. At the 1.2 km physical
radius the existing correlated asset checkout replaces that shell with full
terrain, collision and nearby resources. The v2 shell is a **scalability
fallback, not a preference**: it exists because 254 island-bundle prefetches per
peer are not affordable. `IslandShellFidelityPolicy` makes that an explicit
decision keyed on whether the release-world rollout is active, so the bounded
configuration keeps requesting the v1 retail-LOD shell even though its islands
are also records in the embedded 254-island catalogue. Catalogue membership
alone never selects v2, and v2 can never be selected for an island that has no
outline to encode. A near-band fidelity upgrade (replacing a placed v2 shell
with a v1 mesh as a viewer approaches) is deferred: the client dedups shells by
terrain entity id and both entry points re-acknowledge instead of rebuilding, so
an upgrade needs a client teardown path that does not exist yet. The full
rollout is **not deployed and not visually accepted**.
Trees, revival chambers, turrets and weather-wall gameplay are not spawned by
this milestone; their survey facts are retained for later systems.

### Tier 1 (Wilderness): the complete A2/A3/B2/B3 region (local, off by default)

Resources on release-world islands ALREADY WORK. The two statements that looked
contradictory describe two different flags:
`WAREBORN_FIRST_REGION_TERRAIN_COUNT` registers terrain roots only (that is the
"no new dynamic resource population" sentence above), while
`WAREBORN_RELEASE_WORLD_DISTRICTS` registers terrain PLUS every catalogued
deposit and databank for the selected cells (that is the 1930/1233 assertion in
`ReleaseWorldCatalogTests`). Both are true.

Tier 1 is exactly map cells A2, A3, B2 and B3: 46 islands, all tier 1, and those
four cells contain nothing else. `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1` (or
`t1`/`wilderness`) now names that from the catalogue's own `cellTier` so it
cannot drift; the explicit cell list still works and the selectors compose.

Its content is **328 deposits, 215 databanks, 328 atlas shards**, 12 islands
with surveyed revival chambers and 14 with surveyed tree species. Every one of
the 46 islands now has metal.

It was 46 deposits on FOUR islands until 2026-08-18. The catalogue applied its
density rule only where the Cardinal survey recorded a PvE metal table, and it
recorded one for just 38 of the 254 islands. That turned out to be a coverage gap
in a player-submitted survey, not a barren world: the survey visited all 254
islands (every one has a surveyor name and an exact databank count), its own map
UI renders an empty list as "No metals data", and it had five weeks between
Update 31's new map and shutdown. 216 islands are now populated from a labelled
three-rung provenance ladder - 38 `survey-pve`, 23 `survey-pvp` (no PvE table but
the same island WAS read on the PvP shard), 193 `inferred-tier`. The inference is
NOT Bossa data, it is stamped as such in the catalogue and in
`IslandSurveyProfile.MetalSource`, and the raw survey arrays are preserved
verbatim beside it. Full evidence, the derivation and the load numbers:
`docs/research/findings-island-resource-population.md`.

The one real gap, now closed: release-world deposits registered no atlas shard,
so a tier-1 deposit yielded metal but never the shard that is the mining loop's
payoff (Haven and Trades deposits both had one). Each release deposit now
registers its shard immediately after itself, gated by the existing
`WAREBORN_SPAWN_ATLAS` and `WAREBORN_ATLAS_RATE`, with the rate applied to each
island's own deposit index so every island with metal reliably has at least one.

Headless boot at `tier1` against a throwaway data directory: **terrains=47,
regions=5, 481 registrations classified (global=1, region=480, unclassified=0),
`[domain-host] islands=47 ships=0 owned=480 globals=1 unowned=0 duplicates=0`,
433 boot resource activations (215+46+46 release + 81 trees + 24 fuel pods + 21
metal nodes), spawn plan 964 steps, zero warnings/errors.** The 964-step plan is
process-wide, not per-peer: the nearest tier-1 island is 9.33 km from the Haven
spawn and production loads terrain at 4 km, so a fresh Haven connect streams zero
tier-1 terrains and zero tier-1 resources. Connect (45 m), live resource (120 m)
and terrain (4000/4400 m) radii stay separate; nothing here widens resource
interest. At 4 km a median of 9 terrains are physically loaded (min 5, max 12).

Trees and revival chambers are explicitly DEFERRED, each with its cost, in
`docs/research/findings-tier-one-world.md`. Trees are blocked on there being no
evidenced density (deposits use 0.05/cell, databanks have an exact surveyed
count; trees have a species list and nothing else), revival chambers on there
being no server system of any kind. NOTE: 0.05/cell was previously called "the
recovered retail figure" - it is not. The decompile has the field names
(`metalDepositDensity`, `minMetalRockDeposits`) and confirms the island reports
its LOD0 mesh count to the spawner, but the formula lived in the lost Scala
worker. The SHAPE is retail; the value 0.05 is ours.
Distant island shells still need `WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=1`
separately and remain visually unaccepted. Nothing here is deployed or proved
with a real client.

### Terrain checkout observability (integrated by `a4e135c`, deployed)

Stats schema **5** adds a `terrain` section to `/tmp/wareborn-stats.json` so the
one-island visual acceptance run above can be observed instead of guessed. The
game server reads it from `IslandTerrainInterestService` on the same
authoritative poll loop that already ticks the service, and exports immutable
copies only: the read allocates no entity id, sends nothing, and never asks the
resource-drain gate (asking would mutate the send queue), so it cannot become a
second authority. It reports requested-vs-actually-enabled (the resource-interest
prerequisite can hold the feature back), the radii/ack-timeout/settle
configuration, per-peer lifecycle state keyed by **player entity id**, per-island
registration/ownership truth with envelope-backed extents, and a bounded 64-entry
ring of recent lifecycle events. Peer handles, packet payloads and paths are
structurally unable to reach the file: events carry a closed enum and a
process-local slot ordinal.

`/admin` gains a **Terrain checkout** view: a status strip, a player x island
matrix with expandable per-peer detail, an island inventory, the event timeline,
and an acceptance-run panel that drives the EXISTING guarded Haven /
Mental Facility travel commands rather than adding a command path. The semantic
states are `ABSENT`, `REQUESTING`, `WAITING ACK`, `READY`, `DRAINING`,
`UNLOADING`, `RETAINED (LEGACY)` and `ERROR`, derived once in
`IslandTerrainStatePolicy` so the server, the JSON contract and the console
cannot disagree. A schema-4 game server, a disabled feature and a legacy client
each render as a stated condition rather than an empty page. The panel reports
lifecycle only; whether the terrain LOOKS right stays a human judgement and is
never asserted.

The runtime checkout milestone (`7cbb376`) and its admin observability milestone
(`fa83318`) are consolidated on `feat/island-identity` by merge commit
`a4e135c`. They were pushed, deployed and enabled for the bounded Mental Facility
run in `069a372`; public manifest `2026.08.17-1` supplies the correlated native
acknowledgement. Final validation passed all 2,554 Multiplayer tests, all 181
admin/login tests, all affected Release builds and `git diff --check`.

The first real Unity run on 2026-08-17 proved exact request/asset-ack/add ordering,
deferred teleport, correct Mental Facility rendering and collision, and correct
Haven resource removal/re-checkout. It also exposed one real lifecycle defect:
this client proves teleport arrival through the bounded authoritative-transform
route but omits the sparse 1073 relative-to island acknowledgement. Resource
interest advanced correctly, while terrain interest retained the old requested
destination and therefore kept Mental Facility `READY` after the proved return to
Haven. The follow-up makes a proved teleport landing one shared authority event:
both legitimate arrival proofs update terrain ground identity and clear the
destination pin, allowing normal drain/unload. It also reports a queued
`teleport-wait` as an accepted wait rather than a failed operation. Headless
regression coverage proves the return produces exactly one old-terrain removal;
The fix is deployed as `b52f504`.

The repeat production run on `b52f504` completed that gate. One current v1
client performed two full Haven → Mental Facility → Haven cycles in the same
session. Each outbound leg recorded `teleport-wait` (accepted), request, exact
asset ACK, add and teleport-ready; the player visually confirmed correct terrain
and collision after the second post-removal add. Each return advanced resource
interest to Haven, cleared the destination pin and recorded `remove-ok`. Final
telemetry showed both managed islands `ABSENT`, no pending action, zero ready,
zero retained, zero errors and no warning. The one-client teleport-driven
load/unload/re-entry lifecycle is therefore visually accepted. Distance approach
and independent two-client checkout remain separate acceptance gates.

The first proximity run then flew ship 176 from Haven to The Trades Challenge.
At roughly 1,153 m from the extracted terrain envelope, checkout recorded the
exact `request -> asset-ack -> add-ok` sequence and reached `READY` without a
teleport. The player landed, disembarked and visually confirmed stable terrain
and collision. This accepts one-client ship-approach terrain loading, but exposed
a separate on-foot spatial-interest defect: all 15 recovered Trades resources
(five Aluminium Q4 deposits, five Atlas shards and five databanks) remained
unchecked-out even at the island centre. Telemetry showed the interest position
frozen at the disembark point. The client continues sending ownership-gated
authoritative global 190602 transforms while walking, but the interest services
were fed only from sparse 1073 `positionRelative`/`relativeTo` fields that can
stop changing after disembark.

The local follow-up routes each unparented authoritative 190602 player pose into
both resource and terrain interest. It reuses `FallWatch`'s accumulated sparse
parent state so a parented local transform cannot be mistaken for a global
coordinate. Full Multiplayer validation remains 2,556/2,556 with a clean Release
server build. This server-only correction is deployed as `1aa9fe4`; no client
patch changed. Post-restart verification reported schema 5, terrain mode `on`,
the count-one B3 topology, 3 island domains, 5 ship domains, 255 owned entities,
zero ownership issues and zero terrain warnings/errors.

Live acceptance on `1aa9fe4` succeeded: after a ship approach and disembark, the
player walked across Trades and the authoritative interest centre continued to
move. Twelve of the island's 15 entities were concurrently checked out. Four
databanks rendered, accepted interaction and each durably awarded 10,000
knowledge (32,391 -> 72,391). Metal and Atlas-shard deposits also entered and
left the 120 m bubble as the player moved. Terrain remained `READY` with zero
terrain warnings/errors. This accepts one-client on-foot resource and databank
streaming.

The same run exposed a non-blocking approach-boundary conflict: while aboard,
the 1073 + hull-pose source classified interest by the ship's island affinity,
then each 190602 pose independently classified it by nearest island, alternating
Haven/Trades until the boundary crossing completed. The local follow-up gives
the canonical aboard tracker precedence: 190602 drives spatial interest only
when unparented and not aboard; ship-derived 1073 remains the sole aboard source.
The focused policy covers every fall verdict in both aboard/on-foot states; all
2,570 Multiplayer tests and the Release server build pass. It is deployed as
`7fab2e2`; post-restart verification reported matching hashes, the count-one B3
topology, 255 owned entities, zero ownership issues and zero terrain
warnings/errors. No client patch changed.

The player then departed Trades toward Haven. Resource checkout drained all 15
Trades entities to zero before terrain teardown, `remove-ok` succeeded, and the
island reached `ABSENT` with no pending action, warning, error or legacy
retention. This completes the one-client ship-proximity add, on-foot resource and
databank interaction, departure drain and terrain-unload lifecycle. It does not
accept visual presentation: the approach showed a brief magenta material state
before the normal island shader appeared, and the island still visibly pops in
and disappears because there is no persistent distant visual shell yet.

The release MapFile also proves wall geometry. The nearest Haven separator is a
type-5 WorldEndWall about 1.061 km west of active Haven; prior notes treating
exact release wall placement as missing are superseded. Wall behavior remains
unimplemented.

### Interaction shadow model (local, off by default)

The observation layer above domain ownership. `LocalDomainHost` already answers
"who owns what"; nothing answered **"what is expensive to pull apart"**, and
that is the question any future placement decision has to ask first. Behind
`WAREBORN_SIMULATION_MODEL`, default **0**. Branch
`feat/simulation-shadow-model`. Nothing is deployed.

| variable | default | what it does |
|---|---|---|
| `WAREBORN_SIMULATION_MODEL` | `0` (off) | arms the observer. Only the exact string `1` enables it |

**It only observes.** With the flag off, `SimulationShadowRuntime` never invokes
the observation supplier at all — and that supplier is the only channel to live
state, so a disabled observer cannot have read, and therefore cannot have
perturbed, a ship, a player or an interest set. That is asserted structurally in
`SimulationShadowRuntimeTests`, not promised in a comment. Even enabled it sends
nothing, owns nothing and moves nothing; no hot path reads it.

- **Core** — `Multiplayer/Simulation`: `SimulationEntityId`, `InteractionEdge`
  (kind + ordinal strength + latency sensitivity + observed activity),
  `InteractionPressure`, `SimulationWorldModel`, `WorldSnapshot`,
  `SimulationDiagnostics`. Engine-agnostic; `SimulationCorePurityTests` enforces
  that three ways. It reuses the existing `SimulationDomainId` deliberately —
  two spellings of `ship:893` would make the two halves of the inspector
  impossible to join.
- **Adapter** — `Multiplayer/Simulation/Wareborn`: a plain observation record
  and the projection that turns it into a world. The four first edges are
  **containment** (aboard), **control** (helm), **interest** (resource checkout,
  aggregated at the island DOMAIN, never per node) and **proximity** (ship near
  island). `InteractionKind.Environment` is declared and never produced — that
  is the wind wall's seam.
- **Diagnostics** — `[sim]` lines on stdout every 5 s, never per tick.
- **Inspector** — stats schema **v14** `simulation` section; the admin
  Simulation card grows an "Interaction shadow model" block *below* the
  authoritative ownership topology, labelled as an observation overlay.

**⚠ `pressure` is UNCALIBRATED.** Nobody has measured a message rate, a physics
contact rate or a migration cost. It is an ordinal ranking so a panel can sort
by it. Do not gate behaviour on it; the moment something does, invented numbers
become load-bearing and the first real measurement becomes a regression.

**⚠ Members will read low until entities bind.** Island domains own nothing at
boot — a soak on 2026-08-20 logged `[sim] domain island:haven kind=island
members=1`, and that is faithful: `[domain-host] ... owned=0` with 127
registrations still waiting for their `AddEntityOp`. The shadow model reports
the ownership host, it does not guess ahead of it.

**Deliberately absent**, because the vision doc's "do not freeze this API until
real domain implementations expose what is actually required" beats the
handover's PR-1 wish list: `SimulationContract`, `ConsistencyClass`,
`FidelityClass`, free interaction-strength doubles, conserved-quantity string
lists, a graph partitioner — and any `Tick` on a domain.

### Understorms (local, off by default) — S1 of the storm plan

The island lightning event that refreshes resources, server-side and complete.
Behind `WAREBORN_STORMS`, which defaults to **0**; with it unset this server is
byte-identical on the wire to one built without the feature. Branch
`feat/understorm-s1`. Nothing is deployed.

It adds **no component, no migration and no client change**. 1254
`IslandLightningTimerState` is already seeded on every island, and the shipped
`IslandLightningTimerVisualizer` that reads it is baked onto **255 of 255**
island bundles (PROVED by a UnityPy type-tree sweep — the bundles are
compressed, so grep cannot see this). The whole feature is that this server
stops pinning 1254's two integers and starts scheduling them:
`estimatedMilliTillNextLightning` drives the client's own 30-second rumble and
camera shake within 300 m, and `estimatedMilliTillLightningEnd > 0` **is** the
client's storm switch. When **an island's** storm ends, **that island's** trees,
metal nodes and fuel canisters are restored (S2). The world-wide
`ResetHarvestResources()` survives only as the authenticated operator's
`reset-resources all`.

| variable | default | what it does |
|---|---|---|
| `WAREBORN_STORMS` | `0` (off) | the master switch |
| `WAREBORN_STORM_CADENCE_SECONDS` | `6300` (105 min) | per-island storm interval (RECOVERED — `TreeHarvest.UnderstormCadence`) |
| `WAREBORN_STORM_DURATION_SECONDS` | `45` | how long one storm runs |
| `WAREBORN_STORM_JITTER_FRACTION` | `0.2` | spread of islands' storms across the cadence, clamped to [0, 0.5] |
| `WAREBORN_STORM_COUNTDOWN_REFRESH_SECONDS` | `8` | how often the countdown is re-pushed during the warning. **Floored at 8** — see below |

**⚠ Two things a future agent must not re-derive.**

1. **The client's countdown does not tick down on its own.**
   `TimeEstimationSmoother.StepAndSmooth()` computes a decayed value and returns
   it *without ever storing it*; `smoothed` is written only by `OnUpdatedValue`,
   and only when `Mathf.Abs(new - held) > 7f`. It is a shipped bug. So the
   warning exists only while the server re-pushes the countdown, and only when
   each push moves it by **more than seven seconds**. A 5-second refresh buys
   packets and changes nothing on screen. That is why the refresh interval has a
   floor rather than a ceiling.
2. **`isLightningActive` must never be written true.**
   `IslandLocalTransformBehaviour.HandleLightningActiveUpdated(true)` writes the
   island's transform to End-of-the-World doomsday code that lerps Y toward
   −250…−1500 m. The bool buys nothing — the visualiser switches on the int.
   Three absences currently defuse it (empty 1042 `Option`s; no island transform
   authority granted; and the behaviour is on **0 of 255** bundles), and none of
   them is ours to rely on. The update type has no bool field at all.

**Known divergence from retail, stated rather than hidden:** placement is
RESTORED, not re-rolled (retail's client re-sampled the island surface each
time — §14.6.3). §14.10's S3 closes that. The other S1 divergence — one
world-wide reset per generation at the *last* island's storm end — is **closed
by S2** (below). With storms on and `WAREBORN_TREE_RESPAWN_SECONDS` unset,
per-tree regrowth stops and the forest returns with the lightning, which is
retail's shape; setting that variable is the revert path. Felled logs are never
regrown by a storm.

Not yet seen by a human. See §14.10/§14.11 of `docs/plans/feature-roadmap.md`
and the maintainer test script in the S1 branch's report.

#### S2 — per-island reset (branch `feat/understorm-s2`, NOT deployed)

S1 was deployed and watched on 2026-08-20, and the one player-facing defect it
found was **timing**: the reset landed **212 s** after the first island's storm
started, under a clear sky (10:59:57 → 11:03:29 CEST, MEASURED). The mechanism
was right — it restored exactly the 3 trees and 1 node the maintainer had
harvested — but a global reset can only honestly fire once per generation, at
the *last* island's storm end.

S2 scopes it. Each of `TreeHarvest`, `NodeRegistry`, `MetalHarvest` and
`FuelCanisterRegistry` gained a `ResetAll(Func<long,bool> include)` overload;
`ResourceInterestService` exposes the `_resourceIslands` map it already builds
for per-island checkout; `ResetHarvestResourcesOn(IslandId)` joins the two; and
`IslandStormService` keeps a per-island reset generation.

Headless at production's exact configuration (tier1 = 47 islands, 900 s, 0.2,
45 s): worst gap from an island's own storm START to its own reset is **45.05 s**
— the storm's own length plus one 20 Hz loop turn — and 36 of the 47 resets land
before the last island has even begun to storm.

Two traps recorded because they are invisible from the outside:

* the reset call must sit **outside** the 1254 push's early exits, or an island
  whose `AddEntityOp` has not run silently restores nothing *and* replays every
  generation it slept through when it finally does;
* `_resourceIslands` is **empty when spatial interest is off**, so ownership
  falls back to the same `ClosestIsland` the map is built from. Production reads
  `WAREBORN_INTEREST_RADIUS_M=120` (PROVED, read live 2026-08-20), so the map is
  populated there — the fallback is for the off configuration.

No new component, no migration, no client change. Nothing deployed.

## 10. Known risks and unfinished work

- **Panel placement:** WAPatch `2026.08.14-7` is awaiting visual acceptance.
- **Resource unload:** capability is implemented in transport, but runtime is
  load-near/retain-visited compatibility mode as described above.
- **Loading/crash validation:** Colin's remote loading crash was identified as
  native heap corruption (`c0000374`) and fixed in `3a7cd31` / manifest
  `2026.08.14-10`; his subsequent join passed the former crash point. Extended
  play then exposed the separate server replication congestion and connect-time
  whole-fleet loading now addressed through deployed revision `ab9bc94`.
- **Sail fidelity:** functional scalar propulsion, not retail wind physics.
- **Crafted-part sweep:** catalogue contracts are tested, but every visual,
  attach surface and functional interaction has not been manually exercised.
- **Multiple players / moving ships:** `489517f` fixed late-join delivery,
  steering wake-up, passenger-frame coherence and the reliable congestion
  spiral. `ab9bc94` prevents remote domains from burdening login. `6a2273f`
  introduced canonical carry and coherent
  ShipDomain replication, but its first two-player visual pass exposed the
  timeline/re-checkout failures listed above. Do not claim local domains are a
  completed dynamic handoff system: all domains still run in one process and
  there is no gateway host, remote worker, authority transfer, or live snapshot
  restore seam yet.
- **Server restart reconnect:** still session-ending; separate gateway/worker
  architecture is not required to fix the existing shim reconnect path.
- **Belt divider is unlabelled:** `beltRow` is now the correct row index
  (`height - 4`, so 14 on the stock 10x18 grid) and no server path will place an
  item on it, but the row is a blank unusable strip rather than the retail "Belt"
  bar. Retail drew it with a `beltSeparator` inventory item; sending one
  re-creates the 0.1.6.1 belt-separator exploit unless its footprint height is 0,
  because `InventorySpaceChecker.AddItem` overwrites blocker cells. Needs a live
  client test before it ships. **Existing characters pick the corrected row up on
  their next checkout** - the client reads the grid once, at
  `InventoryVisualiser.OnEnable`.
- **Belt is not yet death-protected:** the bottom three rows are the belt in
  geometry only. Nothing drops anything on death yet, so the "items on your belt
  are not dropped" contract is unimplemented rather than broken.
- **Hosting docs:** native runtime description is current, game deploy command
  is stale Wine-era text.
- **Roadmap:** historical and stale; reconcile it against this file/current code.
- **Security:** rotate the database credential exposed through the systemd
  environment/drop-in and use a root-only environment file. Never reproduce the
  old value.

## 11. Investigation playbooks

### Client visual/interaction bug

1. reproduce once and save screenshot;
2. inspect `BepInEx/LogOutput.log`, `UnityClient@Windows_Data/output_log.txt`,
   and `CoreSdk_OutputLog.txt`;
3. locate retail class/method in the decompile;
4. identify whether the failure is component state, asset load, transform,
   interaction timing, or server rejection;
5. add low-volume event diagnostics, not per-frame spam;
6. build client, fully restart, retest;
7. publish manifest and verify public SHA only after acceptance-quality build.

### Server gameplay transaction bug

1. find the inbound component/event handler;
2. identify the authoritative ledger and persistence transaction;
3. place validation in the pure Multiplayer project where possible;
4. test reject paths, duplicate/idempotent paths, restart restoration, and
   cross-player ownership;
5. build server and inspect `git diff --check`;
6. backup state and deploy only with all players disconnected.

### Resource bug

Follow the whole lifecycle:

```text
registration -> interest classification -> asset request -> AddEntity
-> NoteLoaded -> component-interest gate -> authoritative component seeds
-> damage/harvest handler -> yield transaction -> persistence/depletion
-> optional RemoveEntity -> component/ref cleanup -> clean re-add
```

Do not equate “the prefab renders” with “the resource is authoritative.” That
mistake caused the dynamically streamed visible-but-inert trees and rocks.

**There are TWO ledgers of “this peer has that entity”, and asking the wrong one
is a silent no-op.** `EntitySendLedger` (`WorldsAdriftRebornGameServer.SentEntities`)
records entities announced through the connect-time spawn plan and by the fauna,
whale, terrain, placement and crafting spawners. `ResourceInterestService` keeps
its **own** per-peer `Loaded` set and writes nothing to the send ledger — so for
every one of the 13,266 streamed release-world trees, deposits and fuel pods,
`SentEntities.WasSent` answers **no** while the peer is looking straight at the
thing.

This cost falling trees a whole release. `FallingLogService.Drop` gated the log on
`SentEntities.WasSent(peer, treeEntityId)` alone, so every cut built a log, ticked
it down its arc and retired it having shown it to nobody. The evidence was two
adjacent lines in the server log saying opposite things about the same cut:

```text
[tree-fall] log 2000000002 off tree 1125 mask=111111110 ..., shown to 0 peer(s).
[tree-visual] pushed sectionMask=1 for entity 1125 to 1 checked-out peer(s).
```

The tree lost its sections on screen and nothing fell — indistinguishable, to the
player, from “trees just disappear”. Merged code, green tests, and a shipped patch
note claiming it worked.

**The rule:** when a feature needs to know who can SEE something, ask
`GameState.Instance.ComponentMap` — the peer holds components of that entity —
rather than, or as well as, the send ledger. That is the same evidence
`PushTreeSectionMask` uses, so a visual and the entity it hangs off can never
disagree about the audience. `TreeFall.MayShowLog` is the pure form of it.

### Ship transform bug

Keep coordinate frames explicit:

- registry/global pose;
- live flight-session hull pose;
- hull-local mounted-part pose;
- parent marker (`~`, `deck`, etc.);
- packed quaternion composition.

Never apply hull rotation twice. Use `ShipPartTransform`,
`BuiltShipPlacement`, `ShipHullMetrics`, and the existing orientation probe.

## 12. Completion standard

A change is not complete merely because it compiles or a green phantom appears.
For this project, completion normally means:

- pure policy/regression tests pass;
- relevant server and/or client builds pass;
- persistence and ownership behavior are covered where applicable;
- live logs show the intended branch executed;
- visual behavior is inspected for client-facing changes;
- reconnect/restart behavior is checked for persistent changes;
- the exact built artifacts are the ones deployed;
- public patch manifest and payload hashes match for client changes;
- this handover's production snapshot and active issues are updated.

## 13. Upstream credit and project identity

Wareborn is a fork and continuation of the original WAReborn community work.
Preserve upstream copyright/license notices, keep the upstream repository and
community credits in `README.md`, and describe Wareborn additions as fork work
rather than erasing the original project's authorship.
