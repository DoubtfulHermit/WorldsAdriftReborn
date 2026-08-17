using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>Exact resumable state of the pure flight state machine.</summary>
    public sealed class FlightSessionSnapshot
    {
        public FlightSessionSnapshot(FlightState state, FlightControlInput input, bool manned,
            int restEmitted, long lastStampMs, bool everEmitted)
        {
            if (restEmitted < 0) throw new ArgumentOutOfRangeException(nameof(restEmitted));
            State = state;
            Input = input;
            Manned = manned;
            RestEmitted = restEmitted;
            LastStampMs = lastStampMs;
            EverEmitted = everEmitted;
        }

        public FlightState State { get; }
        public FlightControlInput Input { get; }
        public bool Manned { get; }
        public int RestEmitted { get; }
        public long LastStampMs { get; }
        public bool EverEmitted { get; }
    }
}
