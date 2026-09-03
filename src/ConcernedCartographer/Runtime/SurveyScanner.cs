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
/// on a small per-tick budget (RC10 feedback 9): a fresh instance snapshot
/// is walked a slice at a time every frame, so a matching object near the
/// player becomes an observation within about a second instead of waiting
/// out a 10-second timer, while the per-frame cost stays flat and bounded.
/// Disabled by default; never scans the world database — only
/// already-instantiated ZNetViews within the configured radius, skipping
/// characters entirely. The engine enforces every anti-flood bound on top.
/// The top-left "new survey observations" toast is COALESCED to at most
/// one per <see cref="NotifyCoalesceSeconds"/>, and only when new
/// observations were actually collected.</summary>
internal sealed class SurveyScanner
{
    private const int PerTickExamineBudget = 48;
    private const float NotifyCoalesceSeconds = 10f;

    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>>? InstancesField =
        BuildInstancesRef();

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly List<ZNetView> _buffer = new();
    private int _cursor;
    private int _sweepExamined;
    private int _sweepAdded;
    private float _notifyElapsed = NotifyCoalesceSeconds;
    private int _unnotifiedAdded;
    private bool _disabledForSession;

    public SurveyScanner(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>When the last full sweep over the loaded instances
    /// completed (UTC), or null before the first. Feeds the panel status.</summary>
    public DateTime? LastScanUtc { get; private set; }

    /// <summary>Loaded objects examined by the last completed sweep.</summary>
    public int LastScanExamined { get; private set; }

    /// <summary>Observations the last completed sweep added.</summary>
    public int LastScanAdded { get; private set; }

    /// <summary>True after a scanner failure disabled it for this session
    /// (the panel shows this honestly instead of a silent "no results").</summary>
    public bool DisabledForSession => _disabledForSession;

    /// <summary>Restarts the sweep against a fresh snapshot — the Survey
    /// panel's "Scan now". With continuous scanning this mostly resets the
    /// cursor; results were already arriving every frame.</summary>
    public void RequestImmediateScan()
    {
        _buffer.Clear();
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
            if (player is null || InstancesField is null || ZNetScene.instance == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (_buffer.Count == 0 || _cursor >= _buffer.Count)
            {
                // Sweep boundary: publish the finished sweep's stats, apply
                // live bounds, prune expiries, snapshot fresh instances.
                if (_buffer.Count > 0)
                {
                    LastScanUtc = now;
                    LastScanExamined = _sweepExamined;
                    LastScanAdded = _sweepAdded;
                }

                _sweepExamined = 0;
                _sweepAdded = 0;
                _cursor = 0;
                engine.MaxObservations = (int)_settings.SurveyMaxObservations.Value;
                engine.BaseExclusionRadiusMeters = _settings.SurveyBaseExclusionRadius.Value;
                engine.Prune(now);

                _buffer.Clear();
                foreach (KeyValuePair<ZDO, ZNetView> entry in InstancesField(ZNetScene.instance))
                {
                    _buffer.Add(entry.Value);
                }

                if (_buffer.Count == 0)
                {
                    return;
                }
            }

            Vector3 playerPosition = player.transform.position;
            float radius = _settings.SurveyScanRadius.Value;
            int sliceEnd = Math.Min(_cursor + PerTickExamineBudget, _buffer.Count);
            for (; _cursor < sliceEnd; _cursor++)
            {
                _sweepExamined++;
                ZNetView view = _buffer[_cursor];
                if (view == null || view.gameObject == null)
                {
                    continue;
                }

                Vector3 position = view.transform.position;
                if (Vector3.Distance(position, playerPosition) > radius ||
                    view.GetComponent<Character>() != null)
                {
                    continue;
                }

                SurveyEngine.OfferResult result = engine.Offer(
                    view.gameObject.name,
                    new RoadPoint(position.x, position.y, position.z),
                    pins,
                    now);
                if (result == SurveyEngine.OfferResult.Added)
                {
                    _sweepAdded++;
                    _unnotifiedAdded++;
                }
                else if (result == SurveyEngine.OfferResult.CapReached)
                {
                    _cursor = _buffer.Count;
                    break;
                }
            }

            if (_unnotifiedAdded > 0 && _notifyElapsed >= NotifyCoalesceSeconds)
            {
                player.Message(
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
