using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// ACCEPTANCE CRITERION 7: "the core has no dependency on Worlds Adrift-specific
    /// types."
    ///
    /// <para>
    /// The handover suggested a separate <c>SimulationCore</c> project, where a
    /// compiler would enforce this. That was weighed against the standing rule that
    /// decisions belong in the Multiplayer assembly, which is the only one here with
    /// a test project - and the Multiplayer assembly is ALREADY dependency-free by
    /// design (no ENet, no Unity, no game assemblies; that is what lets it be tested
    /// on Linux without Wine). So the core lives in the
    /// <c>...Multiplayer.Simulation</c> namespace and the boundary is enforced by
    /// this test instead of by a csproj. It is checked two ways, because either one
    /// alone has a hole: reflection sees every type signature but not method bodies,
    /// and the source scan sees the whole file but only textually.
    /// </para>
    ///
    /// <para>
    /// ONE deliberate exception: <c>SimulationDomainId</c>, which already existed and
    /// is used across a dozen files. Reusing it is the point - a second domain-id
    /// type would mean the shadow model and the ownership host spelled "ship:893"
    /// differently and the inspector could never join them. It is a struct over a
    /// string with two static factories, and nothing about it is Worlds Adrift.
    /// </para>
    /// </summary>
    public class SimulationCorePurityTests
    {
        private const string CoreNamespace = "WorldsAdriftRebornGameServer.Multiplayer.Simulation";

        /// <summary>The one recorded exception; see the type remarks.</summary>
        private const string AllowedForeignType =
            "WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains.SimulationDomainId";

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string CoreDirectory() => Path.Combine(
            RepoRoot(), "WorldsAdriftRebornGameServer.Multiplayer", "Simulation");

        /// <summary>The core's own files: the Simulation folder WITHOUT its adapter subfolder.</summary>
        private static string[] CoreSourceFiles() =>
            Directory.GetFiles(CoreDirectory(), "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();

        private static Type[] CoreTypes() =>
            typeof(SimulationWorldModel).Assembly.GetTypes()
                .Where(t => t.Namespace == CoreNamespace)
                // Skip the compiler's own lambda/iterator machinery: its members
                // are synthesised, not authored, and its names are not stable.
                .Where(t => !t.Name.Contains('<'))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToArray();

        [Fact]
        public void The_core_actually_exists_so_this_suite_cannot_pass_vacuously()
        {
            // Every other test in this file is an "assert nothing bad is present"
            // test, and all of those pass on an empty set. This one is the floor.
            Assert.True(Directory.Exists(CoreDirectory()));
            Assert.True(CoreSourceFiles().Length >= 5,
                "expected the core's files, found " + CoreSourceFiles().Length);
            string[] names = CoreTypes().Select(t => t.Name).ToArray();
            foreach (string required in new[]
                     {
                         nameof(SimulationEntityId), nameof(InteractionEdge), nameof(InteractionKind),
                         nameof(InteractionPressure), nameof(SimulationWorldModel),
                         nameof(WorldSnapshot), nameof(DomainSnapshot), nameof(InteractionSnapshot),
                         nameof(SimulationDiagnostics),
                     })
            {
                Assert.Contains(required, names);
            }
        }

        [Fact]
        public void No_core_file_imports_anything_but_the_framework()
        {
            List<string> offences = new List<string>();
            foreach (string file in CoreSourceFiles())
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("using ", StringComparison.Ordinal)) continue;
                    if (line.StartsWith("using System", StringComparison.Ordinal)) continue;
                    string imported = line.Substring(6).TrimEnd(';').Trim();
                    if (imported == "WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains") continue;
                    if (imported == CoreNamespace) continue;
                    offences.Add(Path.GetFileName(file) + ": " + line);
                }
            }
            Assert.True(offences.Count == 0,
                "the engine-agnostic core imported something game-specific: "
                + string.Join(" | ", offences));
        }

        [Fact]
        public void No_core_file_names_a_worlds_adrift_concept()
        {
            // Textual, coarse, and deliberately so: it catches a fully-qualified
            // reference that dodges the using-scan, and it catches a core type
            // quietly growing a "ship" or "island" field.
            string[] forbidden =
            {
                "Improbable", "ENet", "Enet", "UnityEngine", "BepInEx", "GameState",
                "ShipFlightService", "IslandRegistry", "IslandDefinition", "WorldEntity",
                "FixedPointPosition", "PeerIdentity", "ResourceInterest", "AboardTracker",
                "PlayerRegistry", "LocalDomainHost", "ShipDomain", "IslandId",
            };
            List<string> offences = new List<string>();
            foreach (string file in CoreSourceFiles())
            {
                string text = File.ReadAllText(file);
                // Strip comments before matching: the doc comments here talk ABOUT
                // ships and islands on purpose, and prose is not a dependency.
                string code = StripComments(text);
                foreach (string term in forbidden)
                {
                    if (code.Contains(term, StringComparison.Ordinal))
                        offences.Add(Path.GetFileName(file) + " references " + term);
                }
            }
            Assert.True(offences.Count == 0,
                "the engine-agnostic core learned a Worlds Adrift concept: "
                + string.Join(" | ", offences));
        }

        [Fact]
        public void No_core_type_signature_mentions_a_foreign_type()
        {
            List<string> offences = new List<string>();
            foreach (Type type in CoreTypes())
            {
                foreach (Type referenced in SignatureTypes(type))
                {
                    Type target = Unwrap(referenced);
                    string? ns = target.Namespace;
                    if (ns == null) continue;
                    if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) continue;
                    if (ns == CoreNamespace) continue;
                    if (target.FullName == AllowedForeignType) continue;
                    offences.Add(type.Name + " -> " + target.FullName);
                }
            }
            Assert.True(offences.Count == 0,
                "a core type signature reached outside the core: " + string.Join(" | ", offences));
        }

        [Fact]
        public void The_one_allowed_exception_is_still_the_only_one()
        {
            // If the exception ever disappears (someone extracts a real core
            // project), this test should be deleted along with the allowance -
            // not left silently allowing a type nothing uses.
            bool used = CoreTypes()
                .SelectMany(SignatureTypes)
                .Select(Unwrap)
                .Any(t => t.FullName == AllowedForeignType);
            Assert.True(used,
                "the recorded SimulationDomainId exception is no longer used; delete the allowance");
        }

        private static IEnumerable<Type> SignatureTypes(Type type)
        {
            if (type.BaseType != null) yield return type.BaseType;
            foreach (Type i in type.GetInterfaces()) yield return i;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (FieldInfo f in type.GetFields(flags)) yield return f.FieldType;
            foreach (PropertyInfo p in type.GetProperties(flags)) yield return p.PropertyType;
            foreach (MethodInfo m in type.GetMethods(flags))
            {
                yield return m.ReturnType;
                foreach (ParameterInfo p in m.GetParameters()) yield return p.ParameterType;
            }
            foreach (ConstructorInfo c in type.GetConstructors(flags))
            {
                foreach (ParameterInfo p in c.GetParameters()) yield return p.ParameterType;
            }
        }

        private static Type Unwrap(Type type)
        {
            while (type.IsByRef || type.IsArray || type.IsPointer)
            {
                Type? element = type.GetElementType();
                if (element == null) break;
                type = element;
            }
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                // Only the first argument matters for the shapes used here
                // (IReadOnlyList<T>, Nullable<T>, Dictionary<K,V>); the generic
                // definition itself is always a System type.
                Type[] args = type.GetGenericArguments();
                foreach (Type arg in args)
                {
                    Type unwrapped = Unwrap(arg);
                    string? ns = unwrapped.Namespace;
                    if (ns != null && ns != "System"
                        && !ns.StartsWith("System.", StringComparison.Ordinal))
                        return unwrapped;
                }
                return type.GetGenericTypeDefinition();
            }
            return type;
        }

        private static string StripComments(string text)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder(text.Length);
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("///", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("*", StringComparison.Ordinal)) continue;
                int inline = line.IndexOf("//", StringComparison.Ordinal);
                b.Append(inline >= 0 ? line.Substring(0, inline) : line).Append('\n');
            }
            return b.ToString();
        }
    }
}
