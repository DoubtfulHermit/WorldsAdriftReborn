namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The two client-authoritative movement streams this server ingests. They
    /// are judged separately because they disagree about what a sample IS:
    /// 190602 carries a position and no usable clock, 1073 carries the sender's
    /// private timestamp accumulator (starting at ~0.0, advanced by its own
    /// frame time - not NTP, not wall clock, shared with nobody).
    /// </summary>
    public enum MovementStream
    {
        /// <summary>190602 TransformState: world/parent-local position.</summary>
        Transform,

        /// <summary>1073 ClientAuthoritativePlayerState: relative position, bones, and the sender's timestamp.</summary>
        PlayerState,
    }

    /// <summary>What the ingest decided about one inbound movement sample.</summary>
    public enum IngestVerdict
    {
        /// <summary>Newest state for this player. Keep it.</summary>
        Accept,

        /// <summary>
        /// Accepted, but the baseline was re-anchored rather than extended: the
        /// player legitimately IS somewhere the previous samples said they could
        /// not be (teleport confirmed by a second consistent sample, a sender
        /// whose timestamp accumulator restarted, or a change of coordinate
        /// space). Movement fields are as trustworthy as a plain Accept; only
        /// the continuity assumptions were reset.
        /// </summary>
        AcceptReanchor,

        /// <summary>
        /// The sender's timestamp went backwards. The stream is defined by a
        /// monotonic accumulator, so an older stamp is a reordered or stale
        /// packet - relaying it would hand the interpolator a value it will
        /// discard at best and rewind on at worst.
        /// </summary>
        DropTimestampRegression,

        /// <summary>
        /// Identical timestamp AND identical position to the last accepted
        /// sample, carrying nothing else. The receiver's DiscardOutdatedValues
        /// collapses it anyway; relaying it spends wire for nothing.
        /// </summary>
        DropDuplicate,

        /// <summary>
        /// The position implies impossible travel (see
        /// <see cref="MovementIngestPolicy.MaxSpeedMetresPerSecond"/>). One such
        /// sample is presumed garbage; a SECOND sample consistent with the first
        /// re-anchors instead (see <see cref="IngestVerdict.AcceptReanchor"/>),
        /// which is how a genuine teleport gets through one sample late.
        /// </summary>
        DropAbsurdJump,
    }

    /// <summary>
    /// Per-peer ingest counters for one stats window, plus the staleness
    /// accumulator. Returned by <see cref="MovementIngest.SnapshotAndReset"/>.
    /// </summary>
    public readonly struct IngestWindowStats
    {
        public IngestWindowStats(long accepted, long regressions, long duplicates, long jumps, double stalenessSeconds)
        {
            Accepted = accepted;
            TimestampRegressions = regressions;
            Duplicates = duplicates;
            AbsurdJumps = jumps;
            StalenessSeconds = stalenessSeconds;
        }

        /// <summary>Samples kept (Accept + AcceptReanchor), both streams.</summary>
        public long Accepted { get; }

        public long TimestampRegressions { get; }
        public long Duplicates { get; }
        public long AbsurdJumps { get; }

        /// <summary>
        /// Σ(wall_delta − sender_timestamp_delta) over consecutive accepted 1073
        /// samples in this window. THE DISCRIMINATOR: a sender whose simulation
        /// clock loses time against wall clock grows this steadily whatever the
        /// server does, while a server-side backlog grows it only while the
        /// backlog grows and pays it back when the backlog drains. Flat ≈ 0 means
        /// both are healthy. (Measured at arrival on the server, so it contains
        /// network jitter; the SIGNAL is its trend over windows, not one value.)
        /// </summary>
        public double StalenessSeconds { get; }

        public bool IsEmpty => Accepted == 0 && TimestampRegressions == 0 && Duplicates == 0 && AbsurdJumps == 0;
    }

    /// <summary>
    /// One inbound movement sample, reduced to what the ingest judges. Pure
    /// numbers so the policy needs no game types.
    /// </summary>
    public readonly struct MovementSample
    {
        public MovementSample(bool hasTimestamp, float timestamp, bool hasPosition, double x, double y, double z, bool carriesSpaceChange)
        {
            HasTimestamp = hasTimestamp;
            Timestamp = timestamp;
            HasPosition = hasPosition;
            X = x;
            Y = y;
            Z = z;
            CarriesSpaceChange = carriesSpaceChange;
        }

        /// <summary>Whether the update carried the sender's 1073 timestamp.</summary>
        public bool HasTimestamp { get; }
        public float Timestamp { get; }

        /// <summary>Whether the update carried a position at all.</summary>
        public bool HasPosition { get; }

        /// <summary>Position in metres, in whatever space the stream uses.</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        /// <summary>
        /// Whether the update changed the space its positions are measured in -
        /// a 190602 parent change, or a 1073 relativeTo/isRelativeToShip change.
        /// Distances across a space change are meaningless, so the jump check
        /// must not judge them; the baseline re-anchors instead.
        /// </summary>
        public bool CarriesSpaceChange { get; }
    }

    /// <summary>
    /// WHAT counts as an impossible movement sample, and nothing else. Stateless;
    /// <see cref="MovementIngest"/> applies it per peer.
    ///
    /// WHY INGEST AT ALL: the relay used to forward both movement streams raw,
    /// one-for-one, in arrival order. Nothing bounded staleness, nothing dropped
    /// a reordered packet, and the receiving client's interpolator - which keys
    /// on the SENDER's private timestamp - dutifully rendered whatever arrived.
    /// Latest-state ingest is the first half of the fix (the cadence emitter is
    /// the second): keep only the newest believable state per player, and let
    /// everything older or impossible die here instead of on everyone's wire.
    /// </summary>
    public static class MovementIngestPolicy
    {
        /// <summary>
        /// Fastest sustained travel a player is believed capable of. Generous on
        /// purpose: gliding is the fastest thing in the game and eyeballs at
        /// around 30-40 m/s; 60 leaves headroom for a tailwind, a future ship
        /// deck, and being wrong, because the cost of a false POSITIVE here is a
        /// frozen avatar and the cost of a false negative is one garbage sample
        /// relayed.
        /// </summary>
        public const double MaxSpeedMetresPerSecond = 60.0;

        /// <summary>
        /// Flat allowance added on top of the speed budget, so that two samples
        /// arriving nearly simultaneously (wall delta ~0) do not fail the check
        /// on ordinary jitter. 10 m is far above any per-packet movement at real
        /// send rates (60 m/s at 50 ms is 3 m) and far below anything absurd.
        /// </summary>
        public const double JumpSlackMetres = 10.0;

        /// <summary>
        /// A rejected jump or timestamp regression is confirmed - and the
        /// baseline re-anchored - by this many CONSECUTIVE samples that agree
        /// with each other. Two: the first is dropped on suspicion, the second
        /// either corroborates it (teleport, sender restart) or extends the old
        /// baseline (the garbage was the one sample it looked like).
        /// </summary>
        public const int SamplesToReanchor = 2;

        /// <summary>How far a player may plausibly have moved in this much wall time.</summary>
        public static double AllowedTravelMetres(double wallDeltaSeconds)
        {
            double dt = wallDeltaSeconds > 0.0 ? wallDeltaSeconds : 0.0;
            return MaxSpeedMetresPerSecond * dt + JumpSlackMetres;
        }

        /// <summary>Straight-line distance between two samples' positions.</summary>
        public static double DistanceMetres(double ax, double ay, double az, double bx, double by, double bz)
        {
            double dx = ax - bx;
            double dy = ay - by;
            double dz = az - bz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>
    /// Latest-state tracking for every player's two movement streams: applies
    /// <see cref="MovementIngestPolicy"/>, remembers the accepted baseline, and
    /// accumulates the staleness discriminator. Pure and clock-injected, in the
    /// FallWatch mold: every rule is asserted in tests without a packet.
    /// </summary>
    public sealed class MovementIngest
    {
        private sealed class StreamState
        {
            public bool HasBaseline;
            public double BaseX, BaseY, BaseZ;
            public bool HasBaselineTimestamp;
            public float BaselineTimestamp;
            public TimeSpan BaselineWall;

            // The unconfirmed other-world: where the rejected sample(s) said the
            // player is. A follow-up consistent with it re-anchors.
            public bool HasCandidate;
            public double CandX, CandY, CandZ;
            public TimeSpan CandidateWall;

            // Consecutive timestamp regressions, for detecting a sender whose
            // accumulator restarted (it would otherwise be muted forever).
            public int ConsecutiveRegressions;

            // Window counters.
            public long Accepted;
            public long Regressions;
            public long Duplicates;
            public long Jumps;
            public double StalenessSeconds;
        }

        private sealed class PeerState
        {
            public readonly StreamState Transform = new();
            public readonly StreamState PlayerState = new();
        }

        private readonly Dictionary<ulong, PeerState> _peers = new();
        private readonly IClock _clock;

        public MovementIngest(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Judges one inbound sample and, if it is kept, makes it the stream's
        /// new baseline. Called from the typed component handlers - the packet
        /// has already been deserialized once by the game's own code, and this
        /// must never be a second deserialization pass.
        /// </summary>
        public IngestVerdict Observe(ulong peerId, MovementStream stream, in MovementSample sample)
        {
            if (!_peers.TryGetValue(peerId, out PeerState? peer))
            {
                peer = new PeerState();
                _peers[peerId] = peer;
            }

            StreamState s = stream == MovementStream.Transform ? peer.Transform : peer.PlayerState;
            TimeSpan now = _clock.Elapsed;

            // Nothing judgeable: an edge-only or bone-only update. Accepted, but
            // it does not move the baseline - there is nothing to move it TO.
            if (!sample.HasPosition && !sample.HasTimestamp)
            {
                s.Accepted++;
                return IngestVerdict.Accept;
            }

            // A space change makes every continuity rule meaningless: the
            // numbers are now measured from a different origin, and the
            // timestamp is the one thing that survives it. Re-anchor.
            if (sample.CarriesSpaceChange)
            {
                AcceptInto(s, sample, now, accumulateStaleness: false);
                return IngestVerdict.AcceptReanchor;
            }

            // ---- Timestamp rules (1073 only carries one). ----
            if (sample.HasTimestamp && s.HasBaselineTimestamp)
            {
                if (sample.Timestamp < s.BaselineTimestamp)
                {
                    s.ConsecutiveRegressions++;
                    if (s.ConsecutiveRegressions >= MovementIngestPolicy.SamplesToReanchor)
                    {
                        // Not a straggler - the sender's accumulator is simply
                        // running at smaller values now (a restart). Believe it,
                        // or this player is muted for the rest of the session.
                        AcceptInto(s, sample, now, accumulateStaleness: false);
                        return IngestVerdict.AcceptReanchor;
                    }
                    s.Regressions++;
                    return IngestVerdict.DropTimestampRegression;
                }

                if (sample.Timestamp == s.BaselineTimestamp
                    && sample.HasPosition && s.HasBaseline
                    && sample.X == s.BaseX && sample.Y == s.BaseY && sample.Z == s.BaseZ)
                {
                    s.ConsecutiveRegressions = 0;
                    s.Duplicates++;
                    return IngestVerdict.DropDuplicate;
                }
            }
            else if (!sample.HasTimestamp && sample.HasPosition && s.HasBaseline
                && sample.X == s.BaseX && sample.Y == s.BaseY && sample.Z == s.BaseZ)
            {
                // 190602 has no timestamp; a byte-identical position is the
                // whole sample, so it is the duplicate case.
                s.Duplicates++;
                return IngestVerdict.DropDuplicate;
            }

            s.ConsecutiveRegressions = 0;

            // ---- The jump rule. ----
            if (sample.HasPosition && s.HasBaseline)
            {
                double wallDelta = (now - s.BaselineWall).TotalSeconds;
                double travelled = MovementIngestPolicy.DistanceMetres(
                    sample.X, sample.Y, sample.Z, s.BaseX, s.BaseY, s.BaseZ);

                if (travelled > MovementIngestPolicy.AllowedTravelMetres(wallDelta))
                {
                    if (s.HasCandidate)
                    {
                        double sinceCandidate = (now - s.CandidateWall).TotalSeconds;
                        double fromCandidate = MovementIngestPolicy.DistanceMetres(
                            sample.X, sample.Y, sample.Z, s.CandX, s.CandY, s.CandZ);

                        if (fromCandidate <= MovementIngestPolicy.AllowedTravelMetres(sinceCandidate))
                        {
                            // Two samples agree the player is over there. That is
                            // a teleport (this server sends those) or a genuine
                            // relocation, not garbage. Believe it, one sample
                            // late.
                            AcceptInto(s, sample, now, accumulateStaleness: false);
                            return IngestVerdict.AcceptReanchor;
                        }
                    }

                    s.HasCandidate = true;
                    s.CandX = sample.X;
                    s.CandY = sample.Y;
                    s.CandZ = sample.Z;
                    s.CandidateWall = now;
                    s.Jumps++;
                    return IngestVerdict.DropAbsurdJump;
                }
            }

            AcceptInto(s, sample, now, accumulateStaleness: true);
            return IngestVerdict.Accept;
        }

        private static void AcceptInto(StreamState s, in MovementSample sample, TimeSpan now, bool accumulateStaleness)
        {
            if (accumulateStaleness && sample.HasTimestamp && s.HasBaselineTimestamp)
            {
                double wallDelta = (now - s.BaselineWall).TotalSeconds;
                double senderDelta = sample.Timestamp - s.BaselineTimestamp;
                s.StalenessSeconds += wallDelta - senderDelta;
            }

            if (sample.HasPosition)
            {
                s.HasBaseline = true;
                s.BaseX = sample.X;
                s.BaseY = sample.Y;
                s.BaseZ = sample.Z;
            }

            if (sample.HasTimestamp)
            {
                s.HasBaselineTimestamp = true;
                s.BaselineTimestamp = sample.Timestamp;
            }

            // The wall baseline moves on every accepted judgeable sample, so
            // staleness deltas are between CONSECUTIVE accepted samples.
            s.BaselineWall = now;
            s.HasCandidate = false;
            s.ConsecutiveRegressions = 0;
            s.Accepted++;
        }

        /// <summary>
        /// This peer's counters for the window just ended, both streams summed
        /// (staleness is 1073-only by construction: only 1073 carries the
        /// timestamp). Resets the window; baselines are untouched.
        /// </summary>
        public IngestWindowStats SnapshotAndReset(ulong peerId)
        {
            if (!_peers.TryGetValue(peerId, out PeerState? peer))
            {
                return default;
            }

            IngestWindowStats stats = new(
                peer.Transform.Accepted + peer.PlayerState.Accepted,
                peer.Transform.Regressions + peer.PlayerState.Regressions,
                peer.Transform.Duplicates + peer.PlayerState.Duplicates,
                peer.Transform.Jumps + peer.PlayerState.Jumps,
                peer.PlayerState.StalenessSeconds);

            ResetWindow(peer.Transform);
            ResetWindow(peer.PlayerState);
            return stats;
        }

        private static void ResetWindow(StreamState s)
        {
            s.Accepted = 0;
            s.Regressions = 0;
            s.Duplicates = 0;
            s.Jumps = 0;
            s.StalenessSeconds = 0.0;
        }

        /// <summary>Peers with any state, for the stats loop.</summary>
        public IReadOnlyList<ulong> KnownPeers()
        {
            List<ulong> result = new(_peers.Count);
            foreach (ulong peer in _peers.Keys)
            {
                result.Add(peer);
            }
            return result;
        }

        /// <summary>Drops a peer's state. Part of ForgetPeer's everything-contract.</summary>
        public void Forget(ulong peerId)
        {
            _peers.Remove(peerId);
        }
    }
}
