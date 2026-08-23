# Pack D/E preparation — 2026-08-23

## What Codex completed unattended

- Fuel/propulsion, vector force, lift/gravity, collision-shadow and docking pure
  suites pass together (205 focused tests before the C4 trim correction).
- The complete multiplayer suite passed 4,847/4,847 and the game release build
  completed with zero errors for the bare-hull deploy.
- Production world-state inventory contains sails, wings, cores, helms and utility
  parts, but no mounted engine and no mounted power generator. Pack D therefore
  cannot honestly be executed with the current ships.
- `WAREBORN_FUEL_HULL_DEMAND` remains OFF. No fuel lifecycle or persistence shape
  was promoted merely to test a ship that cannot consume fuel.
- E's vector/lift/collision implementations remain PURE/SHADOW. There is no
  reviewed live-authority switch to turn on; inventing one during acceptance would
  bypass the program's integration no-go decision.
- C4's static-sail adapter was corrected to reconstruct the recovered equilibrium
  yaw-joint trim from the authoritative mount and retained wind. Dynamic
  render-frame sail yaw remains explicitly unavailable and has no gameplay role.

## Next single-boot REAL-EYES run

This deliberately groups the remaining safe checks. Do not close the client
between steps.

1. On the two-sail ship, keep both sails furled and hold full positive throttle
   for 20 seconds. Confirm the new 2x bare-hull movement is smooth, noticeably
   faster than the recorded 0.73 m/s run, and clearly slower than one sail.
2. Leave throttle neutral. Unfurl one sail for 20 seconds, then both for 20 seconds.
   Perform one gentle left turn and one gentle right turn. Confirm the pilot stays
   anchored, mounted parts do not split, and motion remains smooth.
3. In Admin → Simulation, select the hull and copy the incident bundle. C4 must
   report `vector-equilibrium-trim-shadow`, finite force/torque, zero rejected propulsors,
   and collision terrain explicitly `UNWIRED`.
4. Furl both sails, return throttle to neutral, leave the helm, and remain aboard
   for one Codex-injected one-second process suspension. Confirm there is no
   teleport, burst, component split, or fall. This closes C3's REAL-EYES item.

Those four observations close the remaining safe Pack C follow-up and E0 shadow
reference in one boot. They do **not** promote vector motion or pass Pack E1–E4.

## Pack D setup gate

Before the next D boot, build a disposable rig with:

- one mounted engine;
- a second engine ready to mount;
- one mounted power generator with visible fuel gauge;
- one sail retained so the run-dry test can prove canvas independence;
- no irreplaceable cargo.

Once that rig exists and nobody is connected, Codex backs up world state, enables
`WAREBORN_FUEL_HULL_DEMAND=1`, stages bounded fuel levels, deploys/restarts once,
and runs D0–D4 in one continuous client session. A single planned restart is kept
inside D4 to prove generator identity persistence. Until that physical rig exists,
Pack D is **PREPARED / BLOCKED ON TEST FIXTURE**, not failed.

## Pack E authority gate

E0/C4 is observation-only and ready for the grouped run above. E1–E4 remain
**NO-GO** for live authority until a separately reviewed adapter wires the fixed
clock and durable angular state to per-hull vector motion, then exposes an exact
per-hull promotion/rollback command. Collision remains shadow-only until oriented
terrain geometry and response safety are independently accepted.
