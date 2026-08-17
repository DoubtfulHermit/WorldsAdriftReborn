using Bossa.Travellers.Crew;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crew
{
    /// <summary>
    /// The ONLY way a crew reaches a client. Nothing else may send 6900.
    ///
    /// Like 1081, 6900 is a FULL-STATE component: the wire carries no delta and
    /// the client rebuilds its whole crew panel from whatever arrived last. So
    /// two pushes in one tick are last-wins, and a loser built from a stale read
    /// erases the earlier change. One function that reads the live ledger, writes
    /// it into every holder's stored component and sends is what makes that
    /// unrepresentable.
    ///
    /// The part that is specific to crews: an action changes SEVERAL players'
    /// state at once. An invite changes the inviter's crew and the invitee's
    /// invite list; a boot changes everyone still in the crew. Pushing only to
    /// the actor is the characteristic crew bug - the other player's UI simply
    /// never updates - which is why <see cref="CrewOutcome.Affected"/> exists and
    /// why this takes a set of uids rather than one.
    /// </summary>
    internal static class CrewPush
    {
        private const uint CrewMembershipStateComponentId = 6900;

        /// <summary>
        /// Pushes the crew state of every affected character to whichever of them
        /// are connected. A uid that is offline is simply skipped: their state is
        /// in the ledger and will be served at their next checkout.
        /// </summary>
        internal static void PushAll(IReadOnlyCollection<string> affectedUids, string reason)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int sent = 0;

            foreach (string uid in affectedUids)
            {
                if (string.IsNullOrEmpty(uid) || !seen.Add(uid)) continue;
                if (PushOne(uid)) sent++;
            }

            Console.WriteLine("[info] crew: pushed 6900 to " + sent + " of "
                + seen.Count + " affected character(s) (" + reason + ").");
        }

        /// <summary>
        /// Tells one player why an action was refused, without changing any state.
        ///
        /// Feedback is an EVENT on 6900, so it rides an update like everything
        /// else. The retail UI has exactly this one line plus a success flag to
        /// report with, which is why every verdict in CrewPolicy is written as a
        /// sentence a player can act on.
        /// </summary>
        internal static void Feedback(string uid, string message, bool ok)
        {
            long? entityId = EntityOf(uid);
            if (!entityId.HasValue) return;

            foreach (ENetPeerHandle peer in PeersHolding(entityId.Value))
            {
                CrewMembershipState.Update update = new CrewMembershipState.Update();
                update.AddFeedback(new CrewManagementFeedback(message, ok));

                SendOPHelper.SendComponentUpdateOp(peer, entityId.Value,
                    new List<uint> { CrewMembershipStateComponentId },
                    new List<object> { update });
            }
        }

        internal static void SearchResult(string uid, int requestId, string playerName,
            string foundPlayerId, bool found)
        {
            long? entityId = EntityOf(uid);
            if (!entityId.HasValue) return;

            foreach (ENetPeerHandle peer in PeersHolding(entityId.Value))
            {
                CrewMembershipState.Update update = new CrewMembershipState.Update();
                update.AddSearchResults(
                    new SearchPlayerResult(requestId, playerName, foundPlayerId, found));

                SendOPHelper.SendComponentUpdateOp(peer, entityId.Value,
                    new List<uint> { CrewMembershipStateComponentId },
                    new List<object> { update });
            }
        }

        private static bool PushOne(string uid)
        {
            long? entityId = EntityOf(uid);
            if (!entityId.HasValue) return false;

            CrewMembershipStateData fresh = CrewWire.For(uid);
            bool sentAny = false;

            foreach (ENetPeerHandle peer in PeersHolding(entityId.Value))
            {
                Dictionary<uint, ulong> components =
                    GameState.Instance.ComponentMap[peer][entityId.Value];
                if (!components.TryGetValue(CrewMembershipStateComponentId, out ulong reference))
                    continue;

                CrewMembershipState.Data stored =
                    (CrewMembershipState.Data)ClientObjects.Instance.Dereference(reference);

                // Written back into the STORED data, not only into the Update, so
                // a later re-serve of 6900 carries the crew rather than the empty
                // seed the checkout built.
                stored.Value.playerId = fresh.playerId;
                stored.Value.name = fresh.name;
                stored.Value.currentCrewLeaderId = fresh.currentCrewLeaderId;
                stored.Value.crewMembers = fresh.crewMembers;
                stored.Value.invitesReceived = fresh.invitesReceived;
                stored.Value.numSlots = fresh.numSlots;
                stored.Value.beaconCoolDown = fresh.beaconCoolDown;

                CrewMembershipState.Update update = new CrewMembershipState.Update();
                update.SetPlayerId(fresh.playerId)
                      .SetName(fresh.name)
                      .SetCurrentCrewLeaderId(fresh.currentCrewLeaderId)
                      .SetCrewMembers(fresh.crewMembers)
                      .SetInvitesReceived(fresh.invitesReceived)
                      .SetNumSlots(fresh.numSlots)
                      .SetBeaconCoolDown(fresh.beaconCoolDown);

                if (SendOPHelper.SendComponentUpdateOp(peer, entityId.Value,
                        new List<uint> { CrewMembershipStateComponentId },
                        new List<object> { update }))
                {
                    sentAny = true;
                }
            }

            return sentAny;
        }

        /// <summary>The live player entity for a durable character uid, if they are on.</summary>
        private static long? EntityOf(string uid)
        {
            foreach ((ulong _, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                string candidate = CharacterOwnership.UidForEntity(entityId);
                if (candidate.Length > 0
                    && string.Equals(CrewPersistence.Key(Guid.Parse(candidate)), uid,
                        StringComparison.Ordinal))
                {
                    return entityId;
                }
            }
            return null;
        }

        /// <summary>
        /// Snapshotted into a list first because sending can disturb peer state,
        /// and enumerating a dictionary that a send mutates throws.
        /// </summary>
        private static List<ENetPeerHandle> PeersHolding(long entityId)
        {
            List<ENetPeerHandle> peers = new List<ENetPeerHandle>();

            foreach (KeyValuePair<ENetPeerHandle, Dictionary<long, Dictionary<uint, ulong>>> entry
                in GameState.Instance.ComponentMap)
            {
                if (entry.Value.TryGetValue(entityId, out Dictionary<uint, ulong>? components)
                    && components.ContainsKey(CrewMembershipStateComponentId))
                {
                    peers.Add(entry.Key);
                }
            }

            return peers;
        }
    }
}
