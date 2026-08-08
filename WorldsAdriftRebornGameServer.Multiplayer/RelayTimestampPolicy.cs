namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// THE ONE PURE FUNCTION that decides what timestamp an emitted 1073 update
    /// carries. Swappable by design: if the scheme is ever wrong, the fix is
    /// this file and its tests, not the emitter.
    ///
    /// WHY REWRITE AT ALL. The client renders a remote player by pairing each
    /// arriving 190602 position with the LATEST 1073 timestamp it holds - two
    /// different unreliable streams, glued by arrival order. The sender's
    /// timestamp is a private accumulator starting at ~0.0 and advanced by its
    /// own frame time: no shared timebase exists, and the receiver's only
    /// coupling to it is a 10%-per-call catch-up plus a hard snap on overshoot.
    /// Once the server coalesces and re-times the stream, the sender's stamps
    /// stop describing anything the receiver experiences - so the server issues
    /// its own.
    ///
    /// THE SCHEME: a SYNTHETIC PER-RECIPIENT, PER-SENDER TIMELINE. Not server
    /// uptime, not wall clock. The receiver anchors on the first value it ever
    /// sees, so the epoch is free - and a small synthetic epoch is strictly
    /// safer than a big one: global float seconds lose sub-tick precision as
    /// uptime grows, and a seed/reconnect on a mismatched scale is a guaranteed
    /// snap. The timeline starts at <see cref="SeedTimestampSeconds"/> (the
    /// component seed's stamp) and advances by exactly one emit interval per
    /// emitted sample. The server's Stopwatch schedules WHEN emits happen; it is
    /// never the timestamp epoch.
    ///
    /// Every emitted sample gets a fresh stamp even when the position has not
    /// changed: a constant-position, advancing-stamp stream is how an avatar
    /// freezes CLEANLY during a source hitch. Going silent instead drains the
    /// receiver's 5-slot queue and invites the snap on resume; repeating a stamp
    /// gets the sample collapsed by DiscardOutdatedValues, starving the queue
    /// the slow way.
    /// </summary>
    public static class RelayTimestampPolicy
    {
        /// <summary>
        /// The timeline's origin, and therefore the stamp the 1073 SEED carries:
        /// 2x the receiver's hardcoded 0.1 s interpolation delay, so the first
        /// live samples land ahead of the receiver's playback clock instead of
        /// behind it. (The old seed said 100 while live senders published ~0.0x -
        /// a guaranteed pathological snap the first time any player saw another.)
        /// </summary>
        public const float SeedTimestampSeconds = 0.2f;

        /// <summary>
        /// The stamp for one emitted sample. Index 0 is the seed itself; the
        /// first live emit is index 1. Computed in double and narrowed once, so
        /// the tests can assert the float result is strictly increasing for
        /// every index a real session can reach.
        /// </summary>
        public static float StampFor(long sampleIndex, double stepSeconds)
        {
            return (float)(SeedTimestampSeconds + sampleIndex * stepSeconds);
        }
    }

    /// <summary>
    /// One recipient's view of one sender's 1073 stream: the sample counter, the
    /// last stamp issued, and the self-check. An INCARNATION begins when the
    /// recipient is served the component seed; re-serving the seed resets the
    /// timeline with it, so stream and seed can never disagree about the epoch.
    /// </summary>
    public sealed class SyntheticTimeline
    {
        private long _nextIndex = 1;
        private float _last = RelayTimestampPolicy.SeedTimestampSeconds;

        /// <summary>
        /// How many times the policy produced a stamp that did not increase.
        /// THE self-check: the receiver pairs "latest 1073 stamp" with each
        /// arriving 190602 position, so a non-increasing stamp means two
        /// positions under one effective timestamp - the exact failure the
        /// rewrite exists to remove. Anything nonzero here means the scheme is
        /// wrong; the tests assert it stays zero across a long session.
        /// </summary>
        public long BadPairs { get; private set; }

        /// <summary>Stamps issued this incarnation (excluding the seed).</summary>
        public long IssuedCount => _nextIndex - 1;

        /// <summary>
        /// The stamp for the next emitted sample. GUARANTEED strictly increasing
        /// even if the policy misbehaves - the guard counts the fault in
        /// <see cref="BadPairs"/> and forces the next representable float,
        /// because emitting a broken stamp to a live client is worse than
        /// emitting a slightly-off one.
        /// </summary>
        public float Next(double stepSeconds)
        {
            float stamp = RelayTimestampPolicy.StampFor(_nextIndex++, stepSeconds);
            if (stamp <= _last)
            {
                BadPairs++;
                stamp = MathF.BitIncrement(_last);
            }
            _last = stamp;
            return stamp;
        }

        /// <summary>
        /// The recipient was (re-)served the 1073 seed for this sender: a new
        /// incarnation. The counter restarts so the first live emit is one step
        /// past the seed's stamp. BadPairs is deliberately NOT reset - it is a
        /// lifetime fault counter, and resetting it on reconnect would hide
        /// exactly the faults reconnects cause.
        /// </summary>
        public void ResetIncarnation()
        {
            _nextIndex = 1;
            _last = RelayTimestampPolicy.SeedTimestampSeconds;
        }
    }
}
