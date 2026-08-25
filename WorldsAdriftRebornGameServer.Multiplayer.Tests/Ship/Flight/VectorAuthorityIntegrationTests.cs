using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The full pure chain the service glue composes for a promoted hull: the
    /// hull's OWN FixedFlightClock feeds publication slices, the vector runtime
    /// consumes the accepted steps, the adapter mints the stamps and owns the
    /// pose, the session adopts the projection and makes the one emission
    /// decision. These are the production types the glue calls - no test
    /// reimplementation of any of them.
    /// </summary>
    public sealed class VectorAuthorityIntegrationTests
    {
        private static readonly FlightTuning Tuning = new FlightTuning();
        private const long HullEntityId = 4200;
        private const int PersistentIndex = 3;

        private static FlightRuntimeFlags PromotingFlags => FlightRuntimeFlags.Parse(
            "1", PersistentIndex.ToString(), "1",
            fixedStepEnabled: true, forceModelEnabled: true);

        private static ShadowMassProperties Mass() => new ShadowMassProperties(
            1000.0, ShadowVector3.Zero, new ShadowVector3(1e5, 1e5, 1e5), true);

        private static VectorFlightStepInput StepInput() => new VectorFlightStepInput(
            "ship:4200", FixedFlightClock.StepSeconds, Mass(),
            new ShadowVector3(2.0, 1.5, 6.0),
            new[]
            {
                new ShadowPropulsor(ShadowPartKind.Engine, ShadowVector3.Zero,
                    ShadowQuaternion.Identity, 1400.0, 58.5),
            },
            Array.Empty<VectorWingSurface>(), 1.0, ShadowVector3.Zero,
            new FlightControlInput(1f, 0f, 0f, 0f, 0f),
            new LiftRuntimeStepPolicy(ShipLiftPolicy.SeededTotalLiftKg,
                GravityParameter.UnityDefaultApproximation, false));

        private static void RunSlices(FixedFlightStepBatch batch, ShipDomain domain,
            FlightAuthorityAdapter adapter, FlightSession session, long nowMs,
            LiftCapacityPlan plan, List<FlightEmit> emits)
        {
            foreach (FixedFlightPublicationSlice slice in
                FixedFlightPublicationSchedule.Slice(batch))
            {
                for (int i = 0; i < slice.Steps; i++)
                {
                    VectorFlightStepResult result = adapter.Vector!.Step(StepInput());
                    Assert.True(adapter.TryCommitVector(slice.FirstStep + i,
                        domain.Generation.Value, result, plan));
                }
                FlightEmit emit = session.AdvanceAdopted(nowMs,
                    ShipMotionPolicy.SendIntervalSeconds, slice.Steps,
                    VectorFlightRuntime.Project(adapter.Vector!.State), Tuning,
                    emitDue: slice.PublishAfter, phaseLockedEmit: true);
                if (emit.Emit) emits.Add(emit);
            }
        }

        [Fact]
        public void Publication_carries_only_the_hull_clocks_accepted_steps_and_the_adapters_pose()
        {
            var clock = new FixedFlightClock();
            clock.Advance(TimeSpan.Zero);
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));
            session.Man();
            var domain = new ShipDomain(HullEntityId, PersistentIndex, session);
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                PromotingFlags, PersistentIndex, session.State);
            Assert.Equal(FlightAuthorityMode.VectorAuthority, adapter.Mode);
            var emits = new List<FlightEmit>();

            FixedFlightStepBatch batch = clock.Advance(TimeSpan.FromMilliseconds(240));
            RunSlices(batch, domain, adapter, session, 1000, default, emits);

            // Exactly one publication for twelve accepted steps, stamped with the
            // clock's own completed-step counter and the live generation.
            Assert.Single(emits);
            Assert.Equal(batch.CompletedSteps, adapter.LastStamp.FixedStep);
            Assert.Equal(domain.Generation.Value, adapter.LastStamp.AuthorityGeneration);
            // And the emitted spec IS the committed pose - no second stream.
            AuthoritativeFlightPose pose = adapter.CurrentPose;
            Assert.Equal(pose.X, emits[0].Spec.X);
            Assert.Equal(pose.Y, emits[0].Spec.Y);
            Assert.Equal(pose.Z, emits[0].Spec.Z);
            Assert.Equal(pose.VxMps, emits[0].Spec.Vx);
            Assert.Equal(pose.VyMps, emits[0].Spec.Vy);
            Assert.Equal(pose.VzMps, emits[0].Spec.Vz);
        }

        [Fact]
        public void Restart_advances_the_epoch_rejects_stale_evidence_and_resumes_deterministically()
        {
            // ---- before the restart: fly a while and persist.
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));
            session.Man();
            var domain = new ShipDomain(HullEntityId, PersistentIndex, session);
            ShipAuthorityToken preRestartToken = domain.AcquirePilot(7, 8);
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                PromotingFlags, PersistentIndex, session.State);
            for (int i = 0; i < 50; i++)
            {
                adapter.Vector!.Step(StepInput());
                Assert.True(adapter.TryCommitVector(i + 1, domain.Generation.Value,
                    default, default));
            }
            FlightAuthorityStamp preRestartStamp = adapter.LastStamp;
            FlightState persistedPose = VectorFlightRuntime.Project(adapter.Vector!.State);
            DurableShipFlightSnapshot durable = DurableShipFlightSnapshot.Capture(
                persistedPose, session.Input, domain.Generation.Value, wasManned: true,
                aboardCount: 1, wasDocked: false, unfurledSailCount: 0);
            durable.Vector = adapter.CaptureVector();

            // ---- the restart: the production restore path.
            Assert.True(durable.TryRead(out FlightState restoredPose, out _));
            ShipDomain restoredDomain = ShipDomain.RestoreAfterProcessRestart(
                HullEntityId, PersistentIndex,
                new AuthorityGeneration(durable.AuthorityGeneration),
                new FlightSession(restoredPose));
            Assert.True(restoredDomain.Generation.Value > preRestartStamp.AuthorityGeneration);
            // The pre-restart pilot token is dead; stale input cannot move the hull.
            Assert.False(restoredDomain.TrySetInput(preRestartToken,
                new FlightControlInput(1f, 0f, 0f, 0f, 0f)));
            Assert.Null(restoredDomain.Pilot);

            Assert.True(durable.Vector!.TryRead(restoredPose,
                out VectorFlightState restoredVector));
            FlightAuthorityAdapter restoredAdapter = FlightAuthorityAdapter.For(
                PromotingFlags, PersistentIndex, restoredPose, restoredVector);

            // ---- stale evidence from the old epoch fails closed on the new adapter.
            Assert.True(restoredAdapter.TryCommitVector(1,
                restoredDomain.Generation.Value, default, default));
            Assert.False(restoredAdapter.TryCommitVector(2,
                preRestartStamp.AuthorityGeneration, default, default));

            // ---- and the physics resumes the exact uninterrupted trajectory.
            var uninterrupted = new VectorFlightRuntime(adapter.Vector!.State);
            for (int i = 0; i < 50; i++)
            {
                uninterrupted.Step(StepInput());
                restoredAdapter.Vector!.Step(StepInput());
            }
            Assert.Equal(uninterrupted.State.VelocityMps.Z,
                restoredAdapter.Vector!.State.VelocityMps.Z, 9);
            Assert.Equal(uninterrupted.State.CommandLiftForceNewtons,
                restoredAdapter.Vector!.State.CommandLiftForceNewtons, 9);
        }

        [Fact]
        public void The_observer_shadow_measures_divergence_without_perturbing_the_scalar_path()
        {
            // The observer rollout's contract: over IDENTICAL inputs the scalar
            // production path runs bit-identically whether or not the vector
            // shadow is measured beside it, and the divergence between the two
            // models is a finite, recorded number - not a behavior change.
            var input = new FlightControlInput(1f, 0f, 0f, 0.4f, 0f);
            FlightSession Scalar()
            {
                var s = new FlightSession(FlightState.AtRestAt(0, 300, 0));
                s.Man();
                s.SetInput(input);
                return s;
            }
            FlightSession alone = Scalar();
            FlightSession observed = Scalar();
            var propulsion = new ShipPropulsion(1000.0, 1400.0, 0);

            long nowMs = 1000;
            double baseTime = 100.0;
            for (int slice = 0; slice < 8; slice++)
            {
                FlightState preSlice = observed.State;
                alone.AdvanceFixed(nowMs, ShipMotionPolicy.SendIntervalSeconds, 12,
                    baseTime, Tuning, 0, 1.0, propulsion, null, null,
                    emitDue: true, phaseLockedEmit: true);
                observed.AdvanceFixed(nowMs, ShipMotionPolicy.SendIntervalSeconds, 12,
                    baseTime, Tuning, 0, 1.0, propulsion, null, null,
                    emitDue: true, phaseLockedEmit: true);

                // The re-anchored shadow, exactly as the glue runs it.
                var shadow = new VectorFlightRuntime(
                    VectorFlightRuntime.FromFlightState(preSlice));
                for (int i = 0; i < 12; i++) shadow.Step(StepInput());
                FlightState vector = VectorFlightRuntime.Project(shadow.State);
                double positionDelta = Math.Sqrt(
                    Math.Pow(vector.X - observed.State.X, 2)
                    + Math.Pow(vector.Y - observed.State.Y, 2)
                    + Math.Pow(vector.Z - observed.State.Z, 2));
                Assert.True(double.IsFinite(positionDelta));
                // Re-anchored per slice, the two models cannot drift far in 240 ms.
                Assert.True(positionDelta < 5.0,
                    "one-slice scalar-vs-vector divergence exploded: " + positionDelta);

                nowMs += 240;
                baseTime += 0.24;
            }

            Assert.Equal(alone.State.X, observed.State.X);
            Assert.Equal(alone.State.Z, observed.State.Z);
            Assert.Equal(alone.State.YawRadians, observed.State.YawRadians);
            Assert.Equal(alone.State.VzMps, observed.State.VzMps);
        }

        [Fact]
        public void Rollback_to_scalar_keeps_flying_the_same_pose_under_a_fresh_epoch()
        {
            // The vector epoch flew the hull somewhere.
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));
            session.Man();
            FlightAuthorityAdapter vectorAdapter = FlightAuthorityAdapter.For(
                PromotingFlags, PersistentIndex, session.State);
            for (int i = 0; i < 24; i++) vectorAdapter.Vector!.Step(StepInput());
            FlightState flownPose = VectorFlightRuntime.Project(vectorAdapter.Vector!.State);

            // The operator removes the hull index; the restart restores the pose
            // and advances the generation; the adapter comes back scalar.
            FlightRuntimeFlags rolledBack = FlightRuntimeFlags.Parse("1", "", null,
                fixedStepEnabled: true, forceModelEnabled: true);
            ShipDomain restored = ShipDomain.RestoreAfterProcessRestart(
                HullEntityId, PersistentIndex, new AuthorityGeneration(5),
                new FlightSession(flownPose));
            FlightAuthorityAdapter scalarAdapter = FlightAuthorityAdapter.For(
                rolledBack, PersistentIndex, flownPose);

            Assert.Equal(FlightAuthorityMode.Scalar, scalarAdapter.Mode);
            Assert.True(scalarAdapter.TryCommitScalar(1, restored.Generation.Value,
                restored.Flight.State));
            // Same pose, new epoch: nothing minted under the vector epoch survives.
            Assert.Equal(flownPose.Z, scalarAdapter.CurrentPose.Z);
            Assert.Equal(6, scalarAdapter.CurrentPose.Stamp.AuthorityGeneration);
        }
    }
}
