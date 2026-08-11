using Improbable;
using Improbable.Collections;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using Improbable.Math;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The ONE place the 190602 TransformState is built for a bolted ship part,
    /// used by BOTH the seed (ComponentsSerializer's 190602 branch, a
    /// <see cref="TransformState.Data"/>) and the wake heartbeat
    /// (<see cref="ShipPartMotionService"/>, a <see cref="TransformState.Update"/>).
    ///
    /// WHY SHARED. The seed and the wake carry the SAME transform - local offset,
    /// identity rotation, the Parent(hullId, "~") relative parent, zero pivot and
    /// velocities, not sleeping - and only the timestamp differs (the seed's 0, the
    /// wake's monotonic stamp). If the two were built in two places they could drift:
    /// a seed with a parent and a wake without it would place the part correctly once
    /// and then snap it to the world origin on the first heartbeat. Building both from
    /// the same field set here makes that impossible.
    ///
    /// The transform maths (which parts, what local offset, at what cadence, what
    /// stamp) lives in the pure Multiplayer policies (<see cref="BoltedPartTransform"/>,
    /// <see cref="ShipPartMotionPolicy"/>); this file is only the game-typed
    /// construction, which cannot live there because these are the client's own types.
    /// </summary>
    internal static class ShipPartTransform
    {
        /// <summary>
        /// The identity-rotation SENTINEL both seed and wake use: the low ten bits all
        /// set. Not "a rotation near identity" - 1 decodes to NaN and the client
        /// rejects a NaN rotation.
        /// </summary>
        private static Quaternion32 IdentityRotation => new Quaternion32(1023);

        /// <summary>
        /// The 190602 parent naming the hull, with the per-part hierarchy
        /// <paramref name="key"/> from <see cref="BoltedPartTransform.HierarchyKeyFor"/>:
        /// <c>"~"</c> (the relative slot) for a position-FOLLOW part the client composes
        /// against the hull's live position each frame, or a real word (the deck) that
        /// makes the client re-parent the part as a genuine Unity CHILD of the hull. The
        /// key is the ONLY difference between "follow" and "become a child"; the entity
        /// id it names is the hull in both cases.
        /// </summary>
        public static Option<Parent> RelativeParent(long hullEntityId, string key)
        {
            return new Option<Parent>(new Parent(new EntityId(hullEntityId), key));
        }

        /// <summary>The fixed-point local offset as the wire vector the client reads.</summary>
        public static FixedPointVector3 LocalPosition(FixedPointPosition offset)
        {
            return new FixedPointVector3(new Improbable.Collections.List<long> { offset.X, offset.Y, offset.Z });
        }

        /// <summary>
        /// The 190602 SEED for one entity: parent PRESENT (Parent(hullId, "~")) for a
        /// bolted part, ABSENT (default) for everything else. Timestamp 0, zero pivot
        /// and velocities, not sleeping - the same values the seed has always carried.
        /// </summary>
        public static TransformState.Data BuildSeed(FixedPointVector3 localPosition, Option<Parent> parent)
        {
            return BuildSeed(localPosition, parent, IdentityRotation);
        }

        /// <summary>
        /// The 190602 seed with an EXPLICIT localRotation. Everything that faces
        /// world-north passes the identity SENTINEL through the overload above; a
        /// DEPLOYED structure whose facing the placing player chose (a shipyard)
        /// passes its own packed <paramref name="rotation"/> here. The rotation is
        /// a <c>Quaternion32</c> and MUST already be a valid packed value - 1023 for
        /// identity, or <c>Placement.Quaternion32Packing.Encode(...)</c> for a real
        /// yaw; a raw 0 decodes to NaN and the client rejects the whole transform.
        /// </summary>
        public static TransformState.Data BuildSeed(FixedPointVector3 localPosition, Option<Parent> parent, Quaternion32 rotation)
        {
            return new TransformState.Data(new TransformStateData(
                localPosition,
                rotation,
                parent,
                new Vector3d(0f, 0f, 0f),
                new Vector3f(0f, 0f, 0f),
                new Vector3f(0f, 0f, 0f),
                false,
                0f));
        }

        /// <summary>
        /// The 190602 WAKE UPDATE for a bolted part: the same transform as the seed
        /// (local offset, identity rotation, Parent(hullId, "~"), zero pivot/velocity,
        /// isSleeping FALSE) with a fresh monotonic timestamp. isSleeping and every
        /// edge field are set explicitly, because the client's writer only puts a field
        /// on the wire when it changes - so a wake that omitted the parent would read as
        /// "parent unchanged", but omitting it here would just mean the update never
        /// asserts the relative parent it depends on. Sent, it fires the part's
        /// PropertyUpdated -> OnTransformChanged -> WakeUp.
        /// </summary>
        public static TransformState.Update BuildWakeUpdate(FixedPointPosition localOffset, long hullEntityId, string key, float timestamp)
        {
            return new TransformState.Update()
                .SetLocalPosition(LocalPosition(localOffset))
                .SetLocalRotation(IdentityRotation)
                .SetParent(RelativeParent(hullEntityId, key))
                .SetPivot(new Vector3d(0f, 0f, 0f))
                .SetVelocity(new Vector3f(0f, 0f, 0f))
                .SetAngularVelocity(new Vector3f(0f, 0f, 0f))
                .SetIsSleeping(false)
                .SetTimestamp(timestamp);
        }
    }
}
