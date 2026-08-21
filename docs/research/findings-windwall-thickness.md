# Wind-wall thickness and traversal audit

Date: 2026-08-21. Scope: release MapFile wall records, shipped/decompiled
Unity client, serialized `level0` scene values, current wall wire implementation,
current force integrator, and the live 2026-08-21 sighting. No server was restarted
or deployed during this audit.

## Result

The thin object seen in game is the intended type-0 **Wind Rift curtain**, not the
width of the weather system. The curtain is a translucent procedural shader feature.
The actual recovered zones around its centreline are:

| zone | one side | complete band | provenance |
|---|---:|---:|---|
| visual weather influence | 800 m | 1,600 m | RECOVERED serialized `level0` `WindRiftDist` / `StormRiftDist` |
| any physical influence | 400 m | 800 m | PROVED hard-coded `WallData.EffectiveDist` |
| full physical strength | 200 m | 400 m | PROVED hard-coded `WallData.DistForMaxStrength` |

From 200 to 400 m the force falls linearly from full strength to zero. Distances
are XZ-only, so the physical wall is vertically infinite. Its cloud height is a
separate client shader value (decompiled field default 3,500 m; the serialized
scene value has not been recovered and must not be claimed as 3,500).

At the final observed live hull position `(-11681, -11520)` the nearest release
segment was wall 5, type 0 Wind Rift, 662 m away. That is inside the 800 m visual
zone but outside the 400 m force zone. The sighting therefore matches the recovered
client geometry exactly; it does not demonstrate the physical band's thickness.

## What the MapFile and wire can control

The MapFile's complete wall record is only `x1,z1,x2,z2,Type`. Component 1204 adds
identity and expresses the same segment as midpoint, unit forward and half-length.
Neither carries opacity, film thickness, cloud thickness, VFX scale, height, wind
strength, gust strength, torque, or damage.

The Wind Rift renderer is client-owned `CmdBufClouds`. Its **decompiled field
defaults**, not recovered serialized values, are opacity 0.6, scale 0.007 (uploaded
with a further 0.3 multiplier), film thickness 1.3, main thickness 1.3 and height
3,500. These numbers describe procedural shader density, not metres of traversal
width. They are not writable through 1204 and changing them requires a client
asset/code modification. A type-1 Storm Rift is the dark, opaque billowing wall
players remember as visually thick; Wind Rifts are the only see-through wall and
are described by both Bossa and the wiki as a waterfall of air/cascading clouds.

## Recovered mechanics

Retail applied three separate systems:

1. Continuous relative-wind drag. A Wind Rift blows perpendicular and away from
   its centreline, with an additional downward term. Other wall types blow along
   their authored forward direction.
2. Timed gust force at a point. Wind Rift gusts point down; other types use random
   horizontal direction.
3. Yaw torque for Storm Rift, Sandstorm and World End only. Wind Rift has no torque.

All use the 200/400 m intensity. Continuous wall wind is attenuated by ship mass as
`1 - clamp01(mass/4000) * 0.75`: ships at or above 4,000 kg feel one quarter of the
wind applied to a zero-mass limit. There is no collider, speed threshold or pass/fail
gate. A ship crosses when its thrust beats relative-wind drag. This is why the wall
could be difficult to cross despite its curtain looking narrow.

The release world contains 20 Wind Rifts, 11 Storm Rifts, 12 Sandstorms and one
World End segment. The current whole-segment serving is geometrically exact; retail
subdivision affected SpatialOS checkout, not the merged wall distance field.

## What remains unrecoverable or blocked

The 50 scalar values delivered by retail in `1229 GlobalWallDataState` do not survive
in the client, assets, MapFile or any known snapshot. They include all five wall wind
multipliers, 24 gust values and 21 torque values. Any magnitude is necessarily
WAReborn tuning and must be labelled as such.

The current deployment serves 1204 only. Consequently it renders walls but applies
no force. The shipped client also leaves its wall-wind multipliers at zero without a
complete 1229. A complete invented 1229 could align visible wind/VFX with chosen
WAReborn strengths, but its UnityWorker force components are absent from our hulls;
the server remains the mechanical authority.

The current scalar flight model can reproduce a head-on traversal exactly and
projects oblique wind onto its longitudinal axis while retaining the recovered
band/direction and mass attenuation. It cannot yet reproduce lateral shove,
downward wind, force-at-point spin, yaw torque, sail damage, lightning damage, or
part detachment. Those need full vector/lift/health work, not a timer or fallback.

## Implemented in this branch

`WallFlightInfluence` projects the exact 44 release segments into the flight wind
field and honours both `WAREBORN_WALLS` and `WAREBORN_WALL_TYPES`. Mechanical strength
defaults to zero because retail values are lost. Four explicit, client-bounded
WAReborn tuning inputs opt types in:

- `WAREBORN_WALL_WIND_RIFT_MPS`
- `WAREBORN_WALL_STORM_RIFT_MPS`
- `WAREBORN_WALL_SANDSTORM_MPS`
- `WAREBORN_WALL_WORLD_END_MPS`

Configured walls feed signed relative wind into the existing recovered quadratic
drag law, including headwinds and the mass ramp, without enabling
`WAREBORN_FLIGHT_WIND_FIELD` or changing ambient wind or sail power. Unconfigured
and walls-off operation remains unchanged. Deploying a nonzero value should wait
for calibration and a deliberate decision about supplying a complete 1229 so the
wind the server applies and the wind the client depicts do not disagree.

One integration dependency is outside this branch's scope: `ShipForceModel` at the
branch point still uses 2.0 / 0.01 while the serialized shipped `ShipConfig` audit
recovered exponent 2.5 / coefficient 0.007. That global correction changes every
ship's terminal speed and belongs with the concurrent flight-calibration work. Wall
resistance uses the shared force model and therefore inherits whichever pair is
merged there; it does not fork a second drag law.
