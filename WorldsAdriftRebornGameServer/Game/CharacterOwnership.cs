using System;
using WorldsAdriftRebornGameServer.Game.Inventory;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The one place a WORLD entity's owner is resolved from the player who created it.
    ///
    /// It reads the SAME durable character uid the inventory and knowledge stores key on:
    /// the uid arrives inside the 1088 customisation map (the mod's own
    /// <c>bossaNetCharacterData</c> publish), is parsed by <c>CharacterIdentity</c>, and
    /// is bound to the player's entity by <see cref="InventoryService"/> as an
    /// <c>InventoryKey</c>. So the owner stamped on a placed shipyard, a crafted loose
    /// part or a mounted part is exactly the character uid that player's saved inventory
    /// and progression are filed under - one identity for everything a player owns.
    ///
    /// Returns "" (unowned) rather than guessing when the uid never arrived: a player on a
    /// volatile session key has no durable identity to own anything with, exactly as their
    /// inventory is never written. An empty owner is what the pre-fix code always wrote, so
    /// an un-bound placement is byte-for-byte the old behaviour.
    /// </summary>
    internal static class CharacterOwnership
    {
        /// <summary>
        /// The owner character uid for <paramref name="entityId"/> as the canonical Guid
        /// string ("D" form), or "" when the entity is on a volatile session key (no uid
        /// arrived). This is the string served into 1205 <c>ShipyardState.registeredCharacterUids</c>,
        /// which is the field the client's <c>ShipyardVisualizer.IsLocalPlayerRegistered</c>
        /// checks against <c>LocalPlayer.PlayerId</c> to decide build access.
        /// </summary>
        internal static string UidForEntity(long entityId)
        {
            Guid? uid = InventoryService.KeyOf(entityId)?.CharacterUid;
            return uid.HasValue ? uid.Value.ToString("D") : "";
        }
    }
}
