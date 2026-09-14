using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Feeds nearby loaded objects to the survey engine CONTINUOUSLY
/// on a small per-tick budget (RC10 feedback 9): a fresh snapshot of the
/// loaded surfaces is walked a slice at a time every frame, so a matching
/// object near the player becomes an observation within about a second
/// instead of waiting out a 10-second timer, while the per-frame cost
/// stays flat and bounded. Disabled by default; never scans the world
/// database — only already-instantiated objects within the configured
/// radius, skipping characters entirely. The engine enforces every
/// anti-flood bound on top. The top-left "new survey observations" toast
/// is COALESCED to at most one per <see cref="NotifyCoalesceSeconds"/>,
/// and only when new observations were actually collected.
///
/// Issue #258: the scanner walks TWO loaded-world surfaces, not one.
/// <see cref="ZNetSceneSightingSource"/> is the networked-object surface it
/// always had; <see cref="LoadedLocationSightingSource"/> is the loaded
/// <c>Location</c> surface where dungeon entrances actually live. Both
/// share one per-tick budget, so the added surface cannot raise the
/// per-frame cost, and both feed the same rules, duplicate suppression,
/// rejection memory, and Accept review.</summary>
internal sealed class SurveyScanner
{
    private const int PerTickExamineBudget = 48;
    private const float NotifyCoalesceSeconds = 10f;

    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>>? InstancesField =
        BuildInstancesRef();

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly ZNetSceneSightingSource _networkedObjects = new();
    private readonly LoadedLocationSightingSource _loadedLocations = new();
    private readonly List<ISurveySightingSource> _sources = new();
    private readonly SurveySweep _sweep = new(PerTickExamineBudget);
    private bool _sweepActive;
    private float _notifyElapsed = NotifyCoalesceSeconds;
    private int _unnotifiedAdded;
    private bool _disabledForSession;

    public SurveyScanner(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _sources.Add(_networkedObjects);
        _sources.Add(_loadedLocations);
    }

    /// <summary>When the last full sweep over the loaded surfaces
    /// completed (UTC), or null before the first. Feeds the panel status.</summary>
    public DateTime? LastScanUtc { get; private set; }

    /// <summary>Loaded objects examined by the last completed sweep.</summary>
    public int LastScanExamined { get; private set; }

    /// <summary>Observations the last completed sweep added.</summary>
    public int LastScanAdded { get; private set; }

    /// <summary>Loaded world locations covered by the newest snapshot
    /// (issue #258 — the surface dungeon entrances live on).</summary>
    public int LastScanLocations { get; private set; }

    /// <summary>True after a scanner failure disabled it for this session
    /// (the panel shows this honestly instead of a silent "no results").</summary>
    public bool DisabledForSession => _disabledForSession;

    /// <summary>False when this game build no longer exposes the loaded
    /// <c>Location</c> surface. The survey still runs on the networked
    /// objects and the panel says so, instead of silently missing
    /// dungeons again.</summary>
    public static bool LocationSurfaceAvailable => LoadedLocationSightingSource.Available;

    /// <summary>Restarts the sweep against a fresh snapshot — the Survey
    /// panel's "Scan now". With continuous scanning this mostly resets the
    /// cursor; results were already arriving every frame.</summary>
    public void RequestImmediateScan()
    {
        _sweepActive = false;
    }

    /// <summary>World switch: drop every snapshot and sweep statistic so
    /// the next world starts from a clean, honest scan state.</summary>
    public void ResetForWorld()
    {
        _sweepActive = false;
        _networkedObjects.Clear();
        _loadedLocations.Clear();
        LastScanUtc = null;
        LastScanExamined = 0;
        LastScanAdded = 0;
        LastScanLocations = 0;
        _unnotifiedAdded = 0;
    }

    public void Tick(float deltaTime, SurveyEngine engine, PinStore pins)
    {
        if (_disabledForSession || !_settings.SurveyRulesEnabled.Value)
        {
            return;
        }

        _notifyElapsed += deltaTime;
        try
        {
            Player player = Player.m_localPlayer;
            if (player is null || ZNetScene.instance == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!_sweepActive || _sweep.Completed)
            {
                // Sweep boundary: publish the finished sweep's stats, apply
                // live bounds, prune expiries, snapshot fresh surfaces.
                if (_sweepActive)
                {
                    LastScanUtc = now;
                    LastScanExamined = _sweep.Examined;
                    LastScanAdded = _sweep.Added;
                }

                engine.MaxObservations = (int)_settings.SurveyMaxObservations.Value;
                engine.BaseExclusionRadiusMeters = _settings.SurveyBaseExclusionRadius.Value;
                engine.Prune(now);

                _networkedObjects.Clear();
                if (InstancesField is not null)
                {
                    foreach (KeyValuePair<ZDO, ZNetView> entry in InstancesField(ZNetScene.instance))
                    {
                        _networkedObjects.Add(entry.Value);
                    }
                }

                _loadedLocations.Refresh();
                LastScanLocations = _loadedLocations.Count;
                if (_networkedObjects.Count == 0 && _loadedLocations.Count == 0)
                {
                    _sweepActive = false;
                    return;
                }

                _sweep.Restart();
                _sweepActive = true;
            }

            Vector3 playerPosition = player.transform.position;
            _unnotifiedAdded += _sweep.Tick(
                _sources,
                new RoadPoint(playerPosition.x, playerPosition.y, playerPosition.z),
                _settings.SurveyScanRadius.Value,
                engine,
                pins,
                now);

            if (_unnotifiedAdded > 0 && _notifyElapsed >= NotifyCoalesceSeconds)
            {
                VanillaMessage.Show(
                    player,
                    MessageHud.MessageType.TopLeft,
                    AtlasStrings.Format("hud.surveyObservations", _unnotifiedAdded));
                _unnotifiedAdded = 0;
                _notifyElapsed = 0f;
            }
        }
        catch (Exception exception)
        {
            _disabledForSession = true;
            _log.LogError($"Survey scanner failed and was disabled for this session: {SafeLogText.Describe(exception)}");
        }
    }

    /// <summary>True when any loaded instance whose prefab name contains
    /// the fragment sits within the radius. Used by the NoMap gate to find
    /// a cartography table; bounded by the loaded-instance set.</summary>
    public static bool AnyInstanceNear(string prefabNameFragment, Vector3 position, float radius)
    {
        if (InstancesField is null || ZNetScene.instance == null)
        {
            return false;
        }

        try
        {
            foreach (KeyValuePair<ZDO, ZNetView> entry in InstancesField(ZNetScene.instance))
            {
                ZNetView view = entry.Value;
                if (view != null && view.gameObject != null &&
                    view.gameObject.name.IndexOf(prefabNameFragment, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    Vector3.Distance(view.transform.position, position) <= radius)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fail open at the caller.
        }

        return false;
    }

    private static AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>>? BuildInstancesRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        }
        catch
        {
            return null;
        }
    }
}
