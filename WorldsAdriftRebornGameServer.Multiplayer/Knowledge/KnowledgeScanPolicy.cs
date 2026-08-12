namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>What a single scan of an entity did to a player's knowledge.</summary>
    public enum ScanGrantOutcome
    {
        /// <summary>The target is not a scannable databank; no response is owed.</summary>
        NotScannable,

        /// <summary>The player already scanned this entity; no points, RepeatedScanResponse.</summary>
        Repeated,

        /// <summary>First scan of this databank; points granted, KnowledgeGainScanResponse.</summary>
        Granted,
    }

    /// <summary>The pure result of evaluating one scan; the handler applies it.</summary>
    public readonly struct ScanGrant
    {
        public ScanGrant(ScanGrantOutcome outcome, int newKnowledge, int newLifetimeKnowledge, long knowledgeGained)
        {
            Outcome = outcome;
            NewKnowledge = newKnowledge;
            NewLifetimeKnowledge = newLifetimeKnowledge;
            KnowledgeGained = knowledgeGained;
        }

        public ScanGrantOutcome Outcome { get; }
        public int NewKnowledge { get; }
        public int NewLifetimeKnowledge { get; }
        public long KnowledgeGained { get; }
    }

    /// <summary>
    /// GAIN half of the KNOWLEDGE loop: a player scans a databank and earns a chunk
    /// of knowledge, once per databank. Pure - the 1331 dedup ledger and the 1332
    /// counters are passed in as plain values and the handler writes the result back.
    ///
    /// Databanks are the big-chunk source (a material node would be a trickle); the
    /// per-databank dedup is what stops a player farming one bank forever. A repeated
    /// scan is not an error - it is the RepeatedScanResponse the client expects, with
    /// no change to any counter.
    /// </summary>
    public static class KnowledgeScanPolicy
    {
        public static ScanGrant Evaluate(
            bool targetIsScannableDatabank,
            bool alreadyScanned,
            int knowledge,
            int lifetimeKnowledge,
            long grantAmount)
        {
            if (!targetIsScannableDatabank)
            {
                return new ScanGrant(ScanGrantOutcome.NotScannable, knowledge, lifetimeKnowledge, 0);
            }

            if (alreadyScanned)
            {
                return new ScanGrant(ScanGrantOutcome.Repeated, knowledge, lifetimeKnowledge, 0);
            }

            // A first scan credits both the spendable pool AND the lifetime total
            // (lifetime is a monotonic tally the client uses for threshold rewards;
            // it must never be spent down, so it moves in lockstep on a GAIN only).
            long gain = grantAmount < 0 ? 0 : grantAmount;
            return new ScanGrant(
                ScanGrantOutcome.Granted,
                knowledge + (int)gain,
                lifetimeKnowledge + (int)gain,
                gain);
        }
    }
}
