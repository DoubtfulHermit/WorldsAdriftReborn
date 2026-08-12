using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;

namespace WorldsAdriftRebornGameServer.Game.Knowledge
{
    /// <summary>
    /// One player's live KNOWLEDGE state, held in memory and keyed by the player's
    /// entity id - the same shape as <see cref="Inventory.InventoryService"/> and
    /// <see cref="Crafting.CraftSessions"/>. It is the single source of truth the 1332
    /// and 1079 serve branches read (so a re-checkout re-serves the CURRENT totals,
    /// not the seed) and the two handlers write.
    ///
    /// Seeded lazily on first touch from the same values the old static 1332/1079
    /// seeds used, so an untouched player is byte-identical to before. In-session
    /// only; persistence across logins is a separate track (see
    /// docs/research/gathering/findings-progression.md "PERSISTENCE SCHEMA").
    /// </summary>
    public sealed class PlayerProgression
    {
        /// <summary>The value the old static 1332 seed used for spendable knowledge.</summary>
        public const int SeedKnowledge = 1;

        /// <summary>The value the old static 1332 seed used for the lifetime tally.</summary>
        public const int SeedLifetimeKnowledge = 1;

        public int Knowledge { get; set; } = SeedKnowledge;
        public int LifetimeKnowledge { get; set; } = SeedLifetimeKnowledge;

        /// <summary>Node id -> times purchased (1332 knowledgeNodeUses).</summary>
        public Dictionary<string, int> NodeUses { get; } = new Dictionary<string, int>();

        /// <summary>Schematic ids learned by spending knowledge (1079 learnedSchematics).</summary>
        public List<string> LearnedSchematics { get; } = new List<string>();

        /// <summary>Entity ids already scanned, for the 1331 dedup ledger.</summary>
        public HashSet<string> AlreadyScanned { get; } = new HashSet<string>();

        public int UsesOf(string nodeId) => NodeUses.TryGetValue(nodeId, out int u) ? u : 0;

        /// <summary>
        /// A pure snapshot of this state for persistence. A copy, not a view: the
        /// live collections keep mutating after a save is serialised.
        /// </summary>
        public ProgressionState ToState()
        {
            return new ProgressionState
            {
                Knowledge = Knowledge,
                LifetimeKnowledge = LifetimeKnowledge,
                NodeUses = new Dictionary<string, int>(NodeUses),
                LearnedSchematics = new List<string>(LearnedSchematics),
                AlreadyScanned = new List<string>(AlreadyScanned),
            };
        }

        /// <summary>
        /// Overwrites this state with a stored snapshot on load. Only called when
        /// <see cref="ProgressionLoadPolicy"/> has already ruled the replacement
        /// safe, so it is unconditional here.
        /// </summary>
        public void ApplyState(ProgressionState state)
        {
            Knowledge = state.Knowledge;
            LifetimeKnowledge = state.LifetimeKnowledge;

            NodeUses.Clear();
            if (state.NodeUses != null)
            {
                foreach (KeyValuePair<string, int> use in state.NodeUses)
                {
                    NodeUses[use.Key] = use.Value;
                }
            }

            LearnedSchematics.Clear();
            if (state.LearnedSchematics != null)
            {
                LearnedSchematics.AddRange(state.LearnedSchematics);
            }

            AlreadyScanned.Clear();
            if (state.AlreadyScanned != null)
            {
                foreach (string scanned in state.AlreadyScanned)
                {
                    AlreadyScanned.Add(scanned);
                }
            }
        }

        /// <summary>Whether anything here differs from a fresh seed. See ProgressionState.</summary>
        public bool HasProgress => ToState().HasProgress;
    }

    /// <summary>Process-global registry of per-player progression, keyed by entity id.</summary>
    public static class ProgressionStore
    {
        private static readonly Dictionary<long, PlayerProgression> ByEntity =
            new Dictionary<long, PlayerProgression>();

        /// <summary>The player's progression, created (seeded) on first touch.</summary>
        public static PlayerProgression For(long entityId)
        {
            if (!ByEntity.TryGetValue(entityId, out PlayerProgression? p))
            {
                p = new PlayerProgression();
                ByEntity[entityId] = p;
            }
            return p;
        }

        /// <summary>Drop a player's state when their entity leaves (avoids leaking ids).</summary>
        public static void Forget(long entityId) => ByEntity.Remove(entityId);
    }
}
