using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class SyncTests
{
    private const string Alice = "author-alice";
    private const string Bob = "author-bob";
    private static DateTime _now = new(2026, 8, 27, 15, 0, 0, DateTimeKind.Utc);

    private static (PinStore Pins, RouteStore Routes) Stores(string author)
    {
        return (new PinStore(() => _now) { LocalAuthor = author },
                new RouteStore(() => _now) { LocalAuthor = author });
    }

    private static AtlasPin SharedPin(PinStore store, string name, AtlasScope scope = AtlasScope.Table)
    {
        return store.Create(pin =>
        {
            pin.Name = name;
            pin.Scope = scope;
            pin.Position = new RoadPoint(10f, 30f, 10f);
        });
    }

    [Fact]
    public void AuditColumns_AreStamped_AndSurviveTheCodec()
    {
        (PinStore pins, RouteStore routes) = Stores(Alice);
        AtlasPin pin = SharedPin(pins, "Dock");
        pins.LocalAuthor = Bob;
        pins.Mutate(pin.Id, edited => edited.Name = "Dock 2");

        Assert.Equal(Alice, pin.OwnerAuthor);
        Assert.Equal(Bob, pin.LastAuthor);

        PinCodec.ParseResult roundtrip = PinCodec.Parse(PinCodec.Serialize(new[] { pin }));
        Assert.Equal(Alice, roundtrip.Pins[0].OwnerAuthor);
        Assert.Equal(Bob, roundtrip.Pins[0].LastAuthor);

        AtlasRoute route = routes.Create(created => created.Name = "R");
        RouteCodec.ParseResult routeTrip = RouteCodec.Parse(RouteCodec.Serialize(new[] { route }));
        Assert.Equal(Alice, routeTrip.Routes[0].OwnerAuthor);
    }

    [Fact]
    public void LegacyRowsWithoutAuthors_StillParse()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            CreatedUtc = _now,
            ModifiedUtc = _now,
            Name = "Old",
        };
        string v2Row = PinCodec.SerializeRow(pin);
        string[] parts = v2Row.Split('\t');
        // Reconstruct the 22-field v1 row: drop the two author columns and
        // restore the old marker.
        var v1Fields = new List<string>();
        for (int index = 0; index < parts.Length; index++)
        {
            if (index == 18 || index == 19)
            {
                continue;
            }

            v1Fields.Add(index == parts.Length - 1 ? "1" : parts[index]);
        }

        PinCodec.ParseResult result = PinCodec.Parse(new[] { string.Join("\t", v1Fields) });
        Assert.Equal(0, result.MalformedRows);
        Assert.Equal("", Assert.Single(result.Pins).OwnerAuthor);
    }

    [Fact]
    public void CollectShared_ExcludesPrivate_IncludesSharedTombstones()
    {
        (PinStore pins, RouteStore routes) = Stores(Alice);
        SharedPin(pins, "Shared");
        AtlasPin deleted = SharedPin(pins, "Deleted");
        pins.Delete(deleted.Id);
        pins.Create(pin => pin.Name = "Private");

        (List<AtlasPin> sharedPins, _) = SyncPlanner.CollectShared(pins, routes);

        Assert.Equal(2, sharedPins.Count);
        Assert.Contains(sharedPins, pin => pin.Deleted);
        Assert.DoesNotContain(sharedPins, pin => pin.Name == "Private");
    }

    [Fact]
    public void Plan_ClassifiesNewUpdatedTombstoneSuperseded()
    {
        (PinStore alicePins, RouteStore aliceRoutes) = Stores(Alice);
        (PinStore bobPins, RouteStore bobRoutes) = Stores(Bob);

        AtlasPin newPin = SharedPin(alicePins, "New");
        AtlasPin updatedPin = SharedPin(alicePins, "Updated");
        AtlasPin deletedPin = SharedPin(alicePins, "Doomed");
        AtlasPin stalePin = SharedPin(alicePins, "Stale");

        // Bob receives everything once.
        (List<AtlasPin> shared1, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlanner.Apply(SyncPlanner.Plan(bobPins, bobRoutes, shared1, new List<AtlasRoute>()), bobPins, bobRoutes, false);

        // Alice keeps editing; Bob's copy of "Stale" advances beyond Alice's.
        alicePins.Mutate(updatedPin.Id, pin => pin.Name = "Updated 2");
        alicePins.Delete(deletedPin.Id);
        bobPins.Mutate(stalePin.Id, pin => pin.Name = "Bob newer");
        bobPins.Mutate(stalePin.Id, pin => pin.Name = "Bob newest");

        (List<AtlasPin> shared2, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlan plan = SyncPlanner.Plan(bobPins, bobRoutes, shared2, new List<AtlasRoute>());

        Assert.Empty(plan.NewPins);
        Assert.Single(plan.UpdatedPins);
        Assert.Single(plan.TombstonePins);
        Assert.Equal(2, plan.SupersededPins);
        Assert.Equal("New", newPin.Name);
    }

    [Fact]
    public void DeletedSharedEntities_NeverResurrect_OnStaleClients()
    {
        (PinStore alicePins, RouteStore aliceRoutes) = Stores(Alice);
        (PinStore bobPins, RouteStore bobRoutes) = Stores(Bob);

        AtlasPin pin = SharedPin(alicePins, "Beacon");
        (List<AtlasPin> shared, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlanner.Apply(SyncPlanner.Plan(bobPins, bobRoutes, shared, new List<AtlasRoute>()), bobPins, bobRoutes, false);

        // Alice deletes; Bob applies the tombstone.
        alicePins.Delete(pin.Id);
        (shared, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlanner.Apply(SyncPlanner.Plan(bobPins, bobRoutes, shared, new List<AtlasRoute>()), bobPins, bobRoutes, false);
        Assert.True(bobPins.TryGet(pin.Id, out AtlasPin bobCopy));
        Assert.True(bobCopy.Deleted);

        // A third, stale client that never saw the deletion shares its old
        // living copy back: the tombstone must survive on both sides.
        AtlasPin staleCopy = pin.Clone();
        staleCopy.Deleted = false;
        staleCopy.DeletedUtc = null;
        staleCopy.Revision = 2;

        SyncPlan bobPlan = SyncPlanner.Plan(bobPins, bobRoutes, new List<AtlasPin> { staleCopy }, new List<AtlasRoute>());
        SyncPlanner.Apply(bobPlan, bobPins, bobRoutes, takeRemoteOnConflict: false);
        Assert.True(bobCopy.Deleted);

        SyncPlan alicePlan = SyncPlanner.Plan(alicePins, aliceRoutes, new List<AtlasPin> { staleCopy }, new List<AtlasRoute>());
        SyncPlanner.Apply(alicePlan, alicePins, aliceRoutes, takeRemoteOnConflict: false);
        Assert.True(pin.Deleted);
    }

    [Fact]
    public void NonOwnerDelete_IsRejected_ByPolicy()
    {
        (PinStore alicePins, RouteStore aliceRoutes) = Stores(Alice);
        (PinStore bobPins, RouteStore bobRoutes) = Stores(Bob);

        AtlasPin pin = SharedPin(alicePins, "Owned by Alice");
        (List<AtlasPin> shared, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlanner.Apply(SyncPlanner.Plan(bobPins, bobRoutes, shared, new List<AtlasRoute>()), bobPins, bobRoutes, false);

        // Bob deletes his copy and shares back: Alice's plan rejects it.
        bobPins.Delete(pin.Id);
        (List<AtlasPin> bobShared, _) = SyncPlanner.CollectShared(bobPins, bobRoutes);
        SyncPlan plan = SyncPlanner.Plan(alicePins, aliceRoutes, bobShared, new List<AtlasRoute>());

        Assert.Equal(1, plan.RejectedPins);
        SyncPlanner.Apply(plan, alicePins, aliceRoutes, false);
        Assert.False(pin.Deleted);
    }

    [Fact]
    public void EqualRevisionDivergence_SurfacesAsConflict_AndResolutionConverges()
    {
        (PinStore alicePins, RouteStore aliceRoutes) = Stores(Alice);
        (PinStore bobPins, RouteStore bobRoutes) = Stores(Bob);

        AtlasPin pin = SharedPin(alicePins, "Fork");
        (List<AtlasPin> shared, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlanner.Apply(SyncPlanner.Plan(bobPins, bobRoutes, shared, new List<AtlasRoute>()), bobPins, bobRoutes, false);

        // Both edit once while offline: same revision, different content.
        alicePins.Mutate(pin.Id, edited => edited.Name = "Alice fork");
        bobPins.TryGet(pin.Id, out AtlasPin bobCopy);
        bobPins.Mutate(pin.Id, edited => edited.Name = "Bob fork");

        (List<AtlasPin> bobShared, _) = SyncPlanner.CollectShared(bobPins, bobRoutes);
        SyncPlan plan = SyncPlanner.Plan(alicePins, aliceRoutes, bobShared, new List<AtlasRoute>());

        Assert.Single(plan.PinConflicts);

        // Keep-local leaves Alice untouched.
        SyncPlanner.Apply(plan, alicePins, aliceRoutes, takeRemoteOnConflict: false);
        Assert.Equal("Alice fork", pin.Name);

        // Take-remote lands Bob's content under a NEW revision, so sharing
        // back now supersedes Bob's copy instead of conflicting forever.
        SyncPlanner.Apply(plan, alicePins, aliceRoutes, takeRemoteOnConflict: true);
        Assert.Equal("Bob fork", pin.Name);
        Assert.True(pin.Revision > bobCopy.Revision);

        (List<AtlasPin> aliceShared, _) = SyncPlanner.CollectShared(alicePins, aliceRoutes);
        SyncPlan bobPlan = SyncPlanner.Plan(bobPins, bobRoutes, aliceShared, new List<AtlasRoute>());
        Assert.Single(bobPlan.UpdatedPins);
        Assert.Empty(bobPlan.PinConflicts);
    }

    [Fact]
    public void PrivateEntities_NeverTravel_EvenIfPresentedAsIncoming()
    {
        (PinStore pins, RouteStore routes) = Stores(Alice);
        var privateIncoming = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 5,
            Name = "Smuggled",
            Scope = AtlasScope.Private,
        };

        SyncPlan plan = SyncPlanner.Plan(pins, routes, new List<AtlasPin> { privateIncoming }, new List<AtlasRoute>());
        Assert.Empty(plan.NewPins);
        Assert.Equal(1, plan.RejectedPins);
    }

    [Fact]
    public void Inbox_KeepsNewestPerAuthor_AndIsBounded()
    {
        var inbox = new SyncInbox();
        for (int index = 0; index < 12; index++)
        {
            inbox.Add(new SyncInbox.Envelope(
                $"author-{index % 10}", $"Player{index % 10}",
                new List<AtlasPin>(), new List<AtlasRoute>(), _now.AddMinutes(index)));
        }

        Assert.True(inbox.Envelopes.Count <= SyncInbox.MaxAuthors);
        Assert.True(inbox.TryTake("player9", out SyncInbox.Envelope envelope));
        Assert.Equal("author-9", envelope.AuthorId);
        Assert.False(inbox.TryTake("player9", out _));
    }
}
