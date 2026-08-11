using System;
using System.Collections.Generic;
using Improbable;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The server-side configuration and cached data of the <c>190000/190001/190002</c>
    /// loading barrier - the glue that turns <see cref="LoadBarrierPolicy"/> (pure,
    /// env-and-partition) into concrete Improbable <see cref="EntityId"/>s the
    /// component serializer can name.
    ///
    /// It exists as a small static holder rather than being folded into the giant
    /// server class because two very different call sites need the same three
    /// answers - is the barrier on, how long is the timeout, and what is the
    /// initial entity-id list - and one is <c>ComponentsSerializer</c> (which
    /// already reaches the world registry through the server class) while the
    /// other is the server's setup path. Keeping the answers here keeps both honest.
    ///
    /// <see cref="Prime"/> must be called once at boot, AFTER the world registry is
    /// fully populated (including any persistence-restored entities) and BEFORE the
    /// first client connects. It binds every world entity id up front so the initial
    /// set can be named in <c>190000</c> even though those entities' AddEntity steps
    /// have not run yet.
    /// </summary>
    internal static class LoadBarrier
    {
        /// <summary>
        /// Whether the barrier is armed. Read once from the environment: a value
        /// that changed mid-run would desync the seed (190002 false) from the
        /// release path, so it is fixed for the process lifetime.
        /// </summary>
        public static bool Enabled { get; } =
            LoadBarrierPolicy.IsEnabled(Environment.GetEnvironmentVariable(LoadBarrierPolicy.EnableEnvVar));

        /// <summary>How long a peer may hold the loading screen before the server activates it anyway.</summary>
        public static TimeSpan Timeout { get; } =
            LoadBarrierPolicy.TimeoutFrom(Environment.GetEnvironmentVariable(LoadBarrierPolicy.TimeoutEnvVar));

        private static readonly List<EntityId> InitialIds = new List<EntityId>();
        private static IReadOnlyList<string> _initialKeys = Array.Empty<string>();
        private static IReadOnlyList<string> _distantKeys = Array.Empty<string>();

        /// <summary>The registration keys in the initial (screen-gating) set, for the boot log.</summary>
        public static IReadOnlyList<string> InitialKeys => _initialKeys;

        /// <summary>The registration keys that stream in after the screen lifts, for the boot log.</summary>
        public static IReadOnlyList<string> DistantKeys => _distantKeys;

        /// <summary>
        /// Binds every world entity id and caches the initial set. Idempotent-ish:
        /// calling it again re-snapshots against the current registry. Safe to call
        /// when the barrier is off (it only reads the registry), but the server only
        /// calls it when <see cref="Enabled"/>.
        /// </summary>
        public static void Prime(WorldEntityRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            InitialIds.Clear();
            List<string> initialKeys = new List<string>();
            List<string> distantKeys = new List<string>();

            foreach (WorldEntity entity in registry.Registrations)
            {
                // Allocate/bind the shared id now so it can be named before its
                // AddEntity runs. Ids are process-constant once handed out.
                long id = registry.EntityIdFor(entity);

                if (LoadBarrierPolicy.IsInitialKey(entity.Key))
                {
                    InitialIds.Add(new EntityId(id));
                    initialKeys.Add(entity.Key);
                }
                else
                {
                    distantKeys.Add(entity.Key);
                }
            }

            _initialKeys = initialKeys;
            _distantKeys = distantKeys;
        }

        /// <summary>
        /// A FRESH copy of the initial entity-id list for one <c>190000</c> seed.
        /// A copy because the client-object serializer takes ownership of what it is
        /// handed, and one shared mutable list would be aliased across every peer.
        /// </summary>
        public static Improbable.Collections.List<EntityId> InitialEntityIds()
        {
            Improbable.Collections.List<EntityId> copy =
                new Improbable.Collections.List<EntityId>(InitialIds.Count);
            for (int i = 0; i < InitialIds.Count; i++)
            {
                copy.Add(InitialIds[i]);
            }
            return copy;
        }
    }
}
