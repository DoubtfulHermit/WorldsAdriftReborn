using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// Turns a <see cref="ProgressionState"/> into the opaque JSON that goes in a
    /// database column, and back. The exact analogue of InventorySnapshot.
    ///
    /// Round-tripping is total in one direction only, on purpose. Writing always
    /// produces valid JSON. Reading a payload that is corrupt, truncated or from
    /// a future version returns null rather than throwing, because the caller's
    /// correct response is "log it and keep the live state", not "refuse to let
    /// this player into the world".
    /// </summary>
    public static class ProgressionSnapshot
    {
        /// <summary>
        /// Stamped into every payload. Bump it when the progression shape changes
        /// in a way an older reader would misread; <see cref="Read"/> refuses a
        /// version it does not know rather than silently mis-parsing.
        /// </summary>
        public const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        /// <summary>The JSON payload for a progression state. Never null, never throws.</summary>
        public static string Write(ProgressionState state)
        {
            Payload payload = new Payload
            {
                Version = CurrentVersion,
                Knowledge = state.Knowledge,
                LifetimeKnowledge = state.LifetimeKnowledge,
                NodeUses = state.NodeUses == null
                    ? new Dictionary<string, int>()
                    : new Dictionary<string, int>(state.NodeUses),
                LearnedSchematics = state.LearnedSchematics == null
                    ? new List<string>()
                    : new List<string>(state.LearnedSchematics),
                AlreadyScanned = state.AlreadyScanned == null
                    ? new List<string>()
                    : new List<string>(state.AlreadyScanned),
            };

            return JsonSerializer.Serialize(payload, options);
        }

        /// <summary>
        /// The progression a payload describes, or null when it is unreadable.
        /// </summary>
        public static ProgressionState? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            Payload? payload;

            try
            {
                payload = JsonSerializer.Deserialize<Payload>(json!, options);
            }
            catch (JsonException)
            {
                return null;
            }

            if (payload == null || payload.Version != CurrentVersion)
            {
                return null;
            }

            return new ProgressionState
            {
                Knowledge = payload.Knowledge,
                LifetimeKnowledge = payload.LifetimeKnowledge,
                NodeUses = payload.NodeUses ?? new Dictionary<string, int>(),
                LearnedSchematics = payload.LearnedSchematics ?? new List<string>(),
                AlreadyScanned = payload.AlreadyScanned ?? new List<string>(),
            };
        }

        private sealed class Payload
        {
            public int Version { get; set; }

            public int Knowledge { get; set; }

            public int LifetimeKnowledge { get; set; }

            public Dictionary<string, int>? NodeUses { get; set; }

            public List<string>? LearnedSchematics { get; set; }

            public List<string>? AlreadyScanned { get; set; }
        }
    }
}
