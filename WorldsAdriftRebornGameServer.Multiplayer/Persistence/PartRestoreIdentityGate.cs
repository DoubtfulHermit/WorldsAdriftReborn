using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Persistence
{
    /// <summary>
    /// Prevents a corrupt snapshot from materialising the same stable PartUid twice
    /// (mounted+loose or duplicate rows). Empty legacy identities remain accepted:
    /// rejecting every empty record would delete old paid-for parts.
    /// </summary>
    public sealed class PartRestoreIdentityGate
    {
        private readonly HashSet<string> _seen = new HashSet<string>(System.StringComparer.Ordinal);

        public bool TryAccept(string? partUid) =>
            string.IsNullOrEmpty(partUid) || _seen.Add(partUid);

        public int StableIdentityCount => _seen.Count;
    }
}
