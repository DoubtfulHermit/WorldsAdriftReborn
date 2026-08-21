namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One 1254 update, ready to go on the wire: exactly the three fields this
    /// server is allowed to write, and no way to express the fourth.
    ///
    /// ⚠ THERE IS DELIBERATELY NO <c>IsLightningActive</c> ON THIS TYPE, and that
    /// is the island-drop hazard's defence in this assembly rather than a comment
    /// asking people to be careful. See <see cref="IslandStormPush"/>.
    /// </summary>
    public readonly struct IslandStormUpdate : IEquatable<IslandStormUpdate>
    {
        public IslandStormUpdate(IslandStormPhase phase, int millisTillNextLightning,
            int millisTillLightningEnd, long generation)
        {
            Phase = phase;
            MillisTillNextLightning = millisTillNextLightning;
            MillisTillLightningEnd = millisTillLightningEnd;
            Generation = generation;
        }

        public IslandStormPhase Phase { get; }

        /// <summary>1254 <c>estimatedMilliTillNextLightning</c>.</summary>
        public int MillisTillNextLightning { get; }

        /// <summary>1254 <c>estimatedMilliTillLightningEnd</c> - the storm switch.</summary>
        public int MillisTillLightningEnd { get; }

        /// <summary>1254 <c>generation</c>.</summary>
        public long Generation { get; }

        public static IslandStormUpdate From(IslandStormSample sample) =>
            new IslandStormUpdate(sample.Phase, sample.MillisTillNextLightning,
                sample.MillisTillLightningEnd, sample.Generation);

        public bool Equals(IslandStormUpdate other) =>
            Phase == other.Phase
            && MillisTillNextLightning == other.MillisTillNextLightning
            && MillisTillLightningEnd == other.MillisTillLightningEnd
            && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is IslandStormUpdate other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Phase, MillisTillNextLightning,
            MillisTillLightningEnd, Generation);

        public override string ToString() =>
            "1254 gen=" + Generation + " next=" + MillisTillNextLightning
            + "ms end=" + MillisTillLightningEnd + "ms (" + Phase + ")";
    }

    /// <summary>
    /// WHETHER to put a 1254 update on the wire this turn, given what this island
    /// was last told. Pure, and separate from <see cref="IslandStormPolicy"/> on
    /// purpose: the policy knows what TIME it is, this knows what the CLIENT
    /// believes, and the two questions fail in different ways.
    ///
    /// THE RATE, and why it is not a relayed component. An island gets an update
    /// only when something a player can perceive changes:
    ///
    ///   * once when the server first observes it (correcting the seeded 50 s to
    ///     the real countdown);
    ///   * once when it enters the client's 30 s warning window;
    ///   * about three times inside that window, to step the warning's ramp;
    ///   * once when the storm starts;
    ///   * once when it ends.
    ///
    /// That is roughly six updates per island per cadence - six per island per 105
    /// minutes at the default. Nothing here is per-frame and nothing here is
    /// relayed.
    ///
    /// ⚠ WHY THE REFRESHES EXIST AT ALL, which is the least obvious thing in this
    /// feature. The client's countdown DOES NOT TICK DOWN BY ITSELF: its smoother
    /// computes a decayed value and throws it away without storing it, so
    /// <c>EstimatedTimeUntilLightningStarts</c> only ever changes when a server
    /// update arrives whose value differs from the held one by more than seven
    /// seconds. See <see cref="IslandStormPolicy.ClientWarpThresholdSeconds"/> for
    /// the proof. Two consequences are baked into <see cref="Next"/>:
    ///
    ///   1. Without refreshes there is NO warning - the seeded 50 s would sit at 50
    ///      until the storm simply began.
    ///   2. A refresh that moves the countdown by seven seconds or less is worse
    ///      than no refresh: it costs a packet and the client discards it. So this
    ///      class refuses to emit one, and a test pins that.
    ///
    /// ⚠ AND THE ONE RULE THAT MATTERS MORE THAN THE RATE.
    /// <c>isLightningActive = true</c> is never sent, because
    /// <c>IslandLocalTransformBehaviour.HandleLightningActiveUpdated(true)</c>
    /// teleports the island toward Y −250..−1500 m (PROVED,
    /// <c>acs/Bossa.Travellers.Visualisers.Islands/IslandLocalTransformBehaviour.cs:46-52</c>).
    /// The bool buys NOTHING: the visualiser that actually draws the storm switches
    /// on <c>EstimatedMilliTillLightningEnd &gt; 0</c>, an INT (PROVED, <c>:226</c>).
    /// So the field is not on <see cref="IslandStormUpdate"/> at all, the wire that
    /// sends it does not call <c>SetIsLightningActive</c>, and a source-reading test
    /// goes red if either of those stops being true.
    ///
    /// THREE ABSENCES currently defuse that hazard, and it is worth knowing that
    /// none of them is ours to rely on:
    ///   1. our 1042 seed leaves <c>originalPosition</c>,
    ///      <c>endOfWorldDurationMultiplier</c> and <c>endOfWorldOutroOffset</c>
    ///      empty, so <c>GetEndOfWorldPosition()</c> returns early;
    ///   2. we never grant a client authority over an island's 190602, so the
    ///      behaviour's <c>TransformStateWriter</c> [Require] cannot resolve;
    ///   3. NEW, and the strongest: <c>IslandLocalTransformBehaviour</c> is baked
    ///      onto ZERO of the 255 shipped island bundles (PROVED 2026-08-20 by a
    ///      UnityPy MonoScript sweep; they carry <c>StaticGlobalTransformBehaviour</c>
    ///      instead). The drop code is not on the prefab.
    /// Three absences are still three absences. The rule costs nothing; keep it.
    /// </summary>
    public static class IslandStormPush
    {
        /// <summary>
        /// The next update to send for one island, or null to send nothing.
        ///
        /// <paramref name="lastSent"/> is what this island was last told, or null if
        /// it has never been told anything. <paramref name="sinceLastSend"/> is how
        /// long ago that was.
        /// </summary>
        public static IslandStormUpdate? Next(IslandStormUpdate? lastSent, IslandStormSample sample,
            TimeSpan sinceLastSend, TimeSpan refreshInterval)
        {
            IslandStormUpdate candidate = IslandStormUpdate.From(sample);

            // Never told: say something, so the client stops believing the seeded
            // 50 s. Without this the very first storm would arrive with no warning.
            if (lastSent == null) return candidate;

            IslandStormUpdate last = lastSent.Value;

            // A phase edge is the whole point of the component. Quiet->Telegraph
            // starts the rumble, Telegraph->Active starts the bolts, Active->Quiet
            // stops them - and that last one is the only thing that ENDS a storm.
            if (last.Phase != candidate.Phase) return candidate;

            // A new cycle, even without a phase edge (a server that was asleep
            // through a whole storm still owes the client a correct countdown).
            if (last.Generation != candidate.Generation) return candidate;

            // Steady state inside a storm: the switch is already true and the client
            // strikes on its own timer. Nothing to say until it ends.
            if (candidate.Phase != IslandStormPhase.Telegraph) return null;

            if (sinceLastSend < refreshInterval) return null;

            // THE WARP GATE. A countdown step the client would discard is not worth
            // a packet - and, far worse, it would read as "the countdown is being
            // pushed" while the warning stayed frozen.
            double movedSeconds =
                Math.Abs(last.MillisTillNextLightning - candidate.MillisTillNextLightning) / 1000.0;
            if (movedSeconds <= IslandStormPolicy.ClientWarpThresholdSeconds) return null;

            return candidate;
        }
    }
}
