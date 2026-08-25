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
    /// drag pulled the hull toward the local wind velocity with a 2.5-power law.
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
        /// RECOVERED SHIPPED VALUE - the serialized
        /// <c>Resources/Configs/ShipConfig</c> ScriptableObject in
        /// <c>resources.assets</c>. The decompiled field initializer is 0.01, but
        /// Unity serialization overrides it with 0.007 in the build players ran.
        /// Drag DECELERATION is <c>c * v^exponent</c>.
        ///
        /// Note this is an ACCELERATION, not a force - retail computed the drag
        /// acceleration and only then multiplied by mass
        /// (<c>WindPhysicsVisualizer.ApplyWindDrag</c>: <c>rb.mass *
        /// GetDrag(...)</c>). Mass therefore CANCELS out of the drag term, which
        /// is exactly why top speed depends on thrust-to-weight and not on mass
        /// alone.
        /// </summary>
        public const double AirResistanceCoefficient = 0.007;

        /// <summary>
        /// RECOVERED SHIPPED VALUE - the same serialized ShipConfig. The
        /// decompiled initializer is 2.0; the shipped asset overrides it with 2.5.
        /// ShipConfiguration was remotely overridable, so retail live may have
        /// changed it, but the client-shipped pair is the strongest surviving
        /// authority.
        /// </summary>
        public const double AirResistanceExponent = 2.5;

        /// <summary>
        /// PROVED SHIPPED VALUE - <c>GetDrag</c> leaves the primary power-law
        /// direction at zero at or below 0.1 m/s. The residual correction remains
        /// active, so this is not a dead zone and does not prevent exact settling.
        /// </summary>
        public const double PrimaryDragDirectionThresholdMps = 0.1;

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
        /// PROVED SHIPPED VALUE - retail's residual drag term, the second half of
        /// <c>WindPhysicsVisualizer.GetDrag</c>. After the primary drag term,
        /// retail subtracted that step from the relative-wind vector, then added a
        /// correction capped at <c>0.03f * dt</c> per step, i.e. an acceleration
        /// of at most 0.03 m/s^2, toward the LOCAL WIND velocity.
        ///
        /// It exists because power-law drag alone can never stop anything: at
        /// 0.08 m/s the primary term is under 0.000013 m/s^2 and a coasting ship crawls
        /// forever. Retail's term closes that gap.
        ///
        /// This correction is NOT conditional on low speed or on propulsion being
        /// absent. The shipped method applies it on every call and clamps only to
        /// the relative wind left after the primary step. WAReborn previously put
        /// it behind an invented 1 m/s/undriven gate; that made a 4.11 m/s coast
        /// take about 115 seconds instead of the recovered curve's 68 seconds.
        ///
        /// WAReborn's separate operational departure remains in
        /// <c>ShipForceEvaluator</c>: ambient wind is withheld from an abandoned
        /// hull so it can eventually go quiet. Once a relative-wind target is
        /// supplied here, both the direction and cadence of this term are retail's.
        /// </summary>
        public const double LowSpeedSettleAccelMps2 = 0.03;

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

        /// <summary>
        /// PROVED - <c>WindPhysicsVisualizer.ApplyDrag</c> (decompile
        /// <c>acs/Assets.Visualizers.Weather/WindPhysicsVisualizer.cs</c>):
        /// <code>
        ///   float num = Mathf.Clamp01(rb.mass / 4000f) * 0.75f;
        ///   return ApplyWindDrag(pos, rb, 1f - num);
        /// </code>
        /// The wind a hull actually feels is attenuated by its own mass: a 4000 kg
        /// ship feels 25% of the wind, a 500 kg one feels 91%. Heavy ships are
        /// shoved around less by weather - but note this multiplies the WIND only,
        /// never the power-law self-drag, so heavy ships still coast identically.
        /// </summary>
        public const double WindMassAttenuationReferenceKg = 4000.0;
        public const double WindMassAttenuationMax = 0.75;

        /// <summary>
        /// The fraction of the ambient wind a hull of this mass feels. PROVED; see
        /// <see cref="WindMassAttenuationReferenceKg"/>.
        /// </summary>
        public static double WindMultiplier(double massKg)
        {
            if (!double.IsFinite(massKg) || massKg <= 0.0)
            {
                return 1.0;
            }
            double ratio = massKg / WindMassAttenuationReferenceKg;
            if (ratio > 1.0)
            {
                ratio = 1.0;
            }
            return 1.0 - (ratio * WindMassAttenuationMax);
        }

        /// <summary>
        /// THE BARE-HULL BASELINE, and the answer to *"the ship without sails can
        /// move too, but really slowly"*.
        ///
        /// MAGNITUDE PROVED, AIM OURS - the same class of departure, and for
        /// exactly the same reason, as <see cref="LowSpeedSettleAccelMps2"/>.
        ///
        /// Retail did not have a separate "self drag" and a separate "wind push".
        /// It had ONE term, and this is the single most useful thing to know about
        /// its flight model: <c>WindPhysicsVisualizer.ApplyWindDrag</c> computes
        /// <c>GetDrag(wind * windMultiplier - rb.velocity, ...)</c>, i.e. the
        /// power law acts on the RELATIVE wind. Set the wind to zero and it is
        /// ordinary drag opposing travel; set the velocity to zero and the very
        /// same term ACCELERATES a stationary hull toward the wind. A ship's
        /// terminal drift is therefore just <c>|wind| * windMultiplier(mass)</c>.
        ///
        /// And a parked ship is NOT exempt. <c>ManagedFixedUpdate</c> early-returns
        /// for a near-asleep rigidbody only when <c>!IsFloatingShip</c>, where
        /// <c>IsFloatingShip = _shipLift != null &amp;&amp; !_shipLift.IsOverloaded</c>.
        /// So **any hull with a working sky core keeps feeling the wind at rest** -
        /// which is precisely the maintainer's "a bare hull moves, slowly", and it
        /// is why the sky core is what makes a ship mobile at all rather than the
        /// sails being a hard prerequisite.
        ///
        /// On our constant wind that works out at roughly 2 m/s - just under
        /// 4 knots - falling to 1 knot for a 4000 kg barge. For scale, the client's
        /// own helm wind VFX does not even switch on below 5 knots, so a bare hull
        /// reads as DRIFTING rather than as sailing. That is the intended feel.
        ///
        /// WHERE WE DEPART, stated as plainly as the settle term above. Retail
        /// aimed this along the WIND, so a bare hull could only ever travel
        /// downwind. Retail could get away with that because its wind varied by
        /// place and time; ours is a single global constant everywhere
        /// (see <see cref="DefaultWindX"/>), so a strictly downwind baseline would
        /// mean a bare hull can travel in exactly ONE compass direction, for ever,
        /// and is worse than retail rather than more faithful to it. We therefore
        /// keep retail's MAGNITUDE and aim it along the hull's own heading while a
        /// pilot is commanding throttle. Restoring the true downwind aim is a real
        /// fidelity item and belongs with weather, alongside the settle term.
        /// </summary>
        public static double BaselineDriveSpeedMps(double massKg) =>
            BaselineDriveSpeedMps(massKg, DefaultWindSpeedMps);

        /// <summary>
        /// The same, for a world whose wind is not the client's fallback strength.
        /// See <c>FlightTuning.WindSpeedMps</c> for why that is a knob worth having:
        /// 2.236 m/s is what retail returned where NO weather cell existed, i.e. it
        /// is the becalmed case rather than a typical one.
        /// </summary>
        public static double BaselineDriveSpeedMps(double massKg, double windSpeedMps)
        {
            if (!double.IsFinite(windSpeedMps) || windSpeedMps <= 0.0)
            {
                return 0.0;
            }
            return windSpeedMps * WindMultiplier(massKg);
        }

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
        /// serialized drag law for a two-engine reference ship:
        /// <c>a = 0.007 * 12^2.5 = 3.492 m/s^2</c>, so
        /// <c>F = 3.492 * 800 = 2794 N</c> total, i.e. 1397 N per engine. Rounded
        /// to 1400. This independently lands inside the 1,200-1,850 N range inferred
        /// from the surviving WAEngenius community power law.
        ///
        /// That is the point: turning the force model on must not lurch the live
        /// game. It re-derives today's speed from real quantities, and only THEN
        /// starts to differ for ships that are unusually light, heavy, or
        /// unusually engined.
        /// </summary>
        public const double DefaultEngineThrustNewtons = 1400.0;

        /// <summary>
        /// WAREBORN TUNING - one unfurled sail's <c>SailState.Power</c>, the
        /// linear coefficient in retail's <c>efficiency * |wind| * Power</c>.
        /// Retail's own value is lost for the same reason engine power is; this
        /// server currently seeds the component's power field at a placeholder
        /// 1.0 (<c>ComponentsSerializer</c>), which in retail's equation would be
        /// 2.2 newtons and would move an 800 kg ship at 0.003 m/s^2 - i.e. the
        /// seed is a stub, not a physical value.
        ///
        /// CALIBRATION (2026-08-22): 840 is the upper end of a surviving retail
        /// balance bracket, not a claim that the lost GSim value has been recovered.
        /// Update 27 build 989 explicitly "Halved wind power, which functionally
        /// halves thrust from sails." The shipped client retained the force equation
        /// but not either era's server-authored SailState.Power, so 420 and 840 are
        /// the only evidence-linked pair available to us. Live acceptance then
        /// established that the lower member made a real 3,094 kg, two-sail ship
        /// take roughly 45 seconds to reach 5.43 m/s and still read as too slow.
        /// Choosing 840 deliberately restores the stronger known retail balance
        /// era without changing ambient wind, drag, hull mass or stopping behaviour.
        ///
        /// It also stays inside the surviving instruments and community evidence:
        /// four sails on an 800 kg total flight mass settle near 38 knots, beyond
        /// the client's 30-knot "fast" VFX mark but far below its 70-knot dial;
        /// modelling the reported 69-sail experiment with our deliberately
        /// conservative 50 kg/part placeholder reaches about 57 knots. The full
        /// mass/sail/heading and acceleration matrices are pinned in
        /// SailCalibrationMatrixTests and documented in the 2026-08-22 audit.
        /// </summary>
        public const double DefaultSailPowerNewtonsPerWind = 840.0;

        // ------------------------------------------------------------------
        // The equations.
        // ------------------------------------------------------------------

        /// <summary>The default wind's magnitude, m/s. sqrt(1 + 4) = 2.236.</summary>
        public static double DefaultWindSpeedMps =>
            Math.Sqrt((DefaultWindX * DefaultWindX)
                + (DefaultWindY * DefaultWindY)
                + (DefaultWindZ * DefaultWindZ));

        /// <summary>
        /// Drag DECELERATION, m/s^2, at a given airspeed. RECOVERED shipped shape and
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
        /// <c>F/m = c * v^p</c> so <c>v = (F / (m*c))^(1/p)</c> with the
        /// recovered shipped <c>p = 2.5</c>. The 0.4 power is the single most
        /// important consequence for ship building: DOUBLING a ship's engines buys
        /// only 1.32x the top speed, and doubling its mass costs only 0.76x. It is
        /// also why "power to weight" was the statistic retail players optimised,
        /// and it agrees in shape - though not in units - with the one published
        /// community speed model, WAEngenius's <c>50*sqrt(2*power/weight)</c>.
        ///
        /// THIS IS THE STILL-AIR FIGURE. With a wind along the heading, equilibrium
        /// is <c>F/m = c*(v - w)^2</c>, so the settled speed is exactly
        /// <c>w + TerminalSpeedMps(F, m)</c> - a tailwind is simply additive, as in
        /// the real world. Reporting the still-air number here is deliberate: it is
        /// the part that belongs to the SHIP, and the part a ship-builder is
        /// comparing when they choose a hull material or bolt on another engine.
        /// See <see cref="BaselineDriveSpeedMps"/> for the term to add.
        /// </summary>
        public static double TerminalSpeedMps(double thrustNewtons, double massKg)
        {
            if (!double.IsFinite(thrustNewtons) || !double.IsFinite(massKg)
                || massKg <= 0.0 || thrustNewtons <= 0.0)
            {
                return 0.0;
            }
            return Math.Pow(
                thrustNewtons / (massKg * AirResistanceCoefficient),
                1.0 / AirResistanceExponent);
        }

        /// <summary>
        /// The signed equilibrium speed for a longitudinal force in moving air.
        /// RECOVERED equation: <c>F/m = c*sign(v-w)*|v-w|^2.5 + 0.03</c> in the
        /// force direction. The final constant is the always-on residual correction
        /// in <c>WindPhysicsVisualizer.GetDrag</c>, not WAReborn tuning. This is the
        /// prediction used by the operator inspector; keeping it here prevents a
        /// browser or stats writer from growing a second flight model.
        ///
        /// Below 0.03 m/s^2 the discrete retail force stack sits inside its final
        /// one-step correction rather than having a non-zero continuous balance
        /// point. Reporting the wind target is the honest stable prediction at that
        /// scale (the 0.24 s integrator can carry a sub-centimetre-per-second ripple).
        /// </summary>
        public static double PredictedSettledSpeedMps(
            double thrustNewtons, double massKg, double windAlongHeadingMps)
        {
            if (!double.IsFinite(thrustNewtons) || !double.IsFinite(massKg)
                || massKg <= 0.0 || !double.IsFinite(windAlongHeadingMps))
            {
                return 0.0;
            }
            if (Math.Abs(thrustNewtons) <= 1e-12)
            {
                return windAlongHeadingMps;
            }
            double balancedPrimaryAccel = Math.Max(
                0.0, (Math.Abs(thrustNewtons) / massKg) - LowSpeedSettleAccelMps2);
            if (balancedPrimaryAccel <= 0.0)
            {
                return windAlongHeadingMps;
            }
            return windAlongHeadingMps
                + (Math.Sign(thrustNewtons) * Math.Pow(
                    balancedPrimaryAccel / AirResistanceCoefficient,
                    1.0 / AirResistanceExponent));
        }

        /// <summary>
        /// Signed angle from the bow to the direction the wind is travelling,
        /// degrees in [-180, 180]. Zero is a tailwind and +/-180 a headwind.
        /// </summary>
        public static double WindAngleDegrees(double headingRadians, double windX, double windZ)
        {
            if (!double.IsFinite(headingRadians) || !double.IsFinite(windX)
                || !double.IsFinite(windZ) || ((windX * windX) + (windZ * windZ)) <= 1e-18)
            {
                return 0.0;
            }
            double windHeading = Math.Atan2(windX, windZ);
            double angle = windHeading - headingRadians;
            while (angle > Math.PI) angle -= Math.PI * 2.0;
            while (angle < -Math.PI) angle += Math.PI * 2.0;
            return angle * (180.0 / Math.PI);
        }

        /// <summary>
        /// One explicit-Euler step of <c>dv/dt = a_thrust - c*sign(v)*|v|^2.5</c>, returning
        /// the new speed. Drag always opposes travel, so it brakes a reversing
        /// ship as readily as a forward one.
        ///
        /// STABILITY: Euler is stable while
        /// <c>dt * p * c * |v|^(p-1) &lt; 2</c>. At the 60 m/s wire clamp and
        /// 0.24 s cadence that product is 1.95: inside the bound, deliberately
        /// close enough that the wire clamp remains load-bearing. Guarded anyway:
        /// a non-finite input returns the old
        /// speed rather than propagating NaN into the control-point stream, which
        /// would strand the hull for every client watching it.
        /// </summary>
        public static double StepSpeed(double speedMps, double thrustAccelMps2, double dtSeconds,
            double windAlongHeadingMps = 0.0)
        {
            if (!double.IsFinite(speedMps) || !double.IsFinite(thrustAccelMps2)
                || !double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            {
                return double.IsFinite(speedMps) ? speedMps : 0.0;
            }
            if (!double.IsFinite(windAlongHeadingMps))
            {
                windAlongHeadingMps = 0.0;
            }

            // Power-law drag alone can never STOP anything - it vanishes faster
            // than the speed it is killing, so a coasting ship crawls forever at a
            // tenth of a metre per second and never settles, which on the wire
            // means it never goes quiet either. Retail's own answer is the second
            // term of WindPhysicsVisualizer.GetDrag; see LowSpeedSettleAccelMps2
            // for its recovered magnitude and application order.
            //
            // RETAIL HAD ONE TERM, NOT TWO. WindPhysicsVisualizer.ApplyWindDrag
            // evaluates the recovered power law on the RELATIVE wind,
            // GetDrag(wind - velocity), so the identical expression is drag when
            // the ship outruns the air and THRUST when the air outruns the ship.
            // With windAlongHeadingMps at its default of 0 this reduces exactly to
            // "0.007 * |v|^2.5 opposing travel", which is what every existing caller
            // and test gets; passing a wind is what lets a bare hull get under way
            // (see BaselineDriveSpeedMps for the magnitude and for the one way the
            // aim differs from retail's).
            double relativeWind = windAlongHeadingMps - speedMps;
            double delta = RelativeWindApproachDeltaMps(Math.Abs(relativeWind), dtSeconds);

            double next = speedMps
                + (Math.Sign(relativeWind) * delta)
                + (thrustAccelMps2 * dtSeconds);
            return double.IsFinite(next) ? next : 0.0;
        }

        /// <summary>
        /// THE ONE RELATIVE-WIND APPROACH LAW, extracted so the scalar integrator
        /// (<see cref="StepSpeed"/>) and the vector runtime's horizontal air step
        /// consume the identical transcription and can never diverge. Given the
        /// magnitude of the relative wind (|wind - velocity|), returns how many
        /// m/s of that gap ONE step closes - always in [0, relativeMagnitude], so
        /// a caller can add it toward the wind without ever overshooting.
        ///
        /// Both terms are the recovered <c>WindPhysicsVisualizer.GetDrag</c>, in
        /// its decompiled order, exactly:
        ///
        /// 1. The primary 2.5-power law, gated OFF at or below the recovered
        ///    0.1 m/s direction threshold and clamped to magnitude/dt - retail's
        ///    first anti-overshoot clamp.
        /// 2. The residual settling correction, capped at 0.03*dt and clamped to
        ///    the relative wind LEFT OVER after the primary step (retail's
        ///    vector5). It exists because the power law vanishes faster than the
        ///    gap it is closing; without it a hull crawls at its last tenth of a
        ///    metre per second for ever and never goes quiet on the wire. There
        ///    is no speed threshold and no throttle/sail gate in the shipped
        ///    method, so there is none here.
        /// </summary>
        public static double RelativeWindApproachDeltaMps(
            double relativeMagnitudeMps, double dtSeconds)
        {
            if (!double.IsFinite(relativeMagnitudeMps) || relativeMagnitudeMps < 0.0
                || !double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            {
                return 0.0;
            }

            double primaryAccel = relativeMagnitudeMps > PrimaryDragDirectionThresholdMps
                ? DragDecelerationMps2(relativeMagnitudeMps)
                : 0.0;

            // The first of retail's two anti-overshoot clamps. GetDrag clamps the
            // power-law acceleration to magnitude / deltaTime before calculating
            // the residual correction. This is normally load-bearing only for a
            // very coarse step or extreme relative wind, but reproducing the order
            // makes this a transcription rather than a lookalike.
            primaryAccel = Math.Min(primaryAccel, relativeMagnitudeMps / dtSeconds);
            double primaryDelta = primaryAccel * dtSeconds;

            // The settling term aims at the relative wind, not at zero: with no
            // wind the relative wind IS -velocity, so it is the familiar
            // brake-to-a-stop; with a wind it is what actually lets a hull REACH
            // its drift speed instead of asymptotically creeping at it.
            double residualMagnitude = Math.Max(0.0, relativeMagnitudeMps - primaryDelta);
            double residualDelta = Math.Min(
                residualMagnitude, LowSpeedSettleAccelMps2 * dtSeconds);

            // Never overshoot the relative wind inside one step: retail clamped
            // the same way (number.Clamp(0f, magnitude / deltaTime)) so that drag
            // can bring a ship TO the air's speed but never push it past and
            // oscillate. At our 0.24 s cadence this is the difference between a
            // settled hull and one that hunts around the wind speed for ever.
            return primaryDelta + residualDelta;
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
        /// joint has reached its target. Retail used
        /// <c>Slerp(current,target,6*deltaTime)</c>, an asymptotic render-step
        /// transition rather than an authoritative snapped state. The server does
        /// not own that render cadence, so this is explicitly the equilibrium
        /// force approximation, not a claim that the joint settles in one 0.24 s
        /// control interval.
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
