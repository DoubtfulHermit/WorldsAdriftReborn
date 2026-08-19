using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>
    /// Decides when a fuel gauge is worth a 1105 broadcast. This is the RATE half of
    /// the standing multiplayer-safety rule: a per-ship level that ticks
    /// continuously is precisely the new networked state that has reintroduced the
    /// congestion spiral before, and the fix is to push on a QUANTUM and a FLOOR,
    /// not every tick.
    ///
    /// Both gates come from the client, not from taste. <c>FuelGaugeVisualizer</c>
    /// puts two smoothing stages in front of the needle - a
    /// <c>DelayedInterpolator</c> with <c>Delay = 2.0</c> seconds, then
    /// <c>Mathf.Lerp(current, target, 2f * Time.deltaTime)</c> with a 0.01 snap
    /// epsilon - so a sub-unit change or a second push inside one second is work the
    /// player cannot possibly see.
    ///
    /// Pure: the caller injects elapsed seconds. NOT THREAD-SAFE.
    /// </summary>
    public sealed class FuelGaugePushTracker
    {
        private readonly struct Pushed
        {
            public Pushed(double level, double atSeconds)
            {
                Level = level;
                AtSeconds = atSeconds;
            }

            public double Level { get; }
            public double AtSeconds { get; }
        }

        private readonly Dictionary<long, Pushed> _byGauge = new Dictionary<long, Pushed>();

        /// <summary>
        /// Whether <paramref name="gaugeEntityId"/> should be sent
        /// <paramref name="level"/> now, and records the decision when it is yes.
        ///
        /// A gauge never pushed before ALWAYS pushes: that first value is what the
        /// needle animates away from, and suppressing it would leave a gauge that
        /// seeded at one number and silently disagreed with the tank forever.
        ///
        /// A move to EXACT zero or EXACT full always pushes regardless of the
        /// quantum. Those are the two readings a player acts on, and rounding one of
        /// them away by less than a unit is the difference between "empty" and
        /// "nearly empty" on a 270-degree needle.
        /// </summary>
        public bool ShouldPush(long gaugeEntityId, double level, double capacity, double nowSeconds)
        {
            if (double.IsNaN(level) || double.IsInfinity(level))
            {
                return false;
            }

            if (!_byGauge.TryGetValue(gaugeEntityId, out Pushed last))
            {
                _byGauge[gaugeEntityId] = new Pushed(level, nowSeconds);
                return true;
            }

            if (level == last.Level)
            {
                return false;
            }

            bool endpoint = level <= 0.0 || (capacity > 0.0 && level >= capacity);
            double moved = System.Math.Abs(level - last.Level);
            if (!endpoint && moved < ShipFuelPolicy.GaugePushQuantum)
            {
                return false;
            }

            if (nowSeconds - last.AtSeconds < ShipFuelPolicy.GaugePushMinIntervalSeconds)
            {
                return false;
            }

            _byGauge[gaugeEntityId] = new Pushed(level, nowSeconds);
            return true;
        }

        /// <summary>
        /// Records a push the caller made WITHOUT asking - a refuel or a run-dry,
        /// both of which must reach the needle immediately. Recording it means the
        /// rate floor still applies to whatever comes next, so "important" can never
        /// become "unbudgeted".
        /// </summary>
        public void Record(long gaugeEntityId, double level, double nowSeconds)
        {
            if (double.IsNaN(level) || double.IsInfinity(level))
            {
                return;
            }
            _byGauge[gaugeEntityId] = new Pushed(level, nowSeconds);
        }

        /// <summary>Forgets a gauge - it was salvaged, lifted off, or its ship is gone.</summary>
        public bool Forget(long gaugeEntityId) => _byGauge.Remove(gaugeEntityId);

        /// <summary>How many gauges are being tracked. For logs and tests.</summary>
        public int Count => _byGauge.Count;
    }
}
