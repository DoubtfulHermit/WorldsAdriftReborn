namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The player-identity string the client exposes as <c>LocalPlayer.PlayerId</c>.
    ///
    /// The client reads it from component 1086 <c>PlayerName</c>, field
    /// <c>field2_player_id</c> (<c>PlayerNameReader.PlayerId =&gt; Data.playerId =&gt;
    /// Field2PlayerId</c>, gencode-verified). Our server currently serves that field as
    /// a fixed stub for every player (the identity system is a documented stub - one id
    /// for all peers), so <c>LocalPlayer.PlayerId</c> is this exact constant on every
    /// client.
    ///
    /// WHY IT LIVES HERE AND IS SHARED. Anything the server writes that the client
    /// compares against <c>LocalPlayer.PlayerId</c> MUST use this same string, or the
    /// compare fails silently. The concrete case: the ship-hull editor's SAVE/RESET
    /// buttons are gated on
    /// <c>ShipHullEditorVisualizer.GetOwnerId() == LocalPlayer.PlayerId</c>
    /// (ShipCraftingUIHelper.cs:309/313), where <c>GetOwnerId()</c> is 1206
    /// <c>Field10OwnerPlayerId</c>. Seeding 1206's owner from the placed-shipyard ledger
    /// (a different uid) left SAVE greyed; it must be THIS value. The 1086 serve and the
    /// 1206 owner now read the same constant so they cannot drift.
    ///
    /// When the identity system stops being a stub (real per-player ids), this becomes a
    /// per-player lookup and both call sites follow it.
    /// </summary>
    public static class LocalPlayerIdentity
    {
        /// <summary>
        /// The <c>field2_player_id</c> served in 1086 PlayerName, i.e. the client's
        /// <c>LocalPlayer.PlayerId</c>. Kept byte-identical to the 1086 serve stub.
        /// </summary>
        public const string PlayerId = "id";
    }
}
