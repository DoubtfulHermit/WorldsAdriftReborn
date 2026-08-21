using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class PartAuthorityWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        [Fact]
        public void Pickup_authority_passes_before_carry_or_unmount_mutation()
        {
            string handler = Source("WorldsAdriftRebornGameServer", "Game", "Components", "Update",
                "Handlers", "PlacementToolPlayerState_Handler.cs");
            int verdict = handler.IndexOf("PartPickupPolicy.Evaluate", StringComparison.Ordinal);
            int accepted = handler.IndexOf("pickupVerdict !=", verdict, StringComparison.Ordinal);
            int carry = handler.IndexOf("MountedParts.SetCarried", verdict, StringComparison.Ordinal);
            int unmount = handler.IndexOf("MountedParts.Unmount", verdict, StringComparison.Ordinal);

            Assert.True(verdict >= 0 && accepted > verdict && carry > accepted && unmount > carry,
                "no carry, detach, persistence or domain mutation may precede the pickup verdict");
        }

        [Fact]
        public void Mount_resolves_the_durable_hull_owner_and_transform_before_policy()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "PartMountService.cs");
            Assert.Contains("BuiltShips.OwnerFor(shipId)", service, StringComparison.Ordinal);
            Assert.Contains("PartMount.IsRepresentableLocalOffset", service, StringComparison.Ordinal);
            Assert.Contains("shipIsBuilt, requesterOwnsShip, targetChild, representableTransform",
                service, StringComparison.Ordinal);
        }

        [Fact]
        public void Disconnect_frees_the_carry_reservation()
        {
            string server = Source("WorldsAdriftRebornGameServer", "WorldsAdriftRebornGameServer.cs");
            Assert.Contains("Game.Crafting.MountedParts.ClearCarried(ownEntity.Value);",
                server, StringComparison.Ordinal);
        }
    }
}
