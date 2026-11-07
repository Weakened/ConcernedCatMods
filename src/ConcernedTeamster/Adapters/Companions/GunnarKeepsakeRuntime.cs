using System;
using BepInEx.Logging;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Actual plugin path to #457's shared local keepsake. Read the
/// existing census only; bounded physics runs at most once a second near home.</summary>
internal sealed class GunnarKeepsakeRuntime
{
    private readonly GunnarCompanionAdapter _companion;
    private readonly Func<bool?> _worker;
    private readonly Action<string> _log;
    private readonly GunnarKeepsakeEnvironment _environment=new();
    private readonly GunnarKeepsakePresenter _presenter;
    private GunnarCompanionRecord? _record;
    private CompanionAnchor _anchor;
    private float _nextPlan;
    private bool _failedThisScope;
    private string? _lastEnvironmentNotice;
    public GunnarKeepsakeRuntime(GunnarCompanionAdapter companion,Func<bool?> worker,Action<string> log)
    {
        _companion=companion; _worker=worker; _log=log;
        _presenter=new GunnarKeepsakePresenter(companion,worker,Notice,BeginIntroduction);
    }
    public static GunnarKeepsakeRuntime? Install(GunnarCompanionAdapter? companion,
        Func<WorkerBodyStatus> census,Func<bool> liveBody,ManualLogSource log)
    {
        if(companion==null) return null;
        try { return new GunnarKeepsakeRuntime(companion,()=>GunnarWorkerPresence.From(census(),liveBody()),
            text=>log.LogInfo(text)); }
        catch(Exception exception)
        { log.LogWarning("Gunnar buckle unavailable ("+exception.GetType().Name+"). Existing tools remain available."); return null; }
    }
    public void Tick()
    {
        try
        {
            GunnarCompanionRecord? current=_companion.Current();
            if(!ReferenceEquals(current,_record)) { Forget(); _record=current; }
            if(current!=null&&current.State==QuestState.Collected)
            {
                // The pickup already reached disk; a failed second save must
                // never strand the story or make a second buckle. Retry quietly.
                _presenter.Forget();
                if(_companion.Enabled.Value&&Time.unscaledTime>=_nextPlan)
                { _nextPlan=Time.unscaledTime+1f; GunnarKeepsakeRecovery.ResumeCollected(current,true); }
                return;
            }
            if(!GunnarKeepsake.Eligible(current,_companion.Enabled.Value,_worker()))
            { _presenter.Forget(); return; }
            if(Time.unscaledTime<_nextPlan) return;
            _nextPlan=Time.unscaledTime+1f;
            if(!_environment.TryAnchor(out CompanionAnchor anchor)) { _presenter.Forget(); ReportEnvironment(); return; }
            Player player=Player.m_localPlayer;
            if(player==null||(player.transform.position-new Vector3(anchor.Position.X,anchor.Position.Y,anchor.Position.Z)).sqrMagnitude>30f*30f)
            { _presenter.Forget(); return; }
            if(anchor.DiffersFrom(_anchor,0.1f)) { _failedThisScope=false; _anchor=anchor; }
            if(_failedThisScope) return;
            _presenter.Refresh(anchor,_environment,_environment.OtherMementos(anchor));
            ReportEnvironment();
            if(_presenter.LastRefusal.Length!=0)
            { _failedThisScope=true; _log("Gunnar buckle unavailable until anchor/world refresh: "+_presenter.LastRefusal); }
        }
        catch(Exception exception)
        {
            _presenter.Forget();
            if(!_failedThisScope) _log("Gunnar buckle paused ("+exception.GetType().Name+"). Existing tools are unchanged.");
            _failedThisScope=true;
        }
    }
    private void BeginIntroduction()
    {
        GunnarCompanionRecord? record=_companion.Current();
        if(record==null||!record.BeginIntroduction())
        { Notice(record?.Notice ?? "The introduction could not be recorded; it will resume when storage is writable."); return; }
        Notice(GunnarKeepsake.Inscription+"\n"+Line("gunnar.intro")+"\n"+Line("gunnar.objective"));
    }
    private static string Line(string key)=>GunnarDialogueStrings.Resolve(key,
        k=>TeamsterStrings.HasKey(k)?TeamsterStrings.Get(k):k);
    private void Notice(string text)
    {
        _log(text);
        MessageHud hud=MessageHud.instance;
        if(hud!=null) hud.ShowMessage(MessageHud.MessageType.Center,text);
    }
    private void ReportEnvironment()
    {
        string? notice=_environment.Limitation;
        if(notice!=null&&notice!=_lastEnvironmentNotice) { _lastEnvironmentNotice=notice; _log(notice); }
    }
    public void Forget()
    {
        _presenter.Forget(); _environment.Forget(); _record=null; _anchor=default;
        _nextPlan=0; _failedThisScope=false; _lastEnvironmentNotice=null;
    }
}
