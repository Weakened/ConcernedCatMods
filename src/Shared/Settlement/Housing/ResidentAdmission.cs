using System;
using System.Linq;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;
namespace TheConcernedCat.Settlement.Housing;
internal enum ResidentAdmissionResult { Refused, Reserved, Admitted, AlreadyRecorded, Homeless, NotRecorded }
/// <summary>The measured slot's exact world-space identity, not the rounded
/// human-facing housing label and never a native object's transient identifier.</summary>
internal readonly struct ResidentHousingSlot
{
    public ResidentHousingSlot(SitePoint position,HousingFacts facts)
    {
        if(!Finite(position.X)||!Finite(position.Y)||!Finite(position.Z)) throw new ArgumentException("A slot has a finite position.");
        Key=position.X.ToString("R",CultureInfo.InvariantCulture)+";"+position.Y.ToString("R",CultureInfo.InvariantCulture)+";"+
            position.Z.ToString("R",CultureInfo.InvariantCulture); Facts=facts;
    }
    private ResidentHousingSlot(string key, HousingFacts facts) { Key=key; Facts=facts; }
    internal static ResidentHousingSlot Missing(string key) => new ResidentHousingSlot(key,
        new HousingFacts(key,true,true,false,0f,false,BedClaim.Unclaimed,exists:false));
    public string Key { get; }
    public HousingFacts Facts { get; }
    public bool IsHabitable=>!string.IsNullOrEmpty(Key)&&HousingRules.Judge(Facts)==HousingRefusal.None;
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
}
/// <summary>One ordinary presentation resident, reserved BEFORE admitted.
/// No figure, job, worker identity, inventory or native state is owned here.
/// The adapter supplies the CURRENT measured bed for each explicit operation.</summary>
internal sealed class ResidentAdmission
{
    private readonly SettlementJournal _journal;
    private readonly Func<SettlementJournal,bool> _save;
    private readonly Guid _epoch;
    private ResidentRecordedRow? _last;
    private bool _everAdmitted;
    public ResidentAdmission(SettlementJournal journal,Func<SettlementJournal,bool> save,Guid epoch)
    {
        _journal=journal; _save=save; _epoch=epoch;
        IsBlocked=journal==null||save==null||epoch==Guid.Empty;
        Refresh();
    }
    private bool Refresh()
    {
        if(IsBlocked) return false;
        if(_journal.IsReadOnly||_journal.IsDirty) { IsBlocked=true; return false; }
        _last=null;
        _everAdmitted=false;
        foreach(JournalEntry entry in _journal.Entries.OrderBy(e=>e.Sequence))
        {
            if(!(entry.Custody is ResidentRecordedRow row)) continue;
            if(!CanFollow(row)) { IsBlocked=true; return false; }
            Adopt(row);
        }
        return true;
    }
    public bool IsBlocked { get; private set; }
    public bool HasResident=>!IsBlocked&&_everAdmitted;
    public string? Resident=>_last?.Resident;
    public string? Slot=>_last?.Slot;
    public ResidentJournalPhase Phase=>_last?.Phase ?? ResidentJournalPhase.Unspecified;
    public HousingRefusal Housing=>_last?.Housing ?? HousingRefusal.NotMeasured;
    public bool HousingUnverified { get; private set; }
    public RequestId Reservation=>_last?.Reservation ?? default;
    public ResidentAdmissionResult Reserve(string resident,ResidentHousingSlot slot,RequestId request)
    {
        if(!Refresh()||!slot.IsHabitable||request.IsEmpty||string.IsNullOrWhiteSpace(resident)) return ResidentAdmissionResult.Refused;
        if(_last!=null&&_last.Resident!=resident) return ResidentAdmissionResult.Refused; // one ordinary resident, not a population simulator
        if(_last!=null&&_last.Phase==ResidentJournalPhase.Reserved)
            return _last.Slot==slot.Key&&_last.Reservation.Equals(request) ? ResidentAdmissionResult.AlreadyRecorded : ResidentAdmissionResult.Refused;
        if(_last!=null&&_last.Phase==ResidentJournalPhase.Admitted) return ResidentAdmissionResult.Refused;
        return Write(new ResidentRecordedRow(resident,slot.Key,request,ResidentJournalPhase.Reserved,HousingRefusal.None))
            ? ResidentAdmissionResult.Reserved : ResidentAdmissionResult.NotRecorded;
    }
    public ResidentAdmissionResult Admit(string resident,ResidentHousingSlot current,RequestId reservation)
    {
        if(!Refresh()||!current.IsHabitable||_last==null||_last.Resident!=resident||_last.Slot!=current.Key||
            !_last.Reservation.Equals(reservation)) return ResidentAdmissionResult.Refused;
        if(_last.Phase==ResidentJournalPhase.Admitted) return ResidentAdmissionResult.AlreadyRecorded;
        if(_last.Phase!=ResidentJournalPhase.Reserved) return ResidentAdmissionResult.Refused;
        return Write(new ResidentRecordedRow(resident,current.Key,reservation,ResidentJournalPhase.Admitted,HousingRefusal.None))
            ? ResidentAdmissionResult.Admitted : ResidentAdmissionResult.NotRecorded;
    }
    public ResidentAdmissionResult Revalidate(HousingRefusal current)
    {
        if(!Refresh()||_last==null||!Enum.IsDefined(typeof(HousingRefusal),current)) return ResidentAdmissionResult.Refused;
        HousingUnverified=current!=HousingRefusal.None;
        if(current==HousingRefusal.NotMeasured) return ResidentAdmissionResult.NotRecorded; // unloaded is not homeless
        if(current==HousingRefusal.None||(_last.Phase==ResidentJournalPhase.Homeless&&_last.Housing==current))
            return ResidentAdmissionResult.AlreadyRecorded; // a repaired home never silently readmits a resident
        return Write(new ResidentRecordedRow(_last.Resident,_last.Slot,_last.Reservation,ResidentJournalPhase.Homeless,current))
            ? ResidentAdmissionResult.Homeless : ResidentAdmissionResult.NotRecorded;
    }
    private bool CanFollow(ResidentRecordedRow row)
    {
        if(_last==null) return row.Phase==ResidentJournalPhase.Reserved;
        if(_last.Resident!=row.Resident) return false;
        if(row.Phase==ResidentJournalPhase.Reserved)
            return _last.Phase==ResidentJournalPhase.Homeless||(_last.Phase==ResidentJournalPhase.Reserved&&
                _last.Slot==row.Slot&&_last.Reservation.Equals(row.Reservation));
        return _last.Slot==row.Slot&&_last.Reservation.Equals(row.Reservation)&&
            (row.Phase==ResidentJournalPhase.Homeless||row.Phase==ResidentJournalPhase.Admitted&&
                (_last.Phase==ResidentJournalPhase.Reserved||_last.Phase==ResidentJournalPhase.Admitted));
    }
    private void Adopt(ResidentRecordedRow row)
    { _last=row; _everAdmitted|=row.Phase==ResidentJournalPhase.Admitted; HousingUnverified=row.Housing!=HousingRefusal.None; }
    private bool Write(ResidentRecordedRow row)
    {
        if(!CanFollow(row)) return false;
        int before=_journal.Entries.Count;
        _journal.AppendCustody(row,0,_epoch);
        bool saved;
        try { saved=_save(_journal); } catch { saved=false; }
        if(!saved)
        {
            if(!_journal.TryDiscardUnsaved(before)) IsBlocked=true;
            return false;
        }
        _journal.MarkClean(); Adopt(row); return true;
    }
}
