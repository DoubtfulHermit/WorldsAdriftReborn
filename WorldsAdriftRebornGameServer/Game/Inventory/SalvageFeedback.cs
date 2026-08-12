using Bossa.Travellers.World;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// Fires the native "Salvaged Iron x12" toast: the 8060 FeedbackListener
    /// event the game's own <c>FeedbackVisualizer</c> renders.
    ///
    /// WHY THIS IS AN EVENT, NOT A STATE PUSH. 8060 is a component the player's
    /// own entity holds (seeded in <c>ComponentsSerializer</c>), but the toast is
    /// driven by a component EVENT - <c>ReceiveSalvageFeedback{itemTypeId,
    /// quantity}</c> - not by a data field. Sending a
    /// <c>FeedbackListener.Update</c> carrying that event through the ordinary
    /// component-update op is what the client turns into the HUD toast and the
    /// salvage SFX. There is nothing to store: the same "+12 iron" can arrive
    /// twice and must toast twice, so unlike 1081 there is no stored Data to keep
    /// in step.
    ///
    /// It is addressed to the HARVESTER's own entity and sent only to the
    /// harvester's peer, because the toast is a first-person acknowledgement -
    /// nobody else's screen should announce your salvage. The player is the only
    /// peer holding 8060 for their own entity anyway (a mirrored remote rig never
    /// checks it out), so the ComponentMap filter below resolves to exactly them.
    ///
    /// ONE HAZARD, carried across from the old seam comment and still true: the
    /// client's toast dereferences <c>InventoryItemManager.LookupItem(itemTypeId)</c>
    /// UNGUARDED. An itemTypeId absent from our own itemData.json is a client-side
    /// NRE, not a missing label - so this refuses to send for an unknown type and
    /// says so, rather than crash the harvester's client.
    /// </summary>
    internal static class SalvageFeedback
    {
        private const uint FeedbackListenerComponentId = 8060;

        /// <summary>
        /// Toasts "Salvaged &lt;name&gt; x&lt;quantity&gt;" on the harvester's HUD.
        ///
        /// Returns whether the event reached a peer. A false is a real signal - it
        /// means the harvester was not holding 8060, i.e. the seed never went out -
        /// not a swallowed error.
        /// </summary>
        internal static bool Send(long harvesterEntityId, string itemTypeId, int quantity, string reason)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(itemTypeId))
            {
                return false;
            }

            if (!ItemHelper.AllItems.ContainsKey(itemTypeId))
            {
                // The grant path already validates this, so reaching here means a
                // caller bypassed it. Refuse rather than hand the client an
                // unguarded LookupItem NRE.
                Console.WriteLine("[warning] salvage feedback for unknown itemTypeId '" + itemTypeId
                    + "' suppressed (" + reason + "); it would NRE the client toast.");
                return false;
            }

            int sent = 0;

            foreach (ENetPeerHandle peer in PeersHolding(harvesterEntityId))
            {
                // A fresh Update per peer: the event list is a mutable object and
                // must not be shared across sends.
                FeedbackListener.Update update = new FeedbackListener.Update()
                    .AddReceiveSalvageFeedback(new ReceiveSalvageFeedback(itemTypeId, quantity));

                if (SendOPHelper.SendComponentUpdateOp(peer, harvesterEntityId,
                        new List<uint> { FeedbackListenerComponentId },
                        new List<object> { update }))
                {
                    sent++;
                }
            }

            if (sent == 0)
            {
                Console.WriteLine("[warning] salvage feedback (" + reason + ") reached no peer: entity "
                    + harvesterEntityId + " holds no 8060 FeedbackListener.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The peers whose stored component map holds this entity's 8060. In
        /// practice exactly one - the entity's owner - because only the local
        /// player checks out their own FeedbackListener.
        ///
        /// Snapshotted into a list first because sending can disturb peer state
        /// and enumerating a dictionary a send mutates throws - the same rule
        /// InventoryPush follows.
        /// </summary>
        private static List<ENetPeerHandle> PeersHolding(long entityId)
        {
            List<ENetPeerHandle> peers = new List<ENetPeerHandle>();

            foreach (KeyValuePair<ENetPeerHandle, Dictionary<long, Dictionary<uint, ulong>>> entry
                in GameState.Instance.ComponentMap)
            {
                if (entry.Value.TryGetValue(entityId, out Dictionary<uint, ulong>? components)
                    && components.ContainsKey(FeedbackListenerComponentId))
                {
                    peers.Add(entry.Key);
                }
            }

            return peers;
        }
    }
}
