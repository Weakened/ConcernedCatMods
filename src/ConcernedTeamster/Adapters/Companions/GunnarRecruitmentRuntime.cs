using System;
using BepInEx.Logging;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Adapters.Workers;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Ui.Companions;
using TheConcernedCat.Workers;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Explicit cart proof and permanent story recruitment, with fresh
/// existing hauling authority. No assignment, job, body or cargo operation.</summary>
internal sealed class GunnarRecruitmentRuntime
{
    private readonly GunnarCompanionAdapter _companion;
    private readonly Func<GunnarHaulService?> _service;
    private readonly ManualLogSource _log;
    private readonly GunnarRecruitment _flow=new();
    private readonly GunnarRecruitmentPanel _panel;
    private GunnarCompanionRecord? _record;
    private Guid _epoch;
    private GunnarRecruitmentPrompt? _prompt;
    private Vagon? _chosen;
    private bool _failed;
    private string _notice=string.Empty;
    private GunnarRecruitmentRuntime(GunnarCompanionAdapter companion,Func<GunnarHaulService?> service,ManualLogSource log)
    { _companion=companion; _service=service; _log=log; _panel=new GunnarRecruitmentPanel(Act,Fail); }
    internal static GunnarRecruitmentRuntime? Install(GunnarCompanionAdapter? companion,Func<GunnarHaulService?> service,ManualLogSource log)
    {
        if(companion==null) return null;
        try { return new GunnarRecruitmentRuntime(companion,service,log); }
        catch(Exception e) { log.LogWarning("Gunnar recruitment UI unavailable ("+e.GetType().Name+"). Existing hauling tools remain available."); return null; }
    }
    internal void Tick()
    {
        if(_failed) return;
        try
        {
            var record=_companion.Current(); var epoch=_service()?.WorldLoadEpoch??Guid.Empty;
            if(!ReferenceEquals(record,_record)||epoch!=_epoch) { Forget(); _record=record; _epoch=epoch; }
            bool show=_companion.Enabled.Value&&record!=null&&
                (record.State==QuestState.IntroPending||record.State==QuestState.Skipped||record.IsRecruited);
            if(!show) Cancel();
            _panel.Tick(show);
        }
        catch(Exception e) { Fail(e); }
    }
    private string Act(string action)
    {
        try
        {
            var record=_companion.Current(); var service=_service(); var epoch=service?.WorldLoadEpoch??Guid.Empty;
            if(!ReferenceEquals(record,_record)||epoch!=_epoch)
            { Forget(); _record=record; _epoch=epoch; return L("gunnar.recruit.scope_changed"); }
            if(_failed||!_companion.Enabled.Value||record==null) return L("gunnar.recruit.unavailable");
            bool authority=service!=null&&service.Authority==WorkAuthorityVerdict.Granted;
            if(action=="cancel") { Cancel(); return Status(record,service); }
            if(action=="status") return Status(record,service);
            if(action=="offer") return record.IsRecruited
                ? L("gunnar.recruit.delivery_offer")
                : L("gunnar.recruit.not_recruited");
            if(record.IsRecruited) return Status(record,service);
            if(action=="choose")
            {
                if(_prompt!=null) return F("gunnar.recruit.pending",_prompt.Label);
                var selected=GunnarRecruitmentCartPort.Hovered();
                var choice=GunnarRecruitmentCartPort.Read(selected,_epoch,service?.ActiveLease);
                var result=_flow.Prepare(record,_epoch,choice,authority,out var prompt);
                if(result==GunnarRecruitmentResult.PendingConfirmation)
                { _chosen=selected; _prompt=prompt; _notice=F("gunnar.recruit.question",prompt!.Label); }
                else _notice=Refusal(result,choice.Check);
                return _notice;
            }
            if(action=="confirm")
            {
                if(_prompt==null) return L("gunnar.recruit.select_first");
                // Re-read THAT object, never a new hover or a nearest-cart search.
                var fresh=GunnarRecruitmentCartPort.Read(_chosen,_epoch,service?.ActiveLease);
                var result=_flow.Confirm(record,_epoch,_prompt.Token,fresh,authority);
                Cancel();
                if(result==GunnarRecruitmentResult.Recruited)
                {
                    _notice=Line("gunnar.complete")+"\n"+L("gunnar.recruit.optional_tip");
                    _log.LogInfo("Gunnar recruitment saved. No cart, worker or cargo operation was performed.");
                }
                else _notice=Refusal(result,fresh.Check);
                return _notice;
            }
            return Status(record,service);
        }
        catch(Exception e) { Fail(e); return L("gunnar.recruit.surface_failure"); }
    }
    private static string L(string key)=>GunnarRecruitmentStrings.Resolve(key,k=>TeamsterStrings.HasKey(k)?TeamsterStrings.Get(k):k);
    private static string F(string key,params object[] values)=>string.Format(System.Globalization.CultureInfo.InvariantCulture,L(key),values);
    private static string Line(string key)=>GunnarDialogueStrings.Resolve(key,k=>TeamsterStrings.HasKey(k)?TeamsterStrings.Get(k):k);
    private string Status(GunnarCompanionRecord record,GunnarHaulService? service)=>record.IsRecruited
        ? L("gunnar.recruit.permanent")+"\n"+Readiness(service)+"\n"+_notice
        : Line("gunnar.objective")+"\n"+L("gunnar.recruit.instructions")+"\n"+_notice;
    private static string Readiness(GunnarHaulService? service)
    {
        if(service==null) return L("gunnar.recruit.work_unavailable");
        if(service.Authority!=WorkAuthorityVerdict.Granted) return L("gunnar.recruit.work_authority");
        if(service.Snapshot.Phase==HaulPhase.NeedsAttention) return F("gunnar.recruit.attention",service.Executor?.AttentionDetail??string.Empty);
        if(!service.WorkerAvailable) return L("gunnar.recruit.worker_setup");
        if(service.ActiveLease==null||!service.ActiveLease.IsActive) return L("gunnar.recruit.assign_cart");
        return L("gunnar.recruit.cart_assigned");
    }
    private static string Refusal(GunnarRecruitmentResult result,GunnarCartCheck check)
    {
        if(result==GunnarRecruitmentResult.AuthorityRequired) return L("gunnar.recruit.authority_required");
        if(result==GunnarRecruitmentResult.NotRecorded) return L("gunnar.recruit.save_failed");
        if(result==GunnarRecruitmentResult.Stale) return L("gunnar.recruit.stale");
        if(result==GunnarRecruitmentResult.NeedsIntroduction) return L("gunnar.recruit.intro_required");
        if(result==GunnarRecruitmentResult.NeedsSelection) return L("gunnar.recruit.selection_required");
        if(check==GunnarCartCheck.WrongKind) return L("gunnar.recruit.wrong_kind");
        if(check==GunnarCartCheck.Destroyed) return L("gunnar.recruit.destroyed");
        if(check==GunnarCartCheck.Unloaded) return L("gunnar.recruit.unloaded");
        if(check==GunnarCartCheck.Unauthorized) return L("gunnar.recruit.ownership");
        if(check==GunnarCartCheck.InUse) return L("gunnar.recruit.in_use");
        if(check==GunnarCartCheck.OutOfReach) return L("gunnar.recruit.reach");
        if(check==GunnarCartCheck.Leased) return L("gunnar.recruit.conflicting_lease");
        return L("gunnar.recruit.unknown_cart");
    }
    private void Cancel() { _flow.Forget(); _prompt=null; _chosen=null; _notice=string.Empty; }
    internal void Forget() { Cancel(); _panel.Dispose(); _record=null; _epoch=Guid.Empty; _notice=string.Empty; }
    private void Fail(Exception e)
    {
        if(_failed) return;
        _failed=true; Forget();
        _log.LogWarning("Gunnar recruitment surface disabled after "+e.GetType().Name+
            ". Check the cart/game API and UI version; the companion record and existing hauling tools are preserved.");
    }
}
