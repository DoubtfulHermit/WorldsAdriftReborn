namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHERE A FELLED LOG COMES TO REST, given ground that this server cannot see.
    ///
    /// THE COMPLAINT THIS EXISTS TO ANSWER. <see cref="TreeFall"/> topples a log
    /// exactly ninety degrees about the trunk's base, which lays the trunk out flat
    /// at the height the tree stood at. On flat ground that is right. On a slope it
    /// is wrong in both directions at once: felled uphill the trunk drives into the
    /// hillside and half of it is inside the mountain, felled downhill it stays level
    /// while the ground falls away and the far end hangs in the air. Both were
    /// reported from the live world in exactly those words.
    ///
    /// WHY THERE IS NO RAYCAST. This server has no terrain query - no collider, no
    /// height table, no physics - and three separate policy files say so as a
    /// standing constraint rather than as a TODO (<c>PlayerPositionPolicy</c>,
    /// <c>IslandLocationPolicy</c>, <c>ReleaseWorldCatalog</c>). The island meshes
    /// live in client asset bundles; the only geometry that ever reaches this
    /// process is what was extracted offline. So grounding is not "ask the ground",
    /// it is "carry the answer".
    ///
    /// WHAT IS CARRIED, and why it is this shape rather than a heightmap. The
    /// extracted surface (docs/research/world-data/island-surfaces/) is a THREE-
    /// dimensional 8 m voxel decimation of the LOD0 vertices, so it is multi-valued
    /// in Y per column and empty over roughly forty per cent of each island's
    /// footprint. It is not a height field and cannot be made into one honestly.
    /// But a log is never born anywhere: it is born at a TREE, and every tree in
    /// this world stands on an authored seat that is itself a measured surface
    /// vertex. So the question is not "what is the ground at an arbitrary point"
    /// but "what does the ground do in the sixteen metres around this seat", which
    /// is 8 bytes per seat instead of a megabyte per island - see
    /// <see cref="GroundProfile"/> and Islands/release-tree-ground-profiles.txt.
    ///
    /// THE RULE IS "REST ON THE HIGH SIDE", and it is one number rather than two.
    /// A profile's rise for a bearing is the MAXIMUM of <c>(y - y0) * reach / t</c>
    /// over every sampled ground point along that bearing, which is precisely the
    /// smallest tilt for which a straight trunk laid from the seat clears every one
    /// of them. That single definition covers every case worth naming:
    /// <list type="bullet">
    /// <item>a uniform slope - every sample has the same ratio, so the trunk lies
    ///   along it and neither end floats;</item>
    /// <item>a bulge in the middle - the bulge wins, and the trunk bridges it with
    ///   its far end in the air, which is what a real trunk does;</item>
    /// <item>a drop-off at the far end - the flat near samples win, and the trunk
    ///   cantilevers out over the edge instead of nose-diving.</item>
    /// </list>
    /// Averaging would bury the trunk at every one of them, and burying is the half
    /// of the complaint that looks worst.
    ///
    /// WHAT THIS DELIBERATELY IS NOT. It is not physics. The log does not slide, does
    /// not roll, does not bounce and does not settle differently twice - the
    /// maintainer asked for none of those and they would all cost a simulation this
    /// server has nowhere to put. It comes to rest sensibly on the ground, once,
    /// and stops.
    ///
    /// IT COSTS NOTHING ON THE WIRE. Both outputs fold into the pose that
    /// <c>FallingLogService</c> already sends: the tilt is the angle the existing
    /// arc ends at, and the lift is the Y of the localPosition that every 190602
    /// already carries. No new component, no new stream, no new rate.
    ///
    /// Pure: no ENet, no Improbable types, no game install, no clock, no I/O.
    /// </summary>
    public static class LogGrounding
    {
        /// <summary>
        /// How far along the ground a profile measures, in metres, and therefore the
        /// baseline every rise is quoted against.
        ///
        /// SIXTEEN METRES, RECONSTRUCTED rather than recovered. A tree's metric
        /// height is not in anything this project extracted - <see cref="TreeTopology"/>
        /// knows how many sections a trunk has and which branch each belongs to, but
        /// not how long any of them is - so the distance a toppled trunk reaches is
        /// not a number that can be looked up. Sixteen metres is the reach of a
        /// mature Worlds Adrift trunk to within the tolerance that matters here,
        /// which is coarse: the value only sets the LEVER ARM the rise is divided by,
        /// so getting it wrong by a quarter changes the resting tilt by a few
        /// degrees, not by a category.
        ///
        /// It is a constant rather than per-species because it must match the number
        /// the offline generator baked with. Changing it means regenerating
        /// Islands/release-tree-ground-profiles.txt, and
        /// <see cref="GroundProfile.ReachMetres"/> is what the file's own header
        /// records so a mismatch is detectable rather than silent.
        /// </summary>
        public const double ReachMetres = GroundProfile.ReachMetres;

        /// <summary>
        /// The most a log may be tilted off flat by grounding, in degrees either way.
        ///
        /// FORTY, AND IT IS A SAFETY RAIL RATHER THAN A STYLE CONTROL - the
        /// distinction decides the number. A clamp tight enough to make steep logs
        /// look gentler is a clamp that lays a log FLATTER than the hill it is on,
        /// and a log flatter than its hill has its far end inside the hill. Burying
        /// the trunk to make it look calmer is the exact defect this whole file
        /// exists to remove, so the rule is: a log may look dramatic, it may not
        /// look buried.
        ///
        /// What the rail is actually for is a data accident. The extracted surface is
        /// an 8 m decimation, so one sample on a boulder or on the lip of a chasm can
        /// imply a slope the surrounding terrain does not have, and unclamped that
        /// stands a log on end. As the profiles are baked TODAY it cannot happen -
        /// <see cref="GroundProfile.DeckBandMetres"/> over
        /// <see cref="GroundProfile.MinDistanceMetres"/> bounds any measurable rise at
        /// about 36.9 degrees, so this clamp never engages on shipped data and the
        /// tilt a log gets is the tilt that was measured. It is set beyond that bound
        /// deliberately, so that a future rebake with a wider band inherits a guard
        /// instead of a surprise.
        /// </summary>
        public const double MaxTiltDegrees = 40.0;

        /// <summary>
        /// How far the log's origin sits ABOVE the ground it rests on, in metres.
        ///
        /// THIS IS A TRUNK RADIUS, not a fudge, and it is the one part of the fix
        /// that helps on dead-flat ground too. A tree's origin is at the centre of
        /// its base, ON the trunk's axis, so a ninety-degree topple lays the AXIS on
        /// the ground - which puts the whole lower half of the trunk underground.
        /// That is a real part of "it clips through" and it has nothing to do with
        /// slope; it is why the lift is applied even when no profile is available.
        ///
        /// Forty centimetres is a mature trunk's radius, RECONSTRUCTED like
        /// <see cref="ReachMetres"/>: prefab bounds are not among the things this
        /// project extracted. It is deliberately a little generous, because the
        /// maintainer's requirement is "fully visible on the floor" - a log a
        /// hand's breadth proud of the ground reads as lying on it, and a log a
        /// hand's breadth into it reads as broken.
        /// </summary>
        public const double DefaultLiftMetres = 0.4;

        /// <summary>
        /// A lift from the operator's <c>WAREBORN_TREE_FALL_LIFT</c> string, in
        /// metres, or null to accept <see cref="DefaultLiftMetres"/>.
        ///
        /// IT IS TUNABLE BECAUSE IT IS RECONSTRUCTED. Every other number here is
        /// either measured or a safety clamp; this one is a guess at a trunk's
        /// girth, and the only instrument that can read it is a person standing next
        /// to a felled log. An environment variable turns "that looks a bit high" into
        /// a restart instead of a build, a deploy and a round trip.
        ///
        /// Zero is ACCEPTED and means "lay the axis exactly on the ground". Negative
        /// and unparseable values fall back rather than throwing, the same rule
        /// <see cref="TreeFall.ParseBudget"/> keeps: a typo in an environment
        /// variable must never stop a server booting.
        /// </summary>
        public static double? ParseLift(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)
                || !double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lift)
                || double.IsNaN(lift)
                || double.IsInfinity(lift)
                || lift < 0.0)
            {
                return null;
            }
            return lift;
        }

        /// <summary>
        /// WHERE A LOG FELLED ON THIS BEARING COMES TO REST.
        ///
        /// The single entry point, and deliberately the only one: a caller that
        /// forgot to ground a log has to have skipped this call, which is a thing a
        /// test can see.
        ///
        /// WHAT "NO MEASURED GROUND" DEGRADES TO, because it is about one bearing in
        /// nine and getting it wrong would reproduce the original complaint on a
        /// tenth of all cuts. <paramref name="profile"/> may be null (an island never
        /// extracted, a tree not on an authored seat) and a profile may be
        /// <see cref="GroundProfile.Unknown"/> on the bearing asked for (the ground
        /// there ran off the island edge, into a decimation gap, or down a face
        /// steeper than the deck band admits). In BOTH cases the answer is
        /// <see cref="TreeFall.FlatRestAngleDegrees"/> WITH the lift - the log lies
        /// flat and fully visible ON the surface at the seat's own height, which is a
        /// measured surface vertex and therefore a true ground height.
        ///
        /// That is emphatically NOT "skip grounding". Skipping would leave the log at
        /// the unlifted pose with its lower half in the dirt, which is the bug. The
        /// unmeasured case gets the weaker half of the fix, never none of it.
        /// </summary>
        public static GroundedRest Rest(GroundProfile? profile, double headingDegrees, double liftMetres)
        {
            double lift = liftMetres >= 0.0 ? liftMetres : DefaultLiftMetres;

            double? rise = profile?.RiseAt(headingDegrees);
            if (rise == null)
            {
                return new GroundedRest(TreeFall.FlatRestAngleDegrees, lift, false);
            }

            return new GroundedRest(RestAngleDegrees(rise.Value), lift, true);
        }

        /// <summary>
        /// The angle a log settles at, measured the way
        /// <see cref="TreeFall.FallAngleDegrees"/> measures it: degrees swung from
        /// standing, so 90 is flat.
        ///
        /// Ground that RISES ahead of the fall stops the trunk SHORT of flat, leaning
        /// up the hill; ground that falls away lets it swing PAST flat and follow the
        /// slope down. Both are the same subtraction, which is why there is no branch
        /// here and no separate uphill case to get wrong.
        /// </summary>
        public static double RestAngleDegrees(double riseMetres)
        {
            double tilt = Math.Atan2(riseMetres, ReachMetres) * 180.0 / Math.PI;
            if (tilt > MaxTiltDegrees)
            {
                tilt = MaxTiltDegrees;
            }
            else if (tilt < -MaxTiltDegrees)
            {
                tilt = -MaxTiltDegrees;
            }

            return TreeFall.FlatRestAngleDegrees - tilt;
        }

        /// <summary>
        /// The log's position raised clear of the ground - the localPosition that
        /// goes on the wire, with the lift folded in.
        ///
        /// Y ONLY. Moving a log sideways to find better ground would put it somewhere
        /// the player did not watch it fall, which is a worse lie than a slightly
        /// wrong height: the whole point of the feature is that the tree you cut
        /// falls where you cut it.
        /// </summary>
        public static FixedPointPosition Raise(FixedPointPosition position, double liftMetres)
        {
            if (liftMetres == 0.0)
            {
                return position;
            }

            return new FixedPointPosition(
                position.X,
                position.Y + (long)(liftMetres * FixedPointPosition.UnitsPerMetre),
                position.Z);
        }

        /// <summary>
        /// Builds a profile for one seat directly from extracted surface samples -
        /// the SAME measurement tools/world-import/generate-tree-ground-profiles.py
        /// bakes, expressed once more in the language the server runs in.
        ///
        /// IT EXISTS FOR TWO REASONS AND THE SECOND IS THE IMPORTANT ONE.
        /// <list type="number">
        /// <item>Haven and the Trades Challenge carry their whole extracted surface
        ///   inside this assembly already, so their trees can be profiled from the
        ///   real thing at boot instead of from a baked row.</item>
        /// <item>Because those two islands have BOTH the surface and a baked row,
        ///   a test can bake-versus-build them against each other. That is the only
        ///   gate that can catch the generator and the server drifting apart, and
        ///   without it the baked file is a number nobody ever checks.</item>
        /// </list>
        ///
        /// <paramref name="samples"/> are island-LOCAL metres, the space the seats
        /// and the extracted surface share; nothing here knows about world fixed
        /// point.
        /// </summary>
        public static GroundProfile FromSamples(
            double seatX, double seatY, double seatZ,
            IReadOnlyList<(double X, double Y, double Z)> samples)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            sbyte[] rises = new sbyte[GroundProfile.Bearings];

            for (int b = 0; b < GroundProfile.Bearings; b++)
            {
                double heading = b * (360.0 / GroundProfile.Bearings) * Math.PI / 180.0;
                double dx = Math.Sin(heading);
                double dz = Math.Cos(heading);

                bool any = false;
                double best = 0.0;

                for (int i = 0; i < samples.Count; i++)
                {
                    (double px, double py, double pz) = samples[i];
                    double ox = px - seatX;
                    double oz = pz - seatZ;

                    double t = ox * dx + oz * dz;
                    if (t < GroundProfile.MinDistanceMetres || t > GroundProfile.ReachMetres)
                    {
                        continue;
                    }
                    if (Math.Abs(ox * dz - oz * dx) > GroundProfile.CorridorMetres)
                    {
                        continue;
                    }

                    double dy = py - seatY;
                    if (Math.Abs(dy) > GroundProfile.DeckBandMetres)
                    {
                        continue;
                    }

                    double ratio = dy * GroundProfile.ReachMetres / t;
                    if (!any || ratio > best)
                    {
                        best = ratio;
                        any = true;
                    }
                }

                rises[b] = any ? GroundProfile.Quantise(best) : GroundProfile.Unknown;
            }

            return new GroundProfile(rises);
        }
    }

    /// <summary>
    /// The pose a log settles into: how far it swung, and how far its origin sits
    /// above the ground.
    /// </summary>
    public readonly struct GroundedRest
    {
        public GroundedRest(double restAngleDegrees, double liftMetres, bool measured)
        {
            RestAngleDegrees = restAngleDegrees;
            LiftMetres = liftMetres;
            Measured = measured;
        }

        /// <summary>
        /// Degrees swung from standing. 90 is flat; less leans up a hill, more
        /// follows a slope down. This is the angle the authored arc ENDS at, not an
        /// extra rotation applied afterwards.
        /// </summary>
        public double RestAngleDegrees { get; }

        /// <summary>Metres the log's origin is lifted, so the trunk is not half buried.</summary>
        public double LiftMetres { get; }

        /// <summary>
        /// Whether the tilt came from measured ground or is just flat-with-a-lift.
        ///
        /// FOR THE LOG LINE, not for the arithmetic - a caller must behave the same
        /// either way. It exists so the live server can say out loud how often it is
        /// guessing, which is the only way anyone will find out that an island's
        /// profiles never shipped.
        /// </summary>
        public bool Measured { get; }

        public override string ToString()
        {
            return "rest=" + RestAngleDegrees.ToString("0.0") + " deg lift="
                + LiftMetres.ToString("0.00") + " m" + (Measured ? " (measured)" : " (flat)");
        }
    }

    /// <summary>
    /// WHAT THE GROUND DOES AROUND ONE TREE SEAT: eight bearings, one signed
    /// decimetre each, and nothing else.
    ///
    /// Each entry is how far the ground has RISEN, relative to the seat, by the time
    /// it is <see cref="ReachMetres"/> away on that bearing - under the high-side
    /// rule spelled out on <see cref="LogGrounding"/>, so it is the rise a straight
    /// trunk must have to clear everything sampled underneath it rather than the
    /// height of any one point.
    ///
    /// EIGHT BYTES PER SEAT is the whole reason this feature is affordable. The
    /// alternative - shipping the extracted surface so the server could sample it -
    /// is twelve to fifteen megabytes of point cloud, of which the interesting part
    /// is the sixteen metres around thirteen thousand seats. Bearings are 45 degrees
    /// apart and <see cref="RiseAt"/> interpolates between them, which is finer than
    /// the 8 m voxel the samples themselves came out of.
    ///
    /// <see cref="Unknown"/> is a first-class answer. Forty per cent of a typical
    /// island's footprint has no extracted sample at all, and a bearing that points
    /// into that emptiness must say so rather than report flat ground - flat is a
    /// measurement, and claiming one we do not have is how a log ends up confidently
    /// inside a cliff.
    /// </summary>
    public readonly struct GroundProfile
    {
        /// <summary>How many bearings a profile carries. 8, so 45 degrees apart.</summary>
        public const int Bearings = 8;

        /// <summary>The distance each rise is measured at, metres. See <see cref="LogGrounding.ReachMetres"/>.</summary>
        public const double ReachMetres = 16.0;

        /// <summary>
        /// Half-width of the corridor a sample must fall inside to count for a
        /// bearing, metres. Four: at the far end the 45-degree sectors are twelve
        /// metres wide, so a four-metre tube is a conservative slice of one bearing
        /// rather than a cone that overlaps its neighbours.
        /// </summary>
        public const double CorridorMetres = 4.0;

        /// <summary>
        /// How far in Y a sample may be from the seat and still be considered the
        /// same ground, metres.
        ///
        /// THE EXTRACTED SURFACE IS MULTI-VALUED PER COLUMN - it was decimated on a
        /// three-dimensional voxel grid, so an island with an overhang, a cave mouth
        /// or a built deck has several Y values above the same spot. Without a band
        /// the roof of a cave twenty-five metres up would be read as a wall the log
        /// must climb.
        ///
        /// SIX METRES, CHOSEN AGAINST MEASURED DISTRIBUTIONS rather than by feel. It
        /// is the value at which no seat in the world rails its byte: together with
        /// <see cref="MinDistanceMetres"/> it bounds any expressible rise at
        /// 6 * 16 / 8 = 12 m, about 36.9 degrees, which is steeper than any slope a
        /// log should be laid along and comfortably inside
        /// <see cref="LogGrounding.MaxTiltDegrees"/>.
        ///
        /// ITS COST IS THE KNOWN LIMITATION OF THIS FEATURE, and it is written down
        /// rather than glossed. A face steeper than roughly forty degrees puts every
        /// sample along a bearing outside the band, so that bearing reads
        /// <see cref="Unknown"/> and the log lies flat at the seat height instead of
        /// along the cliff. It stays ON the surface and fully visible - the seat is a
        /// measured vertex - but it does not follow the plunge. Widening the band to
        /// fix that reintroduces the cave roof; the real fix is a band that grows
        /// with distance, which is a change to the bake and not to this file.
        /// </summary>
        public const double DeckBandMetres = 6.0;

        /// <summary>
        /// How close to the seat a sample may be and still count, metres.
        ///
        /// EIGHT, WHICH IS THE DECIMATION CELL, and the first version of this had it
        /// at one metre - which was wrong in a way worth recording. The rise is
        /// divided by the distance, so a sample at one metre has its height
        /// multiplied by sixteen; and because the surface was thinned to one vertex
        /// per 8 m voxel, a sample that close carries no more real information than
        /// one at eight metres does. The result was 6.4% of all bearings railing at
        /// the byte's limit and a ninetieth percentile of 7.4 m of rise - one felled
        /// log in ten would have tilted up 25 to 38 degrees and stuck out of the
        /// hillside like a ramp, which is visibly worse than the flat log it
        /// replaces. At eight metres the amplification is at most two and nothing
        /// rails at all.
        ///
        /// The general rule it came from: never let an interpolation resolve finer
        /// than the grid the data was sampled on.
        /// </summary>
        public const double MinDistanceMetres = 8.0;

        /// <summary>
        /// The bearing has no measured ground. -128 rather than 0 because it is the
        /// one signed-byte value that cannot be a legitimate clamped rise, so the
        /// sentinel costs no range.
        /// </summary>
        public const sbyte Unknown = -128;

        private readonly sbyte[]? _rises;

        public GroundProfile(sbyte[] rises)
        {
            if (rises == null)
            {
                throw new ArgumentNullException(nameof(rises));
            }
            if (rises.Length != Bearings)
            {
                throw new ArgumentException(
                    "a ground profile carries exactly " + Bearings + " bearings", nameof(rises));
            }

            _rises = rises;
        }

        /// <summary>The raw decimetre rise on one bearing, or <see cref="Unknown"/>.</summary>
        public sbyte RiseDecimetres(int bearing)
        {
            if (_rises == null || bearing < 0 || bearing >= Bearings)
            {
                return Unknown;
            }
            return _rises[bearing];
        }

        /// <summary>Whether any bearing was measured at all.</summary>
        public bool HasAnyMeasurement
        {
            get
            {
                for (int b = 0; b < Bearings; b++)
                {
                    if (RiseDecimetres(b) != Unknown)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Metres of rise on an arbitrary compass bearing, or null if the ground
        /// there was never measured.
        ///
        /// INTERPOLATED between the two bracketing bearings, because a fall heading
        /// is a hash and lands anywhere in the circle; snapping to the nearest of
        /// eight would make a log's tilt jump by up to the whole difference between
        /// neighbours for a bearing change of one degree.
        ///
        /// An UNKNOWN neighbour does not poison the answer, it just stops
        /// contributing: with one side measured the log takes that side's rise, and
        /// only when BOTH are unknown does the bearing have no answer. Falling back
        /// to zero instead would quietly claim flat ground at exactly the places the
        /// extraction has none.
        /// </summary>
        public double? RiseAt(double headingDegrees)
        {
            double step = 360.0 / Bearings;
            double h = headingDegrees % 360.0;
            if (h < 0.0)
            {
                h += 360.0;
            }

            double exact = h / step;
            int low = (int)Math.Floor(exact) % Bearings;
            int high = (low + 1) % Bearings;
            double blend = exact - Math.Floor(exact);

            sbyte a = RiseDecimetres(low);
            sbyte b = RiseDecimetres(high);

            if (a == Unknown && b == Unknown)
            {
                return null;
            }
            if (a == Unknown)
            {
                return b / 10.0;
            }
            if (b == Unknown)
            {
                return a / 10.0;
            }

            return ((a * (1.0 - blend)) + (b * blend)) / 10.0;
        }

        /// <summary>
        /// Metres of rise to the stored decimetre byte, clamped the way the offline
        /// generator clamps it.
        ///
        /// The clamp is at +/-127 rather than +/-128 so that <see cref="Unknown"/>
        /// stays unreachable from real data. +/-12.7 m over sixteen is about 38
        /// degrees, comfortably outside <see cref="LogGrounding.MaxTiltDegrees"/>,
        /// so nothing the clamp discards would have survived the tilt clamp anyway.
        ///
        /// HALF-TO-EVEN, BECAUSE PYTHON'S round() IS. This has to match
        /// tools/world-import/generate-tree-ground-profiles.py exactly or the two
        /// disagree by one decimetre on every value that lands on a midpoint - which
        /// is harmless in the world and fatal to the agreement test that is the only
        /// thing checking the baked file at all. Away-from-zero was the first
        /// version and it cost a debugging round; the fix is not to relax the test.
        /// </summary>
        public static sbyte Quantise(double riseMetres)
        {
            double dm = Math.Round(riseMetres * 10.0, MidpointRounding.ToEven);
            if (dm > 127.0)
            {
                return 127;
            }
            if (dm < -127.0)
            {
                return -127;
            }
            return (sbyte)dm;
        }

        public override string ToString()
        {
            if (_rises == null)
            {
                return "profile(empty)";
            }
            return "profile[" + string.Join(",", _rises) + "]";
        }
    }
}
