using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class VirtualDeckPresentationWiringTests
    {
        private static string Source(params string[] parts)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
                    return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(parts)));
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        [Fact]
        public void LiveClientHidesEveryVirtualDeckRendererAndRetainsGeometry()
        {
            string patch = Source("WorldsAdriftReborn", "Patching", "Ship",
                "VirtualDeckPresentation_Patch.cs");

            Assert.Contains("HarmonyPatch(typeof(MeshGenerator), \"MakeVirtualDeck\")", patch,
                StringComparison.Ordinal);
            Assert.Contains("WorldsAdrift.IsClient", patch, StringComparison.Ordinal);
            Assert.DoesNotContain("CustomShipFrameVisualizer", patch,
                StringComparison.Ordinal);
            Assert.Contains("GetComponentsInChildren<Renderer>(true)", patch,
                StringComparison.Ordinal);
            Assert.Contains("renderers[i].enabled = false", patch, StringComparison.Ordinal);
            Assert.DoesNotContain("Destroy(virtualDeck", patch, StringComparison.Ordinal);
            Assert.DoesNotContain("Collider", patch, StringComparison.Ordinal);
        }
    }
}
