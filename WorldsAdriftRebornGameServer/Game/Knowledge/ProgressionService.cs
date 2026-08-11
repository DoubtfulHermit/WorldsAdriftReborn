using System.Collections.Generic;
using Bossa.Travellers.Player;
using Bossa.Travellers.Scanning;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Knowledge
{
    /// <summary>
    /// The knowledge counterpart of <see cref="Inventory.InventoryService"/>: it
    /// owns when a player's progression is loaded from and written to Postgres,
    /// keyed by the durable CHARACTER uid rather than the volatile entity id.
    ///
    /// The live per-player state still lives in <see cref="ProgressionStore"/> -
    /// the handlers and the 1332/1079/1331 serve branches read and mutate it
    /// exactly as before - so this type is only the persistence wiring bolted
    /// onto the same entity-to-character seam the inventory already models: the
    /// character uid arrives inside the 1088 customisation map and nowhere else,
    /// so the bind happens there and the save happens on mutation and on
    /// disconnect.
    /// </summary>
    internal static class ProgressionService
    {
        private static readonly ProgressionPersistence Persistence = new ProgressionPersistence();

        /// <summary>
        /// The durable character uid each live entity is bound to. Its own map
        /// rather than reaching into InventoryService, so the disconnect order of
        /// the two services cannot make one lose the key the other still needs.
        /// </summary>
        private static readonly Dictionary<long, Guid> EntityUid = new Dictionary<long, Guid>();

        /// <summary>
        /// Says once, at start-up, whether the knowledge a player earns tonight
        /// will still be there tomorrow. Mirrors InventoryService's line so the
        /// two answers appear together in the boot log.
        /// </summary>
        internal static void ReportPersistenceState()
        {
            if (Persistence.Enabled)
            {
                Console.WriteLine("[info] knowledge persistence is ON (Postgres).");
            }
            else
            {
                Console.WriteLine("[warning] knowledge persistence is OFF (" + Persistence.DisabledReason
                    + "). Knowledge will work for the length of a session and then be lost.");
            }
        }

        /// <summary>
        /// Binds an entity to its character identity and loads whatever knowledge
        /// the database holds for that character, pushing the restored 1332/1079
        /// so the client re-reads it. Called from the 1088 handler, the only
        /// place the uid appears. Safe to call repeatedly: the load happens once,
        /// the first time a character key is seen for this entity, so a second
        /// 1088 never overwrites knowledge earned since the first.
        /// </summary>
        internal static void BindIdentity(
            long entityId,
            IReadOnlyDictionary<string, string> customisation,
            ENetPeerHandle player)
        {
            Guid? uid = CharacterIdentity.UidFrom(customisation);

            if (!uid.HasValue)
            {
                // The uid did not arrive. InventoryService already logged the loud
                // warning for this entity; the progression simply stays on the
                // seed and is never saved, exactly like the session-key inventory.
                return;
            }

            if (EntityUid.TryGetValue(entityId, out Guid bound) && bound.Equals(uid.Value))
            {
                return;
            }

            EntityUid[entityId] = uid.Value;

            ProgressionState? stored = Persistence.Load(uid.Value);
            PlayerProgression live = ProgressionStore.For(entityId);

            if (ProgressionLoadPolicy.ShouldApplyStored(live.HasProgress, stored))
            {
                live.ApplyState(stored!);

                Console.WriteLine("[info] restored progression for character:" + uid.Value.ToString("D")
                    + " (entity " + entityId + "): knowledge " + live.Knowledge + ", "
                    + live.LearnedSchematics.Count + " schematic(s), " + live.NodeUses.Count + " node(s), "
                    + live.AlreadyScanned.Count + " scan(s).");

                PushProgression(player, entityId, live);
            }
            else if (stored == null)
            {
                Console.WriteLine("[info] no stored progression for character:" + uid.Value.ToString("D")
                    + " (entity " + entityId + "); keeping this session's knowledge.");
            }
            else
            {
                Console.WriteLine("[warning] stored progression for character:" + uid.Value.ToString("D")
                    + " (entity " + entityId + ") is seed-only but this session holds progress; "
                    + "keeping the session's knowledge rather than resetting it.");
            }
        }

        /// <summary>
        /// Writes an entity's progression if it is on a durable character key.
        /// Called after every mutation (a scan grant, a node purchase) so there
        /// is no separate "remember to save" step, exactly like the inventory
        /// push seam.
        /// </summary>
        internal static void Save(long entityId)
        {
            if (!EntityUid.TryGetValue(entityId, out Guid uid))
            {
                return;
            }

            Persistence.Save(uid, ProgressionStore.For(entityId).ToState());
        }

        /// <summary>
        /// Saves and then drops an entity's progression when its player leaves.
        /// The save comes first and is unconditional: a mutation between the last
        /// write-through and the disconnect would otherwise be lost.
        /// </summary>
        internal static void Forget(long entityId)
        {
            Save(entityId);
            EntityUid.Remove(entityId);
            ProgressionStore.Forget(entityId);
        }

        /// <summary>
        /// Pushes the restored knowledge (1332) and learned schematics (1079) to
        /// the player's own peer, so a relog's stored totals replace the seed the
        /// checkout served before identity arrived. The dedup ledger (1331) is
        /// server-owned and does not need a push - restoring AlreadyScanned in
        /// memory is what makes a re-scan pay nothing.
        /// </summary>
        private static void PushProgression(ENetPeerHandle player, long entityId, PlayerProgression prog)
        {
            KnowledgeServerState.Update knowledge = new KnowledgeServerState.Update();
            knowledge.SetKnowledge(prog.Knowledge);
            knowledge.SetLifetimeKnowledge(prog.LifetimeKnowledge);
            knowledge.SetKnowledgeNodeUses(ToMap(prog.NodeUses));

            SchematicsLearnerClientState.Update schematics = new SchematicsLearnerClientState.Update();
            schematics.SetLearnedSchematics(ToList(prog.LearnedSchematics));

            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1332, 1079 },
                new List<object> { knowledge, schematics });
        }

        private static Improbable.Collections.Map<string, int> ToMap(IReadOnlyDictionary<string, int> source)
        {
            Improbable.Collections.Map<string, int> map = new Improbable.Collections.Map<string, int>();
            foreach (KeyValuePair<string, int> kv in source)
            {
                map.Add(kv.Key, kv.Value);
            }
            return map;
        }

        private static Improbable.Collections.List<string> ToList(IReadOnlyList<string> source)
        {
            Improbable.Collections.List<string> list = new Improbable.Collections.List<string>();
            foreach (string s in source)
            {
                list.Add(s);
            }
            return list;
        }
    }
}
