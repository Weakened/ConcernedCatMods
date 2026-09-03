using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>DEF-v1.0-004 regression suite: the managed-pin rendering
/// lifecycle. A fake map executes the ledger's decisions exactly like
/// PinAdapter does (Add/Remove/Replace/UpdateChecked against a rendering
/// list), so these tests prove the adopt → edit → apply → restart flow
/// keeps exactly one rendering per pin.</summary>
public class PinRenderingLedgerTests
{
    private sealed class FakeRendering
    {
        public string Name = "";
        public float X;
        public float Y;
        public float Z;
        public int Type;
        public bool Checked;
    }

    /// <summary>Mirrors PinAdapter's execution of ledger decisions against
    /// an in-memory "map" of renderings.</summary>
    private sealed class FakeMap
    {
        public readonly List<FakeRendering> Renderings = new();
        public readonly PinRenderingLedger<FakeRendering> Ledger = new(rendering =>
            new PinRenderingLedger<FakeRendering>.RenderingState(
                rendering.Name, rendering.X, rendering.Y, rendering.Z, rendering.Type, rendering.Checked));

        public FakeRendering AddVanilla(string name, float x, float z, int type = 3)
        {
            var rendering = new FakeRendering { Name = name, X = x, Z = z, Type = type };
            Renderings.Add(rendering);
            return rendering;
        }

        public void SyncPin(PinStore store, AtlasId id)
        {
            if (!store.TryGet(id, out AtlasPin managed))
            {
                return;
            }

            int wantedType = IconRegistry.ResolveVanillaType(managed.IconId);
            switch (Ledger.DecideSync(managed, wantedType, out FakeRendering? existing))
            {
                case PinRenderingLedger<FakeRendering>.SyncDecision.Add:
                    AddManaged(managed);
                    break;
                case PinRenderingLedger<FakeRendering>.SyncDecision.Remove:
                    Renderings.Remove(existing!);
                    Ledger.Untrack(existing!);
                    break;
                case PinRenderingLedger<FakeRendering>.SyncDecision.Replace:
                    Renderings.Remove(existing!);
                    Ledger.Untrack(existing!);
                    AddManaged(managed);
                    break;
                case PinRenderingLedger<FakeRendering>.SyncDecision.UpdateChecked:
                    existing!.Checked = managed.Checked;
                    break;
            }
        }

        public void SyncAll(PinStore store)
        {
            foreach (AtlasPin managed in store.All)
            {
                SyncPin(store, managed.Id);
            }
        }

        /// <summary>Mirrors ReconcileOnMapReady: reset + claim by position
        /// and exact name, each rendering claimable once.</summary>
        public void Reconcile(PinStore store)
        {
            Ledger.Reset();
            var unclaimed = new List<FakeRendering>(Renderings);
            foreach (AtlasPin managed in store.All)
            {
                FakeRendering? match = Ledger.ClaimMatch(unclaimed, managed);
                if (managed.Deleted || managed.Archived)
                {
                    if (match is not null)
                    {
                        Renderings.Remove(match);
                    }

                    continue;
                }

                if (match is not null)
                {
                    Ledger.Track(match, managed.Id);
                    SyncPin(store, managed.Id);
                }
                else
                {
                    AddManaged(managed);
                }
            }
        }

        private void AddManaged(AtlasPin managed)
        {
            var rendering = new FakeRendering
            {
                Name = managed.Name,
                X = managed.Position.X,
                Y = managed.Position.Y,
                Z = managed.Position.Z,
                Type = IconRegistry.ResolveVanillaType(managed.IconId),
                Checked = managed.Checked,
            };
            Renderings.Add(rendering);
            Ledger.Track(rendering, managed.Id);
        }
    }

    private static AtlasPin AdoptInto(PinStore store, FakeMap map, FakeRendering vanilla)
    {
        AtlasPin managed = store.Create(pin =>
        {
            pin.Name = vanilla.Name;
            pin.IconId = IconRegistry.FromVanillaType(vanilla.Type);
            pin.Checked = vanilla.Checked;
            pin.Source = AtlasPinSource.AdoptedVanilla;
            pin.Position = new RoadPoint(vanilla.X, vanilla.Y, vanilla.Z);
        });
        map.Ledger.Track(vanilla, managed.Id);
        return managed;
    }

    [Fact]
    public void AdoptEditApply_KeepsExactlyOneRendering()
    {
        var store = new PinStore();
        var map = new FakeMap();
        FakeRendering vanilla = map.AddVanilla("Home", 100f, 200f, type: 1);

        AtlasPin managed = AdoptInto(store, map, vanilla);
        store.Mutate(managed.Id, pin => pin.Name = "Smoke Home");
        map.SyncPin(store, managed.Id);

        Assert.Single(map.Renderings);
        Assert.Equal("Smoke Home", map.Renderings[0].Name);
        Assert.True(map.Ledger.TryGetRendering(managed.Id, out FakeRendering tracked));
        Assert.Same(map.Renderings[0], tracked);
    }

    [Fact]
    public void RepeatedEdits_AlwaysTargetTheSameSingleRendering()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));

        store.Mutate(managed.Id, pin => pin.Name = "Smoke Home");
        map.SyncPin(store, managed.Id);
        store.Mutate(managed.Id, pin => pin.IconId = "vanilla:portal");
        map.SyncPin(store, managed.Id);
        store.Mutate(managed.Id, pin => pin.Position = new RoadPoint(15f, 0f, 25f));
        map.SyncPin(store, managed.Id);
        store.Mutate(managed.Id, pin => pin.Checked = true);
        map.SyncPin(store, managed.Id);

        Assert.Single(map.Renderings);
        Assert.Equal("Smoke Home", map.Renderings[0].Name);
        Assert.Equal(6, map.Renderings[0].Type);
        Assert.Equal(15f, map.Renderings[0].X);
        Assert.True(map.Renderings[0].Checked);
        Assert.Equal(1, map.Ledger.TrackedCount);
    }

    [Fact]
    public void RestartReconcile_AfterEdits_ClaimsExactlyOneRendering()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));
        store.Mutate(managed.Id, pin => pin.Name = "Smoke Home");
        map.SyncPin(store, managed.Id);

        // Restart: the saved rendering carries the last synced name, the
        // ledger starts empty.
        map.Reconcile(store);

        Assert.Single(map.Renderings);
        Assert.Equal("Smoke Home", map.Renderings[0].Name);
        Assert.Equal(1, map.Ledger.TrackedCount);
    }

    [Fact]
    public void MidSessionFullReconcile_WithStaleRenderingName_WouldDuplicate()
    {
        // The DEF-v1.0-004 mechanism, pinned as documentation: a full
        // reconcile after a rename but before the rendering was synced
        // cannot claim the stale-named rendering, so it adds a second one.
        // This is exactly why in-session mutations must use the targeted
        // sync path and never the reset+claim path.
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));
        store.Mutate(managed.Id, pin => pin.Name = "Smoke Home");

        map.Reconcile(store);

        Assert.Equal(2, map.Renderings.Count);
    }

    [Fact]
    public void ClaimMatch_RequiresPositionAndExactName_NeverSteals()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = store.Create(pin =>
        {
            pin.Name = "Harbor";
            pin.Position = new RoadPoint(0f, 0f, 0f);
        });

        FakeRendering sameNameFarAway = map.AddVanilla("Harbor", 5f, 0f);
        FakeRendering samePlaceOtherName = map.AddVanilla("Dock", 0f, 0f);
        var unclaimed = new List<FakeRendering> { sameNameFarAway, samePlaceOtherName };

        Assert.Null(map.Ledger.ClaimMatch(unclaimed, managed));

        FakeRendering exact = map.AddVanilla("Harbor", 0.2f, 0.2f);
        unclaimed.Add(exact);
        Assert.Same(exact, map.Ledger.ClaimMatch(unclaimed, managed));
        Assert.DoesNotContain(exact, unclaimed);
        // Claimed once: a second pin with identical data cannot re-claim it.
        Assert.Null(map.Ledger.ClaimMatch(unclaimed, managed));
    }

    [Fact]
    public void DeleteThenRestore_NeverLeavesTwoRenderings()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));

        store.Delete(managed.Id);
        map.SyncPin(store, managed.Id);
        Assert.Empty(map.Renderings);
        Assert.Equal(0, map.Ledger.TrackedCount);

        store.Restore(managed.Id);
        map.SyncPin(store, managed.Id);
        Assert.Single(map.Renderings);
        Assert.Equal("Home", map.Renderings[0].Name);
    }

    [Fact]
    public void Archive_RemovesRendering_UnarchiveRestoresOne()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));

        store.Mutate(managed.Id, pin => pin.Archived = true);
        map.SyncPin(store, managed.Id);
        Assert.Empty(map.Renderings);

        store.Mutate(managed.Id, pin => pin.Archived = false);
        map.SyncPin(store, managed.Id);
        Assert.Single(map.Renderings);
    }

    [Fact]
    public void InSessionBatchSync_NeverOrphansOrDuplicates()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin first = AdoptInto(store, map, map.AddVanilla("A", 0f, 0f));
        AtlasPin second = AdoptInto(store, map, map.AddVanilla("B", 50f, 0f));
        AtlasPin third = AdoptInto(store, map, map.AddVanilla("C", 100f, 0f));

        store.Mutate(first.Id, pin => pin.Name = "A renamed");
        store.Mutate(second.Id, pin => pin.Name = "B renamed");
        store.Delete(third.Id);
        AtlasPin created = store.Create(pin =>
        {
            pin.Name = "D";
            pin.Position = new RoadPoint(150f, 0f, 0f);
        });

        map.SyncAll(store);

        Assert.Equal(3, map.Renderings.Count);
        Assert.Equal(3, map.Ledger.TrackedCount);
        foreach (AtlasPin pin in store.Living)
        {
            Assert.True(map.Ledger.TryGetRendering(pin.Id, out FakeRendering rendering));
            Assert.Equal(pin.Name, rendering.Name);
        }

        Assert.False(map.Ledger.TryGetRendering(third.Id, out _));
        Assert.True(map.Ledger.TryGetRendering(created.Id, out _));
    }

    [Fact]
    public void CheckedOnlyChange_UpdatesInPlace_WithoutReplacingTheRendering()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = AdoptInto(store, map, map.AddVanilla("Home", 10f, 20f));
        FakeRendering original = map.Renderings[0];

        store.Mutate(managed.Id, pin => pin.Checked = true);
        map.SyncPin(store, managed.Id);

        Assert.Single(map.Renderings);
        Assert.Same(original, map.Renderings[0]);
        Assert.True(original.Checked);
    }

    [Fact]
    public void DecideSync_UntrackedDeletedPin_NeedsNothing()
    {
        var store = new PinStore();
        var map = new FakeMap();
        AtlasPin managed = store.Create(pin => pin.Name = "Ghost");
        store.Delete(managed.Id);

        Assert.Equal(
            PinRenderingLedger<FakeRendering>.SyncDecision.None,
            map.Ledger.DecideSync(managed, 3, out FakeRendering? existing));
        Assert.Null(existing);
    }

    [Fact]
    public void ResetAndTrackBasics()
    {
        var map = new FakeMap();
        FakeRendering rendering = map.AddVanilla("Home", 1f, 2f);
        var id = AtlasId.NewPin();

        map.Ledger.Track(rendering, id);
        Assert.True(map.Ledger.IsTracked(rendering));
        Assert.True(map.Ledger.TryGetId(rendering, out AtlasId roundTripped));
        Assert.Equal(id, roundTripped);

        map.Ledger.Untrack(rendering);
        Assert.False(map.Ledger.IsTracked(rendering));
        Assert.Equal(0, map.Ledger.TrackedCount);

        map.Ledger.Track(rendering, id);
        map.Ledger.Reset();
        Assert.Equal(0, map.Ledger.TrackedCount);
        Assert.False(map.Ledger.TryGetRendering(id, out _));
    }

    [Fact]
    public void QuickPinCapture_TargetedSync_RendersImmediately_NoDuplicates_SurvivesRestart()
    {
        // RC8-10 regression: TryCapture creates the store entity; the
        // runtime must then run the targeted sync path so the rendering
        // appears IMMEDIATELY — and repeating the sync (autosave, later
        // edits) must never duplicate it.
        var map = new FakeMap();
        var store = new PinStore();

        AtlasPin quick = store.Create(pin =>
        {
            pin.Name = "Copper deposit";
            pin.IconId = "cc:resource";
            pin.Category = "Resources";
            pin.Source = AtlasPinSource.Generated;
            pin.Position = new RoadPoint(120f, 30f, -45f);
        });

        map.SyncAll(store);
        Assert.Single(map.Renderings);
        Assert.Equal("Copper deposit", map.Renderings[0].Name);

        map.SyncAll(store);
        map.SyncAll(store);
        Assert.Single(map.Renderings);

        // Restart: reconcile claims the saved rendering back — still one.
        map.Reconcile(store);
        Assert.Single(map.Renderings);
        Assert.True(map.Ledger.TryGetRendering(quick.Id, out FakeRendering linked));
        Assert.Equal("Copper deposit", linked.Name);
    }
}
