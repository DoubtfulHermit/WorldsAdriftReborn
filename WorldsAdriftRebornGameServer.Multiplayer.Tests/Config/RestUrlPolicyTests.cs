using WorldsAdriftRebornGameServer.Multiplayer.Config;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Config;

/// <summary>
/// The rules that keep the Social Sheet pointed at a host that answers.
///
/// The bug these lock down: REST_AlliancesUrl shipped a hardcoded
/// http://127.0.0.1:8080 default while claiming to mean "same origin as
/// REST_ServerUrl". Because it was a NEW key, BepInEx wrote that localhost into
/// every EXISTING player's config, and the Social Sheet failed whole for all of
/// them.
/// </summary>
public class RestUrlPolicyTests
{
    private const string Production = "http://62.171.161.19:8085";

    // ---- the default means "same origin", and keeps meaning it -------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankAlliancesSettingFollowsRestServerUrl(string? setting)
    {
        Assert.True(RestUrlPolicy.FollowsRestServerUrl(setting!));
        Assert.Equal(Production, RestUrlPolicy.ResolveAlliancesUrl(setting!, Production));
    }

    [Fact]
    public void BlankAlliancesSettingTracksRestServerUrlWhenItMoves()
    {
        // The whole point of the sentinel: the old hardcoded literal could not do
        // this, which is how "same origin" and "127.0.0.1" drifted apart.
        Assert.Equal("http://example.test:9000",
            RestUrlPolicy.ResolveAlliancesUrl(RestUrlPolicy.FollowRestServerUrl, "http://example.test:9000"));
        Assert.Equal("http://other.test:1234",
            RestUrlPolicy.ResolveAlliancesUrl(RestUrlPolicy.FollowRestServerUrl, "http://other.test:1234"));
    }

    [Fact]
    public void ShippedDefaultIsTheFollowSentinelNotALiteral()
    {
        Assert.Equal(string.Empty, RestUrlPolicy.FollowRestServerUrl);
    }

    // ---- an operator can still split the two services ----------------------

    [Fact]
    public void ExplicitAlliancesHostOverridesRestServerUrl()
    {
        // Retail ran alliances and REST as separate services. That must stay possible.
        Assert.Equal("http://alliances.example.test",
            RestUrlPolicy.ResolveAlliancesUrl("http://alliances.example.test", Production));
    }

    [Fact]
    public void ExplicitLoopbackAlliancesHostIsHonouredWhenSet()
    {
        // A developer pointing at their own social server must be obeyed, not
        // second-guessed, once the value is not the legacy default.
        Assert.Equal("http://127.0.0.1:8085",
            RestUrlPolicy.ResolveAlliancesUrl("http://127.0.0.1:8085", Production));
    }

    // ---- the no-trailing-slash rule ----------------------------------------

    [Theory]
    [InlineData("http://host.test:8085/", "http://host.test:8085")]
    [InlineData("http://host.test:8085///", "http://host.test:8085")]
    [InlineData("  http://host.test:8085/  ", "http://host.test:8085")]
    [InlineData("http://host.test:8085", "http://host.test:8085")]
    public void TrailingSlashesAreStripped(string input, string expected)
    {
        // The client joins this with "/" + endpoint (SocialRequest.cs:69), so a
        // trailing slash yields "host//crew".
        Assert.Equal(expected, RestUrlPolicy.TrimTrailingSlashes(input));
    }

    [Fact]
    public void TrailingSlashIsStrippedFromAnExplicitOverride()
    {
        Assert.Equal("http://alliances.example.test",
            RestUrlPolicy.ResolveAlliancesUrl("http://alliances.example.test/", Production));
    }

    [Fact]
    public void TrailingSlashIsStrippedFromAnInheritedRestServerUrl()
    {
        Assert.Equal("http://host.test:8085",
            RestUrlPolicy.ResolveAlliancesUrl(RestUrlPolicy.FollowRestServerUrl, "http://host.test:8085/"));
    }

    // ---- deploymentStatus, the same bug class ------------------------------

    [Fact]
    public void BlankDeploymentSettingDerivesFromRestServerUrl()
    {
        Assert.Equal(Production + "/deploymentStatus",
            RestUrlPolicy.ResolveDeploymentUrl(RestUrlPolicy.FollowRestServerUrl, Production));
    }

    [Fact]
    public void DeploymentDerivationDoesNotDoubleTheSlash()
    {
        Assert.Equal("http://host.test:8085/deploymentStatus",
            RestUrlPolicy.ResolveDeploymentUrl(null!, "http://host.test:8085/"));
    }

    [Fact]
    public void ExplicitDeploymentSettingIsUsedVerbatim()
    {
        // Existing installs already carry a full explicit value, written by the
        // patcher. Changing the default must not disturb them.
        Assert.Equal("http://elsewhere.test/deploymentStatus",
            RestUrlPolicy.ResolveDeploymentUrl("http://elsewhere.test/deploymentStatus", Production));
    }

    // ---- what counts as a host only the player can reach -------------------

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.1.2.3:8085")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://LOCALHOST:8080")]
    [InlineData("http://dev.localhost:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://0.0.0.0:8080")]
    public void LoopbackHostsAreRecognised(string url)
    {
        Assert.True(RestUrlPolicy.IsLoopbackOrUnroutable(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("ftp://host.test")]
    [InlineData("62.171.161.19:8085")]
    public void UnparseableUrlsCountAsUnroutableSoCallersFailClosed(string? url)
    {
        Assert.True(RestUrlPolicy.IsLoopbackOrUnroutable(url!));
    }

    [Theory]
    [InlineData("http://62.171.161.19:8085")]
    [InlineData("https://wareborn.ratlabs.cc")]
    [InlineData("http://127x.example.test")]
    public void RealRemoteHostsAreNotLoopback(string url)
    {
        Assert.False(RestUrlPolicy.IsLoopbackOrUnroutable(url));
    }

    // ---- healing installs that already took the bad default ----------------

    [Fact]
    public void HealsTheShippedLocalhostDefaultWhenRestIsRemote()
    {
        // The exact state every existing player was left in.
        Assert.True(RestUrlPolicy.ShouldHealAlliancesUrl(
            RestUrlPolicy.LegacyAlliancesDevDefault, Production, migrationAlreadyApplied: false));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("  http://127.0.0.1:8080  ")]
    public void HealMatchesTheLegacyLiteralModuloPaddingAndTrailingSlash(string stored)
    {
        Assert.True(RestUrlPolicy.ShouldHealAlliancesUrl(stored, Production, false));
    }

    [Fact]
    public void DoesNotHealWhenRestServerUrlIsAlsoLocal()
    {
        // A local dev has REST on loopback too. Leave the whole setup alone.
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(
            RestUrlPolicy.LegacyAlliancesDevDefault, "http://127.0.0.1:8080", false));
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(
            RestUrlPolicy.LegacyAlliancesDevDefault, "http://localhost:8085", false));
    }

    [Fact]
    public void DoesNotHealWhenRestServerUrlIsNotUsable()
    {
        // Fail closed rather than invent a host.
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(RestUrlPolicy.LegacyAlliancesDevDefault, "", false));
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(RestUrlPolicy.LegacyAlliancesDevDefault, null!, false));
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(RestUrlPolicy.LegacyAlliancesDevDefault, "garbage", false));
    }

    [Fact]
    public void DoesNotHealADifferentLoopbackHost()
    {
        // Only the shipped literal is presumed accidental. A developer pointing at
        // the port their own server actually listens on is a deliberate choice.
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl("http://127.0.0.1:8085", Production, false));
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl("http://localhost:8080", Production, false));
    }

    [Fact]
    public void DoesNotHealAnAlreadyCorrectValue()
    {
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(Production, Production, false));
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(RestUrlPolicy.FollowRestServerUrl, Production, false));
    }

    [Fact]
    public void HealRunsAtMostOnce()
    {
        // So a developer who deliberately re-enters that literal afterwards keeps it.
        Assert.False(RestUrlPolicy.ShouldHealAlliancesUrl(
            RestUrlPolicy.LegacyAlliancesDevDefault, Production, migrationAlreadyApplied: true));
    }

    [Fact]
    public void HealedValueResolvesToTheRestOrigin()
    {
        // End to end: the broken config, healed, produces the reachable host.
        const string broken = RestUrlPolicy.LegacyAlliancesDevDefault;
        Assert.True(RestUrlPolicy.ShouldHealAlliancesUrl(broken, Production, false));

        string healed = RestUrlPolicy.FollowRestServerUrl;
        Assert.Equal(Production, RestUrlPolicy.ResolveAlliancesUrl(healed, Production));
    }
}
