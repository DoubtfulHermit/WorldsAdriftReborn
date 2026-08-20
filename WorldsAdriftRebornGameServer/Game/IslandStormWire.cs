using Bossa.Travellers.Loot;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE UNDERSTORM ON THE WIRE. The impure half of
    /// <see cref="IslandStormService"/>, <see cref="IslandStormPolicy"/> and
    /// <see cref="IslandStormPush"/>: it resolves an island's entity id, puts one
    /// 1254 update in front of the peers holding that component, and calls the
    /// world resource reset that already existed.
    ///
    /// Everything that DECIDES anything lives next door in the pure assembly and is
    /// unit-tested there. This file is wiring, and it is written to the same two
    /// rules as <c>PushTreeSectionMask</c>, both of which have cost this project a
    /// debugging round when broken:
    ///
    /// 1. The update goes to each peer DIRECTLY, never through
    ///    <c>RelayToOtherPlayers</c>. That method substitutes the SENDER's entity id
    ///    for the address, so an island's timer would arrive addressed to whichever
    ///    player happened to be moving and the island would never storm on anyone's
    ///    screen.
    /// 2. It sends ONLY the fields that changed, never the whole-component form.
    ///    1254's whole-component update sets all SEVEN properties - and one of them is
    ///    <c>isLightningActive</c>, whose client-side setter is the island-drop
    ///    hazard below. Sending the whole component would arm it on every push.
    ///
    /// ⚠ THE <c>isLightningActive</c> SETTER IS NEVER CALLED FROM THIS FILE, OR
    /// FROM ANYWHERE ELSE IN THIS SERVER.
    /// <c>IslandLocalTransformBehaviour.HandleLightningActiveUpdated(true)</c>
    /// answers a rising <c>isLightningActive</c> by writing the island's transform
    /// to <c>GetEndOfWorldPosition()</c> - End-of-the-World doomsday code that lerps
    /// Y toward −250..−1500 m (PROVED,
    /// <c>acs/Bossa.Travellers.Visualisers.Islands/IslandLocalTransformBehaviour.cs:46-52</c>).
    /// The bool buys nothing: the visualiser that actually draws a storm switches on
    /// <c>EstimatedMilliTillLightningEnd &gt; 0</c>, an INT (PROVED, <c>:226</c>).
    /// <see cref="IslandStormUpdate"/> cannot express the bool, this file does not
    /// mention it, and <c>IslandStormWiringTests</c> reads this source off disk and
    /// goes red if it ever does.
    /// </summary>
    internal static class IslandStormWire
    {
        /// <summary>1254 <c>IslandLightningTimerState</c>, namespace Bossa.Travellers.Loot.</summary>
        internal const uint IslandLightningTimerStateComponentId = 1254;

        /// <summary>
        /// The live implementation handed to <see cref="IslandStormService"/>.
        /// A class rather than lambdas so the wiring test has something to name.
        /// </summary>
        internal sealed class Wire : IIslandStormWire
        {
            public long? IslandEntityId(string islandId)
            {
                Multiplayer.Islands.IslandDefinition? island =
                    WorldsAdriftRebornGameServer.IslandTopology.ById(
                        new Multiplayer.Islands.IslandId(islandId));
                if (island == null) return null;

                // BoundEntityIdFor, never EntityIdFor: asking where an island is
                // must not be what allocates its id. Null until its AddEntityOp has
                // run, which is exactly "no client has it yet".
                return WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(island.WorldEntityKey);
            }

            public int PushTimer(long islandEntityId, IslandStormUpdate update) =>
                Push(islandEntityId, update);

            public string ResetWorldResources()
            {
                string summary = WorldsAdriftRebornGameServer.ResetHarvestResources();
                Console.WriteLine("[storm] understorm reset: " + summary);
                return summary;
            }
        }

        /// <summary>
        /// Pushes one island's new 1254 timer to every peer that holds that
        /// component, and returns how many got it.
        ///
        /// THREE fields, and only three: the countdown, the storm switch and the
        /// cycle counter. Peers that have not been served the island's 1254 are
        /// skipped - the ComponentMap lookup that establishes that is the same one
        /// the update path uses, so this cannot disagree with reality.
        /// </summary>
        private static int Push(long islandEntityId, IslandStormUpdate update)
        {
            int recipients = 0;
            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (!GameState.Instance.ComponentMap.TryGetValue(peer,
                        out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                    || !byEntity.TryGetValue(islandEntityId, out Dictionary<uint, ulong>? byComponent)
                    || !byComponent.TryGetValue(IslandLightningTimerStateComponentId, out ulong refId))
                {
                    continue;
                }

                IslandLightningTimerState.Update timer = new IslandLightningTimerState.Update()
                    .SetEstimatedMilliTillNextLightning(update.MillisTillNextLightning)
                    .SetEstimatedMilliTillLightningEnd(update.MillisTillLightningEnd)
                    .SetGeneration((int)update.Generation);

                // Keep this peer's stored 1254 in step with what it has just been
                // told, so a later re-serve from the stored object cannot resurrect
                // the seeded 50 s countdown - or, far worse, re-assert a storm that
                // has already ended.
                if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId)
                    is IslandLightningTimerState.Data stored)
                {
                    timer.ApplyTo(stored);
                }

                if (SendOPHelper.SendComponentUpdateOp(peer, islandEntityId,
                        new List<uint> { IslandLightningTimerStateComponentId },
                        new List<object> { timer }))
                {
                    recipients++;
                }
            }

            Console.WriteLine("[storm] island " + islandEntityId + " " + update
                + " -> " + recipients + " checked-out peer(s).");
            return recipients;
        }
    }
}
