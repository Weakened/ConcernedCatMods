using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>Exercises the real file path: writes, interrupted writes, unreadable
/// files, and files belonging to somebody else. These run against a temporary
/// directory rather than a mock, because the failures worth catching here are
/// filesystem behaviours.</summary>
public sealed class CompanionSidecarStoreTests : IDisposable
{
    private static readonly ProductId Product = new("synthetic-alpha");
    private static readonly QuestId Quest = new("introduction");

    private readonly string _root;

    public CompanionSidecarStoreTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "concerned-companions-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A locked temp file must not fail the suite.
        }
    }

    private static CompanionScope Scope(long world = 100, long character = 200)
    {
        return new CompanionScope(Product, new WorldId(world), new CharacterId(character));
    }

    private CompanionSidecarStore NewStore() => new(_root);

    [Fact]
    public void AFreshCharacterHasNoSidecarAndThatIsNotAnError()
    {
        CompanionSidecarStore.LoadReport report = NewStore().Load(Scope());

        Assert.Equal(SidecarLoadOutcome.Missing, report.Outcome);
        Assert.Null(report.Notice);
        Assert.False(report.Sidecar.HasAnyCompletedQuest());
        Assert.Equal(UnlockReason.NotUnlocked, report.Sidecar.GrantedReason);
    }

    [Fact]
    public void ProgressSurvivesASaveAndReload()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);

        Assert.True(store.Save(sidecar).Saved);

        CompanionSidecar reloaded = NewStore().Load(Scope()).Sidecar;
        Assert.True(reloaded.TryGetQuest(Quest, out CompanionQuestRecord record));
        Assert.Equal(QuestState.Recruited, record.State);
        Assert.True(reloaded.HasAnyCompletedQuest());
    }

    [Fact]
    public void AnUnchangedSidecarIsNotRewritten()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);
        Assert.True(store.Save(sidecar).Saved);

        Assert.False(store.Save(sidecar).Saved);
        Assert.True(store.Save(sidecar, force: true).Saved);
    }

    [Fact]
    public void DataIsIsolatedPerWorldAndCharacter()
    {
        CompanionSidecarStore store = NewStore();

        CompanionSidecar first = store.Load(Scope(world: 1, character: 1)).Sidecar;
        first.Apply(Quest, QuestTransition.Welcome);
        store.Save(first);

        foreach (CompanionScope other in new[]
                 {
                     Scope(world: 2, character: 1),
                     Scope(world: 1, character: 2),
                 })
        {
            CompanionSidecarStore.LoadReport report = store.Load(other);
            Assert.Equal(SidecarLoadOutcome.Missing, report.Outcome);
            Assert.False(report.Sidecar.HasAnyCompletedQuest());
        }
    }

    [Fact]
    public void AnInterruptedWriteLeavesTheOldFileIntact()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar first = store.Load(Scope()).Sidecar;
        first.Apply(Quest, QuestTransition.Welcome);
        store.Save(first);

        string path = store.ResolvePath(Scope());
        string original = File.ReadAllText(path);

        // A crash mid-save leaves the temporary file behind and the live file
        // untouched, because the live file is only ever replaced whole.
        File.WriteAllText(path + ".tmp", "half-written garbage");

        CompanionSidecarStore.LoadReport report = NewStore().Load(Scope());
        Assert.Equal(SidecarLoadOutcome.Loaded, report.Outcome);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.True(report.Sidecar.HasAnyCompletedQuest());
    }

    [Fact]
    public void AnUnreadableFileIsQuarantinedRatherThanDeleted()
    {
        CompanionSidecarStore store = NewStore();
        string path = store.ResolvePath(Scope());
        File.WriteAllText(path, "not a sidecar at all");

        CompanionSidecarStore.LoadReport report = store.Load(Scope());

        Assert.Equal(SidecarLoadOutcome.Corrupt, report.Outcome);
        Assert.NotNull(report.QuarantinePath);
        Assert.True(File.Exists(report.QuarantinePath!));
        Assert.Equal("not a sidecar at all", File.ReadAllText(report.QuarantinePath!).Trim());
        Assert.False(File.Exists(path));
        Assert.NotNull(report.Notice);
    }

    [Fact]
    public void RecoveryAfterQuarantineCanWriteAFreshFile()
    {
        CompanionSidecarStore store = NewStore();
        File.WriteAllText(store.ResolvePath(Scope()), "garbage");

        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.RecordUnlockGrant(UnlockReason.DataUnreadable);

        Assert.True(store.Save(sidecar).Saved);
        Assert.Equal(
            UnlockReason.DataUnreadable, NewStore().Load(Scope()).Sidecar.GrantedReason);
    }

    [Fact]
    public void RepeatedCorruptionDoesNotOverwriteAnEarlierQuarantine()
    {
        CompanionSidecarStore store = NewStore();
        string path = store.ResolvePath(Scope());

        File.WriteAllText(path, "first failure");
        string? firstQuarantine = store.Load(Scope()).QuarantinePath;

        File.WriteAllText(path, "second failure");
        string? secondQuarantine = store.Load(Scope()).QuarantinePath;

        Assert.NotNull(firstQuarantine);
        Assert.NotNull(secondQuarantine);
        Assert.NotEqual(firstQuarantine, secondQuarantine);
        Assert.Equal("first failure", File.ReadAllText(firstQuarantine!).Trim());
        Assert.Equal("second failure", File.ReadAllText(secondQuarantine!).Trim());
    }

    [Fact]
    public void AnotherCharactersFileIsNeverOverwritten()
    {
        CompanionSidecarStore store = NewStore();

        // Write real data for one character, then rename it onto another
        // character's path - what copying a profile folder effectively does.
        CompanionSidecar theirs = store.Load(Scope(world: 1, character: 1)).Sidecar;
        theirs.Apply(Quest, QuestTransition.Welcome);
        store.Save(theirs);

        string theirPath = store.ResolvePath(Scope(world: 1, character: 1));
        string myPath = store.ResolvePath(Scope(world: 1, character: 2));
        File.Move(theirPath, myPath);
        string before = File.ReadAllText(myPath);

        CompanionSidecarStore.LoadReport report = store.Load(Scope(world: 1, character: 2));

        Assert.Equal(SidecarLoadOutcome.ScopeMismatch, report.Outcome);
        Assert.True(report.Sidecar.IsReadOnly);
        Assert.NotNull(report.Notice);

        report.Sidecar.Apply(Quest, QuestTransition.Welcome);
        CompanionSidecarStore.SaveReport save = store.Save(report.Sidecar);

        Assert.False(save.Saved);
        Assert.NotNull(save.Notice);
        Assert.Equal(before, File.ReadAllText(myPath));
    }

    [Fact]
    public void ANewerSchemaFileIsNeverOverwritten()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);
        store.Save(sidecar);

        string path = store.ResolvePath(Scope());
        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("s\t", StringComparison.Ordinal))
            {
                lines[index] = "s\t" + (CompanionSidecarCodec.SchemaVersion + 1);
            }
        }

        File.WriteAllLines(path, lines);
        string before = File.ReadAllText(path);

        CompanionSidecarStore.LoadReport report = store.Load(Scope());
        Assert.Equal(SidecarLoadOutcome.UnsupportedSchema, report.Outcome);

        report.Sidecar.Apply(Quest, QuestTransition.Welcome);
        Assert.False(store.Save(report.Sidecar).Saved);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void SidecarFilesAreListedForMigrationProbes()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);
        store.Save(sidecar);

        Assert.Single(store.ListSidecarFiles());
        Assert.Empty(new CompanionSidecarStore(Path.Combine(_root, "nope")).ListSidecarFiles());
    }

    [Fact]
    public void SavingCreatesTheDirectoryWhenItIsMissing()
    {
        var store = new CompanionSidecarStore(Path.Combine(_root, "nested", "deeper"));
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);

        Assert.True(store.Save(sidecar).Saved);
        Assert.Single(store.ListSidecarFiles());
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehindAfterASuccessfulSave()
    {
        CompanionSidecarStore store = NewStore();
        CompanionSidecar sidecar = store.Load(Scope()).Sidecar;
        sidecar.Apply(Quest, QuestTransition.Welcome);
        store.Save(sidecar);
        sidecar.Apply(Quest, QuestTransition.Skip);
        store.Save(sidecar, force: true);

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
}
