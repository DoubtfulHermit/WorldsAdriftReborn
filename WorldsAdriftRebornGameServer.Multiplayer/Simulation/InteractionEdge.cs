using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// Why two entities are causally coupled. The kinds here are the ones this
    /// server can actually OBSERVE today; nothing is declared before there is a
    /// live source for it.
    /// </summary>
    public enum InteractionKind
    {
        /// <summary>One entity is physically carried by the other (a player aboard a hull).</summary>
        Containment = 0,

        /// <summary>One entity is driving the other's authoritative state (a player at the helm).</summary>
        Control = 1,

        /// <summary>One entity needs to OBSERVE the other's state (resource/domain checkout).</summary>
        Interest = 2,

        /// <summary>Two entities are close enough that they may soon interact (ship near island).</summary>
        Proximity = 3,

        /// <summary>
        /// An environmental field acting on an entity - a wind wall, a storm.
        /// Declared but NEVER produced by this adapter: it is the seam the wind-wall
        /// work fills in (handover section 23). Leaving the enum member here rather
        /// than adding it later means that work adds an observation, not a schema.
        /// </summary>
        Environment = 4,
    }

    /// <summary>
    /// How hard two entities are to separate, on a coarse ordinal scale.
    ///
    /// Deliberately an enum and NOT a free <c>double</c>. The handover sketched
    /// <c>double Strength</c>, but the vision doc forbids freezing a numeric API
    /// before real domains prove what the numbers mean, and a free double invites
    /// callers to invent precision nobody measured. Four named steps can be argued
    /// about honestly; 0.73 cannot.
    /// </summary>
    public enum InteractionStrength
    {
        Weak = 0,
        Moderate = 1,
        Strong = 2,
        VeryStrong = 3,
    }

    /// <summary>How much a round trip between these two entities would hurt.</summary>
    public enum InteractionLatencySensitivity
    {
        Low = 0,
        Moderate = 1,
        High = 2,
        VeryHigh = 3,
    }

    /// <summary>
    /// Whether the coupling is doing anything right now. This is the only part of
    /// the pressure formula that comes from live observation rather than from a
    /// fixed table, so it is the part that makes a snapshot move.
    /// </summary>
    public enum InteractionActivity
    {
        /// <summary>Observed, but quiescent. Contributes zero pressure.</summary>
        Idle = 0,

        /// <summary>Coupled and occasionally exchanging state.</summary>
        Intermittent = 1,

        /// <summary>Coupled and continuously exchanging state.</summary>
        Active = 2,
    }

    /// <summary>
    /// One observed causal relationship. Undirected: the model normalises the pair
    /// so that (A,B) and (B,A) with the same kind are the same edge, which is what
    /// makes upsert idempotent.
    ///
    /// This is an OBSERVATION. Nothing in this type or its consumers migrates
    /// authority, partitions a graph or schedules anything.
    /// </summary>
    public readonly struct InteractionEdge : IEquatable<InteractionEdge>
    {
        public InteractionEdge(
            SimulationEntityId a,
            SimulationEntityId b,
            InteractionKind kind,
            InteractionStrength strength,
            InteractionLatencySensitivity latencySensitivity,
            InteractionActivity activity)
        {
            if (string.IsNullOrEmpty(a.Value)) throw new ArgumentException("endpoint A is required", nameof(a));
            if (string.IsNullOrEmpty(b.Value)) throw new ArgumentException("endpoint B is required", nameof(b));
            if (a == b) throw new ArgumentException("an entity cannot interact with itself", nameof(b));

            // Normalise so the pair has one canonical order. Without this, the same
            // real relationship observed from the player's side and from the ship's
            // side would be two edges and would count twice in pressure.
            if (a.CompareTo(b) <= 0) { A = a; B = b; }
            else { A = b; B = a; }

            Kind = kind;
            Strength = strength;
            LatencySensitivity = latencySensitivity;
            Activity = activity;
        }

        public SimulationEntityId A { get; }
        public SimulationEntityId B { get; }
        public InteractionKind Kind { get; }
        public InteractionStrength Strength { get; }
        public InteractionLatencySensitivity LatencySensitivity { get; }
        public InteractionActivity Activity { get; }

        /// <summary>The identity an upsert replaces: the pair plus the reason.</summary>
        public InteractionEdgeKey Key => new InteractionEdgeKey(A, B, Kind);

        /// <summary>
        /// This edge's diagnostic pressure. See <see cref="InteractionPressure"/>
        /// for why these numbers are not calibrated.
        /// </summary>
        public double Pressure => InteractionPressure.For(this);

        public bool Equals(InteractionEdge other) =>
            A == other.A && B == other.B && Kind == other.Kind
            && Strength == other.Strength
            && LatencySensitivity == other.LatencySensitivity
            && Activity == other.Activity;

        public override bool Equals(object? obj) => obj is InteractionEdge other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + A.GetHashCode();
                hash = (hash * 31) + B.GetHashCode();
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) + (int)Strength;
                hash = (hash * 31) + (int)LatencySensitivity;
                hash = (hash * 31) + (int)Activity;
                return hash;
            }
        }

        public override string ToString() => A.Value + "<->" + B.Value + " " + Kind;
    }

    /// <summary>The pair-plus-kind identity of an edge, independent of its weights.</summary>
    public readonly struct InteractionEdgeKey
        : IEquatable<InteractionEdgeKey>, IComparable<InteractionEdgeKey>
    {
        public InteractionEdgeKey(SimulationEntityId a, SimulationEntityId b, InteractionKind kind)
        {
            if (a.CompareTo(b) <= 0) { A = a; B = b; }
            else { A = b; B = a; }
            Kind = kind;
        }

        public SimulationEntityId A { get; }
        public SimulationEntityId B { get; }
        public InteractionKind Kind { get; }

        public bool Equals(InteractionEdgeKey other) => A == other.A && B == other.B && Kind == other.Kind;
        public override bool Equals(object? obj) => obj is InteractionEdgeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + A.GetHashCode();
                hash = (hash * 31) + B.GetHashCode();
                hash = (hash * 31) + (int)Kind;
                return hash;
            }
        }

        public int CompareTo(InteractionEdgeKey other)
        {
            int byA = A.CompareTo(other.A);
            if (byA != 0) return byA;
            int byB = B.CompareTo(other.B);
            if (byB != 0) return byB;
            return ((int)Kind).CompareTo((int)other.Kind);
        }

        public override string ToString() => A.Value + "<->" + B.Value + " " + Kind;
    }
}
