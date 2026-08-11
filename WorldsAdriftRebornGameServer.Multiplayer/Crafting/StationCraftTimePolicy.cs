namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// Pure rule for how long a STATION craft (Assembly Station / Shipyard parts) is held
    /// open before it completes, so the client's timed aperture animation + atomizer VFX
    /// play for a visible duration instead of the craft completing the same frame it starts.
    ///
    /// The client opens the aperture on <c>PlayerStartCrafting</c>, keeps it open while the
    /// served <c>itemReadyInSeconds &gt;= 0</c>, and shoots the atomizer on
    /// <c>CraftingStarted</c>; it closes both on <c>CraftingCompleted</c> (or when
    /// <c>itemReadyInSeconds</c> drops below 0) - CraftingStationBehaviour.cs:150-205,290.
    /// So the server must publish a POSITIVE countdown at start and only complete after it.
    ///
    /// The value is the recipe's real craft time, floored to <see cref="MinCraftingSeconds"/>
    /// so a placeholder <c>TimeToCraft</c> of 0 still holds the aperture open long enough to
    /// read (the same floor <c>ShipBuildTimerService.MinCraftingSeconds</c> applies to a ship
    /// build). Dependency-free, so it unit-tests natively.
    /// </summary>
    public static class StationCraftTimePolicy
    {
        /// <summary>
        /// The minimum seconds a station craft is held open, so a zero/negative recipe time
        /// still animates the aperture visibly and the timer cannot fire instantly.
        /// </summary>
        public const int MinCraftingSeconds = 3;

        /// <summary>
        /// The seconds to hold a station craft open: <paramref name="recipeTimeToCraft"/>
        /// floored at <see cref="MinCraftingSeconds"/>.
        /// </summary>
        public static int Seconds(int recipeTimeToCraft) =>
            recipeTimeToCraft > MinCraftingSeconds ? recipeTimeToCraft : MinCraftingSeconds;
    }
}
