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

            if (added > 0)
            {
                player.Message(
                    MessageHud.MessageType.TopLeft,
                    $"Survey: {added} new observation(s) — review with cc_survey");
            }
        }
        catch (Exception exception)
        {
            _disabledForSession = true;
            _log.LogError($"Survey scanner failed and was disabled for this session: {exception}");
        }
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
