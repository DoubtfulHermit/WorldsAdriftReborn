namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    public enum RuntimeWorkKind
    {
        OwnershipMembership,
        AvatarRelay,
        Interest,
        Physics,
        Snapshot,
        Gateway,
        Recovery,
    }

    public readonly record struct RuntimeWorkSample(
        RuntimeWorkKind Kind,
        long WorkUnits,
        long ElapsedMicroseconds,
        long BudgetMicroseconds,
        bool Replayed = false)
    {
        public bool OverBudget => ElapsedMicroseconds > BudgetMicroseconds;
    }

    public readonly record struct RuntimeWorkSummary(
        int SampleCount, int OverBudgetCount, long TotalWorkUnits,
        long TotalElapsedMicroseconds, long MaxElapsedMicroseconds);

    /// <summary>
    /// Fixed-memory instrumentation primitive for future live wiring. Samples are
    /// caller-measured; this type neither reads wall clock nor changes scheduling.
    /// </summary>
    public sealed class BoundedRuntimeTelemetry
    {
        public const int MaxCapacity = 16_384;
        private readonly RuntimeWorkSample[] _samples;
        private int _next;
        private int _count;

        public BoundedRuntimeTelemetry(int capacity = 1024)
        {
            if (capacity <= 0 || capacity > MaxCapacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new RuntimeWorkSample[capacity];
        }

        public int Count => _count;
        public int Capacity => _samples.Length;

        public void Record(RuntimeWorkSample sample)
        {
            if (sample.WorkUnits < 0) throw new ArgumentOutOfRangeException(nameof(sample));
            if (sample.ElapsedMicroseconds < 0 || sample.BudgetMicroseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(sample));
            _samples[_next] = sample;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public IReadOnlyList<RuntimeWorkSample> Snapshot()
        {
            var ordered = new RuntimeWorkSample[_count];
            int start = (_next - _count + _samples.Length) % _samples.Length;
            for (int i = 0; i < _count; i++)
                ordered[i] = _samples[(start + i) % _samples.Length];
            return ordered;
        }

        public RuntimeWorkSummary Summarize(RuntimeWorkKind kind)
        {
            int samples = 0;
            int overBudget = 0;
            long units = 0;
            long elapsed = 0;
            long maximum = 0;
            foreach (RuntimeWorkSample sample in Snapshot())
            {
                if (sample.Kind != kind) continue;
                samples++;
                if (sample.OverBudget) overBudget++;
                units = SaturatingAdd(units, sample.WorkUnits);
                elapsed = SaturatingAdd(elapsed, sample.ElapsedMicroseconds);
                maximum = Math.Max(maximum, sample.ElapsedMicroseconds);
            }
            return new RuntimeWorkSummary(samples, overBudget, units, elapsed, maximum);
        }

        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    public readonly record struct RuntimeScaleEstimate(
        int ActiveShips,
        long PhysicsStepsPerSecond,
        long MembershipComparisonsPerChangedShipBeforeIndex,
        long MembershipComparisonsPerChangedShipAfterIndex,
        long WorstCaseAvatarRelayPairs,
        long BoundedAvatarRelayPairs,
        long SnapshotBytes);

    /// <summary>
    /// Deterministic operation-count baseline. These are not latency claims: they
    /// make complexity and caps reviewable before a repeatable load harness exists.
    /// </summary>
    public static class RuntimeScaleBaseline
    {
        public const int SimulationHz = 50;
        public const int DefaultMembersPerShip = 32;
        public const int DefaultPlayersPerShip = 2;
        public const int DefaultRelayNeighbourCap = 32;
        public const int DefaultSnapshotBytesPerShip = 4096;

        public static RuntimeScaleEstimate Estimate(int activeShips,
            int membersPerShip = DefaultMembersPerShip,
            int playersPerShip = DefaultPlayersPerShip,
            int relayNeighbourCap = DefaultRelayNeighbourCap,
            int snapshotBytesPerShip = DefaultSnapshotBytesPerShip)
        {
            if (activeShips < 0 || membersPerShip < 1 || playersPerShip < 0
                || relayNeighbourCap < 0 || snapshotBytesPerShip < 0)
                throw new ArgumentOutOfRangeException();

            long ships = activeShips;
            long players = SaturatingMultiply(ships, playersPerShip);
            long worldMembers = SaturatingMultiply(ships, membersPerShip);
            long fullRelay = SaturatingMultiply(players, Math.Max(0, players - 1));
            long boundedRelay = SaturatingMultiply(players,
                Math.Min(Math.Max(0, players - 1), relayNeighbourCap));
            return new RuntimeScaleEstimate(
                activeShips,
                SaturatingMultiply(ships, SimulationHz),
                worldMembers,
                membersPerShip,
                fullRelay,
                boundedRelay,
                SaturatingMultiply(ships, snapshotBytesPerShip));
        }

        private static long SaturatingMultiply(long left, long right)
        {
            if (left == 0 || right == 0) return 0;
            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }
    }
}
