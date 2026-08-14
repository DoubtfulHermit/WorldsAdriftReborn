using System;
using System.Runtime.InteropServices;
using Bossa.Travellers.Alliance;
using Bossa.Travellers.Analytics;
using Bossa.Travellers.Clock;
using Bossa.Travellers.Controls;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Devconsole;
using Bossa.Travellers.Ecs;
using Bossa.Travellers.Interact;
using Bossa.Travellers.Inventory;
using Bossa.Travellers.Items;
using Bossa.Travellers.Loot;
using Bossa.Travellers.Misc;
using Bossa.Travellers.Motion.Prediction;
using Bossa.Travellers.Player;
using Bossa.Travellers.Refdata;
using Bossa.Travellers.Rope;
using Bossa.Travellers.Salvaging;
using Bossa.Travellers.Scanning;
using Bossa.Travellers.Ship;
using Bossa.Travellers.Ship.Lock;
using Bossa.Travellers.Social;
using Bossa.Travellers.Weather;
using Bossa.Travellers.World;
using Bossa.Travellers.Utilityslot;
using Improbable;
using Improbable.Collections;
using Improbable.Corelib.Metrics;
using Improbable.Corelib.Worker.Checkout;
using Improbable.Corelibrary.Activation;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using Improbable.Corelibrary.Transforms.Global;
using Improbable.Math;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components
{
    internal class ComponentsSerializer
    {
        /// <summary>
        /// Whether an entity is the REQUESTING peer's OWN player avatar - the only
        /// thing the loading barrier ever holds. The barrier seeds (190000 Requested,
        /// 190002 IsActive=false) must land on the joining peer's own player and
        /// nothing else: a mirrored REMOTE player also looks like "a player" to the
        /// registry, but seeding its Activated false on the observing client would
        /// freeze the wrong avatar, and a world entity must never be seeded Requested
        /// at all. Ownership is the exact "is this your own avatar" test used across
        /// the setup path.
        /// </summary>
        private static bool IsOwnPlayerEntity(ENetPeerHandle player, long entityId) =>
            WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId);

        public unsafe static Multiplayer.ComponentSeedOutcome InitAndSerialize(ENetPeerHandle player, long entityId, uint componentId, byte** buffer, uint* length)
        {
            // THREE DIFFERENT THINGS LOOK IDENTICAL TO A CALLER THAT ONLY READS
            // LENGTH, and all three come back as 0:
            //
            //   a) the id has no vtable in this client build at all, so the loop
            //      below never enters and nothing is written;
            //   b) the id has a vtable but no seed branch here, which logs
            //      "[ToDo] unhandled component id";
            //   c) the entity DOES NOT HAVE the component and we know it.
            //
            // (a) means the component does not exist in the shipped client and no
            // amount of writing branches will help; (b) means write a branch; (c)
            // means nothing is wrong at all. A caller with
            // failOnComponentInitError set must lose its ENTIRE batch for (a) and
            // (b) and must NOT lose it for (c), so the answer is an outcome, not
            // a length. See Multiplayer.ComponentAbsencePolicy.
            //
            // (c) is checked FIRST and before the vtable scan on purpose: whether
            // the shipped client happens to know the id is irrelevant to whether
            // our entity has it, and answering "you asked, it isn't there" is the
            // thing real SpatialOS does and this server never could.
            if (Multiplayer.ComponentAbsencePolicy.IsKnownAbsent(componentId))
            {
                Console.WriteLine(Multiplayer.ComponentAbsencePolicy.DescribeKnownAbsent(entityId, componentId));
                return Multiplayer.ComponentSeedOutcome.KnownAbsent;
            }

            bool hasClientVtable = false;
            Multiplayer.ComponentSeedOutcome outcome = Multiplayer.ComponentSeedOutcome.NoClientVtable;

            for(int i = 0; i < ComponentsManager.Instance.ClientComponentVtables.Length; i++)
            {
                if (ComponentsManager.Instance.ClientComponentVtables[i].ComponentId == componentId)
                {
                    hasClientVtable = true;

                    ulong refId = 0;
                    object obj = null;

                    // A component whose server-side state is LIVE must be
                    // re-served as it is now, not rebuilt from defaults.
                    //
                    // Everything below this point fabricates a fresh component
                    // and the bookkeeping at the bottom of the method then
                    // OVERWRITES any reference already stored for
                    // (peer, entity, component) - and the "already set up"
                    // branch in the interest handler re-serves whatever a client
                    // asks for, forever. For a stateless seed that is merely
                    // wasteful. For the inventory it was destructive: a second
                    // interest request threw the player's items away.
                    //
                    // Serving the existing reference also keeps the reference
                    // ITSELF stable, so every handle already held by the push
                    // seam stays valid.
                    if (IsLiveState(componentId)
                        && GameState.Instance.ComponentMap.TryGetValue(player, out var liveByEntity)
                        && liveByEntity.TryGetValue(entityId, out var liveByComponent)
                        && liveByComponent.TryGetValue(componentId, out ulong liveRefId))
                    {
                        ComponentProtocol.ClientObject liveWrapper = new ComponentProtocol.ClientObject();
                        liveWrapper.Reference = liveRefId;

                        ComponentProtocol.ClientSerialize liveSerialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientSerialize>(ComponentsManager.Instance.ClientComponentVtables[i].Serialize);
                        liveSerialize(componentId, 2, &liveWrapper, buffer, length);

                        Console.WriteLine("[info] re-serving live component " + componentId + " of entity "
                            + entityId + " instead of re-seeding it.");
                        return *length > 0
                            ? Multiplayer.ComponentSeedOutcome.Serialized
                            : Multiplayer.ComponentSeedOutcome.SerializeFailed;
                    }

                    if(componentId == 8065)
                    {
                        Blueprint.Data bData = new Blueprint.Data(new BlueprintData("Player"));
                        obj = bData;
                    }
                    else if(componentId == 190602)
                    {
                        // THE ONE FIELD THAT PLACES ANYTHING IN THIS WORLD.
                        //
                        // This branch used to hand every entity the same
                        // localPosition, {0, 100, 0} = (0, 0.024, 0) m - the world
                        // origin. It only ever looked correct because the island was
                        // at the origin too. With Haven that is fatal rather than
                        // untidy: Haven is ONE asset placed at TWELVE world
                        // positions, so there is no default that is right for
                        // anybody, and the island and the player must be told apart.
                        //
                        // Which entity this is comes from the entity id, which this
                        // method has always received (1088 already used it). The
                        // decision itself is in Multiplayer.SpawnPolicy so the
                        // coordinates are unit-tested natively rather than by
                        // staring at a game client.
                        //
                        // parent is ABSENT for everything EXCEPT a bolted ship part.
                        // With it absent the client's live branch runs,
                        // transform.position = localPosition / 4096 - OffsetOrigin,
                        // which is what we have empirically proven works for islands,
                        // players, trees, nodes and the hull itself. Do not add a
                        // parent to any of those - see docs/research/findings-world.md Q4.
                        //
                        // A bolted PART (deck, helm, engine, sail) is the exception and
                        // MUST carry a parent, or it stands still in world space while
                        // the hull's PathFollower micro-adjusts the hull every frame and
                        // the player falls THROUGH the drifting deck. See the
                        // parent-present block just below and Multiplayer.BoltedPartTransform.
                        //
                        // And this is a SEED, consumed once at OnEnable. It must
                        // never be re-sent to a live entity: for a player that is a
                        // teleport (now a 17 km out-of-world drop, not a nuisance),
                        // and for the island IslandLocalTransformVisualizer does not
                        // teleport at all - it starts a 5-second smoothstep slide
                        // that drags the terrain out from under everyone on it.
                        //
                        // WHERE the seed comes from is no longer a question this
                        // method answers. It used to ask SpawnPolicy "is this the
                        // island or a player?", which is a question with exactly
                        // two answers and therefore a ceiling of two kinds of
                        // thing in the world. It now asks the world-entity
                        // registry for THIS entity's position; a tree, a ship hull
                        // or a second island needs no branch here at all.
                        Multiplayer.FixedPointPosition seed =
                            WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(entityId);

                        // A depleted metal node stays in the registry (rule 1) and is
                        // still seeded to a late joiner - but SUNK, so the joiner sees
                        // it gone exactly as everyone already present does. Sink() is
                        // the same pure function the live depletion broadcast uses
                        // (WorldsAdriftRebornGameServer.BroadcastNodeDepletion), so the
                        // two agree without the server storing a second coordinate.
                        // A destroyed NUGGET sinks under the terrain (it has no
                        // depletion feedback of its own). A destroyed DEPOSIT does NOT
                        // sink - it stays anchored and shows its destroyed crust/core
                        // (2103 isDestroyed + 12283 exploded, seeded below), exactly as
                        // the shipped deposit does. So only sink a nugget.
                        if (WorldsAdriftRebornGameServer.Nodes.IsDestroyed(entityId)
                            && WorldsAdriftRebornGameServer.Nodes.NodeOf(entityId)?.IsDeposit != true)
                        {
                            seed = Multiplayer.MetalNodes.Sink(seed);
                        }

                        // A COLLECTED atlas shard is gone: it has no RemoveEntityOp, so a
                        // late joiner is seeded the same sunk position everyone present was
                        // teleported to at collection (BroadcastShardCollected). A lodged or
                        // released (uncollected) shard keeps its real position.
                        if (WorldsAdriftRebornGameServer.AtlasShards.IsCollected(entityId))
                        {
                            seed = Multiplayer.MetalNodes.Sink(seed);
                        }

                        // An EMPTIED fuel canister is likewise gone: seed the late joiner
                        // the same sunk position everyone present was teleported to on the
                        // emptying salvage shot (BroadcastFuelCanisterDepleted). A
                        // part-salvaged canister keeps its real position - it is still
                        // there to shoot.
                        if (WorldsAdriftRebornGameServer.FuelCanisters.IsDepleted(entityId))
                        {
                            seed = Multiplayer.MetalNodes.Sink(seed);
                        }

                        // A PACKED station (shipyard / Assembly Station picked back up
                        // into inventory) is likewise gone: seed the late joiner the
                        // same sunk position everyone present was teleported to at
                        // pickup (BroadcastStationPickedUp). Its membership ledgers are
                        // removed at pickup, so the tombstone is the only thing that
                        // still knows the entity - the registry entry itself must stay
                        // (no RemoveEntityOp exists to retire it).
                        if (Multiplayer.Placement.StationPickupLedger.Shared.IsPickedUp(entityId))
                        {
                            seed = Multiplayer.MetalNodes.Sink(seed);
                        }

                        // A BOLTED SHIP PART is seeded hull-RELATIVE so it follows the
                        // moving hull instead of drifting. The localPosition becomes the
                        // part's offset FROM the hull and the parent names the hull with
                        // the "~" key that the client resolves as "relative to a moving
                        // parent" (FixedUpdateLerpLocalTransformBehaviour GetRelativeEntity
                        // :444, compose :365, MoveTransform :251 - a "~" parent is NOT a
                        // Unity re-parent, so no GameObject hierarchy changes). The hull's
                        // id is looked up WITHOUT allocating, exactly as the 8066 branch
                        // does; the hull is registered and spawned before its parts, so it
                        // is known by the time a client checks a part out. If it somehow is
                        // not yet known we fall back to the old world-absolute seed for
                        // this one serve - the part will re-seed relative on a later
                        // checkout - rather than parent it to a hull id that does not exist.
                        Multiplayer.FixedPointPosition localSeed = seed;
                        Improbable.Collections.Option<Parent> parent = default;

                        // A mounted part carries the player's PLACED hull-local rotation; captured
                        // here so the rotation seed below honors it (a "~"-parented part CAN carry a
                        // non-identity local rotation - the 1070 commit's own wake proves it).
                        uint? mountedPartRotation = null;

                        string? partKey =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key;
                        if (Multiplayer.WorldEntities.IsBoltedPartKey(partKey))
                        {
                            long? hullEntityId = WorldsAdriftRebornGameServer.WorldEntities
                                .BoundEntityIdFor(Multiplayer.WorldEntities.ShipFrameKey);
                            Multiplayer.FixedPointPosition? hullSeed = WorldsAdriftRebornGameServer.WorldEntities
                                .ByKey(Multiplayer.WorldEntities.ShipFrameKey)?.Position;

                            if (hullEntityId.HasValue && hullSeed.HasValue)
                            {
                                localSeed = Multiplayer.BoltedPartTransform.LocalOffset(seed, hullSeed.Value);
                                // The DECK gets a REAL hierarchy key (a Unity child of the
                                // hull, so a player on it raycasts the hull's rigidbody and
                                // is carried); every other part gets the "~" follow. One
                                // source of that decision: BoltedPartTransform.HierarchyKeyFor.
                                string hierarchyKey = Multiplayer.BoltedPartTransform.HierarchyKeyFor(partKey);
                                parent = ShipPartTransform.RelativeParent(hullEntityId.Value, hierarchyKey);

                                Console.WriteLine("[info] seeding 190602 for bolted part " + entityId + " ("
                                    + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                                    + ") parent=" + hierarchyKey + " of hull " + hullEntityId.Value
                                    + " at local offset " + localSeed + ".");
                            }
                            else
                            {
                                Console.WriteLine("[warn] 190602 for bolted part " + entityId
                                    + " but the hull id is not known yet; seeding world-absolute this once.");
                            }
                        }

                        // A MOUNTED loose part (attached to a BUILT ship via the 1070 flow):
                        // re-seed it already riding the hull - Parent(hullId, "~") + the
                        // stored hull-local offset - so a re-checkout shows it bolted on
                        // rather than loose. VALUE-equivalent to the wake the commit sent.
                        if (!parent.HasValue && Game.Crafting.MountedParts.Is(entityId))
                        {
                            Game.Crafting.MountedParts.Mount? mount = Game.Crafting.MountedParts.MountFor(entityId);
                            if (mount.HasValue)
                            {
                                localSeed = mount.Value.LocalOffset;
                                mountedPartRotation = mount.Value.PackedRotation;
                                parent = ShipPartTransform.RelativeParent(
                                    mount.Value.HullEntityId, Multiplayer.BoltedPartTransform.RelativeSlotKey);
                                Console.WriteLine("[info] seeding 190602 for MOUNTED part " + entityId
                                    + " parent=~ of hull " + mount.Value.HullEntityId
                                    + " at local offset " + localSeed + ".");
                            }
                        }

                        // A BUILT SHIP'S DECK: make it a genuine Unity CHILD of its built
                        // hull (Parent(hullId, "deck"), the same real hierarchy key the
                        // static test deck uses) so the client's AttachToShip HasParentEntity
                        // gate accepts it as a placement SURFACE - the part-mount make-or-break.
                        // Without a real-key parent the deck is world-absolute, HasParentEntity
                        // returns false, and a player can place NOTHING on the built ship.
                        if (!parent.HasValue && Game.Crafting.BuiltShips.IsBuiltDeck(entityId))
                        {
                            string? deckKey = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key;
                            string? hullKey = Multiplayer.Ship.BuiltShipPlacement.HullKeyForDeckKey(deckKey);
                            long? builtHullId = hullKey == null
                                ? null
                                : WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(hullKey);
                            Multiplayer.FixedPointPosition? builtHullSeed = hullKey == null
                                ? null
                                : WorldsAdriftRebornGameServer.WorldEntities.ByKey(hullKey)?.Position;
                            if (builtHullId.HasValue && builtHullSeed.HasValue)
                            {
                                localSeed = Multiplayer.BoltedPartTransform.LocalOffset(seed, builtHullSeed.Value);
                                parent = ShipPartTransform.RelativeParent(builtHullId.Value, Multiplayer.Deck.HierarchyKey);
                                Console.WriteLine("[info] seeding 190602 for BUILT DECK " + entityId
                                    + " parent=" + Multiplayer.Deck.HierarchyKey + " (Unity child) of built hull "
                                    + builtHullId.Value + " at local offset " + localSeed + ".");
                            }
                            else
                            {
                                Console.WriteLine("[warn] 190602 for built deck " + entityId
                                    + " but its built hull id is not known yet; seeding world-absolute this once.");
                            }
                        }

                        // Built through the shared Game.ShipPartTransform so this seed
                        // and the wake heartbeat (ShipPartMotionService) can never carry
                        // different transforms - a seed with a parent and a wake without
                        // it would place the part right once and snap it to the origin
                        // on the first heartbeat.
                        // Rotation: a bolted part keeps the identity sentinel (its
                        // facing rides its "~" parent), but a parentless registered
                        // world entity - a placed shipyard - carries the yaw the
                        // placing player chose, packed into its registration. For
                        // everything that never set a rotation, RotationSeedFor
                        // returns 1023 (identity), so this is byte-for-byte the old
                        // behaviour except for the one entity kind that opts in.
                        // A MOUNTED part honors its stored placed rotation (rides "~" but at the
                        // facing the player chose); any other parented part (a bolted deck/helm)
                        // keeps identity; a parentless registered entity (a placed shipyard) uses
                        // its registration yaw.
                        Quaternion32 rotationSeed = mountedPartRotation.HasValue
                            ? new Quaternion32(mountedPartRotation.Value)
                            : parent.HasValue
                                ? new Quaternion32(1023)
                                : new Quaternion32(WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(entityId));

                        TransformState.Data tData = ShipPartTransform.BuildSeed(
                            ShipPartTransform.LocalPosition(localSeed),
                            parent,
                            rotationSeed);

                        if (!parent.HasValue)
                        {
                            Console.WriteLine("[info] seeding 190602 for entity " + entityId + " ("
                                + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                                + ") at " + seed
                                + (rotationSeed.quaternion != 1023 ? " rot=" + rotationSeed.quaternion : "")
                                + ".");
                        }

                        obj = tData;
                    }
                    else if(componentId == 1017)
                    {
                        // ItemPlacingState: the client-authoritative confirm channel
                        // for deployable placement, seeded on the PLAYER. Its data is
                        // an empty struct (it carries only the PlaceItemEvent), so the
                        // seed is just "the component exists" - which is what lets the
                        // client's ItemPlacingBehaviour bind its 1017 WRITER once the
                        // authority grant lands. Env-gated: only injected when
                        // WAREBORN_PLACEMENT=1, so this branch never runs otherwise.
                        obj = new ItemPlacingState.Data();
                    }
                    else if(componentId == 1019)
                    {
                        // ItemPlacementAgentState: the server-owned placement agent on
                        // the PLAYER. Seeded idle (not placing); the server later writes
                        // a StartPlacingItemEvent onto it to drive the client into
                        // placement preview. The client only READS it.
                        obj = new ItemPlacementAgentState.Data(false, 0, "", false);
                    }
                    else if(componentId == 1205)
                    {
                        // ShipyardState: seeded on a PLACED shipyard world entity so the
                        // client's ShipyardVisualizer treats it as deployed (renders the
                        // legs/dome and calls Shipyard.Deploy()) rather than an inert
                        // prop. Owner/registration are read from the placed-structure
                        // ledger; docked ship is none, inactivity 0, initialised true.
                        //
                        // GATE A (shipyard build-access). registeredCharacterUids is what
                        // the client's ShipyardVisualizer.IsLocalPlayerRegistered checks
                        // with Contains(LocalPlayer.PlayerId) (ShipyardVisualizer.cs:27) -
                        // and LocalPlayer.PlayerId is the 1086 field2 stub
                        // LocalPlayerIdentity.PlayerId, NOT the character uid. So an OWNED
                        // yard registers that stub (per OwnershipRegistrationPolicy),
                        // never OwnerCharacterUid, or the player sees "Interact with
                        // shipyard to gain access" after relog. OwnerCharacterUid is still
                        // stored below (field10) for persistence and the 1206 owner.
                        Placement.PlacedShipyards.Seed shipyardSeed =
                            Placement.PlacedShipyards.SeedFor(entityId);

                        Improbable.Collections.List<string> registered =
                            new Improbable.Collections.List<string>();
                        foreach (string uid in Multiplayer.Ship.OwnershipRegistrationPolicy.ShipyardRegisteredUids(
                                     shipyardSeed.OwnerCharacterUid, Multiplayer.LocalPlayerIdentity.PlayerId))
                        {
                            registered.Add(uid);
                        }

                        // DockedShipId reports the built ship this shipyard produced (or
                        // an invalid EntityId 0 when empty), so a re-checkout of an
                        // occupied yard shows it docked and the ONE-ship-per-yard gate
                        // stays consistent with what the client sees. A live 1205 update
                        // is also pushed at spawn/undock (BuiltShipSpawner / the undock
                        // trigger) for clients already holding the shipyard in interest.
                        long dockedShipId = Crafting.BuiltShips.DockedShipFor(entityId);

                        obj = new ShipyardState.Data(
                            shipyardSeed.Active,
                            new EntityId(dockedShipId),
                            shipyardSeed.Deployed,
                            shipyardSeed.OwnerCharacterUid,
                            0,
                            true,
                            new Improbable.Collections.Map<string, EntityId>(),
                            registered);

                        Console.WriteLine("[info] seeding 1205 ShipyardState for placed shipyard entity "
                            + entityId + " (deployed=" + shipyardSeed.Deployed + ", active=" + shipyardSeed.Active
                            + ", owner='" + shipyardSeed.OwnerCharacterUid + "', dockedShip=" + dockedShipId + ").");
                    }
                    else if(componentId == 1114 && Game.Crafting.BuiltShips.IsBuiltHull(entityId))
                    {
                        // DockableState on a BUILT hull. The ShipFrame prefab bakes a
                        // DockableVisualizer (ShipPreprocessor.cs:103) that [Require]s 1114;
                        // without it that visualizer never enables, so the shipyard's
                        // ShipyardVisualizer.OnDockedShipChanged (which resolves the docked
                        // ship as GetComponent<DockableVisualizer>()) yields a DISABLED
                        // dockable and PlayerScannerTool.IsShipyardActive (Shipyard.DockedShip
                        // != null, plus its IsDocked/state reads) is unreliable. That check is
                        // the crafted-part lift's "active shipyard with a docked ship"
                        // precondition. Serve it docked=true with DockEntityId = the shipyard
                        // this hull was built at, so the client sees a real docked ship to
                        // place parts onto. The shipyard's own 1205 DockedShipId already points
                        // back here (BuiltShipSpawner.PushDockedShipId + the 1205 branch), so
                        // the two directions agree. Gated on IsBuiltHull so no other entity's
                        // 1114 request is answered here (it falls through to the normal path).
                        long dockShipyardId = Game.Crafting.BuiltShips.ShipyardForHull(entityId);
                        var hullPos = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Position;
                        Coordinates dockLocation = hullPos.HasValue
                            ? new Coordinates(hullPos.Value.MetresX, hullPos.Value.MetresY, hullPos.Value.MetresZ)
                            : new Coordinates(0, 0, 0);

                        bool isDocked = dockShipyardId != 0;
                        obj = new DockableState.Data(new EntityId(dockShipyardId), dockLocation, isDocked, false);

                        Console.WriteLine("[info] seeding 1114 DockableState for built hull entity " + entityId
                            + " (docked=" + isDocked + ", dockShipyard=" + dockShipyardId + ").");
                    }
                    else if(componentId == 1258 && Game.Crafting.BuiltShips.IsBuiltHull(entityId))
                    {
                        // ShipLiftState on a BUILT hull: the sky core's lift capacity.
                        // The one live consumer on the pilot path is
                        // ShipControlsBehaviour.UpdateVertical -> ShipLiftVisualizer
                        // .IsOverloaded (totalMass > TotalLift * AtlasMultiplier): if it
                        // reads overloaded, VERTICAL INPUT IS BLOCKED with the "Ship
                        // weighs more than its atlas sky core can lift" OSD. With 1258
                        // absent the visualizer's reader is null and TotalLift returns 0
                        // (null-guarded), leaving the check to whatever
                        // ParentingMassAdderVisualizer.totalMass happens to be - so a
                        // generous seed is the belt-and-braces that keeps climb working.
                        // No server mass model exists (1257 is known-absent), so the
                        // honest seed is "lift is not the limiting factor": a large
                        // totalLift, zero torque, reliable=true. VERIFIED ctor
                        // (gencode ShipLiftStateData: totalLift, totalTorque, reliable).
                        // Gated on IsBuiltHull so no other entity's 1258 is answered here.
                        obj = new ShipLiftState.Data(new ShipLiftStateData(
                            1000000f, new Improbable.Math.Vector3f(0f, 0f, 0f), true));

                        Console.WriteLine("[info] seeding 1258 ShipLiftState for built hull entity " + entityId
                            + " (totalLift=1e6): the sky core lifts, vertical input stays unblocked.");
                    }
                    else if(componentId == 190601)
                    {
                        TransformHierarchyState.Data thData = new TransformHierarchyState.Data(new TransformHierarchyStateData(new Improbable.Collections.List<Child> { }));

                        obj = thData;
                    }
                    else if(componentId == 1080)
                    {
                        SchematicsLearnerGSimState.Data schematicGsimData = new SchematicsLearnerGSimState.Data(new Improbable.Collections.List<string>(), new Map<string, string>(), false, new Improbable.Collections.List<string>());
                        obj = schematicGsimData;
                    }
                    else if(componentId == 1081)
                    {
                        // Seeded FROM THE STORE, not from a fixed seven-item
                        // list, using the same shape 1088 already uses with
                        // Appearances.Get above.
                        //
                        // The old branch built ItemHelper.GetDefaultItems()
                        // fresh on every serve, and the refId bookkeeping at the
                        // bottom of this method happily overwrites a stored
                        // reference - so a SECOND interest request for 1081 in
                        // one session silently reset the player's inventory to
                        // the seven defaults. Reading the store makes a re-seed
                        // idempotent instead of destructive; the sticky check at
                        // the top of this method then keeps even the reference
                        // itself stable.
                        //
                        // The grid is still 10x18 with a belt on row 3 because
                        // the client reads those four fields exactly once, at
                        // InventoryVisualiser.OnEnable - so they are a property
                        // of checkout, not something a later update can change.
                        Multiplayer.Inventory.InventoryModel inventory =
                            Inventory.InventoryService.ForEntity(entityId);

                        InventoryState.Data iData = new InventoryState.Data(new InventoryStateData(100,
                                                                                        "{}",
                                                                                        Inventory.InventoryWire.ToWireList(inventory),
                                                                                        Inventory.InventoryWire.ToStashList(inventory),
                                                                                        inventory.Width,
                                                                                        inventory.Height,
                                                                                        new Improbable.Collections.List<string> { },
                                                                                        inventory.HasBelt,
                                                                                        inventory.BeltRow));
                        obj = iData;
                    }
                    else if(componentId == 1086)
                    {
                        // field2_player_id is what the client exposes as LocalPlayer.PlayerId;
                        // sourced from the shared LocalPlayerIdentity so anything the server
                        // writes that the client compares against PlayerId (e.g. the ship
                        // editor's 1206 ownerPlayerId, gating SAVE) cannot drift from it.
                        PlayerName.Data pData = new PlayerName.Data(new PlayerNameData(
                            "sp00ktober", Multiplayer.LocalPlayerIdentity.PlayerId, "cUid", "bossaToken", "bossaId"));

                        obj = pData;
                    }
                    else if (componentId == 1088)
                    {
                        // Use the entity owner's PUBLISHED appearance when we have
                        // it (recorded by PlayerPropertiesState_Handler), so a
                        // remote mirror seeded after the owner spawned carries the
                        // real look. The hardcoded map is the legacy fallback for
                        // entities whose owner never published.
                        Map<string, string> customisation;
                        var stored = WorldsAdriftRebornGameServer.Appearances.Get(entityId);
                        if (stored != null)
                        {
                            customisation = new Map<string, string>();
                            foreach (var pair in stored)
                            {
                                customisation.Add(pair.Key, pair.Value);
                            }
                            Console.WriteLine("[info] seeding 1088 for entity " + entityId + " with published appearance.");
                        }
                        else
                        {
                            customisation = new Map<string, string>
                            {
                                {"Head", "hair_dreads" },
                                {"Body", "torso_ponchoVariantB" },
                                {"Feet", "legs_wrap" },
                                {"Face", "face_C" }
                            };
                        }

                        PlayerPropertiesState.Data ppData = new PlayerPropertiesState.Data(new PlayerPropertiesStateData(new Map<string, string> { },
                                                                                                                customisation,
                                                                                                                new Improbable.Collections.List<string> { },
                                                                                                                false));
                        obj = ppData;
                    }
                    else if(componentId == 1077)
                    {
                        HealthState.Data hData = new HealthState.Data(new HealthStateData(200, 200, true, 0f, true, new Improbable.Collections.List<EntityId> { }, 1f, 1f));

                        obj = hData;
                    }
                    else if(componentId == 1280)
                    {
                        WearableUtilsState.Data wData = new WearableUtilsState.Data(new WearableUtilsStateData(new Improbable.Collections.List<int> { }, new Improbable.Collections.List<float> { }, new Improbable.Collections.List<bool> { }));

                        obj = wData;
                    }
                    else if(componentId == 1210)
                    {
                        // The interaction prompt. InteractiveObjectVisualizer.OnEnable
                        // does Interactions.FirstOrDefault(i => i.verb == Verb); with
                        // NO entry naming the prefab's baked verb the radius and
                        // timeToUse fall to 0 and the prompt never appears
                        // (findings-metal-deposits.md). So one InteractionEntry
                        // naming that verb with a non-zero radius must be present.
                        // VERIFIED ctor shapes via ilspycmd on Generated.Code.dll
                        // (InteractiveStateData / InteractionEntry / enum
                        // Bossa.Travellers.Interact.InteractVerb { Default, Activate,
                        // PickUp, Man, ... } -> PickUp = 2, Man = 3).
                        //
                        // THREE prefabs ask for 1210 and they bake DIFFERENT verbs, so
                        // this branch is entity-aware exactly like 1099:
                        //   MetalNugget      -> PickUp ("E to pick up")
                        //   Helm01           -> Man    ("Man"), baked at
                        //                       HelmPreprocessor.SetVerb(InteractVerb.Man).
                        //   placed Shipyard  -> Craft  ("Craft"), the centre-console
                        //                       prompt that opens the ship-build UI.
                        //                       The Shipyard prefab bakes InteractVerb.Craft
                        //                       and CraftingStationBehaviour reacts to the
                        //                       1005 PlayerStartCrafting the server echoes
                        //                       back when the client fires this interaction
                        //                       (InteractAgentState_Handler).
                        // A single-verb seed served to the wrong prefab would leave
                        // its FirstOrDefault empty and its prompt dead.
                        //
                        // available TRUE, not in use by anyone; syncSchematics FALSE
                        // for the rock and helm. The shipyard is a crafting station, but
                        // syncSchematics stays FALSE for this milestone - the schematic
                        // catalogue sync (1271) is the full-ship-building follow-on and
                        // the UI opens without it. The three unused strings are empty,
                        // not null - copied by DeepCopy. Craft verb = 5 (VERIFIED).
                        bool isPlacedShipyard =
                            Placement.PlacedShipyards.IsPlacedShipyard(entityId);
                        // A placed Assembly Station bakes the SAME Craft verb as the
                        // shipyard console - the prefab's crafting category (not this
                        // seed) decides parts-vs-ship-build once the interact opens.
                        bool isPlacedCraftingStation = !isPlacedShipyard
                            && Placement.PlacedCraftingStations.IsPlacedCraftingStation(entityId);
                        bool isCraftStation = isPlacedShipyard || isPlacedCraftingStation;
                        // A helm is the STATIC test-ship helm OR any crafted helm part. The
                        // latter must receive Man even while still loose because
                        // InteractiveObjectVisualizer caches the matching entry only once,
                        // in OnEnable. Availability stays false until it is mounted.
                        // This is the one every real player has: the Helm01 prefab's
                        // InteractiveObjectVisualizer has verb Man BAKED, and it caches
                        // `Interactions.FirstOrDefault(i => i.verb == Verb)` at enable - so
                        // when this branch served the mounted helm the generic PickUp entry
                        // instead, that lookup found NOTHING and no E prompt could ever
                        // appear (live report: "i dont get the option to press e next to
                        // helm"). Lifting/re-mounting is untouched: parts are lifted with
                        // the SCANNER (1239), never the E interact.
                        bool isStaticHelm = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key
                            == Multiplayer.WorldEntities.HelmKey;
                        string? craftedPartItemType = Game.Crafting.LooseParts.DefFor(entityId)?.ItemType;
                        bool isHelm = !isCraftStation
                            && (isStaticHelm || craftedPartItemType == "helm");
                        // An ATLAS SHARD bakes the SAME PickUp verb as the nugget, but its
                        // availability is SERVER-GATED on release: available=false while the
                        // shard is lodged in its core (no prompt), flipped true when the core
                        // is destroyed (WorldsAdriftRebornGameServer.BroadcastShardReleased),
                        // and false again once collected. So a late joiner checking the shard
                        // out sees exactly the prompt state everyone present sees, without a
                        // separate replay. See findings-atlas-shards §2 Phase C.
                        bool isAtlasShard = !isCraftStation && !isHelm
                            && WorldsAdriftRebornGameServer.AtlasShards.IsShard(entityId);
                        // An INTERACTABLE PART (sail / lamp / horn): the part
                        // prefab's own InteractiveObjectVisualizer carries verb
                        // Activate SERIALIZED (decompile: GetTutorialStep maps
                        // Activate + Sail/Lamp/HornVisualizer to the per-part
                        // tutorial prompt), and OnEnable caches
                        // Interactions.FirstOrDefault(i => i.verb == Verb) - so
                        // without an Activate entry here the prompt can never
                        // appear, the exact trap the mounted helm's Man fix above
                        // documents. Keyed off the mount ledger's itemType through
                        // the pure PartInteractionPolicy (tested), which answers
                        // None for every part retail did not make interactable and
                        // for the parts whose interaction we cannot honestly serve
                        // yet (storage needs 1081, the reviver needs 1094) - so a
                        // prompt is never a lie. The correct Activate entry exists while
                        // loose but remains unavailable until the mount commit.
                        Multiplayer.Ship.PartVerb mountedPartVerb = Multiplayer.Ship.PartVerb.None;
                        if (!isCraftStation && !isHelm && !isAtlasShard)
                        {
                            Multiplayer.Ship.PartVerb seededVerb =
                                Multiplayer.Ship.PartInteractionPolicy.SeedVerbFor(craftedPartItemType);
                            if (seededVerb == Multiplayer.Ship.PartVerb.Activate)
                            {
                                mountedPartVerb = seededVerb;
                            }
                        }
                        // NOTE: a FUEL CANISTER deliberately has NO 1210 branch. Retail
                        // fuel is SALVAGED with the gauntlet beam, never picked up, so it
                        // must not advertise an interaction prompt at all - its gate is
                        // 1099 isSalvageable (see the 1099 branch below).

                        InteractionEntry entry;
                        string verbName;
                        bool available = true;
                        if (isCraftStation)
                        {
                            entry = new InteractionEntry(
                                InteractVerb.Craft,
                                Multiplayer.Placement.ShipyardInteraction.CraftRadius,
                                false, "", "", "", false,
                                Multiplayer.Placement.ShipyardInteraction.CraftTimeToUse);
                            verbName = "Craft";
                        }
                        else if (isHelm)
                        {
                            entry = new InteractionEntry(
                                InteractVerb.Man,
                                Multiplayer.Helm.ManRadius,
                                false, "", "", "", false,
                                Multiplayer.Helm.ManTimeToUse);
                            verbName = "Man";
                            // The prefab caches its Man entry at OnEnable, while the
                            // part is still loose. Keep that correct entry but gate it
                            // until the mount commit flips availability live.
                            available = isStaticHelm
                                || Multiplayer.Ship.PartInteractionPolicy.IsSeededInteractionAvailable(
                                    craftedPartItemType, Game.Crafting.MountedParts.Is(entityId));
                        }
                        else if (isAtlasShard)
                        {
                            entry = new InteractionEntry(
                                InteractVerb.PickUp,
                                Multiplayer.AtlasShardCatalogue.PickUpRadius,
                                false, "", "", "", false,
                                Multiplayer.AtlasShardCatalogue.PickUpTimeToUse);
                            verbName = "PickUp";
                            // Gated: only offer the prompt once the shard is mined loose,
                            // and never again once collected.
                            available = WorldsAdriftRebornGameServer.AtlasShards.IsAvailable(entityId);
                        }
                        else if (mountedPartVerb != Multiplayer.Ship.PartVerb.None)
                        {
                            // The mounted sail/lamp/horn Activate entry. radius 5 m
                            // (non-zero or no prompt, the ManRadius trap), timeToUse 0
                            // (instant - a light switch, not a hold). The E press comes
                            // back as a 1211 InteractWithObject(Activate) and is
                            // dispatched to PartInteractionService.
                            entry = new InteractionEntry(
                                (InteractVerb)(int)mountedPartVerb,
                                Multiplayer.Ship.PartInteractionPolicy.ActivateRadius,
                                false, "", "", "", false,
                                Multiplayer.Ship.PartInteractionPolicy.ActivateTimeToUse);
                            verbName = ((InteractVerb)(int)mountedPartVerb).ToString();
                            available = Multiplayer.Ship.PartInteractionPolicy.IsSeededInteractionAvailable(
                                craftedPartItemType, Game.Crafting.MountedParts.Is(entityId));
                        }
                        else
                        {
                            entry = new InteractionEntry(
                                InteractVerb.PickUp,
                                Multiplayer.MetalNodes.PickUpRadius,
                                false, "", "", "", false,
                                Multiplayer.MetalNodes.PickUpTimeToUse);
                            verbName = "PickUp";
                            // A FUEL CANISTER must never offer an interact prompt: retail
                            // fuel is SALVAGED with the gauntlet (8/8/9 per canister), not
                            // picked up. This generic branch used to catch the canister and
                            // advertise "Pick Up" - a prompt whose E press the server
                            // rightly ignores, which reads as "E does nothing" to the
                            // player. available=false keeps the 1210 checkout intact (no
                            // batch risk) while suppressing the lie; the real gate is 1099
                            // isSalvageable.
                            if (WorldsAdriftRebornGameServer.FuelCanisters.IsCanister(entityId))
                            {
                                available = false;
                            }
                            // A PACKED station lands on this generic branch too (its
                            // membership ledgers were removed at pickup, so the
                            // isCraftStation check above no longer claims it). It is
                            // sunk under the terrain, but never advertise a prompt on
                            // the ghost either - the same "a prompt is never a lie"
                            // rule the canister above follows.
                            if (Multiplayer.Placement.StationPickupLedger.Shared.IsPickedUp(entityId))
                            {
                                available = false;
                            }
                        }

                        InteractiveState.Data interactiveData = new InteractiveState.Data(
                            new InteractiveStateData(
                                available,
                                EntityId.InvalidEntityId,
                                new Improbable.Collections.List<InteractionEntry> { entry },
                                false));

                        Console.WriteLine("[info] seeding 1210 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") with verb " + verbName + ", available=" + available + ".");

                        obj = interactiveData;
                    }
                    else if(componentId == 1211)
                    {
                        InteractAgentState.Data iaData = new InteractAgentState.Data(new InteractAgentStateData(true,
                                                                                                        new EntityId(0),
                                                                                                        new EntityId(0),
                                                                                                        new EntityId(0),
                                                                                                        new Improbable.Math.Vector3f(0f, 0f, 0f),
                                                                                                        new Improbable.Math.Coordinates(),
                                                                                                        1,
                                                                                                        1));
                        obj = iaData;
                    }
                    else if(componentId == 1212)
                    {
                        InteractAgentServerState.Data iasData = new InteractAgentServerState.Data(new InteractAgentServerStateData(EntityId.InvalidEntityId,
                                                                                                                        0,
                                                                                                                        1,
                                                                                                                        EntityId.InvalidEntityId,  // forcedInteract
                                                                                                                        // exclusivelyUsingEntityId. EntityId(0) is invalid to IsValid()
                                                                                                                        // (which tests Id > 0) but is NOT equal to InvalidEntityId (-1),
                                                                                                                        // and ReleaseInteractiveObject guards on the latter - so a 0 seed
                                                                                                                        // makes the client emit ReleaseInteraction(0) every time an
                                                                                                                        // inventory or crafting window loses focus.
                                                                                                                        EntityId.InvalidEntityId,
                                                                                                                        Bossa.Travellers.Items.MultitoolMode.Default,
                                                                                                                        new Option<ScalaSlottedInventoryItem> { }));
                        obj = iasData;
                    }
                    else if(componentId == 6924)
                    {
                        AllianceNameState.Data anData = new AllianceNameState.Data(new AllianceNameStateData(""));

                        obj = anData;
                    }
                    else if(componentId == 6925)
                    {
                        AllianceAndCrewWorkerState.Data acData = new AllianceAndCrewWorkerState.Data(new AllianceAndCrewWorkerStateData("", ""));

                        obj = acData;
                    }
                    else if(componentId == 1098)
                    {
                        // Empty rope: no control points, not attached. The owner's
                        // RopeObserver publishes real points while grappling; the
                        // relay carries them and the mod's RemoteGrappleLine draws.
                        RopeControlPoints.Data rcpData = new RopeControlPoints.Data(new RopeControlPointsData(
                            new Improbable.Collections.List<Coordinates> { },
                            new Improbable.Collections.List<DynamicRopePoint> { },
                            false,
                            0f));

                        obj = rcpData;
                    }
                    else if(componentId == 6910)
                    {
                        // Default: no utility slot active (glider stowed). The owner's
                        // UtilitySlotActivatedBehaviour publishes real values when it
                        // deploys the glider; the relay carries them to remote rigs,
                        // where UtilitySlotActivatedVisualizer opens/closes the wings.
                        UtilitySlotActivatedState.Data usData = new UtilitySlotActivatedState.Data(new UtilitySlotActivatedStateData(
                            false, false, false,
                            new Option<float> { }, new Option<float> { }, new Option<float> { },
                            new Option<float> { }, new Option<float> { }, new Option<float> { }));

                        obj = usData;
                    }
                    else if(componentId == 1082)
                    {
                        InventoryModificationState.Data imData = new InventoryModificationState.Data();

                        obj = imData;
                    }
                    else if(componentId == 1087)
                    {
                        PlayerPermissionsState.Data ppData = new PlayerPermissionsState.Data(Role.NonAdmin);

                        obj = ppData;
                    }
                    else if(componentId == 4444)
                    {
                        MountedGunShotState.Data mgData = new MountedGunShotState.Data();

                        obj = mgData;
                    }
                    else if(componentId == 1109)
                    {
                        PilotState.Data psData = new PilotState.Data(new PilotStateData(new EntityId(0), new EntityId(0), ControlVehicleType.None));

                        obj = psData;
                    }
                    else if(componentId == 1111)
                    {
                        // ShipControlInput: neutral zero input, for EVERY entity that
                        // asks - the same unconditional shape as 1109 above, because the
                        // same component id lives on three different entities:
                        //   * the PLAYER - the pilot's writer twin. Must be seeded (an
                        //     inbound update is dropped unless the component is in the
                        //     ComponentMap) and is granted under WAREBORN_HELM_FLIGHT so
                        //     ShipControlsBehaviour's [Require] 1111 WRITER binds.
                        //   * the HULL - ShipControlInputVisualizer's reader. LOAD-BEARING
                        //     even though it looks cosmetic: PilotVisualizer's
                        //     OnChangeLinkedEntity does GetComponentInParent<ShipControl
                        //     InputVisualizer>() on the driven hull and calls
                        //     ShipControlsBehaviour.SetInitialInput, which dereferences
                        //     that visualizer's reader - GetComponentInParent finds the
                        //     component whether or not it is enabled, so serving 1111 here
                        //     is what makes that call safe the moment piloting starts.
                        //   * the HELM - HelmVisualizer's reader (the wheel visuals).
                        // This id spent its life in ComponentAbsencePolicy.KnownAbsent
                        // ("this server simulates no piloting"); helm flight made that
                        // false, so it moved here. Zeros = stick centred, throttle off.
                        obj = new ShipControlInput.Data(new ShipControlInputData(
                            new Improbable.Math.Vector3f(0f, 0f, 0f), 0f, 0f));
                    }
                    else if(componentId == 1112)
                    {
                        // TurretControlInput: ShipControlsBehaviour's OTHER [Require]d
                        // writer (alongside 1111 and the 1109 reader) - the behaviour
                        // enables only when every require resolves, so the player must
                        // have 1112 checked out too. Ship piloting never sets its one
                        // field (LookAt is only written under ControlType.Turret, and the
                        // per-frame empty FinishAndSend is diff-suppressed client-side),
                        // so a zero LookAt is the honest idle seed. No handler consumes
                        // it; it is filtered from relay beside 1111.
                        obj = new TurretControlInput.Data(new TurretControlInputData(
                            new Coordinates(0, 0, 0)));
                    }
                    else if(componentId == 1071)
                    {
                        BuilderServerState.Data bsData = new BuilderServerState.Data(new BuilderServerStateData(new EntityId(0)));

                        obj = bsData;
                    }
                    else if(componentId == 1070)
                    {
                        // BuilderState on the PLAYER: the CLIENT-authoritative event writer
                        // for the part-mount commit (PlacePart/CancelPlacePart/TeleportPart).
                        // BuilderStateData is EMPTY (event-only), so the seed is just "the
                        // component exists" - which lets BuilderObserver bind its 1070 WRITER
                        // once the authority grant lands, AND puts 1070 in ComponentMap so an
                        // inbound PlacePart is dispatched rather than dropped
                        // (ComponentUpdateManager.HandleComponentUpdate). Placement-gated:
                        // only injected when WAREBORN_PLACEMENT=1, so this never runs otherwise.
                        obj = new BuilderState.Data(default(BuilderStateData));
                    }
                    else if(componentId == 1239)
                    {
                        // PlacementToolPlayerState on the PLAYER: the CLIENT-authoritative
                        // lift-tool notification writer (PickedUp/Placed/Dropped). Data is
                        // EMPTY; seeded so PlayerPlacementToolBehaviour binds its 1239 WRITER
                        // and so its PickedUpEntityEvent is dispatched not dropped - that event
                        // is how the server learns which part a later PlacePart refers to (the
                        // carry tracker). Placement-gated, exactly like 1070.
                        obj = new PlacementToolPlayerState.Data(default(PlacementToolPlayerStateData));
                    }
                    else if(componentId == 1131)
                    {
                        // timeRate stays at 1f, NOT the client's own default of 144f.
                        //
                        // Each client is served this component at ITS OWN checkout moment and
                        // runs the day/night cycle from there, so two clients that joined a
                        // few minutes apart are a few minutes out of phase. At 1f that is a
                        // few minutes of a 24-hour cycle - invisible. At 144f the same gap
                        // becomes hours of game time, and the sun is visibly in a different
                        // place on each screen. That was observed directly in a two-player
                        // session: light coming through the rocks differed between clients.
                        //
                        // The real fix is a SHARED absolute epoch so both clients compute the
                        // same time of day regardless of when they joined; until then a slow
                        // cycle hides the divergence. Do not raise this without fixing that.
                        WorldData.Data woData = new WorldData.Data(new WorldDataData(new EntityId(0), 0.15f, 1f, 1));

                        obj = woData;
                    }
                    else if(componentId == 1098)
                    {
                        RopeControlPoints.Data rcData = new RopeControlPoints.Data(new RopeControlPointsData(new Improbable.Collections.List<Coordinates> { }, new Improbable.Collections.List<DynamicRopePoint> { }, false, 0f));

                        obj = rcData;
                    }
                    else if(componentId == 1206)
                    {
                        // ShipHullEditorState on the placed SHIPYARD entity: the
                        // read-only editor state the client's ShipHullEditorVisualizer
                        // [Require]s (alongside 1205 ShipyardState) to construct at all.
                        // Seeded INACTIVE - HasShipLoaded() == Active stays false, so the
                        // Edit button is disabled, until a player loads a frame, at which
                        // point ShipHullAgentClientState_Handler pushes an Active=true 1206
                        // update to THAT player only (the entity is shared, so a broadcast
                        // would cross-clobber; the update is per-peer). ownerPlayerId MUST
                        // equal the client's LocalPlayer.PlayerId or the editor's SAVE/RESET
                        // buttons stay greyed (ShipCraftingUIHelper gates them on
                        // GetOwnerId() == LocalPlayer.PlayerId). That id is the 1086 PlayerName
                        // field2_player_id, served from LocalPlayerIdentity - NOT the placed-
                        // shipyard ledger's owner uid (a different string, which was the
                        // mismatch that greyed SAVE). hasDirectAccess=true; hullData empty
                        // (the mesh is rebuilt from the pushed working blob).
                        ShipHullEditorState.Data heData = new ShipHullEditorState.Data(
                            false,                                    // active
                            false,                                    // modified
                            new EntityId(0),                          // editorId (invalid = not being edited)
                            0f,                                       // beamsLength
                            Multiplayer.Ship.StarterFrame.NumberOfDecks, // numberOfDecks
                            new byte[0],                              // hullData
                            0,                                        // slotId
                            true,                                     // hasDirectAccess
                            Multiplayer.LocalPlayerIdentity.PlayerId); // ownerPlayerId
                        obj = heData;
                    }
                    else if(componentId == 1207)
                    {
                        // FRAME DESIGNS: served from the player's live ship-design store
                        // so a re-checkout re-serves the CURRENT saved frames (mutated by
                        // the 1208 save handler), and a fresh player gets exactly one
                        // starter frame (StarterFrame). field6_uuid MUST equal the JSON
                        // uUID or a selected row resolves to slot -1 and never loads;
                        // StarterFrame feeds the same Uuid to both. editorId carries the
                        // shipyard being edited (0 = none) so the client's
                        // ShipHullAgentVisualizer knows whether it is in editor input mode.
                        Multiplayer.Ship.PlayerShipDesigns designs =
                            Multiplayer.Ship.ShipDesignStore.For(entityId);
                        Improbable.Collections.List<ShipHullSchematicData> schematics =
                            new Improbable.Collections.List<ShipHullSchematicData>();
                        foreach (Multiplayer.Ship.ShipDesignSlot slot in designs.Slots)
                        {
                            schematics.Add(new ShipHullSchematicData(
                                (byte[])slot.Data.Clone(),
                                slot.Name,
                                slot.BeamsLength,
                                slot.NumberOfDecks,
                                slot.ClientSchematicsIdJson,
                                slot.Uuid));
                        }
                        ShipHullAgentState.Data shData = new ShipHullAgentState.Data(
                            new ShipHullAgentStateData(schematics, new EntityId(designs.EditingShipyardEntityId)));

                        obj = shData;
                    }
                    else if(componentId == 1208)
                    {
                        // ShipHullAgentClientState: the CLIENT-authoritative marker the
                        // FRAME DESIGNS visualizer needs. ShipHullAgentVisualizer
                        // [Require]s ShipHullAgentStateReader (1207, served above, empty)
                        // AND ShipHullAgentClientStateWriter (1208) - and a WRITER exists
                        // only for a component the client holds authority over, so this
                        // seed is granted+injected via MirrorSendPolicy.ShipBuildUi* under
                        // the placement flag. Data is an empty struct (the component
                        // carries only its schematic-edit events), so the seed is just
                        // "the component exists". Nothing here is server-owned state; the
                        // client publishes its own events, none of which this milestone
                        // acts on.
                        obj = new ShipHullAgentClientState.Data();
                    }
                    else if(componentId == 1270)
                    {
                        // PlayerShipBlueprintInteractionState: the CLIENT->SERVER command
                        // channel on the PLAYER. PlayerShipBlueprintInteractionBehaviour
                        // [Require]s its WRITER (1270) + the 1274 READER; the behaviour
                        // fires RefreshBlueprints on this writer when the ship-build UI
                        // opens. Empty Data (the component carries only its events), so
                        // the seed just makes the component exist so the client can check
                        // it out and - once granted authority (MirrorSendPolicy) - bind
                        // its writer. The RefreshBlueprints reply is
                        // PlayerShipBlueprintInteractionState_Handler.
                        obj = new PlayerShipBlueprintInteractionState.Data();
                    }
                    else if(componentId == 1274)
                    {
                        // GsimShipBlueprintInteractionState: the SERVER->CLIENT reply
                        // channel on the PLAYER, and the one that clears the FRAME
                        // DESIGNS/SHIP BLUEPRINTS loading spinner. The full-panel
                        // LoadingInputBlocker (ShipSchematicsList._loadingInputBlocker)
                        // is bound to Busy; the UI sets Busy true locally on open and it
                        // only clears when the server sends Busy=false on 1274 (the
                        // handler does this on RefreshBlueprints). Seeded Busy=FALSE with
                        // an absent (None) ShipBlueprintList so a fresh checkout is not
                        // already spinning; ShipBlueprintList stays None (empty is the
                        // correct list for a new player). VERIFIED shape (gencode):
                        // GsimShipBlueprintInteractionState.Data(Option<ShipBlueprintList>,
                        // bool busy).
                        obj = new GsimShipBlueprintInteractionState.Data(
                            new Improbable.Collections.Option<ShipBlueprintList>(), false);
                    }
                    else if(componentId == 1271)
                    {
                        // ShipBlueprintCraftingState on the placed SHIPYARD. When the hull
                        // editor ACTIVATES, the client's interest in the shipyard expands to
                        // an 11-component batch that includes THIS id; with no serve branch
                        // it came back UnhandledId and - because an interest batch is
                        // all-or-nothing - dropped the WHOLE editor payload, leaving the
                        // client permanently input-blocked under the LoadingInputBlocker.
                        // Seeded IDLE/EMPTY: no blueprint selected (None prefix + None id),
                        // no schematics, zero crafting time, no character schematics, not
                        // crafting. That is the correct resting state for a shipyard nobody
                        // is blueprint-crafting a ship at, and it lets the blueprint-crafting
                        // behaviour bind without doing anything. VERIFIED shape (gencode):
                        // Data(Option<string> prefix, Option<string> id,
                        // List<ShipBlueprintSchematic>, int craftingTime,
                        // Map<string, CharacterSchematics>, bool isCrafting).
                        obj = new ShipBlueprintCraftingState.Data(
                            new Improbable.Collections.Option<string>(),
                            new Improbable.Collections.Option<string>(),
                            new Improbable.Collections.List<ShipBlueprintSchematic>(),
                            0,
                            new Map<string, CharacterSchematics>(),
                            false);
                    }
                    else if(componentId == 1450)
                    {
                        // ItemHealthNormalizedState on the placed SHIPYARD - the 0..1
                        // normalised health the client's NormalizedItemHealthVisualizer
                        // [Require]s (READER) to draw a health bar. The same editor-active
                        // interest batch asks for it, and its missing branch was the SECOND
                        // UnhandledId that dropped the batch. Seeded FULL (1.0), consistent
                        // with the shipyard's already-healthy 1016 ItemHealthState: a freshly
                        // placed structure is undamaged. Chosen SEED over known-absent because
                        // a real visualizer requires it and seeding both fixes the batch AND
                        // lets the health bar read correctly - strictly safer than omitting.
                        // VERIFIED shape (gencode): Data(float healthNormalized).
                        obj = new ItemHealthNormalizedState.Data(1f);
                    }
                    else if(componentId == 2001)
                    {
                        PlayerAnalyticsState.Data paData = new PlayerAnalyticsState.Data(new PlayerAnalyticsStateData("someuser_id",
                                                                                                            "somesession_id",
                                                                                                            false,
                                                                                                            "unity",
                                                                                                            new Improbable.Collections.List<string> { },
                                                                                                            "defaultPayload",
                                                                                                            false));
                        obj = paData;
                    }
                    else if(componentId == 1332)
                    {
                        // Served from the player's live progression store, not a static
                        // stub: a re-checkout re-serves the CURRENT knowledge and node
                        // uses (mutated by the scan and spend handlers), and an
                        // untouched player is seeded to the same (1, {}, 1, {}) the old
                        // static seed used (PlayerProgression.Seed*). cipherSlotCounts
                        // stays empty - cipher purchases are a later track.
                        Game.Knowledge.PlayerProgression knowledgeProg =
                            Game.Knowledge.ProgressionStore.For(entityId);
                        Map<string, int> nodeUses = new Map<string, int> { };
                        foreach (System.Collections.Generic.KeyValuePair<string, int> use in knowledgeProg.NodeUses)
                        {
                            nodeUses.Add(use.Key, use.Value);
                        }
                        KnowledgeServerState.Data ksData = new KnowledgeServerState.Data(new KnowledgeServerStateData(
                                                                                                            knowledgeProg.Knowledge,
                                                                                                            nodeUses,
                                                                                                            knowledgeProg.LifetimeKnowledge,
                                                                                                            new Map<string, int> { }));
                        obj = ksData;
                    }
                    else if(componentId == 2107)
                    {
                        // ScannerToolPlayerState - the player's own scanner. Empty state
                        // (it only carries the ScanEntityEvent); client-authoritative,
                        // so it is injected + granted via MirrorSendPolicy and the client
                        // publishes scans on its writer. Seeded empty here for the serve.
                        ScannerToolPlayerState.Data stData = new ScannerToolPlayerState.Data();
                        obj = stData;
                    }
                    else if(componentId == 1331)
                    {
                        // ScanningAgentServerState - the server-owned dedup ledger. Served
                        // from the same progression store the scan handler writes, so a
                        // re-checkout re-serves the CURRENT already-scanned set and a
                        // rescan still pays nothing. Non-null list (an empty one is fine).
                        Game.Knowledge.PlayerProgression scanProg =
                            Game.Knowledge.ProgressionStore.For(entityId);
                        Improbable.Collections.List<string> scanned = new Improbable.Collections.List<string>();
                        foreach (string id in scanProg.AlreadyScanned)
                        {
                            scanned.Add(id);
                        }
                        ScanningAgentServerState.Data saData = new ScanningAgentServerState.Data(new ScanningAgentServerStateData(scanned));
                        obj = saData;
                    }
                    else if(componentId == 1334)
                    {
                        // KnowledgeClientState - the player's own knowledge writer. Empty
                        // state (only carries UseNode); client-authoritative via
                        // MirrorSendPolicy. ScanningAgentVisualizer needs this writer to
                        // enable, so without it no knowledge events fire at all.
                        KnowledgeClientState.Data kcData = new KnowledgeClientState.Data();
                        obj = kcData;
                    }
                    else if(componentId == 8073)
                    {
                        // ScannableRuinState - the marker DatabankIslandVisualiser reads to
                        // draw a databank and make it scannable. relativeToIsland names the
                        // island the bank sits on (the client resolves the bank's transform
                        // against it); we point it at the registered island entity when we
                        // know its id, else an empty option. Only databank entities request
                        // 8073, so this branch is databank-only in practice.
                        Multiplayer.WorldEntity? databank =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId);
                        Multiplayer.Islands.IslandRegistry islands =
                            Multiplayer.Islands.IslandRegistry.CreateDefault();
                        Multiplayer.Islands.IslandId owner = databank == null
                            ? Multiplayer.Islands.IslandCatalog.HavenId
                            : Multiplayer.Islands.IslandResourceInterestPolicy.ClosestIsland(
                                databank.Position, islands.All);
                        string islandKey = islands.Require(owner).WorldEntityKey;
                        long? islandId = WorldsAdriftRebornGameServer.WorldEntities
                            .BoundEntityIdFor(islandKey);
                        Option<EntityId> relativeToIsland = islandId.HasValue
                            ? new Option<EntityId>(new EntityId(islandId.Value))
                            : new Option<EntityId>();
                        ScannableRuinState.Data srData = new ScannableRuinState.Data(new ScannableRuinStateData(relativeToIsland));
                        obj = srData;
                    }
                    else if(componentId == 1079)
                    {
                        // defaultSchematics FIRST, then learnedSchematics. Seed only
                        // the MINIMAL starter tier as defaults (SchematicHelper.
                        // DefaultSchematicIds - torch/guitar/clothMakeshift/
                        // makeshiftStorage); the rest of the catalogue is GATED behind
                        // the knowledge tree. learnedSchematics is served from the
                        // progression store so a knowledge purchase that appended a
                        // schematic survives a re-checkout - that is how a gated recipe
                        // reaches the player's book. Seeded at AddComponent time because
                        // AllReferenceAndPlayerDataLoaded clears the buffer
                        // unconditionally; learnedSchematics is kept present (not null)
                        // because LearnedSchematicsUpdated is a callback on
                        // learnedSchematics only, so a later push that touched only
                        // defaults would be invisible.
                        Game.Knowledge.PlayerProgression schematicProg =
                            Game.Knowledge.ProgressionStore.For(entityId);
                        Improbable.Collections.List<string> learned = new Improbable.Collections.List<string>();
                        foreach (string learnedId in schematicProg.LearnedSchematics)
                        {
                            learned.Add(learnedId);
                        }
                        SchematicsLearnerClientState.Data scData = new SchematicsLearnerClientState.Data(new SchematicsLearnerClientStateData(Items.SchematicHelper.DefaultSchematicIds(),
                                                                                                                                    learned,
                                                                                                                                    10,
                                                                                                                                    20,
                                                                                                                                    10,
                                                                                                                                    10));
                        obj = scData;
                    }
                    else if(componentId == 190002)
                    {
                        // LOADING BARRIER. PlayerActivationVisualiser fades the loading
                        // screen when Activated.IsActive goes TRUE. With the barrier on we
                        // seed it FALSE for the player, so the screen stays up while the
                        // ground and ship stream in behind it; EntityLoadingResponse_Handler
                        // (or the timeout) pushes IsActive=true to release the player once
                        // the initial set is ready. Barrier off (or a non-player entity) =>
                        // the original always-active seed, unchanged. Seeding false also
                        // sets the player kinematic (frozen) until activation, which is
                        // exactly the intended "held on the loading screen" behaviour.
                        bool active = !(global::WorldsAdriftRebornGameServer.Game.LoadBarrier.Enabled && IsOwnPlayerEntity(player, entityId));
                        Activated.Data aData = new Activated.Data(new ActivatedData(active, true, 0));

                        obj = aData;
                    }
                    else if(componentId == 190000)
                    {
                        if (global::WorldsAdriftRebornGameServer.Game.LoadBarrier.Enabled && IsOwnPlayerEntity(player, entityId))
                        {
                            // Requested + the initial entity-id list is WA's shipped
                            // readiness barrier: BossaEntityLoadingChecker publishes
                            // 190001 Loaded=true only once every id named here exists and
                            // is active on the client. Distant scenery is deliberately NOT
                            // named, so 21 trees and 21 ore never gate the loading screen.
                            EntityLoadingControl.Data elData = new EntityLoadingControl.Data(new EntityLoadingControlData(EntityLoadingControlData.EntityLoadingStates.Requested,
                                                                                                                0,
                                                                                                                5,
                                                                                                                100,
                                                                                                                false,
                                                                                                                global::WorldsAdriftRebornGameServer.Game.LoadBarrier.InitialEntityIds()));
                            obj = elData;
                        }
                        else
                        {
                            // The original bypass: Idle with an empty list means the
                            // client's checker never runs and the barrier is not used.
                            EntityLoadingControl.Data elData = new EntityLoadingControl.Data(new EntityLoadingControlData(EntityLoadingControlData.EntityLoadingStates.Idle,
                                                                                                                0,
                                                                                                                5,
                                                                                                                100,
                                                                                                                false,
                                                                                                                new Improbable.Collections.List<EntityId> { }));
                            obj = elData;
                        }
                    }
                    else if(componentId == 190001)
                    {
                        // EntityLoadingResponse: the client's writer twin of 190000.
                        // BossaEntityLoadingChecker holds this writer and flips loaded=true
                        // when the initial set is ready. Seeded false and (in barrier mode)
                        // granted to the client at setup so the writer enables; the server
                        // READS the update in EntityLoadingResponse_Handler. Harmless when
                        // the barrier is off - nothing grants the client authority, so the
                        // checker never enables and this component is inert.
                        EntityLoadingResponse.Data erData = new EntityLoadingResponse.Data(false);

                        obj = erData;
                    }
                    else if(componentId == 1150)
                    {
                        PlayerActivationState.Data pcData = new PlayerActivationState.Data(new PlayerActivationStateData(true, 12345, 123));

                        obj = pcData;
                    }
                    else if(componentId == 1219)
                    {
                        // ShipyardVisitorState on the PLAYER: the client's
                        // ShipyardVisitorVisualizer resolves the player's shipyard PURELY
                        // from this ShipyardId being a valid entity id
                        // (ShipyardVisitorVisualizer.cs:130-133), and PlayerScannerTool
                        // refuses the crafted-part lift ("Interact with shipyard to gain
                        // access.") while its Shipyard is null. So report the yard this
                        // player has been GRANTED build access to (set when they interact
                        // with a shipyard console, PlacementService.OpenShipyardConsole);
                        // 0 (an invalid EntityId) when they have none, i.e. no access yet.
                        // Ledger-backed so a re-checkout of the player's own 1219 keeps the
                        // grant, exactly like the 1205 branch reads BuiltShips.DockedShipFor.
                        long grantedShipyard =
                            Multiplayer.Placement.ShipyardBuildAccess.Shared.ShipyardFor(entityId);
                        ShipyardVisitorState.Data svData = new ShipyardVisitorState.Data(
                            new ShipyardVisitorStateData(new EntityId(grantedShipyard), "abcdefg"));

                        obj = svData;
                    }
                    else if(componentId == 1003)
                    {
                        PlayerCraftingInteractionState.Data pcisData = new PlayerCraftingInteractionState.Data(new EntityId(0), true);

                        obj = pcisData;
                    }
                    else if(componentId == 1004)
                    {
                        // CraftingStationGSimState: the SECOND [Require] reader
                        // CraftingStationBehaviour needs (alongside 1005) to enable at
                        // all - without it the placed shipyard's console never registers
                        // its crafting data and the Craft interaction opens nothing
                        // (VERIFIED: CraftingStationBehaviour.cs [Require] _gsimState).
                        // gsimSchematicId "" (no craft queued), lastCreatedEntityId
                        // invalid: an idle station. The 1005 event echo drives the open;
                        // this branch exists only so the require-gate is satisfied and
                        // the seed batch is not dropped. Data shape VERIFIED via gencode
                        // CraftingStationGSimStateData(string gsimSchematicId,
                        // EntityId lastCreatedEntityId).
                        CraftingStationGSimState.Data csgData = new CraftingStationGSimState.Data(
                            new CraftingStationGSimStateData("", EntityId.InvalidEntityId));
                        obj = csgData;
                    }
                    else if(componentId == 1005)
                    {
                        // clientSchematicId is "" (empty), NOT the literal
                        // "schematicId": a non-empty id the catalogue cannot
                        // resolve makes CraftingStationData.GetSchematicFromID
                        // return null and NRE the crafting UI. itemReadyInSeconds
                        // is -1 (aperture closed, no phantom countdown); no
                        // materials are slotted at seed. The 1003 handler drives
                        // real values from here on.
                        CraftingStationClientState.Data csData = new CraftingStationClientState.Data(new CraftingStationClientStateData("",
                                                                                                                                "",
                                                                                                                                new Improbable.Collections.List<SlottedMaterial> { },
                                                                                                                                new Improbable.Collections.List<Cipher> { },
                                                                                                                                -1,
                                                                                                                                0f,
                                                                                                                                new Option<PredictedStatDataExtra> { }));
                        obj = csData;
                    }
                    else if(componentId == 8055)
                    {
                        // FALSE, and spawning on the real Haven does not change that.
                        //
                        // 8055 is the SOLE runtime source of truth for "this player
                        // is in Haven" - the client does not derive it from position
                        // (xOfVerticalSeparator is a World Editor gizmo, not a
                        // runtime check). The only exit is 8056 LeaveHavenRequest,
                        // which has zero references in the entire client, is consumed
                        // server-side, and is unimplemented here. There is no handler
                        // that could flip 8055 back and the client has no writer for
                        // it, so `true` is a permanent prison: five UI features
                        // disabled forever, plus EVERY biome banner in the game
                        // suppressed, because DisplayBiomeNotification is called from
                        // RespawnVisualizer.Update on a one-second poll.
                        //
                        // `false` is silent - NewPlayerVisualiser.OnNewPlayerChanged
                        // acts only on the true-to-false edge.
                        // See docs/research/findings-haven.md.
                        NewPlayerState.Data npData = new NewPlayerState.Data(new NewPlayerStateData(Multiplayer.SpawnPolicy.SeedIsNewPlayer));

                        obj = npData;
                    }
                    else if(componentId == 4329)
                    {
                        PlayerBuffState.Data pbData = new PlayerBuffState.Data(new PlayerBuffStateData(new Improbable.Collections.List<Buff> { }));

                        obj = pbData;
                    }
                    else if(componentId == 8060)
                    {
                        FeedbackListener.Data fbData = new FeedbackListener.Data();

                        obj = fbData;
                    }
                    else if(componentId == 1095)
                    {
                        FSimTimeState.Data fsData = new FSimTimeState.Data(new FSimTimeStateData(0.15f, "fsimId", 100));

                        obj = fsData;
                    }
                    else if(componentId == 190300)
                    {
                        ClientPhysicsLatency.Data cpData = new ClientPhysicsLatency.Data(new ClientPhysicsLatencyData(0, 1000));

                        obj = cpData;
                    }
                    else if(componentId == 1006)
                    {
                        DevelopmentConsoleState.Data dcData = new DevelopmentConsoleState.Data(new DevelopmentConsoleStateData(100, 100, "gsimHostname", new Coordinates(0, 0, 0), "zone"));

                        obj = dcData;
                    }
                    else if(componentId == 1008)
                    {
                        FsimStatus.Data fData = new FsimStatus.Data(new FsimStatusData(60f, 1234, 150, "fsimEngineId", 123));

                        obj = fData;
                    }
                    else if(componentId == 1073)
                    {
                        // The timestamp seed is the SYNTHETIC TIMELINE's origin
                        // (0.2 s), not 100. The receiving client's interpolator
                        // pairs every arriving 190602 position with the latest
                        // 1073 timestamp and anchors on the FIRST value it sees;
                        // 100 against live stamps of ~0.0x-scale was a
                        // guaranteed pathological snap the first time any player
                        // saw another. RelayEmitter's per-recipient timelines
                        // continue from exactly this value (first live emit is
                        // one emit-interval past it), and the hook below is what
                        // keeps stream and seed agreeing about the epoch across
                        // a re-serve. See Multiplayer.RelayTimestampPolicy.
                        ClientAuthoritativePlayerState.Data capData = new ClientAuthoritativePlayerState.Data(new ClientAuthoritativePlayerStateData(new Improbable.Math.Vector3f(0f, 0f, 0f),
                                                                                                                                            new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0), // w-first
                                                                                                                                            EntityId.InvalidEntityId,
                                                                                                                                            0f,
                                                                                                                                            Multiplayer.RelayTimestampPolicy.SeedTimestampSeconds,
                                                                                                                                            new byte[] { },
                                                                                                                                            false,
                                                                                                                                            // TeleportRequestState seeds request 0 and the
                                                                                                                                            // first live request is 1. Seeding this ack above
                                                                                                                                            // zero makes retail's visualizer reject request 1
                                                                                                                                            // as already executed, so it neither moves nor acks.
                                                                                                                                            Multiplayer.TeleportPolicy.SeedRequest,
                                                                                                                                            false,
                                                                                                                                            false,
                                                                                                                                            100));
                        obj= capData;

                        WorldsAdriftRebornGameServer.Relay.OnSeed1073Served(PeerIdentity.IdOf(player), entityId);
                    }
                    else if(componentId == 9005)
                    {
                        SocialWorkerId.Data wiData = new SocialWorkerId.Data(new SocialWorkerIdData("workerId"));

                        obj = wiData;
                    }
                    else if(componentId == 1040)
                    {
                        GamePropertiesState.Data gpData = new GamePropertiesState.Data(new GamePropertiesStateData(new Map<string, string> { }));

                        obj = gpData;
                    }
                    else if(componentId == 6902)
                    {
                        GsimEventAuditState.Data gsData = new GsimEventAuditState.Data(new GsimEventAuditStateData(new Map<string, int> { }));

                        obj = gsData;
                    }
                    // 1269 RadialStormState and 1139 WeatherCellState USED TO BE
                    // SEEDED HERE, with a weight of 0f and a pressure of 1f
                    // respectively, on every entity that asked. They are now
                    // declared known-absent in Multiplayer.ComponentAbsencePolicy
                    // and never reach this chain at all - the branches are gone
                    // rather than left dead, because a dead seed is an invitation
                    // to "fix" the policy by deleting an entry and silently
                    // restoring the error storm.
                    //
                    // 1139 was the whole of docs/research/diag/findings-weather-storm.md:
                    // every entity we spawn floors into the same 500 m weather cell,
                    // so all but one lost an id-map race EVERY FRAME, FOREVER -
                    // ~197 stack-traced error lines a second on the client's main
                    // thread with five entities, and ~49.5 more per entity ever
                    // added. If either id has to come back, it needs a reason why
                    // the entity genuinely HAS it, not a seed.
                    else if(componentId == 1254)
                    {
                        IslandLightningTimerState.Data ilData = new IslandLightningTimerState.Data(new IslandLightningTimerStateData(50 * 1000, // must be >= 30  and below must be > 0 to trigger lightning rumbles. multiply by 1000 to actually get the value you want ingame (50 in this case)
                                                                                                                            0, // must be 0 or you will set the island into a storm
                                                                                                                            1234,
                                                                                                                            1234,
                                                                                                                            false,
                                                                                                                            1,
                                                                                                                            new Improbable.Collections.List<EntityId> { new EntityId(2) }));
                        obj = ilData;
                    }
                    else if(componentId == 1041)
                    {
                        // todo: check how we could get correct values for this.
                        //
                        // The prefab name MUST match the asset the client was told to
                        // load and the name in the island's AddEntityOp. It was a bare
                        // string literal at all three sites, then one constant; it is
                        // now read off THIS ENTITY's registration, so a world with two
                        // islands cannot hand the second one the first one's name.
                        // The fallback keeps the old behaviour if 1041 is ever asked
                        // for on an entity this server did not register.
                        //
                        // The Coordinates(0,0,0) here is IslandState.teleportTarget,
                        // NOT a world position - the island is positioned by 190602
                        // above (IslandLocalTransformBase.cs:44). teleportTarget has
                        // zero client consumers, so its meaning is ours to define.
                        string islandAssetName =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.AssetName
                            ?? Multiplayer.SpawnPolicy.IslandAssetName;

                        IslandState.Data data = new IslandState.Data(new IslandStateData(islandAssetName,
                                                                                            new Coordinates(0, 0, 0),
                                                                                            1f,
                                                                                            new Vector3f(0,0,0),
                                                                                            new Vector3f(100f, 100f, 100f),
                                                                                            new Option<string>("I Dont know who made this island :("),
                                                                                            false,
                                                                                            new Improbable.Collections.List<IslandDatabank>()
                                                                                            ));
                        obj = data;
                    }
                    else if(componentId == 1042)
                    {
                        // todo: check how we could get correct values for this.
                        IslandFabricState.Data data = new IslandFabricState.Data(new IslandFabricStateData(5,
                                                                                                           0,
                                                                                                           0,
                                                                                                           new Improbable.Collections.List<EntityId> { new EntityId(0) },
                                                                                                           new Option<EntityId>(),  // a PRESENT option means "island already has a TCB" and blocks placement
                                                                                                           new Option<string>(),
                                                                                                           Bossa.Travellers.Biomes.BiomeType.Biome1,
                                                                                                           false,
                                                                                                           new Option<Coordinates>(),
                                                                                                           new Option<double>(),
                                                                                                           new Option<double>()));
                        obj = data;
                    }
                    else if (componentId == 1010)
                    {
                        // 1010 IslandResourceSpawnerState - the SERVER's resource-request
                        // component on the ISLAND. The stock client's IslandProxyVisualizer
                        // [Require]s its READER (acs/IslandProxyVisualizer.cs:22) and, in
                        // OnEnable, copies metalOnSurfaceProb off it (:60) and subscribes to
                        // its SpawnResources event (:58). Seeding it here is what makes that
                        // visualizer enable at all; the SpawnResources REQUEST itself is a
                        // separate ComponentUpdate raised later (Game.Gathering.IslandResourceService),
                        // once the client has had a chance to run OnEnable and subscribe.
                        //
                        // The data fields are the LOST server refdata (count/density/quality
                        // maps); only metalOnSurfaceProb is read by the client, and it forces
                        // it to 1 anyway (acs/IslandSurfaceData.cs:184), so a reconstructed
                        // 0.3 is harmless. Everything else is a zero/empty seed - the client
                        // reads none of it. See Multiplayer.IslandResourceHandshake.
                        Bossa.Travellers.Islands.IslandResourceSpawnerState.Data resourceData =
                            new Bossa.Travellers.Islands.IslandResourceSpawnerState.Data(
                                new Bossa.Travellers.Islands.IslandResourceSpawnerStateData(
                                    0,      // metalRocksRequiredToRespawn (unused by client)
                                    0,      // initialMetalRockDeposits (server count; we use the env knob)
                                    0f,     // metalDepositDensity
                                    0f,     // minMetalRockDeposits
                                    Multiplayer.IslandResourceHandshake.MetalOnSurfaceProb,
                                    new Improbable.Collections.Map<string, int>(),   // metalDepositQuantities
                                    new Improbable.Collections.Map<string, int>(),   // metalDepositQualities
                                    0,      // eggsSpawned
                                    new Improbable.Collections.List<EntityId>()));   // spawnedMetalDeposits
                        obj = resourceData;
                    }
                    else if (componentId == 1011)
                    {
                        // 1011 IslandResourceSpawnerClientState - the CLIENT's resource-reply
                        // WRITER on the island (IslandProxyVisualizer [Require]s it, :25). The
                        // client reads batchSize + spawnInterval off this seed (:82-83) to pace
                        // its reply batches, and OVERWRITES initialized + islandMeshCount itself
                        // once its visualizer runs (:86). Seeded initialized=false so the
                        // client's own OnEnable sends its Initialized(true).IslandMeshCount(...)
                        // update, exactly as it does against a real deployment.
                        //
                        // Seeding it is NOT optional even though the client is the writer: the
                        // 1011 update handler dispatches only for a component the server already
                        // has in ComponentMap[peer][island][1011] (ComponentUpdateManager), so
                        // no seed => no handler call. Its authority is granted separately by
                        // the island-resource setup so the client's WRITER binds.
                        Bossa.Travellers.Islands.IslandResourceSpawnerClientState.Data clientResourceData =
                            new Bossa.Travellers.Islands.IslandResourceSpawnerClientState.Data(
                                new Bossa.Travellers.Islands.IslandResourceSpawnerClientStateData(
                                    false,                                                       // initialized (client sets true)
                                    0,                                                           // islandMeshCount (client fills from its own mesh count)
                                    Multiplayer.IslandResourceHandshake.BatchSize,
                                    Multiplayer.IslandResourceHandshake.SpawnIntervalSeconds));
                        obj = clientResourceData;
                    }
                    else if(componentId == 190604)
                    {
                        GlobalTransformState.Data data = new GlobalTransformState.Data(new GlobalTransformStateData(new Coordinates(0, 0, 0),
                                                                                                                        new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0), // w-first; (0,0,0,0) throws on re-encode
                                                                                                                        new Vector3d(0, 0, 0),
                                                                                                                        0));
                        obj = data;
                    }else if(componentId == 1240)  // reader
                    {
                        LorePiecesCollectorGsimState.Data loreGsimData = new LorePiecesCollectorGsimState.Data(new Improbable.Collections.List<string>());
                        obj = loreGsimData;
                    }
                    else if(componentId == 1241) // writer
                    {
                        LorePiecesCollectorClientState.Data loreClientData = new LorePiecesCollectorClientState.Data();
                        obj = loreClientData;
                    }
                    else if (componentId == 8051)
                    {
                        ToolState.Data toolData = new ToolState.Data(new ToolStateData(30));

                        obj = toolData;
                    }
                    else if (componentId == 8050)
                    {
                        ToolRequestState.Data toolRequestData = new ToolRequestState.Data(new ToolRequestStateData());

                        obj = toolRequestData;
                    }
                    
                    else if (componentId == 6908)
                    {
                        ReferenceDataRequestState.Data eeeData = new ReferenceDataRequestState.Data(new ReferenceDataRequestStateData());

                        obj = eeeData;
                    }
                    else if (componentId == 1097)
                    {
                        ReferenceDataState.Data referenceData = new ReferenceDataState.Data(new ReferenceDataStateData(
                            new EntityId(-1), 
                            "",
                            new Map<string, string>(),
                            new Map<string, string>(), 
                            "{}",
                            "{}",
                            "{}",
                            10, 
                            new Map<string, string>(),
                            true
                        ));
                    
                        obj = referenceData;
                    }
                    else if (componentId == 1260)
                    {
                        SchematicsUnlearnerState.Data susData = new SchematicsUnlearnerState.Data();

                        obj = susData;
                    }
                    // ---- the three seeds a procedural ship hull needs, on top of
                    // 190602 above. See Multiplayer.WorldEntities.ShipFrame.
                    else if (componentId == 1209)
                    {
                        // A WHOLE SHIP'S GEOMETRY. 1209 is one field, byte[]
                        // hullData, and CustomShipFrameVisualizer rebuilds the mesh
                        // AND its colliders from it at runtime - so this branch is
                        // the difference between a ship and nothing at all.
                        //
                        // The bytes are a named constant with a generator and
                        // committed output behind them, not a literal here, because
                        // ShipPlan.Load THROWS on malformed input rather than
                        // failing quietly, and the throw lands in the client's log
                        // where we cannot see it. See Multiplayer.ShipHull.
                        //
                        // Fresh array per call, also deliberate - see the same file.
                        //
                        // A BUILT ship (spawned by BuiltShipSpawner on craft completion)
                        // serves its OWN saved-design bytes from the built-ship ledger,
                        // so different builds render as different ships; the global
                        // minimum hull is the fallback for the static test hull and for
                        // any hull whose bytes were not recorded. The spawner already
                        // validated the built bytes (falling back to the minimum hull on
                        // a bad blob), so this is a straight lookup with no re-validation.
                        byte[] builtBytes = Game.Crafting.BuiltShips.HullBytesFor(entityId);
                        byte[] hullBytes = builtBytes ?? Multiplayer.ShipHull.MinimumHullData();

                        CustomShipHullState.Data hullData =
                            new CustomShipHullState.Data(hullBytes);

                        Console.WriteLine("[info] seeding 1209 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") with the " + hullBytes.Length + "-byte "
                            + (builtBytes != null ? "built-ship" : "minimum") + " hull.");

                        obj = hullData;
                    }
                    else if (componentId == 1130)
                    {
                        // ONE control point, at the ship's own position, standing
                        // still. This is NOT a path publisher and must not become
                        // one by accident: a second point arriving less than
                        // SendInterval * 0.95 = 0.228 s after this one is dropped by
                        // ControlPoint.ValidateControlPoints.
                        //
                        // WHY A HULL NEEDS ONE AT ALL, when 190602 already says
                        // where it is: the ShipFrame prefab carries NO
                        // Static/Exact/Lerp LocalTransformBehaviour on its root -
                        // its TransformNature is the Custom mode, and
                        // SSPDeadReckoningVisualizer is that custom implementation.
                        // The thing that actually calls MovePosition on the hull's
                        // kinematic rigidbody is PathFollower, and PathFollower only
                        // has a position to move to once a control point has arrived.
                        // So this seed and the 190602 seed must agree, and they do
                        // by construction: both read the same registration.
                        //
                        // The client subscribes in OnEnable with `+=`, and the
                        // generated event's add-accessor invokes the handler
                        // immediately with the current value - so a SEEDED point is
                        // delivered exactly like a live one, no update needed.
                        //
                        // Coordinates here are GLOBAL METRES, not fixed point and
                        // not Unity-space: ControlPoint.Remap() subtracts the
                        // client's origin offset itself.
                        //
                        // Velocity ZERO is what makes this safe as a seed. Every
                        // extrapolation PathFollower performs from a zero-velocity
                        // point lands on the same position, so the hull cannot drift
                        // no matter how far the client's clock is from our timestamp.
                        //
                        // Rotation 1023 is the identity SENTINEL - the low 10 bits
                        // all set. It is not "a rotation that happens to be near
                        // identity"; 1 decodes to NaN, and a NaN rotation is
                        // rejected outright by ControlPoint.ValidateControlPoint.
                        Multiplayer.FixedPointPosition at =
                            WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(entityId);

                        ShipControlPoint atRest = new ShipControlPoint(
                            Multiplayer.ShipHull.NowMillisecondsSinceEpoch(),
                            new Coordinates(at.MetresX, at.MetresY, at.MetresZ),
                            new Quaternion32(1023),
                            new Improbable.Math.Vector3f(0f, 0f, 0f),
                            Multiplayer.ShipHull.FsimIdHash);

                        // extrapolate FALSE. The flag has no consumer anywhere in
                        // the shipped client - PathFollower extrapolates on its own
                        // when it runs out of points - so false is the answer that
                        // claims the least.
                        SSPPredictedMotionState.Data pmData = new SSPPredictedMotionState.Data(
                            new SSPPredictedMotionStateData(false, new Option<ShipControlPoint>(atRest)));

                        Console.WriteLine("[info] seeding 1130 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") with one control point at rest at " + at + ".");

                        obj = pmData;
                    }
                    else if (componentId == 8066)
                    {
                        // 8066 ShipRootState is what makes the N+1 entities of a
                        // ship ONE ship: a bolted-on part points its shipRoot at the
                        // hull. VERIFIED shape (ilspycmd on Generated.Code.dll):
                        //   struct ShipRootStateData { Option<EntityId> shipRoot; bool isRoot; }
                        //   ShipRootState.Data(Option<EntityId> shipRoot, bool isRoot)
                        // and SERVER-WRITTEN ONLY (ShipRootState.Reader has no
                        // Trigger*). Its sole client consumer is
                        // Assets.Scripts.Visualisers.Ship.ShipPartVisualizer.ShipEntityId,
                        // which returns shipRoot.Value.
                        //
                        // The decision (isRoot, and which hull shipRoot names) is the
                        // pure Multiplayer.ShipRootSeed so it is unit-tested without
                        // the game types; only the Option<EntityId>/ShipRootState.Data
                        // construction lives here, because those live in this assembly.
                        //
                        // The hull id is looked up WITHOUT allocating
                        // (BoundEntityIdFor): the hull is registered and spawned
                        // before the helm, so by the time the client requests the
                        // helm's 8066 the id is known. If it somehow is not, obj stays
                        // null and this best-effort interest simply skips 8066 - the
                        // helm still renders and its 1210 Man prompt still works;
                        // only the ship link waits for a later checkout.
                        //
                        // CLIENT-DORMANT TODAY, and honestly so: ShipPartVisualizer
                        // [Require]s 1120/190601/1016/1013 too, which this server does
                        // not yet seed, so the visualizer that reads 8066 does not
                        // enable. Seeding 8066 is still the correct server-authoritative
                        // truth and is what the aboard/abandonment/pilot work builds on
                        // - and it enables nothing against default data (rule 7),
                        // because its only reader needs four more components to wake up.
                        // A LOOSE (unattached) crafted part belongs to NO ship yet:
                        // shipRoot absent, isRoot=false. ShipPartVisualizer.ShipEntityId
                        // returns InvalidEntityId for an absent option
                        // (ShipPartVisualizer.cs:100) - exactly "not on a ship", the
                        // resting state before the builder/mount flow sets membership.
                        // LIVE-ONLY UNKNOWN: whether the original loose part used an
                        // absent shipRoot or pointed at itself; absent matches the
                        // InvalidEntityId contract and enables nothing against defaults.
                        if (Game.Crafting.MountedParts.Is(entityId))
                        {
                            // A MOUNTED part belongs to its built hull: shipRoot = the hull,
                            // isRoot=false. This is what ShipPartVisualizer.ShipEntityId reads
                            // to know which ship the part rides - flipped from the loose part's
                            // absent shipRoot by the 1070 commit, and re-seeded here on checkout.
                            Game.Crafting.MountedParts.Mount? mount = Game.Crafting.MountedParts.MountFor(entityId);
                            Console.WriteLine("[info] seeding 8066 for MOUNTED part " + entityId
                                + " -> shipRoot " + mount.Value.HullEntityId + " (isRoot=false).");
                            obj = new ShipRootState.Data(
                                new Option<EntityId>(new EntityId(mount.Value.HullEntityId)), false);
                        }
                        else if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            Console.WriteLine("[info] seeding 8066 for LOOSE part " + entityId
                                + " -> no ship (shipRoot absent, isRoot=false).");
                            obj = new ShipRootState.Data(new Option<EntityId>(), false);
                        }
                        else
                        {
                        var shipReg = WorldsAdriftRebornGameServer.WorldEntities;
                        string? shipEntityKey = shipReg.ByEntityId(entityId)?.Key;
                        long? hullEntityId = shipReg.BoundEntityIdFor(Multiplayer.WorldEntities.ShipFrameKey);

                        Multiplayer.ShipRootSeed? rootSeed = null;
                        if (hullEntityId.HasValue)
                        {
                            if (Multiplayer.WorldEntities.IsBoltedPartKey(shipEntityKey))
                            {
                                // Every part bolted onto the hull - helm, deck, engine,
                                // sail - points its shipRoot at the hull. The deck is
                                // the one whose ShipDeckVisualizer does not need 8066 at
                                // all (it reads 1518 + 1099), so seeding 8066 here is
                                // pure ship-membership truth for it, exactly as it is
                                // dormant-but-correct for the helm.
                                rootSeed = Multiplayer.ShipRootSeed.Part(hullEntityId.Value);
                            }
                            else if (shipEntityKey == Multiplayer.WorldEntities.ShipFrameKey)
                            {
                                // Defensive: the ShipFrame carries no ShipPartVisualizer,
                                // so the client never requests 8066 for the hull today.
                                // Present so that IF the hull ever seeds 8066 the
                                // isRoot=true value is already defined and tested.
                                rootSeed = Multiplayer.ShipRootSeed.Root(hullEntityId.Value);
                            }
                        }

                        if (rootSeed.HasValue)
                        {
                            Option<EntityId> shipRoot = rootSeed.Value.HasShipRoot
                                ? new Option<EntityId>(new EntityId(rootSeed.Value.ShipRootEntityId))
                                : new Option<EntityId>();

                            ShipRootState.Data shipRootData =
                                new ShipRootState.Data(shipRoot, rootSeed.Value.IsRoot);

                            Console.WriteLine("[info] seeding 8066 for entity " + entityId + " ("
                                + shipReg.Describe(entityId) + ") -> shipRoot " + rootSeed.Value.ShipRootEntityId
                                + ", isRoot " + rootSeed.Value.IsRoot + ".");

                            obj = shipRootData;
                        }
                        else
                        {
                            Console.WriteLine("[warn] 8066 requested for entity " + entityId
                                + " but no ship hull id is known yet (or it is not a ship part); skipping.");
                        }
                        }
                    }
                    else if (componentId == 1120)
                    {
                        // ShipPartState: the logical part metadata ShipPartVisualizer
                        // [Require]s (ShipPartVisualizer.cs:26). Served per-entity from
                        // the LooseParts ledger so each crafted part loads its own prefab
                        // and reports how it would mount. For a LOOSE part: attached=false,
                        // held=false, no attach transform, and scale MUST be present -
                        // ShipPartVisualizer.OnEnable:119 reads _state.Scale.Value and
                        // throws on an absent option, so scale = Some((1,1,1)). rarity is a
                        // valid tier (EngineVisualizer warns on an absent one; harmless for
                        // the lamp but cheap to set). playersPlacingPart is empty, non-null.
                        // VERIFIED ctor (gencode ShipPartState.cs:1238). Only a loose part
                        // ever requests 1120, so a non-loose entity serves nothing and
                        // best-effort interest skips it.
                        Game.Crafting.MountedParts.Mount? mountedPart = Game.Crafting.MountedParts.MountFor(entityId);
                        var loosePart = Game.Crafting.LooseParts.DefFor(entityId);
                        if (mountedPart.HasValue)
                        {
                            // A MOUNTED part reads as ATTACHED (not liftable): attached=true,
                            // held cleared, attachedTo = the hull (safe default, see spec 3.4).
                            // prefab/attach/title/itemType come from the mount record so the
                            // client still loads the right prefab. attachPos is the hull-local
                            // offset in metres.
                            //
                            // attachRot: the PLACED hull-local rotation, decoded from the mount
                            // record's packed 32-bit form - NOT the identity it used to hard-code.
                            // The visible facing of a "~"-follow part is driven by the 190602
                            // localRotation (FixedUpdateLerpLocalTransformBehaviour composes
                            // hull.rotation * localRotation, MoveTransform:245-252), which the
                            // re-seed above already honors via mountedPartRotation, and the live
                            // 1070 commit's own 1120 already carried the placed rotation - so this
                            // only makes the RE-CHECKOUT 1120 self-consistent with the 190602
                            // beside it (a served 1120 whose attachRot disagreed with the 190602
                            // was the "served component snaps the rotation back" suspect). Decode
                            // is the exact inverse of the Encode the commit packed with, and the
                            // sentinel decodes to identity, so an unrotated mount is unchanged.
                            // Still checkout-only: NOT a new stream, NOT a re-seed of a live part.
                            Game.Crafting.MountedParts.Mount mp = mountedPart.Value;
                            Improbable.Math.Vector3f attachPos = new Improbable.Math.Vector3f(
                                (float)mp.LocalOffset.MetresX, (float)mp.LocalOffset.MetresY, (float)mp.LocalOffset.MetresZ);
                            (float rw, float rx, float ry, float rz) =
                                Multiplayer.Placement.Quaternion32Packing.Decode(mp.PackedRotation);
                            Improbable.Corelib.Math.Quaternion attachRot =
                                new Improbable.Corelib.Math.Quaternion(rw, rx, ry, rz);
                            obj = new ShipPartState.Data(
                                true,                                   // attached
                                new EntityId(mp.AttachedToEntityId),    // attachedTo (the hull)
                                false,                                  // held
                                EntityId.InvalidEntityId,               // heldBy
                                EntityId.InvalidEntityId,               // heldByTool
                                attachPos,                              // attachPos
                                attachRot,                              // attachRot
                                new Bossa.Travellers.Motion.RelativeLocation(
                                    new EntityId(mp.AttachedToEntityId), attachPos, attachRot),  // lastAttachment
                                mp.PrefabName,
                                mp.AttachmentType,
                                mp.Title,
                                mp.ItemType,
                                new Option<Improbable.Math.Vector3f>(new Improbable.Math.Vector3f(1f, 1f, 1f)),  // scale (must be present)
                                new Option<Bossa.Travellers.Materials.SchematicsRarity>(Bossa.Travellers.Materials.SchematicsRarity.Tier1),
                                new Improbable.Collections.List<EntityId> { });           // playersPlacingPart
                            Console.WriteLine("[info] seeding 1120 for MOUNTED part " + entityId + " (prefab '"
                                + mp.PrefabName + "', attached=true, hull " + mp.AttachedToEntityId + ").");
                        }
                        else if (loosePart != null)
                        {
                            obj = new ShipPartState.Data(
                                false,                                  // attached
                                EntityId.InvalidEntityId,               // attachedTo
                                false,                                  // held
                                EntityId.InvalidEntityId,               // heldBy
                                EntityId.InvalidEntityId,               // heldByTool
                                new Improbable.Math.Vector3f(0f, 0f, 0f),                 // attachPos
                                new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0),       // attachRot (w-first identity)
                                new Bossa.Travellers.Motion.RelativeLocation(
                                    EntityId.InvalidEntityId,
                                    new Improbable.Math.Vector3f(0f, 0f, 0f),
                                    new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0)),  // lastAttachment
                                loosePart.PrefabName,
                                loosePart.AttachmentType,
                                loosePart.Title,
                                loosePart.ItemType,
                                new Option<Improbable.Math.Vector3f>(new Improbable.Math.Vector3f(1f, 1f, 1f)),  // scale (must be present)
                                new Option<Bossa.Travellers.Materials.SchematicsRarity>(Bossa.Travellers.Materials.SchematicsRarity.Tier1),
                                new Improbable.Collections.List<EntityId> { });           // playersPlacingPart
                            Console.WriteLine("[info] seeding 1120 for LOOSE part " + entityId + " (prefab '"
                                + loosePart.PrefabName + "', attach '" + loosePart.AttachmentType
                                + "', attached=false).");
                        }
                    }
                    else if (componentId == 1013)
                    {
                        // CraftableSpawningState: ShipPartVisualizer [Require]s it
                        // (ShipPartVisualizer.cs:38). spawning=FALSE means DONE spawning -
                        // ShipPartVisualizer.OnSpawningUpdated sets rigidbody.isKinematic =
                        // spawning, and CanPickUp returns false while spawning
                        // (ShipPartVisualizer.cs:155-161,233), so a finished loose part must
                        // be spawning=false to be non-kinematic and liftable. VERIFIED ctor
                        // (gencode CraftableSpawningState.cs:441).
                        //
                        // MATERIALIZE (3.2 / 6.2): a FRESHLY-crafted part is served
                        // spawning=true (timeLeft==totalTime) so CraftableSpawningVisualizer
                        // plays the dissolve-in; the LooseParts ledger flips it to the settled
                        // (false,0,0) after the dissolve so a later checkout gets the liftable
                        // part. SpawnStateFor returns the settled value for any part not
                        // currently materializing (and for boot-restored / mounted parts).
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            var spawnState = Game.Crafting.LooseParts.SpawnStateFor(entityId);
                            obj = new CraftableSpawningState.Data(
                                spawnState.Spawning, spawnState.TimeLeft, spawnState.TotalTime);
                        }
                    }
                    else if (componentId == 1108)
                    {
                        // LampState: LampVisualizer [Require]s it (LampVisualizer.cs:13).
                        // The light is on only when enabled AND IsFunctional (1236) are
                        // both true (LampVisualizer.cs:87), so a working lamp seeds
                        // enabled=true. VERIFIED ctor (gencode LampState.cs:309).
                        //
                        // A MOUNTED lamp serves its SWITCH ledger instead, so a relog /
                        // late joiner sees the on/off a player set (the 1211 Activate
                        // toggle, PartInteractionService). Lamps.IsOn returns true for
                        // any untracked id, so a LOOSE lamp keeps the proven always-on
                        // serve unchanged.
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            obj = new LampState.Data(WorldsAdriftRebornGameServer.Lamps.IsOn(entityId));
                        }
                    }
                    else if (componentId == 1236)
                    {
                        // IsTooDamagedToWorkState: LampVisualizer [Require]s it
                        // (LampVisualizer.cs:16). isFunctional=true (the lamp is undamaged),
                        // so with 1108 enabled the light turns on. healthThreshold is the
                        // fraction below which it would stop working; 0.2 is a sane default
                        // with no client reader that matters here. VERIFIED ctor (gencode
                        // IsTooDamagedToWorkState.cs:375, struct IsTooDamagedtoWorkStateData).
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            obj = new IsTooDamagedToWorkState.Data(0.2f, true);
                        }
                    }
                    else if (componentId == 1303)
                    {
                        // SailState: SailVisualizer + SailBehaviour [Require] it
                        // (SailVisualizer.cs:19, SailBehaviour.cs:15). A freshly crafted
                        // loose sail is furled and still: unfurled=false, power=0.
                        // SailVisualizer.OnEnable only subscribes to UnfurledUpdated (it
                        // fires immediately with unfurled=false) - no Option deref - so
                        // this idle Data is crash-safe. Only a sail seeds 1303, so only a
                        // sail ever requests it. VERIFIED ctor (gencode SailState.cs:375,
                        // SailStateData fields unfurled/power).
                        //
                        // A MOUNTED sail serves its FURL ledger instead, so a relog /
                        // late joiner sees the rigging a player set (the 1211 Activate
                        // toggle, PartInteractionService) - SailControlVisuals.Init
                        // starts every sail visually furled and the LateUpdate poll
                        // fires UnfurlSail off this served bit. Power rides the same
                        // bit (1 rigged / 0 furled), matching the toggle's push. An
                        // untracked (loose) sail reads false from the ledger, i.e. the
                        // furled idle it always had.
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            bool unfurled = WorldsAdriftRebornGameServer.Sails.IsUnfurled(entityId);
                            obj = new SailState.Data(unfurled, unfurled ? 1f : 0f);
                        }
                    }
                    else if (componentId == 1107)
                    {
                        // HornState: HornVisualizer [Require]s it (HornVisualizer.cs:12).
                        // A loose horn is silent: charge=0. HornVisualizer.OnEnable reads
                        // _state.Charge (a plain float, no Option), so idle Data cannot
                        // NRE. Only a horn seeds 1107, so only a horn requests it. VERIFIED
                        // ctor (gencode HornState.cs:362, HornStateData field charge).
                        //
                        // A MOUNTED horn serves its cooldown ledger's charge instead
                        // (1 ready, ramping 0..1 after a honk) so the needle a relog /
                        // late joiner sees matches what the 1211 Activate honk gate
                        // will actually allow. ChargeFor returns null for an untracked
                        // (loose) horn, which keeps the idle charge=0 serve unchanged.
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            obj = new HornState.Data(
                                WorldsAdriftRebornGameServer.HornChargeNow(entityId) ?? 0f);
                        }
                    }
                    else if (componentId == 1246)
                    {
                        // ShipPartVariationsSeedState. Structural panel geometry derives
                        // its stable art/material variation from this reader. Entity id is
                        // stable for the entity lifetime and therefore gives every peer the
                        // same appearance without storing another mutable field.
                        if (Game.Crafting.LooseParts.Is(entityId))
                        {
                            obj = new ShipPartVariationsSeedState.Data(unchecked((int)entityId));
                        }
                    }
                    else if (componentId == 1118)
                    {
                        // ShipPanelState. A loose panel has not yet been bent against a
                        // hull, so its collider target list is empty and bending is off.
                        // ShipPanelVisualizer still uses the prefab's authored PanelsX/Y
                        // to create the straight panel; after mounting, the normal panel
                        // request path can supply a shaped target in a future physics pass.
                        var panel = Game.Crafting.LooseParts.DefFor(entityId);
                        if (panel != null && (panel.ItemType == "smallPanel"
                            || panel.ItemType == "mediumPanel"
                            || panel.ItemType == "largePanel"
                            || panel.ItemType == "window"))
                        {
                            obj = new ShipPanelState.Data(
                                0, 0,
                                new Improbable.Collections.List<PanelCollider>(),
                                0,
                                false);
                        }
                    }
                    else if (componentId == 12281)
                    {
                        // ModularShipPartState. ModularEngine/ModularWing are shells;
                        // their visualizer calls ShipPartGenerator with this exact map.
                        // Values are Resources prefab BASENAMES (GetModulePrefab adds
                        // ModularShipComponents/<type>/<slot>/ itself). Every selected
                        // name is present in the shipped client's resources.assets.
                        string? itemType = Game.Crafting.LooseParts.DefFor(entityId)?.ItemType;
                        if (itemType == "proceduralEngineDefault")
                        {
                            obj = new ModularShipPartState.Data(new Map<string, string>
                            {
                                { "Body", "Engine_Body_001" },
                                { "Head", "Engine_Head_001" },
                                { "Prop", "Engine_Propeller_001" },
                            });
                        }
                        else if (itemType == "proceduralWingDefault")
                        {
                            obj = new ModularShipPartState.Data(new Map<string, string>
                            {
                                { "Aileron", "Wing_Airleon_003" },
                                { "Body", "Wing_Body_003" },
                                { "Connector", "Wing_Connector_002" },
                                { "Tip", "Wing_Tip_002" },
                            });
                        }
                    }
                    else if (componentId == 8062)
                    {
                        // ShipOwnersDeprecatedState - one of ShipVisualizer's three
                        // [Require] readers, and the DEPRECATED path of Gate B (ship
                        // ownership): when FeatureShipReviverBookeepingEnabled is OFF the
                        // client's ShipVisualizer.IsShipOwner matches SelectedCharacterUid
                        // against OwnersDeprecated (ShipVisualizer.cs:66-72). A BUILT hull
                        // with a recorded owner seeds that owner's CHARACTER uid so the
                        // owner's HostileItemPlacingPredicate treats the ship as placeable;
                        // a non-built hull (the static test ship) or an owner-less built
                        // hull stays UNOWNED (empty list), per OwnershipRegistrationPolicy.
                        // Empty, never null - the list is DeepCopied and Count-read.
                        // VERIFIED ctor (gencode ShipOwnersDeprecatedState.cs:309):
                        //   ShipOwnersDeprecatedState.Data(
                        //     Improbable.Collections.List<DeprecatedPlayerData>), and
                        //   DeprecatedPlayerData(string playerName, string characterUid,
                        //     EntityId playerEntityId) - only characterUid is read by
                        //   IsShipOwner, so playerName/entityId are inert placeholders.
                        //
                        // Not entity-gated: only a hull carries ShipVisualizer, so
                        // only a hull ever requests 8062 (the parts carry
                        // ShipPartVisualizer instead). See Multiplayer.ShipRecognition.
                        Improbable.Collections.List<DeprecatedPlayerData> deprecatedOwners =
                            new Improbable.Collections.List<DeprecatedPlayerData>();
                        foreach (string ownerUid in Multiplayer.Ship.OwnershipRegistrationPolicy.ShipOwnerUids(
                                     Game.Crafting.BuiltShips.IsBuiltHull(entityId),
                                     Game.Crafting.BuiltShips.OwnerFor(entityId)))
                        {
                            deprecatedOwners.Add(new DeprecatedPlayerData("", ownerUid, new EntityId(0)));
                        }

                        ShipOwnersDeprecatedState.Data ownersData =
                            new ShipOwnersDeprecatedState.Data(deprecatedOwners);

                        Console.WriteLine("[info] seeding 8062 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> " + deprecatedOwners.Count + " owner(s).");

                        obj = ownersData;
                    }
                    else if (componentId == 8071)
                    {
                        // ShipPartCountState - the second of ShipVisualizer's three
                        // [Require] readers, and the count the ship HUD reads. One
                        // Helm bolted on, everything else zero; mass 0 (cosmetic - no
                        // client consumer of it gates behaviour; lift/complexity are
                        // 1258/1257). The Sail/Helm/Core/Respawner counts are the only
                        // parts ShipPartCountData tracks - the deck and hull are not
                        // part types. VERIFIED ctor: ShipPartCountState.Data(
                        //   ShipPartCountData shipPartCountData, float mass), and
                        // ShipPartCountData(uint sail, uint helm, uint core, uint
                        // respawner). Values live in Multiplayer.ShipRecognition.
                        ShipPartCountData partCounts = new ShipPartCountData(
                            Multiplayer.ShipRecognition.AttachedSailCount,
                            Multiplayer.ShipRecognition.AttachedHelmCount,
                            Multiplayer.ShipRecognition.AttachedCoreCount,
                            Multiplayer.ShipRecognition.AttachedRespawnerCount);

                        ShipPartCountState.Data partCountData =
                            new ShipPartCountState.Data(partCounts, Multiplayer.ShipRecognition.Mass);

                        Console.WriteLine("[info] seeding 8071 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> " + Multiplayer.ShipRecognition.AttachedHelmCount + " helm(s).");

                        obj = partCountData;
                    }
                    else if (componentId == 4349)
                    {
                        // ShipRegisteredCharactersState - the third [Require] reader,
                        // and one that ALSO enables ShipRegisteredReviversVisualizer
                        // (its only [Require] is 4349). That visualizer is a passive
                        // query object with no OnEnable, so an empty crew is safe.
                        //
                        // EMPTY list, NOT null: ShipVisualizer.OnEnable subscribes to
                        // ReviverInfosCacheUpdated, whose add-accessor fires
                        // immediately with the current reviverInfosCache and reads
                        // .Count - a null list would NRE the enable chain.
                        //
                        // GATE B, the CURRENT path: when FeatureShipReviverBookeepingEnabled
                        // is ON, ShipVisualizer.IsShipOwner matches SelectedCharacterUid
                        // against reviverInfosCache[].characterUid (ShipVisualizer.cs:66-70).
                        // We seed BOTH 8062 and 4349 with the owner so ownership passes
                        // regardless of which feature-flag path the live client takes. A
                        // BUILT owned hull registers a ReviverInfo carrying the owner's
                        // CHARACTER uid; a non-built or owner-less hull registers none.
                        // lastSyncTimestamp is absent. VERIFIED ctors (gencode):
                        //   ShipRegisteredCharactersState.Data(
                        //     Improbable.Collections.List<ReviverInfo> reviverInfosCache,
                        //     Option<long> lastSyncTimestamp)  [ShipRegisteredCharactersState.cs:375]
                        //   ReviverInfo(long reviverUid, Option<EntityId> currentReviverEntityId,
                        //     string characterUid, Option<long> shipUid) [ReviverInfo.cs:17] -
                        //   only characterUid is read by IsShipOwner, so reviverUid 0 and the
                        //   absent Options are inert placeholders.
                        Improbable.Collections.List<ReviverInfo> reviverInfos =
                            new Improbable.Collections.List<ReviverInfo>();
                        foreach (string ownerUid in Multiplayer.Ship.OwnershipRegistrationPolicy.ShipOwnerUids(
                                     Game.Crafting.BuiltShips.IsBuiltHull(entityId),
                                     Game.Crafting.BuiltShips.OwnerFor(entityId)))
                        {
                            reviverInfos.Add(new ReviverInfo(0L, new Option<EntityId>(), ownerUid, new Option<long>()));
                        }

                        ShipRegisteredCharactersState.Data registeredData =
                            new ShipRegisteredCharactersState.Data(
                                reviverInfos,
                                new Option<long> { });

                        Console.WriteLine("[info] seeding 4349 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> " + reviverInfos.Count + " registered owner(s).");

                        obj = registeredData;
                    }
                    else if (componentId == 1518)
                    {
                        // THE WALKABLE FLOOR. 1518 ShipDeckState is one field, a
                        // List<Vector3f> of the deck polygon (VERIFIED, ilspycmd:
                        // ShipDeckState.Data(Improbable.Collections.List<Vector3f>
                        // vertices)). Its reader hands ShipDeckVisualizer that list,
                        // which - with the 1099 material above - runs the client's own
                        // MeshGenerator.MakeDeck with isTriggerCollider:false, giving a
                        // SOLID BoxCollider a player can stand on. The one component
                        // that turns the beam skeleton into a ship you can walk on.
                        //
                        // A BUILT deck panel serves its OWN derived polygon (the panel
                        // DeckGenerator produced for it, keyed by entity id in the built-ship
                        // ledger); the legacy static test deck - and any last-resort fallback -
                        // serves the pure Multiplayer.Deck.LocalVertices rectangle. Both are in
                        // the deck entity's own local space and pre-ShipScale; the client applies
                        // scale 2 in MakeMesh. Only the Vector3f/List construction lives here
                        // because those are game types.
                        Improbable.Collections.List<Improbable.Math.Vector3f> deckVertices =
                            new Improbable.Collections.List<Improbable.Math.Vector3f>();
                        System.Collections.Generic.IReadOnlyList<Multiplayer.Ship.ShipVector3>? builtVerts =
                            Game.Crafting.BuiltShips.DeckVerticesFor(entityId);
                        if (builtVerts != null)
                        {
                            foreach (Multiplayer.Ship.ShipVector3 v in builtVerts)
                            {
                                deckVertices.Add(new Improbable.Math.Vector3f(v.X, v.Y, v.Z));
                            }
                        }
                        else
                        {
                            foreach ((double vx, double vy, double vz) in Multiplayer.Deck.LocalVertices)
                            {
                                deckVertices.Add(new Improbable.Math.Vector3f((float)vx, (float)vy, (float)vz));
                            }
                        }

                        ShipDeckState.Data deckData = new ShipDeckState.Data(deckVertices);

                        Console.WriteLine("[info] seeding 1518 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> " + deckVertices.Count + "-vertex deck polygon.");

                        obj = deckData;
                    }

                    // ------------------------------------------------------------------
                    // THE TREE. Ten ids on the tree entity, three on the player.
                    //
                    // ALL-OR-NOTHING, and that is the whole reason these exist as a
                    // block. The client asks for a tree's components over
                    // SEND_COMPONENT_INTEREST and the server answers with
                    // failOnComponentInitError: true, so ONE id without a branch here
                    // throws away the entire batch. The entity has already been
                    // created by then, so the symptom is a fully-rendered, completely
                    // inert tree - right model, right place, no behaviour - and the
                    // only trace is one "[error] failed to initialize component NNNN"
                    // line in THIS server's log. Nothing appears in the client's.
                    //
                    // The ten were not guessed. The shipped prefab
                    // (entityprefabs/tree_unityclient, 148 nodes, 40 MonoBehaviours)
                    // was read out of resources.assets and its 13 visualizers'
                    // [Require] READER ids resolved, base classes included, which is
                    // exactly what VisualizerMetadataLookup does at runtime -
                    // GetComponentsInChildren<MonoBehaviour>(includeInactive: true)
                    // over the whole hierarchy. 13 is independently confirmed by the
                    // m_Enabled = 0 markers PrefabCompiler.DisableVisualizers leaves
                    // behind on precisely the visualizers.
                    //
                    // An eleventh id, 190604 GlobalTransformState, can arrive later:
                    // FixedUpdateLerpGlobalTransformBehaviour is [DontAutoEnable], and
                    // it wakes up if TransformChildHierarchyBehaviour falls back to
                    // HierarchyMode.Global. It already had a branch above. Note that
                    // when that happens the client re-sends its WHOLE interest set
                    // (SpatialCommunicator clears the dict and resends), so it arrives
                    // as an 11-id message, not as an increment.
                    // ------------------------------------------------------------------
                    else if (componentId == 1035)
                    {
                        // scale MUST be (1,1,1). TreeScaleVisualiser.OnEnable is one
                        // statement - transform.localScale = treeState.Scale.ToUnityVector()
                        // - with no guard, and Vector3d's default is (0,0,0). A tree
                        // seeded with the default is INVISIBLE, keeps working
                        // colliders, and logs nothing anywhere. It is indistinguishable
                        // from a failed asset load until someone walks into it.
                        //
                        // prefabName is TreeState's own copy of the asset name; it is
                        // read off the registration for the same reason 1041's is, so
                        // a second tree species cannot inherit this one's name.
                        // respawnTime is 0 and means nothing - `respawn_time` has zero
                        // references in the whole client, units included.
                        string treePrefabName =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.AssetName
                            ?? Multiplayer.Trees.AssetName;

                        Bossa.Travellers.Materials.TreeState.Data treeData =
                            new Bossa.Travellers.Materials.TreeState.Data(
                                new Bossa.Travellers.Materials.TreeStateData(
                                    new Improbable.Math.Vector3d(Multiplayer.Trees.Scale, Multiplayer.Trees.Scale, Multiplayer.Trees.Scale),
                                    treePrefabName,
                                    Multiplayer.Trees.RespawnTime));
                        obj = treeData;
                    }
                    else if (componentId == 1036)
                    {
                        // THE COMPONENT THAT IS THE PROTOCOL. sectionMask is what the
                        // client turns into which sections exist: TreeVisualizer's
                        // OnEnable feeds it to InitializeTree, which activates and
                        // deactivates each section GameObject by bit.
                        //
                        // The mask comes from the LIVE harvest state, never from
                        // Trees.FullSectionMask. A second player checking the tree out
                        // after the first has chopped half of it must be told what is
                        // actually standing, or the two clients disagree about the
                        // world and every later SetSectionMask arrives as a diff
                        // against the wrong baseline. The fallback is the full mask,
                        // which is only reached for an entity that is not a registered
                        // tree - i.e. never, in practice.
                        //
                        // dynamic is FALSE and that is a trap, not a preference:
                        // TreeBase.Dynamic's SETTER starts TreeAmbienceSfx's
                        // falling-audio loop on the true edge, on a tree that is not
                        // falling because nothing here has physics authority over it.
                        //
                        // woodType is Bossa's, recovered from the _unityworker prefabs.
                        // The client never reads it - TreeFSimState.woodType is written
                        // only by the UnityWorker-only visualizer - so it rides along
                        // purely so the eventual inventory grant has a species.
                        int liveMask = WorldsAdriftRebornGameServer.Harvest.MaskOf(entityId)
                            ?? Multiplayer.Trees.FullSectionMask;
                        string liveWood = WorldsAdriftRebornGameServer.Harvest.WoodTypeOf(entityId)
                            ?? Multiplayer.Trees.WoodType;

                        Improbable.Collections.List<int> sectionHealth = new Improbable.Collections.List<int>();
                        for (int s = 0; s < Multiplayer.Trees.SectionCount; s++)
                        {
                            sectionHealth.Add(Multiplayer.Trees.SectionHealth);
                        }

                        Bossa.Travellers.Materials.TreeFSimState.Data treeFsimData =
                            new Bossa.Travellers.Materials.TreeFSimState.Data(
                                new Bossa.Travellers.Materials.TreeFSimStateData(
                                    sectionHealth,
                                    Multiplayer.Trees.Dynamic,
                                    Multiplayer.Trees.SectionCount,
                                    liveMask,
                                    Multiplayer.Trees.ResourcePerSection,
                                    liveWood,
                                    new Option<float>(Multiplayer.Trees.MassPerSection)));

                        Console.WriteLine("[info] seeding 1036 for entity " + entityId
                            + " with sectionMask " + Convert.ToString(liveMask, 2)
                            + " (" + liveWood + ").");

                        obj = treeFsimData;
                    }
                    else if (componentId == 1016)
                    {
                        // health == maxHealth, both non-zero, and both halves matter.
                        // SalvageableItemVisualiser.OnEnable calls VisualiseItemDeath()
                        // when health is 0 and paints every renderer black. And
                        // IsSalvageable() is !IsDamaged() || IsRepairable(), where
                        // IsDamaged() is health < maxHealth - so a tree seeded damaged
                        // is only aimable if it also happens to be repairable. Equal
                        // and healthy is the unambiguous seed.
                        //
                        // Vulnerable, not Invulnerable: nothing on this path reads it,
                        // but the enum's 0 value is not a member at all (it starts at
                        // 1), so leaving it defaulted would put an out-of-range value
                        // on the wire.
                        //
                        // A DEPOSIT's 1016 is LIVE and drives its core: health is
                        // maxHealth minus the shots taken (a pure function of the shot
                        // count, so this seed and the live BroadcastDepositHealth agree),
                        // so a late joiner checking out a half-mined deposit is told the
                        // real health and the client's core-crack models pick up mid-way.
                        // A destroyed deposit reads health 0. Everything else (the tree,
                        // the nugget) keeps the full, equal, healthy pair.
                        Multiplayer.MetalNode? healthNode =
                            WorldsAdriftRebornGameServer.Nodes.NodeOf(entityId);
                        int seedHealth;
                        int seedMaxHealth;
                        if (healthNode != null && healthNode.IsDeposit)
                        {
                            seedMaxHealth = Multiplayer.MetalDeposits.MaxHealth;
                            seedHealth = Multiplayer.MetalDeposits.HealthAfter(
                                WorldsAdriftRebornGameServer.MetalHarvest.HitsOn(entityId));
                        }
                        else
                        {
                            seedMaxHealth = Multiplayer.Trees.ItemHealth;
                            seedHealth = Multiplayer.Trees.ItemHealth;
                        }

                        Bossa.Travellers.Items.ItemHealthState.Data itemHealthData =
                            new Bossa.Travellers.Items.ItemHealthState.Data(
                                new Bossa.Travellers.Items.ItemHealthStateData(
                                    seedHealth,
                                    seedMaxHealth,
                                    Bossa.Travellers.Items.VulnerabilityState.Vulnerable,
                                    false));
                        obj = itemHealthData;
                    }
                    else if (componentId == 1099)
                    {
                        // A crafted part served from the LooseParts ledger: its OWN itemType
                        // and a NON-EMPTY material list. A genuinely MOUNTED part is
                        // salvageable so PlayerMultitool emits the 2106 ShotEvent that the
                        // server's owned-shipyard-radius transaction validates. This is a
                        // client CAPABILITY bit, not authorization: loose and mounted parts
                        // both need to name hits, while the server rejects anything outside
                        // the owner's yard. LampVisualizer [Require]s
                        // 1099, so this is on the essential seed path, not cosmetic.
                        //
                        // WHY NON-EMPTY (the helm-freeze fix). The lamp guards its
                        // OriginalMaterials read, so an EMPTY list was fine for it. But most
                        // ship-part prefabs bake PartGraphicsVariationByMaterial, whose prefab
                        // getter is an UNGUARDED index: state.OriginalMaterials[_materialIndex]
                        // .rawMaterial (PartGraphicsVariationByMaterial.cs:24). On the HELM
                        // (Helm01) that ran on an empty list every frame in the SDK's deferred
                        // add-component queue -> ArgumentOutOfRangeException in
                        // List<SlottedMaterial>.get_Item -> the exception loop pegged the
                        // client main thread and the whole UI froze (VERIFIED from the client
                        // log: PartGraphicsVariationByMaterial.get_prefab ->
                        // PartGraphicsVariationManager.OnEnable). This is the SAME class the
                        // deck already fixed (ShipDeckVisualizer.OnEnable reads [0]).
                        //
                        // The list must be non-empty AND every entry a REAL material: an
                        // invented materialTypeId NREs ComponentMaterialColors (which resolves
                        // each entry by name through MaterialManager and dereferences the
                        // result). So we reuse the deck's proven-safe Wood material
                        // (Deck.MaterialTypeId = Trees.WoodType, category "Wood") - it resolves
                        // cleanly, and category "Wood" makes GetPrefabFromMaterial return the
                        // part's baked _woodPrefab (a valid mesh; the helm's icon is literally
                        // "helmwood"), so the part renders as a helm with its pilot visualizers
                        // dormant. We seed SEED_MATERIAL_SLOTS entries so any _materialIndex a
                        // loose part bakes is in range: the largest loose-part recipe has 4
                        // material slots (proceduralEngineDefault), so _materialIndex is at
                        // most 3; 8 covers that with headroom and is cheap (one small struct
                        // each, all the same real material).
                        var loosePart1099 = Game.Crafting.LooseParts.DefFor(entityId);
                        if (loosePart1099 != null)
                        {
                            const int SEED_MATERIAL_SLOTS = 8;
                            var looseMaterials = new Improbable.Collections.List<SlottedMaterial>();
                            for (int slot = 0; slot < SEED_MATERIAL_SLOTS; slot++)
                            {
                                looseMaterials.Add(new SlottedMaterial(
                                    slot,
                                    new Bossa.Travellers.Materials.RawMaterial(
                                        Multiplayer.Deck.MaterialTypeId,   // Trees.WoodType, resolves in MaterialManager
                                        1,                                  // quality
                                        Multiplayer.Deck.MaterialCategory,  // "Wood" -> baked _woodPrefab, never throws
                                        new Map<string, string> { }),
                                    1,                                      // amount
                                    new Option<Bossa.Travellers.Materials.RawMaterial> { }));
                            }

                            obj = new Bossa.Travellers.Salvaging.SalvageAndRepairState.Data(
                                new Bossa.Travellers.Salvaging.SalvageAndRepairStateData(
                                    loosePart1099.ItemType,
                                    0f, 0f, 0f, 1f,
                                    false,          // isRepairable
                                    true,           // report part hits; server enforces owned shipyard radius
                                    "",
                                    looseMaterials,
                                    false, 0f, new Option<float> { }));
                            Console.WriteLine("[info] seeding 1099 for LOOSE part " + entityId
                                + " (itemType '" + loosePart1099.ItemType + "', " + looseMaterials.Count
                                + " Wood materials so PartGraphicsVariationByMaterial does not IndexOutOfRange).");
                        }
                        else
                        {
                        // 1099 is required by BOTH a procedural ship hull
                        // (CustomShipFrameVisualizer will not enable without it,
                        // alongside 1209) and a tree (the Salvageable base class
                        // [Require]s it) - and the two need OPPOSITE values. So
                        // this branch has to ask WHICH entity it is seeding.
                        //
                        // Seeding on component id ALONE is exactly the bug the
                        // world-entity registry exists to prevent; 190602 above
                        // already dispatches the same way. Two separate 1099
                        // branches would have compiled, and the second would have
                        // been unreachable dead code that silently gave the tree
                        // the hull's values.
                        //
                        //   tree - isSalvageable TRUE, itemTypeId = the wood.
                        //          SalvageableItemVisualiser.IsSalvageable() is
                        //          what makes the salvage beam ACCEPT a target at
                        //          all, so false here is a tree you cannot chop.
                        //   hull - isSalvageable FALSE, no itemTypeId, so the
                        //          multitool offers nothing on a hull the server
                        //          owns and no client gets to be the first to find
                        //          out what our unimplemented salvage flow does.
                        //
                        // originalMaterials is EMPTY and never null on both paths:
                        // ComponentMaterialColors.SetMaterialColors resolves each
                        // entry by name through MaterialManager and dereferences
                        // the result, so an invented material id is a
                        // NullReferenceException in the client. Empty takes a
                        // guarded path - one logged error, mesh already built.
                        string? entityKey =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key;
                        // A BUILT ship's hull/deck (spawned by BuiltShipSpawner) carry the
                        // same 1099 as the static test hull/deck: the built hull wants the
                        // EMPTY material list (an invented id NREs ComponentMaterialColors),
                        // the built deck wants the one Wood material (ShipDeckVisualizer
                        // .OnEnable IndexOutOfRanges on an empty list).
                        bool isShipHull = entityKey == Multiplayer.WorldEntities.ShipFrameKey
                            || Game.Crafting.BuiltShips.IsBuiltHull(entityId);
                        bool isDeck = entityKey == Multiplayer.WorldEntities.DeckKey
                            || Game.Crafting.BuiltShips.IsBuiltDeck(entityId);

                        // A metal node names its OWN metal as the salvage item type,
                        // not the tree's wood. Cosmetic for the nugget today (it always
                        // renders as aluminium), but it is what the eventual salvage
                        // grant reads, and a real metal name is at least as safe against
                        // MaterialManager lookup as "birch" was.
                        Multiplayer.MetalNode? metalNode =
                            WorldsAdriftRebornGameServer.Nodes.NodeOf(entityId);

                        // A FUEL CANISTER names "fuel" as its salvage item type. THIS
                        // BRANCH IS THE WHOLE GATE for fuel gathering: the client's
                        // Salvageable base class [Require]s 1099 and
                        // PlayerMultitool.TryDeploySalvager refuses to raise a shot at all
                        // unless componentInEntity.IsSalvageable() is true
                        // (acs/PlayerMultitool.cs:296-300, acs/Salvageable.cs:8-9). So a
                        // canister MUST get isSalvageable=true here or it is a canister
                        // the beam will not touch - the exact failure the tree's comment
                        // above warns about. An EMPTIED canister stops being salvageable,
                        // so a late joiner cannot keep shooting a husk for more fuel.
                        bool isFuelCanister =
                            WorldsAdriftRebornGameServer.FuelCanisters.IsCanister(entityId);
                        bool fuelSpent =
                            WorldsAdriftRebornGameServer.FuelCanisters.IsDepleted(entityId);

                        string salvageItemType = isFuelCanister
                            ? Multiplayer.FuelPods.ItemTypeId
                            : metalNode != null
                                ? metalNode.MetalType
                                : (isShipHull ? "" : Multiplayer.Trees.WoodType);

                        // THE DECK IS THE ONE ENTITY WHOSE 1099 MUST NOT BE EMPTY.
                        // ShipDeckVisualizer.OnEnable reads OriginalMaterials[0]
                        // .rawMaterial.category (VERIFIED, ShipDeckVisualizer.cs:60)
                        // and GetMaterial() reads [0].materialTypeId - an empty list is
                        // an IndexOutOfRangeException and the deck's solid floor never
                        // builds. So the deck alone carries exactly one material: a
                        // "Wood" SlottedMaterial. category "Wood"/"Metal" picks the
                        // deck prototype; the name resolves through MaterialManager,
                        // which falls back safely (never NRE) on an unknown one - so a
                        // wrong name is a tint, not a crash, and the collider is built
                        // before any material is resolved anyway. Every OTHER entity
                        // keeps the EMPTY list the hull needs (an invented id WOULD NRE
                        // ComponentMaterialColors on the paths that dereference it).
                        // A SALVAGEABLE RESOURCE MUST NOT HAVE AN EMPTY LIST EITHER, and an
                        // empty one is why the salvage beam did nothing at all. When the
                        // player shoots a salvageable, PlayerMultitool.ImpactSalvage calls
                        // MaterialsEffectsData.GetOrDefaultFromMaterialList(OriginalMaterials)
                        // (acs/PlayerMultitool.cs:347), which sums the amounts, walks the
                        // list, and on falling through the loop indexes mat[0] - on an EMPTY
                        // list that is an ArgumentOutOfRangeException thrown INSIDE the shot
                        // callback, so the whole salvage attempt aborts and the player sees
                        // nothing happen (VERIFIED live: "[MaterialsTypes] Unable to select an
                        // effect from material select" + ArgumentOutOfRangeException in
                        // ImpactSalvage). RawMaterialBreakOnImpactVisualizer.OnBreak calls the
                        // same helper (acs/RawMaterialBreakOnImpactVisualizer.cs:26).
                        //
                        // The empty list was chosen because ComponentMaterialColors
                        // .SetMaterialColors dereferences a MaterialManager lookup, so an
                        // INVENTED id NREs. That hazard does not apply here: we name the
                        // resource's OWN REAL material (the same id the salvage grant uses -
                        // "fuel", the node's metal, the wood), never an invented one, and a
                        // fuel canister / metal deposit prefab does not carry
                        // ComponentMaterialColors at all (checked in the decompile). The ship
                        // HULL keeps the empty list it needs.
                        bool isSalvageableResource = !isShipHull && !isDeck
                            && (isFuelCanister || metalNode != null || !string.IsNullOrEmpty(salvageItemType));

                        string salvageMaterialCategory = isFuelCanister
                            ? "Fuel"
                            : metalNode != null ? "Metal" : "Wood";

                        Improbable.Collections.List<SlottedMaterial> originalMaterials =
                            isDeck
                                ? new Improbable.Collections.List<SlottedMaterial>
                                {
                                    new SlottedMaterial(
                                        0,
                                        new Bossa.Travellers.Materials.RawMaterial(
                                            Multiplayer.Deck.MaterialTypeId,
                                            1,                                  // quality
                                            Multiplayer.Deck.MaterialCategory,  // "Wood"
                                            new Map<string, string> { }),
                                        1,                                      // amount
                                        new Option<Bossa.Travellers.Materials.RawMaterial> { }),
                                }
                                : isSalvageableResource
                                    ? new Improbable.Collections.List<SlottedMaterial>
                                    {
                                        new SlottedMaterial(
                                            0,
                                            new Bossa.Travellers.Materials.RawMaterial(
                                                salvageItemType,                // the REAL material id
                                                metalNode?.Quality ?? 1,        // quality
                                                salvageMaterialCategory,
                                                new Map<string, string> { }),
                                            1,                                  // amount (>0: the sum must be non-zero)
                                            new Option<Bossa.Travellers.Materials.RawMaterial> { }),
                                    }
                                    : new Improbable.Collections.List<SlottedMaterial> { };

                        Bossa.Travellers.Salvaging.SalvageAndRepairState.Data salvageData =
                            new Bossa.Travellers.Salvaging.SalvageAndRepairState.Data(
                                new Bossa.Travellers.Salvaging.SalvageAndRepairStateData(
                                    salvageItemType,
                                    0f,             // salvageDamagePerPeriod - no client reader
                                    0f,             // repairAmountPerPeriod  - no client reader
                                    isShipHull ? 1f : 0f,   // repairToSalvageRatio - no client reader
                                    1f,             // period                 - no client reader
                                    false,          // isRepairable: keeps IsDamaged() false
                                    // isSalvageable: deck/hull are not; a fuel canister IS
                                    // until it has been emptied.
                                    isFuelCanister
                                        ? !fuelSpent
                                        : (!isShipHull && !isDeck),
                                    "",             // isSalvageableStatus
                                    originalMaterials,
                                    false,          // destroyOnSalvageComplete
                                    0f,             // salvageRatio           - no client reader
                                    new Option<float> { }));

                        obj = salvageData;
                        }
                    }
                    // ------------------------------------------------------------------
                    // THE ANCHORED METAL DEPOSIT. Three components on top of 1016/1099/
                    // 190602 above, all VERIFIED shapes (gencode Bossa.Travellers.Materials
                    // + client vtables confirmed present). deposit, core and crust are
                    // three MonoBehaviours on ONE SpatialOS entity in this build, so all
                    // three are served on the same entity id and cross-reference it.
                    // Served best-effort over interest (a deposit is not the sender's own
                    // entity), so a single missing branch skips one component, not the
                    // whole entity. See Multiplayer.MetalDeposits and findings-metal-deposits.md.
                    // ------------------------------------------------------------------
                    else if (componentId == 1255)
                    {
                        // 1255 MetalDepositState - identity. Only variantId is read
                        // (MetalDepositVisualiser.InstantiateVariant); coreId is a dead
                        // field. variantId MUST name a real MetalDepositVisuals asset in
                        // the biome PropLibrary or the visualiser sets enabled=false (an
                        // invisible, dead entity), so it is served from the node (a live
                        // capture / WAREBORN_DEPOSIT_VARIANT override reaches the wire).
                        // coreId points at the deposit's own entity.
                        Multiplayer.MetalNode? depositNode =
                            WorldsAdriftRebornGameServer.Nodes.NodeOf(entityId);
                        string variantId = depositNode?.VariantId ?? Multiplayer.MetalDeposits.VariantId();

                        Bossa.Travellers.Materials.MetalDepositState.Data depositData =
                            new Bossa.Travellers.Materials.MetalDepositState.Data(
                                new Bossa.Travellers.Materials.MetalDepositStateData(
                                    variantId,
                                    new EntityId(entityId)));

                        Console.WriteLine("[info] seeding 1255 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") variant '" + variantId + "'.");

                        obj = depositData;
                    }
                    else if (componentId == 2103)
                    {
                        // 2103 MetalRockCoreState - the core. Only isDestroyed is live
                        // (the client's OnCoreDestroyed); depositId/islandId are dead
                        // fields but attachedEntities MUST be non-null (DeepCopy reads
                        // .Count). isDestroyed comes from the ledger, so a late joiner
                        // checking out an emptied deposit is told it is destroyed - and
                        // the client's one-shot suppression shows the SILENT destroyed
                        // state, not a replayed explosion.
                        bool coreDestroyed = WorldsAdriftRebornGameServer.Nodes.IsDestroyed(entityId);

                        // attachedEntities lists the shard(s) lodged in this core - the
                        // authoritative core->shard relationship (the shard's 1305
                        // rockCoreId is the reverse link). Non-null always (DeepCopy reads
                        // .Count); empty for a deposit with no shard, exactly as before.
                        Improbable.Collections.List<EntityId> attachedShards =
                            new Improbable.Collections.List<EntityId>();
                        foreach (long shardId in WorldsAdriftRebornGameServer.AtlasShards.ShardsForHost(entityId))
                        {
                            attachedShards.Add(new EntityId(shardId));
                        }

                        Bossa.Travellers.Materials.MetalRockCoreState.Data coreData =
                            new Bossa.Travellers.Materials.MetalRockCoreState.Data(
                                new Bossa.Travellers.Materials.MetalRockCoreStateData(
                                    new EntityId(entityId),    // depositId (self)
                                    attachedShards,            // attachedEntities: the lodged shard(s)
                                    EntityId.InvalidEntityId,  // islandId: dead field
                                    coreDestroyed));

                        Console.WriteLine("[info] seeding 2103 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") isDestroyed=" + coreDestroyed + ".");

                        obj = coreData;
                    }
                    else if (componentId == 12283)
                    {
                        // 12283 MetalRockCrustState - the crust. shotPoints is the
                        // accumulated damage cloud (plain Vector3f, NO x4096) a late
                        // joiner replays via SimulatePastShot; it MUST be non-null
                        // (DeepCopy reads .Count) and is capped by the ledger
                        // (NodeRegistry.MaxShotPoints). exploded comes from the ledger.
                        // The LIVE break VFX ride the transient ShotCrustEvent
                        // (WorldsAdriftRebornGameServer.BroadcastCrustShot), which is NOT
                        // part of Data - so a joiner reconstructs the STATE silently
                        // rather than flashing every past impact. coreId and depositId
                        // both name this same one-entity deposit.
                        bool crustExploded = WorldsAdriftRebornGameServer.Nodes.IsDestroyed(entityId);

                        Improbable.Collections.List<Improbable.Math.Vector3f> crustShotPoints =
                            new Improbable.Collections.List<Improbable.Math.Vector3f>();
                        foreach (Multiplayer.ShotPoint sp in WorldsAdriftRebornGameServer.Nodes.ShotPointsOf(entityId))
                        {
                            crustShotPoints.Add(new Improbable.Math.Vector3f(sp.X, sp.Y, sp.Z));
                        }

                        Bossa.Travellers.Materials.MetalRockCrustState.Data crustData =
                            new Bossa.Travellers.Materials.MetalRockCrustState.Data(
                                new Bossa.Travellers.Materials.MetalRockCrustStateData(
                                    new EntityId(entityId),   // coreId (self)
                                    new EntityId(entityId),   // depositId (self)
                                    crustShotPoints,
                                    crustExploded));

                        Console.WriteLine("[info] seeding 12283 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") " + crustShotPoints.Count + " shot point(s), exploded=" + crustExploded + ".");

                        obj = crustData;
                    }
                    // ------------------------------------------------------------------
                    // THE ATLAS SHARD. A SEPARATE entity from its host deposit, carrying
                    // 1305 (identity/host link) + 2102 (lodged state) on top of the
                    // 190602 (transform) + 1210 (PickUp prompt) served above. Its client
                    // visualiser [Require]s ONLY 1305 and won't initialise until its
                    // rockCoreId resolves to an initialised MetalDepositCoreVisualiser, so
                    // the host id MUST be a live deposit; the InteractiveObjectVisualizer
                    // it also carries [Require]s 1210. VERIFIED shapes: gencode
                    // Bossa.Travellers.Materials/MetalDepositAtlasShardStateData.cs:6-16
                    // and LodgeableStateData.cs (ctor bool,EntityId,string). See
                    // findings-atlas-shards §2 + §4 step 3.
                    // ------------------------------------------------------------------
                    else if (componentId == 1257)
                    {
                        // 1257 ParentingMassAdderState - {float mass, bool reliable}
                        // (VERIFIED ctor gencode Bossa.Travellers.Ship/ParentingMassAdderState.cs:375).
                        // The hull's mass, read by ShipLiftVisualizer.Load/IsOverloaded and,
                        // through it, the pilot's ShipControlsBehaviour.UpdateVertical EVERY
                        // FRAME while driving - absence NRE'd the whole flight input loop
                        // (12,077/session measured). Mass is a RECONSTRUCTION (retail values
                        // lost): modest enough that the flight-agent's generous 1258 lift
                        // seed keeps Load < 1 (not overloaded), tunable without rebuild.
                        float shipMass = 800f;
                        string? massEnv = Environment.GetEnvironmentVariable("WAREBORN_SHIP_MASS");
                        if (!string.IsNullOrEmpty(massEnv) && float.TryParse(massEnv,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float parsedMass)
                            && parsedMass > 0f && parsedMass < 1000000f)
                        {
                            shipMass = parsedMass;
                        }
                        obj = new Bossa.Travellers.Ship.ParentingMassAdderState.Data(shipMass, false);
                    }
                    else if (componentId == 1121)
                    {
                        // 1121 OriginalMassState - {float mass} (VERIFIED ctor gencode
                        // Bossa.Travellers.Ship/OriginalMassState.cs:309). A part's own
                        // authored mass; served modest so parented parts add sane weight.
                        obj = new Bossa.Travellers.Ship.OriginalMassState.Data(50f);
                    }
                    else if (componentId == 1294)
                    {
                        // 1294 UidState - a single long uid (VERIFIED ctor: UidState.Data(long),
                        // gencode Bossa.Travellers.Misc/UidState.cs:309). Served for ANY entity
                        // that requests it, uid = the entity id (stable and unique per session).
                        // This id used to be KnownAbsent, which was NOT safe: the player's own
                        // ClientAuthoritativePlayerMovement.CollectDataHighFrequency reads
                        // UidVisualizer.Uid every movement tick regardless of visualizer
                        // enablement, so a never-injected reader threw an NRE per tick
                        // (3,290 in one measured session - a stutter contributor).
                        obj = new Bossa.Travellers.Misc.UidState.Data(entityId);
                    }
                    else if (componentId == 1305)
                    {
                        // 1305 MetalDepositAtlasShardState - {rockCoreId, slotId}. rockCoreId
                        // is the host DEPOSIT entity (which carries the core in this
                        // one-entity-deposit build), stored in the AtlasShards ledger at
                        // spawn. slotId indexes the core's ScrapSlots. A shard whose host is
                        // not registered gets an invalid rockCoreId, which is the correct
                        // "do not render" value (the client's DepositExists() gate returns
                        // false), not a crash.
                        long? shardHost = WorldsAdriftRebornGameServer.AtlasShards.HostOf(entityId);
                        int shardSlot = WorldsAdriftRebornGameServer.AtlasShards.SlotOf(entityId)
                            ?? Multiplayer.AtlasShardCatalogue.DefaultSlotId;
                        EntityId rockCoreId = shardHost.HasValue
                            ? new EntityId(shardHost.Value)
                            : EntityId.InvalidEntityId;

                        Bossa.Travellers.Materials.MetalDepositAtlasShardState.Data shardData =
                            new Bossa.Travellers.Materials.MetalDepositAtlasShardState.Data(
                                new Bossa.Travellers.Materials.MetalDepositAtlasShardStateData(
                                    rockCoreId,
                                    shardSlot));

                        Console.WriteLine("[info] seeding 1305 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") rockCoreId=" + (shardHost?.ToString() ?? "INVALID") + " slot=" + shardSlot + ".");

                        obj = shardData;
                    }
                    else if (componentId == 2102
                        && WorldsAdriftRebornGameServer.FuelCanisters.IsCanister(entityId))
                    {
                        // 2102 LodgeableState for a FUEL CANISTER. Free-standing, so
                        // ownerId is invalid and slotName empty. isLodged is seeded TRUE =
                        // KINEMATIC: the canister sits still on the ground rather than
                        // rolling off the island. That is the component's ONLY job here -
                        // FuelPodVisualiser_fsim [Require]s LodgeableState and pipes
                        // IsLodged straight into FuelPod.IsLodged -> Rigidbody.isKinematic
                        // (acs/FuelPodVisualiser_fsim.cs:47-50, acs/FuelPod.cs:48-51). It
                        // is a PHYSICS flag, NOT an acquisition state: the canister is
                        // salvaged with the beam, so nothing ever "dislodges" it. VERIFIED
                        // ctor: LodgeableStateData(bool isLodged, EntityId ownerId, string
                        // slotName) - gencode Bossa.Travellers.Materials/LodgeableStateData.cs.
                        Bossa.Travellers.Materials.LodgeableState.Data podLodge =
                            new Bossa.Travellers.Materials.LodgeableState.Data(
                                new Bossa.Travellers.Materials.LodgeableStateData(
                                    true,
                                    EntityId.InvalidEntityId,
                                    Multiplayer.FuelPods.SlotName));

                        Console.WriteLine("[info] seeding 2102 for FUEL POD entity " + entityId
                            + " (" + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") isLodged=true (kinematic).");

                        obj = podLodge;
                    }
                    else if (componentId == 2102)
                    {
                        // 2102 LodgeableState - {isLodged, ownerId, slotName}. isLodged is
                        // the live release flag: true while lodged (kinematic, in the slot),
                        // flipped false + a Dislodged event when the core is destroyed
                        // (BroadcastShardReleased). ownerId names the host deposit; slotName
                        // is empty (the client indexes slots by the 1305 slotId int, not
                        // this string). A late joiner checking out an already-mined deposit
                        // is seeded isLodged=false, matching the released/collected world.
                        bool lodged = WorldsAdriftRebornGameServer.AtlasShards.IsLodged(entityId);
                        long? lodgeHost = WorldsAdriftRebornGameServer.AtlasShards.HostOf(entityId);
                        EntityId ownerId = lodgeHost.HasValue
                            ? new EntityId(lodgeHost.Value)
                            : EntityId.InvalidEntityId;

                        Bossa.Travellers.Materials.LodgeableState.Data lodgeData =
                            new Bossa.Travellers.Materials.LodgeableState.Data(
                                new Bossa.Travellers.Materials.LodgeableStateData(
                                    lodged,
                                    ownerId,
                                    Multiplayer.AtlasShardCatalogue.SlotName));

                        Console.WriteLine("[info] seeding 2102 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") isLodged=" + lodged + " ownerId=" + (lodgeHost?.ToString() ?? "INVALID") + ".");

                        obj = lodgeData;
                    }
                    // ------------------------------------------------------------------
                    // THE GLOBAL BIOME TABLE. Served on the GLOBAL entity so the
                    // deposit's MetalDepositVisualiser can resolve a biome and stop
                    // blocking (findings: FindBiomeAsync polls GetBiomeAt forever
                    // otherwise, and the rock never draws). GlobalBiomeDataVisualizer
                    // [Require]s BOTH of these and enables only with both present.
                    // ------------------------------------------------------------------
                    else if (componentId == 1253)
                    {
                        // 1253 GlobalBiomeVoronoiCentresState - the world biome table.
                        // ONE Voronoi centre suffices: FindClosestZone returns a valid
                        // index for ANY position whenever there is at least one centre,
                        // and SqrDist compares X/Z only, so a single Biome1 centre
                        // resolves every Haven position to Biome1 - the biome whose
                        // PropLibrary holds metal_deposit_composite_light_01. Both lists
                        // are non-null (DeepCopy reads .Count); respawnerCounts is empty
                        // (GetRespawnerCountAt guards on its Count).
                        Improbable.Collections.List<Bossa.Travellers.Globaldata.BiomeVoronoiCentre> biomeCentres =
                            new Improbable.Collections.List<Bossa.Travellers.Globaldata.BiomeVoronoiCentre>
                            {
                                new Bossa.Travellers.Globaldata.BiomeVoronoiCentre(
                                    0f, 0f,
                                    Bossa.Travellers.Biomes.BiomeType.Biome1,
                                    Bossa.Travellers.World.CivilizationType.Saborian),
                            };

                        Bossa.Travellers.Globaldata.GlobalBiomeVoronoiCentresState.Data biomeCentresData =
                            new Bossa.Travellers.Globaldata.GlobalBiomeVoronoiCentresState.Data(
                                new Bossa.Travellers.Globaldata.GlobalBiomeVoronoiCentresStateData(
                                    biomeCentres,
                                    new Improbable.Collections.List<int> { }));

                        Console.WriteLine("[info] seeding 1253 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> 1 Voronoi centre (Biome1); unblocks deposit biome resolution.");

                        obj = biomeCentresData;
                    }
                    else if (componentId == 8064)
                    {
                        // 8064 DevBiome - the SECOND [Require] of
                        // GlobalBiomeDataVisualizer (no enable without both). Empty
                        // PVE-override map: BiomePVEOverride does a TryGetValue and treats
                        // a miss as "no override", so empty is the correct "nothing
                        // forced" value. Non-null (DeepCopy reads .Count).
                        Bossa.Travellers.Biomes.DevBiome.Data devBiomeData =
                            new Bossa.Travellers.Biomes.DevBiome.Data(
                                new Improbable.Collections.Map<Bossa.Travellers.Biomes.BiomeType, bool> { });

                        Console.WriteLine("[info] seeding 8064 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") -> empty DevBiome overrides.");

                        obj = devBiomeData;
                    }
                    else if (componentId == 1183)
                    {
                        // Empty. ReconsumablesClient is on the tree because `Tree`
                        // carries eight TreeSeedEmitter children; its OnEnable only
                        // subscribes to two EVENTS (Consumed, ReadyToBeConsumed) and
                        // its handlers do a TryGetValue against a cache we never fill.
                        // An empty map is therefore not a placeholder - it is the
                        // correct value for a tree nobody is eating.
                        Bossa.Travellers.Creatures.Food.ReconsumablesState.Data reconsumablesData =
                            new Bossa.Travellers.Creatures.Food.ReconsumablesState.Data(
                                new Bossa.Travellers.Creatures.Food.ReconsumablesStateData(
                                    new Map<long, Bossa.Travellers.Creatures.Food.ReconsumableRecord> { }));
                        obj = reconsumablesData;
                    }
                    else if (componentId == 1232)
                    {
                        // At-rest collision report. relativeVelocitySqr = 0 is the
                        // load-bearing zero: RollAndDragFxController.OnDataUpdated
                        // gates its whole active path on `> 0f`, so a zero here takes
                        // the quiet branch and starts no rolling audio and no drag VFX
                        // on a tree that is standing still. The two material names are
                        // empty strings rather than null because PairsEqual compares
                        // them.
                        Bossa.Travellers.Physical.RigidbodyCollisionReporterState.Data collisionData =
                            new Bossa.Travellers.Physical.RigidbodyCollisionReporterState.Data(
                                new Bossa.Travellers.Physical.RigidbodyCollisionReporterStateData(
                                    new Bossa.Travellers.Physical.RollAndDragData(
                                        "", "", 0f, 0f,
                                        new Vector3f(0f, 0f, 0f),
                                        0f,      // relativeVelocitySqr: silence
                                        false)));
                        obj = collisionData;
                    }
                    else if (componentId == 4333)
                    {
                        // Not deteriorating, not sinking, not weathered. sunkRatio = 0
                        // is the one the eye would catch: DeteriorateVisualizerClient
                        // reads it at OnEnable (OnRatioUpdated(stateReader.SunkRatio))
                        // and IsSinking() is `SunkRatio > 0f`, so a stray non-zero
                        // would show a tree part-way into the ground. Every Option is
                        // absent, which is what "this has never begun" means.
                        Bossa.Travellers.Items.DeteriorateState.Data deteriorateData =
                            new Bossa.Travellers.Items.DeteriorateState.Data(
                                new Bossa.Travellers.Items.DeteriorateStateData(
                                    new Option<long> { },  // timeBecameAtRest
                                    false,                 // isDeteriorating
                                    new Option<long> { },  // timeBeganDeterioration
                                    new Option<long> { },  // timeBeganSinking
                                    0d,                    // deteriorationRatio
                                    0f,                    // sunkRatio
                                    new Option<int> { },   // secondsToMaxDeteriorate
                                    false,                 // isParentSinking
                                    false,                 // sinkBeforeCleanup
                                    new Option<int> { })); // secondsToStartDeteriorate
                        obj = deteriorateData;
                    }
                    else if (componentId == 4400)
                    {
                        // timeOfDeath ABSENT. TrackedEntityLoadClientVisualizer's only
                        // behaviour is: on a timeOfDeath that HasValue, add a
                        // DissolvableEntity and dissolve the object. A present value
                        // would dissolve the tree away. (The prefab carries two copies
                        // of this visualizer; both read the same id, so it is one seed.)
                        Bossa.Travellers.Blight.TrackedEntityLoadState.Data trackedLoadData =
                            new Bossa.Travellers.Blight.TrackedEntityLoadState.Data(
                                new Bossa.Travellers.Blight.TrackedEntityLoadStateData(
                                    new Option<long> { }));
                        obj = trackedLoadData;
                    }
                    else if (componentId == 1231)
                    {
                        // ON THE PLAYER, not the tree. Where the salvage beam is
                        // pointing, published by SalvagerAimerObserver.
                        //
                        // maxBoltDistance MUST BE NON-ZERO, and this is the most
                        // expensive default on the whole path. IsValidHit is
                        // `AreWithinDistance(hit.point, playerPos, MaxBoltDistance)
                        // && IsSalvageable(...)`. At 0 nothing is ever in range, so
                        // HitInfo stays null forever, so TreeCuttingBehaviour publishes
                        // {InvalidEntityId, -1, false} once - and its FinishAndSend
                        // then suppresses every subsequent send because nothing changes
                        // again. The server gets exactly ONE 1037 packet and never
                        // another, which looks precisely like "the grant did not work".
                        //
                        // The other three fields are the client's to overwrite on its
                        // first Update; they are seeded to "aiming at nothing".
                        Bossa.Travellers.Items.SalvagerAimerState.Data aimerData =
                            new Bossa.Travellers.Items.SalvagerAimerState.Data(
                                new Bossa.Travellers.Items.SalvagerAimerStateData(
                                    EntityId.InvalidEntityId,
                                    new Coordinates(0, 0, 0),
                                    new Vector3f(0f, 0f, 0f),
                                    Multiplayer.MirrorSendPolicy.SalvagerMaxBoltDistance));
                        obj = aimerData;
                    }
                    else if (componentId == 1037)
                    {
                        // ON THE PLAYER. The cut signal, seeded as "cutting nothing" -
                        // exactly the value TreeCuttingBehaviour itself publishes when
                        // the beam is on nothing, so the client's first real latch is a
                        // genuine change and is not suppressed by FinishAndSend.
                        //
                        // Seeding it is not optional even though the client is the
                        // writer: ComponentUpdateManager.HandleComponentUpdate looks the
                        // inbound update up in ComponentMap[peer][entity][component] and
                        // silently drops anything it has no stored component for. No
                        // seed, no handler call.
                        Bossa.Travellers.Materials.TreeCutterState.Data treeCutterData =
                            new Bossa.Travellers.Materials.TreeCutterState.Data(
                                new Bossa.Travellers.Materials.TreeCutterStateData(
                                    EntityId.InvalidEntityId,
                                    -1,
                                    false));
                        obj = treeCutterData;
                    }
                    else if (componentId == 2105)
                    {
                        // ON THE PLAYER, and one of three that must arrive together:
                        // 2105/2106/2002 are the [Require] WRITERS of
                        // PlayerMultitoolVisualizer, and a visualizer enables only when
                        // EVERY writer is injected. Two of three is worth what zero is.
                        //
                        // Mode Default, not Salvage: the client picks the mode from its
                        // own hotbar and publishes it; pre-empting that would fight the
                        // player's selection. salvagerBlastDamage has exactly one
                        // occurrence in the entire decompile - its own declaration - so
                        // its value is arbitrary.
                        Bossa.Travellers.Items.MultiToolPlayerState.Data multitoolPlayerData =
                            new Bossa.Travellers.Items.MultiToolPlayerState.Data(
                                new Bossa.Travellers.Items.MultiToolPlayerStateData(
                                    false,
                                    Bossa.Travellers.Items.MultitoolMode.Default,
                                    0));
                        obj = multitoolPlayerData;
                    }
                    else if (componentId == 2106)
                    {
                        // ON THE PLAYER. Salvager off, not jammed, not engaged - the
                        // client flips all three itself the moment the trigger is held.
                        Bossa.Travellers.Salvaging.MultitoolSalvagerState.Data salvagerData =
                            new Bossa.Travellers.Salvaging.MultitoolSalvagerState.Data(
                                new Bossa.Travellers.Salvaging.MultitoolSalvagerStateData(false, false, false));
                        obj = salvagerData;
                    }
                    else if (componentId == 2002)
                    {
                        // ON THE PLAYER. Repairer off. Seeded only because
                        // PlayerMultitoolVisualizer [Require]s its writer; nothing on
                        // the chopping path touches it.
                        Bossa.Travellers.Salvaging.MultitoolRepairerState.Data repairerData =
                            new Bossa.Travellers.Salvaging.MultitoolRepairerState.Data(
                                new Bossa.Travellers.Salvaging.MultitoolRepairerStateData(false, false));
                        obj = repairerData;
                    }
                    // NOTE: a second `componentId == 1109` branch used to sit here, seeding
                    // PilotState with EntityId(10) instead of EntityId(0). It was unreachable -
                    // the branch at the top of this same else-if chain always won - which was
                    // lucky, because EntityId(10) is a VALID entity id. IsDriving() is
                    // EntityId.IsValidEntityId(DrivingEntityId), so had the chain ever been
                    // reordered the player would have been permanently "driving" and unable to
                    // move. Removed rather than left as a trap.
                    else if (componentId == Multiplayer.TeleportPolicy.TeleportRequestStateComponentId)
                    {
                        // 190607 TeleportRequestState. Everything about this seed -
                        // request 0, parent absent - is in TeleportComponent.Seed,
                        // on purpose: this chain is edited by several workstreams
                        // at once and teleport should not widen it.
                        obj = TeleportComponent.Seed();
                    }
                    else
                    {
                        // LOUD ON PURPOSE. This is not the quiet path above: it
                        // means the client asked for something nobody has ever
                        // thought about, which is how every new entity type has
                        // announced itself so far. It still costs an
                        // all-or-nothing caller its whole batch.
                        Console.WriteLine(Multiplayer.ComponentAbsencePolicy.DescribeUnhandled(entityId, componentId));
                        outcome = Multiplayer.ComponentSeedOutcome.UnhandledId;
                    }

                    if (obj != null)
                    {
                        refId = ClientObjects.Instance.CreateReference(obj);
                        ComponentProtocol.ClientObject wrapper = new ComponentProtocol.ClientObject();
                        wrapper.Reference = refId;

                        ComponentProtocol.ClientSerialize serialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientSerialize>(ComponentsManager.Instance.ClientComponentVtables[i].Serialize);
                        serialize(componentId, 2, &wrapper, buffer, length);

                        outcome = *length > 0
                            ? Multiplayer.ComponentSeedOutcome.Serialized
                            : Multiplayer.ComponentSeedOutcome.SerializeFailed;

                        // store refId for player and component as we need this to access the component later
                        // this needs to change in the future, we need to make use of the games structures.
                        // noone wants to work with this triple dictionary >.>
                        if (!GameState.Instance.ComponentMap.ContainsKey(player))
                        {
                            GameState.Instance.ComponentMap.Add(player, new Dictionary<long, Dictionary<uint, ulong>> {
                                { entityId, new Dictionary<uint, ulong> {
                                    {
                                        componentId, refId
                                    }
                                } }
                            });
                        }
                        else
                        {
                            if (GameState.Instance.ComponentMap[player].ContainsKey(entityId))
                            {
                                if (GameState.Instance.ComponentMap[player][entityId].ContainsKey(componentId))
                                {
                                    // here we need to decide if we want to update the existing refId with the new one or drop the creation above.
                                    // this case should only happen if the same component is added multiple times to the same entityId and player
                                    //
                                    // The slot already holds a live native reference. Overwriting it
                                    // with the freshly created refId above drops the only handle to the
                                    // old one, so it leaks natively unless destroyed first. It is being
                                    // replaced in the same breath - it will never be serialized again -
                                    // so it is safe to destroy here (see ComponentRefCleanup's contract).
                                    ulong deadRefId = GameState.Instance.ComponentMap[player][entityId][componentId];
                                    ClientObjects.Instance.DestroyReference(deadRefId);
                                    GameState.Instance.ComponentMap[player][entityId][componentId] = refId;
                                }
                                else
                                {
                                    GameState.Instance.ComponentMap[player][entityId].Add(componentId, refId);
                                }
                            }
                            else
                            {
                                GameState.Instance.ComponentMap[player].Add(entityId, new Dictionary<uint, ulong> { { componentId, refId } });
                            }
                        }
                    }
                }
            }

            if (!hasClientVtable)
            {
                Console.WriteLine("[error] component " + componentId + " has NO client vtable in this build"
                    + " (entity " + entityId + "). Not a missing seed - the component does not exist"
                    + " in the shipped client, so no branch here can fix it.");
                return Multiplayer.ComponentSeedOutcome.NoClientVtable;
            }

            return outcome;
        }

        /// <summary>
        /// Components the server MUTATES after seeding, and must therefore never
        /// re-seed from defaults.
        ///
        /// Deliberately short. Most seeds here are stateless - re-fabricating
        /// them produces the same bytes - and adding one of those to this set
        /// would only pin a reference for no benefit. A component belongs here
        /// once something writes to its stored Data at runtime.
        ///
        /// 1081 InventoryState and 1280 WearableUtilsState are the inventory
        /// pair, both written by InventoryPush. 1088 is NOT here: its seed
        /// already consults Appearances.Get, so re-seeding it is idempotent by
        /// construction.
        /// </summary>
        private static bool IsLiveState(uint componentId)
        {
            return componentId == 1081 || componentId == 1280;
        }
    }
}
