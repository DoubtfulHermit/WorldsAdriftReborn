using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// One ship's physical make-up, as the force model needs it: how heavy it is,
    /// how hard its engines push, and how much canvas it has flying.
    ///
    /// This is the type that makes flight depend on the SHIP rather than on a
    /// constant. Everything in it is derived from real mounted parts and real hull
    /// materials; nothing in it is a tuning knob. Where the per-part magnitudes
    /// come from - and which of them are recovered and which are ours - is
    /// documented on <see cref="ShipForceModel"/>.
    ///
    /// A ship with no engines has zero ENGINE thrust and is NOT a bug: retail's
    /// progression ran hull, then sails, then engines, and a player sailed for a
    /// long time before they could build an engine.
    ///
    /// A CORRECTION worth keeping, because the sentence that used to be here was
    /// believed twice and is the reason this flag stayed off longer than it needed
    /// to. It claimed a hull with neither engines nor sails "simply hangs in the
    /// air". It does not. Retail's wind acted on the HULL, and
    /// <c>WindPhysicsVisualizer.ManagedFixedUpdate</c> exempts any ship with a
    /// working sky core from its at-rest early return, so a bare hull drifts at
    /// roughly 2 m/s. That baseline lives in
    /// <see cref="ShipForceModel.BaselineDriveSpeedMps"/> rather than in this
    /// struct, because it is a property of the hull and the air rather than of
    /// anything mounted on it - which is exactly why it is not zero here.
    /// </summary>
    public readonly struct ShipPropulsion
    {
        public ShipPropulsion(double massKg, double engineThrustNewtons, int unfurledSails)
        {
            MassKg = double.IsFinite(massKg) && massKg > 0.0 ? massKg : 1.0;
            EngineThrustNewtons = double.IsFinite(engineThrustNewtons) && engineThrustNewtons > 0.0
                ? engineThrustNewtons : 0.0;
            UnfurledSails = unfurledSails < 0 ? 0 : unfurledSails;
        }

        /// <summary>
        /// Total ship mass in kilograms, from the hull's real materials and its
        /// real cell and deck counts (<c>HullMassCalculator</c>). Never zero or
        /// negative: a malformed hull is treated as 1 kg rather than dividing the
        /// whole model by zero.
        /// </summary>
        public double MassKg { get; }

        /// <summary>
        /// The sum of every mounted engine's thrust, in newtons, at full throttle.
        /// Zero for a hull with no engines mounted.
        /// </summary>
        public double EngineThrustNewtons { get; }

        /// <summary>How many mounted sails are currently unfurled.</summary>
        public int UnfurledSails { get; }

        /// <summary>
        /// Thrust-to-weight as an acceleration, m/s^2 - the single number that
        /// decides how this ship flies. Top speed is
        /// <c>10 * sqrt(this)</c>; see <see cref="ShipForceModel.TerminalSpeedMps"/>.
        /// </summary>
        public double ThrustAccelerationMps2 =>
            MassKg > 0.0 ? EngineThrustNewtons / MassKg : 0.0;

        /// <summary>
        /// The top speed this ship can hold on engines alone, m/s. Reported so the
        /// service can log it and a test can assert on it without re-deriving the
        /// drag law.
        /// </summary>
        public double EngineTopSpeedMps =>
            ShipForceModel.TerminalSpeedMps(EngineThrustNewtons, MassKg);

        public override string ToString() =>
            MassKg.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " kg, "
            + EngineThrustNewtons.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " N, "
            + UnfurledSails + " sail(s) -> "
            + EngineTopSpeedMps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m/s";
    }
}
