using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>One shadow domain as the World Inspector sees it.</summary>
    public readonly struct SimulationDomainStat
    {
        public SimulationDomainStat(
            string domainId,
            string kind,
            int memberCount,
            int activeInteractionCount,
            double pressure,
            string? descriptor,
            string? fidelity,
            string? authorityOwner,
            long? migrationGeneration)
        {
            DomainId = domainId ?? "";
            Kind = kind ?? "unknown";
            MemberCount = memberCount < 0 ? 0 : memberCount;
            ActiveInteractionCount = activeInteractionCount < 0 ? 0 : activeInteractionCount;
            Pressure = pressure;
            Descriptor = descriptor;
            Fidelity = fidelity;
            AuthorityOwner = authorityOwner;
            MigrationGeneration = migrationGeneration;
        }

        public string DomainId { get; }
        public string Kind { get; }
        public int MemberCount { get; }
        public int ActiveInteractionCount { get; }
        public double Pressure { get; }

        // The four inspector slots section 24 asks for. Three of them are null
        // today and are carried anyway: a column that is explicitly "not known"
        // is a different statement from a column that does not exist, and the
        // panel should be able to say which.
        public string? Descriptor { get; }
        public string? Fidelity { get; }
        public string? AuthorityOwner { get; }
        public long? MigrationGeneration { get; }
    }

    /// <summary>One observed interaction edge, flattened for the wire.</summary>
    public readonly struct SimulationInteractionStat
    {
        public SimulationInteractionStat(
            string a, string b, string kind, string strength, string latencySensitivity,
            string activity, double pressure, string? domainA, string? domainB, bool crossDomain)
        {
            A = a ?? "";
            B = b ?? "";
            Kind = kind ?? "unknown";
            Strength = strength ?? "unknown";
            LatencySensitivity = latencySensitivity ?? "unknown";
            Activity = activity ?? "unknown";
            Pressure = pressure;
            DomainA = domainA;
            DomainB = domainB;
            CrossDomain = crossDomain;
        }

        public string A { get; }
        public string B { get; }
        public string Kind { get; }
        public string Strength { get; }
        public string LatencySensitivity { get; }
        public string Activity { get; }
        public double Pressure { get; }
        public string? DomainA { get; }
        public string? DomainB { get; }
        public bool CrossDomain { get; }
    }

    /// <summary>
    /// The simulation shadow model as a stats-file section (schema v14+).
    ///
    /// <para>
    /// Three states, all distinguishable, for the same reason every other section
    /// here insists on it: <c>Present=false</c> means an older game server that had
    /// no shadow model at all; <c>Present=true, Enabled=false</c> means
    /// WAREBORN_SIMULATION_MODEL is off on a server that has one; and
    /// <c>Enabled=true</c> with <c>RefreshCount=0</c> means on but not yet warm. A
    /// panel that collapsed those would tell an operator "no coupling in your world"
    /// when the truth is "nobody looked".
    /// </para>
    /// </summary>
    public readonly struct SimulationRuntimeStat
    {
        /// <summary>A server that reports no shadow model at all.</summary>
        public static SimulationRuntimeStat Off => default;

        /// <summary>
        /// Row caps. The domain cap matches the admin table's own 250-row cap; the
        /// edge cap is smaller because edges are per-player-per-thing and a busy
        /// world produces far more of them than anyone reads. Both keep a 3-second
        /// file from growing without bound - the totals above them stay exact.
        /// </summary>
        public const int MaxDomainRows = 250;
        public const int MaxInteractionRows = 64;

        public SimulationRuntimeStat(
            bool enabled,
            int refreshCount,
            double refreshIntervalSeconds,
            string? error,
            WorldSnapshot? snapshot)
        {
            Present = true;
            Enabled = enabled;
            RefreshCount = refreshCount < 0 ? 0 : refreshCount;
            RefreshIntervalSeconds = refreshIntervalSeconds;
            Error = string.IsNullOrWhiteSpace(error) ? null : error!.Trim();
            HasSnapshot = snapshot.HasValue;

            if (!snapshot.HasValue)
            {
                DomainCount = 0;
                EntityCount = 0;
                InteractionCount = 0;
                ActiveInteractionCount = 0;
                TotalCrossDomainPressure = 0;
                _domains = Array.Empty<SimulationDomainStat>();
                _interactions = Array.Empty<SimulationInteractionStat>();
                return;
            }

            WorldSnapshot world = snapshot.Value;
            DomainCount = world.DomainCount;
            EntityCount = world.EntityCount;
            InteractionCount = world.InteractionCount;
            ActiveInteractionCount = world.ActiveInteractionCount;
            TotalCrossDomainPressure = world.TotalCrossDomainPressure;

            // Heaviest first, then by id: the cap should drop the boring rows, and
            // the id tie-break keeps two equal-pressure worlds emitting the same file.
            _domains = world.Domains
                .OrderByDescending(d => d.InteractionPressure)
                .ThenBy(d => d.Id)
                .Take(MaxDomainRows)
                .Select(d => new SimulationDomainStat(
                    d.Id.Value, d.Kind, d.MemberCount, d.ActiveInteractionCount,
                    d.InteractionPressure, d.Descriptor, d.Fidelity, d.AuthorityOwner,
                    d.MigrationGeneration))
                .ToArray();

            _interactions = world.Interactions
                .OrderByDescending(i => i.Pressure)
                .ThenBy(i => i.Edge.Key)
                .Take(MaxInteractionRows)
                .Select(i => new SimulationInteractionStat(
                    i.A.Value, i.B.Value, i.Kind.ToString(), i.Strength.ToString(),
                    i.LatencySensitivity.ToString(), i.Activity.ToString(), i.Pressure,
                    i.DomainA?.Value, i.DomainB?.Value, i.IsCrossDomain))
                .ToArray();
        }

        private readonly IReadOnlyList<SimulationDomainStat>? _domains;
        private readonly IReadOnlyList<SimulationInteractionStat>? _interactions;

        /// <summary>False on a default value: the game server predates the shadow model.</summary>
        public bool Present { get; }

        /// <summary>Whether WAREBORN_SIMULATION_MODEL armed the observer this boot.</summary>
        public bool Enabled { get; }

        /// <summary>True once the observer has built a world at least once.</summary>
        public bool HasSnapshot { get; }

        public int RefreshCount { get; }
        public double RefreshIntervalSeconds { get; }

        /// <summary>The observer's first fault, if it ever faulted. It never rethrows.</summary>
        public string? Error { get; }

        public int DomainCount { get; }
        public int EntityCount { get; }
        public int InteractionCount { get; }
        public int ActiveInteractionCount { get; }

        /// <summary>UNCALIBRATED - see InteractionPressure. Sortable, not meaningful.</summary>
        public double TotalCrossDomainPressure { get; }

        public IReadOnlyList<SimulationDomainStat> Domains => _domains ?? Array.Empty<SimulationDomainStat>();
        public IReadOnlyList<SimulationInteractionStat> Interactions =>
            _interactions ?? Array.Empty<SimulationInteractionStat>();
    }
}
