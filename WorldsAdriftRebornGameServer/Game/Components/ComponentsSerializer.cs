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
using Bossa.Travellers.Player;
using Bossa.Travellers.Refdata;
using Bossa.Travellers.Rope;
using Bossa.Travellers.Scanning;
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
            for(int i = 0; i < ComponentsManager.Instance.ClientComponentVtables.Length; i++)
            {
                if (ComponentsManager.Instance.ClientComponentVtables[i].ComponentId == componentId)
                {
                    ulong refId = 0;
                    object obj = null;

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
                        Multiplayer.FixedPointPosition seed = Multiplayer.SpawnPolicy.TransformSeedFor(
                            entityId, WorldsAdriftRebornGameServer.IslandEntityId);

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
                            + Multiplayer.SpawnPolicy.KindOf(entityId, WorldsAdriftRebornGameServer.IslandEntityId)
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
                        InventoryState.Data iData = new InventoryState.Data(new InventoryStateData(100,
                                                                                        "{}",
                                                                                        ItemHelper.GetDefaultItems(),
                                                                                        ItemHelper.GetStashItems(true, true),
                                                                                        10,
                                                                                        18,
                                                                                        new Improbable.Collections.List<string> { },
                                                                                        true,
                                                                                        3));
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
                        // string literal at all three sites; it is one constant now.
                        //
                        // The Coordinates(0,0,0) here is IslandState.teleportTarget,
                        // NOT a world position - the island is positioned by 190602
                        // above (IslandLocalTransformBase.cs:44). teleportTarget has
                        // zero client consumers, so its meaning is ours to define.
                        IslandState.Data data = new IslandState.Data(new IslandStateData(Multiplayer.SpawnPolicy.IslandAssetName,
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
                    // NOTE: a second `componentId == 1109` branch used to sit here, seeding
                    // PilotState with EntityId(10) instead of EntityId(0). It was unreachable -
                    // the branch at the top of this same else-if chain always won - which was
                    // lucky, because EntityId(10) is a VALID entity id. IsDriving() is
                    // EntityId.IsValidEntityId(DrivingEntityId), so had the chain ever been
                    // reordered the player would have been permanently "driving" and unable to
                    // move. Removed rather than left as a trap.
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
        }
    }
}
