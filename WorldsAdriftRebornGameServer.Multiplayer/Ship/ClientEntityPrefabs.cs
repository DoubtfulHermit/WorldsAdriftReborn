using System.Reflection;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The REAL client entity-prefab census, available AT RUNTIME - the set of
    /// prefab base names the unmodified client can actually resolve when the
    /// server names one in an AddEntityOp.
    ///
    /// Extracted from the client assets (the "entityprefabs/&lt;name&gt;_unityclient"
    /// strings in resources.assets/globalgamemanagers; every name was re-verified
    /// against the ResourceManager container map, so each one is genuinely
    /// addressable via Resources.Load("EntityPrefabs/&lt;name&gt;_unityclient")).
    /// One lower-case base name per line in the embedded
    /// Ship/client-entity-prefabs.txt - the SAME census file LoosePartTests embeds
    /// and pins the catalogue against; ClientEntityPrefabsTests asserts the two
    /// copies are identical so they cannot drift.
    ///
    /// WHY THE SERVER NEEDS IT AT RUNTIME and not only in tests: the catalogue's
    /// prefab names are compile-time-pinned, but the EFFECTIVE prefab of a craft
    /// can be changed per-schematic at runtime via the WAREBORN_PART_PREFAB__*
    /// env overrides (the live escape hatch). A typo there used to mean the
    /// materials were consumed, the entity was broadcast, and the client threw
    /// MissingComponentException and showed NOTHING - the exact "crafted it,
    /// resources eaten, nothing appears" bug. The station-craft gate calls
    /// <see cref="CanResolve"/> BEFORE consuming anything.
    /// </summary>
    public static class ClientEntityPrefabs
    {
        private static readonly HashSet<string> Names = Load();

        private static HashSet<string> Load()
        {
            Assembly asm = typeof(ClientEntityPrefabs).Assembly;
            string? resource = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("client-entity-prefabs.txt", StringComparison.Ordinal))
                {
                    resource = name;
                    break;
                }
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            if (resource == null)
            {
                // A missing resource must FAIL CLOSED into "refuse nothing" would be
                // wrong (it would let a bad prefab eat materials again) and "refuse
                // everything" would break all crafting on a packaging mistake. An
                // empty set means CanResolve() returns false for everything, which
                // the gate treats as refusal - loud, immediate, and covered by
                // ClientEntityPrefabsTests so it cannot ship.
                return set;
            }

            using Stream stream = asm.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    set.Add(trimmed.ToLowerInvariant());
                }
            }
            return set;
        }

        /// <summary>Every census name, lower-case. Exposed for the drift-pinning test.</summary>
        public static IReadOnlyCollection<string> All => Names;

        /// <summary>
        /// Whether the client can resolve <paramref name="prefabName"/>: the client
        /// lower-cases the name and appends the worker suffix, so a name resolves
        /// IFF its lower-cased form is in the census. Null/blank never resolves.
        /// </summary>
        public static bool CanResolve(string? prefabName)
        {
            return !string.IsNullOrWhiteSpace(prefabName)
                && Names.Contains(prefabName!.Trim().ToLowerInvariant());
        }
    }
}
