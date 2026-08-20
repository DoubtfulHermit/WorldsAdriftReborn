namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHICH RESOURCES ONE UNDERSTORM IS ALLOWED TO RESTORE.
    ///
    /// ⚠ THIS FILE EXISTS BECAUSE A MUTATION ESCAPED. The per-island reset was first
    /// written with the scope decided inline in the game server:
    ///
    /// <code>
    /// Func&lt;long, bool&gt;? include = island == null ? null : id => Owner(id) == island;
    /// </code>
    ///
    /// The game-server assembly has no test project (it needs a Windows game install
    /// to compile against), so that line was covered only by a source-reading test
    /// looking for <c>ResetAll(include)</c> at the call sites. Replacing the whole
    /// declaration with <c>Func&lt;long, bool&gt;? include = null;</c> left every one
    /// of those strings intact, reinstated the world-wide reset - the exact S1 defect
    /// that landed 3 m 32 s late on production - and the suite passed 4215/0.
    ///
    /// So the decision lives HERE, in the assembly that can be unit-tested, and the
    /// game server's only remaining freedom is whether it passes the island. That is
    /// a much smaller thing to guard, and a source-reading test guards it.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class IslandResourceScope
    {
        /// <summary>
        /// The predicate the four harvest ledgers' scoped <c>ResetAll</c> overloads
        /// take, or null for "every resource in the world".
        ///
        /// <paramref name="island"/> null is the authenticated operator's
        /// <c>reset-resources all</c> and means the whole world. Anything else is one
        /// island's understorm.
        ///
        /// <paramref name="ownerOf"/> answers which island a resource entity belongs
        /// to, or null if it cannot say. AN UNCLASSIFIABLE RESOURCE IS EXCLUDED, and
        /// that is the deliberate direction: including it would let a storm over
        /// Haven restore a node on the far side of the world, which is a
        /// silently-wrong world state, whereas excluding it leaves a node mined that
        /// the next storm will get. Wrong in the direction a player can see and
        /// report, rather than wrong in the direction nobody notices.
        /// </summary>
        public static Func<long, bool>? Include(IslandId? island, Func<long, IslandId?> ownerOf)
        {
            if (island == null) return null;
            if (ownerOf == null) throw new ArgumentNullException(nameof(ownerOf));

            IslandId target = island.Value;
            return entityId => ownerOf(entityId) is IslandId owner && owner == target;
        }
    }
}
