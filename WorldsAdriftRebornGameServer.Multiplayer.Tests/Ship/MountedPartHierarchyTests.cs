using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// WHICH MOUNTED PARTS BECOME REAL UNITY CHILDREN OF THE HULL - the pure half of
    /// PHASE SC5 (docs/plans/feature-roadmap.md 11.11.4).
    ///
    /// This decision is invisible from the server: a wrong answer produces a part that
    /// still renders, still rides the ship and still logs nothing, and the only symptom
    /// is that a player cannot bolt an instrument to it (too permissive is worse - it
    /// destroys the part's client rigidbody and can churn its transform). That is
    /// exactly the class of feature this repo has shipped invisible before, so the
    /// policy is pinned here rather than trusted.
    /// </summary>
    public class MountedPartHierarchyTests
    {
        /// <summary>The two parts SC5 deliberately scopes itself to.</summary>
        private static readonly string[] BarPipes = { "barPipe", "barPipeBent" };

        [Fact]
        public void A_bar_pipe_gets_a_REAL_hierarchy_key_not_the_no_parent_sentinel()
        {
            // THE deliverable. "~" is SetNoParent() client-side
            // (RelativeParentTransformChildHierarchyBehaviour.cs:34-42), so a "~" pipe
            // has no Unity ancestors and every one of the client's five placement parent
            // walks fails on it. A real key is what makes the pipe a mounting surface.
            foreach (string pipe in BarPipes)
            {
                Assert.True(MountedPartHierarchy.IsUnityChild(pipe), pipe + " must be a Unity child.");
                Assert.NotEqual(BoltedPartTransform.RelativeSlotKey, MountedPartHierarchy.HierarchyKeyFor(pipe));
                Assert.Equal(MountedPartHierarchy.HierarchyKey, MountedPartHierarchy.HierarchyKeyFor(pipe));
            }
        }

        [Fact]
        public void Every_other_catalogue_part_keeps_the_unchanged_tilde_follow()
        {
            // SC5 is scoped to bar pipes ON PURPOSE: no bar pipe exists in any player's
            // world, so this step cannot regress an existing ship. Every other part type
            // IS already bolted to live ships, and a real parent DESTROYS the part's
            // client rigidbody (TransformManageRigidbodyBehaviour.SaveAndRemoveRigidbody),
            // which BoltedPartTransform says in as many words the helm, engine and sail
            // must keep. Widening this list is a separate, riskier change; this test is
            // what makes widening it a deliberate act rather than a slip.
            foreach (LoosePartDefinition def in LoosePartCatalogue.All)
            {
                if (BarPipes.Contains(def.ItemType, StringComparer.Ordinal))
                {
                    continue;
                }

                Assert.False(MountedPartHierarchy.IsUnityChild(def.ItemType),
                    def.ItemType + " must NOT be a Unity child in this phase.");
                Assert.Equal(BoltedPartTransform.RelativeSlotKey,
                    MountedPartHierarchy.HierarchyKeyFor(def.ItemType));
            }
        }

        [Fact]
        public void The_two_pipe_item_types_are_real_catalogue_recipes()
        {
            // The whole policy keys off catalogue schematic ids. Rename a row and this
            // feature switches itself off in total silence - the pipe would go back to
            // "~" and nothing anywhere would say so. Pin the ids to the catalogue.
            foreach (string pipe in BarPipes)
            {
                LoosePartDefinition? def = LoosePartCatalogue.ForSchematic(pipe);
                Assert.NotNull(def);
                Assert.Equal(pipe, def!.ItemType);
            }
        }

        [Fact]
        public void The_list_is_exactly_the_two_bar_pipes()
        {
            Assert.Equal(
                BarPipes.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                MountedPartHierarchy.UnityChildItemTypes.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void The_hierarchy_key_resolves_to_the_hull_ROOT_transform()
        {
            // A key the hull's TransformOffsetsRegistry does not know falls back to the
            // hull's own transform (TransformParentHierarchyBehaviour.GetTransformOffset
            // :59-66). A "#"-prefixed key is the registry's own slot convention, so one
            // could resolve to some authored sub-transform - or to nothing useful - and
            // the pipe would parent somewhere other than the hull root, off by whatever
            // that offset is. Keep it a plain, non-empty word.
            Assert.NotEmpty(MountedPartHierarchy.HierarchyKey);
            Assert.DoesNotContain("#", MountedPartHierarchy.HierarchyKey, StringComparison.Ordinal);
            Assert.NotEqual(BoltedPartTransform.RelativeSlotKey, MountedPartHierarchy.HierarchyKey);
        }

        [Fact]
        public void Unknown_null_and_wrongly_cased_item_types_stay_tilde_followers()
        {
            // Fail SAFE in every direction: an unrecognised part behaves exactly as it
            // does today. Case matters because these are literal catalogue keys, and a
            // case-insensitive match would quietly capture a future "BarPipe" prefab
            // name that is NOT the schematic id this policy is keyed on.
            foreach (string? notAPipe in new string?[] { null, "", "lamp", "railing", "barpipe", "BARPIPE", "BarPipe" })
            {
                Assert.False(MountedPartHierarchy.IsUnityChild(notAPipe), "unexpected match: " + (notAPipe ?? "null"));
                Assert.Equal(BoltedPartTransform.RelativeSlotKey, MountedPartHierarchy.HierarchyKeyFor(notAPipe));
            }
        }

        [Fact]
        public void The_key_and_the_wake_filter_can_never_disagree()
        {
            // The seed, the mount commit and the flight wake all read this pair. If
            // IsUnityChild said "child" while HierarchyKeyFor still handed out "~" (or
            // the reverse), the wake would keep re-asserting a parent the client had
            // just been told not to have - an unparent/reparent on every heartbeat,
            // which is the jitter failure mode SC5's risk 1 names.
            IEnumerable<string?> everything = LoosePartCatalogue.All
                .Select(d => (string?)d.ItemType)
                .Concat(new string?[] { null, "", "deck-haven", "nonsense" });

            foreach (string? itemType in everything)
            {
                bool child = MountedPartHierarchy.IsUnityChild(itemType);
                string key = MountedPartHierarchy.HierarchyKeyFor(itemType);
                Assert.Equal(child, key != BoltedPartTransform.RelativeSlotKey);
            }
        }

        [Fact]
        public void An_instrument_is_not_made_a_unity_child_by_this_phase()
        {
            // Instruments are what MOUNTS ON a pipe; they are not themselves the
            // surface, and they are already bolted to live ships. Flipping them is the
            // documented follow-on, gated on a live confirmation - see
            // ShipInstruments.MountSurface. Listed by schematic id, because a catalogue
            // definition's ItemType IS its schematic id ("altimeter"), not its
            // ShipInstruments.ItemType category - so IsInstrument would answer false
            // here and the loop would pass vacuously.
            string[] instruments =
            {
                "altimeter", "fuelGauge", "headingIndicator", "artificialHorizon", "airspeedIndicator",
            };
            foreach (string instrument in instruments)
            {
                Assert.NotNull(LoosePartCatalogue.ForSchematic(instrument));
                Assert.False(MountedPartHierarchy.IsUnityChild(instrument));
            }

            // The instrument mount surface stays "deck" in this branch. shipSurfaces
            // would raycast Layers.Environment - which the bar pipe now finally answers
            // - but it also LOSES the ShipAttachmentSolid deck, so the flip is a
            // deliberate follow-on and not a side effect of this one.
            Assert.Equal("deck", ShipInstruments.MountSurface);
        }
    }
}
