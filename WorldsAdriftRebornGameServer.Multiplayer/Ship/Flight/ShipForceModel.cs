using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The RECOVERED retail longitudinal force model, as pure maths.
    ///
    /// Unlike <see cref="FlightTuning"/> - whose every number is an invented feel
    /// knob and says so - most of the constants in this file are read directly off
    /// the shipped client and are labelled per member. The distinction is the whole
    /// point of the file: retail's ship physics was a conventional Unity rigidbody
    /// force stack running on the UnityWorker, and while the per-ship DATA it
    /// consumed (engine power, sail power, lift capacity) died with the Scala GSim,
    /// the CONSTANTS and the SHAPE of the equations shipped inside the client and
    /// are still readable.
    ///
    /// What retail did, in one paragraph. Engines called
    /// <c>ShipMotionVisualizer.AddForce(ShipThrustMultiplier * spin * (boost +
    /// power) * forward)</c>. Sails sampled the wind and called
    /// <c>AddSailForce</c> with a force perpendicular to the trimmed sail. Wind
    /// drag pulled the hull toward the local wind velocity with a quadratic law.
    /// The atlas core cancelled the ship's weight up to a lift ceiling. Nothing
    /// anywhere set a top speed: a ship's top speed is simply where thrust and
    /// drag balance, which is why "power to weight" was the only ship-building
    /// statistic that ever mattered.
    ///
    /// This server does not run a rigidbody, so the model here is the same physics
    /// reduced to the one longitudinal axis the kinematic integrator actually
    /// carries. That reduction is faithful for the quantity players feel - how
    /// fast the ship goes and how long it takes to get there - and drops the
    /// off-axis torque retail got from mounting an engine off-centre.
    /// </summary>
    public static class ShipForceModel
    {
        // ------------------------------------------------------------------
        // RECOVERED - read off the shipped client. Do not retune these to taste;
        // they are measurements, not preferences. Retuning belongs in the
        // WAREBORN TUNING block below.
        // ------------------------------------------------------------------

        /// <summary>
        /// PROVED - <c>ShipConfiguration.AirResistanceCoefficient</c> (decompile
        /// <c>acs/ShipConfiguration.cs:68</c>). The quadratic drag constant, in
        /// units of 1/metre: drag DECELERATION is <c>c * v^exponent</c>.
        ///
        /// Note this is an ACCELERATION, not a force - retail computed the drag
        /// acceleration and only then multiplied by mass
        /// (<c>WindPhysicsVisualizer.ApplyWindDrag</c>: <c>rb.mass *
        /// GetDrag(...)</c>). Mass therefore CANCELS out of the drag term, which
        /// is exactly why top speed depends on thrust-to-weight and not on mass
        /// alone.
        /// </summary>
        public const double AirResistanceCoefficient = 0.01;

        /// <summary>
        /// PROVED - <c>ShipConfiguration.AirResistanceExponent</c> (decompile
        /// <c>acs/ShipConfiguration.cs:66</c>). Drag goes as the SQUARE of the
        /// relative airspeed, which is what makes top speed scale as the square
        /// ROOT of thrust-to-weight.
        /// </summary>
        public const double AirResistanceExponent = 2.0;

        /// <summary>
        /// PROVED - <c>ShipConfiguration.ShipThrustMultiplier</c> (decompile
        /// <c>acs/ShipConfiguration.cs:72</c>). The global scalar every engine's
        /// thrust was multiplied by. It shipped at 1.0, i.e. retail shipped this
        /// lever centred; it exists so a live server can move all thrust at once
        /// without touching per-engine data.
        /// </summary>
        public const double ShipThrustMultiplier = 1.0;

        /// <summary>
        /// PROVED - <c>SailBehaviour.MinEfficiency</c> (decompile
        /// <c>acs/SailBehaviour.cs:11</c>), applied as the <c>minPower</c> floor
        /// <c>0.3f * wind.magnitude * SailState.Power</c> at <c>:54</c>.
        ///
        /// This is a real and deliberate design decision, not a rounding artefact:
        /// a badly trimmed sail still delivers 30% of its force, so a player can
        /// never be completely becalmed by pointing the wrong way. It is the
        /// reason sailing in this game is forgiving.
        /// </summary>
        public const double SailMinEfficiency = 0.3;

        /// <summary>
        /// PROVED magnitude, DELIBERATELY REAIMED - retail's low-speed settling
        /// term, the second half of <c>WindPhysicsVisualizer.GetDrag</c>. After the
        /// quadratic term, retail added a correction capped at <c>0.03f * dt</c>
        /// per step, i.e. an acceleration of at most 0.03 m/s^2, pointing from the
        /// ship's velocity toward the LOCAL WIND velocity.
        ///
        /// It exists because quadratic drag alone can never stop anything: at
        /// 0.08 m/s the quadratic term is 0.000064 m/s^2 and a coasting ship crawls
        /// forever. Retail's term closes that gap.
        ///
        /// OUR DEPARTURE, stated plainly: retail aimed this term at the wind, so a
        /// parked retail hull drifted downwind indefinitely at up to the wind speed.
        /// We aim it at ZERO instead. A world in which every unmanned hull drifts
        /// forever is a world in which every unmanned hull emits control points
        /// forever, and unbounded per-hull traffic is the exact congestion class the
        /// standing multiplayer-safety rule exists to prevent - it has already cost
        /// this project one reliable-relay spiral. The magnitude is retail's; the
        /// target is ours. Restoring the true downwind drift is a real fidelity item
        /// and belongs with weather, where the wind stops being a constant.
        /// </summary>
        public const double LowSpeedSettleAccelMps2 = 0.03;

        /// <summary>
        /// WAREBORN TUNING - the speed below which <see cref="LowSpeedSettleAccelMps2"/>
        /// is applied. At 1 m/s the recovered quadratic term has fallen to
        /// 0.01 m/s^2, a third of the settling term, so this is the point where
        /// drag has stopped doing the job and something else must finish it. A ship
        /// coasting down from cruise therefore decelerates on the recovered law
        /// almost the whole way and only picks this up for the last metre per
        /// second, taking roughly six seconds to come to a true stop.
        /// </summary>
        public const double SettleThresholdMps = 1.0;

        /// <summary>
        /// PROVED - the client's OWN fallback wind, returned by
        /// <c>GlobalWeather.GetCellSampleAt</c> (decompile
        /// <c>acs/Assets.Visualizers.Weather/GlobalWeather.cs:66</c>) for any
        /// position with no <c>1139 WeatherCellState</c> entity covering it.
        ///
        /// THIS IS WHY SAILS DO NOT NEED WEATHER. This server deliberately serves
        /// no weather cells (<c>ComponentAbsencePolicy</c> - restoring 1139 on
        /// gameplay entities produced a measured 31,144 client errors in 158 s),
        /// so every position in our world falls through to exactly this constant.
        /// A server-side sail model that uses this vector is not inventing a wind
        /// field; it is agreeing with the wind the shipped client already believes
        /// in everywhere. A real, varying wind field still needs weather - see the
        /// roadmap - but a CONSTANT one is free and is what retail's own fallback
        /// specifies.
        ///
        /// Magnitude is sqrt(5) = 2.236 m/s, blowing toward +X/-Z.
        /// </summary>
        public const double DefaultWindX = 1.0;
        public const double DefaultWindY = 0.0;
        public const double DefaultWindZ = -2.0;

        // ------------------------------------------------------------------
        // WAREBORN TUNING - ours, not Bossa's. These are the per-ship DATA values
        // the Scala GSim computed and that did not ship in any client asset. The
        // SHAPE they plug into is recovered; the magnitudes are chosen, and are
        // chosen to be CALIBRATED rather than arbitrary - see each remark.
        // ------------------------------------------------------------------

        /// <summary>
        /// WAREBORN TUNING - thrust in newtons contributed by one mounted engine
        /// at full throttle. Retail read this per engine from
        /// <c>1116 ShipEngineState.Power</c>, which the GSim computed from the
        /// engine's head part, tier, and the material and quality of its
        /// combustion internals and propeller. None of that survives: the client
        /// only ever RECEIVED the finished number.
        ///
        /// CALIBRATION, so this is not a number out of the air. The reference hull
        /// this server has always published is 800 kg
        /// (<c>HullMassCalculator.ReferenceHullMassKg</c>), and the speed the
        /// server has flown at since flight shipped is
        /// <c>FlightTuning.DefaultMaxSpeedMps</c> = 12 m/s. Inverting the recovered
        /// drag law for a two-engine reference ship:
        /// <c>a = v^2 * c = 144 * 0.01 = 1.44 m/s^2</c>, so
        /// <c>F = 1.44 * 800 = 1152 N</c> total, i.e. 576 N per engine. Rounded to
        /// 600, which puts the reference two-engine ship at 12.2 m/s - within a
        /// fifth of a metre per second of the speed players already have.
        ///
        /// That is the point: turning the force model on must not lurch the live
        /// game. It re-derives today's speed from real quantities, and only THEN
        /// starts to differ for ships that are unusually light, heavy, or
        /// unusually engined.
        /// </summary>
        public const double DefaultEngineThrustNewtons = 600.0;

        /// <summary>
        /// WAREBORN TUNING - one unfurled sail's <c>SailState.Power</c>, the
        /// linear coefficient in retail's <c>efficiency * |wind| * Power</c>.
        /// Retail's own value is lost for the same reason engine power is; this
        /// server currently seeds the component's power field at a placeholder
        /// 1.0 (<c>ComponentsSerializer</c>), which in retail's equation would be
        /// 2.2 newtons and would move an 800 kg ship at 0.003 m/s^2 - i.e. the
        /// seed is a stub, not a physical value.
        ///
        /// CALIBRATION: chosen so that a reference 800 kg hull under sail ALONE,
        /// engines idle, settles at a believable drift-to-cruise. Two well-trimmed
        /// sails give <c>F = 2 * 1.0 * 2.236 * 30 = 134 N</c>, so
        /// <c>a = 0.168 m/s^2</c> and terminal <c>v = 10*sqrt(a) = 4.1 m/s</c>;
        /// badly trimmed, the 0.3 efficiency floor still yields 2.2 m/s. Sails are
        /// therefore worth roughly a third of a ship's speed on their own and a
        /// few percent on top of a fully engined ship - supplementary, free, and
        /// always working, which is what retail's always-on wind force made them.
        /// </summary>
        public const double DefaultSailPowerNewtonsPerWind = 30.0;

        // ------------------------------------------------------------------
        // The equations.
        // ------------------------------------------------------------------

        /// <summary>The default wind's magnitude, m/s. sqrt(1 + 4) = 2.236.</summary>
        public static double DefaultWindSpeedMps =>
            Math.Sqrt((DefaultWindX * DefaultWindX)
                + (DefaultWindY * DefaultWindY)
                + (DefaultWindZ * DefaultWindZ));

        /// <summary>
        /// Drag DECELERATION, m/s^2, at a given airspeed. RECOVERED shape and
        /// constants; mass-independent by construction (see
        /// <see cref="AirResistanceCoefficient"/>).
        ///
        /// Total: a negative, NaN or infinite speed yields 0 rather than throwing
        /// or returning a force that would fling the ship - a malformed state must
        /// leave the ship flying.
        /// </summary>
        public static double DragDecelerationMps2(double speedMps)
        {
            if (!double.IsFinite(speedMps))
            {
                return 0.0;
            }
            double s = Math.Abs(speedMps);
            return AirResistanceCoefficient * Math.Pow(s, AirResistanceExponent);
        }

        /// <summary>
        /// The speed, m/s, at which a given thrust balances drag - i.e. the ship's
        /// actual top speed. This is a CONSEQUENCE of the model, never an input:
        /// retail set no speed cap anywhere, and neither does this.
        ///
        /// <c>F/m = c * v^2</c> so <c>v = sqrt(F / (m * c)) = 10 * sqrt(F/m)</c>
        /// with the recovered c = 0.01. The square root is the single most
        /// important consequence for ship building: DOUBLING a ship's engines buys
        /// only 1.41x the top speed, and doubling its mass costs only 0.71x. It is
        /// also why "power to weight" was the statistic retail players optimised,
        /// and it agrees in shape - though not in units - with the one published
        /// community speed model, WAEngenius's <c>50*sqrt(2*power/weight)</c>.
        /// </summary>
        public static double TerminalSpeedMps(double thrustNewtons, double massKg)
        {
            if (!double.IsFinite(thrustNewtons) || !double.IsFinite(massKg)
                || massKg <= 0.0 || thrustNewtons <= 0.0)
            {
                return 0.0;
            }
            return Math.Sqrt(thrustNewtons / (massKg * AirResistanceCoefficient));
        }

        /// <summary>
        /// One explicit-Euler step of <c>dv/dt = a_thrust - c*v*|v|</c>, returning
        /// the new speed. Drag always opposes travel, so it brakes a reversing
        /// ship as readily as a forward one.
        ///
        /// STABILITY: the explicit step is stable while <c>2*c*|v|*dt &lt; 2</c>,
        /// i.e. below <c>1/(c*dt)</c> = 416 m/s at the 0.24 s control-point
        /// cadence. The wire clamp lands at 60 m/s, seven times inside that, so
        /// this cannot ring. Guarded anyway: a non-finite input returns the old
        /// speed rather than propagating NaN into the control-point stream, which
        /// would strand the hull for every client watching it.
        /// </summary>
        public static double StepSpeed(double speedMps, double thrustAccelMps2, double dtSeconds)
        {
            if (!double.IsFinite(speedMps) || !double.IsFinite(thrustAccelMps2)
                || !double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            {
                return double.IsFinite(speedMps) ? speedMps : 0.0;
            }

            // Quadratic drag alone can never STOP anything - it vanishes faster
            // than the speed it is killing, so a coasting ship crawls forever at a
            // tenth of a metre per second and never settles, which on the wire
            // means it never goes quiet either. Retail's own answer is the second
            // term of WindPhysicsVisualizer.GetDrag; see LowSpeedSettleAccelMps2
            // for its magnitude and for the one way we differ from it.
            //
            // It applies ONLY to an UNDRIVEN ship - lever centred, no canvas - and
            // only below SettleThresholdMps. Both conditions matter. Letting it run
            // at cruise would shift every ship's top speed for no physical reason
            // and make TerminalSpeedMps a lie; letting it run against a driven ship
            // would turn it into STICTION, and retail had none - a sail worth only
            // nine newtons on an 800 kg hull really did get that hull moving, just
            // very slowly. A settling term that can veto thrust is a different and
            // wrong model.
            double decel = DragDecelerationMps2(speedMps);
            bool undriven = thrustAccelMps2 == 0.0;
            if (undriven && Math.Abs(speedMps) < SettleThresholdMps)
            {
                decel += LowSpeedSettleAccelMps2;
            }
            double dragDelta = decel * dtSeconds;
            double speedMagnitude = Math.Abs(speedMps);
            if (dragDelta > speedMagnitude)
            {
                dragDelta = speedMagnitude;
            }

            double next = speedMps
                - (Math.Sign(speedMps) * dragDelta)
                + (thrustAccelMps2 * dtSeconds);
            return double.IsFinite(next) ? next : 0.0;
        }

        /// <summary>
        /// The forward newtons a rigged sail plan delivers, given the ship's
        /// heading and the prevailing wind.
        ///
        /// RECOVERED GEOMETRY. Retail's <c>SailBehaviour.Update</c> trimmed the
        /// sail's yaw joint toward
        /// <c>LookRotation(ship.forward*1.01 - windDir, up)</c> flattened to the
        /// horizontal, then pushed along that joint's RIGHT axis with magnitude
        /// <c>|dot(windDir, joint.right)| * |wind| * Power</c>, flipping the sign
        /// so the force always has a downwind component. <c>AddSailForce</c> then
        /// STRIPPED the component along the hull's right axis - retail's implicit
        /// keel - leaving drive along the hull. That entire chain is reproduced
        /// here in two horizontal dimensions.
        ///
        /// The one modelling assumption is that the sail is TRIMMED, i.e. the yaw
        /// joint has reached its target. Retail slerped toward it at 6/s, so it
        /// arrives inside ~0.2 s, well under the 0.24 s control-point cadence this
        /// runs at. Assuming the settled angle is therefore accurate at the only
        /// resolution the client can observe.
        ///
        /// Returns a SIGNED value along the heading: a sail can push a ship
        /// backwards when the wind is dead ahead of it, which is correct and is
        /// what makes the wind direction matter to a player.
        /// </summary>
        public static double SailForwardNewtons(
            int unfurledSails, double headingRadians, double sailPowerNewtonsPerWind,
            double windX = DefaultWindX, double windZ = DefaultWindZ)
        {
            if (unfurledSails <= 0 || !double.IsFinite(headingRadians)
                || !double.IsFinite(sailPowerNewtonsPerWind) || sailPowerNewtonsPerWind <= 0.0
                || !double.IsFinite(windX) || !double.IsFinite(windZ))
            {
                return 0.0;
            }

            double windSpeed = Math.Sqrt((windX * windX) + (windZ * windZ));
            if (windSpeed <= 1e-9)
            {
                return 0.0;
            }
            double wx = windX / windSpeed;
            double wz = windZ / windSpeed;

            // Hull forward, from the same yaw convention FlightState documents:
            // yaw 0 faces +Z, positive turns the nose toward +X.
            double fx = Math.Sin(headingRadians);
            double fz = Math.Cos(headingRadians);

            // The trimmed boom: forward*1.01 - windDir, flattened and normalised.
            // The 1.01 is retail's own and breaks the degenerate tie when the ship
            // sails exactly downwind, where forward - windDir would be the zero
            // vector and LookRotation would be undefined.
            double bx = (fx * 1.01) - wx;
            double bz = (fz * 1.01) - wz;
            double bLen = Math.Sqrt((bx * bx) + (bz * bz));
            if (bLen <= 1e-9)
            {
                return 0.0;
            }
            bx /= bLen;
            bz /= bLen;

            // The joint's RIGHT axis, i.e. the boom rotated +90 degrees about +Y
            // in Unity's left-handed frame: right = (bz, -bx).
            double rx = bz;
            double rz = -bx;

            // Retail's efficiency: how squarely the wind meets the sail. Floored
            // at MinEfficiency by the minPower clamp inside AddSailForce.
            double efficiency = Math.Abs((wx * rx) + (wz * rz));
            if (efficiency < SailMinEfficiency)
            {
                efficiency = SailMinEfficiency;
            }

            double magnitude = efficiency * windSpeed * sailPowerNewtonsPerWind;

            // Sign: the force must carry the ship downwind, never into the wind.
            double fxForce = rx * magnitude;
            double fzForce = rz * magnitude;
            if (((wx * fxForce) + (wz * fzForce)) < 0.0)
            {
                fxForce = -fxForce;
                fzForce = -fzForce;
            }

            // The keel: only the along-hull component survives.
            return ((fxForce * fx) + (fzForce * fz)) * unfurledSails;
        }
    }
}
