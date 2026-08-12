using Bossa.Travellers.Motion.Prediction;
using Improbable.Collections;
using Improbable.Corelibrary.Math;
using Improbable.Math;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The one ENet-shaped thing the two ship-motion features share: turn a pure
    /// <see cref="ShipControlPointSpec"/> into a 1130 SSPPredictedMotionState
    /// update and RELIABLY send it to every client that has the hull.
    ///
    /// Both callers - the step-3 carry probe (<see cref="ShipMoveService"/>) and
    /// the step-4 ferry (<see cref="ShipFerryService"/>) - address the SAME
    /// entity id on every client, which is the whole reason a control point sent
    /// per peer lands on one hull rather than N. The id comes from the shared
    /// world-entity registry (<c>WorldEntities.EntityIdFor</c>), the same number
    /// the spawn plan already handed out.
    ///
    /// RELIABLE, not the movement relay's unreliable path: a ship control point is
    /// NOT superseded every tick the way a player's 190602 is - each one is a
    /// distinct step of the flight, and <c>ValidateControlPoints</c> rejects a
    /// point that arrives out of order, so a dropped or reordered one is a visible
    /// stutter that never self-heals. <c>SendComponentUpdateOp</c> sends on the
    /// COMPONENT_UPDATE_OP channel with <c>ENetPacketFlag.RELIABLE</c>, which is
    /// exactly what we want here.
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
            ShipControlPoint controlPoint = new ShipControlPoint(
                spec.TimestampMs,
                new Coordinates(spec.X, spec.Y, spec.Z),
                new Quaternion32(1023),
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
        /// reliable path (see <see cref="ShipPartMotionService"/>); only the id and the
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
    }
}
