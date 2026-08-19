using Bossa.Travellers.Interact;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// SHIP STORAGE: binds a crafted container's own inventory, and opens it.
    ///
    /// The ship-part twin of <c>Game/Loot/LootStock</c> + <c>Game/Loot/LootService</c>.
    /// The two are deliberately NOT merged: a ruin chest is identified by the loot
    /// ledger and rolls contents from a table, a ship container is identified by the
    /// loose-part catalogue and starts empty. What they share - the trap and the echo -
    /// is restated here rather than abstracted, because the loot pair is owned by an
    /// in-flight branch and a shared base class would collide with it.
    ///
    /// THE TRAP, restated because it is permanent and silent. The 1081 serve calls
    /// <c>InventoryService.ForEntity</c>, whose create-factory is
    /// <c>InventoryWire.DefaultModel</c> - the PLAYER STARTER KIT - and
    /// <c>InventoryStore.Bind</c> runs a factory at most once per key. A container
    /// that reaches that branch unbound gets a permanent inventory containing four
    /// gauntlets, in a 10x18 belt grid, for the rest of the session, with no way to
    /// correct it and nothing logged. So <see cref="Ensure"/> runs FIRST, at every
    /// seam that can reach a container's inventory: the 1081 seed, the open echo, and
    /// both cross-inventory moves.
    ///
    /// WHY THE CONTENTS ARE SESSION-SCOPED, stated plainly rather than left to be
    /// discovered. <see cref="InventoryService.BindContainer"/> binds on
    /// <c>InventoryKey.ForSession</c>, which is not durable, so a container's contents
    /// do NOT survive a game-server restart. That is the same honest limitation ruin
    /// chests ship with, and making it durable means a contents field on
    /// <c>MountedPartRecord</c> (the shape <c>SailUnfurled</c>/<c>LampOff</c> already
    /// prove needs no schema migration) plus a save hook on every move. It is a
    /// deliberate follow-on, not an oversight - and it is why
    /// <see cref="ShipPartSalvagePolicy"/> refusing a non-empty container matters
    /// more, not less: the salvage beam is the one loss this server CAN prevent today.
    ///
    /// MULTIPLAYER-SAFE: event-driven, one component update per E press, addressed to
    /// one peer, no per-frame state, nothing relayed.
    /// </summary>
    internal static class ShipContainerService
    {
        private const uint InteractiveStateComponentId = 1210;

        /// <summary>
        /// True when this entity is one of the four crafted ship storage containers.
        /// The single question the serve, the gate and the echo ask, so they cannot
        /// disagree about what a container is.
        /// </summary>
        internal static bool IsContainer(long entityId) =>
            ShipContainers.IsContainer(LooseParts.DefFor(entityId)?.ItemType);

        /// <summary>
        /// Binds this container's own empty inventory, at its own grid size, if it has
        /// never been bound. A no-op for anything that is not a ship container and for
        /// a container that already has one - so it is safe to call on every path that
        /// might touch a container inventory, and calling it twice cannot wipe or
        /// duplicate anything (<c>Bind</c> runs its factory at most once per key).
        /// Returns true when it bound one.
        /// </summary>
        internal static bool Ensure(long entityId)
        {
            string? itemType = LooseParts.DefFor(entityId)?.ItemType;
            ShipContainers.Grid? grid = ShipContainers.GridFor(itemType);
            if (grid == null)
            {
                return false;
            }

            if (InventoryService.KeyOf(entityId) != null)
            {
                // Already bound - either by an earlier serve, or filled by a player.
                // Its contents are the store's business from here.
                return false;
            }

            // An EMPTY drops list, which is the whole difference from a ruin chest:
            // a container the player crafted starts with exactly what they put in it.
            InventoryService.BindContainer(
                entityId,
                grid.Value.Width,
                grid.Value.Height,
                ShipContainers.HasBelt,
                ShipContainers.BeltRow,
                System.Array.Empty<Multiplayer.Loot.LootDrop>());

            Console.WriteLine("[ship-storage] bound container " + entityId + " ('" + itemType
                + "') to its own empty " + grid.Value.Width + "x" + grid.Value.Height
                + " grid (" + grid.Value.Cells + " cells); it will NOT open onto the"
                + " player starter kit.");
            return true;
        }

        /// <summary>
        /// How many items this container currently holds, or 0 when the entity is not
        /// a container. Deliberately does NOT bind: asking a never-opened container
        /// what is in it must not be the thing that creates its inventory, or the
        /// salvage path would bind every container it ever shot at.
        /// </summary>
        internal static int ItemCount(long entityId)
        {
            if (!IsContainer(entityId) || InventoryService.KeyOf(entityId) == null)
            {
                return 0;
            }

            InventoryModel model = InventoryService.ForEntity(entityId);
            return model.Items.Count;
        }

        /// <summary>
        /// Answers a completed <c>Inventory</c> interaction on a ship container by
        /// echoing the event the client needs to open the panel. Returns true when the
        /// echo was sent.
        ///
        /// THE PANEL DOES NOT OPEN BECAUSE THE CLIENT HOLDS 1081, and it does not open
        /// because the server sets <c>inUseBy</c>. It opens because an <c>Interact</c>
        /// EVENT arrives on the container's own 1210 -
        /// <c>InWorldInventoryVisualiser.OnObjectInteractionTriggered</c> compares
        /// <c>playerEntityId</c> against its own and <c>verb == Inventory</c>, then
        /// pushes <c>MainInventoryUIState.Storage</c>.
        ///
        /// <c>inUseBy</c> stays unset for the same reason it does on a ruin chest:
        /// <c>OnObjectInUseByUpdated</c> POPS the UI state whenever the previous holder
        /// was the local player, so setting it on open without a matching close signal
        /// closes the panel under the player's own cursor.
        /// </summary>
        internal static bool OpenContainer(ENetPeerHandle peer, long playerEntityId, long containerEntityId)
        {
            if (!IsContainer(containerEntityId))
            {
                // A ruin chest, a prop, a mistake. Not ours to answer, and answering
                // it would put an Interact event on a component that may not be there.
                return false;
            }

            if (!MountedParts.Is(containerEntityId))
            {
                // A container is openable only while BOLTED DOWN - see
                // PartInteractionPolicy.IsSeededInteractionAvailable for why. The
                // prompt is already withheld in that state, so reaching here means a
                // modified or stale client asked anyway.
                Console.WriteLine("[ship-storage] refused to open container " + containerEntityId
                    + " for player " + playerEntityId + ": it is not mounted to a ship."
                    + " A loose container can be lifted away with whatever is inside it,"
                    + " so it stays shut until it is bolted down.");
                return false;
            }

            // Belt and braces: the 1081 serve binds on checkout, but a client that
            // asked for 1210 and not 1081 would otherwise open onto nothing.
            // Idempotent, so this cannot wipe a container a player already filled.
            Ensure(containerEntityId);

            InteractiveState.Update update = new InteractiveState.Update();
            update.AddInteract(new Interact(
                InteractVerb.Inventory,
                new EntityId(playerEntityId),
                // characterUid: the client's handler reads only verb and
                // playerEntityId, and this server has no uid at this seam.
                ""));

            bool sent = SendOPHelper.SendComponentUpdateOp(
                peer, containerEntityId,
                new List<uint> { InteractiveStateComponentId },
                new List<object> { update });

            if (sent)
            {
                Console.WriteLine("[ship-storage] player " + playerEntityId + " opened container "
                    + containerEntityId + " ('" + (LooseParts.DefFor(containerEntityId)?.ItemType ?? "?")
                    + "'); echoed Interact(Inventory) on its 1210.");
            }
            else
            {
                Console.WriteLine("[warning] [ship-storage] player " + playerEntityId
                    + " interacted with container " + containerEntityId
                    + " but the 1210 Interact echo failed to send; their panel will not open."
                    + " The prompt is not a lie - the echo is - so this is a send failure,"
                    + " not a missing component.");
            }

            return sent;
        }
    }
}
