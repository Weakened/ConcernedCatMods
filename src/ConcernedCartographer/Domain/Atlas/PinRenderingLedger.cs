using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pure decision core of the map pin adapter (DEF-v1.0-004): owns
/// the AtlasId↔rendering tracking table and every match/sync decision,
/// leaving the game adapter to only execute them against the real map.
/// THandle is the opaque map rendering object (Minimap.PinData in game,
/// fakes in tests); its visual state is read through the stateOf reader.
///
/// Lifecycle contract:
/// - In-session mutations are synced through <see cref="DecideSync"/>,
///   which targets the TRACKED rendering — a renamed pin replaces its own
///   rendering instead of orphaning it.
/// - <see cref="Reset"/> plus <see cref="ClaimMatch"/> (position + exact
///   name, each rendering claimable once) is reserved for map/world
///   reconstruction, where saved renderings already carry the last synced
///   name and can be re-linked safely.</summary>
internal sealed class PinRenderingLedger<THandle> where THandle : class
{
    /// <summary>Visual state of one rendering as read from the map.</summary>
    public readonly struct RenderingState
    {
        public RenderingState(string name, float x, float y, float z, int vanillaType, bool isChecked)
        {
            Name = name;
            X = x;
            Y = y;
            Z = z;
            VanillaType = vanillaType;
            IsChecked = isChecked;
        }

        public string Name { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int VanillaType { get; }
        public bool IsChecked { get; }
    }

    /// <summary>What the adapter must do to bring one pin's rendering in
    /// line with the store.</summary>
    public enum SyncDecision
    {
        None,
        Add,
        Remove,
        Replace,
        UpdateChecked,
    }

    // ClaimMatch tolerance: renderings within 0.5 m horizontally.
    private const float ClaimToleranceSquaredMeters = 0.25f;

    // Rendering drift below this is float noise, not a move.
    private const float PositionEpsilonMeters = 0.01f;

    private readonly Func<THandle, RenderingState> _stateOf;
    private readonly Dictionary<THandle, AtlasId> _idByRendering = new();
    private readonly Dictionary<Guid, THandle> _renderingById = new();

    public PinRenderingLedger(Func<THandle, RenderingState> stateOf)
    {
        _stateOf = stateOf;
    }

    public int TrackedCount => _idByRendering.Count;

    /// <summary>Tracked rendering→id pairs, for absorbing vanilla-side
    /// changes. Do not track/untrack while enumerating.</summary>
    public IEnumerable<KeyValuePair<THandle, AtlasId>> Tracked => _idByRendering;

    /// <summary>Forgets every link (world/map switch). Store data and the
    /// map are untouched.</summary>
    public void Reset()
    {
        _idByRendering.Clear();
        _renderingById.Clear();
    }

    public void Track(THandle rendering, AtlasId id)
    {
        _idByRendering[rendering] = id;
        _renderingById[id.Value] = rendering;
    }

    public void Untrack(THandle rendering)
    {
        if (_idByRendering.TryGetValue(rendering, out AtlasId id))
        {
            _idByRendering.Remove(rendering);
            _renderingById.Remove(id.Value);
        }
    }

    public bool IsTracked(THandle rendering)
    {
        return _idByRendering.ContainsKey(rendering);
    }

    public bool TryGetId(THandle rendering, out AtlasId id)
    {
        return _idByRendering.TryGetValue(rendering, out id);
    }

    public bool TryGetRendering(AtlasId id, out THandle rendering)
    {
        return _renderingById.TryGetValue(id.Value, out rendering!);
    }

    /// <summary>Map-reconstruction matching: the first unclaimed rendering
    /// within 0.5 m horizontally AND with the exact stored name. Claimed
    /// renderings are removed from the candidate list, so each can back at
    /// most one managed pin and restarts can never duplicate. Requiring
    /// both position and name means a reconcile can never steal a
    /// different nearby pin.</summary>
    public THandle? ClaimMatch(List<THandle> unclaimed, AtlasPin managed)
    {
        for (int index = 0; index < unclaimed.Count; index++)
        {
            RenderingState state = _stateOf(unclaimed[index]);
            float dx = state.X - managed.Position.X;
            float dz = state.Z - managed.Position.Z;
            if ((dx * dx) + (dz * dz) <= ClaimToleranceSquaredMeters &&
                string.Equals(state.Name, managed.Name, StringComparison.Ordinal))
            {
                THandle claimed = unclaimed[index];
                unclaimed.RemoveAt(index);
                return claimed;
            }
        }

        return null;
    }

    /// <summary>The targeted in-session decision for one pin. Because it
    /// resolves the rendering through the tracking table, an edited pin
    /// always updates/replaces its own rendering — never orphans it and
    /// never touches another pin's.</summary>
    public SyncDecision DecideSync(AtlasPin managed, int wantedVanillaType, out THandle? existing)
    {
        if (!_renderingById.TryGetValue(managed.Id.Value, out THandle? rendering))
        {
            existing = null;
            return managed.Deleted || managed.Archived ? SyncDecision.None : SyncDecision.Add;
        }

        existing = rendering;
        if (managed.Deleted || managed.Archived)
        {
            return SyncDecision.Remove;
        }

        RenderingState state = _stateOf(rendering);
        float dx = state.X - managed.Position.X;
        float dy = state.Y - managed.Position.Y;
        float dz = state.Z - managed.Position.Z;
        bool visualChange = state.VanillaType != wantedVanillaType ||
            !string.Equals(state.Name, managed.Name, StringComparison.Ordinal) ||
            (dx * dx) + (dy * dy) + (dz * dz) > PositionEpsilonMeters * PositionEpsilonMeters;
        if (visualChange)
        {
            return SyncDecision.Replace;
        }

        return state.IsChecked != managed.Checked ? SyncDecision.UpdateChecked : SyncDecision.None;
    }
}
