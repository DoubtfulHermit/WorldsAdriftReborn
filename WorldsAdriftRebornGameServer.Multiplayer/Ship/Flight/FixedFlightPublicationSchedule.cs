using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Splits a fixed-clock batch at exact network-publication boundaries. A
    /// 240 ms ship point represents exactly twelve 20 ms simulation steps,
    /// regardless of main-loop jitter.
    /// </summary>
    public static class FixedFlightPublicationSchedule
    {
        public const int StepsPerPublication = 12;

        public static IReadOnlyList<FixedFlightPublicationSlice> Slice(
            FixedFlightStepBatch batch)
        {
            if (batch.Steps < 0 || batch.Steps > FixedFlightClock.DefaultMaxCatchUpSteps)
                throw new ArgumentOutOfRangeException(nameof(batch));
            if (batch.Steps == 0)
                return Array.Empty<FixedFlightPublicationSlice>();
            if (batch.FirstStep <= 0
                || batch.CompletedSteps != batch.FirstStep + batch.Steps - 1)
                throw new ArgumentException("The fixed-step batch is not contiguous.", nameof(batch));

            var slices = new List<FixedFlightPublicationSlice>(3);
            long nextStep = batch.FirstStep;
            int remaining = batch.Steps;
            while (remaining > 0)
            {
                int positionInWindow = (int)((nextStep - 1) % StepsPerPublication);
                int throughBoundary = StepsPerPublication - positionInWindow;
                int count = Math.Min(remaining, throughBoundary);
                bool publishAfter = count == throughBoundary;
                slices.Add(new FixedFlightPublicationSlice(
                    count, nextStep, publishAfter));
                nextStep += count;
                remaining -= count;
            }
            return slices;
        }
    }

    public readonly struct FixedFlightPublicationSlice
    {
        public FixedFlightPublicationSlice(int steps, long firstStep,
            bool publishAfter)
        {
            Steps = steps;
            FirstStep = firstStep;
            PublishAfter = publishAfter;
        }

        public int Steps { get; }
        public long FirstStep { get; }
        public bool PublishAfter { get; }
    }
}
