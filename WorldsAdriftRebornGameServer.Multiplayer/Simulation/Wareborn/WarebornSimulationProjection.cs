using System;
using System.Collections.Generic;
using System.Globalization;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn
{
    /// <summary>
    /// Turns one pass of observed Wareborn state into a shadow-model world.
    ///
    /// <para>
    /// This is the ADAPTER, and it is deliberately here in the Multiplayer assembly
    /// rather than inline in the game server: the game-server assembly has no test
    /// project, so a rule written there is guarded only by string matching. Every
    /// decision about which edges exist and how strong they are is therefore a pure
    /// function of a plain value, and every one of them has a unit test.
    /// </para>
    ///
    /// <para>
    /// The projection is TOTAL and REBUILDING: it constructs a fresh model each pass
    /// rather than diffing. A shadow model that accumulated would eventually disagree
    /// with the world and there is no reconciliation story for that; rebuilding costs
    /// a few hundred small allocations every five seconds and can never drift.
    /// </para>
    /// </summary>
    public static class WarebornSimulationProjection
    {
        // ---- Domain / entity naming -------------------------------------------------
        // Island and ship domain ids come from the EXISTING SimulationDomainId
        // factories, so a shadow domain id is byte-identical to the ownership domain
        // id the admin panel already shows. Two spellings of "ship:893" would make the
        // two halves of the inspector impossible to join.

        public const string PlayerEntityPrefix = "player:";
        public const string IslandEntityPrefix = "island:";
        public const string ShipEntityPrefix = "ship:";

        /// <summary>
        /// Owned entities that are neither a hull nor an island aggregate: rocks,
        /// props, decks, mounted parts. Prefixed distinctly so a member id can never
        /// collide with a player, hull or island aggregate id.
        /// </summary>
        public const string MemberEntityPrefix = "entity:";

        // ---- Proximity bands --------------------------------------------------------
        // UNCALIBRATED. These are three round numbers, not a measurement. They are
        // deliberately NOT the interest load/unload radii: borrowing those would tie a
        // diagnostic score to a streaming rule, and the first tuning of the streaming
        // rule would then silently rewrite history in the inspector.
        public const double StrongProximityMetres = 150.0;
        public const double ModerateProximityMetres = 400.0;
        public const double WeakProximityMetres = 1000.0;

        public static SimulationEntityId PlayerEntity(long playerEntityId) =>
            new SimulationEntityId(PlayerEntityPrefix + playerEntityId.ToString(CultureInfo.InvariantCulture));

        public static SimulationEntityId ShipEntity(long hullEntityId) =>
            new SimulationEntityId(ShipEntityPrefix + hullEntityId.ToString(CultureInfo.InvariantCulture));

        public static SimulationEntityId IslandEntity(string islandId) =>
            new SimulationEntityId(IslandEntityPrefix + islandId);

        public static SimulationEntityId MemberEntity(long entityId) =>
            new SimulationEntityId(MemberEntityPrefix + entityId.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// The strength/latency table, in one place so it can be read as a policy
        /// rather than hunted through the builder. Nothing here is measured; see
        /// <see cref="InteractionPressure"/>.
        /// </summary>
        public static InteractionEdge Containment(
            SimulationEntityId player, SimulationEntityId ship, bool shipMoving) =>
            new InteractionEdge(
                player, ship, InteractionKind.Containment,
                // Very strong: the player's position is literally a function of the
                // hull's. Latency high but not very high - a carried body tolerates
                // more than an input loop does.
                InteractionStrength.VeryStrong,
                InteractionLatencySensitivity.High,
                shipMoving ? InteractionActivity.Active : InteractionActivity.Intermittent);

        public static InteractionEdge Control(SimulationEntityId pilot, SimulationEntityId ship) =>
            new InteractionEdge(
                pilot, ship, InteractionKind.Control,
                // The textbook co-location case: an input loop across a machine
                // boundary is the thing this whole model exists to notice.
                InteractionStrength.VeryStrong,
                InteractionLatencySensitivity.VeryHigh,
                InteractionActivity.Active);

        public static InteractionEdge Interest(SimulationEntityId player, SimulationEntityId island) =>
            new InteractionEdge(
                player, island, InteractionKind.Interest,
                // Aggregate, at island-domain level, exactly as section 8 asks - never
                // one edge per resource node. Streaming is latency-tolerant, so this
                // deliberately scores near the floor.
                InteractionStrength.Weak,
                InteractionLatencySensitivity.Low,
                InteractionActivity.Intermittent);

        /// <summary>
        /// The ship-near-island edge, or null when the ship is out past the widest
        /// band. Null rather than a zero-strength edge: "not near anything" is not an
        /// observation worth a row.
        /// </summary>
        public static InteractionEdge? Proximity(
            SimulationEntityId ship, SimulationEntityId island, double distanceMetres, bool shipMoving)
        {
            if (double.IsNaN(distanceMetres) || distanceMetres < 0) return null;
            InteractionStrength strength;
            if (distanceMetres <= StrongProximityMetres) strength = InteractionStrength.Strong;
            else if (distanceMetres <= ModerateProximityMetres) strength = InteractionStrength.Moderate;
            else if (distanceMetres <= WeakProximityMetres) strength = InteractionStrength.Weak;
            else return null;

            return new InteractionEdge(
                ship, island, InteractionKind.Proximity,
                strength,
                // A static island is not something you round-trip to.
                InteractionLatencySensitivity.Low,
                // A parked ship next to an island is coupled but not doing anything;
                // a moving one is the causal-prefetch case section 20 cares about.
                shipMoving ? InteractionActivity.Active : InteractionActivity.Idle);
        }

        /// <summary>
        /// Builds the whole world. Order-independent by construction: everything is
        /// keyed and the snapshot sorts.
        /// </summary>
        public static SimulationWorldModel Project(WarebornWorldObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var model = new SimulationWorldModel();

            var islandDomains = new Dictionary<string, SimulationDomainId>(StringComparer.Ordinal);
            var islandEntities = new Dictionary<string, SimulationEntityId>(StringComparer.Ordinal);

            foreach (ObservedIsland island in observation.Islands)
            {
                SimulationDomainId domainId = SimulationDomainId.ForIsland(new IslandId(island.IslandId));
                if (islandDomains.ContainsKey(island.IslandId)) continue;

                model.RegisterDomain(domainId, "island", "static island state, resident");
                islandDomains.Add(island.IslandId, domainId);

                // The island's own aggregate entity. Section 8 wants interest expressed
                // against the island DOMAIN, and an edge needs an entity endpoint, so
                // the domain gets a stand-in member that represents it.
                SimulationEntityId aggregate = IslandEntity(island.IslandId);
                model.RegisterEntity(aggregate, domainId);
                islandEntities.Add(island.IslandId, aggregate);

                foreach (long owned in island.OwnedEntityIds)
                {
                    if (owned <= 0) continue;
                    model.RegisterEntity(MemberEntity(owned), domainId);
                }
            }

            var shipEntities = new Dictionary<long, SimulationEntityId>();

            foreach (ObservedShip ship in observation.Ships)
            {
                SimulationDomainId domainId = SimulationDomainId.ForShip(ship.HullEntityId);
                if (model.HasDomain(domainId)) continue;

                model.RegisterDomain(domainId, "ship", ship.Moving ? "live hull, under way" : "live hull, at rest");
                SimulationEntityId hull = ShipEntity(ship.HullEntityId);
                model.RegisterEntity(hull, domainId);
                shipEntities.Add(ship.HullEntityId, hull);

                foreach (long member in ship.MemberEntityIds)
                {
                    if (member <= 0 || member == ship.HullEntityId) continue;
                    model.RegisterEntity(MemberEntity(member), domainId);
                }
            }

            // Players are entities in NO domain. That is the honest state of this
            // server: LocalDomainHost does not own players, and a shadow model that
            // pre-emptively filed a player under a ship would be asserting the very
            // placement decision it is only supposed to describe the need for.
            foreach (ObservedPlayer player in observation.Players)
            {
                SimulationEntityId entity = PlayerEntity(player.PlayerEntityId);
                model.RegisterEntity(entity);

                foreach (string islandId in player.InterestedIslandIds)
                {
                    if (string.IsNullOrWhiteSpace(islandId)) continue;
                    if (!islandEntities.TryGetValue(islandId.Trim(), out SimulationEntityId island)) continue;
                    model.UpsertInteraction(Interest(entity, island));
                }
            }

            foreach (ObservedShip ship in observation.Ships)
            {
                if (!shipEntities.TryGetValue(ship.HullEntityId, out SimulationEntityId hull)) continue;

                foreach (long aboard in ship.AboardPlayerEntityIds)
                {
                    if (aboard <= 0) continue;
                    SimulationEntityId player = PlayerEntity(aboard);
                    // A peer can be aboard a hull a beat after it left the player
                    // registry. Skip rather than register the ghost: this model may
                    // only report what another system already believes.
                    if (!model.HasEntity(player)) continue;
                    model.UpsertInteraction(Containment(player, hull, ship.Moving));
                }

                if (ship.PilotPlayerEntityId is long pilotId && pilotId > 0)
                {
                    SimulationEntityId pilot = PlayerEntity(pilotId);
                    if (model.HasEntity(pilot)) model.UpsertInteraction(Control(pilot, hull));
                }

                if (ship.NearestIslandId != null
                    && islandEntities.TryGetValue(ship.NearestIslandId, out SimulationEntityId nearIsland))
                {
                    InteractionEdge? proximity =
                        Proximity(hull, nearIsland, ship.NearestIslandDistanceMetres, ship.Moving);
                    if (proximity.HasValue) model.UpsertInteraction(proximity.Value);
                }
            }

            return model;
        }
    }
}
