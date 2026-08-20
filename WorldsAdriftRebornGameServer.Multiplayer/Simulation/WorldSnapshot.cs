using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// One domain as the shadow model currently sees it.
    ///
    /// The last four properties are DELIBERATELY UNPOPULATED named slots. The World
    /// Inspector direction (handover section 24) asks for contract, fidelity,
    /// authority owner and migration generation; none of those exist in this server
    /// yet, and inventing typed taxonomies for them now is exactly what the vision
    /// doc forbids ("do not freeze this API until real domain implementations expose
    /// what is actually required"). Naming the slots and leaving them null lets the
    /// inspector grow a column without a schema break, while a null keeps saying
    /// "not known" instead of a default pretending to be an answer.
    /// </summary>
    public readonly struct DomainSnapshot
    {
        public DomainSnapshot(
            SimulationDomainId id,
            string kind,
            IReadOnlyList<SimulationEntityId> members,
            int activeInteractionCount,
            double interactionPressure,
            string? descriptor = null,
            string? fidelity = null,
            string? authorityOwner = null,
            long? migrationGeneration = null)
        {
            Id = id;
            Kind = string.IsNullOrWhiteSpace(kind) ? "unknown" : kind.Trim();
            Members = members ?? Array.Empty<SimulationEntityId>();
            ActiveInteractionCount = activeInteractionCount;
            InteractionPressure = interactionPressure;
            Descriptor = descriptor;
            Fidelity = fidelity;
            AuthorityOwner = authorityOwner;
            MigrationGeneration = migrationGeneration;
        }

        public SimulationDomainId Id { get; }
        public string Kind { get; }

        /// <summary>Members, ordered. Ordering is part of the determinism promise.</summary>
        public IReadOnlyList<SimulationEntityId> Members { get; }

        public int MemberCount => Members.Count;

        /// <summary>Cross-domain edges incident to this domain with non-zero pressure.</summary>
        public int ActiveInteractionCount { get; }

        /// <summary>Sum of the pressure of every cross-domain edge incident to this domain.</summary>
        public double InteractionPressure { get; }

        /// <summary>
        /// An opaque, human-readable note about what this domain is. NOT a contract
        /// object and NOT a taxonomy: a string the adapter may or may not set. It
        /// stands in for the "contract" column the inspector will eventually want,
        /// without committing to ConsistencyClass/FidelityClass before real domains
        /// demand them.
        /// </summary>
        public string? Descriptor { get; }

        /// <summary>Unpopulated slot. No fidelity system exists; see the type remarks.</summary>
        public string? Fidelity { get; }

        /// <summary>Unpopulated slot. There is one host and no authority transfer.</summary>
        public string? AuthorityOwner { get; }

        /// <summary>Unpopulated slot. Nothing migrates, so nothing has a generation.</summary>
        public long? MigrationGeneration { get; }
    }

    /// <summary>One observed edge, resolved against domain membership.</summary>
    public readonly struct InteractionSnapshot
    {
        public InteractionSnapshot(
            InteractionEdge edge,
            SimulationDomainId? domainA,
            SimulationDomainId? domainB)
        {
            Edge = edge;
            DomainA = domainA;
            DomainB = domainB;
        }

        public InteractionEdge Edge { get; }
        public SimulationEntityId A => Edge.A;
        public SimulationEntityId B => Edge.B;
        public InteractionKind Kind => Edge.Kind;
        public InteractionStrength Strength => Edge.Strength;
        public InteractionLatencySensitivity LatencySensitivity => Edge.LatencySensitivity;
        public InteractionActivity Activity => Edge.Activity;
        public double Pressure => Edge.Pressure;

        /// <summary>Owning domain of A, or null when A belongs to no domain.</summary>
        public SimulationDomainId? DomainA { get; }
        public SimulationDomainId? DomainB { get; }

        /// <summary>
        /// True when the two endpoints are NOT in the same domain - including when
        /// one of them is in no domain at all.
        ///
        /// <para>
        /// That last case is the one worth being explicit about. A player aboard a
        /// ship is not a member of the ship's domain today: LocalDomainHost does not
        /// own players, and pretending the shadow model already co-located them
        /// would make the containment edge intra-domain and score zero - which would
        /// erase precisely the coupling this model exists to surface. An edge
        /// between two UNASSIGNED entities is not cross-domain: it crosses nothing
        /// we know about.
        /// </para>
        /// </summary>
        public bool IsCrossDomain =>
            (DomainA.HasValue || DomainB.HasValue)
            && !(DomainA.HasValue && DomainB.HasValue && DomainA.Value == DomainB.Value);
    }

    /// <summary>
    /// A deterministic, serialisable description of the world as the shadow model
    /// sees it. Two models fed the same observations in any order produce two
    /// snapshots that are element-for-element equal, which is what makes this
    /// testable and what makes a diff of two snapshots mean something.
    /// </summary>
    public readonly struct WorldSnapshot
    {
        public WorldSnapshot(
            IReadOnlyList<DomainSnapshot> domains,
            IReadOnlyList<InteractionSnapshot> interactions,
            int entityCount)
        {
            Domains = domains ?? Array.Empty<DomainSnapshot>();
            Interactions = interactions ?? Array.Empty<InteractionSnapshot>();
            EntityCount = entityCount;
        }

        /// <summary>Ordered by domain id, ordinal.</summary>
        public IReadOnlyList<DomainSnapshot> Domains { get; }

        /// <summary>Ordered by (A, B, kind).</summary>
        public IReadOnlyList<InteractionSnapshot> Interactions { get; }

        public int DomainCount => Domains.Count;

        /// <summary>Every registered entity, including ones in no domain.</summary>
        public int EntityCount { get; }

        public int InteractionCount => Interactions.Count;

        /// <summary>Interactions carrying non-zero pressure right now.</summary>
        public int ActiveInteractionCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Interactions.Count; i++)
                {
                    if (Interactions[i].Pressure > 0) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Total pressure across every cross-domain edge. The one world-level number
        /// worth watching: it is what "the world is becoming hard to separate" looks
        /// like as a scalar. Still uncalibrated - see InteractionPressure.
        /// </summary>
        public double TotalCrossDomainPressure
        {
            get
            {
                double total = 0;
                for (int i = 0; i < Interactions.Count; i++)
                {
                    if (Interactions[i].IsCrossDomain) total += Interactions[i].Pressure;
                }
                return InteractionPressure.Round(total);
            }
        }
    }
}
