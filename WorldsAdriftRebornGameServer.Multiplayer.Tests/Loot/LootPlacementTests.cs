using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Loot
{
    /// <summary>
    /// WHERE CONTAINERS STAND, AND HOW THEY SIT ON THE GROUND.
    ///
    /// The grounding half of this file exists because of an active bug in the
    /// neighbouring system: a felled log keeps its parent tree's ground-plane
    /// position for its whole life and merely rotates about the entity origin
    /// (<c>TreeFall.cs:441-442</c>), so its centreline ends at ground level - half
    /// buried on the flat, hanging in the air on a slope (<c>TreeFall.cs:62-63</c>
    /// says so outright). Retail did not make that mistake with lootables: its own
    /// placement pass SINKS the prop 15-30 cm into the surface along the normal
    /// (<c>IslandDataBankAndLootableSpawnerVisualizer.cs:100</c>). These tests pin
    /// that the sink is applied, applied once, and applied to both worlds.
    /// </summary>
    public class LootPlacementTests
    {
        // ---------------- the budget curve ----------------

        [Fact]
        public void TheBudgetIsRetailsClampedExponentialLerp()
        {
            // Below areaForMin: the floor, exactly.
            Assert.Equal(LootBudget.MinContainers,
                LootBudget.ContainersForArea(LootBudget.AreaForMinContainers - 1));

            // Above areaForMax: the ceiling, exactly.
            Assert.Equal(LootBudget.MaxContainers,
                LootBudget.ContainersForArea(LootBudget.AreaForMaxContainers + 1));

            // At the two knots.
            Assert.Equal(LootBudget.MinContainers,
                LootBudget.ContainersForArea(LootBudget.AreaForMinContainers));
            Assert.Equal(LootBudget.MaxContainers,
                LootBudget.ContainersForArea(LootBudget.AreaForMaxContainers));
        }

        [Fact]
        public void TheBudgetIsMonotonicSoABiggerIslandNeverGetsLessLoot()
        {
            int previous = 0;
            for (double area = 0; area <= LootBudget.AreaForMaxContainers * 1.2; area += 2500)
            {
                int now = LootBudget.ContainersForArea(area);
                Assert.True(now >= previous,
                    "budget fell from " + previous + " to " + now + " at area " + area);
                previous = now;
            }
        }

        [Fact]
        public void AnIslandWithNoMeasuredSurfaceGetsNothingRatherThanTheFloor()
        {
            Assert.Equal(0, LootBudget.ContainersForSurfaceSamples(0));
            Assert.Equal(0, LootBudget.ContainersForSurfaceSamples(-1));
        }

        // ---------------- the release catalogue ----------------

        [Fact]
        public void EveryIslandsSeatCountMatchesWhatTheBudgetFormulaSays()
        {
            // The C# formula and the Python generator are two copies of the same
            // curve. This is what stops them drifting apart silently - the same trick
            // ReleaseTreeCatalogTests uses.
            List<string> wrong = new();

            foreach (ReleaseLootIsland island in ReleaseLootCatalog.All)
            {
                int expected = LootBudget.ContainersForSurfaceSamples(island.SurfaceSamples);
                if (island.Points.Count == expected) continue;

                // Under target is allowed ONLY when the surface genuinely cannot seat
                // more - never over target, which would mean the generator ignored
                // the budget.
                if (island.Points.Count < expected) continue;

                wrong.Add(island.Name + ": " + island.Points.Count + " seats but the budget says "
                    + expected);
            }

            Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        }

        [Fact]
        public void OnlyTheTwoKnownIslandsFallShortOfTheirBudget()
        {
            string[] short_ = ReleaseLootCatalog.All
                .Where(i => i.Points.Count < LootBudget.ContainersForSurfaceSamples(i.SurfaceSamples))
                .Select(i => i.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // Belial has 3 measured surface samples; DrunkRaven Inn has 100. Neither
            // can satisfy even the relaxed spacing ladder. Both are honest zeros, and
            // both are the same islands the tree pass struggles with. A THIRD name
            // appearing here means the placement rules moved.
            Assert.Equal(new[] { "Belial", "DrunkRaven Inn" }, short_);
        }

        [Fact]
        public void EveryReleaseIslandIsInTheCatalogueSoNoneIsSilentlyLootless()
        {
            Assert.Equal(ReleaseWorldCatalog.All.Count, ReleaseLootCatalog.All.Count);

            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                Assert.NotNull(ReleaseLootCatalog.ForWorkshopId(island.Survey.WorkshopId));
            }
        }

        [Fact]
        public void NoTwoSeatsOnAnIslandAreCloserThanTheRelaxationFloor()
        {
            // Retail's rule is 20 m; the generator's ladder relaxes to half that when
            // an island cannot satisfy it. Nothing may be closer than the last rung.
            const double floor = 10.0;
            const double floorSquared = floor * floor;

            foreach (ReleaseLootIsland island in ReleaseLootCatalog.All)
            {
                for (int i = 0; i < island.Points.Count; i++)
                {
                    for (int j = i + 1; j < island.Points.Count; j++)
                    {
                        (double ax, double ay, double az) = island.Points[i];
                        (double bx, double by, double bz) = island.Points[j];
                        double d = (ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz);

                        Assert.True(d >= floorSquared,
                            island.Name + ": seats " + i + " and " + j + " are "
                            + Math.Sqrt(d).ToString("0.0") + " m apart, under the " + floor + " m floor");
                    }
                }
            }
        }

        [Fact]
        public void TheWorldsLootBudgetStaysBoundedBesideItsTrees()
        {
            // 2,243 containers against 13,266 trees. A regression that multiplied
            // this by ten would be paid in streaming time by every player who lands
            // anywhere, and nothing else in the suite would notice.
            Assert.InRange(ReleaseLootCatalog.TotalContainers, 1500, 4000);
        }

        // ---------------- grounding ----------------

        [Fact]
        public void EveryHavenSeatIsSunkExactlyOnceBelowItsSurfaceVertex()
        {
            IReadOnlyList<GeneratedPlacement> raw = HavenSurface.LootLocals();
            IReadOnlyList<LootContainers.Placement> placed = LootContainers.HavenPlacements;

            Assert.Equal(raw.Count, placed.Count);

            for (int i = 0; i < raw.Count; i++)
            {
                Assert.Equal(raw[i].LocalX, placed[i].LocalX, 6);
                Assert.Equal(raw[i].LocalZ, placed[i].LocalZ, 6);
                Assert.Equal(raw[i].LocalY - LootContainers.SinkMetres, placed[i].LocalY, 6);
            }
        }

        [Fact]
        public void TheSinkIsInsideRetailsMeasuredRange()
        {
            // acs/IslandDataBankAndLootableSpawnerVisualizer.cs:100 - Random(0.15, 0.30).
            Assert.InRange(LootContainers.SinkMetres, 0.15, 0.30);
        }

        [Fact]
        public void TheFlatnessGateIsStrictEnoughToSinkStraightDown()
        {
            // The sink is applied along -Y, not along the surface normal, because
            // GeneratedPlacement carries only the normal's Y component. That is only
            // honest while the normal is near vertical: at ny = 0.97 the two differ
            // by under a centimetre. Loosening this gate without carrying the whole
            // normal would put chest corners through the terrain.
            Assert.True(HavenSurface.LootMinUpwardNormal >= 0.96);

            double error = LootContainers.SinkMetres * (1.0 - HavenSurface.LootMinUpwardNormal);
            Assert.True(error < 0.01,
                "sinking straight down at ny=" + HavenSurface.LootMinUpwardNormal
                + " is off by " + error.ToString("0.000") + " m");
        }

        [Fact]
        public void TheSpacingRuleIsRetailsRecoveredTwentyMetres()
        {
            // sqrMagnitude < 400f. This is the one placement constant in the loot
            // pipeline that is not a guess; it must not be quietly tuned.
            Assert.Equal(20.0, HavenSurface.LootMinSpacing);
        }

        [Fact]
        public void NoHavenSeatSitsOnTopOfATreeOrADeposit()
        {
            // A chest wedged inside a rock is a chest you cannot open. The generator
            // has no collision test at all, only exclusion discs, so this asserts the
            // discs were actually passed.
            foreach (LootContainers.Placement seat in LootContainers.HavenPlacements)
            {
                foreach (GeneratedPlacement tree in HavenSurface.TreeLocals())
                {
                    double d = Distance2D(seat.LocalX, seat.LocalZ, tree.LocalX, tree.LocalZ);
                    Assert.True(d >= HavenSurface.TreeClearance,
                        "a container sits " + d.ToString("0.0") + " m from a tree");
                }

                foreach (GeneratedPlacement deposit in HavenSurface.DepositLocals())
                {
                    double d = Distance2D(seat.LocalX, seat.LocalZ, deposit.LocalX, deposit.LocalZ);
                    Assert.True(d >= HavenSurface.TreeClearance,
                        "a container sits " + d.ToString("0.0") + " m from a metal deposit");
                }
            }
        }

        [Fact]
        public void HavenSeatsAreTwentyMetresApart()
        {
            IReadOnlyList<LootContainers.Placement> seats = LootContainers.HavenPlacements;
            for (int i = 0; i < seats.Count; i++)
            {
                for (int j = i + 1; j < seats.Count; j++)
                {
                    double dx = seats[i].LocalX - seats[j].LocalX;
                    double dy = seats[i].LocalY - seats[j].LocalY;
                    double dz = seats[i].LocalZ - seats[j].LocalZ;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Assert.True(d >= HavenSurface.LootMinSpacing - 0.001,
                        "Haven seats " + i + " and " + j + " are " + d.ToString("0.0") + " m apart");
                }
            }
        }

        [Fact]
        public void HavenSeatsAreDeterministicAcrossCalls()
        {
            IReadOnlyList<GeneratedPlacement> first = HavenSurface.LootLocals();
            IReadOnlyList<GeneratedPlacement> second = HavenSurface.LootLocals();
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].LocalX, second[i].LocalX, 9);
                Assert.Equal(first[i].LocalY, second[i].LocalY, 9);
                Assert.Equal(first[i].LocalZ, second[i].LocalZ, 9);
            }
        }

        private static double Distance2D(double ax, double az, double bx, double bz) =>
            Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    }
}
