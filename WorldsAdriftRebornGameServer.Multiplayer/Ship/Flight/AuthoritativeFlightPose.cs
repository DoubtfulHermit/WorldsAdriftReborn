using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The one committed pose/velocity of a hull for one stamped frame. Produced only by
    /// the hull's authority adapter after the step commits; collision, docking, publication,
    /// persistence and telemetry consume ONLY this — never FlightState, WorldEntities
    /// seeds, emit specs, or their own integration.
    /// </summary>
    public readonly record struct AuthoritativeFlightPose(
        FlightAuthorityStamp Stamp,
        double X, double Y, double Z,
        double QW, double QX, double QY, double QZ,
        double VxMps, double VyMps, double VzMps,
        double AngVxRadPerSec, double AngVyRadPerSec, double AngVzRadPerSec)
    {
        private const double UnitQuaternionTolerance = 1e-6;

        public bool IsFinite =>
            double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) &&
            double.IsFinite(QW) && double.IsFinite(QX) && double.IsFinite(QY) && double.IsFinite(QZ) &&
            double.IsFinite(VxMps) && double.IsFinite(VyMps) && double.IsFinite(VzMps) &&
            double.IsFinite(AngVxRadPerSec) && double.IsFinite(AngVyRadPerSec) && double.IsFinite(AngVzRadPerSec) &&
            Math.Abs((QW * QW + QX * QX + QY * QY + QZ * QZ) - 1.0) <= UnitQuaternionTolerance;

        public bool IsValid => Stamp.IsValid && IsFinite;
    }
}
