using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC15 final beta blocker: the false relog tombstone. Vanilla
/// rebuilds the whole pin list during login (LoadMapData → SetMapData →
/// ClearPins + re-AddPin), so a tracked rendering's ABSENCE is never
/// deletion evidence — only an explicit vanilla RemovePin event captured
/// during a stable, fully-bound map session may tombstone, exactly
/// once.</summary>
public class PinTombstoneRuleTests
{
    [Fact]
    public void ExplicitDeleteInBoundSession_Tombstones()
    {
        Assert.Equal(
            PinTombstoneRule.Verdict.Tombstone,
            PinTombstoneRule.Decide(explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: false));
    }

    [Theory]
    [InlineData(false, false, false)] // reconstruction absence, unbound
    [InlineData(false, true, false)]  // reconstruction absence, bound session
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void AbsenceIsNeverDeletionEvidence(bool explicitDelete, bool bound, bool alreadyDeleted)
    {
        // The owner's invariant verbatim: map open/close, logout/login,
        // world load/unload, Minimap rebuild, reconcile, sprite
        // destruction, fallback remapping — none may tombstone on a
        // missing rendering alone.
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(explicitDelete, bound, alreadyDeleted));
    }

    [Fact]
    public void ExplicitDeleteOutsideABoundSession_KeepsThePin()
    {
        // Mid-reconstruction removals (before a completed reconcile) are
        // teardown fallout, not user intent.
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(explicitVanillaDelete: true, sessionBound: false, alreadyDeleted: false));
    }

    [Fact]
    public void TombstoneIsWrittenExactlyOnce()
    {
        // A second explicit delete of an already-deleted entity must not
        // re-tombstone (no phantom revision bumps).
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: true));
    }
}

/// <summary>RC15 lifecycle diagnostics: the map-session generation counter
/// that stamps reconstruction transitions and gates the tombstone rule's
/// "stable, fully-bound" requirement.</summary>
public class MapSessionTrackerTests
{
    [Fact]
    public void TransitionsAdvanceTheGenerationAndUnbind()
    {
        var tracker = new MapSessionTracker();
        Assert.Equal(0, tracker.Generation);
        Assert.False(tracker.Bound);

        Assert.Equal(1, tracker.NoteTransition("map-available"));
        Assert.False(tracker.Bound);
        Assert.Equal("map-available", tracker.LastTransitionReason);

        tracker.NoteBound();
        Assert.True(tracker.Bound);

        Assert.Equal(2, tracker.NoteTransition("map-data-loaded"));
        Assert.False(tracker.Bound);
        Assert.Equal("map-data-loaded", tracker.LastTransitionReason);
    }

    [Fact]
    public void WorldUnloadEndsTheBoundSession()
    {
        var tracker = new MapSessionTracker();
        tracker.NoteTransition("map-available");
        tracker.NoteBound();
        tracker.NoteTransition("world-unloaded");
        Assert.False(tracker.Bound);
    }
}

/// <summary>RC15 item 8: the full-redraw write guard. The RC13 Sentry
/// event was a NullReferenceException at Texture2D.SetPixels32 during
/// "rebuild road map" — the texture died between handle resolve and the
/// pixel write.</summary>
public class OverlayWriteGuardTests
{
    [Fact]
    public void AliveAtBothEnds_MayWrite()
    {
        Assert.True(OverlayHandleRule.MayWrite(textureAliveAtResolve: true, textureAliveAtWrite: true));
    }

    [Fact]
    public void AliveAtResolveButDestroyedBeforeWrite_MustAbort()
    {
        // The exact Sentry scenario: resolve succeeded, the stroke loop
        // ran, teardown destroyed the texture, then SetPixels32 threw.
        Assert.False(OverlayHandleRule.MayWrite(textureAliveAtResolve: true, textureAliveAtWrite: false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void DeadAtResolve_MustAbort(bool atResolve, bool atWrite)
    {
        Assert.False(OverlayHandleRule.MayWrite(atResolve, atWrite));
    }
}

/// <summary>The owner's reproduction, replayed against the shipping pure
/// pieces (PinStore, PinRenderingLedger, IconRegistry, SpriteRebindRule,
/// PinTombstoneRule): cc:camp persists Fire (type 0) and cc:travel
/// persists Portal (type 6) as their uninstall-safe vanilla fallbacks;
/// across teardown → save reload → reconcile the atlas entries must stay
/// Deleted=false, regain exactly one rendering each, and rebind their CC
/// sprites — while a genuine stable-session vanilla delete still
/// tombstones exactly once and stays recoverable.</summary>
public class Rc15RelogPersistenceTests
{
    /// <summary>Stands in for Minimap.PinData: what the vanilla save file
    /// reconstructs (name, position, PERSISTED FALLBACK TYPE, checked).</summary>
    private sealed class FakeRendering
    {
        public FakeRendering(string name, float x, float y, float z, int vanillaType)
        {
            Name = name;
            X = x;
            Y = y;
            Z = z;
            VanillaType = vanillaType;
        }

        public string Name { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int VanillaType { get; }
        public bool Checked { get; set; }

        /// <summary>Which CC sprite this rendering currently wears, or
        /// null — a fresh save-file rendering never carries one.</summary>
        public string? AppliedSpriteId { get; set; }
    }

    private static PinRenderingLedger<FakeRendering>.RenderingState StateOf(FakeRendering rendering)
    {
        return new PinRenderingLedger<FakeRendering>.RenderingState(
            rendering.Name, rendering.X, rendering.Y, rendering.Z, rendering.VanillaType, rendering.Checked);
    }

    private static PinStore StoreWithCampAndTravel(out AtlasPin camp, out AtlasPin travel)
    {
        var store = new PinStore();
        camp = store.Create(pin =>
        {
            pin.Name = "camp";
            pin.IconId = "cc:camp";
            pin.Position = new RoadPoint(100f, 30f, -50f);
        });
        travel = store.Create(pin =>
        {
            pin.Name = "route";
            pin.IconId = "cc:travel";
            pin.Position = new RoadPoint(-220f, 12f, 400f);
        });
        return store;
    }

    /// <summary>One reconcile pass over the current "map" — the same
    /// decision sequence PinAdapter.ReconcileOnMapReady executes: reset,
    /// claim by position+name, sync, sprite-rebind check. Returns the
    /// number of custom-sprite rebinds performed.</summary>
    private static int Reconcile(
        PinStore store,
        PinRenderingLedger<FakeRendering> ledger,
        List<FakeRendering> mapPins)
    {
        ledger.Reset();
        var unclaimed = new List<FakeRendering>(mapPins);
        int spriteRebinds = 0;

        foreach (AtlasPin managed in store.All)
        {
            FakeRendering? match = ledger.ClaimMatch(unclaimed, managed);
            if (managed.Deleted || managed.Archived)
            {
                if (match is not null)
                {
                    mapPins.Remove(match);
                }

                continue;
            }

            int wantedType = IconRegistry.ResolveVanillaType(managed.IconId);
            if (match is not null)
            {
                ledger.Track(match, managed.Id);

                // The adapter's EnsureCustomSprite: a claimed cc:* pin with
                // no applied sprite record must rebuild to regain its art.
                bool hasCustomSprite =
                    IconRegistry.TryResolve(managed.IconId, out IconRegistry.IconDefinition definition) &&
                    definition.HasCustomSprite;
                string? wanted = hasCustomSprite ? managed.IconId : null;
                if (SpriteRebindRule.MustRebuild(wanted, match.AppliedSpriteId, appliedSpriteAlive: true))
                {
                    mapPins.Remove(match);
                    ledger.Untrack(match);
                    FakeRendering rebuilt = AddManagedRendering(managed, wantedType, mapPins);
                    ledger.Track(rebuilt, managed.Id);
                    spriteRebinds++;
                }
            }
            else
            {
                FakeRendering added = AddManagedRendering(managed, wantedType, mapPins);
                ledger.Track(added, managed.Id);
            }
        }

        return spriteRebinds;
    }

    private static FakeRendering AddManagedRendering(AtlasPin managed, int vanillaType, List<FakeRendering> mapPins)
    {
        var rendering = new FakeRendering(
            managed.Name, managed.Position.X, managed.Position.Y, managed.Position.Z, vanillaType)
        {
            // AddManagedPin applies the CC sprite immediately.
            AppliedSpriteId = IconRegistry.TryResolve(managed.IconId, out IconRegistry.IconDefinition d) && d.HasCustomSprite
                ? managed.IconId
                : null,
        };
        mapPins.Add(rendering);
        return rendering;
    }

    [Fact]
    public void PersistedFallbackTypes_AreFireAndPortal()
    {
        // The owner's exact symptoms pinned to the registry contract:
        // cc:camp saves as Fire (0), cc:travel saves as Portal (6). These
        // are the uninstall-safe types the save file reconstructs.
        Assert.Equal(0, IconRegistry.ResolveVanillaType("cc:camp"));
        Assert.Equal(6, IconRegistry.ResolveVanillaType("cc:travel"));
    }

    [Fact]
    public void RelogReconstruction_KeepsPinsAliveAndRebindsCustomSprites()
    {
        PinStore store = StoreWithCampAndTravel(out AtlasPin camp, out AtlasPin travel);
        var ledger = new PinRenderingLedger<FakeRendering>(StateOf);

        // Session 1: map-available reconcile against an EMPTY pin list
        // (vanilla has not loaded the save yet) adds managed renderings.
        var mapPins = new List<FakeRendering>();
        Reconcile(store, ledger, mapPins);
        Assert.Equal(2, mapPins.Count);
        Assert.Equal(2, ledger.TrackedCount);

        // LoadMapData → SetMapData → ClearPins + re-AddPin: the list is
        // rebuilt IN PLACE from the character save. Our renderings are
        // destroyed; the reconstructed ones carry the persisted vanilla
        // fallback types and no CC sprite.
        mapPins.Clear();
        mapPins.Add(new FakeRendering("camp", 100f, 30f, -50f, vanillaType: 0));   // Fire
        mapPins.Add(new FakeRendering("route", -220f, 12f, 400f, vanillaType: 6)); // Portal

        // The RC14 absorber would now have inferred "deleted through
        // vanilla UI". The RC15 rule keeps both pins.
        foreach (FakeRendering _ in mapPins)
        {
            Assert.Equal(
                PinTombstoneRule.Verdict.KeepAndRebind,
                PinTombstoneRule.Decide(explicitVanillaDelete: false, sessionBound: true, alreadyDeleted: false));
        }

        Assert.False(camp.Deleted);
        Assert.False(travel.Deleted);

        // The map-data-loaded reconcile claims the save-file renderings
        // and rebinds the CC sprites (fallback type never stays visible).
        int rebinds = Reconcile(store, ledger, mapPins);
        Assert.Equal(2, rebinds);
        Assert.Equal(2, ledger.TrackedCount);
        Assert.Equal(2, mapPins.Count); // exactly one rendering per pin

        Assert.True(ledger.TryGetRendering(camp.Id, out FakeRendering campRendering));
        Assert.Equal("cc:camp", campRendering.AppliedSpriteId);
        Assert.Equal(0, campRendering.VanillaType); // fallback persists underneath
        Assert.True(ledger.TryGetRendering(travel.Id, out FakeRendering travelRendering));
        Assert.Equal("cc:travel", travelRendering.AppliedSpriteId);
        Assert.Equal(6, travelRendering.VanillaType);

        Assert.False(camp.Deleted);
        Assert.False(travel.Deleted);
    }

    [Fact]
    public void RepeatedTeardownRebuildCycles_NeverTombstoneAndNeverDuplicate()
    {
        PinStore store = StoreWithCampAndTravel(out AtlasPin camp, out AtlasPin travel);
        var ledger = new PinRenderingLedger<FakeRendering>(StateOf);
        var mapPins = new List<FakeRendering>();
        Reconcile(store, ledger, mapPins);

        for (int cycle = 0; cycle < 5; cycle++)
        {
            // Teardown + save reload, every cycle (rapid relogging).
            ledger.Reset();
            mapPins.Clear();
            mapPins.Add(new FakeRendering("camp", 100f, 30f, -50f, 0));
            mapPins.Add(new FakeRendering("route", -220f, 12f, 400f, 6));

            Reconcile(store, ledger, mapPins);

            Assert.False(camp.Deleted);
            Assert.False(travel.Deleted);
            Assert.Equal(2, mapPins.Count);
            Assert.Equal(2, ledger.TrackedCount);
            Assert.True(ledger.TryGetRendering(camp.Id, out FakeRendering rendering));
            Assert.Equal("cc:camp", rendering.AppliedSpriteId);
        }

        // Revisions moved only by the two Creates — reconstruction cycles
        // must not churn the store at all.
        Assert.Equal(1L, camp.Revision);
        Assert.Equal(1L, travel.Revision);
    }

    [Fact]
    public void MidSessionListRebuild_ResolvesToRebindNotTombstone()
    {
        PinStore store = StoreWithCampAndTravel(out AtlasPin camp, out AtlasPin travel);
        var ledger = new PinRenderingLedger<FakeRendering>(StateOf);
        var mapPins = new List<FakeRendering>();
        Reconcile(store, ledger, mapPins);

        // SetMapData mid-session: every tracked rendering vanishes from
        // the live list without any RemovePin event. The absorber's only
        // legal move is forget + rebind.
        var vanished = new List<FakeRendering>();
        foreach (KeyValuePair<FakeRendering, AtlasId> tracked in ledger.Tracked)
        {
            vanished.Add(tracked.Key);
        }

        foreach (FakeRendering gone in vanished)
        {
            Assert.Equal(
                PinTombstoneRule.Verdict.KeepAndRebind,
                PinTombstoneRule.Decide(explicitVanillaDelete: false, sessionBound: true, alreadyDeleted: false));
            ledger.Untrack(gone);
        }

        Assert.False(camp.Deleted);
        Assert.False(travel.Deleted);
        Assert.Equal(0, ledger.TrackedCount);

        // The repair reconcile relinks against the reconstructed list.
        mapPins.Clear();
        mapPins.Add(new FakeRendering("camp", 100f, 30f, -50f, 0));
        mapPins.Add(new FakeRendering("route", -220f, 12f, 400f, 6));
        Reconcile(store, ledger, mapPins);
        Assert.Equal(2, ledger.TrackedCount);
        Assert.False(camp.Deleted);
        Assert.False(travel.Deleted);
    }

    [Fact]
    public void GenuineStableSessionDelete_TombstonesExactlyOnceAndStaysRecoverable()
    {
        PinStore store = StoreWithCampAndTravel(out AtlasPin camp, out AtlasPin travel);
        var ledger = new PinRenderingLedger<FakeRendering>(StateOf);
        var mapPins = new List<FakeRendering>();
        Reconcile(store, ledger, mapPins);
        long revisionBefore = camp.Revision;

        // The player right-clicks the camp marker on the large map: an
        // EXPLICIT RemovePin event during a bound session.
        Assert.True(ledger.TryGetRendering(camp.Id, out FakeRendering campRendering));
        Assert.Equal(
            PinTombstoneRule.Verdict.Tombstone,
            PinTombstoneRule.Decide(explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: camp.Deleted));
        Assert.True(store.Delete(camp.Id));
        ledger.Untrack(campRendering);
        mapPins.Remove(campRendering);

        Assert.True(camp.Deleted);
        Assert.Equal(revisionBefore + 1, camp.Revision);
        Assert.False(travel.Deleted);

        // Exactly once: a duplicate event may not touch the entity again.
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: camp.Deleted));
        Assert.Equal(revisionBefore + 1, camp.Revision);

        // The tombstone survives the next reconcile (no resurrection) and
        // the untouched pin keeps its rendering.
        Reconcile(store, ledger, mapPins);
        Assert.True(camp.Deleted);
        Assert.Equal(1, ledger.TrackedCount);
        Assert.True(ledger.TryGetRendering(travel.Id, out _));
        Assert.False(ledger.TryGetRendering(camp.Id, out _));

        // Recoverable: the tombstone is a durable entity, not an erasure.
        Assert.True(store.Restore(camp.Id));
        Assert.False(camp.Deleted);
        Reconcile(store, ledger, mapPins);
        Assert.Equal(2, ledger.TrackedCount);
        Assert.True(ledger.TryGetRendering(camp.Id, out FakeRendering restored));
        Assert.Equal("cc:camp", restored.AppliedSpriteId);
    }

    [Fact]
    public void ReclaimedOwnRendering_DoesNotRebuildWhenSpriteAlreadyCorrect()
    {
        // RC15 flicker guard: a fresh-character login (no save map data)
        // re-reconciles against OUR OWN renderings, which already wear the
        // right live sprite — the adapter re-records instead of rebuilding.
        // At the rule level: an applied record naming the wanted sprite
        // with the sprite alive must not rebuild.
        Assert.False(SpriteRebindRule.MustRebuild("cc:camp", "cc:camp", appliedSpriteAlive: true));

        // While a lost record with a live wanted sprite still rebuilds
        // (the save-file claim path).
        Assert.True(SpriteRebindRule.MustRebuild("cc:camp", null, appliedSpriteAlive: true));
    }
}
