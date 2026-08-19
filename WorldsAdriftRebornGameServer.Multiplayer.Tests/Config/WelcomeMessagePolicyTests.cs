using WorldsAdriftRebornGameServer.Multiplayer.Config;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Config;

/// <summary>
/// The rules that decide what the splash page's welcome scroll actually says.
///
/// The failure these lock down is quiet rather than loud: the message is fetched
/// from our login server, and every way that fetch can fail - server down,
/// player offline, response not landed yet - ends with the client holding null
/// or "". If that reached the label the player would get an empty parchment, or
/// worse, whatever Bossa baked into the prefab in 2019. So "no answer" has to
/// resolve to OUR text, and it has to do so for every shape of no answer.
/// </summary>
public class WelcomeMessagePolicyTests
{
    private const string Production = "http://62.171.161.19:8085";

    // ---- where the client fetches from -------------------------------------

    [Fact]
    public void TheEndpointHangsOffRestServerUrl()
    {
        Assert.Equal(Production + "/welcomeMessage", WelcomeMessagePolicy.ResolveUrl(Production));
    }

    [Fact]
    public void ATrailingSlashOnRestServerUrlDoesNotProduceADoubleSlash()
    {
        Assert.Equal(Production + "/welcomeMessage", WelcomeMessagePolicy.ResolveUrl(Production + "/"));
        Assert.Equal(Production + "/welcomeMessage", WelcomeMessagePolicy.ResolveUrl(Production + "///"));
    }

    // ---- no answer falls back to our text, never to an empty scroll --------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\r\n")]
    public void AnAbsentOrBlankMessageIsNotUsable(string? fetched)
    {
        Assert.False(WelcomeMessagePolicy.IsUsable(fetched!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void AnAbsentOrBlankMessageRendersTheBakedDefault(string? fetched)
    {
        Assert.Equal(
            WelcomeMessagePolicy.Normalize(WelcomeMessagePolicy.DefaultMessage),
            WelcomeMessagePolicy.Choose(fetched!));
    }

    [Fact]
    public void AFetchedMessageWins()
    {
        Assert.Equal("Hello, sailor.", WelcomeMessagePolicy.Choose("Hello, sailor."));
    }

    // ---- normalisation ------------------------------------------------------

    [Fact]
    public void CarriageReturnsAreStrippedSoTextMeshProDoesNotDrawBoxGlyphs()
    {
        Assert.Equal("one\ntwo", WelcomeMessagePolicy.Normalize("one\r\ntwo"));
        Assert.Equal("one\ntwo", WelcomeMessagePolicy.Normalize("one\rtwo"));
        Assert.DoesNotContain("\r", WelcomeMessagePolicy.Choose("a\r\nb\rc"));
    }

    [Fact]
    public void SurroundingBlankSpaceIsTrimmed()
    {
        Assert.Equal("body", WelcomeMessagePolicy.Normalize("\n\n  body  \n\n"));
    }

    [Fact]
    public void ARunOfBlankLinesCollapsesToOne()
    {
        Assert.Equal("a\n\nb", WelcomeMessagePolicy.Normalize("a\n\n\n\n\n\nb"));
    }

    [Fact]
    public void ASingleNewlineSurvives()
    {
        Assert.Equal("a\nb", WelcomeMessagePolicy.Normalize("a\nb"));
    }

    [Fact]
    public void AnOverlongMessageIsCappedRatherThanRefused()
    {
        string huge = new string('x', WelcomeMessagePolicy.MaxLength + 500);
        string capped = WelcomeMessagePolicy.Choose(huge);

        Assert.True(capped.Length <= WelcomeMessagePolicy.MaxLength);
        Assert.StartsWith("xxxx", capped);
    }

    // ---- the baked text itself ---------------------------------------------

    /// <summary>
    /// Pins the shipped copy. It is duplicated in the server's
    /// ServerConfigPolicy.DefaultWelcomeMessage because a net35 Unity assembly
    /// and net8 Postgres code cannot share a file, and a silent drift between
    /// them would mean offline players read different words to everyone else.
    /// If this fails because the copy was deliberately rewritten, update the
    /// server's constant in the same commit.
    /// </summary>
    [Fact]
    public void DefaultMessageIsPinned()
    {
        Assert.Equal(
            "Greetings Traveller,\n"
            + "\n"
            + "Worlds Adrift closed in 2019. Wareborn is a fan-run server that puts it back online.\n"
            + "\n"
            + "Much of the game is here. Islands, ships, mining, crafting, and the sky between them. "
            + "Some of it is not, and some of it breaks. We fix things as we find them.\n"
            + "\n"
            + "Nothing here is for sale. There is no studio behind it, just people who missed the game.\n"
            + "\n"
            + "See you in the skies.\n"
            + "\n"
            + "- The Wareborn crew",
            WelcomeMessagePolicy.DefaultMessage);
    }

    [Fact]
    public void TheBakedDefaultSaysNothingAboutBossaOrEarlyAccess()
    {
        string text = WelcomeMessagePolicy.DefaultMessage;

        Assert.DoesNotContain("Community-Crafted", text);
        Assert.DoesNotContain("Community Managers", text);
        Assert.DoesNotContain("early stages of development", text);
        Assert.Contains("Wareborn", text);
    }

    [Fact]
    public void TheBakedDefaultSurvivesItsOwnNormalisation()
    {
        // A default that changed shape when normalised would mean the offline
        // text and the "operator typed the same thing" text differed.
        Assert.Equal(
            WelcomeMessagePolicy.DefaultMessage,
            WelcomeMessagePolicy.Normalize(WelcomeMessagePolicy.DefaultMessage));
    }
}
