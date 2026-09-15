using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>Regressions for every defect an independent review found in
/// CF-SET-004 before it merged.
///
/// They are gathered here rather than scattered through
/// <see cref="DesignationTests"/> because each one exists for a named reason
/// that would otherwise be lost: the code had a comment claiming the property,
/// and the property was not there. A test whose only job is to stop a specific
/// claim becoming false again is worth being able to find.
///
/// Several of these describe situations no shipped code can reach yet, because
/// nothing before CF-SET-006 creates an order or reserves material. That is
/// precisely why they are written now — the guard's contract is wrong today and
/// would go live silently the moment a worker starts journalling.</summary>
public sealed class DesignationGuardTests : IDisposable
{
    private readonly string _root;
    private readonly SettlementRegisterStore _registers;
    private readonly JournalStore _journals;

    private static readonly SettlementScope Scope =
        new(worldId: 77, settlement: new SettlementId("guarded"));

    private static readonly OrderId Cottage = new("cottage-1");
    private static readonly SitePoint Origin = new(0f, 10f, 0f);

    public DesignationGuardTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-designation-guards", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _registers = new SettlementRegisterStore(_root);
        _journals = new JournalStore(_root);
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
            // A locked temp file must never fail the suite.
        }
    }

    private sealed class GrantingSite : IDesignationSite
    {
        public AreaAccess CheckAccess(SitePoint centre, float radius) => AreaAccess.Granted;
    }

    private static readonly GrantingSite Site = new();

    private const string ThisRun = "run-a";

    private SettlementRegister SetUp()
    {
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(ThisRun);
        register.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, Origin, 24f), Site, true);
        register.Designate(
            DesignationRequest.Area(DesignationKind.HarvestArea, new SitePoint(60f, 10f, 0f), 20f),
            Site, true);
        register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-a"), Site, true);
        return register;
    }

    private static SettlementJournal Reserved(
        string container = "chest-a", bool withTransitions = true)
    {
        var journal = new SettlementJournal(Scope);
        if (withTransitions)
        {
            journal.Append(
                JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Approve);
            journal.Append(
                JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Reserve);
        }

        journal.Append(
            JournalEntryKind.Reserved, Cottage, RequestId.For(Cottage, 0),
            container: container, stacks: new[] { new MaterialStack("Wood", 20) });
        return journal;
    }

    // ------------------------------------------------------------------
    // The journal half of the guard
    // ------------------------------------------------------------------

    [Fact]
    public void AJournalThisBuildMayNotWriteStopsAClearAltogether()
    {
        // The register parses fine, so the old gate -- which looked only at the
        // register -- let the clear happen. The refund would then have been
        // appended to a journal that will never be saved: designation gone,
        // material stranded, and no later cascade able to reach it because the
        // container designation that would have driven one is the thing that
        // disappeared.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();
        journal.MarkReadOnly();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Equal(UndesignationOutcome.Refused, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.True(register.IsSupplyContainer("chest-a"));
        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void AJournalThatMovedOnSinceThePlanIsRefused()
    {
        // A reservation appended between planning and confirming is not in
        // ToRefund. Applying anyway would cancel the order without returning
        // it -- and a cancelled order is terminal, so no later cascade would
        // ever reach that material again.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        journal.Append(
            JournalEntryKind.Reserved, Cottage, RequestId.For(Cottage, 1),
            container: "chest-a", stacks: new[] { new MaterialStack("Wood", 5) });

        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));

        ReplayResult after = journal.Replay();
        Assert.Equal(25, after.Ledger.Totals(ReservationState.Held)["Wood"]);
        Assert.Empty(after.Ledger.Totals(ReservationState.Refunded));
        Assert.True(register.IsSupplyContainer("chest-a"));
    }

    [Fact]
    public void ACommitStartedSinceThePlanIsNeverRefunded()
    {
        // The worst version of the same hole. Replay settles a Refunded entry
        // during its entry loop, and only marks unfinished commits uncertain
        // afterwards -- so a refund written over an in-flight commit WINS, and
        // material that may already be standing as a wall goes back in the
        // chest as well. That is the guess this whole layer refuses to make.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        journal.Append(JournalEntryKind.CommitStarted, Cottage, RequestId.For(Cottage, 0));

        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));

        ReplayResult after = journal.Replay();
        Assert.True(after.NeedsRepair);
        Assert.True(after.Ledger.HasUncertainCustody);
        Assert.Empty(after.Ledger.Totals(ReservationState.Refunded));
    }

    // ------------------------------------------------------------------
    // The book half of the guard
    // ------------------------------------------------------------------

    [Fact]
    public void ADesignationMarkedSinceThePlanIsNotSweptAwayUnlisted()
    {
        // Apply performs a CASCADE, not a removal of the listed rows, so
        // checking only what the plan listed let a designation marked since be
        // removed unlisted, undescribed, and with anything drawn from it
        // stranded.
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(ThisRun);
        register.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, Origin, 24f), Site, true);
        var journal = new SettlementJournal(Scope);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SettlementArea, journal.Replay(), authorised: true);
        Assert.Single(plan.Removed);

        register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-a"), Site, true);

        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.True(register.IsSupplyContainer("chest-a"));
        Assert.True(register.HasSettlementArea);
    }

    [Fact]
    public void AnOrderKnownOnlyByItsReservationIsStillCancelledAndReturned()
    {
        // Replay records a state for a transition entry, but a Reserved entry
        // adds nothing to the order table. Iterating that table alone left such
        // an order invisible: never cancelled, never refunded, and the player
        // told that clearing cost nothing.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved(withTransitions: false);

        Assert.Empty(journal.Replay().Orders);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Single(plan.OrdersToCancel);
        Assert.Equal(20, plan.Totals()["Wood"]);

        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.Equal(20, journal.Replay().Ledger.Totals(ReservationState.Refunded)["Wood"]);
    }

    [Fact]
    public void AChildDesignationWithNoSettlementIsTreatedAsDamaged()
    {
        // A harvest area or a chest belongs to a settlement area. A file
        // carrying one without the other is damaged, and taking the orphan
        // would produce a settlement whose parts answer questions their parent
        // never authorised -- and then write it back in that shape.
        //
        // This also removes the only route by which the "clears the settlement"
        // flag could have been derived from a request that removes nothing of
        // that kind; deriving it from what is actually removed stays as
        // belt-and-braces.
        string path = _registers.ResolvePath(Scope);
        File.WriteAllLines(path, new[]
        {
            "#\tsettlement register v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "d\t2\t60\t10\t0\t20\t",
        });

        SettlementRegisterStore.LoadReport report = _registers.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.Register.Designations);
        Assert.False(report.Register.IsInHarvestArea(new SitePoint(60f, 10f, 0f)));
        Assert.False(_registers.Save(report.Register).Saved);
    }

    [Fact]
    public void ClearingIsRefusedOutrightOnceAuthorityIsLost()
    {
        // A plan can sit unconfirmed for as long as a player takes to type --
        // long enough to switch the runtime off in between. Authority is
        // re-read on every other act in this runtime; this was the one
        // mutating entry point that did not honour that.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);
        Assert.Single(plan.OrdersToCancel);

        Assert.Equal(
            UndesignationOutcome.Refused,
            register.ApplyUndesignation(plan, journal, authorised: false));

        Assert.True(register.IsSupplyContainer("chest-a"));
        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void APlanFromADifferentJournalOfTheSameLengthIsRefused()
    {
        // Scope and length together are not enough: two journal objects for the
        // same settlement can hold different entries and still agree on both.
        SettlementRegister register = SetUp();
        SettlementJournal planned = Reserved();
        SettlementJournal other = Reserved(container: "chest-elsewhere");

        Assert.Equal(planned.NextSequence, other.NextSequence);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, planned.Replay(), authorised: true);

        Assert.Equal(
            UndesignationOutcome.Stale,
            register.ApplyUndesignation(plan, other, authorised: true));
    }

    [Theory]
    [InlineData(1)]  // Reserved
    [InlineData(2)]  // Refunded
    [InlineData(3)]  // CommitStarted
    [InlineData(4)]  // CommitFinished
    public void NoLineWithAnEmptyRequestBringsDownTheReplay(int kind)
    {
        // Every kind, not the ones somebody happened to notice. This was
        // guarded twice -- once for Refunded, once for CommitStarted -- each
        // time with a comment claiming the class was closed, and CommitFinished
        // was still open both times. A Theory over the whole enum is the shape
        // that cannot be half-right.
        //
        // The codec treats the request field as optional for all of them, and
        // each one reaches a dictionary keyed by RequestId.Value, which is null
        // when the id is default. Replay runs on two of the seven cf_settle
        // subcommands -- status and clear -- so an unguarded kind is not an
        // unreachable edge: it is those two permanently dead for that world.
        File.WriteAllLines(_journals.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v1",
            "v\t1\t" + Scope.ToStorageKey(),
            // Container and stacks are present so the Reserved row is a REAL
            // regression row: without the guard it reaches the Reservation
            // constructor, which rejects an empty request. A bare row would be
            // skipped for having no container and would prove nothing.
            "e\t0\t" + kind.ToString() + "\tcottage-1\t\t0\tchest-a\tWood*20",
        });

        ReplayResult replayed = _journals.Load(Scope).Journal.Replay();

        Assert.NotNull(replayed);
        Assert.False(replayed.NeedsRepair);
        Assert.Empty(replayed.Ledger.Reservations);
    }

    [Fact]
    public void EveryJournalKindIsExplicitlyClassifiedAsCarryingARequestOrNot()
    {
        // This test is the guarantee. An earlier comment claimed the compiler
        // provided one -- that a switch statement with no default arm would
        // fail to build when a member was added to the enum. C# does no such
        // thing: it compiles and silently takes the fallback.
        //
        // So the mapping is pinned here instead. Add a member to
        // JournalEntryKind without deciding which side it is on and this fails,
        // naming the member. That is a real guarantee, in the place that can
        // actually make one.
        var expected = new Dictionary<JournalEntryKind, bool>
        {
            [JournalEntryKind.OrderTransition] = false,
            [JournalEntryKind.Reserved] = true,
            [JournalEntryKind.Refunded] = true,
            [JournalEntryKind.CommitStarted] = true,
            [JournalEntryKind.CommitFinished] = true,
        };

        foreach (JournalEntryKind kind in Enum.GetValues(typeof(JournalEntryKind)))
        {
            Assert.True(
                expected.ContainsKey(kind),
                "JournalEntryKind." + kind + " is not classified. Decide whether it carries a " +
                "request id, add it to SettlementJournal.CarriesRequest and to this test. " +
                "Nothing else will tell you.");

            Assert.Equal(expected[kind], SettlementJournal.CarriesRequest(kind));
        }

        Assert.Equal(expected.Count, Enum.GetValues(typeof(JournalEntryKind)).Length);
    }

    [Fact]
    public void AKindThisBuildDoesNotDefineNeverReachesTheReplay()
    {
        // The other half of the contract. An undefined kind cannot arrive from
        // a file at all, because the codec refuses it -- so the fallback inside
        // CarriesRequest is for a kind added in CODE and left unclassified, not
        // for anything a player's disk can produce.
        File.WriteAllLines(_journals.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "e\t0\t99\tcottage-1\treq-1\t0\t",
        });

        JournalStore.LoadReport report = _journals.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.Journal.Entries);
    }

    [Fact]
    public void ClearingAKindThatIsNotMarkedCancelsNothing()
    {
        // Re-expressed after the orphan-restore rule removed the route the
        // earlier version of this test used. The property still worth pinning
        // is that a request naming a kind which is not marked removes nothing
        // and therefore cancels nothing -- it must not fall through to "this
        // clears the settlement" on the strength of the kind that was asked
        // for.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SettlementArea, journal.Replay(), authorised: true);
        Assert.Equal(3, plan.Removed.Count);

        SettlementRegister bare = new SettlementRegister(Scope);
        bare.UseIdentityEpoch(ThisRun);

        UndesignationPlan nothing = bare.PlanUndesignation(
            DesignationKind.SettlementArea, journal.Replay(), authorised: true);

        Assert.True(nothing.ChangesNothing);
        Assert.Empty(nothing.OrdersToCancel);
        Assert.Empty(nothing.ToRefund);
        Assert.Equal(
            UndesignationOutcome.NotDesignated,
            bare.ApplyUndesignation(nothing, journal, authorised: true));
        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void WithNoIdentitySpaceNoChestResolvesEither()
    {
        // The documented rule is that a null epoch stops designating AND
        // resolving. Only the designating half was enforced, so a row with no
        // epoch, read into a book with no epoch, compared equal and resolved --
        // the wrong direction for the half that grants access to a chest.
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(ThisRun);
        register.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, Origin, 24f), Site, true);
        register.Designate(
            DesignationRequest.Container(new SitePoint(3f, 10f, 3f), "chest-a"), Site, true);
        Assert.True(register.IsSupplyContainer("chest-a"));

        register.UseIdentityEpoch(null);

        Assert.False(register.IsSupplyContainer("chest-a"));
        Assert.True(register.HasStaleSupplyIdentity);
    }

    [Fact]
    public void AJournalWhoseRowsAreOutOfOrderIsTreatedAsDamaged()
    {
        // NextSequence is the fingerprint a pending plan is checked against, and
        // it used to read the LAST entry rather than the highest. A reordered
        // file would then hand out a sequence already in use -- defeating the
        // guarantee Append documents, and letting a stale plan through.
        File.WriteAllLines(_journals.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "e\t5\t0\tcottage-1\t\t0\t",
            "e\t1\t0\tcottage-1\t\t0\t",
        });

        JournalStore.LoadReport report = _journals.Load(Scope);

        Assert.Equal(JournalLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);
        Assert.True(report.ReadOnly);
        Assert.Single(report.Journal.Entries);
        Assert.Equal(6L, report.Journal.NextSequence);
    }

    // ------------------------------------------------------------------
    // Replay robustness
    // ------------------------------------------------------------------

    [Fact]
    public void ARefundLineWithNoRequestDoesNotBringDownTheReplay()
    {
        // The codec treats the request field as optional, so a damaged or
        // hand-edited refund line can carry an empty one -- which reached a
        // dictionary lookup on a null key. Replay now runs on nearly every
        // console command, so this went from unreachable to one keystroke away.
        File.WriteAllLines(_journals.ResolvePath(Scope), new[]
        {
            "#\tsettlement journal v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "e\t0\t2\tcottage-1\t\t0\t",
        });

        SettlementJournal journal = _journals.Load(Scope).Journal;

        ReplayResult replayed = journal.Replay();

        Assert.NotNull(replayed);
        Assert.False(replayed.NeedsRepair);
    }

    [Fact]
    public void ARefundLineRecordsWhatItReturned()
    {
        // A refund line used to carry only a request id. Replay does not need
        // more, but a person repairing a half-read journal does: a line that
        // says "something was returned" without saying what is not a record.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();

        register.ApplyUndesignation(
            register.PlanUndesignation(DesignationKind.SupplyContainer, journal.Replay(), true),
            journal, authorised: true);

        JournalEntry refund = journal.Entries.Single(e => e.Kind == JournalEntryKind.Refunded);

        Assert.Equal("chest-a", refund.Container);
        Assert.Equal(new MaterialStack("Wood", 20), Assert.Single(refund.Stacks));
    }

    // ------------------------------------------------------------------
    // Rows this build cannot represent are damage, not data
    // ------------------------------------------------------------------

    [Fact]
    public void ADesignationWithAnImpossibleRadiusIsTreatedAsDamaged()
    {
        // Unlike the ward answer, a radius does not depend on the world, so
        // re-checking it on load cannot delete a settlement someone still has.
        // Accepting it would have made felling legal everywhere in the world --
        // the exact opposite of "nothing marked is never anywhere".
        File.WriteAllLines(_registers.ResolvePath(Scope), new[]
        {
            "#\tsettlement register v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "d\t1\t0\t10\t0\t24\t",
            "d\t2\t0\t10\t0\t1E+30\t",
        });

        SettlementRegisterStore.LoadReport report = _registers.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.False(report.Register.IsInHarvestArea(new SitePoint(100000f, 0f, 100000f)));
        Assert.False(report.Register.IsInHarvestArea(Origin));
    }

    [Fact]
    public void ASecondRowOfTheSameKindIsTreatedAsDamagedRatherThanWinning()
    {
        File.WriteAllLines(_registers.ResolvePath(Scope), new[]
        {
            "#\tsettlement register v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "d\t1\t0\t10\t0\t24\t",
            "d\t1\t500\t10\t500\t48\t",
        });

        SettlementRegisterStore.LoadReport report = _registers.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);
        Assert.True(report.ReadOnly);

        // The FIRST row is kept. Last-wins would have discarded a designation
        // and then rewritten the file without it.
        Assert.True(report.Register.TryGet(DesignationKind.SettlementArea, out Designation area));
        Assert.Equal(24f, area.Radius);
    }

    [Fact]
    public void MoreWorkersThanThisBuildEmploysIsTreatedAsDamaged()
    {
        // A file from a build that employs more of them is a file this build
        // cannot represent. Quietly keeping the first would then rewrite the
        // file with the rest deleted.
        File.WriteAllLines(_registers.ResolvePath(Scope), new[]
        {
            "#\tsettlement register v1",
            "v\t1\t" + Scope.ToStorageKey(),
            "d\t1\t0\t10\t0\t24\t",
            "w\tworker-1\tlabourer",
            "w\tworker-2\tlabourer",
        });

        SettlementRegisterStore.LoadReport report = _registers.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.Single(report.Register.Workers);
        Assert.False(_registers.Save(report.Register).Saved);
    }

    // ------------------------------------------------------------------
    // The two-file write rule
    // ------------------------------------------------------------------

    [Fact]
    public void AFailedJournalWriteStopsTheRegisterWrite()
    {
        // The rule the whole ordering exists for. Previously the register was
        // written unconditionally, producing "the marking is gone and nothing
        // was returned" -- the one inconsistency no later run can repair.
        SettlementRegister register = SetUp();

        var journal = new SettlementJournal(Scope);
        journal.MarkReadOnly();
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Cancel);

        var writer = new SettlementRecordWriter(_journals, _registers);
        RecordSaveOutcome outcome = writer.Save(journal, register);

        Assert.False(outcome.JournalSaved);
        Assert.False(outcome.RegisterSaved);
        Assert.True(outcome.RegisterHeldBack);
        Assert.NotNull(outcome.Notice);

        // Nothing on disk at all, so the two records still agree.
        Assert.False(File.Exists(_registers.ResolvePath(Scope)));
        Assert.False(File.Exists(_journals.ResolvePath(Scope)));
    }

    [Fact]
    public void AJournalWithNothingToWriteDoesNotStopTheRegisterWrite()
    {
        // The trap in the obvious fix: a clean journal also reports Saved ==
        // false. Treating that as a failure would stop the register ever being
        // written, which is every ordinary designation.
        SettlementRegister register = SetUp();
        var journal = new SettlementJournal(Scope);

        Assert.False(journal.IsDirty);

        RecordSaveOutcome outcome =
            new SettlementRecordWriter(_journals, _registers).Save(journal, register);

        Assert.False(outcome.JournalSaved);
        Assert.True(outcome.RegisterSaved);
        Assert.False(outcome.RegisterHeldBack);
        Assert.True(File.Exists(_registers.ResolvePath(Scope)));
    }

    [Fact]
    public void ARealJournalWriteFailureAlsoStopsTheRegisterWrite()
    {
        // The same rule against an actual I/O failure rather than a flag: the
        // live journal file held open by something else, which is the case
        // AtomicTextFile's fallbacks exist for.
        SettlementRegister register = SetUp();
        SettlementJournal journal = Reserved();
        Assert.True(_journals.Save(journal).Saved);
        Assert.True(_registers.Save(register).Saved);

        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Cancel);
        register.Dismiss(new WorkerId("nobody"), authorised: true);
        register.Designate(
            DesignationRequest.Area(DesignationKind.HarvestArea, new SitePoint(90f, 10f, 0f), 12f),
            Site, true);

        string registerBefore = File.ReadAllText(_registers.ResolvePath(Scope));

        using (new FileStream(
            _journals.ResolvePath(Scope), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            RecordSaveOutcome outcome =
                new SettlementRecordWriter(_journals, _registers).Save(journal, register);

            Assert.False(outcome.JournalSaved);
            Assert.False(outcome.RegisterSaved);
            Assert.True(outcome.RegisterHeldBack);
        }

        // The register on disk is exactly what it was.
        Assert.Equal(registerBefore, File.ReadAllText(_registers.ResolvePath(Scope)));
    }
}
