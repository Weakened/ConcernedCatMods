using System.IO;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>Issue #366: the startup rewrite of an untouched
/// <c>survey-rules.tsv</c> reset the file's modification time, and the
/// fresh-install probe reads that modification time as "has this player been
/// here before". A returning player therefore read as a brand-new one, was
/// re-onboarded, and — because that same answer feeds companion feature access —
/// could be told a story written for someone who had never used the mod.
///
/// <b>Why it got worse rather than older.</b> Before issue #385 the rewrite only
/// fired for pre-v1.0.3 starter files. #385 (commit 1e59155) added
/// <see cref="SurveyRuleSet.V103StarterSet"/>, the set shipped from v1.0.3
/// through v1.2.2, so the rewrite now fires for essentially the whole installed
/// base.
///
/// <b>What could not be tested before.</b> The rewrite lived in
/// <c>SurveyRulePersistence</c> and the question in
/// <c>CartographerLegacyProbe</c>, both in layers that need BepInEx and neither
/// compiled into this assembly. A disagreement between two pieces of code is
/// only catchable by a test that runs both, so there was none. Both halves are
/// now <see cref="SurveyRuleFile"/> and <see cref="PreexistingFile"/> in the
/// domain, and every test below drives the code the plugin actually ships
/// against a real file on disk.
///
/// <b>The direction of failure.</b> This program grants feature access on
/// ambiguous evidence and never revokes it. A wrongly-fresh probe is not
/// ambiguity — it is a false negative — so every assertion below is written so a
/// veteran stays a veteran, and a genuinely new player still reads as new
/// (<see cref="FirstRun_WritesTheStarterSetAndStillLooksLikeAFreshInstall"/>:
/// if that one fails, the #264 introduction never runs for anybody again, which
/// is the shape of #343).</summary>
public sealed class SurveyStarterRewriteTests : IDisposable
{
    /// <summary>A moment safely before anything this test writes. A file this
    /// test creates is therefore younger than the "session" and a file it ages
    /// is older, with a minute of slack so no filesystem's timestamp rounding
    /// can decide the outcome.</summary>
    private static readonly DateTime SessionStart = DateTime.UtcNow.AddMinutes(-1);

    /// <summary>When a returning player last touched their rules file: long
    /// before this session, which is the whole point of them.</summary>
    private static readonly DateTime LongBefore = DateTime.UtcNow.AddDays(-30);

    private readonly string _directory;

    private int _files;

    public SurveyStarterRewriteTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "cc-366-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that will not go is not a test failure.
        }
    }

    private string NewPath() =>
        Path.Combine(_directory, $"survey-rules-{++_files}.tsv");

    /// <summary>A rules file as a returning player's profile holds it: the given
    /// shipped starter set, last written long before this session.</summary>
    private string Aged(SurveyRuleSet shipped)
    {
        string path = NewPath();
        File.WriteAllLines(path, shipped.Serialize());
        File.SetLastWriteTimeUtc(path, LongBefore);
        return path;
    }

    private static bool LooksLikePriorUse(string path) =>
        PreexistingFile.ExistedBefore(path, SessionStart);

    /// <summary>What the unlock policy is actually told, for a profile whose only
    /// evidence is the rules file. This is the consequence the timestamp decides.
    /// </summary>
    private static LegacyEvidence EvidenceFor(string rulesPath) =>
        LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
            thisWorldHasData: false,
            anyWorldHasData: false,
            profileWideDataExists: LooksLikePriorUse(rulesPath),
            configuredBeforeThisRelease: false,
            probeFailed: false));

    // ------------------------------------------------------------------
    // The regression itself
    // ------------------------------------------------------------------

    [Fact]
    public void AnUntouchedV103StarterFile_GainsTheMineIdentitiesWithoutLosingItsHistory()
    {
        // The population #385 widened the rewrite to: everybody who installed
        // v1.0.3 or later and never edited their rules.
        string path = Aged(SurveyRuleSet.V103StarterSet());
        Assert.True(LooksLikePriorUse(path), "a file 30 days old is prior use before we touch it");

        SurveyRuleFile.Outcome outcome = SurveyRuleFile.LoadOrCreate(
            path, out SurveyRuleSet rules, out int malformed);

        // #385's property, unchanged: the untouched starter file really is
        // upgraded on disk and really does gain the mine identities.
        Assert.Equal(SurveyRuleFile.Outcome.Upgraded, outcome);
        Assert.Equal(0, malformed);
        Assert.Equal(SurveyRuleSet.Default().Serialize().ToArray(), File.ReadAllLines(path));
        Assert.True(rules.TryMatch("BOM_CopperMine01(Clone)", out _));

        // #366's property, new: and the upgrade did not rewrite his history.
        Assert.True(LooksLikePriorUse(path), "the upgrade must not make a veteran look new");
        Assert.Equal(LongBefore, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("pre-RC8")]
    [InlineData("RC8/RC9")]
    [InlineData("v1.0/v1.1.0")]
    [InlineData("v1.0.3-v1.2.2")]
    public void EveryUntouchedStarterFileEverShipped_UpgradesWithoutMovingItsTimestamp(string release)
    {
        SurveyRuleSet shipped = release switch
        {
            "pre-RC8" => SurveyRuleSet.LegacyStarterSet(),
            "RC8/RC9" => SurveyRuleSet.Rc8StarterSet(),
            "v1.0/v1.1.0" => SurveyRuleSet.V1StarterSet(),
            _ => SurveyRuleSet.V103StarterSet(),
        };

        string path = Aged(shipped);

        Assert.Equal(
            SurveyRuleFile.Outcome.Upgraded,
            SurveyRuleFile.LoadOrCreate(path, out _, out _));
        Assert.Equal(SurveyRuleSet.Default().Serialize().ToArray(), File.ReadAllLines(path));
        Assert.Equal(LongBefore, File.GetLastWriteTimeUtc(path));
        Assert.True(LooksLikePriorUse(path), release);
    }

    // ------------------------------------------------------------------
    // The construction-order property the issue asks for
    // ------------------------------------------------------------------

    [Fact]
    public void TheAnswerIsTheSameWhicheverOrderTheProbeAndTheRewriteRunIn()
    {
        // This is the test the issue asks for, and it is about a property
        // rather than about a call sequence: the probe's answer must not
        // depend on whether it runs before or after the startup rewrite.
        //
        // It goes red if the rewrite stops preserving the timestamp (the
        // "upgrade first" order flips to false), and it goes red if the two
        // are ever re-coupled so that the answer depends on who ran first
        // (the two orders stop agreeing). Either way the failure names the
        // consequence rather than a line number.
        string probeFirst = Aged(SurveyRuleSet.V103StarterSet());
        bool answeredBeforeTheRewrite = LooksLikePriorUse(probeFirst);
        SurveyRuleFile.Outcome firstOutcome = SurveyRuleFile.LoadOrCreate(probeFirst, out _, out _);
        bool answeredAgainAfterTheRewrite = LooksLikePriorUse(probeFirst);

        string rewriteFirst = Aged(SurveyRuleSet.V103StarterSet());
        SurveyRuleFile.Outcome secondOutcome = SurveyRuleFile.LoadOrCreate(rewriteFirst, out _, out _);
        bool answeredAfterTheRewrite = LooksLikePriorUse(rewriteFirst);

        // Both orders really did perform the upgrade, so neither answer is
        // true merely because nothing happened.
        Assert.Equal(SurveyRuleFile.Outcome.Upgraded, firstOutcome);
        Assert.Equal(SurveyRuleFile.Outcome.Upgraded, secondOutcome);

        Assert.True(answeredBeforeTheRewrite, "probe before rewrite: a veteran is a veteran");
        Assert.True(answeredAfterTheRewrite, "probe after rewrite: a veteran is STILL a veteran");
        Assert.True(answeredAgainAfterTheRewrite, "asking twice does not change the answer either");
        Assert.Equal(answeredBeforeTheRewrite, answeredAfterTheRewrite);
    }

    [Fact]
    public void AVeteranWhoseOnlyEvidenceIsTheRulesFile_KeepsHisToolsAcrossTheUpgrade()
    {
        // What the timestamp actually decides, spelled out: a profile with no
        // world sidecars and no saved views, whose one piece of evidence is a
        // rules file older than this session. Present unlocks; None does not.
        string path = Aged(SurveyRuleSet.V103StarterSet());
        Assert.Equal(LegacyEvidence.Present, EvidenceFor(path));

        SurveyRuleFile.LoadOrCreate(path, out _, out _);

        Assert.Equal(LegacyEvidence.Present, EvidenceFor(path));
    }

    // ------------------------------------------------------------------
    // The other direction: a new player must still be new
    // ------------------------------------------------------------------

    [Fact]
    public void FirstRun_WritesTheStarterSetAndStillLooksLikeAFreshInstall()
    {
        // The mistake in the opposite direction, and it has shipped twice
        // (#363, #343): if the starter file this build writes for itself reads
        // as prior use, every brand-new player is classified as a returning one
        // and the #264 introduction can never run again on that profile.
        string path = NewPath();

        SurveyRuleFile.Outcome outcome = SurveyRuleFile.LoadOrCreate(
            path, out SurveyRuleSet rules, out int malformed);

        Assert.Equal(SurveyRuleFile.Outcome.Created, outcome);
        Assert.Equal(0, malformed);
        Assert.Equal(SurveyRuleSet.Default().Serialize().ToArray(), File.ReadAllLines(path));
        Assert.True(rules.TryMatch("BOM_CopperMine01(Clone)", out _));
        Assert.False(LooksLikePriorUse(path), "a file we just created is not somebody's history");
        Assert.Equal(LegacyEvidence.None, EvidenceFor(path));
    }

    [Fact]
    public void AMissingDirectory_IsCreatedOnAFirstRun()
    {
        string path = Path.Combine(_directory, "nested", "deeper", "survey-rules.tsv");

        Assert.Equal(
            SurveyRuleFile.Outcome.Created,
            SurveyRuleFile.LoadOrCreate(path, out _, out _));
        Assert.True(File.Exists(path));
    }

    // ------------------------------------------------------------------
    // Files that must not be touched at all
    // ------------------------------------------------------------------

    [Fact]
    public void AnEditedFile_IsNeitherRewrittenNorRestamped()
    {
        // survey-rules.tsv is the player's own document and the shareable
        // import/export format. An edited file is not read past the comparison
        // and not written, so its timestamp is nobody's business but his.
        SurveyRuleSet edited = SurveyRuleSet.V103StarterSet();
        edited.AddRule(new SurveyRule("mypattern*", "cc:resource", "Resources", 10f, 60f));
        string path = Aged(edited);
        string[] before = File.ReadAllLines(path);

        Assert.Equal(
            SurveyRuleFile.Outcome.Kept,
            SurveyRuleFile.LoadOrCreate(path, out SurveyRuleSet rules, out _));
        Assert.Equal(before, File.ReadAllLines(path));
        Assert.Equal(LongBefore, File.GetLastWriteTimeUtc(path));
        Assert.True(rules.TryMatch("mypattern_thing", out _));
        Assert.True(LooksLikePriorUse(path));
    }

    [Fact]
    public void AFileAlreadyOnTheCurrentSet_IsNeitherRewrittenNorRestamped()
    {
        // Every recognised snapshot is a superseded one. If the current set
        // ever joined that list, this file would be rewritten on every single
        // launch — and under the fix, restamped on every launch too, which
        // would hide the bug rather than fix it.
        string path = Aged(SurveyRuleSet.Default());

        Assert.Equal(
            SurveyRuleFile.Outcome.Kept,
            SurveyRuleFile.LoadOrCreate(path, out _, out _));
        Assert.Equal(SurveyRuleSet.Default().Serialize().ToArray(), File.ReadAllLines(path));
        Assert.Equal(LongBefore, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void MalformedRowsInAPlayersOwnFile_AreStillCountedAndReported()
    {
        string path = NewPath();
        File.WriteAllLines(path, new[]
        {
            "# ConcernedCartographer survey rules v1",
            "raspberrybush*\tcc:resource\tResources\t30\t120",
            "this row is not a rule at all",
        });
        File.SetLastWriteTimeUtc(path, LongBefore);

        Assert.Equal(
            SurveyRuleFile.Outcome.Kept,
            SurveyRuleFile.LoadOrCreate(path, out SurveyRuleSet rules, out int malformed));
        Assert.Equal(1, malformed);
        Assert.Single(rules.Rules);
    }

    // ------------------------------------------------------------------
    // The predicate itself
    // ------------------------------------------------------------------

    [Fact]
    public void AFileThatIsNotThere_IsNotEvidenceOfAnything()
    {
        Assert.False(PreexistingFile.ExistedBefore(NewPath(), SessionStart));
    }

    // PreexistingFile's catch — an unreadable timestamp resolving to "prior
    // use" — is deliberately NOT asserted here. It is pre-existing behaviour
    // moved unchanged out of the probe, and there is no in-process way to make
    // a real filesystem refuse a metadata read on demand: .NET returns false
    // from File.Exists for a malformed path rather than throwing, and
    // GetLastWriteTimeUtc reads metadata, so a share lock does not reach it
    // either. Faking it would need a seam invented for the test, which would
    // prove the seam rather than the product. Said plainly instead of covered
    // by an assertion that cannot fail.
}
