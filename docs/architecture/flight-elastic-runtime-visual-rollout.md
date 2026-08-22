# Flight and elastic-runtime visual rollout

Status: production acceptance plan, written 2026-08-22. This is the operator and
player-facing companion to `elastic-runtime-phases.md` and
`retail-flight-program-final-integration.md`.

The purpose of this plan is not to switch everything on. It is to change one
observable authority behavior at a time, prove it in the real client, preserve
evidence, and retain a tested rollback after every step. The numbered checks are
observations inside a session, **not separate game launches**.

## 0. Acceptance sessions — approximately nine launches, not 128

Compatible checks are grouped into the packs below. The user normally opens the
game once at the start of a pack and remains connected while Codex marks test
boundaries, captures telemetry and changes only safe runtime/per-hull selectors.
The game is closed only where the pack explicitly requires a server restart.

| Pack | What is grouped | Expected user launches/reconnects | Approximate active time |
|---:|---|---:|---:|
| A | Universal baseline + already-live world bounds | 1 launch | 45–60 min |
| B | Retail residual drag + sail power 840 + ship-domain/part alignment | 1 launch | 35–50 min |
| C | Fixed clock + durable snapshot + vector/collision shadow telemetry | 1 initial launch, 2 planned reconnects | 45–60 min |
| D | Fuel/propulsion lifecycle on the engine test rig | 1 launch, 1 planned reconnect | 30–45 min |
| E | Vector authority + lift/gravity/overload + collision shadow | 1 launch | 45–60 min |
| F | Collision response + docking + two-client coherence | 1 launch per tester | 45–75 min |
| G | Wind-wall/storm force + separately gated damage | 1 launch | 35–60 min |
| H | Final multiplayer/performance soak | 1 launch per tester | 60–90 min |
| I | Same-host/remote worker + empty-island and ship migration/failure | 1 launch per tester; restart only if recovery fails | 60–90 min |

Thus the normal path is about **nine purposeful game sessions**. Packs C and D
contain deliberate persistence restarts; those are the feature being tested and
cannot be removed without ceasing to test restart correctness. A failed pack may
require an additional rollback reconnect, but later packs do not proceed until
the failure is understood.

### How checks are combined inside one launch

- The universal smoke checks are performed while walking to the craft needed for
  the pack; they are not repeated as a separate ceremony.
- Acceleration, heading, one/two-sail behavior, component alignment and stopping
  are one continuous flight in Pack B.
- Shadow systems run beside the authoritative system and are inspected during
  movements the user was already going to perform.
- Collision response flows directly into docking in Pack F: slow approach,
  contact, capture, departure and a second-ship contention test use the same two
  disposable ships.
- Wall distance, direction, torque, collision ordering and damage use successive
  crossings of one chosen wall in Pack G.
- Multiplayer checks are sampled during each relevant pack; Pack H is the final
  long soak, not the first time a second player appears.

### Runtime test controls required before later packs

Boot-only global flags are acceptable for Packs B–D because those systems own
restart or global-clock behavior. Before Packs E–I are integrated, the server
must gain authenticated, audited **test-cohort controls** that can promote or
demote one stable disposable hull/domain at a time without restarting the game
or changing unrelated ships. Each change must:

1. rotate the domain authority generation;
2. reject stale commands from the previous mode;
3. publish the selected mode and transition time in World Inspector;
4. fail closed if snapshot/collision/worker prerequisites are absent;
5. provide an immediate rollback to the last accepted authority mode.

These controls reduce client relaunches without hiding multiple simultaneous
changes. Codex marks a timestamp before each promotion, waits for the inspector
to confirm it, and asks the user to begin the next numbered action. If a stage
fails, the current hull is demoted and the rest of the pack stops.

## 1. Rollout rules

1. **One behavioral transition at a marked test boundary.** A build and one game
   session may contain several compatible default-off foundations, but Codex
   promotes only the named per-hull/domain behavior, records the transition and
   completes its checks before promoting the next one.
2. **Automation before visuals.** Full relevant tests, Release builds, diff
   checks and configuration-source tests must pass before the user is asked to
   open the game.
3. **No restart with players connected.** The user explicitly closes the game
   before every game-server restart.
4. **Every deploy uses `tools/deploy-game.sh`.** Several compatible default-off
   tracks may share that deployment. The tool protects the canonical data
   link, native SDK and world-state hash and refuses connected players.
5. **Back up both code and state.** Record the backup path, build, schema,
   environment switches and `world-state.json` hash before the restart.
6. **Shadow before authority.** Vector force, lift, collision, wall and worker
   systems must first report comparisons without changing motion.
7. **Disposable before valuable.** World-edge, overload, collision, damage,
   docking-failure and worker-loss tests never begin with the user's valued
   ship.
8. **One client before two.** Basic control and persistence pass with one client;
   passenger coherence, ship/ship collision, docking competition and migration
   require a second client.
9. **The client prompt is part of acceptance.** If the client shows an authentic
   interaction prompt, the action must complete from that position. A prompt
   with no response is a failure even if server policy says the distance was
   marginal.
10. **A visual pass is not an architectural pass.** Codex must also verify
    ownership, authority generation, cadence, snapshots, errors and resource
    budgets in the journal and World Inspector.

## 2. Test craft and evidence bundle

### Craft A — the user's heavy ship

- Current restored hull: normally `ship:3639`; resolve it afresh after every
  restart rather than assuming runtime entity IDs are durable.
- Approximately 3,094 kg, two `Sail01` sails, no confirmed engines, and 21
  mounted parts at the 2026-08-22 baseline.
- Use for non-destructive feel, sail, settling, relog and passenger-coherence
  tests.

### Craft B — disposable reference ship

- A small, undocked ship built expressly for edge, overload, collision, docking,
  restart and worker tests.
- Record its hull material, cell/deck count, total mass, part list and owner.
- Never carry irreplaceable cargo. Destroy or restore it from backup after a
  destructive phase.

### Craft C — engine/fuel rig

- A disposable ship with one engine, then two engines, one removable generator,
  a fuel gauge and at least one sail.
- Fuel tests do not use Craft A unless engines are intentionally added later.

### Evidence captured for every run

Codex records:

- build and schema;
- active feature switches and rollback values;
- service start time, PID and restart count;
- world-state hash and backup directory;
- exact hull/domain ID, mass, parts, sails, engines and authority generation;
- five-second flight telemetry from before the action until rest;
- 1130 cadence, fixed-step pressure/drops, ownership audit and error counts;
- before/after positions, speed, elapsed time and distance;
- World Inspector screenshots or JSON for the selected ship/domain.

The user records:

- a screenshot at the start and end of each numbered visual test;
- video for anything involving movement, collision, docking or migration;
- the exact moment an interaction key was pressed;
- what was visible, what was expected and what felt wrong, without attempting to
  compensate silently at the helm.

## 3. Universal visual smoke test

Run this abbreviated set once at the start of each acceptance pack, then repeat
only the affected item after an in-session behavior transition:

- [ ] **U-01 Login:** spawn on solid terrain or deck; no fall, old-position snap
      or extended loading-screen stall.
- [ ] **U-02 Ship assembly:** hull, decks, sails, helm, gauges, containers and
      decorative parts appear once and stay attached.
- [ ] **U-03 Walk:** walk bow-to-stern and around both sides while stationary;
      no clipping, invisible collider, deck vibration or camera drag-back.
- [ ] **U-04 Prompts:** approach each sail and helm from front and side. Every
      visible prompt completes on the first normal press.
- [ ] **U-05 Controls:** man the helm, wait without moving the mouse, then test
      throttle up/down, vertical input and steering. No 5–10 second input delay.
- [ ] **U-06 Neutral:** centre throttle and release the helm. No hidden reverse
      command or spontaneous lever/sail movement.
- [ ] **U-07 Domain alignment:** move for at least 30 seconds while watching
      mounted parts. No part remains behind and catches up on the next tick.
- [ ] **U-08 Relog:** leave from a safe stationary deck, reconnect and confirm the
      ship exists, the admin panel selects its domain, and the player does not
      fall.
- [ ] **U-09 Admin truth:** selected map ship, exact ship domain, owner,
      membership, mass, sails, pilot, speed and position agree with the game.
- [ ] **U-10 Errors:** no visible disconnect, duplicate entity, missing asset,
      permanent pink material, or interaction failure.

Any universal failure stops the phase. Do not continue to a more destructive
test to “see whether it gets worse.”

## 4. Rollout sequence

| Order | Phase | Live change | Required craft/clients | Rollback |
|---:|---|---|---|---|
| 0 / A | Baseline and bounds debt | No new switch | B / one client | none |
| 1–2 / B | Retail residual drag, then 420 → 840 sails | Two marked measurements in one flight session | A, then B / one | legacy drag build; sail override 420 |
| 3+5 / C | Fixed clock/snapshots with vector and collision shadows | Clock authority changes; shadows observe only | B, then A / one | fixed-step flag `0`; disable shadows |
| 4 / D | Fuel lifecycle | `WAREBORN_FUEL_HULL_DEMAND=1` | C / one | flag `0` + restart |
| 6–8 / E | Vector authority, lift and collision shadow | Sequential per-hull cohort promotion | B / one | demote selected hull at each boundary |
| 9–10 / F | Collision response and authentic docking | Sequential per-hull/yard promotion | B / one and two | disable response; restore docking association |
| 11 / G | Wall/storm forces, then damage | Force and damage are separately marked | B / one and two | zero tuning / disable damage |
| 12 / H | Scale and multiplayer | No new mechanics | synthetic fleet + two clients | reduce cohort/caps |
| 13 / I | Remote worker | empty island → empty ship → crewed ship | staged / two | transfer authority to `local:primary` |

Phases 5–11 do not yet have live switches in production. Adding a bounded,
observable, default-off switch—preferably a stable hull allowlist rather than a
global toggle—is part of their integration work, not something the operator may
fake with an undocumented environment value.

## 5. Phase 0 — close existing world-bounds acceptance debt

World bounds are already enabled, but they have not received the complete live
disposable-hull pass. Finish this before enabling another flight foundation.

- [ ] **B-01 Interior baseline:** fly Craft B at least 500 m from a boundary and
      below Y=700. Test sail, forward, reverse, turn, climb, neutral, dismount and
      relog. No boundary event should appear.
- [ ] **B-02 Positive edge:** operator-place the disposable ship just inside
      X=17,600 m and cross slowly. It must decelerate inward without a visual snap.
- [ ] **B-03 Opposite sign:** repeat at negative X or either Z edge. Push direction
      must reverse correctly.
- [ ] **B-04 Hard clamp:** approach X=17,700 m slowly. Position must never exceed
      the limit; player and mounted parts must remain aligned.
- [ ] **B-05 Parked recovery:** place the unpiloted ship at X=17,650 m. It must wake
      and recover inward without helm interaction.
- [ ] **B-06 Vertical band:** climb through Y=800 and approach Y=1,000. Observe
      resistance, then a hard ceiling without player ejection.
- [ ] **B-07 Clear:** move back inside. Inspector and journal must show pushback
      and clamp clearing rather than remaining latched.
- [ ] **B-08 Relog:** reconnect to the legal restored pose; no out-of-bounds
      checkpoint and no fall.

Pass: correct signs, finite poses, no separation, matching journal transitions,
ownership `unowned=0` and `duplicates=0`.

## 6. Phase 1 — recovered residual drag

Integrate `f04e69f` first and keep sail power at 420 for this run. If integration
does not add a temporary legacy/retail rollout selector, rollback is the previous
binary through the guarded deployment tool.

- [ ] **D-01 Rest:** Craft A at rest, throttle zero and both sails furled for 30
      seconds. It must remain visually still and drop to resting cadence.
- [ ] **D-02 Reproduce baseline:** reach approximately 4.1 m/s, centre throttle,
      furl both sails and touch nothing. Record the second-sail timestamp.
- [ ] **D-03 Coast:** expect approximately 68 seconds and 90 metres to true rest,
      not the old 115 seconds/159 metres. No instant snap-to-zero is expected.
- [ ] **D-04 Faster start:** repeat from approximately 8–12 m/s on Craft B. Motion
      must decay smoothly without reversing or oscillating.
- [ ] **D-05 Active braking:** while moving forward, command controlled reverse.
      The ship should brake more strongly before reversing; it must not teleport
      through zero velocity.
- [ ] **D-06 Sail transition:** furl one sail, wait ten seconds, furl the second.
      Each transition removes only its own force and does not rotate or jerk the
      hull.
- [ ] **D-07 Rest publication:** after stopping, mounted parts must receive the
      final aligned frames and then settle to the cheap heartbeat together.

Pass: the recovered 50 Hz reference is within practical live tolerance, zero
propulsion is proved, and stopping has no discontinuity. If the user still finds
68 seconds unacceptable, record that as a separate WAReborn balance decision;
do not falsify the recovered correction to hide it.

## 7. Phase 2 — sail power 420 → 840

Integrate `8662bce` after the drag correction and retain its 840-specific matrix
when resolving the shared test file.

- [ ] **S-01 Known heading:** start Craft A at rest with no engines. Hold roughly
      -139 degrees.
- [ ] **S-02 First sail:** unfurl one sail for ten seconds. Confirm immediate,
      smooth acceleration and only one sail visibly opening.
- [ ] **S-03 Second sail:** unfurl the second sail and hold heading for at least 30
      seconds. Initial sail acceleration should be roughly double the old run.
- [ ] **S-04 Observed point:** expect the ship to approach about 7.6 m/s (14.8 kn)
      near -139 degrees rather than the old 5.9 m/s prediction.
- [ ] **S-05 Strong point:** turn gradually toward approximately +135 degrees and
      expect an approach toward 8.6–8.7 m/s (about 16.8 kn).
- [ ] **S-06 Weak point:** test a poor heading. Speed should be lower but non-zero;
      a badly trimmed sail retains the recovered 30% efficiency floor.
- [ ] **S-07 One-vs-two:** furl exactly one sail while cruising. Speed must reduce
      smoothly and the other sail must stay open.
- [ ] **S-08 Stop:** furl the second sail and repeat D-03. Sail calibration must not
      change the accepted drag curve.
- [ ] **S-09 Light-ship ceiling:** on Craft B, test one through four sails. Four
      sails on an approximately 800 kg reference should remain around or below 38
      knots and must not hit the 60 m/s wire clamp.
- [ ] **S-10 Passenger:** repeat a 30-second two-sail run with a second player
      aboard. No deck slip, part lag or remote-player offset.

Pass: Craft A is materially faster and controllable; no missing or self-toggling
sail; heading response remains coherent; light rigs do not become wire-unsafe.

## 8. Phase 3 — fixed clock and durable snapshots

Enable `WAREBORN_FLIGHT_FIXED_STEP=1` first on a backed-up disposable ship test
window. Do not simultaneously enable vector motion or fuel lifecycle.

- [ ] **C-01 Feel parity:** repeat U-03 through U-07. Watch specifically for small
      20 ms/240 ms interpolation judder or altered steering response.
- [ ] **C-02 Cadence:** cruise for two minutes. The visible root stream remains
      smooth at the existing publication cadence; no bursts after a quiet period.
- [ ] **C-03 Normal pressure:** normal play reports zero dropped steps and no
      fixed-clock pressure events.
- [ ] **C-04 Stationary restart:** stop Craft B, dismount, close the game and let
      Codex restart. Ship pose and rest state must survive exactly.
- [ ] **C-05 Moving restart:** fly moderately, dismount and close only after Codex
      confirms a fresh snapshot. After restart, the unpiloted ship resumes its
      saved coast without resurrecting throttle or a pilot.
- [ ] **C-06 Authority rotation:** reconnect and man the helm. New input works;
      pre-restart authority tokens remain rejected in logs.
- [ ] **C-07 Player location:** because player aboard-relative persistence is not
      complete, verify explicitly where the player reconnects if the unoccupied
      ship moved after logout. Do not mark this as a pass merely because the ship
      itself restored.
- [ ] **C-08 Injected stall:** Codex injects a one-second poll stall on a disposable
      process. The user watches for a packet burst, teleport or control freeze.
      Recovery must be bounded to 25 catch-up steps.
- [ ] **C-09 Rollback:** disable the flag and restart. Legacy pose restore remains
      valid and stale moving checkpoints are not replayed.

Pass: deterministic stepping with no visible regression, durable motion, neutral
restart input, generation+1 and bounded stall recovery.

## 9. Phase 4 — correct fuel/propulsion lifecycle

Only after Phase 3 passes, enable `WAREBORN_FUEL_HULL_DEMAND=1` for Craft C. Keep
fuel thrust gating initially disabled until burn/persistence is visually proven.

- [ ] **F-01 No engines:** throttle and sail a zero-engine ship. No generator fuel
      should burn merely because throttle packets exist.
- [ ] **F-02 One engine:** run a measured throttle for 60 seconds. Fuel gauge and
      admin value decrease together.
- [ ] **F-03 Two engines:** repeat with two engines. Burn scales with active engine
      demand, not player packet rate.
- [ ] **F-04 Dismount latch:** set forward throttle, dismount cleanly and observe.
      The engine continues driving and consuming fuel from the authoritative hull
      command.
- [ ] **F-05 Disconnect:** disconnect while the engine command is active. Confirm
      the documented disconnect-neutral behavior and no ghost pilot.
- [ ] **F-06 Dry engine:** exhaust the generator on a disposable rig. Engine thrust
      stops, but an unfurled sail continues moving the ship.
- [ ] **F-07 Generator identity:** detach and remount the generator. Its fuel level
      follows its stable part identity and does not duplicate or refill.
- [ ] **F-08 Restart:** restart with partial fuel. Gauge and server level agree;
      fuel neither resets to full nor becomes negative.
- [ ] **F-09 Multiple generators:** verify independent levels and deterministic
      selection/burn behavior.

Pass: fuel follows generators, burn follows engine demand, sails remain independent,
and restart/detach cannot duplicate fuel.

## 10. Phase 5 — vector shadow telemetry

This phase must have zero visual gameplay difference. A difference is itself a
failure because shadow output is not authority.

- [ ] **VSH-01 Straight engine:** centre-mounted engine; compare scalar and vector
      force in the inspector while the visible ship follows scalar motion.
- [ ] **VSH-02 Symmetric engines:** mirrored engines should produce near-zero shadow
      yaw torque.
- [ ] **VSH-03 Offset engine:** one side-mounted engine produces the expected signed
      shadow torque without changing visible motion.
- [ ] **VSH-04 Mirrored sails:** shadow lateral/yaw forces cancel approximately.
- [ ] **VSH-05 Asymmetric sails:** stable non-zero shadow torque appears.
- [ ] **VSH-06 Part mutation:** attach, detach and rotate a part. Shadow COM/inertia
      updates once using the new mount revision; no stale or duplicate part.
- [ ] **VSH-07 Scale:** compare 1, 16, 64 and 256 mounted parts. No frame-time or
      publication regression while shadow evaluation is enabled.

Pass: finite deterministic comparisons, classified deltas, no live-motion change.

## 11. Phase 6 — vector flight authority

Add a default-off stable-hull allowlist. Promote Craft B only; keep Craft A scalar
until the disposable pass and rollback both work.

- [ ] **V-01 Centre thrust:** accelerate straight with a centre-mounted engine; no
      unexplained angular drift.
- [ ] **V-02 Symmetric thrust:** mirrored engines remain straight over eight
      headings.
- [ ] **V-03 Offset torque:** one offset engine visibly yaws in the shadow-predicted
      direction and magnitude bracket.
- [ ] **V-04 Mirrored sails:** sail-only departure remains approximately straight.
- [ ] **V-05 Asymmetric sails:** turn direction matches shadow; no roll explosion.
- [ ] **V-06 Mass order:** light, reference and heavy rigs accelerate monotonically
      by thrust-to-mass.
- [ ] **V-07 Full stop/reverse:** cross zero velocity smoothly with no orientation
      flip or NaN quarantine.
- [ ] **V-08 Restart:** linear velocity, angular velocity and orientation resume
      without a visible teleport.
- [ ] **V-09 Two players:** passenger and remote observer see the same hull attitude;
      deck, avatars and parts do not split frames.
- [ ] **V-10 Per-hull rollback:** return Craft B to scalar authority without changing
      its identity, parts or saved pose.

Pass: shadow/live signs agree, state is durable and finite, passengers remain coherent.

## 12. Phase 7 — authentic lift, gravity and overload

Use Craft B with measured capacity. Gravity must enter once; lift acts at the COM
without accidental torque.

- [ ] **L-01 Under capacity:** climb, hover and descend. Vertical response is smooth
      and bounded by recovered caps.
- [ ] **L-02 Exactly capacity:** mass equal to capacity may hover but must not gain
      migration eligibility as safely under capacity.
- [ ] **L-03 Over capacity:** add mass past capacity. The authentic overload warning
      appears and climb authority is refused or reduced as designed.
- [ ] **L-04 Remove mass:** unload below capacity. Recovery occurs once with no
      duplicated lift.
- [ ] **L-05 Core detach:** detach the only core on a disposable ship. Lift becomes
      zero safely; no stale capacity remains.
- [ ] **L-06 Multiple-core corruption:** an invalid second core must not multiply
      capacity silently.
- [ ] **L-07 Abandoned sink:** test only on a disposable ship. Document the delayed
      downward behavior; never risk a valued ship.
- [ ] **L-08 Restart:** no one-frame fall, lift spike or capacity disagreement after
      checkpoint restore.

Pass: visible vertical behavior, components 1257/1258 and admin telemetry share one
effective capacity; gravity is never applied twice.

## 13. Phases 8–9 — authoritative collision

### Shadow phase

- [ ] **CSH-01 Slow terrain approach:** skim an island at low speed. The client
      remains authoritative visually while shadow telemetry predicts contact.
- [ ] **CSH-02 Fast sweep:** cross a thin proxy path quickly. Shadow sweep must catch
      tunnelling that endpoint overlap would miss.
- [ ] **CSH-03 Grazing:** fly near concave terrain and record conservative AABB false
      positives. Do not promote response until unacceptable blockers are removed.
- [ ] **CSH-04 Two ships:** approach moving hulls from both clients; pair ordering and
      contact are deterministic.
- [ ] **CSH-05 Cap:** a capped/truncated evaluation must report unknown/blocked, never
      “clear.”

### Response phase, damage disabled

- [ ] **CR-01 Nose contact:** Craft B contacts terrain at walking speed and stops or
      slides without entering the island.
- [ ] **CR-02 Side scrape:** a shallow-angle contact resolves without a large bounce,
      spin explosion or passenger ejection.
- [ ] **CR-03 Fast impact:** no tunnelling through terrain or another hull. Damage is
      still off, so only response is accepted.
- [ ] **CR-04 Resting contact:** the ship can remain against a surface without
      vibrating, sinking, repeatedly waking or flooding the network.
- [ ] **CR-05 Hull/hull:** two disposable ships collide under two clients. Both see
      the same order and neither gains duplicate authority.
- [ ] **CR-06 Parts/players:** mounted parts remain attached and aboard players do not
      fall through during response.
- [ ] **CR-07 Relog:** contact state does not restore inside terrain or launch the ship.

Pass: oriented/extracted geometry is good enough for the tested cohort, no
tunnelling, stable resting manifold, deterministic two-client response. Damage is
a later separately switched acceptance, never bundled with first response.

## 14. Phase 10 — authentic docking

- [ ] **DK-01 Permission:** owner and explicitly authorized crew may approach;
      unauthorized players cannot capture another yard remotely.
- [ ] **DK-02 Neutral capture:** enter the yard envelope slowly with propulsion
      neutral. Approach begins rather than radius-teleporting.
- [ ] **DK-03 Visual convergence:** pose and shortest yaw converge smoothly; linear
      and angular velocity reach zero at capture.
- [ ] **DK-04 Sail suppression:** unfurled sails do not pull a captured ship out of
      the yard. Their state remains truthful.
- [ ] **DK-05 Occupancy race:** two ships attempt one yard. Exactly one claim succeeds;
      the other remains controlled and undocked.
- [ ] **DK-06 Depart by sail:** sail-only departure releases occupancy after the
      clearance envelope, not at the first motion tick and not never.
- [ ] **DK-07 Depart by engine:** repeat with engine and mixed propulsion.
- [ ] **DK-08 Restart docked:** restart and resolve the stable yard key to fresh runtime
      IDs without duplicate claims or pose jumps.
- [ ] **DK-09 Yard destruction:** destroy/recall the yard; the ship exits safely and
      stale associations clear.
- [ ] **DK-10 Two clients:** both clients observe the same capture, dock state and
      release without interpolation disagreement.

Pass: claim, motion, components and persistence commit transactionally under one
authority generation; no legacy `SetDocked` overwrite path remains.

## 15. Phase 11 — vector walls, storms and damage

Begin force-only with all damage disabled and a disposable ship. Per-type force
magnitudes are lost retail data and must be labelled Wareborn tuning.

- [ ] **W-01 Visual/physics bands:** approach a Wind Rift. Curtain is visible around
      800 m; physical influence starts around 400 m; full core is inside 200 m.
- [ ] **W-02 Direction:** cross from both sides. Radial force sign reverses correctly;
      downward force never becomes upward.
- [ ] **W-03 Mass comparison:** light and heavy disposable ships show recovered mass
      attenuation without inversion.
- [ ] **W-04 Longitudinal/lateral:** vector response includes the expected side/down
      motion rather than scalar speed-only resistance.
- [ ] **W-05 Gust:** observe the recovered triangular 0.5-second gust envelope without
      a tick-rate-dependent impulse.
- [ ] **W-06 Torque:** Storm/Sand yaw aligns in the expected direction; Wind Rift does
      not invent yaw.
- [ ] **W-07 Docked suppression:** a docked ship does not get torn out by a wall tick.
- [ ] **W-08 Collision ordering:** wall shove cannot tunnel through terrain; contact
      and wall damage are not charged twice.
- [ ] **W-09 Lightning eligibility:** visual/lightning eligibility changes at the
      recovered 300 m band.
- [ ] **W-10 Damage intent:** only after force acceptance, enable damage on Craft B.
      One deterministic event creates one damage result across replay/restart.
- [ ] **W-11 Understorm separation:** island understorm behavior remains distinct from
      wall force and Blight; resources reset only on the intended island schedule.

Pass: visual and physical bands agree with telemetry, deterministic intent IDs,
no duplicate damage, and every non-recovered magnitude is explicitly recorded.

## 16. Phase 12 — performance and multiplayer hardening

- [ ] **P-01 One client soak:** 60 minutes across islands, terrain checkout, sailing,
      docking and walls; no cadence pressure growth or memory climb.
- [ ] **P-02 Two clients apart:** players on different islands/ships receive only
      their relevant domains and remain responsive.
- [ ] **P-03 Two clients together:** board the same moving ship, interact, separate and
      rejoin. Inspector interaction cluster expands and contracts correctly.
- [ ] **P-04 Fleet tiers:** synthetic 5/20/50/100-ship runs record p50/p95/p99 physics,
      relay, interest, gateway, snapshot, GC, allocations and bandwidth.
- [ ] **P-05 Caps:** deliberately hit part/domain/contact/queue caps. The server sheds
      work truthfully and fails closed rather than stalling.
- [ ] **P-06 Long relog:** repeated disconnect/reconnect does not leak peers, pilots,
      aboard membership, carry state or checkout ledgers.

Pass: documented budgets, bounded memory/queues, no new component-error IDs and no
player-visible degradation at the accepted cohort.

## 17. Phase 13 — remote workers

Remote work begins only after every mutating command uses the in-process gateway,
transport is authenticated, a lease/consensus authority prevents dual ownership,
snapshots and outcomes are durable, and the Inspector exposes worker truth.

### Same-host worker

- [ ] **RW-01 Empty island out:** move an empty test island from `local:primary` to a
      worker process on the same host. Client remains connected.
- [ ] **RW-02 Empty island back:** return authority locally. Generation advances once,
      no duplicate island or stale write.
- [ ] **RW-03 Observe:** World, Simulation and Infrastructure views all point to the
      same domain and worker.

### Separate VPS

- [ ] **RW-04 Remote empty island:** repeat across authenticated transport. Walk and
      interact on the island while watching latency and snapshot age.
- [ ] **RW-05 Network faults:** inject delay, duplicate, reorder, corruption and a
      partition. Commands fail closed; no dual authority appears.
- [ ] **RW-06 Kill worker:** `kill -9 worker-b`. Inspector must show OFFLINE → revoke →
      snapshot restore → candidate ready → generation+1 → takeover. Game connection
      stays alive.
- [ ] **RW-07 Heal old worker:** old authority reconnects and every stale write is
      rejected visibly.

### Ship tiers

- [ ] **RW-08 Uncrewed ship:** migrate a disposable empty ship first. Pose/state remain
      within the declared snapshot-age discontinuity bound.
- [ ] **RW-09 Crewed stationary ship:** two clients aboard, no motion. Migrate and
      verify decks, avatars, interactions and authority.
- [ ] **RW-10 Crewed moving ship:** final tier only. Migrate during controlled flight;
      no disconnect, duplicate pilot, part split or visible teleport beyond the
      accepted bound.
- [ ] **RW-11 Worker loss in flight:** kill the authoritative worker during controlled
      disposable flight. Restore once, advance once, reject the healed old worker and
      keep both clients connected.

Pass: one authority at all times, durable recovery, bounded discontinuity, no client
endpoint change and a truthful Inspector timeline.

## 18. Decision record after each phase

Every phase ends with one of four explicit outcomes:

- **PASS — promote:** all visual and operator gates pass; widen only to the next
  bounded cohort.
- **PASS WITH TUNING HOLD:** recovered behavior works, but a Wareborn balance value
  needs a separately documented choice. Do not conflate this with correctness.
- **FAIL — rollback:** restore the previous switch/build immediately, preserve logs
  and captures, and open a focused correction.
- **INCONCLUSIVE:** required player, second client, disposable craft, boundary,
  collision geometry or observation was unavailable. The phase remains off.

No phase becomes “done” because automated tests passed, because the server stayed
active for five minutes, or because the admin panel looked plausible. The named
visual tests and their matching server evidence are the acceptance contract.
