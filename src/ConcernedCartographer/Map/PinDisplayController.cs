using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Display-only pin filtering and semantic-zoom clustering. This
/// layer decides which managed pins are rendered on the map right now; the
/// store is never touched, so no filter or cluster can ever cause data
/// loss. Cluster markers are unsaved vanilla pins (m_save=false), so they
/// can never persist, be adopted, or survive uninstall.
///
/// Display-hidden pins are simply untracked+removed renderings; the
/// vanilla-change absorber only inspects tracked pins, so display hiding
/// can never be mistaken for a player deletion.</summary>
internal sealed class PinDisplayController
{
    // Zoom is the fraction of the map texture shown; bigger = further out.
    private const float WorldTierZoom = 0.5f;
    private const float RegionalTierZoom = 0.2f;
    private const float WorldTierCellMeters = 256f;
    private const float RegionalTierCellMeters = 96f;

    private readonly ManualLogSource _log;
    private readonly List<Minimap.PinData> _clusterMarkers = new();
    private readonly HashSet<Guid> _displayHidden = new();
    private bool _disabledForSession;
    private int _lastTier = -1;

    public PinDisplayController(ManualLogSource log)
    {
        _log = log;
    }

    public string QueryText { get; private set; } = "";
    public bool ShowPins { get; set; } = true;
    public bool ClusterEnabled { get; set; } = true;

    public int VisibleCount { get; private set; }
    public int HiddenByFilter { get; private set; }
    public int ClusterCount { get; private set; }

    public void SetQuery(string text)
    {
        QueryText = text ?? "";
    }

    /// <summary>Recomputes what the map shows. Idempotent; call after any
    /// filter/toggle change, store resync, or zoom-tier change.</summary>
    public void Apply(PinStore store, PinAdapter adapter)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            ClearClusterMarkers();
            PinQuery query = PinQuery.Parse(QueryText);

            var wanted = new List<AtlasPin>();
            HiddenByFilter = 0;
            foreach (AtlasPin pin in store.Living)
            {
                if (pin.Archived)
                {
                    continue;
                }

                if (!ShowPins || (!query.IsEmpty && !query.Matches(pin)))
                {
                    HiddenByFilter++;
                    continue;
                }

                wanted.Add(pin);
            }

            float cell = 0f;
            if (ClusterEnabled && MinimapReflection.TryGetLargeZoom(out float zoom))
            {
                cell = zoom >= WorldTierZoom ? WorldTierCellMeters
                    : zoom >= RegionalTierZoom ? RegionalTierCellMeters
                    : 0f;
            }

            PinClusterer.Result clustered = PinClusterer.Compute(wanted, cell);

            var visibleIds = new HashSet<Guid>();
            foreach (AtlasPin pin in clustered.Singles)
            {
                visibleIds.Add(pin.Id.Value);
            }

            // Hide what should not render (filtered out or folded into a
            // cluster), show what should.
            foreach (AtlasPin pin in store.Living)
            {
                if (pin.Archived)
                {
                    continue;
                }

                bool shouldShow = visibleIds.Contains(pin.Id.Value);
                bool currentlyHidden = _displayHidden.Contains(pin.Id.Value);
                if (shouldShow && currentlyHidden)
                {
                    _displayHidden.Remove(pin.Id.Value);
                    adapter.SyncPin(store, pin.Id);
                }
                else if (!shouldShow && !currentlyHidden)
                {
                    _displayHidden.Add(pin.Id.Value);
                    adapter.DisplayHide(pin.Id);
                }
            }

            foreach (PinClusterer.Cluster cluster in clustered.Clusters)
            {
                string label = cluster.DominantCategory.Length > 0
                    ? $"{cluster.Members.Count} × {cluster.DominantCategory}"
                    : $"{cluster.Members.Count} pins";
                var position = new Vector3(cluster.Center.X, 0f, cluster.Center.Z);
                var type = (Minimap.PinType)IconRegistry.ResolveVanillaType(cluster.DominantIconId);
                Minimap.PinData marker = Minimap.instance.AddPin(position, type, label, save: false, isChecked: false);
                _clusterMarkers.Add(marker);
            }

            VisibleCount = clustered.Singles.Count;
            ClusterCount = clustered.Clusters.Count;
        }
        catch (Exception exception)
        {
            Disable(store, exception);
        }
    }

    /// <summary>True when the zoom tier changed since the last check, so
    /// the runtime knows to re-apply.</summary>
    public bool ZoomTierChanged()
    {
        if (!ClusterEnabled || !MinimapReflection.TryGetLargeZoom(out float zoom))
        {
            return false;
        }

        int tier = zoom >= WorldTierZoom ? 2 : zoom >= RegionalTierZoom ? 1 : 0;
        if (tier == _lastTier)
        {
            return false;
        }

        _lastTier = tier;
        return true;
    }

    /// <summary>Removes cluster markers and forgets display state (e.g., on
    /// world switch). Store data is untouched.</summary>
    public void Reset()
    {
        ClearClusterMarkers();
        _displayHidden.Clear();
        _lastTier = -1;
        VisibleCount = 0;
        HiddenByFilter = 0;
        ClusterCount = 0;
    }

    private void ClearClusterMarkers()
    {
        foreach (Minimap.PinData marker in _clusterMarkers)
        {
            try
            {
                Minimap.instance?.RemovePin(marker);
            }
            catch
            {
                // The map may already be tearing down.
            }
        }

        _clusterMarkers.Clear();
        ClusterCount = 0;
    }

    private void Disable(PinStore store, Exception exception)
    {
        _disabledForSession = true;
        _log.LogError($"Pin display controller failed and was disabled for this session (all pins will render plainly): {exception}");
        try
        {
            ClearClusterMarkers();
            _displayHidden.Clear();
        }
        catch
        {
            // Best effort.
        }
    }
}
