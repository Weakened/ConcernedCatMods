using TheConcernedCat.Settlement.Housing;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

public sealed class ResidentAdmissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cc-resident", Guid.NewGuid().ToString("N"));
    private readonly SettlementScope _scope = new(11, new SettlementId("home"));
    private readonly RequestId _request = new("resident-one");
    private readonly JournalStore _store;
    private SettlementJournal _journal;
    private bool _failSave;
    public ResidentAdmissionTests()
    {
        Directory.CreateDirectory(_root);
        _store = new JournalStore(_root);
        _journal = _store.Load(_scope).Journal;
    }
    public void Dispose() => Directory.Delete(_root, true);
    private ResidentAdmission Open() => new(_journal, j => !_failSave && _store.Save(j).Saved, Guid.NewGuid());
    private void Reload() => _journal = _store.Load(_scope).Journal;
    private static ResidentHousingSlot Slot(float y = 2, bool roof = true, BedClaim claim = BedClaim.Unclaimed) =>
        new(new SitePoint(1, y, 3), new HousingFacts("rounded-label", true, true, roof, 1, true, claim));
    private ResidentAdmission Admit()
    {
        var resident = Open();
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("ordinary-resident", Slot(), _request));
        Assert.Equal(ResidentAdmissionResult.Admitted, resident.Admit("ordinary-resident", Slot(), _request));
        return resident;
    }
    [Fact]
    public void ReservationSurvivesReloadWithoutCreatingResidentAndAdmissionIsIdempotent()
    {
        var resident = Open();
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("ordinary-resident", Slot(), _request));
        Assert.False(resident.HasResident);
        Reload(); resident = Open();
        Assert.False(resident.HasResident);
        Assert.Equal(ResidentAdmissionResult.Admitted, resident.Admit("ordinary-resident", Slot(), _request));
        Reload(); resident = Open();
        Assert.True(resident.HasResident);
        Assert.Equal(ResidentAdmissionResult.AlreadyRecorded, resident.Admit("ordinary-resident", Slot(), _request));
        Assert.Equal(2, _journal.Entries.Count);
        Assert.All(_journal.Entries, entry => Assert.True(entry.Order.IsEmpty));
    }
    [Fact]
    public void TwoServicesCannotReserveDifferentIdentitiesOnSameJournal()
    {
        var first = Open(); var second = Open();
        Assert.Equal(ResidentAdmissionResult.Reserved, first.Reserve("one", Slot(), _request));
        Assert.Equal(ResidentAdmissionResult.Refused, second.Reserve("two", Slot(8), new RequestId("second")));
        Assert.Single(_journal.Entries);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSaveNeverGrantsAdmissionAndRetryReloadIsSafe(bool duringAdmission)
    {
        var resident = Open();
        if (duringAdmission) Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("one", Slot(), _request));
        _failSave = true;
        Assert.Equal(ResidentAdmissionResult.NotRecorded, duringAdmission
            ? resident.Admit("one", Slot(), _request) : resident.Reserve("one", Slot(), _request));
        Assert.False(resident.HasResident);
        Assert.False(_journal.IsDirty);
        Reload(); resident = Open(); _failSave = false;
        Assert.Equal(duringAdmission ? ResidentJournalPhase.Reserved : ResidentJournalPhase.Unspecified, resident.Phase);
        if (!duringAdmission) Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("one", Slot(), _request));
        Assert.Equal(ResidentAdmissionResult.Admitted, resident.Admit("one", Slot(), _request));
        Reload(); Assert.True(Open().HasResident);
    }
    [Theory]
    [InlineData((int)HousingRefusal.NoRoof)]
    [InlineData((int)HousingRefusal.AlreadyClaimed)]
    [InlineData((int)HousingRefusal.YourOwnBed)]
    public void HousingLossKeepsIdentityAndRepairNeedsExplicitReadmission(int reasonValue)
    {
        var reason = (HousingRefusal)reasonValue;
        var resident = Admit();
        Assert.Equal(ResidentAdmissionResult.Homeless, resident.Revalidate(reason));
        Reload(); resident = Open();
        Assert.True(resident.HasResident);
        Assert.Equal("ordinary-resident", resident.Resident);
        Assert.Equal(ResidentJournalPhase.Homeless, resident.Phase);
        Assert.Equal(ResidentAdmissionResult.AlreadyRecorded, resident.Revalidate(HousingRefusal.None));
        Assert.Equal(ResidentJournalPhase.Homeless, resident.Phase);
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("ordinary-resident", Slot(8), new RequestId("rehome")));
        Assert.True(resident.HasResident);
        Assert.Equal(ResidentAdmissionResult.Admitted, resident.Admit("ordinary-resident", Slot(8), new RequestId("rehome")));
    }
    [Fact]
    public void UnloadedHousingPausesWithoutRecordingHomeless()
    {
        var resident = Admit();
        Assert.Equal(ResidentAdmissionResult.NotRecorded, resident.Revalidate(HousingRefusal.NotMeasured));
        Assert.True(resident.HousingUnverified);
        Assert.True(resident.HasResident);
        Assert.Equal(ResidentJournalPhase.Admitted, resident.Phase);
        Assert.Equal(2, _journal.Entries.Count);
    }
    [Fact]
    public void FailedInvalidationPausesRoutineWithoutErasingExistingResident()
    {
        var resident = Admit(); _failSave = true;
        Assert.Equal(ResidentAdmissionResult.NotRecorded, resident.Revalidate(HousingRefusal.NoRoof));
        Assert.True(resident.HasResident); Assert.True(resident.HousingUnverified);
        Assert.Equal(ResidentJournalPhase.Admitted, resident.Phase);
        _failSave = false;
        Assert.Equal(ResidentAdmissionResult.Homeless, resident.Revalidate(HousingRefusal.NoRoof));
    }
    [Fact]
    public void AdmissionRechecksExactBedCurrentRulesAndReservation()
    {
        var resident = Open();
        Assert.Equal(ResidentAdmissionResult.Refused, resident.Reserve("one", Slot(roof: false), _request));
        Assert.Equal(ResidentAdmissionResult.Refused, resident.Reserve("one", Slot(claim: BedClaim.YoursAndCurrent), _request));
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("one", Slot(), _request));
        Assert.Equal(ResidentAdmissionResult.Refused, resident.Admit("one", Slot(2.01f), _request));
        Assert.Equal(ResidentAdmissionResult.Refused, resident.Admit("one", Slot(roof: false), _request));
        Assert.Equal(ResidentAdmissionResult.Refused, resident.Admit("one", Slot(), new RequestId("other")));
        Assert.False(resident.HasResident);
    }
    [Fact]
    public void OrphanAdmissionAndUnsavedRowsAreNeverAdopted()
    {
        _journal.AppendCustody(new ResidentRecordedRow("one", Slot().Key, _request, ResidentJournalPhase.Admitted, HousingRefusal.None), 0, Guid.NewGuid());
        Assert.True(Open().IsBlocked);
        Assert.True(_store.Save(_journal).Saved);
        Reload(); Assert.True(Open().IsBlocked);
    }
    [Fact]
    public void DestroyedReservedBedCanBeReplacedWithoutCreatingAnUnadmittedResident()
    {
        var resident = Open();
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("one", Slot(), _request));
        Assert.Equal(ResidentAdmissionResult.Homeless, resident.Revalidate(HousingRefusal.BedRemoved));
        Reload(); resident = Open(); Assert.False(resident.HasResident);
        Assert.Equal(ResidentAdmissionResult.Reserved, resident.Reserve("one", Slot(8), new RequestId("replacement")));
        Assert.Equal(ResidentAdmissionResult.Admitted, resident.Admit("one", Slot(8), new RequestId("replacement")));
        Assert.True(resident.HasResident);
    }
    [Fact]
    public void DifferentWorldAndSettlementDoNotShareResidents()
    {
        Admit();
        foreach (var scope in new[] { new SettlementScope(12, new SettlementId("home")), new SettlementScope(11, new SettlementId("away")) })
        {
            var other = _store.Load(scope).Journal;
            Assert.False(new ResidentAdmission(other, j => _store.Save(j).Saved, Guid.NewGuid()).HasResident);
        }
    }
    [Theory]
    [InlineData("phase", "future-resident-phase")]
    [InlineData("phase", "127")]
    [InlineData("housing", "future-housing-reason")]
    [InlineData("housing", "127")]
    [InlineData("unrecognized-authority", "yes")]
    public void UnknownResidentAuthorityFieldsNeverDecode(string changed, string value)
    {
        var fields = new JournalFields().Add("resident", "one").Add("slot", Slot().Key).Add("reservation", _request.Value);
        fields.Add("phase", changed == "phase" ? value : "Reserved");
        fields.Add("housing", changed == "housing" ? value : "None");
        if (changed == "unrecognized-authority") fields.Add(changed, value);
        Assert.False(CustodyRowCodec.TryDecode(JournalEntryKind.ResidentRecorded, fields, out _));
    }
    [Fact]
    public void SavedButFalseReturnBlocksUntilReloadInsteadOfDiscardingDurableEvidence()
    {
        var resident = new ResidentAdmission(_journal, j => { Assert.True(_store.Save(j).Saved); return false; }, Guid.NewGuid());
        Assert.Equal(ResidentAdmissionResult.NotRecorded, resident.Reserve("one", Slot(), _request));
        Assert.True(resident.IsBlocked);
        Reload(); Assert.Equal(ResidentJournalPhase.Reserved, Open().Phase);
    }
}
