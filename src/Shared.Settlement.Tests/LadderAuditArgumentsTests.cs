using TheConcernedCat.ConcernedForeman.Domain.Ladders;

namespace Shared.Settlement.Tests;

/// <summary>CF-LAD-004: `cf_ladders` reads the arguments the console actually
/// hands it.
///
/// This is the test that was missing. The audit read its subcommand from
/// `args[1]` and its radius from `args[2]`, one place too far along, because
/// the console strips the command's own name before calling `Run`. Nothing
/// threw and nothing said so: `cf_ladders snaps` fell through to `list` and
/// printed a credible prefab table with no snap-point section, which is
/// indistinguishable from "this piece has no snap points" - the confidently
/// wrong answer to the question §3 L3 exists to ask. `cf_ladders here 24`
/// answered "Unknown subcommand".
///
/// So these hold the two positions by the exact commands the LADDERS.md
/// procedure tells the lead to type.</summary>
public sealed class LadderAuditArgumentsTests
{
    // --- the subcommand is the first argument -----------------------------

    [Theory]
    [InlineData(LadderAuditArguments.List)]
    [InlineData(LadderAuditArguments.Here)]
    [InlineData(LadderAuditArguments.Snaps)]
    public void TheSubcommandIsTheFirstArgument(string typed)
    {
        // What the registrar passes for "cf_ladders <typed>": the name is
        // already gone.
        Assert.Equal(typed, LadderAuditArguments.Subcommand(new[] { typed }));
        Assert.True(LadderAuditArguments.IsKnown(LadderAuditArguments.Subcommand(new[] { typed })));
    }

    [Fact]
    public void SnapsIsSnapsAndNotList()
    {
        // The exact defect, as step 2 of the procedure types it. Reading
        // args[1] made this "list", and a prefab table is not an answer about
        // snap points.
        Assert.Equal(LadderAuditArguments.Snaps, LadderAuditArguments.Subcommand(new[] { "snaps" }));
        Assert.NotEqual(LadderAuditArguments.Default, LadderAuditArguments.Subcommand(new[] { "snaps" }));
    }

    [Theory]
    [InlineData("SNAPS")]
    [InlineData("Here")]
    [InlineData("LiSt")]
    public void CapitalsAreNotATypo(string typed)
    {
        Assert.True(LadderAuditArguments.IsKnown(LadderAuditArguments.Subcommand(new[] { typed })));
    }

    [Fact]
    public void NothingTypedIsTheListing()
    {
        // `list` needs no ladder nearby, so it is the one that always has
        // something to say.
        Assert.Equal(LadderAuditArguments.List, LadderAuditArguments.Subcommand(Array.Empty<string>()));
        Assert.Equal(LadderAuditArguments.List, LadderAuditArguments.Subcommand(null));
        Assert.Equal(LadderAuditArguments.List, LadderAuditArguments.Subcommand(new[] { "   " }));
    }

    // --- an unknown subcommand is refused, never quietly answered ----------

    [Theory]
    [InlineData("snap")]
    [InlineData("snapz")]
    [InlineData("nearby")]
    [InlineData("24")]
    [InlineData("cf_ladders")]
    public void AnUnknownSubcommandIsRefusedRatherThanTreatedAsList(string typed)
    {
        string subcommand = LadderAuditArguments.Subcommand(new[] { typed });

        // It comes back as it stands, so the audit refuses it by name. Had it
        // been corrected to the default, a typo would have printed a report
        // about something else and looked like an answer.
        Assert.Equal(typed.ToLowerInvariant(), subcommand);
        Assert.False(LadderAuditArguments.IsKnown(subcommand));
    }

    [Fact]
    public void TheCommandNameItselfIsNotASubcommand()
    {
        // If the registrar ever stopped stripping the name, "cf_ladders" would
        // arrive as the first argument. That must read as a refusal, not as a
        // listing, so the breakage is visible the first time somebody runs it.
        Assert.False(LadderAuditArguments.IsKnown(
            LadderAuditArguments.Subcommand(new[] { "cf_ladders", "snaps" })));
    }

    // --- the radius is the second argument --------------------------------

    [Fact]
    public void TheRadiusIsTheSecondArgument()
    {
        // Step 6 of the procedure, typed exactly as it is written there.
        Assert.Equal(24f, LadderAuditArguments.Radius(new[] { "here", "24" }));
        Assert.Equal(24f, LadderAuditArguments.Radius(new[] { "snaps", "24" }));
    }

    [Fact]
    public void TheRadiusIsNotReadFromTheOldPosition()
    {
        // The defect's other half. Fixing only the subcommand index would have
        // left this reading args[2], so the radius a person typed was ignored
        // and every report claimed 12 m while saying nothing was wrong.
        Assert.Equal(24f, LadderAuditArguments.Radius(new[] { "here", "24", "48" }));
        Assert.Equal(LadderAuditArguments.DefaultRadius, LadderAuditArguments.Radius(new[] { "here" }));
    }

    [Fact]
    public void NoRadiusIsTwelveMetres()
    {
        Assert.Equal(12f, LadderAuditArguments.DefaultRadius);
        Assert.Equal(LadderAuditArguments.DefaultRadius, LadderAuditArguments.Radius(Array.Empty<string>()));
        Assert.Equal(LadderAuditArguments.DefaultRadius, LadderAuditArguments.Radius(null));
    }

    [Theory]
    [InlineData("twelve")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("65")]
    [InlineData("NaN")]
    [InlineData("1e30")]
    public void ASillyRadiusMeasuresTheDefaultRatherThanRefusing(string typed)
    {
        // A read-only audit run by hand at a console. Refusing to measure
        // anything because somebody typed a word would help nobody, and the
        // report's first line states the radius it actually used.
        Assert.Equal(LadderAuditArguments.DefaultRadius, LadderAuditArguments.Radius(new[] { "here", typed }));
    }

    [Fact]
    public void TheLargestRadiusIsAccepted()
    {
        Assert.Equal(64f, LadderAuditArguments.MaximumRadius);
        Assert.Equal(
            LadderAuditArguments.MaximumRadius,
            LadderAuditArguments.Radius(new[] { "here", "64" }));
    }

    [Fact]
    public void ADecimalRadiusIsReadTheSameInEveryLocale()
    {
        // The console is invariant; a machine whose locale uses a comma must
        // not read 7.5 as 75 or as nothing at all.
        Assert.Equal(7.5f, LadderAuditArguments.Radius(new[] { "snaps", "7.5" }));
        Assert.Equal(LadderAuditArguments.DefaultRadius, LadderAuditArguments.Radius(new[] { "snaps", "7,5" }));
    }
}
