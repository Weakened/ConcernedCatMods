using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-004: the four explicit player acts, and what happens when
/// one of them is taken back.
///
/// The acceptance criteria this file exists to pin are all negative or
/// adversarial ones. "The player can mark a circle" is not interesting; that
/// nothing can be marked implicitly, that an unanswerable ward check refuses
/// rather than assumes, and that taking a designation back returns material
/// exactly once — those are the whole leaf.</summary>
public sealed class DesignationTests : IDisposable
{
    private readonly string _root;
    private readonly SettlementRegisterStore _store;

    private static readonly SettlementScope Scope =
        new(worldId: 4242, settlement: new SettlementId("first-camp"));

    private static readonly SitePoint Origin = new(100f, 30f, 200f);

    private static readonly OrderId Cottage = new("cottage-1");

    public DesignationTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-designation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new SettlementRegisterStore(_root);
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

    // ------------------------------------------------------------------
    // Test doubles
    // ------------------------------------------------------------------

    /// <summary>A ward answer under the test's control, counting how often it
    /// was asked so that "this path did not consult the world" is provable
    /// rather than assumed.</summary>
    private sealed class FakeSite : IDesignationSite
    {
        private readonly AreaAccess _answer;

        internal FakeSite(AreaAccess answer)
        {
            _answer = answer;
        }

        internal int Calls { get; private set; }

        public AreaAccess CheckAccess(SitePoint centre, float radius)
        {
            Calls++;
            return _answer;
        }
    }

    /// <summary>An adapter that never fills its answer in. The whole point of
    /// <see cref="AreaAccess.Unavailable"/> being zero is that this refuses.</summary>
    private sealed class ForgetfulSite : IDesignationSite
    {
        public AreaAccess CheckAccess(SitePoint centre, float radius) => default;
    }

    private static FakeSite Granting() => new(AreaAccess.Granted);

    /// <summary>The identity space a chest key belongs to. Real runs get a
    /// fresh one from the adapter on every world load, because the game hands
    /// every persisted object a new id then; tests name it so they can be
    /// explicit about which side of a reload they are on.</summary>
    private const string ThisRun = "run-a";
    private const string AfterAReload = "run-b";

    private SettlementRegister Fresh()
    {
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(ThisRun);
        return register;
    }

    private static DesignationRequest Settlement(float radius = 24f, SitePoint? at = null)
    {
        return DesignationRequest.Area(DesignationKind.SettlementArea, at ?? Origin, radius);
    }

    private static DesignationRequest Harvest(float radius = 20f, SitePoint? at = null)
    {
        return DesignationRequest.Area(
            DesignationKind.HarvestArea, at ?? new SitePoint(140f, 31f, 200f), radius);
    }

    private static DesignationRequest Supply(string key = "chest-a", SitePoint? at = null)
    {
        return DesignationRequest.Container(at ?? new SitePoint(105f, 30f, 203f), key);
    }

    private SettlementRegister SetUpSettlement(out FakeSite site)
    {
        site = Granting();
        SettlementRegister register = Fresh();
        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Settlement(), site, authorised: true).Outcome);
        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Harvest(), site, authorised: true).Outcome);
        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Supply(), site, authorised: true).Outcome);
        return register;
    }

    // ------------------------------------------------------------------
    // Nothing is ever designated implicitly
    // ------------------------------------------------------------------

    [Fact]
    public void AFreshSettlementHasMarkedNothing()
    {
        SettlementRegister register = Fresh();

        Assert.Empty(register.Designations);
        Assert.False(register.HasSettlementArea);
        Assert.Empty(register.Workers);
    }

    [Fact]
    public void WithNothingMarkedNoGroundIsHarvestableAndNoChestIsSupply()
    {
        SettlementRegister register = Fresh();

        // "Nothing marked" must never read as "anywhere". These two answers are
        // what a worker will ask before felling or before taking, and both of
        // them defaulting to false is what makes an unconfigured settlement
        // inert rather than unrestricted.
        Assert.False(register.IsInHarvestArea(Origin));
        Assert.False(register.IsSupplyContainer("chest-a"));
        Assert.False(register.IsSupplyContainer(null));
        Assert.False(register.IsSupplyContainer(string.Empty));
    }

    [Fact]
    public void AMarkedHarvestAreaCoversOnlyItself()
    {
        SettlementRegister register = SetUpSettlement(out _);

        Assert.True(register.IsInHarvestArea(new SitePoint(140f, 31f, 200f)));
        Assert.True(register.IsInHarvestArea(new SitePoint(159.9f, 31f, 200f)));
        Assert.False(register.IsInHarvestArea(new SitePoint(161f, 31f, 200f)));

        // Inside the settlement but outside the harvest area is still not
        // harvestable: the two designations are separate acts.
        Assert.False(register.IsInHarvestArea(Origin));
    }

    [Fact]
    public void OnlyTheDesignatedContainerIsTheSupplyContainer()
    {
        SettlementRegister register = SetUpSettlement(out _);

        Assert.True(register.IsSupplyContainer("chest-a"));
        Assert.False(register.IsSupplyContainer("chest-b"));
        Assert.False(register.IsSupplyContainer("CHEST-A"));
    }

    [Fact]
    public void ASupplyContainerIsNotAnArea()
    {
        SettlementRegister register = SetUpSettlement(out _);
        Assert.True(register.TryGet(DesignationKind.SupplyContainer, out Designation supply));

        // A container has no extent, so nothing is "inside" it. Answering
        // otherwise would let a caller treat a chest as a region.
        Assert.False(supply.Contains(supply.Centre));
        Assert.False(supply.IsArea);
    }

    // ------------------------------------------------------------------
    // The ward gate, at designation time
    // ------------------------------------------------------------------

    [Fact]
    public void GroundInsideSomebodyElsesWardIsRefusedWhenItIsMarked()
    {
        var denied = new FakeSite(AreaAccess.Denied);
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Settlement(), denied, authorised: true);

        Assert.Equal(DesignationOutcome.Refused, result.Outcome);
        Assert.Equal(DesignationRefusal.WardDenied, result.Refusal);
        Assert.Empty(register.Designations);
        Assert.Contains("ward", result.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWardCheckThatCouldNotBeMadeRefuses()
    {
        var unavailable = new FakeSite(AreaAccess.Unavailable);
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Settlement(), unavailable, authorised: true);

        Assert.Equal(DesignationOutcome.Refused, result.Outcome);
        Assert.Equal(DesignationRefusal.WardCheckUnavailable, result.Refusal);
        Assert.Empty(register.Designations);
    }

    [Fact]
    public void AnAdapterThatNeverAnswersRefuses()
    {
        // The safe answer is the one you get by doing nothing. If Unavailable
        // were not zero, this adapter would grant.
        Assert.Equal(AreaAccess.Unavailable, default(AreaAccess));

        SettlementRegister register = Fresh();

        DesignationResult result =
            register.Designate(Settlement(), new ForgetfulSite(), authorised: true);

        Assert.Equal(DesignationRefusal.WardCheckUnavailable, result.Refusal);
    }

    [Fact]
    public void AMissingAdapterRefuses()
    {
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Settlement(), null!, authorised: true);

        Assert.Equal(DesignationRefusal.WardCheckUnavailable, result.Refusal);
        Assert.Empty(register.Designations);
    }

    [Fact]
    public void WithoutAuthorityNothingIsMarkedAndTheWorldIsNotEvenAsked()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Settlement(), site, authorised: false);

        Assert.Equal(DesignationRefusal.NotAuthorised, result.Refusal);
        Assert.Equal(0, site.Calls);
        Assert.Empty(register.Designations);
    }

    // ------------------------------------------------------------------
    // Idempotence and explicit replacement
    // ------------------------------------------------------------------

    [Fact]
    public void MarkingTheSameThingTwiceChangesNothing()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();

        DesignationResult first = register.Designate(Settlement(), site, authorised: true);
        DesignationResult second = register.Designate(Settlement(), site, authorised: true);

        Assert.Equal(DesignationOutcome.Designated, first.Outcome);
        Assert.Equal(DesignationOutcome.AlreadyDesignated, second.Outcome);
        Assert.True(second.IsDesignated);
        Assert.Single(register.Designations);

        // The second call must not consult the world at all. Re-marking what is
        // already marked changes nothing, so it must not be able to fail on a
        // ward check that has started answering differently — the existing
        // designation would survive such a refusal anyway.
        Assert.Equal(1, site.Calls);
    }

    [Fact]
    public void ReMarkingIsIdempotentEvenWhenTheWardCheckHasStartedRefusing()
    {
        FakeSite granting = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(), granting, authorised: true);

        DesignationResult again =
            register.Designate(Settlement(), new FakeSite(AreaAccess.Denied), authorised: true);

        Assert.Equal(DesignationOutcome.AlreadyDesignated, again.Outcome);
        Assert.Single(register.Designations);
    }

    [Fact]
    public void MovingADesignationIsRefusedRatherThanDoneSilently()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(radius: 24f), site, authorised: true);

        DesignationResult moved = register.Designate(Settlement(radius: 30f), site, authorised: true);

        Assert.Equal(DesignationOutcome.Refused, moved.Outcome);
        Assert.Equal(DesignationRefusal.AlreadyDesignatedDifferently, moved.Refusal);
        Assert.NotNull(moved.Designation);
        Assert.Equal(24f, moved.Designation!.Radius);

        // Still exactly the original.
        Assert.True(register.TryGet(DesignationKind.SettlementArea, out Designation live));
        Assert.Equal(24f, live.Radius);
    }

    [Fact]
    public void ANudgedCentreIsADifferentDesignationNotTheSameOne()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(at: Origin), site, authorised: true);

        DesignationResult nudged = register.Designate(
            Settlement(at: new SitePoint(101f, 30f, 200f)), site, authorised: true);

        Assert.Equal(DesignationRefusal.AlreadyDesignatedDifferently, nudged.Refusal);
    }

    // ------------------------------------------------------------------
    // Structural refusals
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(3.9f)]
    [InlineData(48.1f)]
    [InlineData(500f)]
    public void AnAreaOutsideTheAllowedSizeIsRefused(float radius)
    {
        SettlementRegister register = Fresh();

        DesignationResult result =
            register.Designate(Settlement(radius: radius), Granting(), authorised: true);

        Assert.Equal(DesignationRefusal.RadiusOutOfRange, result.Refusal);
    }

    [Theory]
    [InlineData(4f)]
    [InlineData(48f)]
    public void TheBoundsThemselvesAreAllowed(float radius)
    {
        SettlementRegister register = Fresh();

        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Settlement(radius: radius), Granting(), authorised: true).Outcome);
    }

    [Fact]
    public void AHarvestAreaWithoutASettlementIsRefused()
    {
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Harvest(), Granting(), authorised: true);

        Assert.Equal(DesignationRefusal.NoSettlementArea, result.Refusal);
    }

    [Fact]
    public void ASupplyContainerWithoutASettlementIsRefused()
    {
        SettlementRegister register = Fresh();

        DesignationResult result = register.Designate(Supply(), Granting(), authorised: true);

        Assert.Equal(DesignationRefusal.NoSettlementArea, result.Refusal);
    }

    [Fact]
    public void AChestOutsideTheSettlementIsRefused()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(radius: 10f), site, authorised: true);

        DesignationResult result = register.Designate(
            Supply(at: new SitePoint(200f, 30f, 200f)), site, authorised: true);

        Assert.Equal(DesignationRefusal.ContainerOutsideSettlement, result.Refusal);
    }

    [Fact]
    public void AContainerWithNoIdentityIsRefused()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(), site, authorised: true);

        // A designation that resolved by position would follow whatever ends up
        // standing there after the chest is destroyed and rebuilt.
        Assert.Equal(
            DesignationRefusal.ContainerNotIdentified,
            register.Designate(Supply(key: null!), site, authorised: true).Refusal);
        Assert.Equal(
            DesignationRefusal.ContainerNotIdentified,
            register.Designate(Supply(key: string.Empty), site, authorised: true).Refusal);
    }

    [Fact]
    public void ADefaultConstructedRequestIsRefused()
    {
        SettlementRegister register = Fresh();

        DesignationResult result =
            register.Designate(default, Granting(), authorised: true);

        Assert.Equal(DesignationRefusal.InvalidRequest, result.Refusal);
    }

    [Fact]
    public void ARefusalWithNoReasonSaysSoRatherThanInventingOne()
    {
        // Unspecified exists so that a code path which refused without saying
        // why shows up as a defect instead of borrowing a plausible reason.
        string text = DesignationResult.Refused(DesignationRefusal.Unspecified).Describe();

        Assert.Contains("bug", text, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Recruitment
    // ------------------------------------------------------------------

    [Fact]
    public void RecruitingWithoutASettlementIsRefused()
    {
        SettlementRegister register = Fresh();

        RecruitmentResult result =
            register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, authorised: true);

        Assert.Equal(RecruitmentRefusal.NoSettlementArea, result.Refusal);
        Assert.Empty(register.Workers);
    }

    [Fact]
    public void RecruitingWithoutAuthorityIsRefused()
    {
        SettlementRegister register = SetUpSettlement(out _);

        RecruitmentResult result =
            register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, authorised: false);

        Assert.Equal(RecruitmentRefusal.NotAuthorised, result.Refusal);
        Assert.Empty(register.Workers);
    }

    [Fact]
    public void RecruitingTheSameWorkerTwiceIsIdempotent()
    {
        SettlementRegister register = SetUpSettlement(out _);
        var hand = new WorkerId("hand-1");

        Assert.Equal(
            RecruitmentOutcome.Recruited,
            register.Recruit(hand, WorkerRoles.Labourer, authorised: true).Outcome);
        Assert.Equal(
            RecruitmentOutcome.AlreadyRecruited,
            register.Recruit(hand, WorkerRoles.Labourer, authorised: true).Outcome);

        Assert.Single(register.Workers);
    }

    [Fact]
    public void TheFirstProofEmploysOneWorker()
    {
        SettlementRegister register = SetUpSettlement(out _);
        register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, authorised: true);

        RecruitmentResult second =
            register.Recruit(new WorkerId("hand-2"), WorkerRoles.Labourer, authorised: true);

        Assert.Equal(RecruitmentRefusal.RosterFull, second.Refusal);
        Assert.Equal(1, WorkerRoster.MaxWorkers);
        Assert.Single(register.Workers);
    }

    [Fact]
    public void ARoleThisBuildDoesNotEmployIsRefused()
    {
        SettlementRegister register = SetUpSettlement(out _);

        Assert.Equal(
            RecruitmentRefusal.UnknownRole,
            register.Recruit(new WorkerId("hand-1"), "blacksmith", authorised: true).Refusal);
    }

    [Fact]
    public void DismissingIsItsOwnExplicitAct()
    {
        SettlementRegister register = SetUpSettlement(out _);
        var hand = new WorkerId("hand-1");
        register.Recruit(hand, WorkerRoles.Labourer, authorised: true);

        Assert.Equal(DismissalOutcome.Dismissed, register.Dismiss(hand, authorised: true));
        Assert.Equal(DismissalOutcome.NotOnRoster, register.Dismiss(hand, authorised: true));
        Assert.Empty(register.Workers);
    }

    [Fact]
    public void DismissingWithoutAuthorityIsRefused()
    {
        SettlementRegister register = SetUpSettlement(out _);
        var hand = new WorkerId("hand-1");
        register.Recruit(hand, WorkerRoles.Labourer, authorised: true);

        Assert.Equal(DismissalOutcome.Refused, register.Dismiss(hand, authorised: false));
        Assert.Single(register.Workers);
    }

    // ------------------------------------------------------------------
    // Persistence: designations survive a relog
    // ------------------------------------------------------------------

    [Fact]
    public void EverythingMarkedSurvivesASaveAndLoad()
    {
        SettlementRegister register = SetUpSettlement(out _);
        register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, authorised: true);

        Assert.True(_store.Save(register).Saved);

        SettlementRegisterStore.LoadReport report = _store.Load(Scope);
        report.Register.UseIdentityEpoch(ThisRun);

        Assert.Equal(RegisterLoadOutcome.Loaded, report.Outcome);
        Assert.False(report.ReadOnly);
        Assert.Equal(3, report.Register.Designations.Count);
        Assert.Single(report.Register.Workers);
        Assert.Equal("hand-1", report.Register.Workers[0].Id.Value);
        Assert.True(report.Register.IsInHarvestArea(new SitePoint(140f, 31f, 200f)));

        // The chest row round-trips INCLUDING which run of the world its
        // identity belongs to, so it still resolves while that run lasts.
        Assert.True(report.Register.IsSupplyContainer("chest-a"));
        Assert.False(report.Register.HasStaleSupplyIdentity);
    }

    [Fact]
    public void AChestMarkedBeforeAReloadResolvesToNothingAfterIt()
    {
        // The game gives a placed object no identity that survives a save:
        // ZDO.Load reassigns every uid in load order. A key written before a
        // reload therefore names nothing after it -- and because the new ids
        // are dense from one, it is LIKELY to name some other chest. Answering
        // "no chest" is the only safe answer; answering "yes" would let a
        // worker draw from a container the player never designated.
        SettlementRegister register = SetUpSettlement(out _);
        _store.Save(register);

        SettlementRegister reloaded = _store.Load(Scope).Register;
        reloaded.UseIdentityEpoch(AfterAReload);

        Assert.True(reloaded.HasStaleSupplyIdentity);
        Assert.False(reloaded.IsSupplyContainer("chest-a"));

        // The row is still THERE, so the player can see what they marked and
        // where. It just does not resolve.
        Assert.Equal(3, reloaded.Designations.Count);

        // The areas are unaffected: a circle is described by its own
        // coordinates, not by a reference to an object.
        Assert.True(reloaded.IsInHarvestArea(new SitePoint(140f, 31f, 200f)));
    }

    [Fact]
    public void ReMarkingTheChestAfterAReloadJustWorks()
    {
        // Recovery must not require clearing something that is already inert.
        SettlementRegister register = SetUpSettlement(out _);
        _store.Save(register);

        SettlementRegister reloaded = _store.Load(Scope).Register;
        reloaded.UseIdentityEpoch(AfterAReload);

        // Still one step for the player -- but with the settlement's record now
        // (#294): replacing the stale chest runs the undesignation cascade, so
        // nothing drawn from it can be orphaned. Without the record the book
        // refuses rather than overwriting.
        Assert.Equal(
            DesignationRefusal.StaleContainerNeedsTheRecord,
            reloaded.Designate(Supply(key: "chest-a-new-id"), Granting(), authorised: true).Refusal);

        DesignationResult again = reloaded.Designate(
            Supply(key: "chest-a-new-id"), Granting(), authorised: true, new SettlementJournal(Scope),
            out UndesignationPlan? replaced);

        Assert.NotNull(replaced);
        Assert.Equal(DesignationOutcome.Designated, again.Outcome);
        Assert.False(reloaded.HasStaleSupplyIdentity);
        Assert.True(reloaded.IsSupplyContainer("chest-a-new-id"));
        Assert.False(reloaded.IsSupplyContainer("chest-a"));
        Assert.Equal(3, reloaded.Designations.Count);
    }

    [Fact]
    public void WithNoIdentitySpaceNoChestCanBeMarkedAtAll()
    {
        FakeSite site = Granting();
        var register = new SettlementRegister(Scope);
        register.UseIdentityEpoch(null);
        register.Designate(Settlement(), site, authorised: true);

        // Fail-closed: without a current identity space a key could not later
        // be told apart from one left over from a previous run.
        Assert.Equal(
            DesignationRefusal.ContainerIdentityUnavailable,
            register.Designate(Supply(), site, authorised: true).Refusal);
    }

    [Fact]
    public void AChestThatMovedIsStillTheSameChest()
    {
        // A container IS its key. Its centre is recorded only to show the
        // player where it stood, so re-marking a chest that has moved -- one
        // riding a wagon, which SettlementTargets explicitly supports -- must
        // not be refused as "already marked differently".
        FakeSite site = Granting();
        SettlementRegister register = SetUpSettlement(out _);

        DesignationResult moved = register.Designate(
            Supply(at: new SitePoint(104f, 30f, 202f)), site, authorised: true);

        Assert.Equal(DesignationOutcome.AlreadyDesignated, moved.Outcome);
    }

    [Fact]
    public void ADesignationReadBackIsStillTheSameDesignation()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(radius: 17.37f, at: new SitePoint(1.5f, -3.25f, 0.1f)), site, authorised: true);
        _store.Save(register);

        SettlementRegister reloaded = _store.Load(Scope).Register;

        // Round-tripping through the file must not move it by a fraction of a
        // millimetre: if it did, re-marking the same area would stop being
        // idempotent and start being refused as "already marked differently".
        DesignationResult again = reloaded.Designate(
            Settlement(radius: 17.37f, at: new SitePoint(1.5f, -3.25f, 0.1f)), site, authorised: true);

        Assert.Equal(DesignationOutcome.AlreadyDesignated, again.Outcome);
    }

    [Fact]
    public void ANeverSavedSettlementLoadsEmptyAndIsWritable()
    {
        SettlementRegisterStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.Missing, report.Outcome);
        Assert.False(report.ReadOnly);
        Assert.Empty(report.Register.Designations);
    }

    [Fact]
    public void AFileFromAnotherSettlementIsLeftAloneAndNothingNewIsMarked()
    {
        SettlementRegister register = SetUpSettlement(out _);
        _store.Save(register);

        var elsewhere = new SettlementScope(worldId: 4242, settlement: new SettlementId("other-camp"));
        File.Copy(_store.ResolvePath(Scope), _store.ResolvePath(elsewhere));

        SettlementRegisterStore.LoadReport report = _store.Load(elsewhere);

        Assert.Equal(RegisterLoadOutcome.ScopeMismatch, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.Equal(
            DesignationRefusal.RecordReadOnly,
            report.Register.Designate(Settlement(), Granting(), authorised: true).Refusal);
        Assert.False(_store.Save(report.Register).Saved);
    }

    [Fact]
    public void AFileFromANewerBuildIsNotWrittenOver()
    {
        string path = _store.ResolvePath(Scope);
        File.WriteAllLines(path, new[]
        {
            "#\tsettlement register v99",
            "v\t99\t" + Scope.ToStorageKey(),
        });
        string before = File.ReadAllText(path);

        SettlementRegisterStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.UnsupportedSchema, report.Outcome);
        Assert.True(report.ReadOnly);
        Assert.False(_store.Save(report.Register).Saved);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void ADamagedLineKeepsWhatIsReadableAndStopsNewMarking()
    {
        SettlementRegister register = SetUpSettlement(out _);
        _store.Save(register);

        string path = _store.ResolvePath(Scope);
        string[] lines = File.ReadAllLines(path);
        lines[3] = "d\t2\tnot-a-number\t31\t200\t20\t";
        File.WriteAllLines(path, lines);

        SettlementRegisterStore.LoadReport report = _store.Load(Scope);
        report.Register.UseIdentityEpoch(ThisRun);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Equal(1, report.SkippedLines);
        Assert.True(report.ReadOnly);

        // What was readable is kept — the file is never quietly replaced with
        // an empty one, because it says which chest a worker may take from.
        Assert.Equal(2, report.Register.Designations.Count);
        Assert.True(report.Register.IsSupplyContainer("chest-a"));
        Assert.False(report.Register.IsInHarvestArea(new SitePoint(140f, 31f, 200f)));

        // And nothing new is written over a record that cannot be accounted for.
        Assert.Equal(
            DesignationRefusal.RecordReadOnly,
            report.Register.Designate(Harvest(), Granting(), authorised: true).Refusal);
        Assert.False(_store.Save(report.Register).Saved);
    }

    [Fact]
    public void ARowThatBreaksTheTypesOwnRulesIsTreatedAsDamagedNotRepaired()
    {
        string path = _store.ResolvePath(Scope);
        File.WriteAllLines(path, new[]
        {
            "#\tsettlement register v1",
            "v\t1\t" + Scope.ToStorageKey(),
            // A supply container with a radius: the type forbids it, and
            // guessing which of the two fields was wrong would be inventing
            // data.
            "d\t3\t105\t30\t203\t9\tchest-a",
        });

        SettlementRegisterStore.LoadReport report = _store.Load(Scope);

        Assert.Equal(RegisterLoadOutcome.LoadedWithSkippedLines, report.Outcome);
        Assert.Empty(report.Register.Designations);
    }

    [Fact]
    public void AnUnreadableRecordGoesReadOnlyRatherThanStartingFresh()
    {
        SettlementRegister saved = SetUpSettlement(out _);
        _store.Save(saved);
        string path = _store.ResolvePath(Scope);

        // Something else is holding the file open. This is the realistic way a
        // read fails — a backup tool, a sync client, an editor — and the thing
        // that matters is that it is never answered by starting fresh.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SettlementRegisterStore.LoadReport report = _store.Load(Scope);

            Assert.Equal(RegisterLoadOutcome.Unreadable, report.Outcome);
            Assert.True(report.ReadOnly);
            Assert.NotNull(report.Notice);
            Assert.Empty(report.Register.Designations);
            Assert.Equal(
                RecruitmentRefusal.RecordReadOnly,
                report.Register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, true).Refusal);

            // And above all: the file it could not read is still there.
            Assert.False(_store.Save(report.Register).Saved);
        }

        Assert.Equal(3, _store.Load(Scope).Register.Designations.Count);
    }

    [Fact]
    public void SavingTwiceWithNoChangeWritesNothing()
    {
        SettlementRegister register = SetUpSettlement(out _);

        Assert.True(_store.Save(register).Saved);
        Assert.False(_store.Save(register).Saved);
    }

    [Fact]
    public void ContainerNamesWithTabsSurviveTheRoundTrip()
    {
        FakeSite site = Granting();
        SettlementRegister register = Fresh();
        register.Designate(Settlement(), site, authorised: true);
        register.Designate(Supply(key: "chest\tone%09two"), site, authorised: true);

        _store.Save(register);
        SettlementRegister reloaded = _store.Load(Scope).Register;
        reloaded.UseIdentityEpoch(ThisRun);

        Assert.True(reloaded.IsSupplyContainer("chest\tone%09two"));
    }

    // ------------------------------------------------------------------
    // Undesignation: back to untouched, and dependent orders cancelled cleanly
    // ------------------------------------------------------------------

    private SettlementJournal JournalWithReservedOrder(
        string container = "chest-a", OrderState upTo = OrderState.Reserved)
    {
        var journal = new SettlementJournal(Scope);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Approve);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Reserve);
        journal.Append(
            JournalEntryKind.Reserved, Cottage, RequestId.For(Cottage, 0),
            container: container, stacks: new[] { new MaterialStack("Wood", 20) });

        if (upTo == OrderState.Gathering)
        {
            journal.Append(
                JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.BeginGathering);
        }

        return journal;
    }

    [Fact]
    public void ClearingTheSupplyContainerCancelsTheOrderAndReturnsTheWoodExactlyOnce()
    {
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Single(plan.Removed);
        Assert.Single(plan.OrdersToCancel);
        Assert.Single(plan.ToRefund);
        Assert.Equal(20, plan.Totals()["Wood"]);
        Assert.Contains("20 Wood", plan.Describe());

        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));

        ReplayResult after = journal.Replay();
        Assert.Equal(OrderState.Cancelled, after.StateOf(Cottage));
        Assert.Equal(20, after.Ledger.Totals(ReservationState.Refunded)["Wood"]);
        Assert.Empty(after.Ledger.Totals(ReservationState.Held));
        Assert.False(after.NeedsRepair);

        // And the designation really is gone.
        Assert.False(register.IsSupplyContainer("chest-a"));
        Assert.True(register.HasSettlementArea);
    }

    [Fact]
    public void AnOrderDrawingFromADifferentContainerIsNotDisturbed()
    {
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder(container: "chest-elsewhere");

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Empty(plan.OrdersToCancel);
        Assert.Empty(plan.ToRefund);

        register.ApplyUndesignation(plan, journal, authorised: true);

        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void ClearingTheHarvestAreaCancelsOnlyOrdersActuallyGathering()
    {
        SettlementRegister reserved = SetUpSettlement(out _);
        SettlementJournal notGathering = JournalWithReservedOrder();

        UndesignationPlan quiet = reserved.PlanUndesignation(
            DesignationKind.HarvestArea, notGathering.Replay(), authorised: true);

        // An order that already has what it needs is not affected by losing a
        // source it is no longer using.
        Assert.Single(quiet.Removed);
        Assert.Empty(quiet.OrdersToCancel);

        SettlementRegister gatheringRegister = SetUpSettlement(out _);
        SettlementJournal gathering = JournalWithReservedOrder(upTo: OrderState.Gathering);

        UndesignationPlan loud = gatheringRegister.PlanUndesignation(
            DesignationKind.HarvestArea, gathering.Replay(), authorised: true);

        Assert.Single(loud.OrdersToCancel);
        gatheringRegister.ApplyUndesignation(loud, gathering, authorised: true);
        Assert.Equal(OrderState.Cancelled, gathering.Replay().StateOf(Cottage));
    }

    [Fact]
    public void ClearingTheSettlementClearsWhatBelongedToItAndCancelsEverythingUnfinished()
    {
        SettlementRegister register = SetUpSettlement(out _);
        register.Recruit(new WorkerId("hand-1"), WorkerRoles.Labourer, authorised: true);
        SettlementJournal journal = JournalWithReservedOrder();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SettlementArea, journal.Replay(), authorised: true);

        Assert.Equal(3, plan.Removed.Count);

        // Children first, so applying in order puts the container's contents
        // back before the settlement it stood in stops existing.
        Assert.Equal(DesignationKind.SupplyContainer, plan.Removed[0].Kind);
        Assert.Equal(DesignationKind.HarvestArea, plan.Removed[1].Kind);
        Assert.Equal(DesignationKind.SettlementArea, plan.Removed[2].Kind);

        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));

        Assert.Empty(register.Designations);
        Assert.Equal(OrderState.Cancelled, journal.Replay().StateOf(Cottage));

        // Nobody was dismissed. Letting somebody go is its own explicit act,
        // not a side effect of clearing ground.
        Assert.Single(register.Workers);
    }

    [Fact]
    public void ClearingSomethingThatIsNotMarkedChangesNothing()
    {
        SettlementRegister register = Fresh();
        var journal = new SettlementJournal(Scope);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.HarvestArea, journal.Replay(), authorised: true);

        Assert.True(plan.ChangesNothing);
        Assert.Equal(
            UndesignationOutcome.NotDesignated, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.Contains("Nothing to clear", plan.Describe());
    }

    [Fact]
    public void AnOrderNeedingRepairIsNeverTidiedAwayByClearingGround()
    {
        SettlementRegister register = SetUpSettlement(out _);
        var journal = new SettlementJournal(Scope);
        journal.Append(JournalEntryKind.OrderTransition, Cottage, transition: OrderTransition.Approve);
        journal.Append(
            JournalEntryKind.Reserved, Cottage, RequestId.For(Cottage, 0),
            container: "chest-a", stacks: new[] { new MaterialStack("Wood", 20) });

        // A commit that started and never finished: whether that wood became a
        // wall is not recorded, and clearing some ground must not decide it.
        journal.Append(JournalEntryKind.CommitStarted, Cottage, RequestId.For(Cottage, 0));

        ReplayResult before = journal.Replay();
        Assert.True(before.NeedsRepair);
        Assert.Equal(OrderState.NeedsRepair, before.StateOf(Cottage));

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SettlementArea, before, authorised: true);

        Assert.Empty(plan.OrdersToCancel);
        Assert.Empty(plan.ToRefund);

        register.ApplyUndesignation(plan, journal, authorised: true);

        ReplayResult after = journal.Replay();
        Assert.Equal(OrderState.NeedsRepair, after.StateOf(Cottage));
        Assert.True(after.NeedsRepair);
        Assert.True(after.Ledger.HasUncertainCustody);
        Assert.Empty(after.Ledger.Totals(ReservationState.Refunded));
    }

    [Fact]
    public void ASpentReservationIsNotReturnedASecondTime()
    {
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();
        RequestId request = RequestId.For(Cottage, 0);
        journal.Append(JournalEntryKind.CommitStarted, Cottage, request);
        journal.Append(JournalEntryKind.CommitFinished, Cottage, request);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        // The order is still unfinished, so it is cancelled — but the wood is
        // already a wall, so there is nothing to give back.
        Assert.Single(plan.OrdersToCancel);
        Assert.Empty(plan.ToRefund);

        register.ApplyUndesignation(plan, journal, authorised: true);

        ReplayResult after = journal.Replay();
        Assert.Equal(20, after.Ledger.Totals(ReservationState.Committed)["Wood"]);
        Assert.Empty(after.Ledger.Totals(ReservationState.Refunded));
    }

    [Fact]
    public void ConservationHoldsAcrossAnUndesignation()
    {
        // An earlier version of this test summed every reservation state and
        // asserted the total was unchanged. That is a tautology: the only
        // operation that changes the total is Reserve, and clearing never
        // reserves, so it passed whether the refund was right, wrong, doubled
        // or absent. An independent review caught it. This version models the
        // CONTAINER, which is the half the invariant is actually about:
        //
        //     container + held + committed = constant
        //
        // The container is simulated here because no container type exists in
        // this layer yet -- CF-SET-006 owns that -- but the plan is what a
        // container would be driven by, so driving one from the plan is a real
        // test of the plan.
        const int Stocked = 50;
        const int Reserved = 20;

        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        // The reserve already happened: the chest is down by that much.
        int container = Stocked - Reserved;

        int Total(ReplayResult state, ReservationState which)
        {
            return state.Ledger.Totals(which).TryGetValue("Wood", out int count) ? count : 0;
        }

        int Accounted(ReplayResult state)
        {
            return container
                + Total(state, ReservationState.Held)
                + Total(state, ReservationState.Committed);
        }

        Assert.Equal(Stocked, Accounted(journal.Replay()));

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);
        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));

        // A container driven by this plan puts back exactly what it names.
        container += plan.Totals().TryGetValue("Wood", out int returned) ? returned : 0;

        ReplayResult after = journal.Replay();
        Assert.Equal(Stocked, Accounted(after));
        Assert.Equal(Stocked, container);

        // And it really moved: nothing is still held, nothing became committed,
        // and the refund is recorded exactly once.
        Assert.Equal(0, Total(after, ReservationState.Held));
        Assert.Equal(0, Total(after, ReservationState.Committed));
        Assert.Equal(Reserved, Total(after, ReservationState.Refunded));
    }

    [Fact]
    public void ApplyingTheSamePlanTwiceDoesNotRefundTwice()
    {
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));

        // The designation is gone, so the plan no longer matches the book and
        // is refused as stale rather than replayed.
        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));

        ReplayResult after = journal.Replay();
        Assert.Equal(20, after.Ledger.Totals(ReservationState.Refunded)["Wood"]);
    }

    [Fact]
    public void AnOldPlanIsRefusedEvenWhenTheSameThingIsMarkedAgainIdentically()
    {
        // The case the book check alone cannot catch: clear, then re-mark the
        // SAME container the same way, then confirm the old plan. The book now
        // matches the plan again, so only the journal moving on tells them
        // apart. Without that check this replays a refund against a settlement
        // that has already been settled once.
        FakeSite site = Granting();
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Supply(), site, authorised: true).Outcome);

        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));

        // Still exactly one refund.
        Assert.Equal(20, journal.Replay().Ledger.Totals(ReservationState.Refunded)["Wood"]);
        Assert.True(register.IsSupplyContainer("chest-a"));
    }

    [Fact]
    public void APlanMadeStaleByAReplacedDesignationIsRefused()
    {
        FakeSite site = Granting();
        SettlementRegister register = SetUpSettlement(out _);
        var journal = new SettlementJournal(Scope);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);

        // Somebody clears and re-marks the container between planning and
        // applying. Applying the old plan would remove a designation the player
        // has since replaced.
        register.ApplyUndesignation(
            register.PlanUndesignation(DesignationKind.SupplyContainer, journal.Replay(), true),
            journal, authorised: true);
        register.Designate(Supply(key: "chest-b"), site, authorised: true);

        Assert.Equal(UndesignationOutcome.Stale, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.True(register.IsSupplyContainer("chest-b"));
    }

    [Fact]
    public void ClearingWithoutAuthorityPlansNothingAndAppliesNothing()
    {
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: false);

        Assert.True(plan.IsRefused);
        Assert.Equal(DesignationRefusal.NotAuthorised, plan.Refusal);
        Assert.Equal(UndesignationOutcome.Refused, register.ApplyUndesignation(plan, journal, authorised: true));
        Assert.True(register.IsSupplyContainer("chest-a"));
        Assert.Equal(OrderState.Reserved, journal.Replay().StateOf(Cottage));
    }

    [Fact]
    public void AJournalFromAnotherSettlementIsRefusedRatherThanWrittenTo()
    {
        SettlementRegister register = SetUpSettlement(out _);
        var foreign = new SettlementJournal(
            new SettlementScope(worldId: 9, settlement: new SettlementId("other-camp")));

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, new SettlementJournal(Scope).Replay(), authorised: true);

        Assert.Throws<ArgumentException>(() => register.ApplyUndesignation(plan, foreign, authorised: true));
    }

    [Fact]
    public void AClearedSettlementIsStillWritableAndCanBeMarkedAgain()
    {
        FakeSite site = Granting();
        SettlementRegister register = SetUpSettlement(out site);
        var journal = new SettlementJournal(Scope);

        register.ApplyUndesignation(
            register.PlanUndesignation(DesignationKind.SettlementArea, journal.Replay(), true),
            journal, authorised: true);

        Assert.Empty(register.Designations);
        Assert.Equal(
            DesignationOutcome.Designated,
            register.Designate(Settlement(radius: 30f), site, authorised: true).Outcome);
    }

    [Fact]
    public void ClearingIsRecordedSoItSurvivesARelog()
    {
        // This used an empty journal and saved only the register, so it proved
        // nothing about the thing that matters: that the refund survives
        // alongside the designation going away. Both files now round-trip.
        var journals = new JournalStore(_root);
        SettlementRegister register = SetUpSettlement(out _);
        SettlementJournal journal = JournalWithReservedOrder();

        Assert.True(_store.Save(register).Saved);
        Assert.True(journals.Save(journal).Saved);

        UndesignationPlan plan = register.PlanUndesignation(
            DesignationKind.SupplyContainer, journal.Replay(), authorised: true);
        Assert.Equal(UndesignationOutcome.Removed, register.ApplyUndesignation(plan, journal, authorised: true));

        var writer = new SettlementRecordWriter(journals, _store);
        RecordSaveOutcome saved = writer.Save(journal, register);

        Assert.True(saved.JournalSaved);
        Assert.True(saved.RegisterSaved);
        Assert.False(saved.RegisterHeldBack);

        // Reloaded from disk, both halves agree: the container is no longer
        // designated AND the wood was returned.
        Assert.False(_store.Load(Scope).Register.IsSupplyContainer("chest-a"));

        ReplayResult reloaded = journals.Load(Scope).Journal.Replay();
        Assert.Equal(OrderState.Cancelled, reloaded.StateOf(Cottage));
        Assert.Equal(20, reloaded.Ledger.Totals(ReservationState.Refunded)["Wood"]);
        Assert.Empty(reloaded.Ledger.Totals(ReservationState.Held));
    }
}
