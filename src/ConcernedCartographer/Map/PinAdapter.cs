using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The bridge between the pin store and Valheim's Minimap.
///
/// Ownership model (PIN_WORKBENCH_DESIGN.md): managed pins render as
/// ordinary saved vanilla pins (m_save=true, owner 0), which makes
/// uninstall/downgrade safe by construction. Adoptable vanilla pins are the
/// player's own saved, ownerless, player-placeable-type pins; everything
/// else (death/bed/boss/player/event pins, server shared-map pins with an
/// owner, foreign-author pins) is foreign and is never edited, adopted, or
/// removed by any operation here.
///
/// Tracking and every match/sync decision live in the pure
/// <see cref="PinRenderingLedger{THandle}"/> (DEF-v1.0-004): in-session
/// mutations go through the targeted <see cref="SyncPin"/>/<see
/// cref="SyncAllPins"/> path, which preserves tracking so an edited pin
/// replaces its own rendering; <see cref="ReconcileOnMapReady"/> (reset +
/// claim-by-position-and-name) is reserved for map/world reconstruction.
///
/// The private Minimap.m_pins list is read through a Harmony
/// skip-visibility FieldRef (direct publicized access throws
/// MethodAccessException at JIT — proven by DEF-v0.2-001). Every visual
/// refresh uses only the public RemovePin/AddPin pair: the map-level
/// PinData object may be replaced, but the store-level entity identity
/// never changes.</summary>
internal sealed class PinAdapter
{
    private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>>? PinsField =
        BuildPinsFieldRef();

    // Player-placeable vanilla pin types: Icon0..Icon3 and Icon4 (portal).
    private static readonly int[] PlaceableTypes = { 0, 1, 2, 3, 6 };

    private readonly ManualLogSource _log;
    private readonly PinRenderingLedger<Minimap.PinData> _ledger = new(ReadRenderingState);
    private bool _disabledForSession;

    public PinAdapter(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsOperational => !_disabledForSession && PinsField is not null;

    /// <summary>Clears map-object tracking on world switch. Store data is
    /// untouched.</summary>
    public void Reset()
    {
        _ledger.Reset();
    }

    /// <summary>Vanilla pins the player could adopt right now.</summary>
    public List<Minimap.PinData> ListAdoptable()
    {
        var adoptable = new List<Minimap.PinData>();
        if (!TryGetPins(out List<Minimap.PinData> pins))
        {
            return adoptable;
        }

        foreach (Minimap.PinData pin in pins)
        {
            if (IsAdoptableVanilla(pin))
            {
                adoptable.Add(pin);
            }
        }

        return adoptable;
    }

    /// <summary>Adopts one vanilla pin: creates the managed entity with the
    /// pin's exact position, name, icon, and checked state, and tracks the
    /// existing PinData as its rendering. The map object is not touched at
    /// all, so adoption can never shift or duplicate the pin.</summary>
    public AtlasPin? Adopt(PinStore store, Minimap.PinData source)
    {
        if (_disabledForSession || !IsAdoptableVanilla(source))
        {
            return null;
        }

        try
        {
            AtlasPin managed = store.Create(pin =>
            {
                pin.Name = source.m_name ?? "";
                pin.IconId = IconRegistry.FromVanillaType((int)source.m_type);
                pin.Checked = source.m_checked;
                pin.Source = AtlasPinSource.AdoptedVanilla;
                pin.Position = new RoadPoint(source.m_pos.x, source.m_pos.y, source.m_pos.z);
            });
            _ledger.Track(source, managed.Id);
            return managed;
        }
        catch (Exception exception)
        {
            Disable(exception);
            return null;
        }
    }

    /// <summary>Rebuilds the store↔map link after a world/map load: matches
    /// stored managed pins to saved vanilla pins by position (0.5 m) and
    /// name, adds map pins for unmatched living entries, removes renderings
    /// of deleted/archived entries, and leaves every unmatched vanilla or
    /// foreign pin untouched. Each vanilla pin can be claimed at most once,
    /// so restarts can never produce duplicates.
    ///
    /// Map/world reconstruction ONLY (DEF-v1.0-004): the reset discards
    /// tracking, and claim-by-name cannot re-link a rendering whose pin was
    /// renamed since the rendering was last synced. In-session mutations
    /// must go through <see cref="SyncPin"/>/<see cref="SyncAllPins"/>.</summary>
    public void ReconcileOnMapReady(PinStore store)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            Reset();
            if (!TryGetPins(out List<Minimap.PinData> pins))
            {
                return;
            }

            var unclaimed = new List<Minimap.PinData>();
            foreach (Minimap.PinData pin in pins)
            {
                if (IsAdoptableVanilla(pin))
                {
                    unclaimed.Add(pin);
                }
            }

            int added = 0;
            int removed = 0;
            foreach (AtlasPin managed in store.All)
            {
                Minimap.PinData? match = _ledger.ClaimMatch(unclaimed, managed);

                if (managed.Deleted || managed.Archived)
                {
                    if (match is not null)
                    {
                        Minimap.instance.RemovePin(match);
                        removed++;
                    }

                    continue;
                }

                if (match is not null)
                {
                    _ledger.Track(match, managed.Id);
                    SyncPin(store, managed.Id);
                }
                else
                {
                    AddManagedPin(managed);
                    added++;
                }
            }

            if (added > 0 || removed > 0)
            {
                _log.LogInfo($"Pin reconcile: linked {_ledger.TrackedCount} managed pin(s), added {added}, removed {removed} stale rendering(s).");
            }
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    /// <summary>Pushes a store entity's current state onto the map through
    /// the tracked rendering. Uses public RemovePin/AddPin when visual
    /// fields changed; tracking follows the replacement, so repeated edits
    /// always target the same single rendering.</summary>
    public void SyncPin(PinStore store, AtlasId id)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            if (!store.TryGet(id, out AtlasPin managed))
            {
                return;
            }

            int wantedType = IconRegistry.ResolveVanillaType(managed.IconId);
            switch (_ledger.DecideSync(managed, wantedType, out Minimap.PinData? existing))
            {
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.Add:
                    AddManagedPin(managed);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.Remove:
                    Minimap.instance.RemovePin(existing);
                    _ledger.Untrack(existing!);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.Replace:
                    Minimap.instance.RemovePin(existing);
                    _ledger.Untrack(existing!);
                    AddManagedPin(managed);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.UpdateChecked:
                    existing!.m_checked = managed.Checked;
                    break;
            }
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    /// <summary>In-session batch sync (DEF-v1.0-004): pushes every store
    /// entity through the targeted <see cref="SyncPin"/> path WITHOUT
    /// resetting tracking, so batch edits, merges, undo/redo, sync applies
    /// and survey accepts can never orphan a rendering. skipRendering lets
    /// the display layer keep filtered/clustered pins hidden.</summary>
    public void SyncAllPins(PinStore store, Func<AtlasPin, bool>? skipRendering = null)
    {
        if (_disabledForSession)
        {
            return;
        }

        foreach (AtlasPin managed in store.All)
        {
            if (skipRendering is not null && !managed.Deleted && !managed.Archived && skipRendering(managed))
            {
                continue;
            }

            SyncPin(store, managed.Id);
            if (_disabledForSession)
            {
                return;
            }
        }
    }

    /// <summary>Absorbs edits the player made through vanilla UI into the
    /// store: cross-off toggles become metadata edits, and vanilla-side
    /// deletion of a tracked pin becomes a store tombstone. Runs on the
    /// autosave cadence.</summary>
    public void AbsorbVanillaChanges(PinStore store)
    {
        if (_disabledForSession || _ledger.TrackedCount == 0)
        {
            return;
        }

        try
        {
            if (!TryGetPins(out List<Minimap.PinData> pins))
            {
                return;
            }

            var live = new HashSet<Minimap.PinData>(pins);
            var vanished = new List<Minimap.PinData>();
            foreach (KeyValuePair<Minimap.PinData, AtlasId> tracked in _ledger.Tracked)
            {
                if (!live.Contains(tracked.Key))
                {
                    vanished.Add(tracked.Key);
                    continue;
                }

                if (store.TryGet(tracked.Value, out AtlasPin managed) &&
                    managed.Checked != tracked.Key.m_checked)
                {
                    bool nowChecked = tracked.Key.m_checked;
                    store.Mutate(tracked.Value, pin => pin.Checked = nowChecked);
                }
            }

            foreach (Minimap.PinData gone in vanished)
            {
                if (_ledger.TryGetId(gone, out AtlasId id))
                {
                    store.Delete(id);
                    _ledger.Untrack(gone);
                    _log.LogInfo($"Managed pin {id} was deleted through vanilla UI; tombstoned in the atlas.");
                }
            }
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    /// <summary>Removes a managed pin's rendering only (display filtering /
    /// clustering). The store entity is untouched, and because the pin is
    /// untracked first, the vanilla-change absorber can never mistake this
    /// for a player deletion.</summary>
    public void DisplayHide(AtlasId id)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            if (_ledger.TryGetRendering(id, out Minimap.PinData rendered))
            {
                _ledger.Untrack(rendered);
                Minimap.instance.RemovePin(rendered);
            }
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    public bool TryGetManagedId(Minimap.PinData pin, out AtlasId id)
    {
        return _ledger.TryGetId(pin, out id);
    }

    /// <summary>The nearest map pin to a world position with its class, for
    /// selection and status displays. Foreign pins are reported but marked
    /// read-only.</summary>
    public bool TryFindNearest(Vector3 position, float maxRadius, out Minimap.PinData pin, out float distance)
    {
        pin = null!;
        distance = float.MaxValue;
        if (!TryGetPins(out List<Minimap.PinData> pins))
        {
            return false;
        }

        foreach (Minimap.PinData candidate in pins)
        {
            float d = Vector2.Distance(
                new Vector2(candidate.m_pos.x, candidate.m_pos.z),
                new Vector2(position.x, position.z));
            if (d < distance)
            {
                distance = d;
                pin = candidate;
            }
        }

        return pin is not null && distance <= maxRadius;
    }

    public bool IsAdoptableVanilla(Minimap.PinData pin)
    {
        return pin is not null &&
            pin.m_save &&
            pin.m_ownerID == 0L &&
            !_ledger.IsTracked(pin) &&
            Array.IndexOf(PlaceableTypes, (int)pin.m_type) >= 0;
    }

    public bool IsForeign(Minimap.PinData pin)
    {
        return !_ledger.IsTracked(pin) && !IsAdoptableVanilla(pin);
    }

    private void AddManagedPin(AtlasPin managed)
    {
        var position = new Vector3(managed.Position.X, managed.Position.Y, managed.Position.Z);
        var type = (Minimap.PinType)IconRegistry.ResolveVanillaType(managed.IconId);
        Minimap.PinData created = Minimap.instance.AddPin(position, type, managed.Name, save: true, managed.Checked);
        _ledger.Track(created, managed.Id);
    }

    private static PinRenderingLedger<Minimap.PinData>.RenderingState ReadRenderingState(Minimap.PinData pin)
    {
        return new PinRenderingLedger<Minimap.PinData>.RenderingState(
            pin.m_name ?? "",
            pin.m_pos.x,
            pin.m_pos.y,
            pin.m_pos.z,
            (int)pin.m_type,
            pin.m_checked);
    }

    private bool TryGetPins(out List<Minimap.PinData> pins)
    {
        pins = null!;
        if (PinsField is null || Minimap.instance == null)
        {
            return false;
        }

        pins = PinsField(Minimap.instance);
        return pins is not null;
    }

    private void Disable(Exception exception)
    {
        _disabledForSession = true;
        _log.LogError($"Pin adapter failed and was disabled for this session (store data is safe): {exception}");
    }

    private static AccessTools.FieldRef<Minimap, List<Minimap.PinData>>? BuildPinsFieldRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        }
        catch
        {
            return null;
        }
    }
}
