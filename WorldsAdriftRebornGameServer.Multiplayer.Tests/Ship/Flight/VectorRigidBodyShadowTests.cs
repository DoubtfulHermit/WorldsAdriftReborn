using System;
using System.Collections.Generic;
using System.Diagnostics;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class VectorRigidBodyShadowTests
    {
        private static readonly ShadowVector3 HullHalfExtents = new ShadowVector3(4.0, 1.5, 8.0);

        [Fact]
        public void SymmetricOffCentreEnginesCancelTorque()
        {
            var parts = new[]
            {
                EngineAt(-2.0, 0.0, 1400.0),
                EngineAt( 2.0, 0.0, 1400.0)
            };

            Assert.True(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 1.0, ShadowVector3.Zero, out var result));

            VectorNear(new ShadowVector3(0.0, 0.0, 2800.0), result.ForceNewtons);
            VectorNear(ShadowVector3.Zero, result.RawTorqueNewtonMetres);
            VectorNear(ShadowVector3.Zero, result.RetailTorqueNewtonMetres);
        }

        [Fact]
        public void RightMountedForwardEngineProducesNegativeYawTorque()
        {
            var parts = new[] { EngineAt(2.0, 0.0, 2000.0) };

            Assert.True(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 1.0, ShadowVector3.Zero, out var result));

            Assert.Equal(-4000.0, result.RawTorqueNewtonMetres.Y, 9);
            // Mutation guard for recovered order/sign: cross(position-COM, force),
            // then 2500 dead-zone and 0.5 scale: (-4000+2500)*0.5 = -750.
            Assert.Equal(-750.0, result.RetailTorqueNewtonMetres.Y, 9);
            Assert.Equal(0.0, result.RetailTorqueNewtonMetres.X);
        }

        [Fact]
        public void TorquelessEngineStillContributesLinearForce()
        {
            var parts = new[]
            {
                new ShadowPropulsor(ShadowPartKind.Engine, new ShadowVector3(25.0, 7.0, 0.0),
                    ShadowQuaternion.Identity, 2000.0, 0.0, torqueless: true)
            };

            Assert.True(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 1.0, ShadowVector3.Zero, out var result));
            Assert.Equal(2000.0, result.ForceNewtons.Z, 9);
            VectorNear(ShadowVector3.Zero, result.RawTorqueNewtonMetres);
        }

        [Theory]
        [InlineData(0, 0.0, 1.0)]
        [InlineData(45, 0.7071067811865475, 0.7071067811865475)]
        [InlineData(90, 1.0, 0.0)]
        [InlineData(135, 0.7071067811865475, -0.7071067811865475)]
        [InlineData(180, 0.0, -1.0)]
        [InlineData(225, -0.7071067811865475, -0.7071067811865475)]
        [InlineData(270, -1.0, 0.0)]
        [InlineData(315, -0.7071067811865475, 0.7071067811865475)]
        public void EngineDirectionCoversEightHeadings(double degrees, double expectedX, double expectedZ)
        {
            ShadowQuaternion rotation = ShadowQuaternion.FromAxisAngle(
                ShadowVector3.Up, degrees * Math.PI / 180.0);
            var engine = new ShadowPropulsor(ShadowPartKind.Engine, ShadowVector3.Zero,
                rotation, 1000.0, 0.0);

            ShadowVector3 force = VectorRigidBodyShadow.EngineForce(engine, 1.0);

            Assert.Equal(expectedX * 1000.0, force.X, 9);
            Assert.Equal(0.0, force.Y, 9);
            Assert.Equal(expectedZ * 1000.0, force.Z, 9);
        }

        [Fact]
        public void MirroredSailsProduceEqualForwardForceAndCancelYawTorque()
        {
            double quarterTurn = Math.PI / 4.0;
            var parts = new[]
            {
                new ShadowPropulsor(ShadowPartKind.Sail, new ShadowVector3(-2.0, 0.0, 0.0),
                    ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, quarterTurn), 100.0, 10.0),
                new ShadowPropulsor(ShadowPartKind.Sail, new ShadowVector3( 2.0, 0.0, 0.0),
                    ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, -quarterTurn), 100.0, 10.0)
            };

            Assert.True(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 0.0, ShadowVector3.Forward, out var result));

            Assert.Equal(100.0, result.ForceNewtons.Z, 9);
            Assert.Equal(0.0, result.ForceNewtons.X, 9);
            Assert.Equal(0.0, result.RawTorqueNewtonMetres.Y, 9);
        }

        [Fact]
        public void ExactCalmUsesRetailForwardUnitWindFallback()
        {
            var sail = new ShadowPropulsor(ShadowPartKind.Sail, ShadowVector3.Zero,
                ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, Math.PI / 4.0), 100.0, 10.0);
            ShadowVector3 force = VectorRigidBodyShadow.SailForce(sail, ShadowVector3.Zero);
            Assert.Equal(50.0, force.Z, 9);
            Assert.Equal(0.0, force.X, 9);
            Assert.Equal(0.0, force.Y, 9);
        }

        [Fact]
        public void WeakWindIsNormalisedToRetailMinimumStrength()
        {
            var sail = new ShadowPropulsor(ShadowPartKind.Sail, ShadowVector3.Zero,
                ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, Math.PI / 4.0), 100.0, 10.0);
            ShadowVector3 weak = VectorRigidBodyShadow.SailForce(sail, ShadowVector3.Forward * 0.1);
            ShadowVector3 unit = VectorRigidBodyShadow.SailForce(sail, ShadowVector3.Forward);
            Assert.Equal(unit, weak);
        }

        [Fact]
        public void MassPropertiesIncludeMountedMassCentreAndParallelAxisInertia()
        {
            var parts = new[] { EngineAt(2.0, 0.0, 1400.0, massKg: 200.0) };

            Assert.True(ShadowMassProperties.TryEstimate(
                800.0, HullHalfExtents, parts, out var mass));

            Assert.Equal(1000.0, mass.TotalMassKg, 9);
            Assert.Equal(0.4, mass.CentreOfMass.X, 9);
            Assert.True(mass.DiagonalInertiaKgM2.X > 0.0);
            Assert.True(mass.DiagonalInertiaKgM2.Y > mass.DiagonalInertiaKgM2.X);
            Assert.True(mass.IsApproximation);
        }

        [Fact]
        public void DifferentMassAndPartConfigurationsStayFinite()
        {
            foreach (double hullMass in new[] { 1.0, 800.0, 3000.0, 1000000.0 })
            {
                foreach (int count in new[] { 0, 1, 16, 128, 256 })
                {
                    var parts = new List<ShadowPropulsor>();
                    for (int i = 0; i < count; i++)
                    {
                        parts.Add(EngineAt((i % 16) - 7.5, i % 3, 1400.0, 5.0));
                    }
                    Assert.True(VectorRigidBodyShadow.TryEvaluate(
                        hullMass, HullHalfExtents, parts, 0.75, ShadowVector3.Zero, out var result));
                    Assert.True(result.ForceNewtons.IsFinite);
                    Assert.True(result.RawTorqueNewtonMetres.IsFinite);
                    Assert.True(result.Mass.DiagonalInertiaKgM2.IsFinite);
                }
            }
        }

        [Fact]
        public void IdenticalReplayIsBitDeterministic()
        {
            var parts = new[]
            {
                EngineAt(-1.25, 2.5, 1234.5, 12.0),
                new ShadowPropulsor(ShadowPartKind.Sail, new ShadowVector3(3.0, 1.0, -4.0),
                    ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, 0.73), 456.7, 8.0)
            };

            Assert.True(VectorRigidBodyShadow.TryEvaluate(
                987.6, HullHalfExtents, parts, 0.321, new ShadowVector3(1.0, 0.0, -2.0), out var first));
            for (int i = 0; i < 1000; i++)
            {
                Assert.True(VectorRigidBodyShadow.TryEvaluate(
                    987.6, HullHalfExtents, parts, 0.321, new ShadowVector3(1.0, 0.0, -2.0), out var replay));
                Assert.Equal(first.ForceNewtons, replay.ForceNewtons);
                Assert.Equal(first.RawTorqueNewtonMetres, replay.RawTorqueNewtonMetres);
                Assert.Equal(first.Mass.CentreOfMass, replay.Mass.CentreOfMass);
                Assert.Equal(first.Mass.DiagonalInertiaKgM2, replay.Mass.DiagonalInertiaKgM2);
            }
        }

        [Theory]
        [InlineData(double.NaN, 0, 0)]
        [InlineData(double.PositiveInfinity, 0, 0)]
        [InlineData(257, 0, 0)]
        [InlineData(0, double.NaN, 0)]
        public void InvalidMountTransformsAreRejected(double x, double y, double z)
        {
            var parts = new[]
            {
                new ShadowPropulsor(ShadowPartKind.Engine, new ShadowVector3(x, y, z),
                    ShadowQuaternion.Identity, 1400.0, 1.0)
            };
            Assert.False(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 1.0, ShadowVector3.Zero, out _));
        }

        [Fact]
        public void InvalidQuaternionIsRejectedByFactoryInsteadOfBecomingNonFinite()
        {
            Assert.False(ShadowQuaternion.TryNormalized(0.0, 0.0, 0.0, 0.0, out var zero));
            Assert.False(ShadowQuaternion.TryNormalized(double.NaN, 0.0, 0.0, 0.0, out var nan));
            Assert.Equal(ShadowVector3.Forward, zero.Rotate(ShadowVector3.Forward));
            Assert.Equal(ShadowVector3.Forward, nan.Rotate(ShadowVector3.Forward));
        }

        [Fact]
        public void Default_zero_quaternion_and_unknown_part_kind_fail_closed()
        {
            var zeroRotation = new[]
            {
                new ShadowPropulsor(ShadowPartKind.Engine, ShadowVector3.Zero,
                    default, 100.0, 1.0)
            };
            var unknownKind = new[]
            {
                new ShadowPropulsor((ShadowPartKind)99, ShadowVector3.Zero,
                    ShadowQuaternion.Identity, 100.0, 1.0)
            };

            Assert.False(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, zeroRotation, 1.0, ShadowVector3.Zero, out _));
            Assert.False(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, unknownKind, 1.0, ShadowVector3.Zero, out _));
        }

        [Fact]
        public void PartCountCapIsEnforcedBeforeIteration()
        {
            var parts = new List<ShadowPropulsor>();
            for (int i = 0; i <= VectorRigidBodyShadowPolicy.MaxParts; i++)
            {
                parts.Add(EngineAt(0.0, 0.0, 1.0));
            }
            Assert.False(VectorRigidBodyShadow.TryEvaluate(
                800.0, HullHalfExtents, parts, 1.0, ShadowVector3.Zero, out _));
        }

        [Fact]
        public void ComparisonRecordPreservesScalarAndVectorAxes()
        {
            var scalar = new ShipForceEvaluation(10.0, default, 800.0, 2, 0.0,
                1000.0, 500.0, 1.875, 2.0, 12.0);
            var mass = new ShadowMassProperties(800.0, ShadowVector3.Zero,
                new ShadowVector3(1.0, 1.0, 1.0), true);
            var shadow = new VectorRigidBodyShadowResult(mass,
                new ShadowVector3(25.0, -10.0, 1475.0), ShadowVector3.Zero,
                ShadowVector3.Zero, 2, 0);

            ForceModelComparison comparison = VectorRigidBodyShadow.Compare(scalar, shadow);

            Assert.Equal(1500.0, comparison.ScalarTotalNewtons);
            Assert.Equal(1475.0, comparison.ShadowForwardNewtons);
            Assert.Equal(25.0, comparison.ShadowLateralNewtons);
            Assert.Equal(-10.0, comparison.ShadowVerticalNewtons);
            Assert.Equal(-25.0, comparison.ForwardDeltaNewtons);
        }

        [Fact]
        public void MaximumLegalShipShadowEvaluationHasBoundedCost()
        {
            var parts = new List<ShadowPropulsor>();
            for (int i = 0; i < VectorRigidBodyShadowPolicy.MaxParts; i++)
            {
                parts.Add(EngineAt((i % 16) - 8.0, (i % 7) - 3.0, 1400.0, 2.0));
            }

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
            {
                Assert.True(VectorRigidBodyShadow.TryEvaluate(
                    3000.0, HullHalfExtents, parts, 0.5, ShadowVector3.Zero, out _));
            }
            stopwatch.Stop();

            // Broad regression tripwire, not a micro-benchmark: 512k part visits
            // should be comfortably below this even on shared CI hardware.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), stopwatch.Elapsed.ToString());
        }

        [Fact]
        public void RecoveredTorqueConstantsCannotDriftUnnoticed()
        {
            Assert.Equal(2500.0, VectorRigidBodyShadowPolicy.RetailTorqueDeadZoneNewtonMetres);
            Assert.Equal(0.5, VectorRigidBodyShadowPolicy.RetailTorqueScale);
            Assert.Equal(0.3, ShipForceModel.SailMinEfficiency, 12);
            Assert.Equal(1.0, ShipForceModel.ShipThrustMultiplier, 12);
        }

        private static ShadowPropulsor EngineAt(double x, double y, double power, double massKg = 0.0) =>
            new ShadowPropulsor(ShadowPartKind.Engine, new ShadowVector3(x, y, 0.0),
                ShadowQuaternion.Identity, power, massKg);

        private static void VectorNear(ShadowVector3 expected, ShadowVector3 actual, int precision = 9)
        {
            Assert.Equal(expected.X, actual.X, precision);
            Assert.Equal(expected.Y, actual.Y, precision);
            Assert.Equal(expected.Z, actual.Z, precision);
        }
    }
}
