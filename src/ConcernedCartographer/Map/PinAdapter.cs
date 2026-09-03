using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
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

    // RC8: which icon id's CUSTOM sprite a rendering currently shows (only
    // renderings with a CC sprite are listed). Lets the sync detect icon
    // changes the vanilla-type comparison cannot see (two cc:* ids can
    // share a fallback type) and re-apply sprites after a restart claim.
    private readonly Dictionary<Minimap.PinData, string> _customSpriteByRendering = new();
    private bool _disabledForSession;

    // RC15: true only between a COMPLETED reconcile and the next map/world
    // transition — the "stable, fully-bound map session" the tombstone
    // rule requires before an explicit vanilla delete may tombstone.
    private bool _sessionBound;

    // RC15 lifecycle diagnostics: sprite rebuilds performed by the current
    // reconcile pass (aggregate count only; no pin data is ever logged).
    private int _spriteRebinds;

    public PinAdapter(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsOperational => !_disabledForSession && PinsField is not null;

    /// <summary>RC15: set when tracked renderings disappeared WITHOUT an
    /// explicit vanilla delete event — vanilla rebuilt the pin list (map
    /// reconstruction), so the runtime must re-reconcile to re-link the
    /// markers. Cleared by a completed reconcile.</summary>
    public bool NeedsRebind { get; private set; }

    /// <summary>Clears map-object tracking on world switch. Store data is
    /// untouched. RC14 fix 5: a world/map boundary also un-latches the
    /// fail-soft session disable — the adapter object outlives every game
    /// session, so a latch that nothing cleared turned one teardown-frame
    /// failure into "every cc:* marker renders as its vanilla fallback
    /// (Dot) forever after". A genuinely broken adapter re-latches on its
    /// next failure.</summary>
    public void Reset()
    {
        _ledger.Reset();
        _customSpriteByRendering.Clear();
        _disabledForSession = false;
        // RC15: a transition boundary ends the bound session; explicit
        // deletes recorded from here on may not tombstone until the next
        // reconcile completes.
        _sessionBound = false;
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
    /// must go through <see cref="SyncPin"/>/<see cref="SyncAllPins"/>.
    ///
    /// RC15: reason names the lifecycle transition for the aggregate
    /// diagnostic line ("map-available", "map-data-loaded",
    /// "rendering-loss-repair"); a completed pass binds the session, which
    /// re-arms explicit-delete tombstoning and clears any pending rebind
    /// request.</summary>
    public void ReconcileOnMapReady(PinStore store, string reason = "map-available")
    {
        // RC14 fix 5: map reconstruction begins a fresh session, so the
        // previous session's fail-soft latch never survives into it (the
        // relog leg of the "custom markers become Dots" report).
        _disabledForSession = false;

        try
        {
            Reset();
            _spriteRebinds = 0;
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

            // TryGetPins proved the map alive this frame; keep one live
            // reference for the writes below (RC14 fix 5).
            Minimap map = Minimap.instance;

            int claimed = 0;
            int added = 0;
            int removed = 0;
            foreach (AtlasPin managed in store.All)
            {
                Minimap.PinData? match = _ledger.ClaimMatch(unclaimed, managed);

                if (managed.Deleted || managed.Archived)
                {
                    if (match is not null)
                    {
                        RemoveRenderingFromMap(map, match);
                        removed++;
                    }

                    continue;
                }

                if (match is not null)
                {
                    _ledger.Track(match, managed.Id);
                    claimed++;
                    SyncPin(store, managed.Id);
                }
                else
                {
                    AddManagedPin(managed);
                    added++;
                }
            }

            if (!_disabledForSession)
            {
                // RC15: only a COMPLETED pass counts as a bound session
                // and satisfies a pending rebind request.
                _sessionBound = true;
                NeedsRebind = false;
            }

            // RC15 lifecycle diagnostics: aggregate counts only, once per
            // reconstruction event — never pin names, ids, or positions.
            _log.LogInfo(
                $"Pin reconcile ({reason}): linked {_ledger.TrackedCount} managed pin(s) " +
                $"(claimed {claimed}, added {added}), removed {removed} stale rendering(s), " +
                $"custom sprite rebinds {_spriteRebinds}.");
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

        // RC14 fix 5: on login/logout teardown frames no Minimap exists;
        // writing through it anyway threw NullReferenceException (the
        // Sentry pin-update event) and latched the session disable. A
        // missing map makes the sync a no-op — the next map-available
        // reconcile repairs every rendering.
        Minimap map = Minimap.instance;
        if (map == null)
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
                    RemoveRenderingFromMap(map, existing!);
                    ForgetRendering(existing!);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.Replace:
                    RemoveRenderingFromMap(map, existing!);
                    ForgetRendering(existing!);
                    AddManagedPin(managed);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.UpdateChecked:
                    existing!.m_checked = managed.Checked;
                    EnsureCustomSprite(existing, managed);
                    break;
                case PinRenderingLedger<Minimap.PinData>.SyncDecision.None:
                    if (existing is not null && !managed.Deleted && !managed.Archived)
                    {
                        EnsureCustomSprite(existing, managed);
                    }

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
    /// store: cross-off toggles become metadata edits. Runs on the
    /// autosave cadence.
    ///
    /// RC15 final beta blocker: a tracked rendering MISSING from the live
    /// pin list is NEVER treated as a deletion anymore. Vanilla rebuilds
    /// m_pins wholesale during login (LoadMapData → SetMapData →
    /// ClearPins + re-AddPin, decompile-verified) and that reconstruction
    /// made this absorber rewrite live cc:* pins Deleted=1 ("deleted
    /// through vanilla UI") while their save-file copies rendered as
    /// plain Fire/Portal markers. Absence now only drops the stale link
    /// and requests a rebind; tombstones are written exclusively by
    /// <see cref="HandleExplicitVanillaDelete"/> from the RemovePin choke
    /// point (<see cref="PinDeletionWatch"/>).</summary>
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

            if (vanished.Count > 0)
            {
                foreach (Minimap.PinData gone in vanished)
                {
                    ForgetRendering(gone);
                }

                NeedsRebind = true;
                _sessionBound = false;
                // Aggregate count only — no pin data. Info because this is
                // the expected signature of a vanilla map rebuild.
                _log.LogInfo(
                    $"Pin lifecycle: {vanished.Count} tracked rendering(s) disappeared without an explicit " +
                    "vanilla delete (map rebuild assumed); no tombstones written, markers will rebind.");
            }
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    /// <summary>RC15: the ONLY path that turns a vanilla-side action into a
    /// store tombstone. Fed by <see cref="PinDeletionWatch"/> from the
    /// vanilla RemovePin choke point at the moment of deletion, so the
    /// evidence is the event itself — never an inference from absence. The
    /// pure <see cref="PinTombstoneRule"/> additionally requires a bound
    /// session (reconcile completed for the current map generation) and
    /// tombstones an entity at most once; anything else just unlinks the
    /// rendering and lets the next reconcile repair it.</summary>
    public void HandleExplicitVanillaDelete(PinStore store, Minimap.PinData pin)
    {
        if (_disabledForSession || pin is null)
        {
            return;
        }

        try
        {
            if (!_ledger.TryGetId(pin, out AtlasId id) || !store.TryGet(id, out AtlasPin managed))
            {
                return;
            }

            if (PinTombstoneRule.Decide(
                    explicitVanillaDelete: true,
                    sessionBound: _sessionBound,
                    alreadyDeleted: managed.Deleted) == PinTombstoneRule.Verdict.Tombstone)
            {
                store.Delete(id);
                ForgetRendering(pin);
                _log.LogInfo(
                    "Pin lifecycle: managed pin tombstoned (cause: explicit vanilla delete in a bound map session).");
            }
            else
            {
                // Unbound session (mid-reconstruction) or already deleted:
                // drop the link only; the next reconcile decides rendering.
                ForgetRendering(pin);
                NeedsRebind = true;
                _log.LogInfo(
                    "Pin lifecycle: vanilla delete observed outside a bound map session; kept the atlas entry and will rebind.");
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
                ForgetRendering(rendered);
                // RC14 fix 5: with no live map the rendering died with it;
                // untracking above is the whole job.
                Minimap map = Minimap.instance;
                if (map != null)
                {
                    RemoveRenderingFromMap(map, rendered);
                }
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

    /// <summary>True while the rendering still exists on the map. Guards
    /// palette-birth adoption against a pin the player removed during its
    /// own naming flow.</summary>
    public bool ContainsPin(Minimap.PinData pin)
    {
        return TryGetPins(out List<Minimap.PinData> pins) && pins.Contains(pin);
    }

    /// <summary>The adoptable vanilla pin standing where a vanished newborn
    /// stood (RC12 blocker 5): if the naming close replaced the PinData
    /// object, the replacement is adopted instead of duplicating the
    /// marker. Same 0.5 m horizontal tolerance as the reconcile claim; a
    /// name match is preferred when several candidates crowd the spot.</summary>
    public Minimap.PinData? TryFindAdoptableAt(Vector3 position, string preferredName)
    {
        const float toleranceSquared = 0.25f;
        Minimap.PinData? candidate = null;
        foreach (Minimap.PinData pin in ListAdoptable())
        {
            float dx = pin.m_pos.x - position.x;
            float dz = pin.m_pos.z - position.z;
            if ((dx * dx) + (dz * dz) > toleranceSquared)
            {
                continue;
            }

            if (string.Equals(pin.m_name ?? "", preferredName, StringComparison.Ordinal))
            {
                return pin;
            }

            candidate ??= pin;
        }

        return candidate;
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
        // RC14 fix 5: no live map (teardown frame) makes this a no-op; the
        // next map-available reconcile adds the rendering.
        Minimap map = Minimap.instance;
        if (map == null)
        {
            return;
        }

        var position = new Vector3(managed.Position.X, managed.Position.Y, managed.Position.Z);
        var type = (Minimap.PinType)IconRegistry.ResolveVanillaType(managed.IconId);
        Minimap.PinData created = map.AddPin(position, type, managed.Name, save: true, managed.Checked);
        _ledger.Track(created, managed.Id);

        // RC8 (hardened by RC14 fix 1): the saved pin persists only its
        // vanilla type — the CC sprite is rendering-only. Applying through
        // ApplyImmediateSprite sets BOTH m_icon and, when the UI element
        // already exists this frame, the live element's sprite; m_icon
        // alone only affects elements built later.
        ApplyImmediateSprite(created, managed.IconId);
    }

    /// <summary>RC10 feedback 12: a palette-armed newborn wears its chosen
    /// CC sprite from the first frame of the vanilla naming flow — never a
    /// temporary vanilla Dot. Sets both the pin's sprite and the LIVE map
    /// element's sprite (m_icon alone only affects future elements), and
    /// records the applied sprite so the adoption sync recognizes it
    /// instead of rebuilding the rendering. Visual-only; the saved pin
    /// still persists its vanilla type.</summary>
    public void ApplyImmediateSprite(Minimap.PinData pin, string iconId)
    {
        try
        {
            if (pin is null || !CcIconSprites.TryGet(iconId, out Sprite sprite))
            {
                return;
            }

            if (!ReferenceEquals(pin.m_icon, sprite))
            {
                pin.m_icon = sprite;
                _customSpriteByRendering[pin] = iconId;
            }

            if (pin.m_iconElement != null && !ReferenceEquals(pin.m_iconElement.sprite, sprite))
            {
                pin.m_iconElement.sprite = sprite;
            }
        }
        catch
        {
            // Per-frame cosmetic path: the adoption sync still applies the
            // sprite at naming close, so failing silent here is safe.
        }
    }

    /// <summary>A kept rendering whose icon id changed to (or from) a
    /// custom-sprite icon of the SAME vanilla fallback type is invisible to
    /// the ledger's type comparison; rebuild it so the element picks up the
    /// right sprite. Also restores CC sprites onto renderings claimed from
    /// the save file after a restart.</summary>
    private void EnsureCustomSprite(Minimap.PinData rendering, AtlasPin managed)
    {
        string? wanted = CcIconSprites.TryGet(managed.IconId, out Sprite wantedSprite) ? managed.IconId : null;
        string? applied = _customSpriteByRendering.TryGetValue(rendering, out string current) ? current : null;

        // RC15: a claimed rendering that ALREADY wears the wanted live CC
        // sprite (typically our own rendering re-claimed after a reconcile
        // reset dropped the applied-sprite record) just re-records — a
        // remove/re-add rebuild would be pure flicker.
        if (wanted is not null && applied is null &&
            wantedSprite != null && ReferenceEquals(rendering.m_icon, wantedSprite))
        {
            _customSpriteByRendering[rendering] = wanted;
            return;
        }

        // RC14 fix 1: the rebuild decision is the pure, tested
        // SpriteRebindRule — a restart-claimed cc:* rendering (wanted
        // sprite, none applied) rebuilds to regain its art, a genuine
        // vanilla pin never does, and a sprite Unity destroyed across a
        // scene change counts as not applied.
        if (!SpriteRebindRule.MustRebuild(wanted, applied, rendering.m_icon != null))
        {
            return;
        }

        Minimap map = Minimap.instance;
        if (map == null)
        {
            return;
        }

        RemoveRenderingFromMap(map, rendering);
        ForgetRendering(rendering);
        AddManagedPin(managed);
        // RC15 lifecycle diagnostics: aggregate rebind count for the
        // reconcile summary line.
        _spriteRebinds++;
    }

    /// <summary>Every adapter-initiated RemovePin runs inside a
    /// <see cref="PinDeletionWatch"/> self-removal scope, so the explicit
    /// vanilla-delete capture can never mistake our own rendering
    /// maintenance for a player deletion (RC15).</summary>
    private static void RemoveRenderingFromMap(Minimap map, Minimap.PinData rendering)
    {
        using (PinDeletionWatch.BeginSelfRemoval())
        {
            map.RemovePin(rendering);
        }
    }

    private void ForgetRendering(Minimap.PinData rendering)
    {
        _ledger.Untrack(rendering);
        _customSpriteByRendering.Remove(rendering);
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
        // RC15: a broken adapter is not a bound session — explicit deletes
        // may not tombstone until a clean reconcile completes again.
        _sessionBound = false;
        _log.LogError($"Pin adapter failed and was disabled for this session (store data is safe): {SafeLogText.Describe(exception)}");
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
