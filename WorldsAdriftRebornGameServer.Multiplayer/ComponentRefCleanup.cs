using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Which stored native client-object references become DEAD - and so must be
    /// handed to <c>ClientObjects.DestroyReference</c> to free their unmanaged
    /// backing - when a peer's slice of the component map is dropped.
    ///
    /// The component map is <c>peer -> entityId -> componentId -> refId</c>. Every
    /// refId in it is a live native reference created by
    /// <c>ClientObjects.CreateReference</c>; a refId that is never destroyed is an
    /// unmanaged leak that grows for the life of the process (there is one per
    /// component per entity per peer, seeded on join).
    ///
    /// This module is pure bookkeeping over that map's shape, kept separate so the
    /// "which refs are dead" decision is unit-testable away from the native
    /// destroy call. The safety contract it encodes: only ever surface a refId
    /// that will NEVER be serialized again. The one caller - ForgetPeer - is
    /// dropping a peer that is GONE, so every refId under it is dead. A refId still
    /// reachable by a connected peer is never returned here, because a peer only
    /// ever sees its own slice.
    /// </summary>
    public static class ComponentRefCleanup
    {
        /// <summary>
        /// Every refId stored under one departed peer's slice. The peer is leaving,
        /// so all of them are dead and safe to destroy. Null-tolerant so the caller
        /// need not distinguish "peer had no slice" from "peer had an empty slice".
        /// </summary>
        public static IEnumerable<ulong> RefsForDepartedPeer(
            Dictionary<long, Dictionary<uint, ulong>>? peerSlice)
        {
            if (peerSlice == null)
            {
                yield break;
            }

            foreach (Dictionary<uint, ulong> byComponent in peerSlice.Values)
            {
                foreach (ulong refId in byComponent.Values)
                {
                    yield return refId;
                }
            }
        }
    }
}
