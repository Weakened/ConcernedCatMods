using TheConcernedCat.Diagnostics;

namespace Shared.Settlement.Tests;

/// <summary>#411: the one path scrubber and the one failure wording, tested where
/// they live rather than in a product.
///
/// <b>Why they are shared at all.</b> Two products wrote these patterns
/// independently, because products never reference each other at compile time.
/// Then #388 fixed a defect in one and #410 had to notice and fix the identical
/// defect in the other, separately — and Concerned Foreman, which had no
/// scrubber at all, was about to need a third copy. These cases are the ones that
/// used to be duplicated across two suites.</summary>
public class PathScrubberTests
{
    private const string Thunderstore =
        "C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager\\DataFolder\\Valheim" +
        "\\profiles\\tcc-dev\\BepInEx\\config\\teamster.cfg";

    // ------------------------------------------------------------------
    // The one parameter, which is a real difference and not a preference
    // ------------------------------------------------------------------

    [Fact]
    public void KeepingTheFileName_IsTheOnlyDifferenceBetweenTheTwoCallers()
    {
        // A caller that composes its own strings from known-safe parts keeps the
        // file name for diagnostics; a caller handling arbitrary log text cannot,
        // because a leaf file name can itself be the identifying content.
        Assert.Equal("<path>/teamster.cfg", PathScrubber.Scrub(Thunderstore, keepFileName: true));
        Assert.Equal("<path>", PathScrubber.Scrub(Thunderstore, keepFileName: false));
    }

    [Theory]
    // Neither setting keeps anything that identifies the machine or the player.
    [InlineData(true)]
    [InlineData(false)]
    public void NeitherSettingKeepsTheUserOrTheProfile(bool keepFileName)
    {
        string scrubbed = PathScrubber.Scrub(Thunderstore, keepFileName);

        Assert.DoesNotContain("erenc", scrubbed);
        Assert.DoesNotContain("Mod Manager", scrubbed);
        Assert.DoesNotContain("DataFolder", scrubbed);
        Assert.DoesNotContain("profiles", scrubbed);
        Assert.DoesNotContain("tcc-dev", scrubbed);
        Assert.DoesNotContain("BepInEx", scrubbed);
        Assert.DoesNotContain("\\", scrubbed);
    }

    // ------------------------------------------------------------------
    // The space rule, in both directions it has to be right in
    // ------------------------------------------------------------------

    [Theory]
    // A user name with a space in it. The rule used to test the CASE of the
    // token after a space, so a lower-case second word refused the run and the
    // surname, the folder layout and the profile name all survived.
    [InlineData("C:\\Users\\Eren cansunar\\AppData\\Roaming\\TMM\\p\\x.cfg", "cansunar")]
    [InlineData("C:\\Documents and Settings\\erenc\\Application Data\\x.cfg", "erenc")]
    [InlineData("C:\\Users\\erenc\\Meine \u00e4nderungen\\Valheim\\profiles\\secret\\x.cfg", "secret")]
    [InlineData("C:\\Users\\erenc\\\u041c\u043e\u0438 \u043c\u043e\u0434\u044b\\Valheim\\profiles\\secret\\x.cfg", "secret")]
    [InlineData("C:\\Program Files (x86)\\Steam\\steamapps\\common\\Valheim\\a.dll", "steamapps")]
    [InlineData("/Users/erenc/Library/Application Support/Steam/steamapps/common/x.dll", "steamapps")]
    [InlineData("/home/erenc/.config/r2modmanPlus-local/Valheim/profiles/My Test/BepInEx/a.cfg", "My Test")]
    public void APathWithSpacesIsScrubbedPastTheFirstSpace(string line, string forbidden)
    {
        Assert.DoesNotContain(forbidden, PathScrubber.Scrub(line, keepFileName: false));
        Assert.DoesNotContain(forbidden, PathScrubber.Scrub(line, keepFileName: true));
    }

    [Theory]
    // The other direction: the sentence around a path is the diagnostic, and a
    // rule that admitted runs without limit ate it. The second row is a mod's
    // own warning shape, and the pointer to the log file went with it.
    [InlineData("wrote C:\\a\\b.cfg OK", "wrote <path> OK")]
    [InlineData("wrote C:\\a\\b OK See BepInEx/LogOutput.log", "wrote <path> OK See BepInEx/LogOutput.log")]
    [InlineData("plugin loaded from C:\\a\\plugin.dll 0.9.0", "plugin loaded from <path> 0.9.0")]
    [InlineData("C:\\a\\b.cfg Cannot Be Read", "<path> Cannot Be Read")]
    [InlineData("C:\\a\\b.cfg Zugriffsverweigerung", "<path> Zugriffsverweigerung")]
    [InlineData("copy C:\\a\\b.cfg to D:\\home\\erenc\\mods\\x.cfg", "copy <path> to <path>")]
    [InlineData(
        "Could not open C:\\a\\b.txt was not found, see the log/file for details.",
        "Could not open <path> was not found, see the log/file for details.")]
    public void TheSentenceAroundAPathSurvives(string line, string expected)
    {
        Assert.Equal(expected, PathScrubber.Scrub(line, keepFileName: false));
    }

    [Fact]
    public void AnOrdinaryLineIsUnchanged()
    {
        const string ordinary = "Cart telemetry sampler armed: interval 0.5 s, radius 25 m.";
        Assert.Equal(ordinary, PathScrubber.Scrub(ordinary, keepFileName: false));
        Assert.Equal(ordinary, PathScrubber.Scrub(ordinary, keepFileName: true));
    }

    [Fact]
    public void NullAndEmptyAnswerWithTheEmptyString()
    {
        Assert.Equal("", PathScrubber.Scrub(null, keepFileName: false));
        Assert.Equal("", PathScrubber.Scrub("", keepFileName: true));
    }

    [Fact]
    public void ALongLineIsTruncated()
    {
        // Short space-separated words, so no scrub pattern fires and shrinks the
        // string first: this isolates truncation.
        string line = string.Concat(System.Linq.Enumerable.Repeat("word ", 700));
        string scrubbed = PathScrubber.Scrub(line, keepFileName: false);

        Assert.True(scrubbed.Length <= PathScrubber.DefaultMaxLength + "…[truncated]".Length);
        Assert.EndsWith("…[truncated]", scrubbed);
    }

    [Fact]
    public void AWorldNameStillReachesTheSaveFileRule()
    {
        // Valheim's save-file names ARE world and character names, so path
        // scrubbing alone is not enough for them even when a file name is kept
        // on purpose.
        Assert.Contains("<save>.db", PathScrubber.Scrub("world=MyWorld.db", keepFileName: true));
        Assert.DoesNotContain("MyWorld", PathScrubber.Scrub("world=MyWorld.db", keepFileName: true));
    }
}

/// <summary>#411: what a console command says when something threw. The defect
/// was the same line in four commands across two products, after #367 and #389
/// had removed it from seven in a third.</summary>
public class SafeFailureTests
{
    private static System.IO.IOException Realistic() =>
        new System.IO.IOException(
            "Could not find a part of the path 'C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore " +
            "Mod Manager\\DataFolder\\Valheim\\profiles\\TCC\\BepInEx\\config\\x.tsv'.");

    [Theory]
    [InlineData("ct_haul", "retire")]
    [InlineData("ct_collect", "pick")]
    [InlineData("ct_collect", "chest")]
    [InlineData("cf_collect", "start")]
    public void AFailureNamesTheCommandAndSubcommandAndScrubsThePath(string command, string subcommand)
    {
        string reply = SafeFailure.Describe(command, subcommand, Realistic());

        Assert.Contains(command + " " + subcommand, reply);
        Assert.Contains("IOException", reply);

        Assert.DoesNotContain("erenc", reply);
        Assert.DoesNotContain("AppData", reply);
        Assert.DoesNotContain("Mod Manager", reply);
        Assert.DoesNotContain("profiles", reply);
        Assert.DoesNotContain("BepInEx", reply);
        Assert.DoesNotContain("\\", reply);
    }

    [Fact]
    public void TheOldRawReplyIsWhatThisRulesOut()
    {
        // Written as a contrast because "it is scrubbed now" only means
        // something against what it replaced. This is the exact shape four
        // wrappers produced.
        System.IO.IOException exception = Realistic();
        string raw = "ct_haul failed: " + exception.GetType().Name + ": " + exception.Message;

        Assert.Contains("erenc", raw);
        Assert.DoesNotContain("retire", raw);

        string scrubbed = SafeFailure.Describe("ct_haul", "retire", exception);
        Assert.DoesNotContain("erenc", scrubbed);
        Assert.Contains("ct_haul retire", scrubbed);
    }

    [Fact]
    public void ABlankSubcommandAnswersWithTheCommandAlone()
    {
        // Naming a subcommand the player did not type points their bug report at
        // the wrong operation, which is a defect #389 found the hard way in a
        // command whose bare form dispatches to something other than `status`.
        Assert.Equal("ct_haul could not finish.", SafeFailure.Describe("ct_haul", "", null));
        Assert.Equal("ct_haul could not finish.", SafeFailure.Describe("ct_haul", "   ", null));
    }

    [Fact]
    public void AFailureReportNeverAddsASecondFailure()
    {
        // This runs in a catch block. Anything it throws replaces a reported
        // failure with an unreported one.
        Assert.Equal("", SafeFailure.Brief(null));
        Assert.Equal("", SafeFailure.Describe((System.Exception?)null));
        Assert.Equal("cf_collect start could not finish.", SafeFailure.Describe("cf_collect", "start", null));
    }

    [Fact]
    public void DescribeScrubsTheStackAsWellAsTheMessage()
    {
        // ToString() carries the path AND the stack, which is why it is scrubbed
        // rather than trusted: a rule that banned only `.Message` would have let
        // the bigger leak through under a different spelling.
        System.Exception caught;
        try
        {
            throw Realistic();
        }
        catch (System.Exception exception)
        {
            caught = exception;
        }

        string described = SafeFailure.Describe(caught);

        Assert.Contains("IOException", described);
        Assert.DoesNotContain("erenc", described);
        Assert.DoesNotContain("Mod Manager", described);
    }
}
