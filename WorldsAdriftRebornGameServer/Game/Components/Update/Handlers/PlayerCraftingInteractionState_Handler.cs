using System;
using System.Collections.Generic;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Materials;
using Improbable;
using Improbable.Collections;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /// <summary>
    /// Personal (multitool) crafting: the real transaction behind the Craft tab.
    ///
    /// The client sends every crafting action on this component (1003), which is
    /// already client-authoritative. It carries four events this handler acts on:
    /// SetSchematic (choose a recipe), AddItemFromInventory / ReturnItemToInventory
    /// (fill and clear the material slots) and StartPlayerCrafting (the Craft
    /// button). The server answers on 1005 (CraftingStationClientState, its own
    /// per-player state) and 1081 (the inventory), which are the only two things
    /// that clear the client's three hard-lock wait flags. Every branch - even a
    /// rejected one - touches 1005 (and 1081 where a wait flag is involved) so the
    /// Craft tab can never be left hung.
    ///
    /// Consumption is atomic and happens once, in CraftingPolicy, when the player
    /// clicks Craft: it validates the bag against the recipe, removes the
    /// materials and grants the output as one all-or-nothing step. The material
    /// slots are a display reservation only (see CraftSession) - filling a slot
    /// takes nothing out of the bag, which is why closing the tab loses nothing.
    ///
    /// MULTIPLAYER SAFETY: everything here is event-driven and per-player. 1005 is
    /// pushed only to the crafting player's own peer; 1081 goes only to holders of
    /// that inventory via InventoryPush. Nothing is relayed to other players and
    /// nothing runs per frame.
    /// </summary>
    [RegisterComponentUpdateHandler]
    internal class PlayerCraftingInteractionState_Handler : IComponentUpdateHandler<PlayerCraftingInteractionState, PlayerCraftingInteractionState.Update, PlayerCraftingInteractionState.Data>
    {
        public PlayerCraftingInteractionState_Handler() { Init(1003); }

        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId, PlayerCraftingInteractionState.Update clientComponentUpdate, PlayerCraftingInteractionState.Data serverComponentData)
        {
            clientComponentUpdate.ApplyTo(serverComponentData);
            PlayerCraftingInteractionState.Update serverComponentUpdate = (PlayerCraftingInteractionState.Update)serverComponentData.ToUpdate();

            CraftSession session = CraftSessions.For(entityId);

            if (clientComponentUpdate.setSchematic != null)
            {
                foreach (SetSchematic set in clientComponentUpdate.setSchematic)
                {
                    HandleSetSchematic(player, entityId, session, set);
                }
            }

            if (clientComponentUpdate.addItemFromInventory != null)
            {
                foreach (AddItemFromInventory add in clientComponentUpdate.addItemFromInventory)
                {
                    HandleAddItem(player, entityId, session, add);
                }
            }

            if (clientComponentUpdate.returnItemToInventory != null)
            {
                foreach (ReturnItemToInventory ret in clientComponentUpdate.returnItemToInventory)
                {
                    HandleReturnItem(player, entityId, session, ret);
                }
            }

            if (clientComponentUpdate.startPlayerCrafting != null)
            {
                foreach (StartPlayerCrafting _ in clientComponentUpdate.startPlayerCrafting)
                {
                    HandleStartCrafting(player, entityId, session);
                }
            }

            // STATION crafting: a Shipyard / CraftingStation-category craft fires
            // StartCrafting(stationEntityId) rather than StartPlayerCrafting. This is
            // the faithful path for a ship PART (the lamp is category "Shipyard"), and
            // its output is a loose WORLD entity, not an inventory item.
            if (clientComponentUpdate.startCrafting != null)
            {
                foreach (StartCrafting start in clientComponentUpdate.startCrafting)
                {
                    HandleStartStationCrafting(player, entityId, session, start);
                }
            }

            // Echo the 1003 field state (craftingStationEntityId / debugMode).
            // The component is client-authoritative and this carries no transient
            // events, so it only mirrors the fields the client already set.
            SendOPHelper.SendComponentUpdateOp(player, entityId, new System.Collections.Generic.List<uint> { ComponentId }, new System.Collections.Generic.List<object> { serverComponentUpdate });
        }

        private static void HandleSetSchematic( ENetPeerHandle player, long entityId, CraftSession session, SetSchematic set )
        {
            string? schematicId = set.schematicId.HasValue ? set.schematicId.Value : null;

            if (string.IsNullOrEmpty(schematicId))
            {
                // Deselected: clear the recipe and empty the slots.
                session.SchematicId = null;
                session.Slots = Array.Empty<SlotHold>();
                PushCraftingState(player, entityId, session);
                return;
            }

            SchematicRecord? record = SchematicHelper.Get(schematicId);

            if (record == null)
            {
                Console.WriteLine("[warning] craft: entity " + entityId + " selected unknown recipe '" + schematicId + "'.");
                session.SchematicId = null;
                session.Slots = Array.Empty<SlotHold>();
                PushCraftingState(player, entityId, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed("unknown recipe")));
                return;
            }

            session.SchematicId = schematicId;
            session.Slots = new SlotHold[record.CraftingRequirements.Count];
            for (int i = 0; i < session.Slots.Length; i++)
            {
                session.Slots[i] = new SlotHold { Amount = 0, MaterialTypeId = "" };
            }

            PushCraftingState(player, entityId, session);
        }

        private static void HandleAddItem( ENetPeerHandle player, long entityId, CraftSession session, AddItemFromInventory add )
        {
            SchematicRecord? record = session.SchematicId != null ? SchematicHelper.Get(session.SchematicId) : null;
            int slotIndex = add.targetSlotIndex;

            if (record == null || slotIndex < 0 || slotIndex >= record.CraftingRequirements.Count || slotIndex >= session.Slots.Length)
            {
                FailAdd(player, entityId, session, "no recipe selected or slot out of range");
                return;
            }

            InventoryModel model = InventoryService.ForEntity(entityId);
            InventoryItem? item = model.ById(add.itemId);

            if (item == null)
            {
                FailAdd(player, entityId, session, "no such inventory item");
                return;
            }

            CraftingRequirement requirement = record.CraftingRequirements[slotIndex];

            if (!CraftingPolicy.Matches(requirement.Name, item.ItemTypeId, InventoryWire.CategoryOf(item.ItemTypeId)))
            {
                FailAdd(player, entityId, session, "'" + item.ItemTypeId + "' does not fit requirement '" + requirement.Name + "'");
                return;
            }

            // A display reservation, capped at what the slot needs and at what the
            // bag actually holds of a matching material. Nothing leaves the bag.
            int available = CraftingPolicy.AvailableFor(model, InventoryWire.CategoryLookup, requirement.Name);
            int reserved = Math.Min(requirement.AmountRequired, available);
            session.Slots[slotIndex] = new SlotHold { Amount = reserved, MaterialTypeId = item.ItemTypeId };

            PushCraftingState(player, entityId, session);
            // Clears the client's inventory wait flag; contents are unchanged.
            InventoryPush.Push(entityId, "craft slot fill");
        }

        private static void HandleReturnItem( ENetPeerHandle player, long entityId, CraftSession session, ReturnItemToInventory ret )
        {
            int slotIndex = ret.slotIndex;

            if (session.Slots.Length == 0)
            {
                PushCraftingState(player, entityId, session);
                InventoryPush.Push(entityId, "craft slot return");
                return;
            }

            if (slotIndex == -1)
            {
                // -1 means return everything.
                for (int i = 0; i < session.Slots.Length; i++)
                {
                    session.Slots[i] = new SlotHold { Amount = 0, MaterialTypeId = "" };
                }
            }
            else if (slotIndex >= 0 && slotIndex < session.Slots.Length)
            {
                session.Slots[slotIndex] = new SlotHold { Amount = 0, MaterialTypeId = "" };
            }
            else
            {
                PushCraftingState(player, entityId, session,
                    update => update.AddReturnItemToInventoryFailed(new ReturnItemToInventoryFailed("slot out of range")));
                InventoryPush.Push(entityId, "craft slot return rejected");
                return;
            }

            PushCraftingState(player, entityId, session);
            // Reservations only, so the bag is unchanged; the push clears the wait flag.
            InventoryPush.Push(entityId, "craft slot return");
        }

        private static void HandleStartCrafting( ENetPeerHandle player, long entityId, CraftSession session )
        {
            SchematicRecord? record = session.SchematicId != null ? SchematicHelper.Get(session.SchematicId) : null;

            if (record == null)
            {
                PushCraftingState(player, entityId, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed("no recipe selected")));
                return;
            }

            if (!ItemHelper.AllItems.ContainsKey(record.ItemType))
            {
                PushCraftingState(player, entityId, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed("output item '" + record.ItemType + "' is not in the item database")));
                return;
            }

            InventoryModel model = InventoryService.ForEntity(entityId);
            ItemHelper.ValidItem output = ItemHelper.GetItem(record.ItemType);
            IReadOnlyDictionary<string, string> meta = output.metadata ?? new Dictionary<string, string>();

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                record,
                model,
                InventoryWire.CategoryLookup,
                () => InventoryService.NextItemId(entityId),
                0,
                meta,
                output.rarity,
                InventoryWire.Footprints);

            if (!outcome.Ok)
            {
                Console.WriteLine("[info] craft rejected (entity " + entityId + ", recipe " + record.SchematicId + "): " + outcome.Reason);
                PushCraftingState(player, entityId, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed(outcome.Reason)));
                return;
            }

            // Materials consumed, output granted. Push the new inventory first,
            // then clear the slot reservations and tell the client the craft both
            // started and completed in one 1005 update so CraftingInProgress nets
            // back to false and the panel is never stranded mid-craft.
            InventoryPush.Push(entityId, "crafted " + record.ItemType);

            for (int i = 0; i < session.Slots.Length; i++)
            {
                session.Slots[i] = new SlotHold { Amount = 0, MaterialTypeId = "" };
            }

            string schematicId = record.SchematicId;
            PushCraftingState(player, entityId, session, update =>
            {
                update.AddCraftingStarted(new CraftingStarted(-1, schematicId));
                update.AddCraftingCompleted(new CraftingCompleted(schematicId));
            });

            Console.WriteLine("[info] entity " + entityId + " crafted " + record.ItemType + " (item id "
                + outcome.OutputItemId + "), consumed " + outcome.Consumed.Count + " stack(s).");
        }

        /// <summary>
        /// A CraftingStation / Shipyard-category craft (StartCrafting), for a recipe
        /// whose output is a loose WORLD part rather than an inventory item - the lamp.
        /// Validates the selected recipe is a loose ship part, consumes its materials
        /// atomically (no inventory grant), spawns the part next to the station, and
        /// tells the client the craft started and completed. The 1005 reply is pushed
        /// to the STATION entity (where CraftingStationClientState lives) so the
        /// station panel unlocks; exact 1005 routing for a station craft is a
        /// live-only unknown, so a non-positive station id falls back to the player's
        /// own 1003/1005.
        ///
        /// FAITHFULNESS: the ship-part craft category and instant (vs timed)
        /// completion are simplifications flagged for follow-on; the completion->spawn
        /// hook (<see cref="LoosePartSpawner.Spawn"/>) is deliberately reusable.
        /// </summary>
        private static void HandleStartStationCrafting( ENetPeerHandle player, long entityId, CraftSession session, StartCrafting start )
        {
            long stationEntityId = start.craftingEntity.Id;
            long pushTarget = stationEntityId > 0 ? stationEntityId : entityId;

            SchematicRecord? record = session.SchematicId != null ? SchematicHelper.Get(session.SchematicId) : null;

            if (record == null)
            {
                PushCraftingState(player, pushTarget, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed("no recipe selected")));
                return;
            }

            LoosePartDefinition? part = LoosePartCatalogue.ForSchematic(record.SchematicId);

            if (part == null)
            {
                Console.WriteLine("[info] station craft: entity " + entityId + " tried to craft '"
                    + record.SchematicId + "', which is not a loose ship part; not supported this phase.");
                PushCraftingState(player, pushTarget, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed(
                        "station craft for '" + record.SchematicId + "' is not supported yet")));
                return;
            }

            InventoryModel model = InventoryService.ForEntity(entityId);

            if (!CraftingPolicy.TryConsumeOnly(record, model, InventoryWire.CategoryLookup, out string reason))
            {
                Console.WriteLine("[info] station craft rejected (entity " + entityId + ", recipe "
                    + record.SchematicId + "): " + reason);
                PushCraftingState(player, pushTarget, session,
                    update => update.AddCraftingValidationFailed(new CraftingValidationFailed(reason)));
                InventoryPush.Push(entityId, "station craft rejected");
                return;
            }

            InventoryPush.Push(entityId, "crafted " + record.ItemType);

            long? spawned = LoosePartSpawner.Spawn(stationEntityId, part);

            for (int i = 0; i < session.Slots.Length; i++)
            {
                session.Slots[i] = new SlotHold { Amount = 0, MaterialTypeId = "" };
            }

            string schematicId = record.SchematicId;
            PushCraftingState(player, pushTarget, session, update =>
            {
                update.AddCraftingStarted(new CraftingStarted(-1, schematicId));
                update.AddCraftingCompleted(new CraftingCompleted(schematicId));
            });

            Console.WriteLine("[info] entity " + entityId + " station-crafted loose part '" + record.ItemType
                + "' at station " + stationEntityId + " -> part entity "
                + (spawned?.ToString() ?? "none") + ".");
        }

        private static void FailAdd( ENetPeerHandle player, long entityId, CraftSession session, string reason )
        {
            Console.WriteLine("[info] craft add rejected (entity " + entityId + "): " + reason);
            PushCraftingState(player, entityId, session,
                update => update.AddAddItemFromInventoryFailed(new AddItemFromInventoryFailed(reason)));
            // The client greyed the inventory when it began the drag; a 1081 push
            // is the only thing that ungreys it, so send one even though the bag
            // is unchanged.
            InventoryPush.Push(entityId, "craft add rejected");
        }

        /// <summary>
        /// Sends the player's current crafting state on 1005: the selected recipe,
        /// the slot reservations (a positionally-indexed list at least as long as
        /// the recipe's requirements), the closed countdown, and any events.
        /// Pushed only to the crafting player's own peer.
        /// </summary>
        private static void PushCraftingState( ENetPeerHandle player, long entityId, CraftSession session,
            Action<CraftingStationClientState.Update>? addEvents = null )
        {
            Improbable.Collections.List<SlottedMaterial> slotted = new Improbable.Collections.List<SlottedMaterial>();

            for (int i = 0; i < session.Slots.Length; i++)
            {
                SlotHold hold = session.Slots[i];
                RawMaterial rawMaterial = new RawMaterial(hold.MaterialTypeId ?? "", 0, "", new Map<string, string>());
                slotted.Add(new SlottedMaterial(i, rawMaterial, hold.Amount, new Option<RawMaterial>()));
            }

            CraftingStationClientState.Update update = new CraftingStationClientState.Update();
            update.SetClientSchematicId(session.SchematicId ?? "");
            update.SetSchematicOwner("");
            update.SetSlottedMaterials(slotted);
            update.SetItemReadyInSeconds(-1);
            update.SetCurrentWeight(0f);

            addEvents?.Invoke(update);

            SendOPHelper.SendComponentUpdateOp(player, entityId, new System.Collections.Generic.List<uint> { 1005 }, new System.Collections.Generic.List<object> { update });
        }
    }
}
