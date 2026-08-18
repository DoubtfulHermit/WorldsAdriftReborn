namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE ECOLOGY, BOUND TO A WORLD SEED AND READY TO DRIVE POSES.
    ///
    /// <see cref="IslandFaunaEcology"/> is a pile of pure functions; this is the
    /// one object that holds the world's seed, memoises each island's bloom
    /// parameters, and exposes the two shapes the rest of the server wants: a
    /// <see cref="FaunaPoseFunction"/> for <see cref="IslandFaunaRegistry"/>, and
    /// the bloom list for the telemetry/map projections.
    ///
    /// MEMOISATION IS NOT AN OPTIMISATION HERE, it is a determinism aid: the
    /// blooms for one (island, species) are computed ONCE and shared by every
    /// pose, every telemetry write and every projection, so the map and the wire
    /// cannot be reading two different derivations of the same seed. The cache
    /// is keyed on the pure inputs and never invalidated, because the function it
    /// caches has no time argument - the TIME dependence lives entirely in
    /// <see cref="IslandFaunaEcology.BloomCentreAt"/>, which is evaluated fresh
    /// every call.
    ///
    /// Not thread-safe, like every other fauna type: the server is one poll loop.
    /// </summary>
    public sealed class FaunaEcologyEvaluator
    {
        private readonly int _worldSeed;
        private readonly Dictionary<(IslandId Island, FaunaSpecies Species), FaunaBloom[]> _blooms =
            new Dictionary<(IslandId, FaunaSpecies), FaunaBloom[]>();

        public FaunaEcologyEvaluator(int worldSeed)
        {
            _worldSeed = worldSeed;
        }

        /// <summary>The seed every bloom in this world is derived from.</summary>
        public int WorldSeed => _worldSeed;

        /// <summary>
        /// One island's blooms for one species, memoised. The envelope is an
        /// input to the derivation but NOT part of the key: an island's envelope
        /// is immutable catalogue data, so two calls with the same island id
        /// cannot legitimately disagree about it.
        /// </summary>
        public FaunaBloom[] BloomsFor(
            IslandId islandId, FaunaSpecies species, IslandTerrainEnvelope envelope)
        {
            if (_blooms.TryGetValue((islandId, species), out FaunaBloom[]? cached))
            {
                return cached;
            }
            FaunaBloom[] blooms = IslandFaunaEcology.BloomsFor(
                _worldSeed, islandId, species, envelope);
            _blooms[(islandId, species)] = blooms;
            return blooms;
        }

        /// <summary>
        /// The bloom one GROUP of a species rides on this island. Round-robin
        /// over the island's blooms, so a second group finds a different maximum
        /// wherever the island carries one.
        /// </summary>
        public FaunaBloom BloomForGroup(
            IslandId islandId, FaunaSpecies species, IslandTerrainEnvelope envelope, int groupIndex)
        {
            FaunaBloom[] blooms = BloomsFor(islandId, species, envelope);
            return blooms[IslandFaunaEcology.BloomIndexFor(groupIndex, blooms.Length)];
        }

        /// <summary>
        /// A creature's island-LOCAL pose under the ecology, in metres: its
        /// group's field-following centre plus its member offset.
        ///
        /// LATERAL comes from the ecology (the group circulates its bloom's
        /// moving maximum). VERTICAL does NOT, and that is deliberate: both
        /// species' altitude laws are RECOVERED - the manta's midpoint-to-top
        /// band (PatrolVisualiser's wrapped sine) and the jelly's day/night blend
        /// between the underside and the walkable rim (JellyFishMovement's 0.2/0.8
        /// window) - while the field is WAREBORN TUNING. A tuned term must not
        /// overwrite a recovered one, so the field moves wildlife AROUND the
        /// island and the recovered laws still decide how high it flies.
        ///
        /// The manta's band keeps the ISLAND LAP's pace, not the bloom orbit's,
        /// and the distinction was learned by measuring: a bloom orbit is tens
        /// of metres across, so a circuit takes half a minute, and a band tied
        /// to it had the manta pumping its full hundred-metre climb every
        /// thirty seconds at up to 8 m/s of pure vertical - frantic, and over
        /// the pose budget. Retail's band traversal took one PATROL LAP, which
        /// is minutes; the island's own lap time is the recovered pace and is
        /// what the fraction below uses.
        /// </summary>
        public (double X, double Y, double Z) LocalPoseAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            FaunaBloom bloom = BloomForGroup(
                creature.IslandId, creature.Species, envelope, creature.SchoolIndex);

            (double gx, double gz) = IslandFaunaEcology.GroupCentreAt(
                bloom, creature.Species, creature.SchoolIndex, elapsedSeconds);

            double x = IslandFaunaMovement.CentreXOf(envelope) + gx;
            double z = IslandFaunaMovement.CentreZOf(envelope) + gz;
            double y = creature.Species == FaunaSpecies.MantaRay
                ? IslandFaunaMovement.CentreYOf(envelope)
                    + (IslandFaunaMovement.HalfHeightOf(envelope)
                        * IslandFaunaMovement.MantaVerticalOffsetRatioAt(
                            MantaBandFraction(creature, envelope, elapsedSeconds)))
                : IslandFaunaMovement.JellyAltitudeAt(envelope, elapsedSeconds);

            (double radius, double verticalRadius) =
                IslandFaunaSchool.ClusterFor(creature.Species);
            (double ox, double oy, double oz) = IslandFaunaSchool.MemberOffset(
                creature.MemberIndex, radius, verticalRadius, elapsedSeconds,
                IslandFaunaSchool.WeaveRadiansPerSecond);

            return (x + ox, y + oy, z + oz);
        }

        /// <summary>
        /// A creature's facing under the ecology.
        ///
        /// SAME MACHINERY, DIFFERENT PATH - and that is the point of deriving
        /// facing by finite difference rather than by hand
        /// (<see cref="IslandFaunaMovement.HeadingSampleSeconds"/>): the heading
        /// follows whatever the position function does, so replacing a circle
        /// with a field-following epicycle cannot leave a creature pointing along
        /// the old path. The species rules are unchanged - a manta noses along
        /// its school's travel, held level and banked into the turn; a jelly
        /// keeps its bell up with a free-drifting yaw.
        /// </summary>
        public FaunaRotation RotationAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds) =>
            creature.Species == FaunaSpecies.MantaRay
                ? IslandFaunaMovement.MantaRotationAlong(
                    creature, elapsedSeconds,
                    t => GroupCentreWorldish(creature, envelope, t))
                : IslandFaunaMovement.JellyRotationAlong(
                    creature, elapsedSeconds,
                    t => GroupCentreWorldish(creature, envelope, t));

        /// <summary>
        /// The pose function <see cref="IslandFaunaRegistry"/> drives, in world
        /// coordinates. Shaped exactly like
        /// <see cref="IslandFaunaMovement.WorldTransformAt"/> so the registry
        /// cannot tell the two apart, and evaluating position and rotation in one
        /// call for the same reason that one does: two calls could describe two
        /// different instants.
        /// </summary>
        public FaunaTransform WorldTransformAt(
            FaunaCreature creature, IslandDefinition island,
            IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }
            (double x, double y, double z) = LocalPoseAt(creature, envelope, elapsedSeconds);
            return new FaunaTransform(
                island.LocalToGlobal(x, y, z),
                RotationAt(creature, envelope, elapsedSeconds));
        }

        /// <summary>
        /// The GROUP's centre (no member offset) at an instant, which is what the
        /// heading rules differentiate - a member's own cluster weave is a slow
        /// circulation, and differentiating it would have animals at the front
        /// and back of one school facing measurably different ways.
        /// </summary>
        private (double X, double Y, double Z) GroupCentreWorldish(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            FaunaBloom bloom = BloomForGroup(
                creature.IslandId, creature.Species, envelope, creature.SchoolIndex);
            (double gx, double gz) = IslandFaunaEcology.GroupCentreAt(
                bloom, creature.Species, creature.SchoolIndex, elapsedSeconds);
            double y = creature.Species == FaunaSpecies.MantaRay
                ? IslandFaunaMovement.CentreYOf(envelope)
                    + (IslandFaunaMovement.HalfHeightOf(envelope)
                        * IslandFaunaMovement.MantaVerticalOffsetRatioAt(
                            MantaBandFraction(creature, envelope, elapsedSeconds)))
                : IslandFaunaMovement.JellyAltitudeAt(envelope, elapsedSeconds);
            return (IslandFaunaMovement.CentreXOf(envelope) + gx, y,
                IslandFaunaMovement.CentreZOf(envelope) + gz);
        }

        /// <summary>
        /// How far through the vertical band a manta group is: the island's OWN
        /// recovered lap pace, phase-spread per group. See
        /// <see cref="LocalPoseAt"/> for why this is not the bloom orbit's angle.
        /// </summary>
        private static double MantaBandFraction(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds) =>
            IslandFaunaSchool.Fraction(
                (elapsedSeconds / IslandFaunaMovement.MantaLapSecondsOf(envelope))
                + IslandFaunaSchool.SchoolPhaseFraction(creature.SchoolIndex));
    }
}
