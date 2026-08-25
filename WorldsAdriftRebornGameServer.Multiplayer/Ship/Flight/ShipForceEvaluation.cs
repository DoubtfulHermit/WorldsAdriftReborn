using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The complete longitudinal force/wind answer for one hull at one simulation
    /// instant. Runtime integration and operator telemetry consume this SAME value;
    /// neither is allowed to resample wind, omit walls or choose a newer clock.
    /// </summary>
    public readonly struct ShipForceEvaluation
    {
        public static ShipForceEvaluation Unavailable => default;

        public ShipForceEvaluation(double sampledAtSeconds, WindSample wind,
            double massKg, int unfurledSails, double windAngleDegrees, double engineForceNewtons,
            double sailForceNewtons, double propulsionAccelerationMps2,
            double windAlongHeadingMps, double predictedSettledSpeedMps)
        {
            Present = true;
            SampledAtSeconds = sampledAtSeconds;
            Wind = wind;
            MassKg = massKg;
            UnfurledSails = unfurledSails;
            WindAngleDegrees = windAngleDegrees;
            EngineForceNewtons = engineForceNewtons;
            SailForceNewtons = sailForceNewtons;
            PropulsionAccelerationMps2 = propulsionAccelerationMps2;
            WindAlongHeadingMps = windAlongHeadingMps;
            PredictedSettledSpeedMps = predictedSettledSpeedMps;
        }

        public bool Present { get; }
        public double SampledAtSeconds { get; }
        public WindSample Wind { get; }
        public double MassKg { get; }
        public int UnfurledSails { get; }
        public double WindAngleDegrees { get; }
        public double EngineForceNewtons { get; }
        public double SailForceNewtons { get; }
        public double PropulsionAccelerationMps2 { get; }
        public double WindAlongHeadingMps { get; }
        public double PredictedSettledSpeedMps { get; }
    }

    public static class ShipForceEvaluator
    {
        public static ShipForceEvaluation Evaluate(
            double x, double z, double headingRadians,
            FlightControlInput input, ShipPropulsion ship, FlightTuning tuning,
            double sampledAtSeconds,
            IReadOnlyList<WeatherWallSegment>? walls = null)
        {
            WindSample wind = WindField.SampleAt(
                x, z, sampledAtSeconds,
                tuning.WindSpeedMps, tuning.WindVariation, walls);

            double throttle = Math.Clamp(input.Throttle, -1.0, 1.0);
            double engineForce = ship.EngineThrustNewtons
                * ShipForceModel.ShipThrustMultiplier
                * (throttle >= 0.0 ? throttle : throttle * tuning.ReverseFactor);
            double sailForce = ShipForceModel.SailForwardNewtons(
                ship.UnfurledSails, headingRadians, tuning.SailPowerNewtons,
                wind.WindX, wind.WindZ);
            double totalForce = engineForce + sailForce;

            double windAlongHeading = CarriedWindAlongHeadingMps(
                in wind, headingRadians, throttle, sailForce, ship.MassKg, tuning);

            return new ShipForceEvaluation(
                sampledAtSeconds, wind, ship.MassKg, ship.UnfurledSails,
                ShipForceModel.WindAngleDegrees(headingRadians, wind.WindX, wind.WindZ),
                engineForce, sailForce, totalForce / ship.MassKg, windAlongHeading,
                ShipForceModel.PredictedSettledSpeedMps(
                    totalForce, ship.MassKg, windAlongHeading));
        }

        /// <summary>
        /// THE ONE CARRIED-WIND DECISION - the tier logic that says what wind a
        /// hull is actually carried by. Extracted so the scalar path (above) and
        /// the vector runtime consume the identical code and can never diverge
        /// (kill-list: no parallel calculations). Three tiers, in decision order:
        ///
        /// 1. WALL AIR - spatial resistance: the signed wall wind along the bow,
        ///    attenuated by the recovered mass law
        ///    (<see cref="ShipForceModel.WindMultiplier"/>), throttle or not.
        /// 2. THE SKY-CORE COMMANDED BASELINE - the throttle-requested bare-hull
        ///    carry (<c>alongMps * throttle * BareHullDriveMultiplier</c>). The
        ///    baseline and canvas are separate propulsion tiers: opening canvas
        ///    must not remove an already-commanded baseline - doing so made a
        ///    live two-sail ship slower than the same hull with its sails furled.
        /// 3. CANVAS - natural wind as the canvas floor, with any stronger
        ///    commanded baseline retained, sail force added independently above.
        ///
        /// <paramref name="throttle"/> must already be clamped to [-1, 1] (the
        /// scalar caller clamps before computing engine force);
        /// <paramref name="sailForceNewtons"/> is the production
        /// <see cref="ShipForceModel.SailForwardNewtons"/> answer, whose
        /// magnitude decides "is canvas driving"; <paramref name="massKg"/> is
        /// the ONE snapshot flight mass (<c>ShipPropulsion.MassKg</c> /
        /// <c>ShipMassSnapshot.TotalFlightMassKg</c>).
        /// </summary>
        public static double CarriedWindAlongHeadingMps(in WindSample wind,
            double headingRadians, double throttle, double sailForceNewtons,
            double massKg, FlightTuning tuning)
        {
            double windAlongHeading = 0.0;
            bool canvasIsDriving = Math.Abs(sailForceNewtons) >= 1e-9;
            if (wind.WallIntensity > 0.0)
            {
                // Wall air is spatial resistance and therefore acts with the
                // lever centred. Preserve the sign of a headwind so crossing a
                // wall remains a force contest instead of a free speed bonus.
                windAlongHeading = WindField.SignedAlongHeading(in wind, headingRadians)
                    * ShipForceModel.WindMultiplier(massKg);
            }
            else if (throttle > 0.0 || canvasIsDriving)
            {
                double alongMps = tuning.WindVariation.IsEnabled
                    ? WindField.AlongHeading(in wind, headingRadians)
                        * ShipForceModel.WindMultiplier(massKg)
                    : ShipForceModel.BaselineDriveSpeedMps(massKg, tuning.WindSpeedMps);
                // The sky-core baseline and canvas are separate propulsion tiers.
                // Opening canvas must not remove an already-commanded baseline:
                // doing so made a live two-sail ship slower than the same hull with
                // its sails furled. Keep natural wind as the canvas floor, retain
                // any stronger throttle-requested WAReborn baseline, and continue
                // to add sail force independently above it.
                double commandedBaseline = alongMps
                    * throttle * tuning.BareHullDriveMultiplier;
                windAlongHeading = canvasIsDriving
                    ? Math.Max(alongMps, commandedBaseline)
                    : commandedBaseline;
            }
            return windAlongHeading;
        }
    }
}
