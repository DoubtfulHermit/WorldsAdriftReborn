using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The up-front "no craft may eat materials it cannot show" gate. The
    /// handler calls this BEFORE consuming anything, with the EFFECTIVE prefab
    /// (post env-override) and the runtime census - so these tests pin that a
    /// prefab the client cannot load refuses the craft at zero cost, with a
    /// player-facing reason, and that a resolvable prefab passes untouched.
    /// </summary>
    public class StationCraftOutputGateTests
    {
        private static readonly Func<string?, bool> Resolves = _ => true;
        private static readonly Func<string?, bool> NeverResolves = _ => false;

        [Fact]
        public void A_resolvable_prefab_passes_with_no_reason()
        {
            Assert.True(StationCraftOutputGate.CanRealize("CoreMain", Resolves, out string reason));
            Assert.Equal(string.Empty, reason);
        }

        [Fact]
        public void An_unresolvable_prefab_is_refused_and_names_the_prefab()
        {
            // The exact live failure shape: a prefab the client cannot load (a typo'd
            // env override, a bad future catalogue row). Refusal happens BEFORE any
            // consume, and the reason both names the prefab and tells the player their
            // materials were not taken.
            Assert.False(StationCraftOutputGate.CanRealize("CoreMian", NeverResolves, out string reason));
            Assert.Contains("CoreMian", reason);
            Assert.Contains("materials", reason);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_missing_prefab_name_is_refused(string? prefab)
        {
            Assert.False(StationCraftOutputGate.CanRealize(prefab, Resolves, out string reason));
            Assert.NotEqual(string.Empty, reason);
        }

        [Fact]
        public void An_empty_census_fails_closed()
        {
            // ClientEntityPrefabs falls back to an EMPTY set if its embedded resource
            // is missing; the gate must then refuse (loud, costs nothing) rather than
            // wave crafts through to spawn invisible parts.
            Assert.False(StationCraftOutputGate.CanRealize("Helm01", NeverResolves, out _));
        }

        [Fact]
        public void The_resolver_is_required()
        {
            Assert.Throws<ArgumentNullException>(
                () => StationCraftOutputGate.CanRealize("Helm01", null!, out _));
        }
    }
}
