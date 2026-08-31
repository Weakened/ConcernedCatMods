using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Feeds nearby loaded objects to the survey engine on a slow,
/// budgeted cadence. Disabled by default; never scans the world database —
/// only already-instantiated ZNetViews within the configured radius, at
/// most <see cref="ScanBudget"/> per scan, skipping characters entirely.
/// The engine enforces every anti-flood bound on top.</summary>
internal sealed class SurveyScanner
{
    private const int ScanBudget = 300;

    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>>? InstancesField =
        BuildInstancesRef();

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly List<ZNetView> _buffer = new();
    private float _elapsed;
    private int _cursor;
    private bool _disabledForSession;

    public SurveyScanner(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>When the last scan pass ran (UTC), or null before the
    /// first. Feeds the Survey panel status.</summary>
    public DateTime? LastScanUtc { get; private set; }

    /// <summary>Loaded objects examined by the last scan pass.</summary>
    public int LastScanExamined { get; private set; }

    /// <summary>Observations the last scan pass added.</summary>
    public int LastScanAdded { get; private set; }

    /// <summary>True after a scanner failure disabled it for this session
    /// (the panel shows this honestly instead of a silent "no results").</summary>
    public bool DisabledForSession => _disabledForSession;

    /// <summary>Makes the next enabled tick scan immediately instead of
    /// waiting out the cadence — the Survey panel's "Scan now".</summary>
    public void RequestImmediateScan()
    {
        _elapsed = float.MaxValue;
    }

    public void Tick(float deltaTime, SurveyEngine engine, PinStore pins)
    {
        if (_disabledForSession || !_settings.SurveyRulesEnabled.Value)
        {
            return;
        }

        _elapsed += deltaTime;
        if (_elapsed < _settings.SurveyScanIntervalSeconds.Value)
        {
            return;
        }

        _elapsed = 0f;
        try
        {
            Player player = Player.m_localPlayer;
            if (player is null || InstancesField is null || ZNetScene.instance == null)
            {
                return;
            }

            engine.MaxObservations = (int)_settings.SurveyMaxObservations.Value;
            engine.BaseExclusionRadiusMeters = _settings.SurveyBaseExclusionRadius.Value;
            DateTime now = DateTime.UtcNow;
            engine.Prune(now);

            _buffer.Clear();
            foreach (KeyValuePair<ZDO, ZNetView> entry in InstancesField(ZNetScene.instance))
            {
                _buffer.Add(entry.Value);
            }

            Vector3 playerPosition = player.transform.position;
            float radius = _settings.SurveyScanRadius.Value;
            LastScanUtc = now;
            int added = 0;
            int examined = 0;
            while (examined < ScanBudget && _buffer.Count > 0)
            {
                examined++;
                _cursor = (_cursor + 1) % _buffer.Count;
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
                    added++;
                }
                else if (result == SurveyEngine.OfferResult.CapReached)
                {
                    break;
                }
            }

            LastScanExamined = examined;
            LastScanAdded = added;
            if (added > 0)
            {
                player.Message(
                    MessageHud.MessageType.TopLeft,
                    AtlasStrings.Format("hud.surveyObservations", added));
            }
        }
        catch (Exception exception)
        {
            _disabledForSession = true;
            _log.LogError($"Survey scanner failed and was disabled for this session: {exception}");
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
