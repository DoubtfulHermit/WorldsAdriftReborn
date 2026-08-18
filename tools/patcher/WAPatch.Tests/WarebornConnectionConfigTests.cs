using WAPatch;
using Xunit;

namespace WAPatch.Tests;

public sealed class WarebornConnectionConfigTests
{
    [Fact]
    public void Empty_config_gets_every_required_section_and_value()
    {
        string result = WarebornConnectionConfig.Merge(string.Empty);

        Assert.Contains("[GameServer]", result);
        Assert.Contains("GameServer_Host = 62.171.161.19", result);
        Assert.Contains("GameServer_Port = 7779", result);
        Assert.Contains("[REST]", result);
        Assert.Contains("REST_ServerUrl = http://62.171.161.19:8085", result);
        Assert.Contains("REST_ServerDeploymentUrl = http://62.171.161.19:8085/deploymentStatus", result);
        Assert.False(WarebornConnectionConfig.NeedsUpdateText(result));
    }

    [Fact]
    public void Localhost_values_are_replaced_without_touching_personal_settings()
    {
        const string original = """
            # keep this comment
            [GameServer]
            GameServer_Host = 127.0.0.1
            GameServer_Port = 7777

            [Interact]
            Interact_StationPickupKey = P

            [REST]
            REST_ServerUrl = http://127.0.0.1:8080
            REST_ServerDeploymentUrl = http://127.0.0.1:8080/deploymentStatus
            """;

        string result = WarebornConnectionConfig.Merge(original);

        Assert.Contains("# keep this comment", result);
        Assert.Contains("Interact_StationPickupKey = P", result);
        Assert.DoesNotContain("127.0.0.1", result);
        Assert.DoesNotContain("GameServer_Port = 7777", result);
        Assert.False(WarebornConnectionConfig.NeedsUpdateText(result));
    }

    [Fact]
    public void Merge_is_idempotent_and_preserves_crlf()
    {
        string first = WarebornConnectionConfig.Merge("[Perf]\r\nPerf_SpikeThresholdMs = 250\r\n");
        string second = WarebornConnectionConfig.Merge(first);

        Assert.Equal(first, second);
        Assert.Contains("\r\n", second);
        Assert.DoesNotContain("\n", second.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void Conflicting_duplicate_cannot_leave_a_localhost_winner()
    {
        const string original = """
            [GameServer]
            GameServer_Host = 62.171.161.19
            GameServer_Host = 127.0.0.1
            GameServer_Port = 7779
            [REST]
            REST_ServerUrl = http://62.171.161.19:8085
            REST_ServerDeploymentUrl = http://62.171.161.19:8085/deploymentStatus
            """;

        Assert.True(WarebornConnectionConfig.NeedsUpdateText(original));
        string result = WarebornConnectionConfig.Merge(original);
        Assert.DoesNotContain("127.0.0.1", result);
        Assert.False(WarebornConnectionConfig.NeedsUpdateText(result));
    }

    [Fact]
    public void The_alliances_url_is_left_entirely_to_the_mod()
    {
        // The patcher must not force REST_AlliancesUrl. The mod's default is blank,
        // meaning "same origin as REST_ServerUrl", resolved at read time; forcing a
        // literal here would restore the hardcoded duplicate that broke the Social
        // Sheet for every player, and would clobber a deliberate operator split on
        // every patch run.
        string fresh = WarebornConnectionConfig.Merge(string.Empty);
        Assert.DoesNotContain("REST_AlliancesUrl", fresh);
    }

    [Fact]
    public void An_operators_explicit_alliances_host_survives_a_patch_run()
    {
        const string original = """
            [REST]
            REST_ServerUrl = http://127.0.0.1:8080
            REST_ServerDeploymentUrl = http://127.0.0.1:8080/deploymentStatus
            REST_AlliancesUrl = http://social.example.test:9100
            """;

        string result = WarebornConnectionConfig.Merge(original);

        Assert.Contains("REST_AlliancesUrl = http://social.example.test:9100", result);
        Assert.Contains("REST_ServerUrl = http://62.171.161.19:8085", result);
    }

    [Fact]
    public void A_blank_alliances_url_is_not_filled_in()
    {
        // Blank is the correct, self-healing state: it follows REST_ServerUrl.
        const string original = """
            [GameServer]
            GameServer_Host = 62.171.161.19
            GameServer_Port = 7779

            [REST]
            REST_ServerUrl = http://62.171.161.19:8085
            REST_ServerDeploymentUrl = http://62.171.161.19:8085/deploymentStatus
            REST_AlliancesUrl =
            """;

        // A blank alliances key is not a reason to rewrite the file.
        Assert.False(WarebornConnectionConfig.NeedsUpdateText(original));
        Assert.Contains("REST_AlliancesUrl =", WarebornConnectionConfig.Merge(original));
    }
}
