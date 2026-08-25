using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The parsed vector-authority / lift-runtime feature gates, decided in ONE
    /// place so the dependency rules are unit-tested instead of scattered through
    /// glue. All three gates default OFF and opt in via the literal "1":
    ///
    /// <list type="bullet">
    /// <item><c>WAREBORN_FLIGHT_VECTOR_AUTHORITY</c> - master switch; requires
    ///   <c>WAREBORN_FLIGHT_FIXED_STEP=1</c> (there is no honest authority stamp
    ///   without the per-hull fixed clock) and <c>WAREBORN_FLIGHT_FORCES=1</c>
    ///   (the vector model shares the force model's propulsion inputs).</item>
    /// <item><c>WAREBORN_FLIGHT_VECTOR_HULLS</c> - comma-separated PERSISTENT
    ///   INDICES of promoted hulls. Persistent indices are stable across restart;
    ///   runtime entity ids are not and are never accepted here. Empty means no
    ///   hull is promoted even with the master on (the shadow/observer phase).
    ///   Removing an index rolls that hull back to the scalar path; the restart
    ///   that applied the change already advanced its AuthorityGeneration, so
    ///   every stamp from the vector epoch is dead.</item>
    /// <item><c>WAREBORN_FLIGHT_LIFT_RUNTIME</c> - the authentic
    ///   lift/gravity/overload runtime; requires the vector master (lift acts in
    ///   the vector authority path only, and only on promoted hulls).</item>
    /// </list>
    ///
    /// A dependent flag whose prerequisite is OFF stays OFF and contributes one
    /// startup warning - it never half-enables.
    /// </summary>
    public sealed class FlightRuntimeFlags
    {
        private static readonly IReadOnlySet<int> NoHulls = new HashSet<int>();

        private FlightRuntimeFlags(bool vectorAuthorityEnabled, bool liftRuntimeEnabled,
            IReadOnlySet<int> promotedHullPersistentIndices, IReadOnlyList<string> startupWarnings)
        {
            VectorAuthorityEnabled = vectorAuthorityEnabled;
            LiftRuntimeEnabled = liftRuntimeEnabled;
            PromotedHullPersistentIndices = promotedHullPersistentIndices;
            StartupWarnings = startupWarnings;
        }

        /// <summary>Everything off - the shipped default and the OFF-path proof anchor.</summary>
        public static FlightRuntimeFlags Disabled { get; } =
            new FlightRuntimeFlags(false, false, NoHulls, Array.Empty<string>());

        public bool VectorAuthorityEnabled { get; }
        public bool LiftRuntimeEnabled { get; }

        /// <summary>Persistent indices (never runtime entity ids) of promoted hulls.</summary>
        public IReadOnlySet<int> PromotedHullPersistentIndices { get; }

        /// <summary>One line per mis-configuration, for the service to log once at startup.</summary>
        public IReadOnlyList<string> StartupWarnings { get; }

        /// <summary>
        /// Whether THIS hull flies under vector authority. A hull with no
        /// persistent index (not a built ship) can never be promoted.
        /// </summary>
        public bool IsPromoted(int? persistentIndex) =>
            VectorAuthorityEnabled && persistentIndex.HasValue
            && PromotedHullPersistentIndices.Contains(persistentIndex.Value);

        /// <summary>The lift runtime applies per hull, and only where vector authority does.</summary>
        public bool LiftRuntimeAppliesTo(int? persistentIndex) =>
            LiftRuntimeEnabled && IsPromoted(persistentIndex);

        public static FlightRuntimeFlags Parse(string? vectorAuthorityRaw, string? vectorHullsRaw,
            string? liftRuntimeRaw, bool fixedStepEnabled, bool forceModelEnabled)
        {
            var warnings = new List<string>();
            bool masterRequested = vectorAuthorityRaw == "1";
            bool liftRequested = liftRuntimeRaw == "1";
            bool hullsProvided = !string.IsNullOrWhiteSpace(vectorHullsRaw);

            bool master = masterRequested;
            if (masterRequested && !fixedStepEnabled)
            {
                master = false;
                warnings.Add("WAREBORN_FLIGHT_VECTOR_AUTHORITY=1 requires WAREBORN_FLIGHT_FIXED_STEP=1; "
                    + "vector authority stays OFF.");
            }
            if (masterRequested && !forceModelEnabled)
            {
                master = false;
                warnings.Add("WAREBORN_FLIGHT_VECTOR_AUTHORITY=1 requires WAREBORN_FLIGHT_FORCES=1; "
                    + "vector authority stays OFF.");
            }

            var promoted = new HashSet<int>();
            if (hullsProvided && !master)
            {
                warnings.Add("WAREBORN_FLIGHT_VECTOR_HULLS is set but vector authority is OFF; "
                    + "no hull is promoted.");
            }
            else if (hullsProvided)
            {
                foreach (string token in vectorHullsRaw!.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int index) && index >= 0)
                    {
                        promoted.Add(index);
                    }
                    else
                    {
                        warnings.Add("WAREBORN_FLIGHT_VECTOR_HULLS token '" + token
                            + "' is not a non-negative persistent index; token ignored.");
                    }
                }
            }

            bool lift = liftRequested;
            if (liftRequested && !master)
            {
                lift = false;
                warnings.Add("WAREBORN_FLIGHT_LIFT_RUNTIME=1 requires WAREBORN_FLIGHT_VECTOR_AUTHORITY=1 "
                    + "(with its own prerequisites); lift runtime stays OFF.");
            }

            if (!master && !lift && promoted.Count == 0 && warnings.Count == 0)
            {
                return Disabled;
            }
            return new FlightRuntimeFlags(master, lift, promoted, warnings);
        }
    }
}
