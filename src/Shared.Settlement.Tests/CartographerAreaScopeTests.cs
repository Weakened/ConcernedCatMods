using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Interop.WorkArea;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>#296 slice 3: collection consumes <c>concernedcat.workarea/1</c>.
/// Every lookup here goes through the real provider responder, so the client
/// and the provider cannot drift apart; the rules pinned are the ones #296 and
/// #375 name: a chosen area that cannot be used refuses and is never replaced
/// by another shape, an area is contained by its drawn boundary, a redrawn
/// area is a changed scope, and nothing crosses worlds.</summary>
public sealed class CartographerAreaScopeTests
{
    private const string World = "123456789";
    private static readonly Guid Epoch = new Guid("11111111-2222-3333-4444-555555555555");
    private const string GroveId = "wa-0123456789abcdef0123456789abcdef";

    private static WorkAreaPolygon Polygon(params (float X, float Z)[] corners)
    {
        var points = new List<WorkAreaPoint>();
        foreach ((float x, float z) in corners)
        {
            points.Add(new WorkAreaPoint(x, z));
        }

        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(points, out WorkAreaPolygon? polygon));
        return polygon!;
    }

    /// <summary>An L-shaped grove: the circle around it covers the missing
    /// corner, the boundary does not.</summary>
    private static WorkAreaPolygon LShape(float offset = 0f) =>
        Polygon((offset, 0f), (offset + 40f, 0f), (offset + 40f, 10f), (offset + 10f, 10f), (offset + 10f, 40f), (offset, 40f));

    private sealed class FakeProvider
    {
        public string WorldUid = World;
        public bool NoWorld;
        public bool Throws;
        public readonly List<PublishedWorkArea> Areas = new();

        public Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> Endpoint =>
            request =>
            {
                if (Throws)
                {
                    throw new InvalidOperationException("provider fault");
                }

                return WorkAreaResponder.Answer(request, "1.0.0", () => NoWorld ? null : new PublishedWorkAreas(WorldUid, Areas));
            };

        public WorkAreaLookup Describe(string id) => WorkAreaClient.Describe(Endpoint, id, World, "test");

        public WorkAreaLookup List() => WorkAreaClient.List(Endpoint, World, "test");
    }

    private static FakeProvider WithGrove(WorkAreaPolygon? boundary = null)
    {
        var provider = new FakeProvider();
        provider.Areas.Add(new PublishedWorkArea(GroveId, "Birch grove", WorkAreaContract.Purposes.WoodGathering, boundary ?? LShape()));
        return provider;
    }

    private static WorkAreaChoice Choose(FakeProvider provider, string name = "birch GROVE")
    {
        Assert.True(CartographerAreaScopes.TryChoose(provider.List(), name, Epoch, out WorkAreaChoice? choice, out string message), message);
        return choice!;
    }

    private static WorkScope Build(FakeProvider provider)
    {
        Assert.True(
            CartographerAreaScopes.TryBuild(Choose(provider), provider.Describe, 31f, Epoch, out WorkScope? scope, out string failure),
            failure);
        return scope!;
    }

    // ------------------------------------------------------------ the client

    [Fact]
    public void TheClientReadsWhatTheProviderPublishes()
    {
        FakeProvider provider = WithGrove();

        WorkAreaLookup listed = provider.List();
        Assert.True(listed.IsOk);
        WorkAreaSummary entry = Assert.Single(listed.Areas);
        Assert.Equal(GroveId, entry.Id);
        Assert.Equal("Birch grove", entry.Name);
        Assert.Null(entry.Boundary);

        WorkAreaLookup described = provider.Describe(GroveId);
        Assert.True(described.IsOk);
        Assert.Equal(LShape().Revision, described.Area!.Revision);
        Assert.Equal(LShape().ToWire(), described.Area.Boundary!.ToWire());
    }

    [Fact]
    public void NoProviderNoWorldAndAThrowingProviderAreUnavailable_NeverAnEmptyWorld()
    {
        Assert.Equal(WorkAreaLookupStatus.Unavailable, WorkAreaClient.List(null, World, "test").Status);

        FakeProvider noWorld = WithGrove();
        noWorld.NoWorld = true;
        Assert.Equal(WorkAreaLookupStatus.Unavailable, noWorld.List().Status);
        Assert.Equal(WorkAreaLookupStatus.Unavailable, noWorld.Describe(GroveId).Status);

        FakeProvider faulty = WithGrove();
        faulty.Throws = true;
        Assert.Equal(WorkAreaLookupStatus.Unavailable, faulty.Describe(GroveId).Status);
        Assert.Empty(faulty.List().Areas);
    }

    [Fact]
    public void AnAnswerAboutAnotherWorldIsNeverThisWorldsArea()
    {
        FakeProvider other = WithGrove();
        other.WorldUid = "987654321";

        Assert.Equal(WorkAreaLookupStatus.WrongWorld, other.List().Status);
        Assert.Equal(WorkAreaLookupStatus.WrongWorld, other.Describe(GroveId).Status);
        Assert.Equal(WorkAreaLookupStatus.WrongWorld, WorkAreaClient.Describe(WithGrove().Endpoint, GroveId, string.Empty, "test").Status);
    }

    [Fact]
    public void ADeletedAreaIsNotFound()
    {
        Assert.Equal(WorkAreaLookupStatus.NotFound, new FakeProvider().Describe(GroveId).Status);
    }

    [Fact]
    public void AnOkReplyWithAnUnusableBoundaryIsABadReply()
    {
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> lying = request =>
        {
            var reply = new Dictionary<string, string>(WorkAreaResponder.Answer(
                request, "1.0.0", () => new PublishedWorkAreas(World, WithGrove().Areas)));
            reply[WorkAreaContract.Keys.Boundary] = "0;0|10;10|0;10|10;0"; // a figure-eight
            return reply;
        };

        WorkAreaLookup described = WorkAreaClient.Describe(lying, GroveId, World, "test");
        Assert.Equal(WorkAreaLookupStatus.BadReply, described.Status);
        Assert.Null(described.Area);
    }

    // ------------------------------------------------------------- choosing

    [Fact]
    public void ChoosingMatchesAnIdOrAUniqueNameAndRefusesAnAmbiguousOne()
    {
        FakeProvider provider = WithGrove();
        Assert.Equal(GroveId, Choose(provider, GroveId).AreaId);
        Assert.Equal(GroveId, Choose(provider).AreaId);

        Assert.False(CartographerAreaScopes.TryChoose(provider.List(), "Oak copse", Epoch, out _, out string missing));
        Assert.Contains("no work area called", missing);

        provider.Areas.Add(new PublishedWorkArea(
            "wa-ffffffffffffffffffffffffffffffff", "birch grove", WorkAreaContract.Purposes.WoodGathering, LShape(200f)));
        Assert.False(CartographerAreaScopes.TryChoose(provider.List(), "Birch grove", Epoch, out _, out string ambiguous));
        Assert.Contains("more than one", ambiguous);
    }

    [Fact]
    public void CyclingGoesThroughEveryAreaAndBackToTheUsualArea()
    {
        FakeProvider provider = WithGrove();
        provider.Areas.Add(new PublishedWorkArea(
            "wa-ffffffffffffffffffffffffffffffff", "Copse", WorkAreaContract.Purposes.WoodGathering, LShape(200f)));

        Assert.True(CartographerAreaScopes.TryCycle(provider.List(), null, Epoch, out WorkAreaChoice? first, out _));
        Assert.Equal(GroveId, first!.AreaId);
        Assert.True(CartographerAreaScopes.TryCycle(provider.List(), first, Epoch, out WorkAreaChoice? second, out _));
        Assert.Equal("Copse", second!.Name);
        Assert.True(CartographerAreaScopes.TryCycle(provider.List(), second, Epoch, out WorkAreaChoice? back, out string usual));
        Assert.Null(back);
        Assert.Contains("harvest area or the camp circle", usual);
    }

    // --------------------------------------------------------------- scopes

    [Fact]
    public void TheScopeIsTheDrawnBoundary_NotTheCircleAroundIt()
    {
        WorkScope scope = Build(WithGrove());

        Assert.Equal(WorkScopeSource.CartographerWorkArea, scope.Source);
        Assert.Equal(GroveId, scope.AreaId);
        Assert.True(scope.Contains(new SitePoint(5f, 0f, 5f)));
        Assert.True(scope.Contains(new SitePoint(35f, 0f, 5f)));

        // Inside the circle around the L, outside the L itself.
        var missingCorner = new SitePoint(30f, 0f, 30f);
        Assert.True(scope.Centre.HorizontalDistanceTo(missingCorner) <= scope.RadiusMetres);
        Assert.False(scope.Contains(missingCorner));
        Assert.True(scope.OutsideBoundary(missingCorner));

        Assert.Equal(31f, scope.Centre.Y);
        Assert.Contains("Birch grove", scope.AnchorDescription);
    }

    [Fact]
    public void AnAreaWiderThanThorsteinWorksIsRefusedWithItsSize_NeverClippedOrWidened()
    {
        FakeProvider provider = WithGrove(Polygon((0f, 0f), (120f, 0f), (120f, 120f), (0f, 120f)));

        Assert.False(CartographerAreaScopes.TryBuild(Choose(provider), provider.Describe, 0f, Epoch, out WorkScope? scope, out string failure));
        Assert.Null(scope);
        Assert.Contains("Draw a smaller area", failure);
        Assert.Contains(WorkScope.MaxRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + " m", failure);
    }

    [Fact]
    public void AChosenAreaThatCannotBeUsedRefuses_AndNamesWhy()
    {
        FakeProvider provider = WithGrove();
        WorkAreaChoice choice = Choose(provider);

        provider.Areas.Clear();
        Assert.False(CartographerAreaScopes.TryBuild(choice, provider.Describe, 0f, Epoch, out WorkScope? gone, out string deleted));
        Assert.Null(gone);
        Assert.Contains("no longer exists", deleted);
        Assert.Contains("refused rather than moved to another area", deleted);

        provider.NoWorld = true;
        Assert.False(CartographerAreaScopes.TryBuild(choice, provider.Describe, 0f, Epoch, out _, out string unavailable));
        Assert.Contains("not available", unavailable);

        Assert.False(CartographerAreaScopes.TryBuild(
            choice, _ => throw new InvalidOperationException("boom"), 0f, Epoch, out _, out string threw));
        Assert.Contains("could not be read", threw);
    }

    [Fact]
    public void AChoiceFromAnotherWorldLoadIsNoChoice()
    {
        FakeProvider provider = WithGrove();
        WorkAreaChoice choice = Choose(provider);

        Assert.False(CartographerAreaScopes.TryBuild(
            choice, provider.Describe, 0f, Guid.NewGuid(), out WorkScope? scope, out string failure));
        Assert.Null(scope);
        Assert.Contains("choose it again", failure);
        Assert.False(CartographerAreaScopes.TryBuild(null, provider.Describe, 0f, Epoch, out _, out _));
    }

    // ----------------------------------------------------------- checkpoint

    [Fact]
    public void ARedrawnAreaIsAChangedScope_AndADeletedOneIsInvalid()
    {
        FakeProvider provider = WithGrove();
        WorkScope scope = Build(provider);
        Func<SitePoint, bool> loaded = _ => true;

        Assert.Equal(
            ScopeCheck.Valid,
            ScopeCheckpoint.Revalidate(scope, CartographerAreaScopes.Observe(scope, provider.Describe, Epoch), loaded));

        provider.Areas[0] = new PublishedWorkArea(GroveId, "Birch grove", WorkAreaContract.Purposes.WoodGathering, LShape(1f));
        Assert.Equal(
            ScopeCheck.Changed,
            ScopeCheckpoint.Revalidate(scope, CartographerAreaScopes.Observe(scope, provider.Describe, Epoch), loaded));

        provider.Areas.Clear();
        Assert.Equal(
            ScopeCheck.Invalid,
            ScopeCheckpoint.Revalidate(scope, CartographerAreaScopes.Observe(scope, provider.Describe, Epoch), loaded));

        Assert.Equal(
            ScopeCheck.Invalid,
            ScopeCheckpoint.Revalidate(scope, CartographerAreaScopes.Observe(scope, null, Epoch), loaded));
    }

    [Fact]
    public void AScopeRebuiltFromTheJournalHasNoBoundary_ContainsNothingAndIsNeverValid()
    {
        WorkScope scope = Build(WithGrove());
        var row = new CollectionReboundRow(
            new OrderId("collect-0001"), scope, DeliveryTarget.HoldForPlayer());

        JournalFields fields = CustodyRowCodec.Encode(row);
        Assert.True(CustodyRowCodec.TryDecode(JournalEntryKind.CollectionRebound, fields, out CustodyRow decoded));
        WorkScope rebuilt = Assert.IsType<CollectionReboundRow>(decoded).Scope;

        Assert.Equal(WorkScopeSource.CartographerWorkArea, rebuilt.Source);
        Assert.True(rebuilt.IsMissingBoundary);
        Assert.False(rebuilt.Contains(scope.Centre));
        Assert.False(rebuilt.Contains(new SitePoint(5f, 0f, 5f)));
        Assert.True(rebuilt.OutsideBoundary(new SitePoint(5f, 0f, 5f)));

        var stillThere = new ScopeObservation(true, rebuilt.SourceRevision, Epoch);
        Assert.Equal(ScopeCheck.Invalid, ScopeCheckpoint.Revalidate(rebuilt, stillThere, _ => true));
        Assert.False(CartographerAreaScopes.Observe(rebuilt, WithGrove().Describe, Epoch).SourcePresent);
        Assert.Contains("choose it again", CooperationSentences.ForScope(rebuilt));
    }

    // --------------------------------------------------------------- rebind

    private const string CopseId = "wa-ffffffffffffffffffffffffffffffff";

    private static FakeProvider WithGroveAndCopse()
    {
        FakeProvider provider = WithGrove();
        provider.Areas.Add(new PublishedWorkArea(CopseId, "Copse", WorkAreaContract.Purposes.WoodGathering, LShape(200f)));
        return provider;
    }

    private static WorkScope FromJournal(WorkScope scope)
    {
        JournalFields fields = CustodyRowCodec.Encode(
            new CollectionReboundRow(new OrderId("collect-0002"), scope, DeliveryTarget.HoldForPlayer()));
        Assert.True(CustodyRowCodec.TryDecode(JournalEntryKind.CollectionRebound, fields, out CustodyRow decoded));
        return Assert.IsType<CollectionReboundRow>(decoded).Scope;
    }

    [Fact]
    public void ALiveOrderRebindsToItsOwnArea_WhateverIsChosenForTheNextOrder()
    {
        FakeProvider provider = WithGroveAndCopse();
        WorkScope accepted = Build(provider);
        WorkAreaChoice copse = Choose(provider, "Copse");

        Assert.True(CartographerAreaScopes.TryRebind(accepted, copse, provider.Describe, 0f, Epoch, out WorkScope? kept, out string failure), failure);
        Assert.Equal(GroveId, kept!.AreaId);
        Assert.Equal(accepted.SourceRevision, kept.SourceRevision);

        // "Usual area" for the next order does not stop a chest-only rebind.
        Assert.True(CartographerAreaScopes.TryRebind(accepted, null, provider.Describe, 0f, Epoch, out WorkScope? still, out failure), failure);
        Assert.Equal(GroveId, still!.AreaId);
    }

    [Fact]
    public void ALiveOrderWhoseAreaWasRedrawnIsNotRebound()
    {
        FakeProvider provider = WithGrove();
        WorkScope accepted = Build(provider);
        provider.Areas[0] = new PublishedWorkArea(GroveId, "Birch grove", WorkAreaContract.Purposes.WoodGathering, LShape(1f));

        Assert.False(CartographerAreaScopes.TryRebind(accepted, null, provider.Describe, 0f, Epoch, out WorkScope? scope, out string failure));
        Assert.Null(scope);
        Assert.Contains("redrawn", failure);
    }

    [Fact]
    public void ARecoveredOrderRebindsOnlyToTheSameAreaWithTheSameShape()
    {
        FakeProvider provider = WithGroveAndCopse();
        WorkScope recovered = FromJournal(Build(provider));
        Assert.True(recovered.IsMissingBoundary);

        Assert.False(CartographerAreaScopes.TryRebind(recovered, null, provider.Describe, 0f, Epoch, out _, out string unchosen));
        Assert.Contains("Choose that area again", unchosen);

        Assert.False(CartographerAreaScopes.TryRebind(
            recovered, Choose(provider, "Copse"), provider.Describe, 0f, Epoch, out WorkScope? wrong, out string other));
        Assert.Null(wrong);
        Assert.Contains("not the one this order was accepted in", other);

        Assert.True(CartographerAreaScopes.TryRebind(
            recovered, Choose(provider), provider.Describe, 0f, Epoch, out WorkScope? same, out string failure), failure);
        Assert.Equal(GroveId, same!.AreaId);
        Assert.False(same.IsMissingBoundary);

        provider.Areas[0] = new PublishedWorkArea(GroveId, "Birch grove", WorkAreaContract.Purposes.WoodGathering, LShape(1f));
        Assert.False(CartographerAreaScopes.TryRebind(
            recovered, Choose(provider), provider.Describe, 0f, Epoch, out _, out string redrawn));
        Assert.Contains("redrawn", redrawn);
    }

    [Fact]
    public void AFailedChoiceSaysTheChoiceIsUnchanged()
    {
        Assert.False(CartographerAreaScopes.TryChoose(WithGrove().List(), "Oak copse", Epoch, out _, out string message));
        Assert.StartsWith("Work area choice unchanged:", message);
    }

    [Fact]
    public void EveryCornerIsInsideTheCircle()
    {
        WorkScope scope = Build(WithGrove(Polygon((0.03f, 0.07f), (33.33f, 1.11f), (17.77f, 29.99f))));
        foreach (WorkAreaPoint corner in scope.Boundary!.Points)
        {
            Assert.True(scope.Centre.HorizontalDistanceTo(new SitePoint(corner.X, 0f, corner.Z)) <= scope.RadiusMetres);
        }
    }

    [Fact]
    public void AnAreaExactlyAtTheLimitIsAccepted_AndJustBeyondItRefused()
    {
        // A square centred on the origin: corners 48 m out along the axes.
        FakeProvider atLimit = WithGrove(Polygon((48f, 0f), (0f, 48f), (-48f, 0f), (0f, -48f)));
        Assert.True(CartographerAreaScopes.TryBuild(Choose(atLimit), atLimit.Describe, 0f, Epoch, out WorkScope? scope, out string failure), failure);
        Assert.Equal(WorkScope.MaxRadiusMetres, scope!.RadiusMetres);

        FakeProvider beyond = WithGrove(Polygon((48.2f, 0f), (0f, 48.2f), (-48.2f, 0f), (0f, -48.2f)));
        Assert.False(CartographerAreaScopes.TryBuild(Choose(beyond), beyond.Describe, 0f, Epoch, out _, out _));
    }

    [Fact]
    public void CircleScopesAreUnchanged()
    {
        var circle = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 0f, 0f), 30f, "your bed", 4, Epoch);
        Assert.True(circle.Contains(new SitePoint(29f, 0f, 0f)));
        Assert.False(circle.Contains(new SitePoint(31f, 0f, 0f)));
        Assert.False(circle.OutsideBoundary(new SitePoint(500f, 0f, 0f)));
        Assert.False(circle.IsMissingBoundary);
    }
}
