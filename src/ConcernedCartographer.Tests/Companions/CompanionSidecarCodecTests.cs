using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace ConcernedCartographer.Tests.Companions;

public class CompanionSidecarCodecTests
{
    private static readonly ProductId Product = new("synthetic-alpha");
    private static readonly QuestId Quest = new("introduction");

    private static CompanionScope Scope(long world = 100, long character = 200)
    {
        return new CompanionScope(Product, new WorldId(world), new CharacterId(character));
    }

    private static List<string> Serialize(CompanionSidecar sidecar)
    {
        return CompanionSidecarCodec.Serialize(sidecar).ToList();
    }

    [Fact]
    public void RoundTripsQuestProgressAndGrant()
    {
        var original = new CompanionSidecar(Scope());
        original.Apply(Quest, QuestTransition.Collect);
        original.Apply(Quest, QuestTransition.Welcome);
        original.MarkPresentationRetired(Quest);
        original.RecordUnlockGrant(UnlockReason.ExistingUserData);

        CompanionSidecarCodec.ParseResult parsed =
            CompanionSidecarCodec.Parse(Serialize(original), Scope());

        Assert.Equal(SidecarLoadOutcome.Loaded, parsed.Outcome);
        Assert.Equal(UnlockReason.ExistingUserData, parsed.Sidecar.GrantedReason);
        Assert.True(parsed.Sidecar.TryGetQuest(Quest, out CompanionQuestRecord record));
        Assert.Equal(QuestState.Recruited, record.State);
        Assert.True(record.PresentationRetired);
        Assert.Equal(original.Quests[0].Revision, record.Revision);
    }

    [Fact]
    public void MissingInputIsMissingRatherThanCorrupt()
    {
        Assert.Equal(SidecarLoadOutcome.Missing, CompanionSidecarCodec.Parse(null, Scope()).Outcome);
        Assert.Equal(
            SidecarLoadOutcome.Missing,
            CompanionSidecarCodec.Parse(Array.Empty<string>(), Scope()).Outcome);
        Assert.Equal(
            SidecarLoadOutcome.Missing,
            CompanionSidecarCodec.Parse(new[] { "", "   " }, Scope()).Outcome);
    }

    [Fact]
    public void GarbageWithoutASchemaRowIsCorrupt()
    {
        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(
            new[] { "this is not a sidecar", "neither is this" }, Scope());

        Assert.Equal(SidecarLoadOutcome.Corrupt, parsed.Outcome);
    }

    [Fact]
    public void ATruncatedFileIsCorruptRatherThanEmptyProgress()
    {
        // A write cut short after the header must not read back as "this
        // character has never played", which would re-run the introduction.
        List<string> full = Serialize(BuildRecruited());
        CompanionSidecarCodec.ParseResult parsed =
            CompanionSidecarCodec.Parse(full.Take(1), Scope());

        Assert.Equal(SidecarLoadOutcome.Corrupt, parsed.Outcome);
    }

    [Theory]
    [InlineData(101, 200)]
    [InlineData(100, 201)]
    public void DataFromAnotherWorldOrCharacterIsRefusedNotAdopted(long world, long character)
    {
        List<string> lines = Serialize(BuildRecruited());

        CompanionSidecarCodec.ParseResult parsed =
            CompanionSidecarCodec.Parse(lines, Scope(world, character));

        Assert.Equal(SidecarLoadOutcome.ScopeMismatch, parsed.Outcome);
        Assert.False(parsed.Sidecar.TryGetQuest(Quest, out _));
    }

    [Fact]
    public void DataFromAnotherProductIsRefused()
    {
        List<string> lines = Serialize(BuildRecruited());
        var otherProduct = new CompanionScope(
            new ProductId("synthetic-beta"), new WorldId(100), new CharacterId(200));

        Assert.Equal(
            SidecarLoadOutcome.ScopeMismatch,
            CompanionSidecarCodec.Parse(lines, otherProduct).Outcome);
    }

    [Fact]
    public void ANewerSchemaIsReadOnlyAndNeverParsed()
    {
        List<string> lines = Serialize(BuildRecruited());
        int schemaIndex = lines.FindIndex(line => line.StartsWith("s\t", StringComparison.Ordinal));
        lines[schemaIndex] = "s\t" + (CompanionSidecarCodec.SchemaVersion + 1);

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());

        Assert.Equal(SidecarLoadOutcome.UnsupportedSchema, parsed.Outcome);
        Assert.True(parsed.Sidecar.IsReadOnly);
        Assert.True(parsed.Sidecar.HasForwardData);
    }

    [Fact]
    public void MalformedRowsAreSkippedWithoutLosingValidOnes()
    {
        List<string> lines = Serialize(BuildRecruited());
        lines.Add("q\tsecond-quest\tnot-a-number\t0\t0");
        lines.Add("q");

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());

        Assert.Equal(SidecarLoadOutcome.LoadedWithSkippedRows, parsed.Outcome);
        Assert.Equal(2, parsed.SkippedRows);
        Assert.True(parsed.Sidecar.TryGetQuest(Quest, out CompanionQuestRecord record));
        Assert.Equal(QuestState.Recruited, record.State);
    }

    [Fact]
    public void MalformedRowsAreCarriedButAreNotTreatedAsEvidence()
    {
        // Garbage must not be mistaken for "a newer build wrote this", which
        // would hand out an unlock on the strength of a corrupted byte.
        List<string> lines = Serialize(new CompanionSidecar(Scope()));
        lines.Add("q\tbroken\tx\ty\tz");

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());

        Assert.Equal(1, parsed.SkippedRows);
        Assert.False(parsed.Sidecar.HasForwardData);
        Assert.Contains("q\tbroken\tx\ty\tz", parsed.Sidecar.ForwardLines);
    }

    [Fact]
    public void AQuestStageFromANewerBuildIsCarriedAndCountsAsForwardData()
    {
        List<string> lines = Serialize(new CompanionSidecar(Scope()));
        lines.Add("q\tintroduction\t99\t3\t1");

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());

        Assert.True(parsed.Sidecar.HasForwardData);
        Assert.False(parsed.Sidecar.TryGetQuest(Quest, out _));
        Assert.Contains("q\tintroduction\t99\t3\t1", parsed.Sidecar.ForwardLines);
    }

    [Fact]
    public void UnknownRowKindsSurviveARoundTripThroughThisBuild()
    {
        // Running an older build once must not quietly delete what a newer one
        // recorded.
        List<string> lines = Serialize(BuildRecruited());
        lines.Add("z\tsomething-a-later-build-added\t1");

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());
        List<string> rewritten = Serialize(parsed.Sidecar);

        Assert.Contains("z\tsomething-a-later-build-added\t1", rewritten);
        Assert.True(parsed.Sidecar.HasForwardData);
    }

    [Fact]
    public void UnknownTrailingQuestFieldsSurviveARoundTrip()
    {
        List<string> lines = Serialize(BuildRecruited());
        int questIndex = lines.FindIndex(line => line.StartsWith("q\t", StringComparison.Ordinal));
        lines[questIndex] = lines[questIndex] + "\tfuture-field";

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());
        List<string> rewritten = Serialize(parsed.Sidecar);

        Assert.Contains(rewritten, line => line.EndsWith("\tfuture-field", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnlockRowFromANewerBuildIsNotOverwritten()
    {
        List<string> lines = Serialize(new CompanionSidecar(Scope()));
        lines.Add("u\t77");

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, Scope());

        Assert.True(parsed.Sidecar.HasCarriedUnlockRow);
        Assert.True(parsed.Sidecar.HasForwardData);
        Assert.False(parsed.Sidecar.RecordUnlockGrant(UnlockReason.ExistingUserData));

        List<string> rewritten = Serialize(parsed.Sidecar);
        Assert.Single(rewritten, line => line.StartsWith("u\t", StringComparison.Ordinal));
        Assert.Contains("u\t77", rewritten);
    }

    [Fact]
    public void GrantsAreMonotonicOnceRecorded()
    {
        var sidecar = new CompanionSidecar(Scope());

        Assert.True(sidecar.RecordUnlockGrant(UnlockReason.AmbiguousLegacyEvidence));
        Assert.False(sidecar.RecordUnlockGrant(UnlockReason.ToolsOnlyPreference));
        Assert.Equal(UnlockReason.AmbiguousLegacyEvidence, sidecar.GrantedReason);
        Assert.False(sidecar.RecordUnlockGrant(UnlockReason.NotUnlocked));
    }

    [Fact]
    public void SerializedOutputIsStableAcrossRoundTrips()
    {
        CompanionSidecar original = BuildRecruited();
        List<string> first = Serialize(original);
        List<string> second = Serialize(CompanionSidecarCodec.Parse(first, Scope()).Sidecar);

        Assert.Equal(first, second);
    }

    private static CompanionSidecar BuildRecruited()
    {
        var sidecar = new CompanionSidecar(Scope());
        sidecar.Apply(Quest, QuestTransition.Discover);
        sidecar.Apply(Quest, QuestTransition.Collect);
        sidecar.Apply(Quest, QuestTransition.BeginIntroduction);
        sidecar.Apply(Quest, QuestTransition.Welcome);
        return sidecar;
    }
}
