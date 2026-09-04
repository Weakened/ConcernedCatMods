using TheConcernedCat.ConcernedTeamster.Domain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-001: the startup banner must render every environment label
/// on exactly one line, with a deterministic "unknown" fallback, no matter
/// what the foreign assemblies and reflective game lookups return.</summary>
public class EnvironmentBannerTests
{
    [Fact]
    public void Compose_AllValuesKnown_ProducesTheDocumentedBannerShape()
    {
        string banner = EnvironmentBanner.Compose(
            "ConcernedTeamster@0.1.0+abc1234",
            "0.220.5",
            "6000.0.32f1",
            "5.4.23.3",
            "2.29.2");

        Assert.Equal(
            "Release ConcernedTeamster@0.1.0+abc1234 | Valheim 0.220.5 | " +
            "Unity 6000.0.32f1 | BepInEx 5.4.23.3 | Jotunn 2.29.2",
            banner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Normalize_MissingOrBlankLabels_FallBackToUnknown(string? label)
    {
        Assert.Equal(EnvironmentBanner.Unknown, EnvironmentBanner.Normalize(label));
    }

    [Fact]
    public void Normalize_TrimsPaddingAndKeepsInnerContent()
    {
        Assert.Equal("0.220.5", EnvironmentBanner.Normalize("  0.220.5  "));
    }

    [Theory]
    [InlineData("5.4\n[Fake] error line", "5.4 [Fake] error line")]
    [InlineData("5.4\r\ninjected", "5.4 injected")]
    [InlineData("a\rb\nc", "a b c")]
    public void Normalize_CollapsesLineBreaks_SoLabelsCannotForgeLogLines(
        string hostile, string expected)
    {
        Assert.Equal(expected, EnvironmentBanner.Normalize(hostile));
    }

    [Fact]
    public void Compose_HostileMultilineLabels_StillYieldExactlyOneLine()
    {
        string banner = EnvironmentBanner.Compose(
            "release\nid",
            "game\r\nversion",
            null,
            "",
            "jotunn\rlabel");

        Assert.DoesNotContain('\n', banner);
        Assert.DoesNotContain('\r', banner);
        Assert.Equal(
            "Release release id | Valheim game version | Unity unknown | " +
            "BepInEx unknown | Jotunn jotunn label",
            banner);
    }

    [Fact]
    public void Compose_AllValuesMissing_StillNamesEveryComponent()
    {
        string banner = EnvironmentBanner.Compose(null, null, null, null, null);

        Assert.Equal(
            "Release unknown | Valheim unknown | Unity unknown | " +
            "BepInEx unknown | Jotunn unknown",
            banner);
    }
}
