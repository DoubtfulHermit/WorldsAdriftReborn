using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// The shadow model's own behaviour: identity, membership, idempotence and the
    /// determinism promise. Nothing here touches a ship, an island or a packet -
    /// that is the point of the layer being separable.
    /// </summary>
    public class SimulationWorldModelTests
    {
        private static readonly SimulationDomainId Haven =
            SimulationDomainId.ForIsland(new IslandId("haven"));
        private static readonly SimulationDomainId Ship = SimulationDomainId.ForShip(893);

        private static SimulationEntityId E(string value) => new SimulationEntityId(value);

        [Fact]
        public void Entity_ids_compare_and_hash_deterministically()
        {
            Assert.Equal(E("ship:893"), E("ship:893"));
            Assert.Equal(E("ship:893").GetHashCode(), E(" ship:893 ").GetHashCode());
            Assert.NotEqual(E("ship:893"), E("ship:894"));
            Assert.True(E("island:haven").CompareTo(E("ship:893")) < 0);
            Assert.Equal("ship:893", E("ship:893").ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void An_empty_entity_id_is_refused(string? value) =>
            Assert.Throws<ArgumentException>(() => new SimulationEntityId(value!));

        [Fact]
        public void Duplicate_domain_registration_is_rejected()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Haven, "island");
            Assert.Throws<ArgumentException>(() => model.RegisterDomain(Haven, "island"));
            Assert.Equal(1, model.DomainCount);
        }

        [Fact]
        public void Entity_membership_is_deterministic_regardless_of_insertion_order()
        {
            var forward = new SimulationWorldModel();
            forward.RegisterDomain(Haven, "island");
            foreach (string id in new[] { "entity:1", "entity:2", "entity:3" })
                forward.RegisterEntity(E(id), Haven);

            var backward = new SimulationWorldModel();
            backward.RegisterDomain(Haven, "island");
            foreach (string id in new[] { "entity:3", "entity:1", "entity:2" })
                backward.RegisterEntity(E(id), Haven);

            Assert.Equal(
                forward.Snapshot().Domains[0].Members.Select(m => m.Value),
                backward.Snapshot().Domains[0].Members.Select(m => m.Value));
        }

        [Fact]
        public void Moving_an_entity_updates_membership_on_both_sides()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Haven, "island");
            model.RegisterDomain(Ship, "ship");
            model.RegisterEntity(E("entity:1"), Haven);

            model.MoveEntityToDomain(E("entity:1"), Ship);

            Assert.Equal(Ship, model.DomainOf(E("entity:1")));
            WorldSnapshot snapshot = model.Snapshot();
            Assert.Empty(snapshot.Domains.Single(d => d.Id == Haven).Members);
            Assert.Single(snapshot.Domains.Single(d => d.Id == Ship).Members);
        }

        [Fact]
        public void Moving_an_entity_to_where_it_already_is_is_a_no_op()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Haven, "island");
            model.RegisterEntity(E("entity:1"), Haven);

            model.MoveEntityToDomain(E("entity:1"), Haven);

            Assert.Single(model.Snapshot().Domains[0].Members);
        }

        [Fact]
        public void Removing_a_domain_leaves_its_members_registered_but_unassigned()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Ship, "ship");
            model.RegisterEntity(E("ship:893"), Ship);

            Assert.True(model.RemoveDomain(Ship));

            Assert.Equal(0, model.DomainCount);
            Assert.True(model.HasEntity(E("ship:893")));
            Assert.Null(model.DomainOf(E("ship:893")));
            Assert.Equal(1, model.Snapshot().EntityCount);
        }

        [Fact]
        public void Interaction_upsert_is_idempotent_on_the_pair_and_kind()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:7"));
            model.RegisterEntity(E("ship:893"));

            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Containment,
                InteractionStrength.Weak, InteractionLatencySensitivity.Low,
                InteractionActivity.Idle));
            // Same pair, same kind, opposite endpoint order, different weights.
            model.UpsertInteraction(new InteractionEdge(
                E("ship:893"), E("player:7"), InteractionKind.Containment,
                InteractionStrength.VeryStrong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));

            Assert.Equal(1, model.InteractionCount);
            InteractionSnapshot only = model.Snapshot().Interactions.Single();
            Assert.Equal(InteractionStrength.VeryStrong, only.Strength);
            // Normalised: "player:7" sorts before "ship:893" whichever way it was fed.
            Assert.Equal("player:7", only.A.Value);
        }

        [Fact]
        public void A_different_kind_between_the_same_pair_is_a_different_edge()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:7"));
            model.RegisterEntity(E("ship:893"));
            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Containment,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));
            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Control,
                InteractionStrength.Strong, InteractionLatencySensitivity.VeryHigh,
                InteractionActivity.Active));

            Assert.Equal(2, model.InteractionCount);
        }

        [Fact]
        public void Interaction_removal_works_from_either_endpoint_order()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:7"));
            model.RegisterEntity(E("ship:893"));
            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Control,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));

            Assert.True(model.RemoveInteraction(E("ship:893"), E("player:7"), InteractionKind.Control));
            Assert.False(model.RemoveInteraction(E("ship:893"), E("player:7"), InteractionKind.Control));
            Assert.Equal(0, model.InteractionCount);
        }

        [Fact]
        public void An_edge_to_an_unregistered_entity_is_refused()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:7"));
            Assert.Throws<KeyNotFoundException>(() => model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Containment,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active)));
        }

        [Fact]
        public void Removing_an_entity_removes_every_edge_it_was_an_endpoint_of()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:7"));
            model.RegisterEntity(E("ship:893"));
            model.RegisterEntity(E("island:haven"));
            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("ship:893"), InteractionKind.Containment,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));
            model.UpsertInteraction(new InteractionEdge(
                E("player:7"), E("island:haven"), InteractionKind.Interest,
                InteractionStrength.Weak, InteractionLatencySensitivity.Low,
                InteractionActivity.Intermittent));

            Assert.True(model.RemoveEntity(E("player:7")));

            Assert.Equal(0, model.InteractionCount);
            Assert.Equal(2, model.Snapshot().EntityCount);
        }

        [Fact]
        public void An_entity_cannot_interact_with_itself() =>
            Assert.Throws<ArgumentException>(() => new InteractionEdge(
                E("ship:893"), E("ship:893"), InteractionKind.Containment,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));

        [Fact]
        public void World_snapshot_ordering_is_stable_across_two_differently_built_worlds()
        {
            string[] Describe(WorldSnapshot s) =>
                s.Domains.Select(d => d.Id.Value)
                    .Concat(s.Interactions.Select(i => i.A.Value + "|" + i.B.Value + "|" + i.Kind))
                    .ToArray();

            Assert.Equal(Describe(BuildSampleWorld(reversed: false).Snapshot()),
                Describe(BuildSampleWorld(reversed: true).Snapshot()));
        }

        [Fact]
        public void The_snapshot_is_byte_for_byte_repeatable_from_the_same_model()
        {
            SimulationWorldModel model = BuildSampleWorld(reversed: false);
            WorldSnapshot first = model.Snapshot();
            WorldSnapshot second = model.Snapshot();

            Assert.Equal(first.Domains.Select(d => d.Id.Value + ":" + d.InteractionPressure),
                second.Domains.Select(d => d.Id.Value + ":" + d.InteractionPressure));
            Assert.Equal(first.TotalCrossDomainPressure, second.TotalCrossDomainPressure);
        }

        [Fact]
        public void Pressure_lands_on_the_domains_an_edge_actually_separates()
        {
            SimulationWorldModel model = BuildSampleWorld(reversed: false);
            WorldSnapshot snapshot = model.Snapshot();

            DomainSnapshot ship = snapshot.Domains.Single(d => d.Id == Ship);
            DomainSnapshot haven = snapshot.Domains.Single(d => d.Id == Haven);

            // Control (1.00) + containment (0.75) both reach the ship: the player is
            // in no domain, so both edges separate the ship from something.
            Assert.Equal(1.75, ship.InteractionPressure);
            Assert.Equal(2, ship.ActiveInteractionCount);
            // Interest only: 0.25 x 0.25 x 0.5.
            Assert.Equal(0.0313, haven.InteractionPressure);
            Assert.Equal(1, haven.ActiveInteractionCount);
        }

        [Fact]
        public void An_edge_between_two_unassigned_entities_crosses_nothing()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(E("player:1"));
            model.RegisterEntity(E("player:2"));
            model.UpsertInteraction(new InteractionEdge(
                E("player:1"), E("player:2"), InteractionKind.Proximity,
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));

            WorldSnapshot snapshot = model.Snapshot();
            Assert.False(snapshot.Interactions.Single().IsCrossDomain);
            Assert.Equal(0, snapshot.TotalCrossDomainPressure);
        }

        [Fact]
        public void An_intra_domain_edge_is_not_cross_domain()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Ship, "ship");
            model.RegisterEntity(E("ship:893"), Ship);
            model.RegisterEntity(E("entity:5"), Ship);
            model.UpsertInteraction(new InteractionEdge(
                E("ship:893"), E("entity:5"), InteractionKind.Containment,
                InteractionStrength.VeryStrong, InteractionLatencySensitivity.VeryHigh,
                InteractionActivity.Active));

            WorldSnapshot snapshot = model.Snapshot();
            Assert.False(snapshot.Interactions.Single().IsCrossDomain);
            Assert.Equal(0, snapshot.Domains.Single().InteractionPressure);
            // Still visible as an interaction. Coupling that costs nothing to keep
            // together is not coupling that stopped existing.
            Assert.Equal(1, snapshot.InteractionCount);
            Assert.Equal(1, snapshot.ActiveInteractionCount);
        }

        [Fact]
        public void The_reserved_inspector_slots_survive_snapshotting_unpopulated()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(Haven, "island", "static island state, resident");

            DomainSnapshot domain = model.Snapshot().Domains.Single();
            // The one opaque descriptive field is carried through verbatim...
            Assert.Equal("static island state, resident", domain.Descriptor);
            // ...and the three genuinely unknown slots stay explicitly unknown.
            Assert.Null(domain.Fidelity);
            Assert.Null(domain.AuthorityOwner);
            Assert.Null(domain.MigrationGeneration);
        }

        private static SimulationWorldModel BuildSampleWorld(bool reversed)
        {
            var model = new SimulationWorldModel();
            var domains = new List<(SimulationDomainId Id, string Kind)>
            {
                (Haven, "island"), (Ship, "ship"),
            };
            var entities = new List<(string Id, SimulationDomainId? Domain)>
            {
                ("island:haven", Haven), ("ship:893", Ship), ("player:7", null),
            };
            if (reversed) { domains.Reverse(); entities.Reverse(); }

            foreach ((SimulationDomainId id, string kind) in domains) model.RegisterDomain(id, kind);
            foreach ((string id, SimulationDomainId? domain) in entities) model.RegisterEntity(E(id), domain);

            var edges = new List<InteractionEdge>
            {
                new InteractionEdge(E("player:7"), E("ship:893"), InteractionKind.Control,
                    InteractionStrength.VeryStrong, InteractionLatencySensitivity.VeryHigh,
                    InteractionActivity.Active),
                new InteractionEdge(E("player:7"), E("ship:893"), InteractionKind.Containment,
                    InteractionStrength.VeryStrong, InteractionLatencySensitivity.High,
                    InteractionActivity.Active),
                new InteractionEdge(E("player:7"), E("island:haven"), InteractionKind.Interest,
                    InteractionStrength.Weak, InteractionLatencySensitivity.Low,
                    InteractionActivity.Intermittent),
            };
            if (reversed) edges.Reverse();
            foreach (InteractionEdge edge in edges) model.UpsertInteraction(edge);
            return model;
        }
    }
}
