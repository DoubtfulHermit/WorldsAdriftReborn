namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// The client's own weather-wall taxonomy, PROVED off the decompiled client at
    /// <c>Assets.Scripts.UI.WorldEditor/WorldEditorWallData.cs:11-19</c> and matched
    /// by the release MapFile's <c>Type</c> column and by this repo's operator-map
    /// legend (<c>WorldsAdriftServer/Admin/MapWallPalette.cs:38-45</c>).
    ///
    /// The numeric values ARE the wire values: <c>1204 WallSegmentState.wallType</c>
    /// is an int the client casts straight to this enum
    /// (<c>WallSegmentVisualizer.Type</c>), so renumbering these would silently turn
    /// every sand storm into a typhon.
    ///
    /// WHAT EACH ONE COSTS A CLIENT, since this is the only place the whole list is
    /// written down (findings-storm-walls.md section 3, findings-storm-sky.md section 1.5):
    ///
    /// <list type="bullet">
    /// <item><see cref="WindRift"/> - a see-through "waterfall of air" curtain on a
    /// separate translucent shader path. No storm renderer, no debris, no rain (it
    /// actively SUPPRESSES rain), no ambient lightning. The cheapest wall there is,
    /// and 20 of the 44.</item>
    /// <item><see cref="StormRift"/> - the expensive one, and the one worth looking
    /// at. Inside ~367 m the volumetric cloud renderer is SWAPPED OUT for the opaque
    /// storm renderer, two debris emitters switch on, and every registered rift adds
    /// its length to the world-wide ambient-bolt spawn rate. 11 of the 44.</item>
    /// <item><see cref="Typhon"/> - zero segments in the release map, no wiki
    /// description, no gust direction (<c>Vector3.zero</c>). Present for
    /// completeness only.</item>
    /// <item><see cref="SandStorm"/> - the sand renderer plus debris; no ambient
    /// lightning (<c>LightningVisualInstancesManager</c> draws only from storm
    /// rifts). 12 of the 44.</item>
    /// <item><see cref="IceStorm"/> - zero segments in the release map.</item>
    /// <item><see cref="WorldEndWall"/> - the map-edge curtain. Its gusts are a
    /// shipped no-op (<c>GetGustForceUnit</c> returns <c>Vector3.zero</c>). 1 of the
    /// 44, and it is 36 km long.</item>
    /// </list>
    /// </summary>
    public enum WallType
    {
        WindRift = 0,
        StormRift = 1,
        Typhon = 2,
        SandStorm = 3,
        IceStorm = 4,
        WorldEndWall = 5,
    }
}
