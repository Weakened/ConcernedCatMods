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
            { Forget(); _record=record; _epoch=epoch; return "The character/world changed. Reopen the introduction and select again."; }
            if(_failed||!_companion.Enabled.Value||record==null) return "Gunnar's introduction is unavailable here.";
            bool authority=service!=null&&service.Authority==WorkAuthorityVerdict.Granted;
            if(action=="cancel") { Cancel(); return Status(record,service); }
            if(action=="status") return Status(record,service);
            if(action=="offer") return record.IsRecruited
                ? "Optional first delivery: open Gunnar's existing hauling panel, choose your cart and plan a trip using your own cargo. This offer adds nothing and starts no job."
                : "Confirm Gunnar's recruitment before planning an optional first delivery.";
            if(record.IsRecruited) return Status(record,service);
            if(action=="choose")
            {
                if(_prompt!=null) return "A confirmation is already open for "+_prompt.Label+". Confirm or Cancel / Close first.";
                var selected=GunnarRecruitmentCartPort.Hovered();
                var choice=GunnarRecruitmentCartPort.Read(selected,_epoch,service?.ActiveLease);
                var result=_flow.Prepare(record,_epoch,choice,authority,out var prompt);
                if(result==GunnarRecruitmentResult.PendingConfirmation)
                { _chosen=selected; _prompt=prompt; _notice="Confirm recruitment using "+prompt!.Label+"? No cart/cargo operation or job will start."; }
                else _notice=Refusal(result,choice.Check);
                return _notice;
            }
            if(action=="confirm")
            {
                if(_prompt==null) return "Point at your cart, then Choose Pointed Cart before confirming.";
                // Re-read THAT object, never a new hover or a nearest-cart search.
                var fresh=GunnarRecruitmentCartPort.Read(_chosen,_epoch,service?.ActiveLease);
                var result=_flow.Confirm(record,_epoch,_prompt.Token,fresh,authority);
                Cancel();
                if(result==GunnarRecruitmentResult.Recruited)
                {
                    _notice=Line("gunnar.complete")+"\nOptional: plan a first delivery using your own cargo in the existing hauling panel.";
                    _log.LogInfo("Gunnar recruitment saved. No cart, worker or cargo operation was performed.");
                }
                else _notice=Refusal(result,fresh.Check);
                return _notice;
            }
            return Status(record,service);
        }
        catch(Exception e) { Fail(e); return "Recruitment stopped after a native/UI error. Existing hauling tools remain available."; }
    }
    private static string Line(string key)=>GunnarDialogueStrings.Resolve(key,k=>TeamsterStrings.HasKey(k)?TeamsterStrings.Get(k):k);
    private string Status(GunnarCompanionRecord record,GunnarHaulService? service)=>record.IsRecruited
        ? "Gunnar is permanently recruited. Work readiness is separate.\n"+Readiness(service)+"\n"+_notice
        : Line("gunnar.objective")+"\nPoint at a nearby real cart, Choose Pointed Cart, then confirm its name. Hauling opt-in, host authority and no connected peers are required.\n"+_notice;
    private static string Readiness(GunnarHaulService? service)
    {
        if(service==null) return "Hauling is unavailable here.";
        if(service.Authority!=WorkAuthorityVerdict.Granted) return "Hauling is paused: enable the hauling switches on the host with no connected peers.";
        if(service.Snapshot.Phase==HaulPhase.NeedsAttention) return "His current trip needs attention: "+service.Executor?.AttentionDetail;
        if(!service.WorkerAvailable) return "Gunnar needs his worker setup in the existing hauling panel before a job can begin.";
        if(service.ActiveLease==null||!service.ActiveLease.IsActive) return "No cart is assigned for a job. Choose one in the existing hauling panel.";
        return "His cart is assigned. Use the existing hauling panel to plan or inspect a trip.";
    }
    private static string Refusal(GunnarRecruitmentResult result,GunnarCartCheck check)
    {
        if(result==GunnarRecruitmentResult.AuthorityRequired) return "Recruitment needs Teamster and Gunnar hauling enabled, Match Player pull strength, the host, and no connected peers. Nothing was recorded.";
        if(result==GunnarRecruitmentResult.NotRecorded) return "Recruitment could not be saved. Select and confirm again when storage is writable.";
        if(result==GunnarRecruitmentResult.Stale) return "That confirmation is no longer current. Choose the exact cart again.";
        if(result==GunnarRecruitmentResult.NeedsIntroduction) return "Read Gunnar's introduction before choosing a cart.";
        if(result==GunnarRecruitmentResult.NeedsSelection) return "Point at a nearby real cart and choose it explicitly.";
        if(check==GunnarCartCheck.WrongKind) return "Choose a hand cart, rather than a siege machine.";
        if(check==GunnarCartCheck.Destroyed) return "The selected cart is gone. Choose a real cart again.";
        if(check==GunnarCartCheck.Unloaded) return "The selected cart is not fully loaded. Return to it and select again.";
        if(check==GunnarCartCheck.Unauthorized) return "The selected cart is not available to this host. Its ownership is left alone.";
        if(check==GunnarCartCheck.InUse) return "The selected cart is occupied or in use. Free it before selecting again.";
        if(check==GunnarCartCheck.OutOfReach) return "Return within reach of the selected cart, then select again.";
        if(check==GunnarCartCheck.Leased) return "Gunnar already has a different cart assigned. Resolve that assignment in the hauling panel first.";
        return "The cart could not be checked. Nothing was recorded; select it again when available.";
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
