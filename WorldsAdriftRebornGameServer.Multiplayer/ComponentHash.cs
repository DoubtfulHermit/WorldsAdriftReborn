namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The hash that keys component-update handlers, factored out of
    /// ComponentUpdateManager so it is a pure function with a unit test.
    ///
    /// It hashes a type's FullName with 64-bit FNV (offset basis
    /// 14695981039346656037, prime 1099511628211, xor-then-multiply - i.e. the
    /// FNV-1a variant, whatever the original comment called it). BOTH sides of
    /// the handler table go through this same function: registration hashes the
    /// handler's TBase type, per-packet dispatch hashes the component factory's
    /// metaclass type, and they meet in the dictionary only because the two
    /// FullNames are equal. That makes the exact algorithm load-bearing: change
    /// one bit and every handler silently stops matching, with no error anywhere
    /// - which is why it now has pinned test vectors.
    /// </summary>
    public static class ComponentHash
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        /// <summary>Hash of a type's FullName. Pure; same input, same output, forever.</summary>
        public static ulong OfTypeFullName(string typeFullName)
        {
            ulong hash = OffsetBasis;
            for (int i = 0; i < typeFullName.Length; i++)
            {
                hash ^= typeFullName[i];
                hash *= Prime;
            }
            return hash;
        }
    }
}
