using TheConcernedCat.Settlement.Placement;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-003: the game-free half of placement authority.
///
/// The leaf's go/no-go is a single sentence — *every check that cannot be
/// faithfully reimplemented must become a refusal, and none may be silently
/// skipped*. That is a property of the decision structure, not of any one
/// check, so it is proved here rather than argued in a comment.
///
/// These tests do <b>not</b> prove that any individual check is faithful to
/// vanilla's. That is the adapter's job and it is owed live evidence.</summary>
public sealed class PlacementGateTests
{
    private static readonly PlacementCheck[] AllChecks =
        (PlacementCheck[])Enum.GetValues(typeof(PlacementCheck));

    private static PlacementAnswers AllPassing()
    {
        PlacementAnswers answers = new();
        foreach (PlacementCheck check in AllChecks)
        {
            answers.Record(check, CheckOutcome.Passed);
        }

        return answers;
    }

    [Fact]
    public void EveryCheckPassing_Allows()
    {
        Assert.True(AllPassing().Evaluate().IsAllowed);
    }

    [Fact]
    public void AFreshGate_RefusesEverything()
    {
        // Nothing recorded at all. The default answer must be "could not
        // establish", never "fine" -- this is the whole leaf in one assertion.
        PlacementVerdict verdict = new PlacementAnswers().Evaluate();

        Assert.False(verdict.IsAllowed);
        Assert.Equal(PlacementRefusal.CouldNotEstablish, verdict.Refusal);
    }

    [Fact]
    public void OmittingAnySingleCheck_Refuses_NamingThatCheck()
    {
        // The "none is skipped" proof: for every check in turn, answer all the
        // others and leave that one unmentioned. Each must refuse, and must
        // refuse *for the omitted check*, so a future member added to the enum
        // cannot be forgotten silently.
        foreach (PlacementCheck omitted in AllChecks)
        {
            PlacementAnswers answers = new();
            foreach (PlacementCheck check in AllChecks)
            {
                if (check != omitted)
                {
                    answers.Record(check, CheckOutcome.Passed);
                }
            }

            PlacementVerdict verdict = answers.Evaluate();

            Assert.False(verdict.IsAllowed);
            Assert.Equal(omitted, verdict.Check);
            Assert.Equal(PlacementRefusal.CouldNotEstablish, verdict.Refusal);
        }
    }

    [Fact]
    public void AnUnavailableCheck_RefusesAsUnestablished_NotAsDenied()
    {
        // "The ward said no" and "we could not ask about the ward" are
        // different things to tell a player, and only one of them is worth
        // retrying after moving.
        PlacementAnswers answers = AllPassing();
        answers.Record(PlacementCheck.Ward, CheckOutcome.Unavailable);

        PlacementVerdict verdict = answers.Evaluate();

        Assert.False(verdict.IsAllowed);
        Assert.Equal(PlacementCheck.Ward, verdict.Check);
        Assert.Equal(PlacementRefusal.CouldNotEstablish, verdict.Refusal);
    }

    [Fact]
    public void ADefiniteDenial_OutranksAnUnestablishedCheck()
    {
        // Reporting "could not establish the cost" for a piece that is plainly
        // inside somebody's ward would read as a bug the player could retry
        // past. The definite answer wins.
        PlacementAnswers answers = AllPassing();
        answers.Record(PlacementCheck.Cost, CheckOutcome.Unavailable);
        answers.Record(PlacementCheck.Ward, CheckOutcome.Failed);

        PlacementVerdict verdict = answers.Evaluate();

        Assert.Equal(PlacementCheck.Ward, verdict.Check);
        Assert.Equal(PlacementRefusal.Denied, verdict.Refusal);
    }

    [Fact]
    public void AmongDenials_TheReportOrderDecides()
    {
        PlacementAnswers answers = AllPassing();
        answers.Record(PlacementCheck.BuildStation, CheckOutcome.Failed);
        answers.Record(PlacementCheck.Ward, CheckOutcome.Failed);

        // Ward before workbench: being somewhere you are not allowed to build
        // matters more than what you would have needed if you were.
        Assert.Equal(PlacementCheck.Ward, answers.Evaluate().Check);
    }

    [Theory]
    [InlineData((int)CheckOutcome.Passed, (int)CheckOutcome.Failed, (int)CheckOutcome.Failed)]
    [InlineData((int)CheckOutcome.Failed, (int)CheckOutcome.Passed, (int)CheckOutcome.Failed)]
    [InlineData((int)CheckOutcome.Passed, (int)CheckOutcome.Unavailable, (int)CheckOutcome.Unavailable)]
    [InlineData((int)CheckOutcome.Unavailable, (int)CheckOutcome.Passed, (int)CheckOutcome.Unavailable)]
    [InlineData((int)CheckOutcome.Unavailable, (int)CheckOutcome.Failed, (int)CheckOutcome.Failed)]
    [InlineData((int)CheckOutcome.Passed, (int)CheckOutcome.Passed, (int)CheckOutcome.Passed)]
    public void RecordingTwice_KeepsTheWorseAnswer(int first, int second, int expected)
    {
        // A second opinion may only ever tighten a placement. Otherwise a later
        // permissive answer could quietly overwrite an earlier refusal.
        PlacementAnswers answers = new();
        answers.Record(PlacementCheck.Ground, (CheckOutcome)first);
        answers.Record(PlacementCheck.Ground, (CheckOutcome)second);

        Assert.Equal((CheckOutcome)expected, answers.Outcome(PlacementCheck.Ground));
    }

    [Fact]
    public void TheBooleanOverload_NeverProducesUnavailable()
    {
        // bool is for checks that were actually asked. "Could not ask" has to
        // be said explicitly, so it can never be produced by accident from a
        // false.
        PlacementAnswers answers = new();
        answers.Record(PlacementCheck.Biome, passed: false);
        Assert.Equal(CheckOutcome.Failed, answers.Outcome(PlacementCheck.Biome));

        answers.Record(PlacementCheck.Clearance, passed: true);
        Assert.Equal(CheckOutcome.Passed, answers.Outcome(PlacementCheck.Clearance));
    }

    [Fact]
    public void UnavailableIsZero_SoAForgottenAnswerFailsClosed()
    {
        // Pinned as a value, not a convention: if somebody reorders the enum so
        // that Passed becomes the default, every un-answered check in the
        // repository silently starts allowing placements.
        Assert.Equal(0, (int)CheckOutcome.Unavailable);
        Assert.Equal(default, CheckOutcome.Unavailable);
    }

    [Fact]
    public void AnUnknownCheck_IsRejectedRatherThanIgnored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlacementAnswers().Record((PlacementCheck)999, CheckOutcome.Passed));
    }

    [Fact]
    public void AnAllowedVerdictCarriesNoRefusal()
    {
        Assert.Equal(PlacementRefusal.None, PlacementVerdict.Allow().Refusal);
        Assert.Equal(
            PlacementRefusal.Denied,
            PlacementVerdict.Refuse(PlacementCheck.Ward, PlacementRefusal.Denied).Refusal);
    }
}
