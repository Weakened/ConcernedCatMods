using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Housing;
namespace TheConcernedCat.Settlement.Journal;
internal enum ResidentJournalPhase { Unspecified, Reserved, Admitted, Homeless }
/// <summary>A resident is not a worker or an order. Use the existing named-field
/// journal lane without manufacturing a material order or touching its ledger.</summary>
internal sealed class ResidentRecordedRow : CustodyRow
{
    public ResidentRecordedRow(string resident,string slot,RequestId reservation,ResidentJournalPhase phase,HousingRefusal housing)
    {
        if(string.IsNullOrWhiteSpace(resident)||string.IsNullOrWhiteSpace(slot)||reservation.IsEmpty||
            phase==ResidentJournalPhase.Unspecified||!Enum.IsDefined(typeof(ResidentJournalPhase),phase)||
            !Enum.IsDefined(typeof(HousingRefusal),housing)||
            ((phase==ResidentJournalPhase.Homeless)!=(housing!=HousingRefusal.None)))
            throw new ArgumentException("A resident event needs a stable identity/slot/request and explicit known housing state.");
        Resident=resident; Slot=slot; Reservation=reservation; Phase=phase; Housing=housing;
    }
    public string Resident { get; }
    public string Slot { get; }
    public RequestId Reservation { get; }
    public ResidentJournalPhase Phase { get; }
    public HousingRefusal Housing { get; }
    public override JournalEntryKind Kind=>JournalEntryKind.ResidentRecorded;
    public override OrderId Order=>default;
}
