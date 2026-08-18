using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The embedded Haven structure table. An empty table would make every
    /// clearance check PASS, which is the dangerous direction, so the count is
    /// pinned: a packaging mistake fails here instead of putting a monument back
    /// inside the ruined metal camp.
    /// </summary>
    public sealed class HavenStructuresTests
    {
        [Fact]
        public void The_table_is_embedded_and_loads()
        {
            Assert.Equal(253, HavenStructures.All.Count);
        }

        [Fact]
        public void It_is_the_ruins_and_only_the_ruins()
        {
            // Rocks, foliage, grass and VFX are deliberately absent: a monument may
            // overlap a shrub without trapping anybody, and including them makes
            // every spot on Haven fail.
            Assert.All(HavenStructures.All, p => Assert.StartsWith("Ruins (", p.Asset));
        }

        /// <summary>
        /// The camp is where findings-haven.md says it is: island-local
        /// x 164..223, y -0.5..25.6, z -31..27, centroid ~(205.3, 15.2, -0.8).
        /// If the projection ever drifts, this catches it before a placement test
        /// silently starts passing for the wrong reason.
        /// </summary>
        [Fact]
        public void The_ruined_metal_camp_is_where_the_research_says_it_is()
        {
            int inCamp = 0;
            foreach (HavenStructures.Prop p in HavenStructures.All)
            {
                if (p.X >= 164 && p.X <= 223 && p.Z >= -31 && p.Z <= 27 && p.Y >= -1 && p.Y <= 26)
                    inCamp++;
            }
            // 217 of the 253 - findings-haven.md's "~178 metal platforms plus
            // walkways, ladders, pipes and girders" plus the Saborian pieces that
            // fall inside the same box.
            Assert.InRange(inCamp, 200, 235);
        }

        /// <summary>
        /// The Haven SPAWN POINT is under the camp - that is the authored
        /// experience, and it is also the reason horizontal distance alone cannot be
        /// used as a clearance test.
        /// </summary>
        [Fact]
        public void The_spawn_point_has_the_camp_over_it()
        {
            Assert.True(HavenStructures.CountNear(208.0, 4.70, 4.0,
                radiusMetres: 8.0, belowMetres: 2.0, aboveMetres: 25.0) > 0);
        }

        [Fact]
        public void Clearance_is_horizontal_and_finite_everywhere_on_the_island()
        {
            Assert.True(HavenStructures.ClearanceAt(208.0, 4.0) < 10.0);
            Assert.True(HavenStructures.ClearanceAt(0.0, 0.0) < 1000.0);
        }
    }
}
