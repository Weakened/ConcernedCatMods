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

    // RC12 blockers 5/6: pins the player JUST created (palette birth,
    // survey accept, quick pin) that must stay individually visible —
    // exempt from query filtering and cluster folding — until the player
    // changes the zoom tier or the display state resets. Without this, a
    // marker born next to existing pins folded into a cluster (or fell to
    // an active search filter) the same frame its creation flow closed,
    // which read as the marker disappearing.
    private readonly HashSet<Guid> _stickyVisible = new();
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

    /// <summary>True when this layer is currently hiding the pin's
    /// rendering (filtered out or folded into a cluster). The in-session
    /// batch sync uses this to leave hidden pins hidden (DEF-v1.0-004).</summary>
    public bool IsDisplayHidden(AtlasPin pin)
    {
        return _displayHidden.Contains(pin.Id.Value);
    }

    /// <summary>Marks a just-created pin as sticky-visible (RC12 blockers
    /// 5/6): it renders as itself — never folded into a cluster, never
    /// dropped by the search filter — until the zoom tier changes or the
    /// display state resets. Call before the Apply that follows the
    /// creating operation.</summary>
    public void MarkStickyVisible(AtlasId id)
    {
        _stickyVisible.Add(id.Value);
    }

    /// <summary>Recomputes what the map shows. Idempotent; call after any
    /// filter/toggle change, store resync, or zoom-tier change.</summary>
    public void Apply(PinStore store, PinAdapter adapter)
    {
        if (_disabledForSession)
        {
            return;
        }

        // RC14 fix 5: on teardown frames no Minimap exists; adding cluster
        // markers through it anyway threw NullReferenceException and
        // latched the session disable. A missing map makes Apply a no-op —
        // the map-available path re-applies against the fresh map.
        Minimap map = Minimap.instance;
        if (map == null)
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

                // Sticky pins bypass the query filter (RC12 blockers 5/6);
                // the ShowPins master switch still applies to everything.
                bool sticky = _stickyVisible.Contains(pin.Id.Value);
                if (!ShowPins || (!sticky && !query.IsEmpty && !query.Matches(pin)))
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

            PinClusterer.Result clustered = PinClusterer.Compute(
                wanted, cell, alwaysVisible: _stickyVisible.Count > 0 ? _stickyVisible : null);

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
                Minimap.PinData marker = map.AddPin(position, type, label, save: false, isChecked: false);

                // RC14 fix 1: a cluster dominated by a cc:* icon wears that
                // icon's CC sprite too — previously it fell back to the
                // saved vanilla type, which for several cc:* icons is the
                // Dot (part of the "markers become Dots" report). Unsaved
                // marker, so the sprite stays rendering-only by
                // construction.
                if (CcIconSprites.TryGet(cluster.DominantIconId, out Sprite clusterSprite))
                {
                    marker.m_icon = clusterSprite;
                    if (marker.m_iconElement != null)
                    {
                        marker.m_iconElement.sprite = clusterSprite;
                    }
                }

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

        // A deliberate zoom-tier change ends the sticky-visibility grace of
        // just-created pins (RC12 blockers 5/6): normal folding resumes.
        _stickyVisible.Clear();
        _lastTier = tier;
        return true;
    }

    /// <summary>Removes cluster markers and forgets display state (e.g., on
    /// world switch). Store data is untouched. RC14 fix 5: a world/map
    /// boundary also un-latches the fail-soft session disable — the
    /// controller outlives every game session, and an uncleared latch kept
    /// filtering/clustering dead (and cluster markers un-sprited) for the
    /// rest of the process after one teardown-frame failure.</summary>
    public void Reset()
    {
        ClearClusterMarkers();
        _displayHidden.Clear();
        _stickyVisible.Clear();
        _lastTier = -1;
        VisibleCount = 0;
        HiddenByFilter = 0;
        ClusterCount = 0;
        _disabledForSession = false;
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
