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
        public unsafe static void InitAndSerialize(ENetPeerHandle player, long entityId, uint componentId, byte** buffer, uint* length)
        {
            // TWO DIFFERENT FAILURES LOOK IDENTICAL TO THE CALLER, and both come
            // back as length 0:
            //
            //   a) the id has no vtable in this client build at all, so the loop
            //      below never enters and nothing is written;
            //   b) the id has a vtable but no seed branch here, which logs
            //      "[ToDo] unhandled component id".
            //
            // (a) means the component does not exist in the shipped client and no
            // amount of writing branches will help; (b) means write a branch. Any
            // caller with failOnComponentInitError set loses its ENTIRE batch
            // either way, so telling them apart is the difference between an
            // afternoon and a day. Hence the flag.
            bool hasClientVtable = false;

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
                        return;
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
                        // parent stays ABSENT (the null below). With a parent present
                        // the client applies localPosition raw with no origin remap;
                        // with it absent the live branch runs,
                        // transform.position = localPosition / 4096 - OffsetOrigin,
                        // which is what we have empirically proven works. Do not add
                        // one here - see docs/research/findings-world.md Q4.
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

                        TransformStateData tInit = new TransformStateData(new FixedPointVector3(new Improbable.Collections.List<long> { seed.X, seed.Y, seed.Z }),
                                                                new Quaternion32(1023), // identity sentinel is the low 10 bits ALL set; 1 decodes to NaN
                                                                null,
                                                                new Improbable.Math.Vector3d(0f, 0f, 0f),
                                                                new Improbable.Math.Vector3f(0f, 0f, 0f),
                                                                new Improbable.Math.Vector3f(0f, 0f, 0f),
                                                                false,
                                                                0f);
                        TransformState.Data tData = new TransformState.Data(tInit);

                        Console.WriteLine("[info] seeding 190602 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") at " + seed + ".");

                        obj = tData;
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
                        PlayerName.Data pData = new PlayerName.Data(new PlayerNameData("sp00ktober", "id", "cUid", "bossaToken", "bossaId"));

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
                    else if(componentId == 1071)
                    {
                        BuilderServerState.Data bsData = new BuilderServerState.Data(new BuilderServerStateData(new EntityId(0)));

                        obj = bsData;
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
                    else if(componentId == 1207)
                    {
                        ShipHullAgentState.Data shData = new ShipHullAgentState.Data(new ShipHullAgentStateData(new Improbable.Collections.List<ShipHullSchematicData> { }, new EntityId(0)));

                        obj = shData;
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
                        KnowledgeServerState.Data ksData = new KnowledgeServerState.Data(new KnowledgeServerStateData(1,
                                                                                                            new Map<string, int> { },
                                                                                                            1,
                                                                                                            new Map<string, int> { }));
                        obj = ksData;
                    }
                    else if(componentId == 1079)
                    {
                        SchematicsLearnerClientState.Data scData = new SchematicsLearnerClientState.Data(new SchematicsLearnerClientStateData(new Improbable.Collections.List<string> { },
                                                                                                                                    new Improbable.Collections.List<string> { },
                                                                                                                                    10,
                                                                                                                                    20,
                                                                                                                                    10,
                                                                                                                                    10));
                        obj = scData;
                    }
                    else if(componentId == 190002)
                    {
                        Activated.Data aData = new Activated.Data(new ActivatedData(true, true, 0));

                        obj = aData;
                    }
                    else if(componentId == 190000)
                    {
                        EntityLoadingControl.Data elData = new EntityLoadingControl.Data(new EntityLoadingControlData(EntityLoadingControlData.EntityLoadingStates.Idle,
                                                                                                            0,
                                                                                                            5,
                                                                                                            100,
                                                                                                            false,
                                                                                                            new Improbable.Collections.List<EntityId> { }));
                        obj = elData;
                    }
                    else if(componentId == 1150)
                    {
                        PlayerActivationState.Data pcData = new PlayerActivationState.Data(new PlayerActivationStateData(true, 12345, 123));

                        obj = pcData;
                    }
                    else if(componentId == 1219)
                    {
                        ShipyardVisitorState.Data svData = new ShipyardVisitorState.Data(new ShipyardVisitorStateData(new EntityId(0), "abcdefg"));

                        obj = svData;
                    }
                    else if(componentId == 1003)
                    {
                        PlayerCraftingInteractionState.Data pcisData = new PlayerCraftingInteractionState.Data(new EntityId(0), true);
                        
                        obj = pcisData;
                    }
                    else if(componentId == 1005)
                    {
                        CraftingStationClientState.Data csData = new CraftingStationClientState.Data(new CraftingStationClientStateData("schematicId",
                                                                                                                                "owner",
                                                                                                                                new Improbable.Collections.List<SlottedMaterial> { },
                                                                                                                                new Improbable.Collections.List<Cipher> { },
                                                                                                                                12,
                                                                                                                                30f,
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
                        ClientAuthoritativePlayerState.Data capData = new ClientAuthoritativePlayerState.Data(new ClientAuthoritativePlayerStateData(new Improbable.Math.Vector3f(0f, 0f, 0f),
                                                                                                                                            new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0), // w-first
                                                                                                                                            EntityId.InvalidEntityId,
                                                                                                                                            0f,
                                                                                                                                            100,
                                                                                                                                            new byte[] { },
                                                                                                                                            false,
                                                                                                                                            2,
                                                                                                                                            false,
                                                                                                                                            false,
                                                                                                                                            100));
                        obj= capData;
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
                    else if(componentId == 1269)
                    {
                        RadialStormState.Data rsData = new RadialStormState.Data(new RadialStormStateData(0f));

                        obj = rsData;
                    }
                    else if(componentId == 1139)
                    {
                        WeatherCellState.Data wcData = new WeatherCellState.Data(new WeatherCellStateData(1f, new Vector3f(0f, 0f, 0f)));

                        obj = wcData;
                    }
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
                        CustomShipHullState.Data hullData =
                            new CustomShipHullState.Data(Multiplayer.ShipHull.MinimumHullData());

                        Console.WriteLine("[info] seeding 1209 for entity " + entityId + " ("
                            + WorldsAdriftRebornGameServer.WorldEntities.Describe(entityId)
                            + ") with the " + Multiplayer.ShipHull.MinimumHullDataLength
                            + "-byte minimum hull.");

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
                        Bossa.Travellers.Items.ItemHealthState.Data itemHealthData =
                            new Bossa.Travellers.Items.ItemHealthState.Data(
                                new Bossa.Travellers.Items.ItemHealthStateData(
                                    Multiplayer.Trees.ItemHealth,
                                    Multiplayer.Trees.ItemHealth,
                                    Bossa.Travellers.Items.VulnerabilityState.Vulnerable,
                                    false));
                        obj = itemHealthData;
                    }
                    else if (componentId == 1099)
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
                        bool isShipHull =
                            WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key
                                == Multiplayer.WorldEntities.ShipFrameKey;

                        Bossa.Travellers.Salvaging.SalvageAndRepairState.Data salvageData =
                            new Bossa.Travellers.Salvaging.SalvageAndRepairState.Data(
                                new Bossa.Travellers.Salvaging.SalvageAndRepairStateData(
                                    isShipHull ? "" : Multiplayer.Trees.WoodType,
                                    0f,             // salvageDamagePerPeriod - no client reader
                                    0f,             // repairAmountPerPeriod  - no client reader
                                    isShipHull ? 1f : 0f,   // repairToSalvageRatio - no client reader
                                    1f,             // period                 - no client reader
                                    false,          // isRepairable: keeps IsDamaged() false
                                    !isShipHull,    // isSalvageable
                                    "",             // isSalvageableStatus
                                    new Improbable.Collections.List<SlottedMaterial> { },
                                    false,          // destroyOnSalvageComplete
                                    0f,             // salvageRatio           - no client reader
                                    new Option<float> { }));

                        obj = salvageData;
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
                        Console.WriteLine("[ToDo] unhandled component id needs investigation: " + componentId);
                    }

                    if (obj != null)
                    {
                        refId = ClientObjects.Instance.CreateReference(obj);
                        ComponentProtocol.ClientObject wrapper = new ComponentProtocol.ClientObject();
                        wrapper.Reference = refId;

                        ComponentProtocol.ClientSerialize serialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientSerialize>(ComponentsManager.Instance.ClientComponentVtables[i].Serialize);
                        serialize(componentId, 2, &wrapper, buffer, length);

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
            }
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
