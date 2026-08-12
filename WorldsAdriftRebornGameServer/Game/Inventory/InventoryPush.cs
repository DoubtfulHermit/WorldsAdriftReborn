using Bossa.Travellers.Inventory;
using Bossa.Travellers.Player;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// The ONLY way an inventory reaches a client. Nothing else may send 1081.
    ///
    /// WHY A SEAM AND NOT A HELPER. 1081 is a full-state component - the wire has
    /// no add-delta, the client receives the entire list every time - and the
    /// client rebuilds its whole local model from whatever arrives last. So two
    /// pushes in one tick are last-wins, and if the loser was built from a stale
    /// read the earlier change is simply erased. The way to make that
    /// unrepresentable is for there to be one function that reads the live model,
    /// writes it back into every peer's stored component, and sends. This is it.
    ///
    /// AND WHY EVERY 1082 REQUEST MUST REACH IT, including a rejected one. The
    /// client sets IsWaitingForServer before it sends an inventory request and
    /// clears it in exactly one place: inside LoadInventory, which runs only off
    /// a 1081 update. There is no timeout, no rollback and no spinner expiry.
    /// Echoing the 1082 back does NOT clear it. So a request that is answered
    /// with anything other than a 1081 push greys the player's inventory panel
    /// out permanently - which is what happened the first time anybody dragged
    /// an item, before this existed.
    ///
    /// Component order inside one op is 1280 then 1081 then 1088, which is the
    /// order the working equipWearable path used and the reason it worked: 1081
    /// sets an item's slotType to something meaningful, and 1088 expects that to
    /// already be true when it arrives.
    /// </summary>
    internal static class InventoryPush
    {
        private const uint WearableUtilsStateComponentId = 1280;
        private const uint InventoryStateComponentId = 1081;
        private const uint PlayerPropertiesStateComponentId = 1088;

        /// <summary>
        /// Pushes an entity's inventory to every peer that holds it, writes the
        /// new contents back into each of those peers' stored components, and
        /// persists it.
        ///
        /// <paramref name="reason"/> is logged. It is there because the single
        /// most common failure in this area is "nothing happened and nobody
        /// knows which of the fifteen events did not fire".
        /// </summary>
        internal static void Push( long entityId, string reason )
        {
            InventoryModel model = InventoryService.ForEntity(entityId);

            IReadOnlyList<string> problems = InventoryPolicy.ValidateForWire(model, InventoryWire.Footprints);

            if (problems.Count > 0)
            {
                // Logged, then sent anyway. Refusing to send would leave the
                // panel greyed out forever, which is strictly worse than an item
                // in an odd place: the player can fix a misplaced item, they
                // cannot fix a dead panel. The log is what makes the bug ours.
                Console.WriteLine("[warning] inventory of entity " + entityId + " has "
                    + problems.Count + " problem(s) before push (" + reason + "): "
                    + string.Join("; ", problems));
            }

            WearableArrays wearables = WearableInvariants.For(model);

            int sent = 0;

            foreach (ENetPeerHandle peer in PeersHolding(entityId))
            {
                if (PushTo(peer, entityId, model, wearables))
                {
                    sent++;
                }
            }

            Console.WriteLine("[info] pushed inventory of entity " + entityId + " to " + sent
                + " peer(s) (" + reason + "), " + model.Items.Count + " item(s).");

            InventoryService.Save(entityId);
        }

        /// <summary>
        /// The peers whose stored component map holds this entity's 1081.
        ///
        /// Snapshotted into a list first because sending can disturb peer state,
        /// and enumerating a dictionary that a send mutates throws.
        /// </summary>
        private static List<ENetPeerHandle> PeersHolding( long entityId )
        {
            List<ENetPeerHandle> peers = new List<ENetPeerHandle>();

            foreach (KeyValuePair<ENetPeerHandle, Dictionary<long, Dictionary<uint, ulong>>> entry
                in GameState.Instance.ComponentMap)
            {
                if (entry.Value.TryGetValue(entityId, out Dictionary<uint, ulong>? components)
                    && components.ContainsKey(InventoryStateComponentId))
                {
                    peers.Add(entry.Key);
                }
            }

            return peers;
        }

        private static bool PushTo(
            ENetPeerHandle peer,
            long entityId,
            InventoryModel model,
            WearableArrays wearables )
        {
            Dictionary<uint, ulong> components = GameState.Instance.ComponentMap[peer][entityId];

            List<uint> ids = new List<uint>();
            List<object> updates = new List<object>();

            // 1280 first: the wearable arrays have to be in place before the
            // slotType they describe arrives.
            if (components.TryGetValue(WearableUtilsStateComponentId, out ulong wearableRef))
            {
                WearableUtilsState.Data stored =
                    (WearableUtilsState.Data)ClientObjects.Instance.Dereference(wearableRef);

                Improbable.Collections.List<int> itemIds = new Improbable.Collections.List<int>();
                Improbable.Collections.List<float> healths = new Improbable.Collections.List<float>();
                Improbable.Collections.List<bool> active = new Improbable.Collections.List<bool>();

                for (int i = 0; i < wearables.Count; i++)
                {
                    itemIds.Add(wearables.ItemIds[i]);
                    healths.Add(wearables.Healths[i]);
                    active.Add(wearables.Active[i]);
                }

                // Written back into the STORED data, not only into an Update.
                // The old equip path called SetItemIds on a copy taken from
                // ToUpdate(), which replaces the Option's inner value and leaves
                // the stored component at its empty seed forever - so equipping
                // a second wearable replaced the first, and any re-serve of 1280
                // served an empty list.
                stored.Value.itemIds = itemIds;
                stored.Value.healths = healths;
                stored.Value.active = active;

                WearableUtilsState.Update wearableUpdate = new WearableUtilsState.Update();
                wearableUpdate.SetItemIds(itemIds).SetHealths(healths).SetActive(active);

                ids.Add(WearableUtilsStateComponentId);
                updates.Add(wearableUpdate);
            }

            if (!components.TryGetValue(InventoryStateComponentId, out ulong inventoryRef))
            {
                return false;
            }

            InventoryState.Data inventory = (InventoryState.Data)ClientObjects.Instance.Dereference(inventoryRef);

            // A FRESH list per peer. Sharing one list between two peers' stored
            // Data makes them the same mutable object, which is how one peer's
            // later mutation silently rewrites another's.
            Improbable.Collections.List<ScalaSlottedInventoryItem> items = InventoryWire.ToWireList(model);
            Improbable.Collections.List<ScalaSlottedInventoryItem> stash = InventoryWire.ToStashList(model);

            inventory.Value.inventoryList = items;
            inventory.Value.lockBoxItems = stash;

            InventoryState.Update inventoryUpdate = new InventoryState.Update();
            inventoryUpdate.SetInventoryList(items);
            inventoryUpdate.SetLockBoxItems(stash);

            // width/height/hasBelt/beltRow are deliberately NOT set. The client
            // reads them exactly once, at InventoryVisualiser.OnEnable, and
            // LoadInventory never calls Setup - so sending them is at best a
            // no-op and at worst a lie the server tells itself about a grid the
            // player can never actually be given.

            ids.Add(InventoryStateComponentId);
            updates.Add(inventoryUpdate);

            // 1088 last, unchanged, only where it exists. It carries the
            // appearance that renders worn gear, and the proven equip path sent
            // it after 1081 for exactly that reason.
            if (components.TryGetValue(PlayerPropertiesStateComponentId, out ulong propertiesRef))
            {
                PlayerPropertiesState.Data properties =
                    (PlayerPropertiesState.Data)ClientObjects.Instance.Dereference(propertiesRef);

                ids.Add(PlayerPropertiesStateComponentId);
                updates.Add((PlayerPropertiesState.Update)properties.ToUpdate());
            }

            return SendOPHelper.SendComponentUpdateOp(peer, entityId, ids, updates);
        }
    }
}
