using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// A deterministic 50 Hz accumulator fed by a monotonic process clock. It
    /// exposes a bounded number of whole physics steps and deliberately drops an
    /// excessive backlog instead of allowing a stalled poll loop to enter a
    /// catch-up death spiral.
    /// </summary>
    public sealed class FixedFlightClock
    {
        public const double StepSeconds = 0.02;
        public const int DefaultMaxCatchUpSteps = 25;
        private static readonly TimeSpan Step = TimeSpan.FromSeconds(StepSeconds);

        private readonly int _maxCatchUpSteps;
        private bool _started;
        private TimeSpan _lastObserved;
        private TimeSpan _accumulator;
        private long _simulationStep;
        private long _droppedSteps;
        private long _pressureEvents;

        public FixedFlightClock(int maxCatchUpSteps = DefaultMaxCatchUpSteps)
        {
            if (maxCatchUpSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxCatchUpSteps));
            _maxCatchUpSteps = maxCatchUpSteps;
        }

        public FixedFlightStepBatch Advance(TimeSpan now)
        {
            if (!_started)
            {
                _started = true;
                _lastObserved = now;
                return Snapshot(0, 0);
            }

            TimeSpan elapsed = now - _lastObserved;
            _lastObserved = now;
            if (elapsed < TimeSpan.Zero)
            {
                // A monotonic clock must not move backwards. Treat an injected bad
                // origin as zero elapsed; never rewind authoritative simulation.
                elapsed = TimeSpan.Zero;
            }

            _accumulator += elapsed;
            long available = _accumulator.Ticks / Step.Ticks;
            int steps = (int)Math.Min(available, _maxCatchUpSteps);
            long dropped = Math.Max(0, available - steps);
            if (dropped > 0)
            {
                _pressureEvents++;
                _droppedSteps += dropped;
            }

            // Consume the complete backlog, including deliberately dropped time.
            // The remainder stays below one step and preserves sub-step jitter.
            _accumulator -= TimeSpan.FromTicks(available * Step.Ticks);
            long firstStep = _simulationStep + 1;
            _simulationStep += steps;
            return Snapshot(steps, dropped, firstStep);
        }

        private FixedFlightStepBatch Snapshot(int steps, long dropped, long firstStep = 0) =>
            new FixedFlightStepBatch(steps, firstStep, dropped, _simulationStep,
                _droppedSteps, _pressureEvents, _accumulator.TotalSeconds);
    }

    public readonly struct FixedFlightStepBatch
    {
        public FixedFlightStepBatch(int steps, long firstStep, long droppedSteps,
            long completedSteps, long totalDroppedSteps, long pressureEvents,
            double remainderSeconds)
        {
            Steps = steps;
            FirstStep = firstStep;
            DroppedSteps = droppedSteps;
            CompletedSteps = completedSteps;
            TotalDroppedSteps = totalDroppedSteps;
            PressureEvents = pressureEvents;
            RemainderSeconds = remainderSeconds;
        }

        public int Steps { get; }
        public long FirstStep { get; }
        public long DroppedSteps { get; }
        public long CompletedSteps { get; }
        public long TotalDroppedSteps { get; }
        public long PressureEvents { get; }
        public double RemainderSeconds { get; }
        public bool UnderPressure => DroppedSteps > 0;
    }
}
