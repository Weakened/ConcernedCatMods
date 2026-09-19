using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Scope;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Meeting him, hiring him, and the record that remembers both.
/// </summary>
public sealed class RecruitmentTests
{
    private static IntroductionFacts Ready =>
        new IntroductionFacts(authorised: true, hasSettlementArea: true, recordWritable: true);

    [Fact]
    public void The_introduction_advances_one_step_at_a_time_and_each_one_is_idempotent()
    {
        var introduction = new StewardIntroduction();
        Assert.Equal(IntroductionStage.Unmet, introduction.Stage);

        Assert.True(introduction.Advance(IntroductionStage.Noticed, Ready).Changed);
        Assert.Equal(
            IntroductionOutcome.AlreadyThere,
            introduction.Advance(IntroductionStage.Noticed, Ready).Outcome);

        Assert.True(introduction.Advance(IntroductionStage.Offered, Ready).Changed);
        Assert.True(introduction.Advance(IntroductionStage.Engaged, Ready).Changed);
        Assert.True(introduction.IsEngaged);

        // Running the whole thing again changes nothing at all.
        int revision = introduction.Revision;
        foreach (IntroductionStage stage in new[]
        {
            IntroductionStage.Noticed, IntroductionStage.Offered, IntroductionStage.Engaged,
        })
        {
            Assert.Equal(
                IntroductionOutcome.AlreadyThere, introduction.Advance(stage, Ready).Outcome);
        }

        Assert.Equal(revision, introduction.Revision);
    }

    [Fact]
    public void Skipping_a_step_is_refused_rather_than_taken_as_a_shortcut()
    {
        var introduction = new StewardIntroduction();

        IntroductionResult result = introduction.Advance(IntroductionStage.Engaged, Ready);

        Assert.Equal(IntroductionOutcome.Refused, result.Outcome);
        Assert.Equal(IntroductionRefusal.OutOfOrder, result.Refusal);
        Assert.Equal(IntroductionStage.Unmet, introduction.Stage);
    }

    [Theory]
    [InlineData(false, true, true, (int)IntroductionRefusal.NotAuthorised)]
    [InlineData(true, false, true, (int)IntroductionRefusal.NoSettlementArea)]
    [InlineData(true, true, false, (int)IntroductionRefusal.RecordNotWritable)]
    public void Recruitment_fails_closed_on_every_missing_precondition(
        bool authorised, bool hasArea, bool writable, int expected)
    {
        var introduction = new StewardIntroduction();

        IntroductionResult result = introduction.Advance(
            IntroductionStage.Noticed,
            new IntroductionFacts(authorised, hasArea, writable));

        Assert.Equal(IntroductionOutcome.Refused, result.Outcome);
        Assert.Equal((IntroductionRefusal)expected, result.Refusal);
        Assert.Equal(IntroductionStage.Unmet, introduction.Stage);
        Assert.False(introduction.IsEngaged);
    }

    [Fact]
    public void Dismissing_him_lowers_the_stage_and_never_the_high_water_mark()
    {
        var introduction = new StewardIntroduction();
        introduction.Advance(IntroductionStage.Noticed, Ready);
        introduction.Advance(IntroductionStage.Offered, Ready);
        introduction.Advance(IntroductionStage.Engaged, Ready);

        Assert.True(introduction.Dismiss());

        Assert.Equal(IntroductionStage.Offered, introduction.Stage);
        Assert.Equal(IntroductionStage.Engaged, introduction.FurthestReached);
        Assert.True(introduction.HasEverEngaged);
        Assert.False(introduction.IsEngaged);

        // Dismissing twice is dismissing once.
        Assert.False(introduction.Dismiss());
    }

    [Fact]
    public void Re_hiring_somebody_you_let_go_does_not_replay_the_introduction()
    {
        var introduction = new StewardIntroduction();
        introduction.Advance(IntroductionStage.Noticed, Ready);
        introduction.Advance(IntroductionStage.Offered, Ready);
        introduction.Advance(IntroductionStage.Engaged, Ready);
        introduction.Dismiss();

        // One step, not three: he is already known and already offered.
        Assert.True(introduction.Advance(IntroductionStage.Engaged, Ready).Changed);
        Assert.True(introduction.IsEngaged);
    }

    [Fact]
    public void A_record_that_claims_more_than_its_own_high_water_mark_is_repaired_upwards()
    {
        var introduction = new StewardIntroduction();

        // A damaged file. The repair raises the mark; it never lowers the
        // stage, which is the one direction this type exists to prevent.
        introduction.Restore(IntroductionStage.Engaged, IntroductionStage.Noticed);

        Assert.Equal(IntroductionStage.Engaged, introduction.Stage);
        Assert.Equal(IntroductionStage.Engaged, introduction.FurthestReached);
    }

    [Fact]
    public void Loading_is_not_advancing_so_no_world_check_is_re_run()
    {
        var introduction = new StewardIntroduction();

        // Nothing here passes any IntroductionFacts: a settlement un-marked for
        // five minutes must not quietly un-hire him at the next load.
        introduction.Restore(IntroductionStage.Engaged, IntroductionStage.Engaged);

        Assert.True(introduction.IsEngaged);
    }

    /// <summary>The owner named her in #382, and the whole point of keeping the
    /// name out of the identity was that naming her would cost one string.
    ///
    /// This test used to assert that nothing she says carries a name. It now
    /// asserts the thing that actually mattered: that the name moved and the
    /// identity did not. A saved body, a record row and a capability handshake
    /// written by the build before she was named are found by exactly the same
    /// three values they always were.</summary>
    [Fact]
    public void She_has_a_name_now_and_none_of_the_identity_moved_with_it()
    {
        string[] lines =
        {
            StewardSentences.ForStage(IntroductionStage.Noticed),
            StewardSentences.ForStage(IntroductionStage.Offered),
            StewardSentences.ForStage(IntroductionStage.Engaged),
            StewardSentences.WelcomeBack(),
            StewardSentences.Dismissed(),
        };

        foreach (string line in lines)
        {
            // Still nobody else's name, and still nothing that reads as a
            // placeholder.
            Assert.DoesNotContain("Hulgi", line);
            Assert.DoesNotContain("Thorstein", line);
            Assert.DoesNotContain("Gunnar", line);
        }

        Assert.Equal("Sunniva", StewardRole.DisplayNameFallback);

        // Unchanged, and this is the half with a cost: renaming any of these
        // orphans every saved body and every record that used the old one.
        Assert.Equal("steward", StewardRole.RoleKey);
        Assert.Equal("steward/steward", StewardRole.Worker.Value);
        Assert.Equal("CS_Steward", StewardRole.BodyPrefabName);
        Assert.Equal("tcc.steward.", StewardRole.BodyKeyPrefix);
    }

    /// <summary>Every sentence goes through the one constant, so the next rename
    /// costs the same one edit this one did.</summary>
    [Fact]
    public void Her_name_is_spelled_in_exactly_one_place()
    {
        foreach (string line in new[]
        {
            StewardSentences.ForStage(IntroductionStage.Noticed),
            StewardSentences.ForStage(IntroductionStage.Engaged),
            StewardSentences.WelcomeBack(),
            StewardSentences.Dismissed(),
        })
        {
            Assert.Contains(StewardRole.DisplayNameFallbackCapitalised, line);
        }
    }
}

/// <summary>Where he may work and what he may take from, and the two ways both
/// stop meaning anything.</summary>
public sealed class ScopeTests
{
    [Fact]
    public void Nothing_marked_means_nothing_in_scope_never_everywhere()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");

        ScopeSnapshot snapshot = scope.Resolve();

        Assert.Equal(ScopeVerdict.NoSettlementArea, snapshot.Verdict);
        Assert.False(snapshot.IsReady);
        Assert.Null(snapshot.Settlement);
        Assert.Contains("cs_steward area", snapshot.Describe());
    }

    [Fact]
    public void A_settlement_without_a_chest_is_not_ready_and_says_which_one_to_mark()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());

        Assert.Equal(ScopeVerdict.NoSupplyDepot, scope.Resolve().Verdict);
        Assert.Contains("cs_steward depot", scope.Resolve().Describe());
    }

    [Fact]
    public void A_chest_marked_in_a_previous_session_resolves_to_nothing()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());
        scope.Designate(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-37"), new AlwaysGranted());
        Assert.True(scope.Resolve().IsReady);

        // The world reloads. Every placed object is handed a fresh id in load
        // order, so "chest-37" now names some OTHER chest, densely and likely.
        scope.UseIdentityEpoch("epoch-2");

        Assert.Equal(ScopeVerdict.StaleDepotIdentity, scope.Resolve().Verdict);
        Assert.False(scope.IsSupplyDepot("chest-37"));
        Assert.Contains("cs_steward depot", scope.Resolve().Describe());
    }

    [Fact]
    public void A_stale_chest_can_be_replaced_without_the_settlement_being_cleared()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());
        scope.Designate(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-37"), new AlwaysGranted());
        scope.UseIdentityEpoch("epoch-2");

        DesignationResult result = scope.ReplaceStaleDepot(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-4"), new AlwaysGranted());

        Assert.True(result.IsDesignated);
        Assert.True(scope.Resolve().IsReady);
        Assert.True(scope.IsSupplyDepot("chest-4"));
        Assert.False(scope.IsSupplyDepot("chest-37"));
    }

    [Fact]
    public void A_replacement_that_would_be_refused_does_not_lose_the_stale_row_first()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());
        scope.Designate(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-37"), new AlwaysGranted());
        scope.UseIdentityEpoch("epoch-2");

        // A ward went up over the chest. The replacement is refused, and the
        // player is not left with neither.
        DesignationResult result = scope.ReplaceStaleDepot(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-4"), new NeverGranted());

        Assert.Equal(DesignationOutcome.Refused, result.Outcome);
        Assert.True(scope.Book.Has(DesignationKind.SupplyContainer));
    }

    [Fact]
    public void The_revision_moves_on_every_change_so_a_job_can_tell_its_view_is_stale()
    {
        var scope = new StewardScope();
        int start = scope.Revision;

        scope.UseIdentityEpoch("epoch-1");
        Assert.True(scope.Revision > start);

        int afterEpoch = scope.Revision;
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());
        Assert.True(scope.Revision > afterEpoch);

        ScopeSnapshot snapshot = scope.Resolve();
        Assert.True(scope.IsCurrent(snapshot));

        scope.Undesignate(DesignationKind.SettlementArea);
        Assert.False(scope.IsCurrent(snapshot));
    }

    [Fact]
    public void A_ward_check_that_cannot_be_answered_refuses_rather_than_assuming()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");

        DesignationResult result = scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new Unanswerable());

        Assert.Equal(DesignationOutcome.Refused, result.Outcome);
        Assert.Equal(DesignationRefusal.WardCheckUnavailable, result.Refusal);
    }

    [Fact]
    public void Without_an_identity_space_nothing_resolves_at_all()
    {
        var scope = new StewardScope();

        Assert.Equal(ScopeVerdict.NoIdentityEpoch, scope.Resolve().Verdict);
        Assert.False(scope.IsSupplyDepot("anything"));
    }

    [Fact]
    public void Clearing_the_settlement_clears_the_chest_that_belonged_to_it()
    {
        var scope = new StewardScope();
        scope.UseIdentityEpoch("epoch-1");
        scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f),
            new AlwaysGranted());
        scope.Designate(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "chest-1"), new AlwaysGranted());

        scope.Undesignate(DesignationKind.SettlementArea);

        Assert.False(scope.Book.Has(DesignationKind.SettlementArea));
        Assert.False(scope.Book.Has(DesignationKind.SupplyContainer));
    }
}

/// <summary>The file, and what it is and is not for.</summary>
public sealed class RecordStoreTests
{
    private static SettlementScope Scope => new SettlementScope(0x1234L, new SettlementId("home"));

    [Fact]
    public void An_absent_record_is_a_fresh_start_and_is_writable()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        RecordLoadReport report = store.Load(Scope);

        Assert.Equal(RecordLoadOutcome.Absent, report.Outcome);
        Assert.True(report.MayWrite);
        Assert.Equal(IntroductionStage.Unmet, report.Stage);
        Assert.Empty(report.Designations);
        Assert.Empty(report.OpenIntents);
    }

    [Fact]
    public void Everything_that_cannot_be_re_derived_round_trips_exactly()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(12.5f, 3f, -7.25f), 32f, null);
        var depot = new Designation(
            DesignationKind.SupplyContainer, new SitePoint(1f, 0f, 1f), 0f, "chest-9", "epoch-1");
        var open = new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-3"), 2), UpkeepStep.Withdraw, "$item_wood", 7);

        Assert.Null(store.Save(
            Scope, IntroductionStage.Offered, IntroductionStage.Engaged,
            new[] { settlement, depot }, new[] { open }, recordedLoss: 4, fuelItemName: "$item_wood"));

        RecordLoadReport report = store.Load(Scope);

        Assert.Equal(RecordLoadOutcome.Intact, report.Outcome);
        Assert.Equal(IntroductionStage.Offered, report.Stage);
        Assert.Equal(IntroductionStage.Engaged, report.Furthest);
        Assert.Equal(4, report.RecordedLoss);
        Assert.Equal("$item_wood", report.FuelItemName);
        Assert.Equal(0, report.SkippedLines);

        Assert.Equal(2, report.Designations.Count);
        Designation readSettlement = report.Designations.Single(
            d => d.Kind == DesignationKind.SettlementArea);
        Assert.Equal(settlement.Centre, readSettlement.Centre);
        Assert.Equal(32f, readSettlement.Radius);

        Designation readDepot = report.Designations.Single(
            d => d.Kind == DesignationKind.SupplyContainer);
        Assert.Equal("chest-9", readDepot.ContainerKey);
        Assert.Equal("epoch-1", readDepot.IdentityEpoch);

        UpkeepIntent readOpen = Assert.Single(report.OpenIntents);
        Assert.Equal(open.Request, readOpen.Request);
        Assert.Equal(UpkeepStep.Withdraw, readOpen.Step);
        Assert.Equal(7, readOpen.Count);
    }

    [Fact]
    public void A_file_that_lost_its_tail_goes_read_only_and_keeps_what_it_has()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);
        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f, null);
        store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged,
            new[] { settlement }, new UpkeepIntent[0], 0, "$item_wood");

        // A file cut on a line boundary parses cleanly line by line, which is
        // exactly why the closing line exists.
        string path = store.ResolvePath(Scope);
        string[] lines = File.ReadAllLines(path);
        File.WriteAllLines(path, lines.Take(lines.Length - 1).ToArray());

        RecordLoadReport report = store.Load(Scope);

        Assert.Equal(RecordLoadOutcome.Unreadable, report.Outcome);
        Assert.False(report.MayWrite);
        Assert.Single(report.Designations);
        Assert.Contains("damaged", report.Notice);
    }

    [Fact]
    public void A_file_whose_rows_were_changed_under_the_trailer_is_caught()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);
        store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged,
            new[]
            {
                new Designation(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f, null),
            },
            new UpkeepIntent[0], 0, "$item_wood");

        string path = store.ResolvePath(Scope);
        string text = File.ReadAllText(path).Replace("intro\t3\t3", "intro\t0\t0");
        File.WriteAllText(path, text);

        Assert.Equal(RecordLoadOutcome.Unreadable, store.Load(Scope).Outcome);
    }

    [Fact]
    public void A_file_from_a_format_this_build_does_not_know_is_not_written_over()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);
        Directory.CreateDirectory(folder.Path);
        File.WriteAllLines(
            store.ResolvePath(Scope),
            new[] { "# a later build", "format\t99", "end\t1\t-1\t0000000000000000" });

        RecordLoadReport report = store.Load(Scope);

        Assert.Equal(RecordLoadOutcome.Unreadable, report.Outcome);
        Assert.False(report.MayWrite);
        Assert.Contains("different version", report.Notice);
    }

    [Fact]
    public void Two_worlds_never_share_a_record()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        string one = store.ResolvePath(new SettlementScope(1L, new SettlementId("home")));
        string two = store.ResolvePath(new SettlementScope(2L, new SettlementId("home")));

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void Saving_twice_with_the_same_state_produces_the_same_file()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);
        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 20f, null);

        store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged, new[] { settlement },
            new UpkeepIntent[0], 0, "$item_wood");
        string first = File.ReadAllText(store.ResolvePath(Scope));

        store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged, new[] { settlement },
            new UpkeepIntent[0], 0, "$item_wood");
        string second = File.ReadAllText(store.ResolvePath(Scope));

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_journal_write_that_fails_leaves_memory_agreeing_with_the_disk()
    {
        bool allow = true;
        var written = new System.Collections.Generic.List<UpkeepIntent>();
        var journal = new RecordBackedJournal(open =>
        {
            if (!allow)
            {
                return false;
            }

            written.Clear();
            written.AddRange(open);
            return true;
        });

        var intent = new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-1"), 0), UpkeepStep.Withdraw, "$item_wood", 5);
        Assert.True(journal.TryRecordIntent(intent));
        Assert.Single(journal.UnresolvedIntents);
        Assert.Single(written);

        // The disk refuses. The in-memory set must roll back, or the Steward
        // would believe a step was open that the record does not carry.
        allow = false;
        var second = new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-1"), 1), UpkeepStep.Feed, "fire-1", 1);
        Assert.False(journal.TryRecordIntent(second));
        Assert.Single(journal.UnresolvedIntents);
        Assert.Single(written);

        // And a receipt that cannot be written leaves the step OPEN, so the
        // next load reconciles rather than assuming it finished.
        Assert.False(journal.TryRecordReceipt(
            new UpkeepReceipt(intent.Request, UpkeepStep.Withdraw, UpkeepOutcome.Completed, 5, "")));
        Assert.Single(journal.UnresolvedIntents);
    }
}

/// <summary>A folder that cleans up after itself, so a failing test does not
/// leave files behind for the next one to read.</summary>
internal sealed class TemporaryFolder : System.IDisposable
{
    internal TemporaryFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cs-steward-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test that could not clean up is not a test that failed.
        }
    }
}
