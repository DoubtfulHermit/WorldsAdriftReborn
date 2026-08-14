using Bossa.Travellers.Motion.Prediction;
using Improbable.Collections;
using Improbable.Corelibrary.Math;
using Improbable.Math;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The one ENet-shaped thing the two ship-motion features share: turn a pure
    /// <see cref="ShipControlPointSpec"/> into a 1130 SSPPredictedMotionState
    /// update and send it to every client that has the hull.
    ///
    /// Both callers - the step-3 carry probe (<see cref="ShipMoveService"/>) and
    /// the step-4 ferry (<see cref="ShipFerryService"/>) - address the SAME
    /// entity id on every client, which is the whole reason a control point sent
    /// per peer lands on one hull rather than N. The id comes from the shared
    /// world-entity registry (<c>WorldEntities.EntityIdFor</c>), the same number
    /// the spawn plan already handed out.
    ///
    /// Each update contains one complete absolute latest control point: timestamp,
    /// global position, rotation and velocity. A later point therefore supersedes
    /// a lost one. <c>ValidateControlPoints</c> only rejects regression or a gap
    /// smaller than 0.228 s; skipping a 0.24 s point widens the next gap to 0.48 s,
    /// which is valid, and <c>PathFollower</c> corrects from its extrapolated pose.
    /// Delivery is consequently UNRELIABLE through <see cref="MirrorSendPolicy"/>,
    /// avoiding reliable-channel head-of-line delay during loss.
    /// </summary>
    internal static class ShipPublisher
    {
        /// <summary>
        /// The hull's entity id and its seed position, or false if the ship has
        /// not been spawned into the world yet.
        ///
        /// Guarded on <c>IsBound</c> and NEVER on a path that could allocate: the
        /// id must be the one the spawn plan chose (EntityIdAllocator hands ids
        /// out on first read from a shared counter, so allocating it here first
        /// would only be safe because it is keyed - but asking, not allocating, is
        /// the honest thing, and before the plan has run there is simply no ship
        /// to move). By the time this runs in the main loop the plan has long
        /// since bound it, so <c>EntityIdFor</c> just returns the cached value.
        /// </summary>
        public static bool TryResolveShip(out long entityId, out FixedPointPosition seed)
        {
            entityId = 0;
            seed = default;

            WorldEntity? ship = WorldsAdriftRebornGameServer.WorldEntities.ByKey(Multiplayer.WorldEntities.ShipFrameKey);
            if (ship == null || !WorldsAdriftRebornGameServer.WorldEntities.IsBound(ship))
            {
                return false;
            }

            entityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(ship);
            seed = ship.Position;
            return true;
        }

        /// <summary>
        /// Builds the 1130 update for one control point.
        ///
        /// Rotation is the identity SENTINEL <c>Quaternion32(1023)</c> - the low
        /// ten bits all set - NOT a rotation that happens to be near identity: 1
        /// decodes to NaN and <c>ControlPoint.ValidateControlPoint</c> rejects a
        /// NaN rotation outright. The fsimIdHash is the one constant marker for
        /// the whole flight (<see cref="ShipHull.FsimIdHash"/>); a change between
        /// points makes the client ignore half a second of motion, a collision
        /// with a client's own WorkerId hash makes it drop them silently.
        /// Position is GLOBAL METRES - the client's <c>Remap()</c> subtracts its
        /// own origin.
        /// </summary>
        public static object BuildUpdate(ShipControlPointSpec spec)
        {
            return BuildUpdate(spec, 1023u);
        }

        /// <summary>
        /// The 1130 update with an EXPLICIT packed rotation - the overload piloted
        /// flight uses so the hull BANKS onto its heading (PathFollower slerps and
        /// MoveRotation()s the rotation between points, VERIFIED at
        /// acs/PathFollower.cs:332-354). The value must already be a valid packed
        /// Quaternion32 - 1023 for identity, or the output of
        /// Quaternion32Packing.Encode / FlightIntegrator.PackedRotation; a raw 0 or 1
        /// decodes to NaN and ControlPoint.ValidateControlPoint rejects the point
        /// silently. Everything else matches the identity overload above.
        /// </summary>
        public static object BuildUpdate(ShipControlPointSpec spec, uint packedRotation)
        {
            ShipControlPoint controlPoint = new ShipControlPoint(
                spec.TimestampMs,
                new Coordinates(spec.X, spec.Y, spec.Z),
                new Quaternion32(packedRotation),
                new Vector3f((float)spec.Vx, (float)spec.Vy, (float)spec.Vz),
                ShipHull.FsimIdHash);

            SSPPredictedMotionState.Update update = new SSPPredictedMotionState.Update();
            update.SetLatestControlPoint(new Option<ShipControlPoint>(controlPoint));
            // extrapolate is left UNSET: an absent field leaves the client on the
            // seed's `false`, and false is what claims the least (the flag has no
            // consumer in the shipped client anyway - PathFollower extrapolates on
            // its own when it runs out of points).
            return update;
        }

        /// <summary>
        /// Sends one already-built 1130 update to every fully-loaded peer, keeping
        /// count. A peer still in its loading screen is skipped: it has not
        /// checked the hull out, so an update would land on a component it does
        /// not have and the client's ComponentUpdateManager would drop it. The
        /// next control point (0.24 s later, for the ferry) reaches it once it is
        /// in.
        /// </summary>
        public static int Broadcast(long entityId, object update)
        {
            return Broadcast(entityId, ShipMotionPolicy.ComponentId, update);
        }

        /// <summary>
        /// Sends one already-built component update on a GIVEN component id to every
        /// fully-loaded peer, keeping count. The hull's 1130 SSPPredictedMotionState
        /// and each bolted part's 190602 TransformState wake both ride this same
        /// superseding-update path (see <see cref="ShipPartMotionService"/>); only the id and the
        /// target entity differ.
        /// </summary>
        public static int Broadcast(long entityId, uint componentId, object update)
        {
            int sent = 0;
            foreach ((ulong peerId, long _) in WorldsAdriftRebornGameServer.Players.All())
            {
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
                if (peer == null || !PeerManager.Instance.clientSetupState.Contains(peer))
                {
                    continue;
                }

                if (SendOPHelper.SendComponentUpdateOp(
                        peer,
                        entityId,
                        new System.Collections.Generic.List<uint> { componentId },
                        new System.Collections.Generic.List<object> { update }))
                {
                    sent++;
                }
            }
            return sent;
        }

        /// <summary>
        /// Publishes high-frequency motion only to peers that both hold the target
        /// entity and are near the ship (or piloting/aboard it). Event updates keep
        /// using <see cref="Broadcast(long,uint,object)"/>; this gate is specifically
        /// for the 1130/190602 stream. Without it, one abandoned ship cruising tens
        /// of kilometres away sent its hull and every mounted-part wake to every
        /// player forever.
        /// </summary>
        public static int BroadcastMotion(long targetEntityId, long hullEntityId,
            FixedPointPosition hullPosition, uint componentId, object update)
        {
            int sent = 0;
            foreach ((ulong peerId, long playerEntityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
                if (peer == null || !PeerManager.Instance.clientSetupState.Contains(peer))
                {
                    continue;
                }

                bool checkedOut = WorldsAdriftRebornGameServer.SentEntities
                    .WasSent(peer, targetEntityId)
                    && WorldsAdriftRebornGameServer.ServedComponents
                        .HasServed(peer, targetEntityId, componentId);
                bool pilot = WorldsAdriftRebornGameServer.Flight
                    .IsPilotOf(playerEntityId, hullEntityId);
                bool aboard = WorldsAdriftRebornGameServer.Aboard.ShipOf(peerId) == hullEntityId;
                FixedPointPosition center = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);

                if (!ShipUpdateVisibilityPolicy.ShouldPublish(
                        checkedOut, pilot, aboard, center, hullPosition, Interest.RadiusMetres))
                {
                    continue;
                }

                if (SendOPHelper.SendComponentUpdateOp(
                        peer,
                        targetEntityId,
                        new System.Collections.Generic.List<uint> { componentId },
                        new System.Collections.Generic.List<object> { update }))
                {
                    sent++;
                }
            }
            return sent;
        }
    }
}
