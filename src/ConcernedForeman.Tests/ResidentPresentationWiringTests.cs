using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConcernedForeman.Tests;

/// <summary>#356 review fixes, read from source because the presentation binds
/// the game: a resident who has lost their home is not left standing at the bed
/// that is gone, and being away does not revalidate the journal every frame.</summary>
public sealed class ResidentPresentationWiringTests
{
    private static string Source(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(path, "ConcernedCatMods.sln")))
        {
            path = Directory.GetParent(path)!.FullName;
        }

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static string Code(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);

    private static string Presentation =>
        Code(Source("src", "ConcernedForeman", "Runtime", "Presentation", "ResidentPresentation.cs"));

    private static string Body(string code, string signature)
    {
        int start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing " + signature);
        int open = code.IndexOf('{', start);
        int depth = 0;
        for (int index = open; index < code.Length; index++)
        {
            if (code[index] == '{') depth++;
            else if (code[index] == '}' && --depth == 0) return code.Substring(open, index - open + 1);
        }

        throw new InvalidOperationException("unbalanced " + signature);
    }

    [Fact]
    public void ARealHousingRefusalTakesTheFigureDown()
    {
        string record = Body(Presentation, "private bool RecordHousing(HousingRefusal refusal)");
        int none = record.IndexOf("if (refusal == HousingRefusal.None) return true;", StringComparison.Ordinal);
        int down = record.IndexOf("if (refusal != HousingRefusal.NotMeasured) TakeDown();", StringComparison.Ordinal);
        Assert.True(none >= 0 && down > none, "a homeless resident's figure must come down after the None check");
    }

    [Fact]
    public void BeingAwayRevalidatesOnceNotEveryFrame()
    {
        string tick = Body(Presentation, "internal void Tick(float deltaTime)");
        Assert.Contains("if (!_away) { _away = true; RecordHousing(HousingRefusal.NotMeasured); }", tick);
        Assert.DoesNotContain("TakeDown(); RecordHousing(HousingRefusal.NotMeasured); return;", tick);
        Assert.Contains("_away = false;", tick);
        Assert.Contains("_away = false;", Body(Presentation, "internal void Forget()"));
    }
}
