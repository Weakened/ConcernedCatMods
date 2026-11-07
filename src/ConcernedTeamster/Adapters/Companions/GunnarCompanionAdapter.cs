using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Read-only game identity seam for #456, installed from Plugin.Awake
/// and refreshed by its Update/teardown lifecycle. No worker settings are read or
/// changed. No body, memento, cart, inventory or world object is created here.</summary>
internal sealed class GunnarCompanionAdapter
{
    private readonly GunnarCompanionSession _session;
    private readonly ManualLogSource _log;
    private bool _scopeFailureReported;
    private GunnarCompanionRecord? _lastRecord;
    private string? _lastNotice;

    private GunnarCompanionAdapter(ConfigFile config, ManualLogSource log)
    {
        _session = new GunnarCompanionSession(TripRecordingService.SidecarDirectory);
        _log = log;
        Enabled = config.Bind("Companions", "Gunnar", GunnarCompanionRecord.PresentationEnabledByDefault,
            "Show Gunnar's introduction and local companion figure. Off hides both; existing Teamster tools and worker switches are unchanged.");
        DisplayName = config.Bind("Companions", "GunnarDisplayName", GunnarCompanionRecord.DefaultDisplayName,
            "Editable display name for Gunnar. This does not change his saved identity.");
    }

    public static GunnarCompanionAdapter Bind(ConfigFile config, ManualLogSource log) => new(config, log);

    public static GunnarCompanionAdapter? Install(ConfigFile config, ManualLogSource log)
    {
        try { return Bind(config,log); }
        catch (Exception exception)
        {
            log.LogWarning("Gunnar companion configuration is unavailable ("+exception.GetType().Name+
                "). Check the Companions section in Teamster config. Existing cart tools remain available.");
            return null;
        }
    }

    public void Tick()
    {
        GunnarCompanionRecord? record=Current();
        if (!ReferenceEquals(record,_lastRecord)) { _lastRecord=record; _lastNotice=null; }
        string? notice=record?.Notice;
        if (notice!=null && !string.Equals(notice,_lastNotice,StringComparison.Ordinal))
        { _lastNotice=notice; _log.LogWarning("Gunnar companion record: "+notice); }
    }
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<string> DisplayName { get; }

    /// <summary>Call at each interaction/presentation tick, never keep a record
    /// across world changes. Profile id and world uid are the same read-only
    /// native surfaces used by the existing Thorstein quest adapter.</summary>
    public GunnarCompanionRecord? Current()
    {
        try
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || Game.instance == null)
                return _session.Resolve(0, 0);
            PlayerProfile profile = Game.instance.GetPlayerProfile();
            return _session.Resolve(ZNet.instance.GetWorldUID(), profile != null ? profile.GetPlayerID() : 0L);
        }
        catch (Exception exception)
        {
            _session.Forget();
            if (!_scopeFailureReported)
            {
                _scopeFailureReported = true;
                _log.LogWarning("Gunnar's introduction cannot identify this character/world (" +
                    exception.GetType().Name + "). Existing tools remain available; recruitment is refused.");
            }
            return null;
        }
    }

    public void Forget()
    {
        _session.Forget(); _lastRecord=null; _lastNotice=null; _scopeFailureReported=false;
    }
}
