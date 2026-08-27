using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

public class AtlasStringsTests
{
    [Fact]
    public void Defaults_ResolveAndUnknownKeysFallBackToTheKey()
    {
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
        Assert.Equal("Pin Workbench", AtlasStrings.Get("workbench.title"));
        Assert.Equal("nonsense.key", AtlasStrings.Get("nonsense.key"));
    }

    [Fact]
    public void Overrides_Win_AndMissingKeysFallBackToEnglish()
    {
        AtlasStrings.LoadOverrides(new Dictionary<string, string> { ["workbench.title"] = "Nadel-Werkbank" });
        Assert.Equal("Nadel-Werkbank", AtlasStrings.Get("workbench.title"));
        Assert.Equal("Apply", AtlasStrings.Get("workbench.apply"));
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
    }

    [Fact]
    public void BrokenTranslationFormat_NeverThrows()
    {
        AtlasStrings.LoadOverrides(new Dictionary<string, string> { ["hud.quickPinned"] = "Broken {0} {1} {2}" });
        string result = AtlasStrings.Format("hud.quickPinned", "X");
        Assert.Contains("X", result);
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
    }

    [Fact]
    public void TemplateRoundtrip_CoversEveryKey()
    {
        var template = new List<string>(AtlasStrings.TranslatorTemplate());
        Dictionary<string, string> parsed = AtlasStrings.ParseOverrides(template, out int skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(AtlasStrings.Defaults.Count, parsed.Count);
    }

    [Fact]
    public void UnknownKeysAndMalformedRows_AreSkipped()
    {
        var lines = new List<string>
        {
            AtlasStrings.TemplateHeader,
            "workbench.title\tOK",
            "not.a.real.key\tX",
            "noTabHere",
        };

        Dictionary<string, string> parsed = AtlasStrings.ParseOverrides(lines, out int skipped);
        Assert.Single(parsed);
        Assert.Equal(2, skipped);
    }
}
