namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// How many ENet events one main-loop iteration may drain, and how long each
    /// of those polls may block.
    ///
    /// WHY THIS EXISTS. The main loop used to call <c>ENet_Poll</c> exactly once
    /// per iteration, and the native side returns at most ONE event per call
    /// (enetLayer.cpp: one <c>enet_host_service</c>, one event, return). That
    /// makes the server's maximum drain rate equal to its loop rate: the moment
    /// per-packet cost exceeds packet inter-arrival time, the inbound queue grows
    /// without bound while the observed packet rate stays pinned at exactly the
    /// arrival rate - "rate flat, age growing". A live two-player session
    /// degraded precisely this way and ended with ENet timing a peer out after
    /// 73 seconds. A backlog must be able to CLEAR, which requires draining
    /// faster than arrival; hence a bounded inner loop.
    ///
    /// WHY BOUNDED. An unbounded "drain until empty" loop starves everything
    /// else the iteration does (mirror flushes, tree harvest, teleport polling,
    /// the spawn sync pass) whenever a client can produce events faster than we
    /// consume them - the same failure inverted. The budget caps how long the
    /// timers can be held off: worst case one iteration processes
    /// <see cref="DefaultBudget"/> packets before the timers run again.
    ///
    /// WHY THE WAIT DROPS TO ZERO. The FIRST poll of an iteration keeps the
    /// historical 50 ms block so an idle server still sleeps instead of spinning.
    /// Every subsequent poll in the same iteration is an opportunistic "is there
    /// more already queued?" and must not block at all, or a budget of 32 could
    /// stall one iteration for 32 x 50 ms.
    /// </summary>
    public static class PollDrainPolicy
    {
        /// <summary>
        /// Default events drained per iteration. 32 is deliberately modest: at
        /// the observed two-player load (transform + bone streams from each
        /// client every frame, ~120 packets/s total) a backlog drains 32 packets
        /// per ~50 ms iteration (~640/s) - a 5x catch-up margin - while the
        /// timers still run at least every 32 packets.
        /// </summary>
        public const int DefaultBudget = 32;

        /// <summary>The historical blocking wait of the first poll.</summary>
        public const int FirstWaitMs = 50;

        /// <summary>
        /// Upper clamp. A budget beyond this buys no additional catch-up in
        /// practice and turns a typo (say, an accidental "32000") into a loop
        /// that can hold the timers off for seconds under flood.
        /// </summary>
        public const int MaxBudget = 1024;

        /// <summary>
        /// The drain budget for a WAREBORN_DRAIN_BUDGET environment value.
        /// Unset, unparsable or out-of-range values fall back to the default
        /// rather than failing: a perf knob must never stop the server booting.
        /// </summary>
        public static int BudgetFrom(string? env)
        {
            if (!int.TryParse(env, out int parsed))
            {
                return DefaultBudget;
            }

            if (parsed < 1)
            {
                return DefaultBudget;
            }

            return parsed > MaxBudget ? MaxBudget : parsed;
        }

        /// <summary>
        /// How long the poll for event number <paramref name="drainedSoFar"/>
        /// (zero-based) may block, in milliseconds.
        /// </summary>
        public static int WaitMsFor(int drainedSoFar)
        {
            return drainedSoFar == 0 ? FirstWaitMs : 0;
        }
    }
}
