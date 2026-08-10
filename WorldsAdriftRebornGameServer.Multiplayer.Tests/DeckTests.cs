using System;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The walkable FLOOR: a Deck01 whose 1518 vertices the client turns into a
    /// SOLID collider. The primary deliverable of the full-ship work, and like the
    /// hull and helm it is mostly VALUES that fail in ways only a running client
    /// would show - an empty material list is an IndexOutOfRange the moment the
    /// deck visualizer enables, a non-rectangular polygon quietly swaps the solid
    /// BoxCollider for a convex mesh one - so they are pinned here.
    /// </summary>
    public class DeckTests
    {
        [Fact]
        public void The_deck_asks_for_the_bare_deck_prefab()
        {
            // "Deck01", not "Deck01_unityclient" - the client appends its own
            // worker suffix.
            Assert.Equal("Deck01", Deck.AssetName);
            Assert.Equal(Deck.AssetName, WorldEntities.Deck01().AssetName);
            Assert.Equal(WorldEntities.DefaultAssetContext, WorldEntities.Deck01().AssetContext);
        }

        [Fact]
        public void The_deck_polygon_has_four_corners_at_deck_height_zero()
        {
            // Four corners (a quad) at y = 0: MeshGenerator builds the deck plane at
            // the entity's own y, which is the hull's deck plane.
            Assert.Equal(4, Deck.LocalVertices.Count);
            Assert.All(Deck.LocalVertices, v => Assert.Equal(0.0, v.Y));
        }

        [Fact]
        public void The_deck_polygon_is_rectangular_so_the_client_gives_it_a_solid_box_collider()
        {
            // MeshGenerator.IsRectangular (VERIFIED) requires exactly four vertices
            // with right angles at each corner (adjacent edge dot products ~ 0); it
            // then adds a solid BoxCollider rather than a convex MeshCollider. This
            // replays that check so a future edit that skews the quad cannot silently
            // drop off the box-collider path.
            var vs = Deck.LocalVertices.ToArray();
            Assert.Equal(4, vs.Length);

            (double X, double Z) Edge(int a, int b) => (vs[b].X - vs[a].X, vs[b].Z - vs[a].Z);
            double Dot((double X, double Z) p, (double X, double Z) q) => p.X * q.X + p.Z * q.Z;

            var e0 = Edge(0, 1);
            var e1 = Edge(1, 2);
            var e2 = Edge(2, 3);
            var e3 = Edge(3, 0);

            Assert.True(Math.Abs(Dot(e0, e1)) < 0.05);
            Assert.True(Math.Abs(Dot(e1, e2)) < 0.05);
            Assert.True(Math.Abs(Dot(e2, e3)) < 0.05);
        }

        [Fact]
        public void The_deck_covers_the_one_cell_hull_footprint_at_client_scale()
        {
            // Pre-scale extents; the client applies ShipScale = 2. Port-starboard
            // should span the hull's ~+/-3 m half-width (12 m at scale) and fore-aft
            // ~4 m at scale. Pinning the spans catches an accidental unit slip that
            // would leave the floor smaller than the deck the player expects.
            var vs = Deck.LocalVertices.ToArray();
            double xSpan = vs.Max(v => v.X) - vs.Min(v => v.X);
            double zSpan = vs.Max(v => v.Z) - vs.Min(v => v.Z);
            Assert.Equal(6.0, xSpan, 3);   // *2 = 12 m across
            Assert.Equal(2.0, zSpan, 3);   // *2 = 4 m fore-aft
        }

        [Fact]
        public void The_deck_carries_a_wood_material_because_an_empty_list_would_crash_the_visualizer()
        {
            // ShipDeckVisualizer.OnEnable indexes OriginalMaterials[0]; the material
            // category must be Wood or Metal to select a deck prototype. These
            // constants are what the 1099 branch seeds for the deck (and ONLY the
            // deck - every other entity keeps the empty list the hull needs).
            Assert.True(Deck.MaterialCategory == "Wood" || Deck.MaterialCategory == "Metal");
            Assert.False(string.IsNullOrEmpty(Deck.MaterialTypeId));
        }

        [Fact]
        public void The_deck_sits_centred_on_the_hull_derived_from_it_not_pasted()
        {
            // The deck is a SEPARATE entity whose 190602 is seeded hull-RELATIVE (see
            // BoltedPartTransform); OnHull is the global position that offset is derived
            // from, so its X and Z are the hull's exactly and only Y carries the
            // (currently zero) up offset.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            FixedPointPosition deck = Deck.OnHull(hull);

            Assert.Equal(hull.X, deck.X);
            Assert.Equal(hull.Z, deck.Z);
            Assert.Equal(hull.Y + (long)(Deck.DeckUpMetres * FixedPointPosition.UnitsPerMetre), deck.Y);
            Assert.Equal(deck, WorldEntities.Deck01().Position);
        }

        [Fact]
        public void The_deck_and_the_extra_parts_are_recognised_as_bolted_parts_but_the_hull_is_not()
        {
            // The 8066 branch asks IsBoltedPartKey to decide isRoot: a part points at
            // the hull, the hull is its own root. The deck, helm, engine and sail are
            // parts; the hull and unrelated entities are not.
            Assert.True(WorldEntities.IsBoltedPartKey(WorldEntities.DeckKey));
            Assert.True(WorldEntities.IsBoltedPartKey(WorldEntities.HelmKey));
            Assert.True(WorldEntities.IsBoltedPartKey(WorldEntities.EngineKey));
            Assert.True(WorldEntities.IsBoltedPartKey(WorldEntities.SailKey));
            Assert.False(WorldEntities.IsBoltedPartKey(WorldEntities.ShipFrameKey));
            Assert.False(WorldEntities.IsBoltedPartKey(WorldEntities.IslandKey));
            Assert.False(WorldEntities.IsBoltedPartKey(null));
        }

        [Fact]
        public void The_deck_is_on_by_default_and_registered_after_the_hull()
        {
            WorldEntityRegistry r = WorldEntities.Default(new EntityIdAllocator());

            WorldEntity? deck = r.ByKey(WorldEntities.DeckKey);
            WorldEntity? hull = r.ByKey(WorldEntities.ShipFrameKey);
            Assert.NotNull(deck);
            Assert.NotNull(hull);

            // The hull must be registered before the deck: the deck's 8066 and its
            // ship-surface membership both name the hull by its already-allocated id.
            var keys = r.Registrations.Select(e => e.Key).ToList();
            Assert.True(keys.IndexOf(WorldEntities.ShipFrameKey) < keys.IndexOf(WorldEntities.DeckKey));
        }

        [Fact]
        public void The_deck_can_be_switched_off_and_the_extra_parts_off_by_default_on_by_flag()
        {
            WorldEntityRegistry withDefaults = WorldEntities.Default(new EntityIdAllocator());
            Assert.Null(withDefaults.ByKey(WorldEntities.EngineKey));
            Assert.Null(withDefaults.ByKey(WorldEntities.SailKey));

            WorldEntityRegistry noDeck = WorldEntities.Default(new EntityIdAllocator(), includeDeck: false);
            Assert.Null(noDeck.ByKey(WorldEntities.DeckKey));

            WorldEntityRegistry withParts = WorldEntities.Default(new EntityIdAllocator(), includeExtraParts: true);
            Assert.NotNull(withParts.ByKey(WorldEntities.EngineKey));
            Assert.NotNull(withParts.ByKey(WorldEntities.SailKey));
        }

        [Fact]
        public void The_extra_parts_sit_at_distinct_fore_aft_stations_on_the_hull()
        {
            // Helm forward (+1), sail amidships (0), engine aft (-1.5): distinct
            // stations so they do not stack on one another.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            Assert.Equal(hull.X, ShipParts.EngineOnHull(hull).X);
            Assert.Equal(hull.X, ShipParts.SailOnHull(hull).X);
            Assert.True(ShipParts.EngineOnHull(hull).Z < hull.Z);   // aft
            Assert.Equal(hull.Z, ShipParts.SailOnHull(hull).Z);     // amidships
        }
    }
}
