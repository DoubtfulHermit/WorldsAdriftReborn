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
    /// <item><c>WAREBORN_FLIGHT_COLLISION_OBSERVE</c> - the in-tick collision
    ///   shadow; requires <c>WAREBORN_FLIGHT_FIXED_STEP=1</c> (there is no
    ///   honest stamp without the fixed clock).</item>
    /// <item><c>WAREBORN_FLIGHT_COLLISION_RESPONSE</c> - velocity-only collision
    ///   response; requires observe.</item>
    /// <item><c>WAREBORN_FLIGHT_DOCKING_TXN</c> - transactional authentic
    ///   docking; suppresses the legacy capture writers for runtime-managed
    ///   hulls; requires observe (clearance evidence).</item>
    /// </list>
    ///
    /// A dependent flag whose prerequisite is OFF stays OFF and contributes one
    /// startup warning - it never half-enables.
    ///
    /// LOAD-BEARING LIFETIME GUARANTEE - do not hot-reload these flags. The
    /// service holds the parsed result in a <c>static readonly</c> field
    /// (<c>ShipFlightService.RuntimeFlags</c>), so flipping a hull between the
    /// scalar and vector paths REQUIRES a process restart - and the restart is
    /// exactly what advances every hull's <c>AuthorityGeneration</c>
    /// (<c>ShipDomain.RestoreAfterProcessRestart</c>). Stamp monotonicity across
    /// a scalar/vector flip depends on this: the new path's first
    /// <c>FlightAuthorityStamp</c> is minted under a strictly newer generation,
    /// so no consumer can ever accept old-path evidence as fresher. Making this
    /// type reloadable at runtime (a mutable field, a re-Parse on SIGHUP, an
    /// admin toggle) would let two authority models mint stamps under ONE
    /// generation - that change must trip a review, not slip in.
    /// This instance is immutable by construction (all properties get-only,
    /// pinned by <c>Mode_flips_require_a_restart_because_the_parsed_flags_are_immutable</c>).
    /// </summary>
    public sealed class FlightRuntimeFlags
    {
        private static readonly IReadOnlySet<int> NoHulls = new HashSet<int>();

        private FlightRuntimeFlags(bool vectorAuthorityEnabled, bool liftRuntimeEnabled,
            IReadOnlySet<int> promotedHullPersistentIndices, IReadOnlyList<string> startupWarnings,
            bool collisionObserveEnabled, bool collisionResponseEnabled, bool dockingTxnEnabled)
        {
            VectorAuthorityEnabled = vectorAuthorityEnabled;
            LiftRuntimeEnabled = liftRuntimeEnabled;
            PromotedHullPersistentIndices = promotedHullPersistentIndices;
            StartupWarnings = startupWarnings;
            CollisionObserveEnabled = collisionObserveEnabled;
            CollisionResponseEnabled = collisionResponseEnabled;
            DockingTxnEnabled = dockingTxnEnabled;
        }

        /// <summary>Everything off - the shipped default and the OFF-path proof anchor.</summary>
        public static FlightRuntimeFlags Disabled { get; } =
            new FlightRuntimeFlags(false, false, NoHulls, Array.Empty<string>(),
                collisionObserveEnabled: false, collisionResponseEnabled: false,
                dockingTxnEnabled: false);

        public bool VectorAuthorityEnabled { get; }
        public bool LiftRuntimeEnabled { get; }

        /// <summary>
        /// HARD PREREQUISITE MARKER for collision response, encoded as code so it
        /// cannot be forgotten: today collision observes COMMITTED slice-end state
        /// (one evaluation per publication slice, ~4.2 Hz, roughly 8% trajectory
        /// coverage), not proposed motion per accepted 20 ms step as contract
        /// section 6 requires for an honest response. Until the per-step
        /// proposed-motion path exists (terrain/other-hull proxies fed through
        /// IntegratedFlightShadow's Terrain/OtherHulls parameters in the vector
        /// path), a requested response logs a startup warning and every response
        /// remains observe-graded - the geometry gate additionally rejects
        /// ConservativeEnvelope subjects, so nothing can mutate velocity. Flip
        /// this to true ONLY when the per-step path lands, together with its
        /// integration tests.
        /// </summary>
        public const bool PerStepCollisionPathExists = false;

        /// <summary>The Steps 4-5 in-tick collision shadow gate.</summary>
        public bool CollisionObserveEnabled { get; }

        /// <summary>Velocity-only collision response; never on without observe.</summary>
        public bool CollisionResponseEnabled { get; }

        /// <summary>Transactional docking; never on without observe (clearance evidence).</summary>
        public bool DockingTxnEnabled { get; }

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
            string? liftRuntimeRaw, bool fixedStepEnabled, bool forceModelEnabled,
            string? collisionObserveRaw = null, string? collisionResponseRaw = null,
            string? dockingTxnRaw = null)
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

            bool observeRequested = collisionObserveRaw == "1";
            bool responseRequested = collisionResponseRaw == "1";
            bool dockingRequested = dockingTxnRaw == "1";

            bool observe = observeRequested && fixedStepEnabled;
            if (observeRequested && !observe)
            {
                warnings.Add("WAREBORN_FLIGHT_COLLISION_OBSERVE=1 requires "
                    + "WAREBORN_FLIGHT_FIXED_STEP=1; collision observation stays OFF.");
            }

            bool response = responseRequested && observe;
            if (responseRequested && !response)
            {
                warnings.Add("WAREBORN_FLIGHT_COLLISION_RESPONSE=1 requires "
                    + "WAREBORN_FLIGHT_COLLISION_OBSERVE=1 (with the fixed step); "
                    + "collision response stays OFF.");
            }
            if (response && !PerStepCollisionPathExists)
            {
                warnings.Add("WAREBORN_FLIGHT_COLLISION_RESPONSE=1 but collision still "
                    + "evaluates committed slice-end state (~4.2 Hz), not proposed motion "
                    + "per accepted step; per-step evaluation is a HARD PREREQUISITE for "
                    + "an honest response - contacts remain observe-only until it exists.");
            }

            bool dockingTxn = dockingRequested && observe;
            if (dockingRequested && !dockingTxn)
            {
                warnings.Add("WAREBORN_FLIGHT_DOCKING_TXN=1 requires "
                    + "WAREBORN_FLIGHT_COLLISION_OBSERVE=1 (with the fixed step); "
                    + "transactional docking stays OFF.");
            }

            if (!master && !lift && !observe && !response && !dockingTxn
                && promoted.Count == 0 && warnings.Count == 0)
            {
                return Disabled;
            }
            return new FlightRuntimeFlags(master, lift, promoted, warnings,
                observe, response, dockingTxn);
        }
    }
}
