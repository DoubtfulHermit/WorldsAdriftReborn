using WorldsAdriftRebornGameServer.Multiplayer.Config;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Config;

/// <summary>
/// The record that makes a healing migration run exactly once, so it cannot
/// re-clobber a value the player deliberately chose afterwards.
/// </summary>
public class ConfigMigrationLedgerTests
{
    private const string Id = RestUrlPolicy.AlliancesHealMigrationId;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyLedgerRecordsNothing(string? ledger)
    {
        Assert.False(ConfigMigrationLedger.Contains(ledger!, Id));
        Assert.Empty(ConfigMigrationLedger.Entries(ledger!));
    }

    [Fact]
    public void AddThenContains()
    {
        string ledger = ConfigMigrationLedger.Add(null!, Id);
        Assert.Equal(Id, ledger);
        Assert.True(ConfigMigrationLedger.Contains(ledger, Id));
    }

    [Fact]
    public void AddIsIdempotentSoTheValueDoesNotGrowEveryLaunch()
    {
        string once = ConfigMigrationLedger.Add(null!, Id);
        string twice = ConfigMigrationLedger.Add(once, Id);
        string thrice = ConfigMigrationLedger.Add(twice, Id);
        Assert.Equal(once, twice);
        Assert.Equal(once, thrice);
        Assert.Single(ConfigMigrationLedger.Entries(thrice));
    }

    [Fact]
    public void SeveralMigrationsCoexist()
    {
        string ledger = ConfigMigrationLedger.Add(ConfigMigrationLedger.Add(null!, Id), "another-fix");
        Assert.True(ConfigMigrationLedger.Contains(ledger, Id));
        Assert.True(ConfigMigrationLedger.Contains(ledger, "another-fix"));
        Assert.Equal(new[] { Id, "another-fix" }, ConfigMigrationLedger.Entries(ledger));
    }

    [Fact]
    public void HandEditedPaddingAndBlanksAreTolerated()
    {
        // The value lives in a text file a player can edit.
        const string messy = "  , alliances-url-follows-rest ,,  another-fix , ";
        Assert.True(ConfigMigrationLedger.Contains(messy, Id));
        Assert.Equal(new[] { Id, "another-fix" }, ConfigMigrationLedger.Entries(messy));
        Assert.Equal(Id + ",another-fix", ConfigMigrationLedger.Add(messy, Id));
    }

    [Fact]
    public void IdsAreCaseInsensitive()
    {
        string ledger = ConfigMigrationLedger.Add(null!, Id);
        Assert.True(ConfigMigrationLedger.Contains(ledger, Id.ToUpperInvariant()));
    }

    [Fact]
    public void AnUnknownIdIsNotContained()
    {
        string ledger = ConfigMigrationLedger.Add(null!, Id);
        Assert.False(ConfigMigrationLedger.Contains(ledger, "some-future-fix"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankIdIsNeverRecorded(string? id)
    {
        Assert.False(ConfigMigrationLedger.Contains(Id, id!));
        Assert.Equal(Id, ConfigMigrationLedger.Add(Id, id!));
    }

    [Fact]
    public void ClearingTheLedgerLetsAMigrationRunAgain()
    {
        // The documented escape hatch: a player who wants the heal re-applied
        // empties the key.
        Assert.False(ConfigMigrationLedger.Contains(string.Empty, Id));
    }
}
