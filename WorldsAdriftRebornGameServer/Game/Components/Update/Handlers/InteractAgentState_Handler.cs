using Bossa.Travellers.Interact;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1211 InteractAgentState - the player's per-frame "what am I looking at, which
     * hotbar slot is selected, is the use key down" state.
     *
     * This handler does TWO event-driven jobs, both on the sender's OWN player entity
     * and both gated behind WAREBORN_PLACEMENT=1:
     *
     *   1. DEPLOY TRIGGER (useItemKeyPressed): the decompiled client has no dedicated
     *      "start placing" command. The closest observable signal is 1211's
     *      UseItemKeyPressed event, which fires once on use-key-DOWN and carries the
     *      selected hotbar slot. When the player has a deployable on that slot and
     *      presses use, we send them 1019 StartPlacingItemEvent (via PlacementService).
     *
     *   2. INTERACT-OPEN (interactWithObject): when the player completes a timed
     *      interaction on a world object, the client fires
     *      TriggerInteractWithObject(target, verb) on its own 1211 - VERIFIED at
     *      InteractAgentObserver.IssueInteraction. For a placed shipyard's centre
     *      console the verb is Craft. The ship-build UI does NOT open off that event
     *      directly: CraftingStationBehaviour opens it only when the SHIPYARD's 1005
     *      emits PlayerStartCrafting for the local player. So we translate the Craft
     *      interaction into that 1005 echo (PlacementService.OpenShipyardConsole).
     *
     * WHY THIS IS SAFE despite 1211 being a per-frame stream:
     *   - The handler early-returns when BOTH event lists are empty, which is every
     *     frame except a key-down or an interaction completion. The per-frame
     *     LookingAt/slot DATA is ignored. So the per-frame cost is two empty checks.
     *   - It never relays anything and never mutates inventory; it only starts a
     *     preview or opens a UI on the sender's OWN client.
     *   - It is a no-op unless WAREBORN_PLACEMENT=1.
     *
     * RESIDUAL RISK (needs a live client to settle, documented in the report):
     *   - that UseItemKeyPressed fires for a placeable non-tool hotbar item and that
     *     CurrentItemSlot matches HotBarSlotNum (the debug file trigger proves the
     *     rest of the pipeline regardless);
     *   - that the client actually SENDS the Craft interactWithObject: the client
     *     gates the interact behind _isShipBuildingAware (InteractAgentObserver), so a
     *     player who is not yet shipbuilding-aware is shown a "no crafting yet" chat
     *     message and NO 1211 event is sent - the server never sees it and cannot
     *     open the UI. This is a client onboarding gate, not a missing seed.
     */
    [RegisterComponentUpdateHandler]
    internal class InteractAgentState_Handler : IComponentUpdateHandler<InteractAgentState, InteractAgentState.Update, InteractAgentState.Data>
    {
        public InteractAgentState_Handler() { Init(1211); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            InteractAgentState.Update clientComponentUpdate, InteractAgentState.Data serverComponentData)
        {
            // A DELTA: the vast majority of 1211 packets carry only look/slot data and
            // no event. Read the event lists straight off the update and get out fast
            // when there is nothing to act on - this runs at frame rate.
            Improbable.Collections.List<UseItemKeyPressed>? presses = clientComponentUpdate.useItemKeyPressed;
            Improbable.Collections.List<InteractWithObject>? interacts = clientComponentUpdate.interactWithObject;
            Improbable.Collections.List<ReleaseInteraction>? releases = clientComponentUpdate.releaseInteraction;
            bool noPress = presses == null || presses.Count == 0;
            bool noInteract = interacts == null || interacts.Count == 0;
            bool noRelease = releases == null || releases.Count == 0;
            if (noPress && noInteract && noRelease)
            {
                return;
            }

            // Only the sender's OWN entity: 1211 is the player's own interact state
            // (rule 6). This ownership fact is what the atlas pickup policy is handed and
            // what the placement paths below require.
            ulong peerId = PeerIdentity.IdOf(player);
            bool ownsPlayer = WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId);

            // ATLAS SHARD PICKUP is ALWAYS active - NOT gated behind WAREBORN_PLACEMENT -
            // because the acquisition loop must work in a plain deposit session. When the
            // client completes a PickUp interaction on a released shard it fires
            // TriggerInteractWithObject(shard, PickUp) here (findings-atlas-shards §2
            // Phase C); the server validates + grants in the pure-policy transaction
            // WorldsAdriftRebornGameServer.TryCollectAtlasShard. Ownership and verb are
            // handed to that policy rather than short-circuited here, so the single gate
            // is the policy. Other verbs/targets fall through to the placement paths.
            if (!noInteract)
            {
                foreach (InteractWithObject pickup in interacts!)
                {
                    if (pickup.verb != InteractVerb.PickUp)
                    {
                        continue;
                    }
                    long pickupTarget = pickup.target.Id;

                    // An ATLAS SHARD: mine-loose-then-pick-up, grants the atlas shard.
                    if (WorldsAdriftRebornGameServer.AtlasShards.IsShard(pickupTarget))
                    {
                        Multiplayer.AtlasPickupOutcome outcome =
                            WorldsAdriftRebornGameServer.TryCollectAtlasShard(
                                entityId, pickupTarget, ownsPlayer, verbIsPickUp: true);
                        if (outcome != Multiplayer.AtlasPickupOutcome.Grant)
                        {
                            Console.WriteLine("[info] atlas shard PickUp by entity " + entityId
                                + " on " + pickupTarget + " not granted: " + outcome + ".");
                        }
                        continue;
                    }

                    // A PLACED STATION (shipyard / Assembly Station): the non-retail
                    // "pack it back into inventory" extension. The client mod's
                    // dedicated hold-to-pack key (StationPickup_Patch) issues the SAME
                    // native TriggerInteractWithObject(station, PickUp) the shard path
                    // uses; the server validates + grants in the pure-policy
                    // transaction TryPickUpPlacedStation (StationPickupPolicy is the
                    // single gate - ownership and verb are handed to it, exactly like
                    // the shard dispatch above). The tombstone clause routes a
                    // duplicate event on a just-packed station to the same transaction
                    // so it is REJECTED with a named reason instead of falling through
                    // silently. Every decision gets one greppable [pickup] line.
                    if (Placement.PlacedShipyards.IsPlacedShipyard(pickupTarget)
                        || Placement.PlacedCraftingStations.IsPlacedCraftingStation(pickupTarget)
                        || Multiplayer.Placement.StationPickupLedger.Shared.IsPickedUp(pickupTarget))
                    {
                        Multiplayer.StationPickupOutcome outcome =
                            WorldsAdriftRebornGameServer.TryPickUpPlacedStation(
                                player, entityId, pickupTarget, ownsPlayer, verbIsPickUp: true);
                        Console.WriteLine("[pickup] station PickUp by entity " + entityId
                            + " on " + pickupTarget + " -> " + outcome + ".");
                        continue;
                    }

                    // NOTE: a FUEL CANISTER is deliberately NOT handled here. Retail fuel
                    // is SALVAGED with the gauntlet beam, not picked up, so its shots
                    // arrive on 2106 (MultitoolSalvagerState_Handler -> OnSalvageShot ->
                    // OnFuelCanisterShot) and it advertises no 1210 prompt at all.
                }

                // HELM FLIGHT (verb Man): the pilot takes - or, re-manning, leaves -
                // the helm of their built ship. ALWAYS-ON like the atlas pickup, NOT
                // behind WAREBORN_PLACEMENT: the flight service carries its own
                // WAREBORN_HELM_FLIGHT gate and answers false for any target that is
                // not a mounted helm, so this dispatch costs one ledger miss for
                // every other Man. Ownership is handed to the service, not
                // short-circuited here, so the single gate is the service.
                foreach (InteractWithObject man in interacts!)
                {
                    // THE WILDERNESS SHRINE: the exit from Haven. Checked FIRST and
                    // short-circuited, because the shrine answers to three verbs
                    // (its baked one is Activate - RECOVERED from the prefab, see
                    // WildernessShrine.Verbs for why the other two are still served)
                    // and two of them, Man and Activate, are also the helm's and the
                    // mounted part's. Letting a shrine interaction fall through to
                    // those would cost two ledger misses and a confusing log line;
                    // letting a HELM interaction reach the shrine would be far
                    // worse, which is why the target key is what selects, not the
                    // verb. ALWAYS-ON, like the atlas pickup and the helm above:
                    // graduating is not a placement feature.
                    //
                    // Owner-only. It moves the SENDER's character - and, on a fresh
                    // crew island, writes their crewmates' home rows - so a peer
                    // must never be able to fire it for somebody else's entity.
                    string? targetKey =
                        WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(man.target.Id)?.Key;

                    // EVERY completed interaction gets ONE line naming what the
                    // client actually sent. This costs one log line per E press in
                    // the whole world, and it is the line that was missing on
                    // 2026-08-18 when a player held E on the shrine and the server
                    // could not say whether it had received an interaction at all,
                    // let alone which verb or which target. Interact events are
                    // rare - the per-frame 1211 look/slot stream returns long before
                    // here - so this is not a rate concern.
                    Console.WriteLine("[interact] entity " + entityId + " -> target "
                        + man.target.Id + " (" + (targetKey ?? "not a world entity") + ")"
                        + " verb " + man.verb + "(" + (int)man.verb + ")"
                        + " owns=" + ownsPlayer + ".");

                    // Default is the client's empty-target interaction boundary.
                    // Re-arm stateful Activate parts before verb-specific routing;
                    // the dedicated Activate key-up bridge below reaches the same
                    // method through ReleaseInteraction. An invalid/default target
                    // releases every held target for this player.
                    if (man.verb == InteractVerb.Default && ownsPlayer)
                    {
                        WorldsAdriftRebornGameServer.PartInteractions.OnInteractionReleased(
                            entityId, man.target.Id);
                    }

                    Multiplayer.Wilderness.ShrineInteractOutcome shrine =
                        Multiplayer.Wilderness.ShrineInteractRouting.Decide(
                            ownsPlayer, (int)man.verb, targetKey);

                    if (Multiplayer.Wilderness.ShrineInteractRouting.IsAboutTheShrine(shrine))
                    {
                        // Aimed at the shrine, so it gets an answer either way. A
                        // refusal that logs nothing is what made this bug invisible.
                        Console.WriteLine("[interact] shrine: "
                            + Multiplayer.Wilderness.ShrineInteractRouting.Explain(shrine) + ".");
                    }

                    if (shrine == Multiplayer.Wilderness.ShrineInteractOutcome.Use)
                    {
                        WildernessGraduationService.Use(entityId);
                        continue;
                    }

                    if (Multiplayer.Wilderness.ShrineInteractRouting.IsAboutTheShrine(shrine))
                    {
                        // It named the shrine and was refused; do NOT let it fall
                        // through to the helm or mounted-part paths and pick up a
                        // second, more confusing log line.
                        continue;
                    }

                    if (man.verb == InteractVerb.Inventory)
                    {
                        // OPENING A CONTAINER. ALWAYS-ON like the atlas pickup
                        // and the shrine, NOT behind WAREBORN_PLACEMENT: searching
                        // an island is not a placement feature.
                        //
                        // TWO KINDS ANSWER THIS VERB and each service answers false
                        // for the other's targets, so the order is arbitrary and the
                        // cost of a miss is one dictionary lookup:
                        //   * a ruin chest, identified by the loot ledger;
                        //   * a crafted trunk/mountedBox/storageContainer/
                        //     shippingContainer bolted to a ship, identified by the
                        //     loose-part catalogue.
                        // If BOTH refuse, the client pressed E on something whose
                        // prompt we never advertised, and the log line above already
                        // names the target.
                        //
                        // Owner-gated: the echo names the OPENING player's entity id
                        // and the client compares it against its own, so firing it
                        // for somebody else's entity would open a panel on a peer
                        // who did not press anything.
                        if (ownsPlayer)
                        {
                            if (!Loot.LootService.OpenContainer(player, entityId, man.target.Id))
                            {
                                ShipContainerService.OpenContainer(player, entityId, man.target.Id);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[warning] [loot] 1211 Inventory interact for entity "
                                + entityId + " from a peer that does not own it; ignored.");
                        }
                    }
                    else if (man.verb == InteractVerb.Man)
                    {
                        WorldsAdriftRebornGameServer.Flight.OnManInteraction(
                            player, entityId, man.target.Id, ownsPlayer);
                    }
                    else if (man.verb == InteractVerb.Activate)
                    {
                        // MOUNTED PART ACTIVATE (sail furl/unfurl, lamp switch, horn
                        // honk): ALWAYS-ON like the Man dispatch above - the service's
                        // per-part ledgers are the single gate, so an Activate on
                        // anything else costs three dictionary misses. The Activate
                        // verb is baked into the Sail01/Lamp01/Horn01 prefabs' own
                        // InteractiveObjectVisualizer (decompile-verified), and the
                        // prompt only appears once the 1210 mounted-part branch
                        // advertises a matching entry, so an unhandled Activate here
                        // is a client poking at something not interactable yet.
                        bool handled = WorldsAdriftRebornGameServer.PartInteractions
                            .OnActivateInteraction(entityId, man.target.Id, ownsPlayer);
                        if (!handled)
                        {
                            Console.WriteLine("[info] 1211 Activate on target " + man.target.Id
                                + " matched no mounted sail/lamp/horn ledger; ignored.");
                        }
                    }
                    else if (man.verb == InteractVerb.Default && man.target.Id <= 0 && ownsPlayer)
                    {
                        // THE PILOT'S ACTUAL EXIT, measured live: a seated pilot pressing
                        // the interact key produced `verb Default on target -1` events -
                        // NOT the ReleaseInteraction the design expected and NOT a re-Man
                        // (while driving, the player is not aiming at the helm's collider,
                        // so the client has no target and sends the default verb with an
                        // invalid id). Route it to the release path, which dismounts a
                        // seated pilot and is a no-op for everyone else - so "E gets me
                        // off the helm" finally holds.
                        WorldsAdriftRebornGameServer.Flight.OnReleaseInteraction(
                            player, entityId, man.target.Id);
                    }
                }
            }

            // The client's OWN dismount signal: TriggerReleaseInteraction from
            // InteractAgentObserver.ReleaseInteractiveObject. Belt-and-braces beside
            // the re-Man toggle - whichever the live client sends, the pilot gets
            // off. Events only, a handful per session; ignored for non-pilots.
            if (!noRelease && ownsPlayer)
            {
                foreach (ReleaseInteraction release in releases!)
                {
                    WorldsAdriftRebornGameServer.PartInteractions.OnInteractionReleased(
                        entityId, release.interactEntityId.Id);
                    WorldsAdriftRebornGameServer.Flight.OnReleaseInteraction(
                        player, entityId, release.interactEntityId.Id);
                }
            }

            // Everything below is placement-only and gated behind WAREBORN_PLACEMENT.
            if (!WorldsAdriftRebornGameServer.Placement.Enabled)
            {
                return;
            }

            // The placement paths act only on the sender's OWN entity.
            if (!ownsPlayer)
            {
                return;
            }

            // INTERACT-OPEN: the player completed an interaction on a world object. For
            // a placed shipyard's console (verb Craft) echo 1005 PlayerStartCrafting so
            // their client opens the ship-build UI. Other verbs/targets are ignored here.
            if (!noInteract)
            {
                foreach (InteractWithObject interact in interacts!)
                {
                    // DIAGNOSTIC (events only - a handful per session, never per-frame):
                    // the live "press E on the station, nothing happens" report needs the
                    // failure NAMED. One line per completed interaction event: verb,
                    // target, and ownership - so a silent both-ledgers-miss below is
                    // visible instead of a mystery.
                    Console.WriteLine("[info] 1211 interact: entity " + entityId + " -> verb "
                        + interact.verb + " on target " + interact.target.Id
                        + " (ownsPlayer=" + ownsPlayer + ").");

                    if (interact.verb == InteractVerb.Craft)
                    {
                        // Both a placed shipyard and a placed Assembly Station bake the
                        // Craft verb, and BOTH open off the same 1005 PlayerStartCrafting
                        // echo - the prefab's baked category decides ship-build vs parts.
                        // Try the shipyard first (it also notes the frame-design console);
                        // it returns false when the target is not a shipyard we placed, so
                        // fall through to the crafting station. Each guard checks its own
                        // ledger, so only the matching one answers.
                        long target = interact.target.Id;
                        bool opened = WorldsAdriftRebornGameServer.Placement.OpenShipyardConsole(
                            player, entityId, target);
                        bool openedStation = false;
                        if (!opened)
                        {
                            openedStation = WorldsAdriftRebornGameServer.Placement.OpenCraftingStationConsole(
                                player, entityId, target);
                        }
                        if (!opened && !openedStation)
                        {
                            // Both ledgers missed - previously a SILENT no-op, which reads
                            // to the player as "I press E and nothing happens". Name it.
                            Console.WriteLine("[warning] 1211 Craft on target " + target
                                + " matched NEITHER the placed-shipyard nor the crafting-station"
                                + " ledger - the console cannot open. Check the target id against"
                                + " the placement RESTORE lines for this boot.");
                        }
                    }
                    else if (interact.verb == InteractVerb.ReclaimShip)
                    {
                        Multiplayer.Ship.ShipSalvageReject outcome =
                            Game.Crafting.ShipSalvageService.Reclaim(
                                entityId, interact.target.Id, ownsPlayer);
                        Console.WriteLine("[salvage] ReclaimShip by entity " + entityId
                            + " on shipyard " + interact.target.Id + " -> " + outcome + ".");
                    }
                }
            }

            if (noPress)
            {
                return;
            }

            // Already mid-placement: ignore further use-presses until it resolves or
            // the session times out (PlacementService guards this too, but skipping
            // the inventory lookup keeps the frame cheap).
            if (WorldsAdriftRebornGameServer.Placement.IsPlacing(entityId))
            {
                return;
            }

            foreach (UseItemKeyPressed press in presses!)
            {
                int slot = press.itemSlot; // CurrentItemSlot, the selected hotbar slot 0-7
                InventoryModel model = InventoryService.ForEntity(entityId);
                InventoryItem? selected = model.OnHotBar(slot);

                if (selected != null
                    && Multiplayer.Placement.Deployables.IsDeployable(selected.ItemTypeId))
                {
                    Console.WriteLine("[info] placement: entity " + entityId + " pressed use on a '"
                        + selected.ItemTypeId + "' in hotbar slot " + slot + " (item " + selected.ItemId
                        + "); starting placement.");
                    WorldsAdriftRebornGameServer.Placement.StartPlacing(player, entityId, selected.ItemId, selected.ItemTypeId);
                    return;
                }
            }
        }
    }
}
