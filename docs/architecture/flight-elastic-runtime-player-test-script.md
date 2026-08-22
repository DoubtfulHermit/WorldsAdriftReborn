# Player script for flight and elastic-runtime acceptance

Status: user-facing companion to `flight-elastic-runtime-visual-rollout.md`.
This tells the player exactly what to do. Codex owns builds, flags, backups,
telemetry, admin inspection, operator placement and rollback.

There are nine test packs. They do not have to be completed on the same day.
Most packs require one game launch. Restart persistence and worker recovery are
the exceptions because reconnecting is the behavior under test.

## How every pack works

### Before opening the game

Wait until Codex says:

> **PACK X READY — open the game now.**

Do not run WAPatch unless Codex explicitly says a client DLL or manifest changed.
Most flight work is server-only.

Start screen recording if practical. Screenshots alone are acceptable for
stationary checks, but movement, collision, docking and migration are much easier
to diagnose from video.

### What to report

After each numbered step, reply with one of:

- `X-01 PASS`
- `X-01 FAIL — <what you saw>`
- `X-01 UNCLEAR — <what you could not judge>`

You do not need to record server time. Your message gives Codex the marker needed
to find the corresponding journal interval.

### Stop protocol

If something unexpected happens:

1. Release movement keys and do not touch the helm again.
2. Do not teleport, relog, furl another sail or try to “fix” the state unless
   Codex asks.
3. Send `STOP — <what happened>` and, if possible, a screenshot.
4. Leave the game open while Codex captures the live state.

Immediately stop for falling through the world, a violent launch/spin, duplicate
ship, missing deck, detached parts, a stuck loading screen, loss of control, or a
disconnect. Codex decides whether to continue, demote the test hull or roll back.

### Normal controls used by the script

- **Interact** means the normal `E` interaction shown by the client.
- **Neutral/idle** means the physical helm throttle lever is centred.
- **Leave helm** means press `E` once to release it; do not alter throttle while
  leaving unless the step says so.
- **Touch nothing** means no helm interaction, sail interaction, movement command,
  teleport or corrective input.

## Pack A — baseline, interactions and world bounds

Purpose: close the acceptance debt for behavior already live. Craft B is a
disposable ship supplied and positioned by Codex. Do not use the valued ship for
edge tests.

### A0 — login and ordinary ship smoke

1. Open the game only after `PACK A READY`.
2. Log into the normal Hermit character.
3. The moment the world appears, do not move for five seconds.
4. Report where you spawned: deck, island, air or elsewhere.
5. Open the admin panel in the browser and select the ship Codex names.
6. Confirm the ship domain is listed rather than “No registered ship domain.”
7. In game, walk slowly from stern to bow and back along both sides.
8. Look at every deck seam, sail, helm, gauge, container and decorative part.
9. Report clipping, shaking, missing pieces or a part that catches up one tick late.

Expected: solid deck, complete ship, selectable ship domain and no movement while
stationary.

### A1 — interaction reach and control readiness

1. Approach the rear sail until the normal client prompt first appears.
2. Stop exactly there and press `E` once.
3. Confirm the sail changes state on that first press.
4. Repeat with the forward/top sail.
5. Approach the helm from the front until its prompt appears; press `E` once.
6. Leave the helm, then approach from the side until the prompt appears and enter
   again.
7. On the final entry, do not move the mouse. Immediately try throttle up once,
   throttle down once and return to neutral.

Expected: every visible prompt works; no need to stand unusually close or wait
5–10 seconds; no automatic sail toggles.

### A2 — ordinary interior flight

1. Codex confirms Craft B is at least 500 m from a world boundary and below Y=700.
2. Enter the helm with sails furled and throttle neutral.
3. Hold forward throttle for five seconds, then return to neutral.
4. Steer left for three seconds, centre, then right for three seconds and centre.
5. Climb for three seconds, centre, descend for three seconds and centre.
6. Leave the helm and watch the ship and mounted parts for 30 seconds.
7. Unfurl one sail, wait ten seconds, then furl it.

Expected: finite smooth motion, correct control direction, no boundary warning,
no part or player separation.

### A3 — horizontal boundary

1. Remain aboard while Codex operator-positions Craft B just inside +X 17,600 m.
2. Wait for Codex to say `A3 GO`.
3. Enter the helm and apply low forward power for no more than five seconds.
4. Return to neutral and watch; do not fight the resistance.
5. Report whether motion slows smoothly inward or visibly snaps.
6. Codex repositions the ship at the opposite X/Z side.
7. Repeat the same low-power crossing.

Expected: resistance direction reverses correctly on the opposite edge and the
ship remains assembled.

### A4 — hard edge and parked recovery

1. Codex positions Craft B for a slow approach to +X 17,700 m.
2. On `A4 GO`, apply low outward power until Codex says `NEUTRAL`.
3. Centre the lever immediately and watch for ten seconds.
4. Leave the helm.
5. Codex places the unpiloted ship at about X=17,650 m.
6. Touch nothing and watch whether it wakes and moves inward on its own.

Expected: no position beyond the hard limit, no player ejection, and an unpiloted
out-of-band ship recovers inward.

### A5 — vertical boundary

1. Codex positions Craft B below Y=800 with plenty of horizontal clearance.
2. Enter the helm and climb slowly when told.
3. Continue through Y=800 and report the first visible change in climb response.
4. Continue toward Y=1,000 only while Codex says it is safe.
5. At `DESCEND`, command a controlled descent back below Y=800.

Expected: increasing resistance above 800, no pose above 1,000, smooth recovery
and no falling passenger.

### A6 — relog

1. Return Craft B to a safe interior position and complete stop.
2. Stand on deck and tell Codex `A6 READY TO CLOSE`.
3. Close the game only after Codex confirms the logout position was captured.
4. Reopen when told and report where you spawn.

Pack A passes when the ship restores, its domain exists, the deck is solid and
all boundary directions match server evidence.

## Pack B — corrected stopping, faster sails and frame alignment

Purpose: test the retail residual-drag correction and 840 sail calibration in one
continuous flight. Use Craft A, the 3,094 kg two-sail ship. Codex deploys both
commits but marks sail and stopping segments separately in telemetry.

### B0 — prepare at rest

1. Open after `PACK B READY` and go to Craft A.
2. Confirm both sails are furled and the helm lever is neutral.
3. Do not enter the helm for 30 seconds.
4. Watch the world and the ship's instruments for any drift.

Expected: true rest and resting publication cadence.

### B1 — one sail

1. Codex watches heading and tells you how to turn until the ship is near the
   chosen -139-degree comparison heading. You do not need to estimate it alone.
2. Leave the helm at neutral.
3. On `B1 GO`, unfurl exactly one sail.
4. Touch nothing for ten seconds.
5. Report whether acceleration begins immediately and smoothly.

### B2 — two sails and known heading

1. On `B2 GO`, unfurl the second sail.
2. Hold the requested heading for at least 30 seconds. Make only small steering
   corrections when Codex asks.
3. Watch the airspeed/heading instruments and all mounted components.
4. Report any one-tick part correction, deck jump or sail that closes itself.

Expected near -139 degrees: approach roughly 7.6 m/s / 14.8 knots, materially
faster than the old run.

### B3 — stronger and weaker points of sail

1. Codex guides a gradual turn toward approximately +135 degrees.
2. Hold that heading for 25–30 seconds.
3. Record or report the highest stable speed you see.
4. Codex then guides a poor sailing heading; hold it for 20 seconds.

Expected: strong heading approaches about 8.6–8.7 m/s; poor heading remains
slower but not completely becalmed.

### B4 — furl one sail

1. Leave the helm with the lever neutral.
2. Furl exactly one sail.
3. Touch nothing for ten seconds.
4. Confirm the other sail remains visibly open and the ship slows smoothly.

### B5 — measured stop

1. On `B5 GO`, furl the second sail.
2. After pressing `E`, do not enter the helm or touch either sail.
3. Tell Codex `B5 SECOND SAIL CLOSED` immediately.
4. Keep recording and watch mounted components until Codex says the server has
   reached true rest.
5. Report whether the coast felt smooth and whether it still felt unreasonably
   long.

Expected from about 4.1 m/s: approximately 68 seconds and 90 metres, rather than
the old 115 seconds/159 metres. A stronger brake would be a separate balance
decision, so record feel honestly.

### B6 — optional passenger coherence

1. A second player stands on deck without using the helm.
2. Repeat a 30-second two-sail straight run and a gentle turn.
3. Both players report deck slip, avatar offset, part lag or differing ship angle.

Pack B passes when speed increases within its evidence bracket, stopping matches
the corrected curve and the whole ship remains one visual frame.

## Pack C — fixed clock, snapshots and observation-only shadows

Purpose: enable the 50 Hz clock and durable moving-flight snapshot while vector
and collision systems only observe. Use Craft B first. This pack deliberately
contains two reconnects.

### C0 — normal play under fixed stepping

1. Open after `PACK C READY` and board Craft B.
2. Repeat ordinary forward, reverse, left/right, climb/descend and one-sail tests.
3. Cruise continuously for two minutes with gentle turns.
4. Watch for tiny repeated jumps, delayed components, control bursts or changed
   steering feel.

Expected: no visible difference attributable to 20 ms stepping; server reports
zero dropped steps under normal load.

### C1 — stationary restart

1. Bring Craft B to complete rest in a safe area.
2. Leave the helm and stand on solid ground or a location Codex designates.
3. Tell Codex `C1 SAFE TO RESTART`, then close the game.
4. Codex restarts the server and confirms the snapshot/authority state.
5. Reopen only on `C1 REOPEN`.
6. Find Craft B and confirm exact pose, parts, sails and rest state.

### C2 — moving restart

1. Build moderate speed on Craft B.
2. Centre throttle, furl sails and leave the helm so there is momentum but no
   active propulsion.
3. Codex confirms a fresh moving snapshot.
4. Close on `C2 CLOSE NOW`.
5. Codex restarts while no pilot is connected.
6. Reopen on `C2 REOPEN` and report the player's spawn location and visible ship
   location separately.

Expected: ship resumes its saved coast, input is neutral, no ghost pilot exists
and authority generation advances once. Player-relative-to-moving-ship logout is
not yet guaranteed, so do not hide a spawn mismatch.

### C3 — controlled one-second stall

1. Remain aboard Craft B at safe moderate motion.
2. On `C3 WATCH`, keep the camera on the bow and mounted instruments; do not steer.
3. Codex injects one controlled poll stall.
4. Report freeze length, teleport, burst, component split or whether recovery was
   visually clean.

### C4 — shadow confirmation

1. Perform one straight engine/sail run, one gentle turn and one slow island
   approach already requested above.
2. Nothing new should appear visually; Codex compares scalar motion against
   vector and collision shadow telemetry.

Pack C passes when restart and stall recovery are durable and shadows have zero
gameplay effect.

## Pack D — fuel and propulsion lifecycle

Purpose: use Craft C, a disposable engine/generator rig. Codex stages safe fuel
levels so the test does not require hours of waiting.

### D0 — no-engine control

1. Board the zero-engine configuration.
2. Move the throttle up and down for 20 seconds and use one sail briefly.
3. Watch the fuel gauge.

Expected: no engine means no generator burn merely from throttle packets.

### D1 — one and two engines

1. Codex confirms one engine is mounted and records initial fuel.
2. Hold the requested fixed throttle for 60 seconds.
3. Return to neutral and report the gauge change.
4. Codex or the agreed build step adds/enables the second engine.
5. Repeat the same throttle and duration.

Expected: two-engine demand burns according to engine demand, not key presses;
admin and gauge agree.

### D2 — dismount and disconnect

1. Set a moderate forward throttle.
2. Leave the helm without centring the physical lever.
3. Watch for 20 seconds and report whether the engine continues.
4. Re-enter, return to neutral and leave again.
5. Repeat the setup, then close only when Codex says `D2 CLOSE`.
6. Reopen when told.

Expected: clean dismount preserves the physical latched command and continues
burn; disconnect clears connection authority according to the documented policy,
with no ghost pilot or free thrust.

### D3 — run dry while sailing

1. Codex stages low remaining fuel.
2. Unfurl one sail and engage the engine at moderate power.
3. Hold course until the generator runs dry.
4. Do not furl the sail.

Expected: engine force stops; sail force continues; gauge reaches zero once and
never becomes negative.

### D4 — generator identity and restart

1. With partial fuel, detach/pick up the generator using normal gameplay.
2. Remount it and report the gauge.
3. Tell Codex when safe, close for one planned restart and reopen when told.
4. Verify the same partial fuel remains.

Pack D passes when fuel follows the generator part, demand follows engines, and
sails remain independent.

## Pack E — vector authority, lift and collision shadow

Purpose: use authenticated per-hull controls to promote only Craft B while the
game stays open. Codex announces every mode boundary. Collision remains shadow
only in this pack.

### E0 — scalar reference

1. Fly Craft B straight for 20 seconds, turn gently both ways and stop.
2. Codex captures the scalar and shadow reference.

### E1 — vector authority

1. Stop and leave the helm.
2. Codex promotes Craft B and says `E1 VECTOR LIVE`.
3. Repeat the exact straight/left/right/stop sequence.
4. With symmetric engines/sails, watch for unexplained yaw or roll.
5. With the prepared offset-engine/asymmetric-sail configuration, apply low power
   and report the direction of turn.
6. Reverse through zero velocity slowly.

Expected: signs match shadow, no violent spin, no NaN/teleport and predictable
mass response.

### E2 — lift and overload

1. Codex enables lift for Craft B and confirms measured mass/capacity.
2. Under capacity, climb, release vertical input, hover, then descend.
3. Codex adds or identifies enough disposable mass to reach exact capacity; repeat
   a brief climb/hover attempt.
4. Move over capacity; attempt a controlled climb.
5. Report the overload warning and actual vertical response.
6. Remove the test mass and repeat.

Expected: under-capacity control, exact-capacity hover boundary, strict overload
and immediate truthful recovery after removing mass.

### E3 — core-loss safety

1. Only over safe disposable terrain, detach the only core when Codex instructs.
2. Touch no controls and report initial vertical response.
3. Remount it when told.

Stop immediately for a launch, infinite fall, duplicate core or stale lift.

### E4 — collision shadow

1. Make two slow approaches near the chosen island surface without intentionally
   crashing.
2. Perform one faster pass across the shadow proxy line Codex identifies.
3. A second ship approaches from the instructed direction if a second client is
   available.

Expected: no collision-authority change yet. Codex evaluates contacts and false
positives from telemetry.

Pack E passes when vector/lift authority is controllable and collision shadow is
accurate enough to justify response work.

## Pack F — collision response and authentic docking

Purpose: use two disposable ships and preferably two clients. Damage remains off.

### F0 — slow collision response

1. Codex promotes Craft B to collision response and says `F0 LIVE`.
2. Approach terrain nose-first at walking speed.
3. Release power before contact and touch nothing.
4. Repeat as a shallow side scrape.

Expected: stop or slide without tunnelling, huge bounce, vibration or passenger
ejection.

### F1 — faster and ship/ship contacts

1. Perform the controlled faster approach Codex specifies; do not exceed its
   requested throttle/time.
2. With the second client, approach two disposable ships slowly from opposite
   directions.
3. Both players release controls at contact and report what they see.
4. Leave the ships touching/resting for 20 seconds.

Expected: both clients see the same order, no penetration, repeated wake, part
detach or duplicate authority.

### F2 — basic docking

1. Move Craft B to the selected empty shipyard at low speed.
2. Centre throttle and leave sails furled.
3. Enter the approach envelope and release controls.
4. Watch the full position/yaw convergence without correcting it.

Expected: smooth approach and capture, then zero linear/angular velocity; no
radius teleport.

### F3 — sail/engine departure

1. While docked, unfurl one sail and observe whether capture remains stable until
   a valid departure begins.
2. Depart under sail when Codex says `F3 SAIL GO`.
3. Confirm the yard becomes free only after clearance.
4. Redock, then repeat under engine power if Craft B has an engine configuration.

### F4 — two-ship contention

1. Both clients approach the same empty yard slowly.
2. On Codex's countdown, enter the capture region.
3. Do not back away or retry after the first result.

Expected: exactly one ship captures; the other remains controlled and undocked.

### F5 — dock restart and yard removal

1. Dock Craft B and close only after Codex confirms the saved stable yard key.
2. Reopen after the planned restart and inspect pose/state.
3. Codex later removes/destroys the disposable yard while the ship is safely
   controlled; report the ship's transition.

Pack F passes when collision and docking are deterministic, stable and agreed by
both clients.

## Pack G — wind walls, storms and damage

Purpose: cross one selected wall repeatedly with Craft B. Codex first enables
force only. Damage is a separate marked transition later in the same launch.

### G0 — distance bands

1. Codex positions or guides Craft B more than 800 m from the chosen Wind Rift.
2. Fly toward it at low steady power.
3. Report when the curtain first becomes clearly visible.
4. Keep controls steady while Codex calls out 800 m, 400 m, 300 m and 200 m.
5. Describe visual, force, lightning and sound changes at each callout.

Expected: visual influence before physical influence; physics begins near 400 m;
full core inside 200 m; lightning eligibility around 300 m.

### G1 — direction, mass and torque

1. Cross the wall from side A to side B, then turn well clear and cross back.
2. Use the same low input on both crossings.
3. Report lateral/downward push and whether its sign reverses correctly.
4. Repeat later with the prepared lighter/heavier disposable craft.
5. At a Storm/Sand wall, hold the requested heading and report yaw/torque.

Expected: recovered mass attenuation; Wind Rift does not invent yaw; Storm/Sand
torque has stable direction.

### G2 — wall plus terrain/docking

1. Follow Codex's safe route near wall influence and collision geometry.
2. Do not fight an unexpected shove; use the stop protocol.
3. Repeat while docked only at the selected safe test yard.

Expected: no tunnelling, duplicate contact effect or wall force tearing a docked
ship free.

### G3 — damage transition

1. Stop outside the wall while Codex enables damage for Craft B only.
2. Remove valuable cargo and confirm the disposable status.
3. Cross once at the specified path/speed.
4. Do not cross again until Codex confirms the first damage intent/result.
5. Relog/replay only when asked.

Expected: one deterministic event produces one damage result; no duplicate on
replay/restart. Stop immediately if valuable items or another ship are affected.

Pack G passes when bands, vectors, collision ordering and damage identity agree.

## Pack H — multiplayer and performance soak

Purpose: play a representative 60–90 minute route while Codex measures budgets.
This is not a stress test you must micromanage.

### H0 — together

1. Both players meet on the same ship.
2. Walk, use separate sails, alternate helm ownership and sail for 15 minutes.
3. Dock, depart and cross one already accepted wall.

### H1 — separate

1. One player remains with the ship; the second travels to another accepted island
   or ship.
2. Play normally for 15 minutes: movement, harvesting, containers and terrain
   checkout.
3. Report missing/extra remote entities or unrelated lag.

### H2 — reunite and relog

1. Reunite on the ship and continue for 15 minutes.
2. One player disconnects and reconnects once while the ship is safely stationary.
3. Continue normal play for the remainder of the soak.

Expected: inspector clusters split/rejoin truthfully, no peer/aboard/pilot leak,
no growing lag or new component error.

Pack H passes when Codex's p50/p95/p99 and resource budgets remain bounded and
both players report ordinary play quality.

## Pack I — workers, migration and failure recovery

Purpose: prove the elastic architecture. The client always stays connected to the
same gateway. Codex operates workers; the player only observes and performs small
interactions.

### I0 — same-host empty island

1. Stand on or observe the designated empty test island.
2. Do not move while Codex transfers the island to a same-host worker.
3. After `I0 MOVED`, walk, jump and perform one harmless interaction.
4. Stop while Codex transfers authority back to `local:primary`.

Expected: no disconnect, duplicate terrain/entity, interaction loss or visible
teleport; Inspector worker/generation changes exactly once.

### I1 — remote VPS and network faults

1. Repeat the island observation after authority moves to the remote VPS.
2. Walk and interact normally for five minutes.
3. On `I1 WATCH`, stop and watch one fixed landmark while Codex injects bounded
   delay, duplication/reordering and a brief partition separately.
4. After each fault, perform one interaction only when Codex asks.

Expected: no dual authority; uncertain commands fail closed rather than applying
twice.

### I2 — kill the empty-island worker

1. Keep the camera on a fixed island landmark.
2. Codex says `I2 KILLING WORKER` and runs the failure injection.
3. Do not move until Codex says recovery is complete.
4. Then walk and perform one harmless interaction.

Expected: connection survives, one bounded discontinuity at most, snapshot
restores, generation advances once and the healed old worker remains rejected.

### I3 — uncrewed and stationary crewed ship

1. Observe an empty disposable ship while Codex migrates it out and back.
2. Report any hull/part split or pose jump.
3. Both players then stand aboard a stationary disposable ship.
4. Codex migrates it; players walk around and use one sail/helm interaction only
   after promotion completes.

### I4 — moving crewed ship

1. One player pilots at low steady power; the other watches mounted parts and the
   horizon.
2. Codex announces `I4 MIGRATION START`.
3. Pilot holds input unchanged until `I4 COMPLETE`; passenger touches nothing.
4. Both report freeze, jump, orientation disagreement, part lag or lost control.

### I5 — worker loss during flight

1. Repeat low controlled flight on a disposable ship with no valuable cargo.
2. On `I5 HOLD`, pilot keeps one steady command; passenger records.
3. Codex kills the authoritative worker.
4. After recovery, pilot centres input when told and both players verify control,
   parts, position and authority.

Expected: no disconnect, duplicate pilot, split ship or dual authority; recovery
uses one committed snapshot and one generation advance.

Pack I passes only when every stale/healed-worker write is rejected and the World
Inspector timeline matches what both players saw.

## What the player does not need to do

The player never needs to:

- edit environment variables or systemd files;
- SSH to the VPS;
- identify entity IDs manually;
- calculate heading, distance, acceleration or stopping distance;
- decide whether a server restart is safe;
- create or restore production backups;
- infer pass/fail from the admin panel alone;
- run WAPatch unless Codex explicitly identifies a client artifact change.

Codex supplies the craft, destinations, countdowns, headings and go/stop calls and
correlates every player report with logs and Inspector state.
