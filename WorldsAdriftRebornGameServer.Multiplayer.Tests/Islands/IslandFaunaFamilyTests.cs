using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The family is what makes a calf FINDABLE: a school full of identical
    /// animals with one small one somewhere in it is a needle, and a pair
    /// travelling together with one small member is a shape. So the properties
    /// tested here are the ones a player would actually notice failing - a calf
    /// that drifts away from its mother, two calves drawn inside each other, a
    /// calf that changes size as its neighbours come and go, and every school in
    /// the world pairing off the same animal.
    /// </summary>
    public sealed class IslandFaunaFamilyTests
    {
        private static readonly IslandId Island = new IslandId("beautiful-wildlands");

        // ---- which slots are calves ------------------------------------------

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(3, 0)]
        [InlineData(4, 1)]
        [InlineData(7, 1)]
        [InlineData(8, 2)]
        [InlineData(10, 2)]
        [InlineData(12, 3)]
        [InlineData(24, 6)]
        public void A_quarter_of_a_group_is_juvenile_rounded_down(int members, int expected)
        {
            Assert.Equal(expected,
                IslandFaunaFamily.CalfSlotCount(FaunaSpecies.MantaRay, members));
        }

        [Fact]
        public void A_jelly_shoal_has_no_calves_at_any_size()
        {
            // Not a policy choice we could revisit: no jelly prefab carries an
            // AgeVisualizer and no component in the basic-creature stack has a
            // size field, so a smaller jelly cannot be drawn at all.
            for (int members = 0; members <= 40; members++)
            {
                Assert.Equal(0, IslandFaunaFamily.CalfSlotCount(FaunaSpecies.JellyFish, members));
                Assert.Empty(IslandFaunaFamily.SlotsFor(
                    Island, FaunaSpecies.JellyFish, 0, members));
            }
        }

        [Fact]
        public void Calves_are_the_LAST_slots_of_the_fixed_list_never_of_the_expressed_prefix()
        {
            // THE DESIGN TRAP, asserted rather than remembered. If calf-ness were
            // a property of the expressed tail, an animal would move in and out
            // of it as the population rose and fell, and the SAME entity id would
            // visibly shrink and grow. Calf-ness must depend only on the member
            // index and the group's fixed size.
            for (int members = 4; members <= 24; members++)
            {
                int adults = IslandFaunaFamily.AdultSlotCount(FaunaSpecies.MantaRay, members);
                for (int i = 0; i < members; i++)
                {
                    Assert.Equal(i >= adults,
                        IslandFaunaFamily.IsCalfSlot(FaunaSpecies.MantaRay, members, i));
                }
                Assert.Equal(members,
                    adults + IslandFaunaFamily.CalfSlotCount(FaunaSpecies.MantaRay, members));
            }
        }

        [Fact]
        public void A_creature_with_no_group_size_stated_is_never_a_calf()
        {
            // The default of the new FaunaCreature field. A caller that predates
            // groups must get exactly the behaviour it had before.
            FaunaCreature old = new FaunaCreature(
                1, FaunaSpecies.MantaRay, Island, 7, 0, 7);
            Assert.False(IslandFaunaFamily.IsCalfSlot(old));
            Assert.Equal(-1, IslandFaunaFamily.MotherOf(old));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(99)]
        public void A_slot_outside_the_group_is_not_a_calf(int memberIndex)
        {
            Assert.False(IslandFaunaFamily.IsCalfSlot(FaunaSpecies.MantaRay, 8, memberIndex));
            Assert.Equal(-1,
                IslandFaunaFamily.MotherOf(Island, FaunaSpecies.MantaRay, 0, 8, memberIndex));
        }

        // ---- who the mother is ------------------------------------------------

        [Fact]
        public void Every_mother_is_an_adult_and_a_female()
        {
            // Proposal D's whole content: the parent role is consumed from the
            // gender we already send, and the female is the parent - which is the
            // one RECOVERED fact about retail's breeding that we can honour.
            for (int members = 4; members <= 24; members++)
            {
                for (int group = 0; group < 4; group++)
                {
                    foreach (FaunaCalfSlot slot in IslandFaunaFamily.SlotsFor(
                        Island, FaunaSpecies.MantaRay, group, members))
                    {
                        Assert.InRange(slot.MotherMemberIndex, 0,
                            IslandFaunaFamily.AdultSlotCount(FaunaSpecies.MantaRay, members) - 1);
                        Assert.False(IslandFaunaFamily.IsCalfSlot(
                            FaunaSpecies.MantaRay, members, slot.MotherMemberIndex));
                        Assert.Equal(FaunaGender.Female,
                            IslandFaunaPolicy.GenderFor(slot.MotherMemberIndex));
                    }
                }
            }
        }

        [Fact]
        public void Two_calves_never_share_a_mother()
        {
            // They would sit at IDENTICAL offsets and render as one animal inside
            // another - the kind of bug that reads as a broken mesh rather than as
            // a broken rule.
            for (int members = 4; members <= 24; members++)
            {
                for (int group = 0; group < 4; group++)
                {
                    IReadOnlyList<FaunaCalfSlot> slots = IslandFaunaFamily.SlotsFor(
                        Island, FaunaSpecies.MantaRay, group, members);
                    HashSet<int> mothers = new HashSet<int>();
                    foreach (FaunaCalfSlot slot in slots)
                    {
                        Assert.True(mothers.Add(slot.MotherMemberIndex),
                            "two calves of a " + members + "-slot group share member "
                            + slot.MotherMemberIndex);
                    }
                }
            }
        }

        [Fact]
        public void The_mother_is_not_the_same_animal_on_every_island()
        {
            // The design's named caution: alternation makes member 0 always
            // Female, so an unhashed "the first female" rule would put the calf
            // in the same place in every school in the world.
            HashSet<int> seen = new HashSet<int>();
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                seen.Add(IslandFaunaFamily.MotherOf(
                    island.Definition.Id, FaunaSpecies.MantaRay, 0, 12, 9));
            }
            Assert.True(seen.Count > 1,
                "every island in the world pairs the calf with the same member");
        }

        [Fact]
        public void The_pairing_is_stable_across_processes()
        {
            // FNV-1a over the textual tuple, not string.GetHashCode, which .NET
            // randomises per process - a restarted server that re-paired every
            // school would move calves under a reconnecting player.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(
                    IslandFaunaFamily.MotherOf(Island, FaunaSpecies.MantaRay, 1, 12, 10),
                    IslandFaunaFamily.MotherOf(Island, FaunaSpecies.MantaRay, 1, 12, 10));
            }
            // The hash is a pure function of the id string, so a second IslandId
            // with the same text must agree.
            Assert.Equal(
                IslandFaunaFamily.MotherOf(Island, FaunaSpecies.MantaRay, 1, 12, 10),
                IslandFaunaFamily.MotherOf(new IslandId(Island.Value),
                    FaunaSpecies.MantaRay, 1, 12, 10));
        }

        // ---- where the calf ends up -------------------------------------------

        [Fact]
        public void A_calf_sits_exactly_the_recovered_pair_standoff_from_its_mother()
        {
            // The one recovered distance in this feature: retail's
            // PursuingMateConductVisualiser closed to targetDistance = 4 m on the
            // specific animal it was approaching. It must hold at every cluster
            // radius and every instant, which is the point of not scaling the
            // displacement with the mother's own radius.
            foreach (int mother in new[] { 0, 2, 4, 6, 10 })
            {
                foreach (double t in new[] { 0.0, 1.0, 37.5, 900.0, 86_400.0 })
                {
                    (double mx, double my, double mz) = IslandFaunaSchool.MemberOffset(
                        mother, IslandFaunaSchool.MantaSchoolRadiusMetres,
                        IslandFaunaSchool.MantaSchoolVerticalRadiusMetres, t,
                        IslandFaunaSchool.WeaveRadiansPerSecond);
                    (double cx, double cy, double cz) = IslandFaunaSchool.CalfOffset(
                        mother, IslandFaunaSchool.MantaSchoolRadiusMetres,
                        IslandFaunaSchool.MantaSchoolVerticalRadiusMetres, t,
                        IslandFaunaSchool.WeaveRadiansPerSecond);
                    double distance = Math.Sqrt(
                        ((cx - mx) * (cx - mx)) + ((cy - my) * (cy - my)) + ((cz - mz) * (cz - mz)));
                    Assert.True(Math.Abs(distance - IslandFaunaFamily.PairStandoffMetres) < 1e-9,
                        "a calf stood " + distance + " m from member " + mother + " at t=" + t);
                }
            }
        }

        [Fact]
        public void A_calf_is_below_its_mother()
        {
            foreach (int mother in new[] { 0, 2, 4 })
            {
                for (double t = 0.0; t < 600.0; t += 17.0)
                {
                    (_, double my, _) = IslandFaunaSchool.MemberOffset(
                        mother, 12.0, 4.0, t, IslandFaunaSchool.WeaveRadiansPerSecond);
                    (_, double cy, _) = IslandFaunaSchool.CalfOffset(
                        mother, 12.0, 4.0, t, IslandFaunaSchool.WeaveRadiansPerSecond);
                    Assert.True(cy < my, "a calf was above its mother at t=" + t);
                }
            }
        }

        [Fact]
        public void The_pair_stays_inside_the_school_rather_than_splintering_off()
        {
            // 4 m of standoff inside a 12 m school radius. A pair that hung
            // outside the cluster would read as two animals leaving, not as a
            // mother and calf.
            Assert.True(IslandFaunaFamily.PairStandoffMetres
                < IslandFaunaSchool.MantaSchoolRadiusMetres / 2.0);

            double worst = 0.0;
            for (int member = 0; member < 12; member++)
            {
                for (double t = 0.0; t < 600.0; t += 7.0)
                {
                    (double x, double y, double z) = IslandFaunaSchool.CalfOffset(
                        member, IslandFaunaSchool.MantaSchoolRadiusMetres,
                        IslandFaunaSchool.MantaSchoolVerticalRadiusMetres, t,
                        IslandFaunaSchool.WeaveRadiansPerSecond);
                    worst = Math.Max(worst, Math.Sqrt((x * x) + (y * y) + (z * z)));
                }
            }
            Assert.True(worst <= IslandFaunaSchool.MantaSchoolRadiusMetres
                + IslandFaunaFamily.PairStandoffMetres + 1e-9,
                "a calf reached " + worst + " m from its school centre");
        }

        [Fact]
        public void An_offset_with_no_mother_is_the_function_this_file_always_had()
        {
            // THE FLAG-OFF GUARANTEE, at the only seam where the flag reaches the
            // pose. -1 must be indistinguishable from the old overload, to the
            // bit, or "off is byte-identical on the wire" is not true.
            for (int member = 0; member < 24; member++)
            {
                foreach (double t in new[] { 0.0, 0.5, 61.0, 1234.5, 2_592_000.0 })
                {
                    Assert.Equal(
                        IslandFaunaSchool.MemberOffset(member, 12.0, 4.0, t, 0.05),
                        IslandFaunaSchool.MemberOffset(member, 12.0, 4.0, t, 0.05, -1));
                }
            }
        }

        [Fact]
        public void A_calf_offset_is_pure_and_total()
        {
            for (int member = 0; member < 8; member++)
            {
                foreach (double t in new[] { 0.0, 1e-6, 99.5, 2_592_000.0 })
                {
                    (double x, double y, double z) =
                        IslandFaunaSchool.CalfOffset(member, 26.0, 14.0, t, 0.05);
                    Assert.Equal(IslandFaunaSchool.CalfOffset(member, 26.0, 14.0, t, 0.05),
                        (x, y, z));
                    Assert.False(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z));
                }
            }
        }

        // ---- what the world actually gets --------------------------------------

        [Fact]
        public void The_tier1_world_carries_calf_slots_on_most_islands_and_none_on_the_quiet_ones()
        {
            // Measured on the world PRODUCTION actually serves - the tier-1
            // Wilderness rollout, 46 islands - rather than on the whole 254-island
            // catalogue, because that is the number an operator has to recognise
            // in the boot log. At the constants of this commit it is 37 calf slots
            // on 30 of 46 islands, 7 of which can show two at once.
            IReadOnlyList<ReleaseIslandRecord> islands =
                ReleaseWorldRolloutPolicy.Select("tier1");
            Assert.Equal(46, islands.Count);

            int islandsWithCalves = 0, totalSlots = 0, twoOrMore = 0;
            foreach (ReleaseIslandRecord island in islands)
            {
                (int capacity, _) = IslandFaunaCapacity.ClampedToPeerBudget(
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.JellyFish,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaInterestPolicy.DefaultPerPeerCreatures);
                int groups = IslandFaunaCapacity.GroupCountFor(FaunaSpecies.MantaRay, capacity);
                int slots = 0;
                for (int group = 0; group < groups; group++)
                {
                    int size = (capacity / Math.Max(groups, 1))
                        + (group < capacity % Math.Max(groups, 1) ? 1 : 0);
                    slots += IslandFaunaFamily.CalfSlotCount(FaunaSpecies.MantaRay, size);
                }
                if (slots > 0) islandsWithCalves++;
                if (slots >= 2) twoOrMore++;
                totalSlots += slots;

                // A quiet island is a deliberate zero and must stay one.
                if (IslandFaunaCapacity.QuietFactorFor(island.Definition.Id) <= 0.0)
                {
                    Assert.Equal(0, slots);
                }
            }

            // Ranges rather than equalities: the exact counts move with any
            // capacity retune, and pinning them would make this a change detector
            // for IslandFaunaCapacity. What must hold is the SHAPE the feature was
            // costed on - calves on most islands, two on the big ones, and a
            // world-wide total in the tens rather than the hundreds, because a
            // calf that is everywhere is a mesh variant rather than an event.
            Assert.InRange(islandsWithCalves, 24, 40);
            Assert.InRange(twoOrMore, 3, 20);
            Assert.InRange(totalSlots, 25, 60);
        }

        [Fact]
        public void The_smallest_islands_carry_no_calf_at_all_and_that_is_stated_not_hidden()
        {
            // THE FLOOR INTERACTION, asserted so nobody later reads the absence as
            // a bug. Births are integer crossings between the population floor and
            // capacity, and the Phase 3 fix made the floor PROPORTIONAL
            // (TroughLevel of the island's own capacity, never below two). On an
            // island whose manta capacity is two, the floor IS the capacity: the
            // population never moves, so there is never a birth. On a
            // three-capacity island there are births but no calf SLOT, because a
            // quarter of three rounds to zero.
            //
            // That is a deliberate consequence rather than an oversight: a calf on
            // a two-animal rock would be a third of that island's whole wildlife,
            // which is the opposite of what a juvenile is supposed to read as.
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                (int capacity, _) = IslandFaunaCapacity.ClampedToPeerBudget(
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.JellyFish,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaInterestPolicy.DefaultPerPeerCreatures);
                if (capacity >= IslandFaunaFamily.MembersPerCalfSlot) continue;

                Assert.Equal(0, IslandFaunaFamily.CalfSlotCount(FaunaSpecies.MantaRay, capacity));
                if (capacity == 2)
                {
                    Assert.Equal(capacity, IslandFaunaAge.FloorOf(capacity));
                }
            }
        }
    }
}
