using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// A pure, transport-agnostic snapshot of one player's KNOWLEDGE state - the
    /// exact set of values that has to survive a relog: spendable knowledge, the
    /// lifetime tally, the tree nodes purchased, the schematics learned and the
    /// databanks already scanned.
    ///
    /// It lives in this project, next to the pure knowledge policies, so the
    /// round-trip (state to JSON and back) and the anti-wipe decision can be unit
    /// tested with no database, no entity id and no socket. The game server's
    /// live PlayerProgression converts to and from this; nothing here knows about
    /// components, peers or Postgres.
    /// </summary>
    public sealed class ProgressionState
    {
        /// <summary>The seed a never-touched player starts with (old static 1332 seed).</summary>
        public const int SeedKnowledge = 1;

        /// <summary>The seed lifetime tally a never-touched player starts with.</summary>
        public const int SeedLifetimeKnowledge = 1;

        public int Knowledge { get; set; } = SeedKnowledge;

        public int LifetimeKnowledge { get; set; } = SeedLifetimeKnowledge;

        /// <summary>Node id -> times purchased.</summary>
        public Dictionary<string, int> NodeUses { get; set; } = new Dictionary<string, int>();

        /// <summary>Schematic ids learned by spending knowledge.</summary>
        public List<string> LearnedSchematics { get; set; } = new List<string>();

        /// <summary>Entity ids already scanned, for the scan dedup ledger.</summary>
        public List<string> AlreadyScanned { get; set; } = new List<string>();

        /// <summary>
        /// Whether this state carries anything a fresh seed does not. It is the
        /// input to <see cref="ProgressionLoadPolicy"/>: a stored record that has
        /// no progress must never overwrite a live one that does, exactly as an
        /// empty stored inventory must never wipe a full session.
        /// </summary>
        public bool HasProgress =>
            Knowledge != SeedKnowledge
            || LifetimeKnowledge != SeedLifetimeKnowledge
            || (NodeUses != null && NodeUses.Count > 0)
            || (LearnedSchematics != null && LearnedSchematics.Count > 0)
            || (AlreadyScanned != null && AlreadyScanned.Count > 0);
    }
}
