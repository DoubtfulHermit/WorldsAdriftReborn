namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE WEATHER WALLS ON THE WIRE. The impure half of
    /// <see cref="Multiplayer.Walls.WallPolicy"/>, <see cref="Multiplayer.Walls.WallCatalog"/>
    /// and <see cref="Multiplayer.Walls.WorldWalls"/>: it answers the one question
    /// <c>ComponentsSerializer</c> asks - "this entity id, is it a wall, and if so
    /// which one" - and nothing else.
    ///
    /// IT SENDS NOTHING AND SCHEDULES NOTHING, and that is the whole shape of this
    /// feature. A wall is static geometry: the client is told once, at checkout, and
    /// from four numbers it produces the opaque billowing cloud, the rain, the storm
    /// debris, the audio mix and the ambient lightning entirely on its own
    /// (<c>WeatherWalls.Register</c> on <c>OnEnable</c>, then
    /// <c>WeatherTextureGenerator</c>, <c>WeatherEffectHandler</c> and
    /// <c>LightningVisualInstancesManager</c> with zero further server involvement).
    /// There is no push loop here because there is nothing to push.
    ///
    /// ⚠ THIS FILE MUST NEVER SERVE <c>1229 GlobalWallDataState</c>. It is the wall
    /// system's OTHER component and it looks tempting because it is the one that
    /// would make walls push a ship. It carries only wind/gust/torque scalars as a
    /// <c>Map&lt;string,float&gt;</c>; retail's 50 values are unrecoverable
    /// (findings-storm-walls.md section 5.1, negative results with controls); the
    /// client <c>Debug.LogError</c>s once per missing key; and a missing TORQUE key
    /// makes the client SILENTLY skip that wall type's whole table. Half of it is
    /// worse than none of it. It would also buy nothing today: the behaviours that
    /// read it are in <c>ShipPreprocessor</c>'s <c>UnityWorker</c> branch and are not
    /// on our hulls at all. <c>WallSegmentWiringTests</c> reads this source off disk
    /// and goes red if "1229" ever appears in it.
    /// </summary>
    internal static class WallSegmentWire
    {
        /// <summary>1204 <c>WallSegmentState</c>, namespace Bossa.Travellers.Weather.</summary>
        internal const uint WallSegmentStateComponentId =
            Multiplayer.Walls.WallPolicy.WallSegmentStateComponentId;

        /// <summary>
        /// The wall behind an entity id, or null if that entity is not one of ours.
        ///
        /// Null is the correct and complete answer for every other entity in the
        /// world and for every entity when <c>WAREBORN_WALLS</c> is unset - with the
        /// feature off no wall is registered, so this is a lookup miss and the
        /// serializer's 1204 branch produces no object, exactly as it did before the
        /// branch existed.
        ///
        /// Resolution goes id -> registration -> key -> wall id, never id -> wall id
        /// directly, because entity ids are allocated at boot in registration order
        /// and are not a stable function of anything. The key is.
        /// </summary>
        internal static Multiplayer.Walls.WallSegmentSeed? SeedFor(long entityId)
        {
            Multiplayer.WorldEntity? entity =
                WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId);
            if (entity == null)
            {
                return null;
            }

            int? wallId = Multiplayer.Walls.WallPolicy.WallIdFor(entity.Key);
            return wallId.HasValue ? Multiplayer.Walls.WallCatalog.ById(wallId.Value) : null;
        }

        /// <summary>
        /// The <c>8065 Blueprint</c> string for an entity id. "Player" for everything
        /// that is not a wall, which is what every entity in this world has always
        /// been sent; "WallSegment" for a wall.
        ///
        /// The decision itself is <see cref="Multiplayer.Walls.WallPolicy.BlueprintNameFor"/>,
        /// in the assembly with a test project, because widening a hard-coded literal
        /// that every entity reads is precisely the edit that quietly changes what
        /// they all receive.
        /// </summary>
        internal static string BlueprintNameFor(long entityId)
        {
            Multiplayer.WorldEntity? entity =
                WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId);
            return Multiplayer.Walls.WallPolicy.BlueprintNameFor(entity?.Key);
        }
    }
}
