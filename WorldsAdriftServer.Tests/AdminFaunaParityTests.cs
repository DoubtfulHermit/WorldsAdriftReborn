using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// THE DRIFT GUARD.
    ///
    /// The operator console draws the world's wildlife moving, and it does that by
    /// evaluating the game server's own closed-form movement in the browser
    /// instead of being sent 460 positions every three seconds. That is only
    /// honest while the two evaluators agree, and "they agree because I wrote them
    /// carefully" is exactly the promise that rots.
    ///
    /// So this suite takes the REAL page the server serves, cuts the marked
    /// movement mirror out of its script, runs it in a JavaScript engine against
    /// the REAL published model, and asserts it returns the metres
    /// <see cref="IslandFaunaMovement.LocalPoseAt"/> returns - at timestamps
    /// chosen to land in daylight, in darkness, inside both phase ramps, and
    /// across an orbit wrap.
    ///
    /// It checks the chain in two places on purpose:
    /// <list type="bullet">
    /// <item>with EXACT parameters, to a nanometre - that is the "it is the same
    ///   function" claim, and nothing but a difference in the formulas can break
    ///   it;</item>
    /// <item>with the parameters as the page actually publishes them, rounded, to
    ///   five centimetres - that is the "the wire carries what the browser reads"
    ///   claim, and a renamed JSON field breaks it even though the formulas are
    ///   untouched.</item>
    /// </list>
    /// </summary>
    public class AdminFaunaParityTests
    {
        private const string MirrorBegin = "// ==== FAUNA MOTION MIRROR BEGIN ====";
        private const string MirrorEnd = "// ==== FAUNA MOTION MIRROR END ====";

        /// <summary>
        /// Nanometres. The two evaluators run the same formulas over the same
        /// doubles, so anything a reordered term could produce is orders of
        /// magnitude above this.
        /// </summary>
        private const double ExactTolerance = 1e-9;

        /// <summary>
        /// Five centimetres, which is what the page's published rounding can cost
        /// and is far below one screen pixel at every zoom the map allows. A
        /// formula that had actually diverged would miss by metres.
        /// </summary>
        private const double PublishedTolerance = 0.05;

        /// <summary>
        /// Timestamps that exercise the parts of the model a happy-path sample
        /// would sail past: the middle of the fauna day and of the night, both
        /// dawn and dusk ramps, either side of the orbit wrap, and a whole day
        /// out so a phase that only drifts slowly still has room to diverge.
        /// </summary>
        private static readonly double[] Moments =
        {
            0.0, 1.0, 7.5, 123.4,
            239.0, 240.0, 241.0,        // dawn: day begins at 0.2 of 1200 s
            600.0, 900.0,
            959.0, 960.0, 961.0,        // dusk: day ends at 0.8
            1199.5, 1200.7, 3600.25, 86_400.0,
            // A month of uptime. This is the sample that catches a rounded lap
            // time: an error in a divisor of elapsed seconds is multiplied by how
            // long the server has been running, and a live server is up for weeks.
            2_592_000.0,
        };

        [NodeFact]
        public void The_browser_mirror_returns_the_same_metres_as_the_server_movement()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);
            JObject map = EmbeddedWorldMap(html);
            JObject model = (JObject)map["faunaModel"]!;

            List<JObject> samples = new List<JObject>();
            List<(FaunaCreature Creature, IslandTerrainEnvelope Envelope, double T, bool Exact)> expected =
                new List<(FaunaCreature, IslandTerrainEnvelope, double, bool)>();

            foreach (ReleaseIslandRecord island in SampleIslands())
            {
                JObject published = PublishedFauna(map, island);
                JObject exact = ExactFauna(island.Envelope);

                foreach (FaunaSpecies species in new[] { FaunaSpecies.MantaRay, FaunaSpecies.JellyFish })
                {
                    for (int member = 0; member < 4; member++)
                    {
                        foreach (double t in Moments)
                        {
                            FaunaCreature creature = new FaunaCreature(
                                IslandFaunaPolicy.FirstFaunaEntityId + member, species,
                                island.Definition.Id, member, 0, member);

                            samples.Add(Sample(exact, species, member, t));
                            expected.Add((creature, island.Envelope, t, true));

                            samples.Add(Sample(published, species, member, t));
                            expected.Add((creature, island.Envelope, t, false));
                        }
                    }
                }
            }

            JArray actual = Evaluate(mirror, model, samples);
            Assert.Equal(samples.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                (FaunaCreature creature, IslandTerrainEnvelope envelope, double t, bool isExact) = expected[i];
                (double x, double y, double z) =
                    IslandFaunaMovement.LocalPoseAt(creature, envelope, t);
                JArray got = (JArray)actual[i];
                double tolerance = isExact ? ExactTolerance : PublishedTolerance;
                string where = (isExact ? "exact" : "published") + " " + creature.Species
                    + " member " + creature.MemberIndex + " on " + envelope.IslandId
                    + " at t=" + t.ToString(CultureInfo.InvariantCulture);

                Assert.True(Math.Abs(x - (double)got[0]!) <= tolerance,
                    where + ": X was " + got[0] + ", the server movement says " + x);
                Assert.True(Math.Abs(y - (double)got[1]!) <= tolerance,
                    where + ": Y was " + got[1] + ", the server movement says " + y);
                Assert.True(Math.Abs(z - (double)got[2]!) <= tolerance,
                    where + ": Z was " + got[2] + ", the server movement says " + z);
            }
        }

        /// <summary>
        /// THE ECOLOGY'S PARITY (schema v9). Same discipline as the classic
        /// motion: the mirror's field-following school centre - a bloom's
        /// wandering maximum plus the group's circulation orbit, with the
        /// recovered vertical laws on top - must return the metres
        /// <see cref="FaunaEcologyEvaluator.LocalPoseAt"/> returns, to a
        /// nanometre, with the bloom parameters shaped EXACTLY as the live feed
        /// publishes them (the sanitized wire keys), because that is the object
        /// the page actually hands the mirror.
        /// </summary>
        [NodeFact]
        public void The_ecology_mirror_returns_the_same_metres_as_the_evaluator()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);
            JObject model = (JObject)EmbeddedWorldMap(html)["faunaModel"]!;

            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);

            List<JObject> samples = new List<JObject>();
            List<(FaunaCreature Creature, IslandTerrainEnvelope Envelope, double T)> expected =
                new List<(FaunaCreature, IslandTerrainEnvelope, double)>();

            foreach (ReleaseIslandRecord island in SampleIslands())
            {
                JObject parameters = ExactFauna(island.Envelope);
                parameters["blooms"] = new JObject
                {
                    ["manta"] = BloomsJson(evaluator, island, FaunaSpecies.MantaRay),
                    ["jelly"] = BloomsJson(evaluator, island, FaunaSpecies.JellyFish),
                };

                foreach (FaunaSpecies species in new[] { FaunaSpecies.MantaRay, FaunaSpecies.JellyFish })
                {
                    FaunaBloom[] blooms = evaluator.BloomsFor(
                        island.Definition.Id, species, island.Envelope);
                    for (int group = 0; group < 2; group++)
                    {
                        for (int member = 0; member < 3; member++)
                        {
                            foreach (double t in Moments)
                            {
                                FaunaCreature creature = new FaunaCreature(
                                    IslandFaunaPolicy.FirstFaunaEntityId + member, species,
                                    island.Definition.Id, member, group, member);
                                JObject sample = Sample(parameters, species, member, t);
                                sample["school"] = group;
                                // The (behaviour, epoch) descriptor the live feed
                                // would publish for this instant - the mirror
                                // evaluates the PUBLISHED pair, never the
                                // schedule (it has no seed), and the timestamps
                                // land inside feeds, dives and migrations as the
                                // schedule happens to place them.
                                FaunaGroupBehaviour segment = IslandFaunaBehaviour.SegmentAt(
                                    IslandFaunaEcology.DefaultWorldSeed, island.Definition.Id,
                                    species, group, island.Envelope, blooms.Length, t);
                                sample["g"] = new JObject
                                {
                                    ["behaviour"] = segment.Behaviour.ToString(),
                                    ["epochSeconds"] = segment.EpochSeconds,
                                    ["durationSeconds"] = segment.DurationSeconds,
                                    ["bloom"] = segment.FromBloom,
                                    ["toBloom"] = segment.ToBloom,
                                };
                                samples.Add(sample);
                                expected.Add((creature, island.Envelope, t));
                            }
                        }
                    }
                }
            }

            JArray actual = Evaluate(mirror, model, samples);
            Assert.Equal(samples.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                (FaunaCreature creature, IslandTerrainEnvelope envelope, double t) = expected[i];
                (double x, double y, double z) = evaluator.LocalPoseAt(creature, envelope, t);
                JArray got = (JArray)actual[i];
                string where = "ecology " + creature.Species + " group " + creature.SchoolIndex
                    + " member " + creature.MemberIndex + " on " + envelope.IslandId
                    + " at t=" + t.ToString(CultureInfo.InvariantCulture);

                Assert.True(Math.Abs(x - (double)got[0]!) <= ExactTolerance,
                    where + ": X was " + got[0] + ", the evaluator says " + x);
                Assert.True(Math.Abs(y - (double)got[1]!) <= ExactTolerance,
                    where + ": Y was " + got[1] + ", the evaluator says " + y);
                Assert.True(Math.Abs(z - (double)got[2]!) <= ExactTolerance,
                    where + ": Z was " + got[2] + ", the evaluator says " + z);
            }
        }

        /// <summary>
        /// THE FAMILY'S PARITY (Phase 5). A calf does not sit at its own
        /// golden-angle position - it sits four metres behind and below its
        /// mother - so the browser has to be told which slots are calves and
        /// which adult each trails, and then has to place them with the same
        /// arithmetic the server uses.
        ///
        /// The pairing is SEED-DERIVED and time-independent, so it travels in the
        /// live feed as the group's <c>calves</c> array exactly as the bloom
        /// parameters and the behaviour descriptors do; only the geometry is
        /// restated in JavaScript, and that is what this pins. A mirror that
        /// ignored the array would draw one manta per school in the wrong place
        /// by four metres - invisible at world zoom, wrong at island zoom, and
        /// exactly the drift this whole suite exists to refuse.
        /// </summary>
        [NodeFact]
        public void The_family_mirror_puts_a_calf_where_the_evaluator_does()
        {
            const int GroupMembers = 8;   // 6 adults, 2 calves - a large island's group

            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);
            JObject model = (JObject)EmbeddedWorldMap(html)["faunaModel"]!;

            FaunaEcologyEvaluator evaluator = new FaunaEcologyEvaluator(
                IslandFaunaEcology.DefaultWorldSeed, juveniles: true);

            List<JObject> samples = new List<JObject>();
            List<(FaunaCreature Creature, IslandTerrainEnvelope Envelope, double T)> expected =
                new List<(FaunaCreature, IslandTerrainEnvelope, double)>();
            int calfSamples = 0;

            foreach (ReleaseIslandRecord island in SampleIslands())
            {
                JObject parameters = ExactFauna(island.Envelope);
                parameters["blooms"] = new JObject
                {
                    ["manta"] = BloomsJson(evaluator, island, FaunaSpecies.MantaRay),
                    ["jelly"] = BloomsJson(evaluator, island, FaunaSpecies.JellyFish),
                };

                FaunaBloom[] blooms = evaluator.BloomsFor(
                    island.Definition.Id, FaunaSpecies.MantaRay, island.Envelope);
                for (int group = 0; group < 2; group++)
                {
                    // The pairing in the wire shape StatsSnapshot writes.
                    JArray calves = new JArray();
                    foreach (FaunaCalfSlot slot in IslandFaunaFamily.SlotsFor(
                        island.Definition.Id, FaunaSpecies.MantaRay, group, GroupMembers))
                    {
                        calves.Add(new JObject
                        {
                            ["member"] = slot.MemberIndex,
                            ["mother"] = slot.MotherMemberIndex,
                        });
                    }
                    Assert.NotEmpty(calves);

                    for (int member = 0; member < GroupMembers; member++)
                    {
                        if (IslandFaunaFamily.IsCalfSlot(
                            FaunaSpecies.MantaRay, GroupMembers, member))
                        {
                            calfSamples++;
                        }
                        foreach (double t in Moments)
                        {
                            FaunaCreature creature = new FaunaCreature(
                                IslandFaunaPolicy.FirstFaunaEntityId + member,
                                FaunaSpecies.MantaRay, island.Definition.Id,
                                member, group, member, GroupMembers);
                            FaunaGroupBehaviour segment = IslandFaunaBehaviour.SegmentAt(
                                IslandFaunaEcology.DefaultWorldSeed, island.Definition.Id,
                                FaunaSpecies.MantaRay, group, island.Envelope, blooms.Length, t);
                            JObject sample = Sample(parameters, FaunaSpecies.MantaRay, member, t);
                            sample["school"] = group;
                            sample["g"] = new JObject
                            {
                                ["behaviour"] = segment.Behaviour.ToString(),
                                ["epochSeconds"] = segment.EpochSeconds,
                                ["durationSeconds"] = segment.DurationSeconds,
                                ["bloom"] = segment.FromBloom,
                                ["toBloom"] = segment.ToBloom,
                                ["calves"] = calves.DeepClone(),
                            };
                            samples.Add(sample);
                            expected.Add((creature, island.Envelope, t));
                        }
                    }
                }
            }

            Assert.True(calfSamples > 0, "the sweep never sampled a calf slot");
            JArray actual = Evaluate(mirror, model, samples);
            Assert.Equal(samples.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                (FaunaCreature creature, IslandTerrainEnvelope envelope, double t) = expected[i];
                (double x, double y, double z) = evaluator.LocalPoseAt(creature, envelope, t);
                JArray got = (JArray)actual[i];
                string where = (IslandFaunaFamily.IsCalfSlot(creature) ? "calf " : "adult ")
                    + "group " + creature.SchoolIndex + " member " + creature.MemberIndex
                    + " on " + envelope.IslandId
                    + " at t=" + t.ToString(CultureInfo.InvariantCulture);

                Assert.True(Math.Abs(x - (double)got[0]!) <= ExactTolerance,
                    where + ": X was " + got[0] + ", the evaluator says " + x);
                Assert.True(Math.Abs(y - (double)got[1]!) <= ExactTolerance,
                    where + ": Y was " + got[1] + ", the evaluator says " + y);
                Assert.True(Math.Abs(z - (double)got[2]!) <= ExactTolerance,
                    where + ": Z was " + got[2] + ", the evaluator says " + z);
            }
        }

        /// <summary>
        /// AND THE OTHER HALF OF THE FLAG: with juveniles OFF, a group carrying
        /// calf slots must be drawn exactly as it was before this feature existed.
        /// The mirror is handed no <c>calves</c> array and the evaluator is
        /// constructed without juveniles, and the two must still agree to a
        /// nanometre - which is only true if the family is genuinely inert rather
        /// than merely quiet.
        /// </summary>
        [NodeFact]
        public void With_juveniles_off_a_calf_slot_is_an_ordinary_member()
        {
            const int GroupMembers = 8;

            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);
            JObject model = (JObject)EmbeddedWorldMap(html)["faunaModel"]!;

            FaunaEcologyEvaluator plain =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);

            List<JObject> samples = new List<JObject>();
            List<(FaunaCreature Creature, IslandTerrainEnvelope Envelope, double T)> expected =
                new List<(FaunaCreature, IslandTerrainEnvelope, double)>();

            foreach (ReleaseIslandRecord island in SampleIslands())
            {
                JObject parameters = ExactFauna(island.Envelope);
                parameters["blooms"] = new JObject
                {
                    ["manta"] = BloomsJson(plain, island, FaunaSpecies.MantaRay),
                    ["jelly"] = BloomsJson(plain, island, FaunaSpecies.JellyFish),
                };
                FaunaBloom[] blooms = plain.BloomsFor(
                    island.Definition.Id, FaunaSpecies.MantaRay, island.Envelope);

                for (int member = GroupMembers - 2; member < GroupMembers; member++)
                {
                    foreach (double t in Moments)
                    {
                        FaunaCreature creature = new FaunaCreature(
                            IslandFaunaPolicy.FirstFaunaEntityId + member,
                            FaunaSpecies.MantaRay, island.Definition.Id,
                            member, 0, member, GroupMembers);
                        Assert.True(IslandFaunaFamily.IsCalfSlot(creature),
                            "the sample is not a calf slot, so it proves nothing");

                        FaunaGroupBehaviour segment = IslandFaunaBehaviour.SegmentAt(
                            IslandFaunaEcology.DefaultWorldSeed, island.Definition.Id,
                            FaunaSpecies.MantaRay, 0, island.Envelope, blooms.Length, t);
                        JObject sample = Sample(parameters, FaunaSpecies.MantaRay, member, t);
                        sample["g"] = new JObject
                        {
                            ["behaviour"] = segment.Behaviour.ToString(),
                            ["epochSeconds"] = segment.EpochSeconds,
                            ["durationSeconds"] = segment.DurationSeconds,
                            ["bloom"] = segment.FromBloom,
                            ["toBloom"] = segment.ToBloom,
                        };
                        samples.Add(sample);
                        expected.Add((creature, island.Envelope, t));
                    }
                }
            }

            JArray actual = Evaluate(mirror, model, samples);
            for (int i = 0; i < expected.Count; i++)
            {
                (FaunaCreature creature, IslandTerrainEnvelope envelope, double t) = expected[i];
                (double x, double y, double z) = plain.LocalPoseAt(creature, envelope, t);
                JArray got = (JArray)actual[i];
                Assert.True(Math.Abs(x - (double)got[0]!) <= ExactTolerance);
                Assert.True(Math.Abs(y - (double)got[1]!) <= ExactTolerance);
                Assert.True(Math.Abs(z - (double)got[2]!) <= ExactTolerance);
            }
        }

        /// <summary>
        /// A species' blooms in the LIVE FEED's wire shape - the keys
        /// StatsSnapshot writes and GameStats/PublicMapProjection pass through -
        /// so the parity claim covers the object the page actually receives.
        /// </summary>
        private static JArray BloomsJson(
            FaunaEcologyEvaluator evaluator, ReleaseIslandRecord island, FaunaSpecies species)
        {
            JArray result = new JArray();
            FaunaBloom[] blooms = evaluator.BloomsFor(
                island.Definition.Id, species, island.Envelope);
            for (int i = 0; i < blooms.Length; i++)
            {
                result.Add(new JObject
                {
                    ["species"] = species == FaunaSpecies.MantaRay ? "manta" : "jelly",
                    ["index"] = i,
                    ["amplitude"] = blooms[i].Amplitude,
                    ["sigma"] = blooms[i].SigmaMetres,
                    ["annulusRadius"] = blooms[i].AnnulusRadiusMetres,
                    ["radialDrift"] = blooms[i].RadialDriftMetres,
                    ["angularDrift"] = blooms[i].AngularDriftRadians,
                    ["omegaRadial"] = blooms[i].OmegaRadial,
                    ["omegaAngular"] = blooms[i].OmegaAngular,
                    ["omegaMigration"] = blooms[i].OmegaMigration,
                    ["phaseRadial"] = blooms[i].PhaseRadial,
                    ["phaseAngular"] = blooms[i].PhaseAngular,
                    ["baseAngle"] = blooms[i].BaseAngleRadians,
                });
            }
            return result;
        }

        /// <summary>
        /// The mirror must be CUT OUT of the served page, not read from a copy.
        /// A parity test against a second copy of the JavaScript would pass
        /// happily while the page shipped something else.
        /// </summary>
        [Fact]
        public void The_served_page_carries_the_marked_movement_mirror_and_the_model_it_needs()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);

            Assert.Contains("function faunaMotion(M)", mirror);
            Assert.Contains("localPose", mirror);

            // Every number the mirror reads must be a field the projection
            // publishes. A renamed constant would otherwise reach the browser as
            // `undefined`, and `undefined` in this arithmetic is NaN - a creature
            // that silently stops being drawn at all.
            JObject model = (JObject)EmbeddedWorldMap(html)["faunaModel"]!;
            foreach (string field in new[]
            {
                "dayNightCycleSeconds", "dayBeginsAtCycleFraction", "dayEndsAtCycleFraction",
                "phaseTransitionFraction", "jellyDayRadiusRatio", "jellyNightRadiusRatio",
                "jellySecondsPerRevolution", "walkableHeightFraction", "mantaVerticalSpanRatio",
                "mantaSchoolRadius", "mantaSchoolVerticalRadius", "jellyShoalRadius",
                "jellyShoalVerticalRadius", "weaveRadiansPerSecond", "goldenAngleRadians",
                "goldenRatioFraction",
                // The ecology's constants (v9). Their bloom PARAMETERS travel in
                // the live feed, but these ratios and speeds are compile-time and
                // the mirror must read the published copy, not restate them.
                "mantaCirculationSigmaRatio", "jellyCirculationSigmaRatio",
                "mantaOrbitSpeed", "jellyOrbitSpeed", "maxGroupSpread",
                // The behaviour excursions' shape constants (Phase 4).
                "excursionRamp", "feedRadiusPinch", "diveBelowFloorFraction",
                // The family's two lengths (Phase 5). WHICH slots are calves and
                // WHICH adult each trails is seed-derived and rides the live feed;
                // these are compile-time and must be read, not restated.
                "calfTrailMetres", "calfDropMetres",
            })
            {
                Assert.True(mirror.Contains("M." + field, StringComparison.Ordinal),
                    "the mirror never reads the published constant '" + field + "'");
                Assert.True(model[field] != null,
                    "the mirror reads 'M." + field + "' but the projection does not publish it");
            }
        }

        /// <summary>
        /// The per-island geometry the page publishes must be the movement's own
        /// accessors, not a re-derivation. This runs without an engine, so the
        /// projection stays pinned even where node is absent.
        /// </summary>
        [Fact]
        public void Published_island_geometry_is_the_movements_own_arithmetic()
        {
            JObject map = EmbeddedWorldMap(
                AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json));

            int checkedIslands = 0;
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                JObject? published = TryPublishedFauna(map, island);
                if (published == null) continue;
                checkedIslands++;

                FaunaIslandMotion motion = IslandFaunaMapModel.MotionFor(island.Envelope);
                Near(motion.MantaOrbitRadiusMetres, published, "mantaOrbitRadius", 0.001);
                Near(motion.JellyLateralRadiusMetres, published, "jellyLateralRadius", 0.001);
                Near(motion.CentreX, published, "cx", 0.01);
                Near(motion.CentreY, published, "cy", 0.01);
                Near(motion.CentreZ, published, "cz", 0.01);
                Near(motion.HalfHeightMetres, published, "halfHeight", 0.001);
                // Exactly, not nearly: see ReleaseWorldMap for why the lap time is
                // the one field that may not be trimmed.
                Assert.Equal(motion.MantaLapSeconds, (double)published["mantaLapSeconds"]!);

                FaunaIslandPopulation population =
                    IslandFaunaMapModel.PopulationFor(island.Survey.Tier);
                Assert.Equal(population.MantaRays, (int)published["manta"]!);
                Assert.Equal(population.JellyFish, (int)published["jelly"]!);
            }

            // The whole active catalogue, not a corner of it: an island silently
            // dropped from the projection is a stretch of empty ocean on the map.
            Assert.Equal(ReleaseWorldCatalog.All.Count, checkedIslands);
        }

        /// <summary>
        /// An ABSOLUTE tolerance in metres. xUnit's decimal-places overload rounds
        /// both sides first, so a value a third of a millimetre off the boundary
        /// fails it while a value ten times further away passes - which is a test
        /// that reports on where a number sits in a decimal expansion rather than
        /// on how far apart two lengths are.
        /// </summary>
        private static void Near(double expected, JObject published, string field, double tolerance)
        {
            double actual = (double)published[field]!;
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                field + " was published as " + actual.ToString(CultureInfo.InvariantCulture)
                + " but the movement computes " + expected.ToString(CultureInfo.InvariantCulture));
        }

        private static IEnumerable<ReleaseIslandRecord> SampleIslands()
        {
            // Smallest, median and largest by lateral radius, so the samples
            // cover the extremes the geometry is expressed as ratios of.
            List<ReleaseIslandRecord> ordered = ReleaseWorldCatalog.All
                .OrderBy(island => IslandFaunaMovement.LateralRadiusOf(island.Envelope))
                .ToList();
            yield return ordered[0];
            yield return ordered[ordered.Count / 2];
            yield return ordered[ordered.Count - 1];
        }

        private static JObject Sample(JObject parameters, FaunaSpecies species, int member, double t) =>
            new JObject
            {
                ["p"] = parameters,
                ["species"] = species == FaunaSpecies.MantaRay ? "manta" : "jelly",
                ["school"] = 0,
                ["member"] = member,
                ["t"] = t,
            };

        private static JObject ExactFauna(IslandTerrainEnvelope envelope)
        {
            FaunaIslandMotion motion = IslandFaunaMapModel.MotionFor(envelope);
            return new JObject
            {
                ["cx"] = motion.CentreX,
                ["cy"] = motion.CentreY,
                ["cz"] = motion.CentreZ,
                ["minY"] = motion.MinY,
                ["maxY"] = motion.MaxY,
                ["halfHeight"] = motion.HalfHeightMetres,
                ["mantaOrbitRadius"] = motion.MantaOrbitRadiusMetres,
                ["mantaLapSeconds"] = motion.MantaLapSeconds,
                ["jellyLateralRadius"] = motion.JellyLateralRadiusMetres,
            };
        }

        private static JObject PublishedFauna(JObject map, ReleaseIslandRecord island) =>
            TryPublishedFauna(map, island)
            ?? throw new InvalidOperationException(
                "the page publishes no fauna block for island " + island.Definition.Id);

        private static JObject? TryPublishedFauna(JObject map, ReleaseIslandRecord island)
        {
            foreach (JToken token in (JArray)map["islands"]!)
            {
                if (token is not JObject placement) continue;
                if (placement["inventory"] is not JObject inventory) continue;
                if ((string?)inventory["islandId"] != island.Definition.Id.Value) continue;
                return placement["fauna"] as JObject;
            }
            return null;
        }

        private static string ExtractMirror(string html)
        {
            int begin = html.IndexOf(MirrorBegin, StringComparison.Ordinal);
            int end = html.IndexOf(MirrorEnd, StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin,
                "the served admin page no longer carries the marked fauna movement mirror, so "
                + "nothing is pinning the browser's arithmetic to the server's");
            return html.Substring(begin + MirrorBegin.Length, end - begin - MirrorBegin.Length);
        }

        private static JObject EmbeddedWorldMap(string html)
        {
            const string open = "<script id=\"releaseWorldMap\" type=\"application/json\">";
            int begin = html.IndexOf(open, StringComparison.Ordinal);
            Assert.True(begin >= 0, "the served page carries no embedded release world map");
            begin += open.Length;
            int end = html.IndexOf("</script>", begin, StringComparison.Ordinal);
            return JObject.Parse(html.Substring(begin, end - begin));
        }

        /// <summary>
        /// Runs the extracted mirror in the engine and hands back what it says.
        /// The harness deliberately does nothing but call <c>localPose</c>: any
        /// arithmetic it did itself would be arithmetic the page does not do.
        /// </summary>
        private static JArray Evaluate(string mirror, JObject model, IReadOnlyList<JObject> samples)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "wareborn-fauna-parity-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(directory);
            try
            {
                string inputPath = Path.Combine(directory, "input.json");
                File.WriteAllText(inputPath, new JObject
                {
                    ["model"] = model,
                    ["samples"] = new JArray(samples),
                }.ToString(Formatting.None));

                StringBuilder script = new StringBuilder();
                script.Append(mirror);
                script.Append(@"
const input = JSON.parse(require('fs').readFileSync(process.argv[2], 'utf8'));
const motion = faunaMotion(input.model);
process.stdout.write(JSON.stringify(input.samples.map(function(s){
  const pose = motion.localPose(s.p, s.species, s.school, s.member, s.t, s.g);
  return [pose.x, pose.y, pose.z];
})));
");
                string scriptPath = Path.Combine(directory, "parity.js");
                File.WriteAllText(scriptPath, script.ToString());

                return JArray.Parse(NodeFactAttribute.Run(scriptPath, inputPath));
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        }
    }
}
