using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHAT A SCHOOL HAS TO BE, asserted rather than eyeballed.
    ///
    /// This module is the answer to "the wildlife should move in schools", and the
    /// honest part of that answer is that retail's own grouping CANNOT be replayed
    /// here: a flock was an attractor entity and members solved a five-rule boid
    /// steerer on the UnityWorker, which is an integrator with velocity and live
    /// neighbour transforms. This server's whole restart-reproducible design is that
    /// a pose is a closed form of (creature, seconds). So the cluster shape is a
    /// reconstruction, and these tests pin the properties that make it read as a
    /// school regardless:
    ///
    /// TOGETHER BUT NOT STACKED. Every member must sit inside the cluster radius -
    /// otherwise it is a scattering, not a school - and no two members may share a
    /// point, which is the failure mode a naive "offset by index" formula produces
    /// whenever the count wraps the ring.
    ///
    /// ALIVE. A frozen formation reads as a rigid model, so the cluster must turn
    /// over. Retail's fifth boid rule was a wander with weight 10, so motion within
    /// the group is a recovered property even though its shape is not.
    ///
    /// PURE AND CONTINUOUS. The offsets feed straight into
    /// <see cref="IslandFaunaMovement"/>, which promises both, so a discontinuity or
    /// a hidden clock here would break the whole feature's restart guarantee.
    /// </summary>
    public sealed class IslandFaunaSchoolTests
    {
        private const double Radius = IslandFaunaSchool.MantaSchoolRadiusMetres;
        private const double Vertical = IslandFaunaSchool.MantaSchoolVerticalRadiusMetres;
        private const double Weave = IslandFaunaSchool.WeaveRadiansPerSecond;

        [Fact]
        public void The_cluster_radius_sits_between_the_two_recovered_flock_distances()
        {
            // PROVED constants from the decompiled client, and the only thing that
            // anchors a school's size in metres:
            //   FlockingConductVisualiser: ready to flock inside sqrMagnitude < 100f -> 10 m
            //   FlockVisualiser:           caught up inside Mathf.Pow(15f, 2f)      -> 15 m
            // If somebody later widens the cluster past that, they are no longer
            // making a claim the surviving client supports.
            Assert.InRange(IslandFaunaSchool.MantaSchoolRadiusMetres, 10.0, 15.0);

            // A jelly shoal is deliberately OUTSIDE that range, because retail jellies
            // did not flock at all and the look being reproduced is a diffuse cloud of
            // independent drifters rather than a congregated flock.
            Assert.True(IslandFaunaSchool.JellyShoalRadiusMetres
                > IslandFaunaSchool.MantaSchoolRadiusMetres * 1.5,
                "a jelly shoal must be visibly looser than a manta school");
        }

        [Fact]
        public void Every_member_stays_inside_the_cluster()
        {
            foreach (double seconds in Times())
            {
                for (int i = 0; i < 64; i++)
                {
                    (double x, double y, double z) =
                        IslandFaunaSchool.MemberOffset(i, Radius, Vertical, seconds, Weave);

                    Assert.True(Math.Sqrt((x * x) + (z * z)) <= Radius + 1e-9,
                        "member " + i + " left the school laterally");
                    Assert.True(Math.Abs(y) <= Vertical + 1e-9,
                        "member " + i + " left the school vertically");
                }
            }
        }

        [Fact]
        public void No_two_members_ever_occupy_the_same_point()
        {
            // The failure this catches: an angle of index * (2*pi / N) collides the
            // moment the count differs from the N it was written for. The golden angle
            // cannot collide for any count, and this is the assertion that says so.
            foreach (double seconds in Times())
            {
                (double X, double Y, double Z)[] members = Enumerable.Range(0, 32)
                    .Select(i => IslandFaunaSchool.MemberOffset(i, Radius, Vertical, seconds, Weave))
                    .ToArray();

                for (int a = 0; a < members.Length; a++)
                {
                    for (int b = a + 1; b < members.Length; b++)
                    {
                        double dx = members[a].X - members[b].X;
                        double dy = members[a].Y - members[b].Y;
                        double dz = members[a].Z - members[b].Z;
                        Assert.True(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > 0.5,
                            "members " + a + " and " + b + " are on top of each other at t=" + seconds);
                    }
                }
            }
        }

        [Fact]
        public void The_leading_member_is_not_parked_on_the_schools_own_centre()
        {
            // FlockStateData carries member lists and NO leader field, so a school with
            // one animal sitting exactly on the attractor would be asserting a
            // structure retail did not have.
            foreach (double seconds in Times())
            {
                (double x, double y, double z) =
                    IslandFaunaSchool.MemberOffset(0, Radius, Vertical, seconds, Weave);
                Assert.True(Math.Sqrt((x * x) + (y * y) + (z * z)) > 1.0,
                    "member zero must be a member, not the centre");
            }
        }

        [Fact]
        public void The_cluster_turns_over_instead_of_freezing_into_a_lattice()
        {
            // Retail's fifth boid rule was a wander (weight 10). A closed-form school
            // that held a fixed shape would read as a towed model.
            (double X, double Y, double Z) start =
                IslandFaunaSchool.MemberOffset(3, Radius, Vertical, 0.0, Weave);
            (double X, double Y, double Z) later =
                IslandFaunaSchool.MemberOffset(3, Radius, Vertical, 60.0, Weave);

            double dx = later.X - start.X;
            double dy = later.Y - start.Y;
            double dz = later.Z - start.Z;
            Assert.True(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > 1.0,
                "a member's place in the school must change over a minute");
        }

        [Fact]
        public void The_offset_is_continuous_so_a_member_never_teleports_within_its_school()
        {
            (double X, double Y, double Z) previous =
                IslandFaunaSchool.MemberOffset(5, Radius, Vertical, 0.0, Weave);
            for (double t = 0.25; t <= 600.0; t += 0.25)
            {
                (double X, double Y, double Z) now =
                    IslandFaunaSchool.MemberOffset(5, Radius, Vertical, t, Weave);
                double dx = now.X - previous.X;
                double dy = now.Y - previous.Y;
                double dz = now.Z - previous.Z;
                Assert.True(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) < 1.0,
                    "the cluster offset stepped at t=" + t);
                previous = now;
            }
        }

        [Fact]
        public void The_offset_is_a_pure_function_of_its_arguments()
        {
            foreach (double seconds in Times())
            {
                for (int i = 0; i < 8; i++)
                {
                    (double X, double Y, double Z) first =
                        IslandFaunaSchool.MemberOffset(i, Radius, Vertical, seconds, Weave);
                    for (int repeat = 0; repeat < 4; repeat++)
                    {
                        Assert.Equal(first,
                            IslandFaunaSchool.MemberOffset(i, Radius, Vertical, seconds, Weave));
                    }
                }
            }
        }

        [Fact]
        public void A_negative_member_index_is_a_programming_error_not_a_position()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                IslandFaunaSchool.MemberOffset(-1, Radius, Vertical, 0.0, Weave));
        }

        [Fact]
        public void School_phases_spread_around_the_lap_and_never_collide()
        {
            HashSet<double> seen = new HashSet<double>();
            for (int school = 0; school < 32; school++)
            {
                double phase = IslandFaunaSchool.SchoolPhaseFraction(school);
                Assert.InRange(phase, 0.0, 1.0);
                Assert.True(seen.Add(phase), "school " + school + " shares a phase");
            }

            // Two schools land on opposite halves of the lap rather than together.
            Assert.True(Math.Abs(IslandFaunaSchool.SchoolPhaseFraction(0)
                - IslandFaunaSchool.SchoolPhaseFraction(1)) > 0.25);
        }

        [Fact]
        public void Cluster_shape_differs_in_kind_between_a_school_and_a_shoal()
        {
            (double radius, double vertical) manta =
                IslandFaunaSchool.ClusterFor(FaunaSpecies.MantaRay);
            (double radius, double vertical) jelly =
                IslandFaunaSchool.ClusterFor(FaunaSpecies.JellyFish);

            Assert.Equal(IslandFaunaSchool.MantaSchoolRadiusMetres, manta.radius);
            Assert.Equal(IslandFaunaSchool.JellyShoalRadiusMetres, jelly.radius);

            // Rays travel as a broad flat sheet; a school as tall as it is wide would
            // read as a ball of fish.
            Assert.True(manta.vertical < manta.radius / 2.0,
                "a manta school must be flatter than it is wide");
            Assert.True(jelly.radius > manta.radius && jelly.vertical > manta.vertical);
        }

        [Fact]
        public void Fraction_is_total_and_lands_in_the_unit_interval_for_any_input()
        {
            foreach (double value in new[]
                { 0.0, 0.5, 1.0, 1.5, -0.25, -1.0, -7.75, 1234.5678, -1234.5678 })
            {
                double fraction = IslandFaunaSchool.Fraction(value);
                Assert.InRange(fraction, 0.0, 1.0);
                Assert.True(fraction < 1.0, "a fraction of one turn is never a whole turn");
            }
        }

        private static double[] Times() =>
            new[] { 0.0, 0.25, 1.0, 13.0, 97.5, 600.0, 1200.0, 4321.75 };
    }
}
