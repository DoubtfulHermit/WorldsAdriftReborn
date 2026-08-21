using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// WHAT THE WIND IS AT A PLACE AND A TIME. One answer, for every consumer.
    ///
    /// Before this type there was no wind field at all: there was a SPEED
    /// (<c>FlightTuning.WindSpeedMps</c>) and, separately, a compile-time
    /// DIRECTION baked into two constants that nothing could rotate
    /// (<see cref="ShipForceModel.DefaultWindX"/>/<c>Z</c>). Sails read the
    /// direction, the bare-hull baseline ignored it and blew along the hull's own
    /// heading instead, and neither knew where it was. This type is the seam that
    /// makes "the wind here is different from the wind there" expressible at all.
    ///
    /// ---------------------------------------------------------------------
    /// THE THING TO UNDERSTAND BEFORE CHANGING ANY NUMBER IN THIS FILE.
    ///
    /// The shipped client is ALREADY DRAWING A WIND, right now, on production,
    /// and it is not reading anything we send. <c>GlobalWeather.GetWeatherAt</c>
    /// returns the hard-coded fallback <c>(1, 0, -2)</c> for any position with no
    /// <c>1139 WeatherCellState</c> covering it (decompile
    /// <c>acs/Assets.Visualizers.Weather/GlobalWeather.cs:65-68</c>), and this
    /// server serves no weather cells anywhere - 1139 is in
    /// <c>ComponentAbsencePolicy.KnownAbsentComponentIds</c> and its serialize
    /// branch is deleted. So EVERY position in our world returns that constant,
    /// and the client renders it:
    ///
    ///   * <c>WindTrail.cs:79</c> - twenty wind-streak TrailRenderers around the
    ///     camera, oriented with <c>SetLookRotation(wind, up)</c> and moving at
    ///     <c>baseSpeed + |wind| * multiplier</c>. These are the "windtrails in
    ///     the sky" the wiki's own sailing guide tells players to steer by. Plain
    ///     MonoBehaviour, no [Require], live in <c>level0</c>.
    ///   * <c>WindControl.cs:162-164</c> - <c>transform.forward =
    ///     LocalPlayer.Weather.Wind.normalized</c>, which then drives the Unity
    ///     <c>WindZone</c> (foliage/SpeedTree sway), every registered <c>Cloth</c>'s
    ///     external acceleration, and the global shader uniforms
    ///     <c>_SinWindRotation</c>/<c>_CosWindRotation</c>.
    ///   * <c>FlagWind.cs:58-62</c> - a mounted flag points downwind. It is the
    ///     nearest thing the shipped client has to the wiki's "windsock on your
    ///     helm"; a real windsock part does not exist (searched: the decompile for
    ///     windsock/windvane/weathervane/anemometer, and resources.assets with
    ///     <c>grep -a</c> - the only hit is <c>3x2_Windsock</c>, a scrap-item icon).
    ///   * <c>SailVisualizer.cs:75-80</c> - sail fill, luff, which side the canvas
    ///     bellies to, and the flapping SFX, from <c>|dot(windDir,
    ///     yawJoint.right)|</c>. DIRECTION ONLY: line 76-79 normalises the wind
    ///     whenever its magnitude is under 1, so the canvas cannot show strength.
    ///   * <c>StormDebris.cs:82</c>, <c>WeatherTextureGenerator.cs:200</c>,
    ///     <c>AmbienceSoundController.cs:246</c>, <c>GliderControl.cs:130</c>,
    ///     <c>WeatherInfoProvider.cs:17</c> (the dev-console readout).
    ///
    /// ---------------------------------------------------------------------
    /// AND RETAIL'S OWN PLAYERS SAW THE SAME CONSTANT. This is the finding that
    /// reframes everything above, and it is read off the shipped bytes.
    ///
    /// The client ships THREE entity blueprints carrying an explicit
    /// <c>EntityReadAccess</c> grant list, as TextAssets inside
    /// <c>resources.assets</c> (not as files on disk - a filesystem search for
    /// them finds nothing and means nothing). Verbatim:
    ///
    ///   Blight        -> "EntityReadAccess" : ["physics","visual"]
    ///   WeatherCell   -> "EntityReadAccess" : [ "social", "physics" ]
    ///   (a third, weather-cell-adjacent) -> "EntityReadAccess": [ "social" ]
    ///
    /// <b>The WeatherCell blueprint does not grant "visual".</b> "visual" is the
    /// Unity client, and it is plainly in the vocabulary because the Blight
    /// blueprint sitting beside it names it. If the default granted every
    /// worker, Blight would not have had to ask - so the list is exhaustive and
    /// the omission is a denial. The same blueprint gives
    /// <c>WeatherCellStateC</c> a <c>ComponentWriteAccess</c> of "social", not
    /// "visual" either.
    ///
    /// PROVED: the grant lists. INFERRED, but hard to escape: retail's own
    /// player clients therefore never checked out a weather cell,
    /// <c>_weatherCellCoordMap</c> was empty on a real machine,
    /// <c>GetCellSampleAt</c> always missed, and <c>GetWeatherAt</c> returned
    /// <c>(1,0,-2)</c> and pressure 0.5 <b>for every position in retail too</b>.
    /// Contrary evidence, stated rather than buried: a
    /// <c>WeatherCell_unityclient</c> prefab does exist, and the client ships
    /// two ECS systems that maintain a weather-cell coordinate map. Both are
    /// consistent with machinery that was built and then never fed - which this
    /// codebase has met many times.
    ///
    /// WHAT THAT CHANGES, and it is a correction to this repo's own roadmap:
    ///   * 2.236 m/s toward +X/-Z is NOT "retail's becalmed case standing in for
    ///     an absent weather system". It is, as far as a player was concerned,
    ///     retail's ONLY ambient wind. Serving it is fidelity, not a placeholder.
    ///   * The wind that VARIED in retail, the thing the wiki tells players to
    ///     steer by, was the WALL wind - and walls are client-readable through
    ///     an ordinary visualizer. Chasing the 500 m lattice would be chasing
    ///     something a retail player never saw either.
    ///   * Anything this server does with <see cref="WindFieldVariation"/> is
    ///     therefore an invention, not a restoration, and diverges from retail
    ///     as well as from what the client draws. Which is why it is OFF.
    ///
    /// CAVEAT ON METHOD, because it invalidated an earlier pass: the decompile
    /// at <c>WAReborn-decompiled/</c> does NOT contain <c>WASystems.dll</c> or
    /// <c>SpatialTranslator.dll</c>, so any "no consumer exists" conclusion
    /// drawn only from grepping that tree is a possible FALSE ZERO. The claims
    /// above are read off <c>acs</c> (which is present and complete for the
    /// classes named) or off the shipped assets directly.
    ///
    /// ---------------------------------------------------------------------
    /// CONSEQUENCE, and it is the single most important design constraint here:
    /// <b>we cannot change what the client DRAWS without the 500 m weather-cell
    /// lattice, but we entirely own what the player FEELS.</b> The client's ship
    /// physics does not run on a player's machine at all - both
    /// <c>WindPhysicsVisualizer</c> and <c>SailBehaviour</c> are on
    /// <c>*_unityworker</c> prefabs only - so motion is whatever our 1130 control
    /// points say. Every metre of divergence between this field and
    /// <c>(1,0,-2)</c> is a metre where the streaks a player is steering by lie to
    /// them. That is the budget. Spend it deliberately.
    ///
    /// ---------------------------------------------------------------------
    /// WHAT VARIES WITHOUT THE LATTICE, AND WHAT CANNOT.
    ///
    /// 1. TIME AND PLACE, in what a ship FEELS - free, and implemented here as
    ///    <see cref="WindFieldVariation"/>. Costs nothing on the wire and needs no
    ///    component: the server integrates and the client replays.
    /// 2. WEATHER WALLS - the one source of wind variation the client will also
    ///    DRAW, and it needs no weather cell whatsoever.
    ///    <c>GlobalWeather.GetWeatherAt</c> line 83 is
    ///    <c>Lerp(cellWind, wallWind, wallQuery.Intensity)</c>, and
    ///    <c>WallSegmentVisualizer</c> has exactly one <c>[Require]</c> -
    ///    <c>WallSegmentStateReader</c>, i.e. <c>1204</c>. It registers into a
    ///    static <c>WeatherWalls</c> list that <c>GetWallWindAt</c> walks. No
    ///    lattice, no Cantor pair, no 1139. See <see cref="WeatherWallSegment"/>
    ///    for the recovered geometry and the trap that comes with it.
    ///    <b>This corrects the roadmap</b>, whose §12.6 lists windwalls under
    ///    "genuinely blocked on weather" and whose Phase 6 says they are "nearly
    ///    free ONCE weather exists". They are nearly free NOW.
    /// 3. ALTITUDE AND MAP-EDGE RAMPS - blocked on <c>1250 WorldBoundsDataState</c>
    ///    (<c>GlobalWeather.cs:84</c> gates them on
    ///    <c>WorldBoundsDataVisualizer.CheckedOut</c>), NOT on 1139. Also not
    ///    implemented here, because our flight model has no vertical wind term.
    /// 4. GENUINELY BLOCKED ON THE LATTICE: any variation the client must AGREE
    ///    with, away from a wall. The trails, the foliage, the flags and the sail
    ///    cloth will keep showing <c>(1,0,-2)</c> forever otherwise. Also pressure
    ///    (pinned at 0.5) and therefore turbulence, which is
    ///    <c>|wind|/100</c> - though <c>WobbleVisualiser</c> needs a
    ///    <c>TransformStateWriter</c>, i.e. client authority over a hull's
    ///    transform, which this server grants to nobody, so it is doubly dead.
    ///
    /// TWO LEVERS THAT LOOK LIKE ANSWERS AND ARE NOT. Recorded so nobody spends a
    /// session on them, since the roadmap currently recommends the first:
    ///   * <c>5129 WindReceiverState</c> is a REPORT channel, not a delivery one.
    ///     Its only toucher in <c>acs</c> is <c>WindReceiverBehaviour</c>, which
    ///     holds a <c>...StateWriter</c> and PUBLISHES
    ///     <c>WeatherWalls.GetWallWindAt(...)</c> once a second; it is added on
    ///     the <c>_unityworker</c> branch only
    ///     (<c>SailPreprocessor.cs:24-29</c>). Note the ONE thing it reports is
    ///     WALL wind, so it is downstream of walls rather than an independent
    ///     input. INFERRED rather than proved that nothing reads it: an ECS
    ///     reader could live in the missing <c>WASystems.dll</c>. Even so it
    ///     would be a worker-side reader of a worker-side writer, so it is not a
    ///     way to push wind at a player. The roadmap currently names 5129 as the
    ///     intended per-entity delivery mechanism; that is not what it is.
    ///   * <c>1202</c>/<c>1203</c> wind multipliers have readers that do ship and
    ///     do run, but both only call
    ///     <c>GlobalWeather.RegisterWeatherModifier(this)</c>, and
    ///     <c>GlobalWeather._modifiers</c> is never enumerated while
    ///     <c>GetWindModifierAt</c> is a hard-coded <c>return 0f</c>
    ///     (<c>GlobalWeather.cs:144-147</c>). Inert.
    /// </summary>
    public static class WindField
    {
        /// <summary>
        /// PROVED - <c>GlobalWeather.GetCellSampleAt</c>
        /// (<c>acs/Assets.Visualizers.Weather/GlobalWeather.cs:66</c>) returns
        /// <c>(1, 0, -2)</c> for an uncovered position, so this is the compass
        /// bearing of every wind streak, every blade of grass and every flag on
        /// every player's screen in this world. Atan2 of the X/Z pair, in the
        /// server's own yaw convention (0 = +Z, growing toward +X), which is the
        /// same convention <see cref="ShipForceModel.SailForwardNewtons"/> reads.
        /// </summary>
        public static readonly double PublishedBearingRadians =
            Math.Atan2(ShipForceModel.DefaultWindX, ShipForceModel.DefaultWindZ);

        /// <summary>
        /// The wind at a position and a moment.
        ///
        /// <paramref name="meanSpeedMps"/> is <c>FlightTuning.WindSpeedMps</c> -
        /// the field's MEAN magnitude, not its instantaneous one. A malformed or
        /// negative value yields a dead calm rather than a NaN ship.
        ///
        /// <paramref name="walls"/> is retail's second wind source and is empty on
        /// production today, because this server serves no <c>1204</c>. Passing
        /// segments here changes what a ship FEELS; it does not make the client
        /// draw anything until 1204 is actually served. See
        /// <see cref="WeatherWallSegment"/>.
        /// </summary>
        public static WindSample SampleAt(
            double x,
            double z,
            double timeSeconds,
            double meanSpeedMps,
            WindFieldVariation variation = default,
            IReadOnlyList<WeatherWallSegment>? walls = null)
        {
            if (!double.IsFinite(meanSpeedMps) || meanSpeedMps <= 0.0)
            {
                return WindSample.Calm;
            }
            // The published vector at this world's strength. Written as a SCALING
            // of retail's own (1,0,-2) rather than as sin/cos of its bearing so
            // that the no-variation path is bit-identical to the arithmetic this
            // replaced, and a "wind is off" regression test can assert equality
            // rather than a tolerance.
            double scale = meanSpeedMps / ShipForceModel.DefaultWindSpeedMps;
            double baseX = ShipForceModel.DefaultWindX * scale;
            double baseZ = ShipForceModel.DefaultWindZ * scale;

            if (!double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(timeSeconds))
            {
                // A malformed pose must leave the ship in the world's mean wind,
                // not becalm it and not NaN it. Same reflex as the agility clamp
                // in FlightIntegrator.Step.
                return WindSample.FromComponents(baseX, baseZ, 0.0);
            }

            // Rotate by the veer and scale by the gust. Both are exact identities
            // at zero veer / unit gust (cos 0 = 1, sin 0 = 0, x * 1.0 == x), which
            // is what keeps the disabled path exact.
            double veer = variation.VeerAt(x, z, timeSeconds);
            double gust = variation.GustAt(x, z, timeSeconds);
            double cos = Math.Cos(veer);
            double sin = Math.Sin(veer);
            WindSample ambient = WindSample.FromComponents(
                ((baseX * cos) + (baseZ * sin)) * gust,
                ((baseZ * cos) - (baseX * sin)) * gust,
                0.0);

            if (walls == null || walls.Count == 0)
            {
                return ambient;
            }

            // Retail picks the NEAREST wall with any intensity at all, not the
            // strongest and not a sum - WeatherWalls.GetWallWindAt walks every
            // wall and keeps the one with the smallest SqrDist among those whose
            // intensity exceeds 1e-6.
            double bestSqr = double.MaxValue;
            WeatherWallSegment best = default;
            bool found = false;
            for (int i = 0; i < walls.Count; i++)
            {
                WeatherWallSegment segment = walls[i];
                double sqr = segment.SqrDistanceTo(x, z);
                if (WeatherWallSegment.IntensityAtSqrDistance(sqr) > 1e-6 && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = segment;
                    found = true;
                }
            }
            if (!found)
            {
                return ambient;
            }

            double intensity = WeatherWallSegment.IntensityAtSqrDistance(bestSqr);
            best.WindAt(x, z, out double wallX, out double wallZ);

            // GlobalWeather.cs:83 - Vector3.Lerp(cellWind, wallWind, intensity).
            // A componentwise lerp, NOT a bearing slerp: inside a wall the ambient
            // wind is REPLACED, and at intensity 1 with a zero wall multiplier the
            // result is a dead calm. That is not a bug here, it is what the client
            // does, and it is the trap documented on WeatherWallSegment.
            double mixedX = ambient.WindX + ((wallX - ambient.WindX) * intensity);
            double mixedZ = ambient.WindZ + ((wallZ - ambient.WindZ) * intensity);
            return WindSample.FromComponents(mixedX, mixedZ, intensity);
        }

        /// <summary>
        /// How much of a wind a hull on this heading is actually carried by:
        /// the wind's component along the hull's forward axis, never negative.
        ///
        /// RETAIL AIMED THE HULL WIND DOWNWIND, and this is that aim. Its own
        /// term is <c>GetDrag(wind - velocity)</c>, so a ship pointing across the
        /// wind is carried by the projection and a ship pointing into it is not
        /// carried at all - which is exactly the wiki's own sailing guide: *"If
        /// the wind is directly behind your ship, you will move at maximum speed.
        /// If the wind is directly in front of you, your movement will be
        /// negligible."*
        ///
        /// THE ONE DEPARTURE, and it is deliberate: the projection is floored at
        /// zero rather than allowed to go negative. Retail's relative-wind term
        /// really would push a hull backwards on a dead headwind; ours would have
        /// to express that as a negative commanded speed, i.e. a hull sliding
        /// astern under a forward lever, which reads as a bug rather than as
        /// weather. "Negligible", per the guide, not "reverse".
        /// </summary>
        public static double AlongHeading(in WindSample wind, double headingRadians)
        {
            if (wind.SpeedMps <= 0.0 || !double.IsFinite(headingRadians))
            {
                return 0.0;
            }
            double forwardX = Math.Sin(headingRadians);
            double forwardZ = Math.Cos(headingRadians);
            double along = (wind.WindX * forwardX) + (wind.WindZ * forwardZ);
            return along > 0.0 ? along : 0.0;
        }

        /// <summary>
        /// The signed component of a wind along a hull's bow. Unlike
        /// <see cref="AlongHeading"/>, this deliberately preserves a headwind as a
        /// negative value. Retail's wall drag used the complete relative-wind vector,
        /// so a Wind Rift on the approach side of its centreline pushed a ship back;
        /// flooring that projection at zero would make an authored weather barrier
        /// mechanically indistinguishable from still air.
        /// </summary>
        public static double SignedAlongHeading(in WindSample wind, double headingRadians)
        {
            if (wind.SpeedMps <= 0.0 || !double.IsFinite(headingRadians))
            {
                return 0.0;
            }
            return (wind.WindX * Math.Sin(headingRadians))
                + (wind.WindZ * Math.Cos(headingRadians));
        }
    }

    /// <summary>
    /// A wind vector in the horizontal plane, in the server's yaw convention
    /// (bearing 0 = +Z, growing toward +X - the same one
    /// <see cref="ShipForceModel.SailForwardNewtons"/> uses for a hull's heading).
    ///
    /// HORIZONTAL ONLY, deliberately. Retail's wind has a Y term (a Wind Rift
    /// blows a ship down as well as sideways) but our flight model integrates a
    /// scalar speed along a heading plus a separate commanded climb, so there is
    /// nowhere for a vertical wind to go. Adding one is a flight-model change, not
    /// a wind change, and belongs with lift.
    /// </summary>
    public readonly struct WindSample
    {
        public static readonly WindSample Calm = new WindSample(0.0, 0.0, 0.0, 0.0, 0.0);

        private WindSample(double windX, double windZ, double speedMps, double bearingRadians, double wallIntensity)
        {
            WindX = windX;
            WindZ = windZ;
            SpeedMps = speedMps;
            BearingRadians = bearingRadians;
            WallIntensity = wallIntensity;
        }

        public static WindSample FromComponents(double windX, double windZ, double wallIntensity)
        {
            if (!double.IsFinite(windX) || !double.IsFinite(windZ))
            {
                return Calm;
            }
            double speed = Math.Sqrt((windX * windX) + (windZ * windZ));
            // PROVED - GlobalWeather.GetWindAt returns Vector3.zero above 100 m/s
            // rather than a stronger wind, so a runaway field becalms instead of
            // launching every hull in the world.
            if (speed > MaxSpeedMps)
            {
                return Calm;
            }
            double bearing = speed > 0.0 ? Math.Atan2(windX, windZ) : 0.0;
            return new WindSample(windX, windZ, speed, bearing, wallIntensity);
        }

        /// <summary>
        /// PROVED - <c>GlobalWeather.cs:163-170</c>. Above this the client's own
        /// field returns zero.
        /// </summary>
        public const double MaxSpeedMps = 100.0;

        public double WindX { get; }

        public double WindZ { get; }

        public double SpeedMps { get; }

        /// <summary>Compass bearing the wind blows TOWARD, in the server's yaw convention.</summary>
        public double BearingRadians { get; }

        /// <summary>
        /// 0 in open sky, 1 inside a weather wall's full-strength core. Exposed so
        /// a caller can say "this hull is in a wall" without re-walking the list.
        /// </summary>
        public double WallIntensity { get; }
    }

    /// <summary>
    /// WAREBORN TUNING, all of it. How far the wind is allowed to wander from the
    /// bearing and strength the client is drawing.
    ///
    /// WHY THIS IS BOUNDED RATHER THAN FREE. A wind that varies is strictly better
    /// gameplay - it is the difference between "a bare hull travels in exactly one
    /// compass direction, for ever" and a route being worth choosing - and it is
    /// the stated reason
    /// <see cref="ShipForceModel.BaselineDriveSpeedMps(double)"/> aims along the
    /// hull's heading instead of downwind. But every degree of veer is a degree by
    /// which the wind streaks a player is steering by become wrong, and we cannot
    /// fix that without the forbidden lattice. So the excursion is capped, and the
    /// default is <see cref="None"/>: a server that does not opt in behaves
    /// exactly as it did before this type existed, to the last bit.
    ///
    /// The model is a closed form of position and time with no state, for the same
    /// reason the fauna model is: the admin map can then evaluate the identical
    /// expression in the browser from a clock alone and be honestly showing the
    /// server's wind rather than a drawing of one.
    /// </summary>
    public readonly struct WindFieldVariation
    {
        /// <summary>The production default: no variation at all, bit-identical to a bare constant.</summary>
        public static readonly WindFieldVariation None = default;

        /// <summary>
        /// WAREBORN TUNING - the distance over which the wind completes one full
        /// swing. 4 km against a 36 km world and a median island-to-nearest-wall
        /// distance of 1.3 km: a ship crossing from one island to the next sees
        /// the wind move, and a ship parked at an island does not sit on a seam.
        /// </summary>
        public const double CellMetres = 4000.0;

        /// <summary>
        /// WAREBORN TUNING - ten minutes for a full cycle. Long enough that a
        /// player experiences it as weather rather than as jitter; short enough
        /// that waiting out a bad wind is a real option in one session.
        /// </summary>
        public const double PeriodSeconds = 600.0;

        /// <summary>
        /// WAREBORN TUNING - the widest the bearing may stray from the published
        /// <c>(1,0,-2)</c>, in radians. 40 degrees: a player reading the wind
        /// streaks still gets the right half of the compass and a usable heading,
        /// which is the whole budget described on <see cref="WindField"/>.
        /// </summary>
        public const double MaxVeerRadians = 40.0 * Math.PI / 180.0;

        /// <summary>
        /// WAREBORN TUNING - the widest the strength may stray from the mean, as a
        /// fraction. 0.35 gives a roughly 0.65x-1.35x band, so a good wind is
        /// twice a bad one without either becoming a different game.
        /// </summary>
        public const double MaxGustFraction = 0.35;

        private readonly double _scale;

        /// <param name="scale">
        /// 0 disables variation entirely; 1 is the full tuned excursion. Values
        /// between scale both the veer and the gust, so one knob moves the whole
        /// field. Out-of-range and malformed values clamp to [0,1] rather than
        /// throwing, because this arrives from an environment variable.
        /// </param>
        public WindFieldVariation(double scale)
        {
            _scale = !double.IsFinite(scale) || scale <= 0.0
                ? 0.0
                : (scale > 1.0 ? 1.0 : scale);
        }

        /// <summary>0 when this is <see cref="None"/>. Cheap enough to branch on.</summary>
        public double Scale => _scale;

        public bool IsEnabled => _scale > 0.0;

        /// <summary>
        /// Bearing offset from the published wind, in radians. Two sinusoids at an
        /// irrational period ratio so the pattern does not visibly repeat, one
        /// travelling in X and one in Z, each advancing with time - so the field
        /// both varies across the map and drifts over it.
        /// </summary>
        public double VeerAt(double x, double z, double timeSeconds)
        {
            if (_scale <= 0.0)
            {
                return 0.0;
            }
            double a = Math.Sin(Tau * ((x / CellMetres) + (timeSeconds / PeriodSeconds)));
            double b = Math.Sin(Tau * ((z / CellMetres) - (timeSeconds / (PeriodSeconds * GoldenRatio))));
            return MaxVeerRadians * _scale * 0.5 * (a + b);
        }

        /// <summary>
        /// Multiplier on the mean speed. Deliberately out of phase with
        /// <see cref="VeerAt"/> (the half-cell and half-period offsets) so that a
        /// veer and a gust do not arrive together and read as one event.
        /// </summary>
        public double GustAt(double x, double z, double timeSeconds)
        {
            if (_scale <= 0.0)
            {
                return 1.0;
            }
            double a = Math.Sin(Tau * (((x + (CellMetres * 0.5)) / CellMetres) - (timeSeconds / (PeriodSeconds * 1.31))));
            double b = Math.Sin(Tau * (((z + (CellMetres * 0.5)) / CellMetres) + (timeSeconds / (PeriodSeconds * 0.77))));
            return 1.0 + (MaxGustFraction * _scale * 0.5 * (a + b));
        }

        private const double Tau = 2.0 * Math.PI;

        /// <summary>Irrational, so the two sinusoids never come back into phase.</summary>
        private const double GoldenRatio = 1.6180339887498949;
    }

    /// <summary>
    /// One authored weather-wall segment, in world XZ metres - the shape of a
    /// <c>1204 WallSegmentState</c> once the client has turned it into
    /// <c>WallData</c>.
    ///
    /// WHY THIS EXISTS EVEN THOUGH WE SERVE NO 1204. It is the concrete form of
    /// the finding that wind walls need no weather lattice, and the map layer and
    /// the tests both exercise it. 44 authored segments are already imported into
    /// <c>docs/research/world-data/wamap-islands.json</c> (20 Wind Rift, 12 Sand
    /// Storm, 11 Storm Rift, 1 World End) and already drawn on the admin map;
    /// nothing in the game server has ever read them.
    ///
    /// THE GEOMETRY IS PROVED, off <c>acs/WallData.cs</c>:
    ///   * distance is 2-D, to the LINE SEGMENT, in XZ only - a wall is infinitely
    ///     tall for wind purposes (<c>DistanceSqr</c>, line 185-188);
    ///   * intensity is 1 within 200 m of the line, ramps linearly to 0 at 400 m,
    ///     and is 0 beyond (<c>GetIntensityAt</c>, line 237-248);
    ///   * a Wind Rift blows PERPENDICULAR, away from its own line, plus a
    ///     vertical term; every other type blows along the wall's forward axis
    ///     (<c>GetWindUnscaled</c>, line 207-230).
    ///
    /// ⚠ THE TRAP, AND IT IS THE REASON SERVING 1204 IS NOT A ONE-LINE CHANGE.
    /// Every one of those wall winds is scaled by a
    /// <c>GlobalWeatherDataVisualizer.*WindMultiplier</c> static, and those
    /// statics are <c>0f</c> until something serves <c>1229 GlobalWallDataState</c>
    /// and its <c>FloatValues</c> map. So a client given 1204 alone computes
    /// <c>Lerp(ambient, ZERO, intensity)</c> and goes DEAD CALM inside every wall
    /// band - the trails stop, the grass stops, and the sails lose the wind, in
    /// exactly the places that are supposed to be the windiest in the world.
    /// Worse, <c>GlobalWeatherDataVisualizer.UpdateValues</c> <c>Debug.LogError</c>s
    /// once per MISSING KEY, and it wants roughly forty of them (five multipliers,
    /// six gust keys x four wall types, seven torque keys x three) - so a partial
    /// 1229 is its own error storm. <b>1204 and a COMPLETE 1229 land together or
    /// neither lands.</b> That pairing is the whole content of the phase, and it
    /// needs a soak because it adds a new streamed entity class.
    /// </summary>
    public readonly struct WeatherWallSegment
    {
        /// <summary>PROVED - <c>WallData.DistForMaxStrength</c> = 200 m, squared.</summary>
        public const double FullStrengthSqrMetres = 40_000.0;

        /// <summary>PROVED - <c>WallData.EffectiveDist</c> = 400 m, squared.</summary>
        public const double EffectiveSqrMetres = 160_000.0;

        public WeatherWallSegment(
            double x1, double z1, double x2, double z2,
            WeatherWallType type, double windMultiplier)
        {
            X1 = x1;
            Z1 = z1;
            X2 = x2;
            Z2 = z2;
            Type = type;
            WindMultiplier = double.IsFinite(windMultiplier) ? windMultiplier : 0.0;
        }

        public double X1 { get; }

        public double Z1 { get; }

        public double X2 { get; }

        public double Z2 { get; }

        public WeatherWallType Type { get; }

        /// <summary>
        /// The strength this wall's wind is served at. On a real client this comes
        /// from <c>1229 GlobalWallDataState</c> and is <b>zero</b> until that is
        /// served - see the type remarks. A server may pick any value it likes,
        /// but a value that disagrees with the 1229 we send is a wind the player
        /// feels and cannot see.
        /// </summary>
        public double WindMultiplier { get; }

        /// <summary>PROVED - <c>WallData.GetIntensityAt(float sqrDist)</c>, line 237.</summary>
        public static double IntensityAtSqrDistance(double sqrDistance)
        {
            if (!double.IsFinite(sqrDistance) || sqrDistance > EffectiveSqrMetres)
            {
                return 0.0;
            }
            if (sqrDistance < FullStrengthSqrMetres)
            {
                return 1.0;
            }
            return 1.0 - ((Math.Sqrt(sqrDistance) - 200.0) / 200.0);
        }

        /// <summary>
        /// Squared 2-D distance from a point to this segment.
        /// PROVED - <c>WallData.DistanceSqr</c> is
        /// <c>MathUtils.DistanceToLineSegmentSquared</c> on the XZ projection.
        /// </summary>
        public double SqrDistanceTo(double x, double z)
        {
            double dx = X2 - X1;
            double dz = Z2 - Z1;
            double lengthSqr = (dx * dx) + (dz * dz);
            double t = lengthSqr <= 0.0
                ? 0.0
                : (((x - X1) * dx) + ((z - Z1) * dz)) / lengthSqr;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }
            double px = X1 + (t * dx);
            double pz = Z1 + (t * dz);
            double ox = x - px;
            double oz = z - pz;
            return (ox * ox) + (oz * oz);
        }

        /// <summary>
        /// The wall's own wind at a point, BEFORE the intensity lerp.
        /// PROVED shape - <c>WallData.GetWindUnscaled</c>, line 207.
        /// The vertical term a Wind Rift also carries is dropped; see
        /// <see cref="WindSample"/> for why this field is horizontal.
        /// </summary>
        public void WindAt(double x, double z, out double windX, out double windZ)
        {
            if (Type == WeatherWallType.WindRift)
            {
                // Perpendicular, pointing AWAY from the wall's own line: retail
                // projects the point onto the line and normalises the offset.
                double dx = X2 - X1;
                double dz = Z2 - Z1;
                double lengthSqr = (dx * dx) + (dz * dz);
                double t = lengthSqr <= 0.0
                    ? 0.0
                    : (((x - X1) * dx) + ((z - Z1) * dz)) / lengthSqr;
                double ox = x - (X1 + (t * dx));
                double oz = z - (Z1 + (t * dz));
                double length = Math.Sqrt((ox * ox) + (oz * oz));
                if (length <= 0.0)
                {
                    windX = 0.0;
                    windZ = 0.0;
                    return;
                }
                windX = (ox / length) * WindMultiplier;
                windZ = (oz / length) * WindMultiplier;
                return;
            }

            // Every other type blows along the wall's forward axis.
            double fx = X2 - X1;
            double fz = Z2 - Z1;
            double forwardLength = Math.Sqrt((fx * fx) + (fz * fz));
            if (forwardLength <= 0.0)
            {
                windX = 0.0;
                windZ = 0.0;
                return;
            }
            windX = (fx / forwardLength) * WindMultiplier;
            windZ = (fz / forwardLength) * WindMultiplier;
        }
    }

    /// <summary>
    /// PROVED - <c>Assets.Scripts.UI.WorldEditor.WorldEditorWallData.WallType</c>,
    /// and the same six the admin map's <c>MapWallPalette</c> already colours. The
    /// 44 imported segments use only 0, 1, 3 and 5.
    /// </summary>
    public enum WeatherWallType
    {
        WindRift = 0,
        StormRift = 1,
        Typhon = 2,
        SandStorm = 3,
        IceStorm = 4,
        WorldEndWall = 5,
    }
}
