namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One calf slot and the slot it travels with: both are MEMBER INDICES
    /// inside a group, never entity ids, because a family is a property of the
    /// group's fixed slot layout and not of whichever animals are expressed
    /// right now.
    /// </summary>
    /// <param name="MemberIndex">The calf's slot.</param>
    /// <param name="MotherMemberIndex">The slot it trails. Always an EVEN index,
    /// which <see cref="IslandFaunaPolicy.GenderFor"/> makes a Female.</param>
    public readonly record struct FaunaCalfSlot(int MemberIndex, int MotherMemberIndex);

    /// <summary>
    /// MOTHERS AND CALVES: which slots of a group are juveniles, which adult
    /// they travel with, and where that puts them.
    ///
    /// WHY THE TWO HALVES SHIP TOGETHER, since either alone is nearly pointless.
    /// A manta orbits at the island's half-diagonal plus a standoff, which on a
    /// median tier-1 island is about 300 m. At that range a smaller manta is a
    /// few pixels smaller than a larger one and reads as nothing at all - size
    /// is judged AGAINST A NEIGHBOUR, not against an absolute. Equally, two
    /// same-sized animals travelling as a pair inside a loose school reads as
    /// two mantas that happen to be near each other. Together they are a mother
    /// and a calf: the eye lands on the school, the pair resolves out of it, and
    /// the small one is unmistakable because there is a big one right beside it.
    ///
    /// THE SLOT RULE, and why the obvious version is wrong. The tempting rule is
    /// "the last quarter of the EXPRESSED prefix are calves". Do not: as the
    /// population rises and falls a given animal would move in and out of the
    /// tail, so THE SAME ENTITY ID WOULD VISIBLY SHRINK AND GROW as its
    /// neighbours came and went, and a player watching one manta through a cycle
    /// would see it change size for no reason. The rule here is the last
    /// <see cref="MembersPerCalfSlot"/>th of the group's FIXED, SEEDED slot list
    /// - a property of the slot, not of the moment. Calves are then simply
    /// ABSENT when the population is low, which is both stable and legible:
    /// calves in a bloom, none in a lean season.
    ///
    /// MANTAS ONLY, and that split is retail's rather than ours. All four jelly
    /// prefabs we spawn carry an identical thirteen-component set with no
    /// <c>AgeVisualizer</c> and no <c>ScalableObjectVisualiser</c>, and there is
    /// no size, scale or age field anywhere in the basic-creature stack - so a
    /// smaller jelly cannot be drawn at all. Retail maintained real creatures by
    /// BREEDING and basic creatures by a SPAWNER, two structurally different
    /// systems; a jelly had no gender, no age, no mating conduct and no egg. We
    /// are not losing juvenile jellies to a limitation, we are inheriting the
    /// split Bossa shipped.
    ///
    /// PROVENANCE.
    /// <list type="bullet">
    /// <item><b>RECOVERED</b>: that the FEMALE is the parent -
    ///   <c>MatingConductVisualiser</c> calls <c>PregnancyVisualiser.Impregnate()</c>
    ///   on the female only - and that habitats and flocks tracked
    ///   <c>femaleEntities</c> and <c>maleEntities</c> separately. And
    ///   <see cref="PairStandoffMetres"/>: <c>PursuingMateConductVisualiser</c>
    ///   closes to <c>targetDistance = 4f</c> on the specific animal it is
    ///   approaching, which is the only surviving statement in the client about
    ///   how far apart two mantas are when they are deliberately TOGETHER rather
    ///   than merely in the same flock.</item>
    /// <item><b>WAREBORN TUNING</b>: the calf FRACTION, which slots are calves,
    ///   how the standoff splits between trailing and dropping, and the mother
    ///   ROTATION. Retail's spacing was emergent from a five-rule boid steerer
    ///   (cohesion 1.5, separation 1.5, alignment 1.5, seek 15, wander 10) and no
    ///   formation table exists anywhere - so a mother-and-calf offset is ours,
    ///   exactly as <see cref="IslandFaunaSchool"/>'s golden-angle cluster
    ///   already is.</item>
    /// </list>
    ///
    /// WHAT THIS DOES NOT CLAIM. Nobody is anybody's offspring. A "mother" here
    /// is the adult a calf slot is drawn beside, chosen so the pair reads as a
    /// pair; there is no lineage, no gestation and no inheritance, and the
    /// client offers no channel through which a player could perceive one if
    /// there were. Consuming gender for the PARENT ROLE is the recovered part;
    /// the strict alternation that assigns gender in the first place stays what
    /// it always was, tuning.
    /// </summary>
    public static class IslandFaunaFamily
    {
        /// <summary>
        /// How many slots a group must carry for each calf slot: a quarter of
        /// the group is juvenile, rounded down. WAREBORN TUNING, chosen for the
        /// duty cycle it produces rather than for the fraction itself - it puts
        /// no calf on a two- or three-animal group (where one more animal is a
        /// crowd), one on the common four-to-seven group, and two on the large
        /// islands, each present for roughly half the population cycle. A calf
        /// that is always there is a mesh variant; a calf that is never there is
        /// nothing.
        /// </summary>
        public const int MembersPerCalfSlot = 4;

        /// <summary>
        /// HOW FAR A CALF SITS FROM ITS MOTHER, in metres. RECOVERED, and the
        /// only recovered distance in this file: <c>targetDistance = 4f</c> in
        /// <c>PursuingMateConductVisualiser</c> is the standoff retail's own
        /// conduct closed to when one manta was deliberately approaching another
        /// specific manta. It is the client's own answer to "how far apart are
        /// two mantas that are together", and it sits comfortably inside
        /// <see cref="IslandFaunaSchool.MantaSchoolRadiusMetres"/> (12 m), so a
        /// pair reads as a pair inside the school rather than as a splinter of
        /// it.
        ///
        /// The per-prefab override of that field is lost, so this is the
        /// default rather than the manta's own number - stated because it is the
        /// difference between "recovered" and "recovered exactly".
        /// </summary>
        public const double PairStandoffMetres = 4.0;

        /// <summary>
        /// How much of <see cref="PairStandoffMetres"/> is VERTICAL rather than
        /// trailing. WAREBORN TUNING: the standoff's length is recovered, the
        /// direction it points is not.
        ///
        /// Below and behind, because that is where a calf sits on every animal a
        /// player has ever watched, and because a manta school is a broad flat
        /// sheet - <see cref="IslandFaunaSchool.MantaSchoolVerticalRadiusMetres"/>
        /// is 4 m against a 12 m lateral radius - so a purely lateral offset
        /// would hide the calf inside the sheet at exactly the shallow viewing
        /// angles a player on an island actually has.
        /// </summary>
        public const double CalfDropRatio = 0.45;

        /// <summary>
        /// The vertical component of the calf's offset, in metres, downward.
        /// Derived so it and <see cref="CalfTrailMetres"/> cannot drift apart
        /// from the recovered standoff.
        /// </summary>
        public const double CalfDropMetres = PairStandoffMetres * CalfDropRatio;

        /// <summary>
        /// The trailing component, in metres - the remainder of the standoff,
        /// so the calf's distance from its mother is EXACTLY
        /// <see cref="PairStandoffMetres"/> whatever the drop ratio is set to.
        /// </summary>
        public static double CalfTrailMetres { get; } =
            PairStandoffMetres * Math.Sqrt(1.0 - (CalfDropRatio * CalfDropRatio));

        /// <summary>
        /// Whether this species can carry juveniles at all. Manta only; see the
        /// type remarks for why that is retail's split rather than ours.
        /// </summary>
        public static bool AppliesTo(FaunaSpecies species) => species == FaunaSpecies.MantaRay;

        /// <summary>
        /// How many of a group's slots are calf slots. Total: a negative or tiny
        /// group, or a species with no scale path, gets zero.
        /// </summary>
        public static int CalfSlotCount(FaunaSpecies species, int groupMembers) =>
            !AppliesTo(species) || groupMembers < MembersPerCalfSlot
                ? 0
                : groupMembers / MembersPerCalfSlot;

        /// <summary>How many of a group's slots are adults - everything that is not a calf.</summary>
        public static int AdultSlotCount(FaunaSpecies species, int groupMembers) =>
            groupMembers <= 0 ? 0 : groupMembers - CalfSlotCount(species, groupMembers);

        /// <summary>
        /// Whether one member slot is a calf slot: the LAST
        /// <see cref="CalfSlotCount"/> slots of the group, which is what makes it
        /// a property of the slot rather than of the moment.
        /// </summary>
        public static bool IsCalfSlot(FaunaSpecies species, int groupMembers, int memberIndex) =>
            memberIndex >= 0
            && memberIndex < groupMembers
            && memberIndex >= AdultSlotCount(species, groupMembers);

        /// <summary>The same question asked of a seeded creature, which carries its own group size.</summary>
        public static bool IsCalfSlot(FaunaCreature creature) =>
            IsCalfSlot(creature.Species, creature.GroupMembers, creature.MemberIndex);

        /// <summary>
        /// WHICH ADULT A CALF SLOT TRAVELS WITH, or -1 for a slot that is not a
        /// calf.
        ///
        /// FEMALES ONLY, which is where proposal D earns its one line: the
        /// candidates are the EVEN adult slots, and
        /// <see cref="IslandFaunaPolicy.GenderFor"/> makes every even member a
        /// Female. That the female was the parent is RECOVERED; that we assign
        /// gender by strict alternation is not, and stays labelled as tuning
        /// where it lives.
        ///
        /// HASHED, and this is the caution the design called out by name:
        /// alternation makes member 0 always Female, so an unhashed "the mother
        /// is the first female" rule would put the calf in the same place in
        /// every school in the world. The candidate list is ROTATED by a stable
        /// per-group hash, which both spreads the choice across islands and
        /// guarantees distinct calves get distinct mothers - two calves sharing
        /// a mother would sit at the same offset and render as one animal
        /// inside another. The rotation cannot exhaust the list: a group has
        /// <c>members/4</c> calves against about <c>3*members/8</c> even adults.
        ///
        /// FNV-1a over the textual tuple, not <c>string.GetHashCode</c>, for the
        /// reason every other hash in this feature states: .NET string hashing
        /// is randomised per process, and a restarted server would re-pair every
        /// school in the world.
        /// </summary>
        public static int MotherOf(
            IslandId islandId, FaunaSpecies species, int groupIndex,
            int groupMembers, int memberIndex)
        {
            if (!IsCalfSlot(species, groupMembers, memberIndex)) return -1;

            int adults = AdultSlotCount(species, groupMembers);
            int candidates = (adults + 1) / 2;      // the even indices in [0, adults)
            if (candidates <= 0) return -1;

            int calfOrdinal = memberIndex - adults; // 0 for the first calf slot
            int start = (int)(Unit(islandId, species, groupIndex) % (uint)candidates);
            return 2 * ((start + calfOrdinal) % candidates);
        }

        /// <summary>The same question asked of a seeded creature.</summary>
        public static int MotherOf(FaunaCreature creature) =>
            MotherOf(creature.IslandId, creature.Species, creature.SchoolIndex,
                creature.GroupMembers, creature.MemberIndex);

        /// <summary>
        /// Every calf slot in one group, in slot order, with the adult each one
        /// travels with. This is the shape the telemetry publishes and the
        /// browser mirror consumes: the PAIRING is seed-derived and
        /// time-independent, so it travels in the feed exactly as the bloom
        /// parameters and the behaviour descriptors do, and only the TIME part
        /// is restated in JavaScript.
        /// </summary>
        public static IReadOnlyList<FaunaCalfSlot> SlotsFor(
            IslandId islandId, FaunaSpecies species, int groupIndex, int groupMembers)
        {
            int calves = CalfSlotCount(species, groupMembers);
            if (calves <= 0) return Array.Empty<FaunaCalfSlot>();

            int adults = AdultSlotCount(species, groupMembers);
            FaunaCalfSlot[] slots = new FaunaCalfSlot[calves];
            for (int i = 0; i < calves; i++)
            {
                int member = adults + i;
                slots[i] = new FaunaCalfSlot(member,
                    MotherOf(islandId, species, groupIndex, groupMembers, member));
            }
            return slots;
        }

        /// <summary>
        /// The deterministic uniform this file pairs on. Tagged "family" so it
        /// can never collide with the rhythm's or the ecology's channels.
        /// </summary>
        public static uint Unit(IslandId islandId, FaunaSpecies species, int groupIndex)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            uint hash = OffsetBasis;
            void Mix(string s)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash = (hash ^ s[i]) * Prime;
                }
                hash = (hash ^ '|') * Prime;
            }
            Mix("family");
            Mix(islandId.ToString());
            Mix(((int)species).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(groupIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return hash;
        }
    }
}
