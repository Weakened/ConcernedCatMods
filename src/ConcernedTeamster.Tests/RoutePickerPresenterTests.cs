using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-022: the route picker presenter must list exactly the
/// eligible routes under the documented rule (not archived, ≥2 points;
/// archived hidden, short geometry listed with its reason), hold selection
/// by stable id across renames, and invalidate it with an explicit state —
/// never a crash or a stale ghost — when the source deletes, archives,
/// shrinks, or stops being readable mid-session.</summary>
public class RoutePickerPresenterTests
{
    private static CartographerRouteSnapshot Route(
        Guid id, string name, bool archived = false, params (float X, float Y, float Z)[] points)
    {
        var copied = new List<CartographerRoutePoint>(points.Length);
        foreach ((float x, float y, float z) in points)
        {
            copied.Add(new CartographerRoutePoint(x, y, z));
        }

        return new CartographerRouteSnapshot(id, name, archived, copied);
    }

    private static (float, float, float)[] Line(int pointCount)
    {
        var points = new (float, float, float)[pointCount];
        for (int index = 0; index < pointCount; index++)
        {
            points[index] = (index * 10f, 0f, 0f);
        }

        return points;
    }

    // -- listing and eligibility --

    [Fact]
    public void Present_ListsEligibleRoutesSortedByNameThenId()
    {
        Guid idA = Guid.NewGuid();
        Guid idB = Guid.NewGuid();
        var routes = new List<CartographerRouteSnapshot>
        {
            Route(idA, "Zinc haul", points: Line(3)),
            Route(idB, "copper run", points: Line(2)),
        };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, routes, null);

        Assert.Equal(2, viewModel.Rows.Count);
        Assert.Contains("copper run", viewModel.Rows[0].Text);
        Assert.Contains("Zinc haul", viewModel.Rows[1].Text);
        Assert.True(viewModel.Rows[0].Eligible);
        Assert.Contains("20 m", viewModel.Rows[1].Text);
        Assert.Contains("(3 pts)", viewModel.Rows[1].Text);
        Assert.Equal("Pick a route to profile.", viewModel.StatusLine);
        Assert.Null(viewModel.EffectiveSelectedId);
        Assert.False(viewModel.SelectionLost);
    }

    [Fact]
    public void Present_ArchivedRoutesAreHidden()
    {
        var routes = new List<CartographerRouteSnapshot>
        {
            Route(Guid.NewGuid(), "Put away", archived: true, points: Line(4)),
            Route(Guid.NewGuid(), "Active", points: Line(2)),
        };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, routes, null);

        Assert.Single(viewModel.Rows);
        Assert.Contains("Active", viewModel.Rows[0].Text);
    }

    [Fact]
    public void Present_ShortGeometryListedWithReasonAndNotEligible()
    {
        Guid shortId = Guid.NewGuid();
        var routes = new List<CartographerRouteSnapshot>
        {
            Route(shortId, "Just started", points: Line(1)),
        };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, routes, null);

        Assert.Single(viewModel.Rows);
        Assert.False(viewModel.Rows[0].Eligible);
        Assert.Contains("(no usable geometry)", viewModel.Rows[0].Text);
        Assert.DoesNotContain(" m ", viewModel.Rows[0].Text);
    }

    [Fact]
    public void Present_UnnamedRoute_ShowsPlaceholder()
    {
        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(
            true, new List<CartographerRouteSnapshot> { Route(Guid.NewGuid(), "", points: Line(2)) }, null);

        Assert.Contains("(unnamed route)", viewModel.Rows[0].Text);
    }

    [Fact]
    public void Present_EmptyCatalog_ExplicitMessage()
    {
        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(
            true, new List<CartographerRouteSnapshot>(), null);

        Assert.True(viewModel.Rows.Count == 0);
        Assert.Contains("No routes in this world yet", viewModel.StatusLine);
        Assert.False(viewModel.SelectionLost);
    }

    [Fact]
    public void HorizontalLength_IgnoresHeight()
    {
        var points = new List<CartographerRoutePoint>
        {
            new(0f, 10f, 0f),
            new(3f, 55f, 4f),
        };

        Assert.Equal(5f, RoutePickerPresenter.HorizontalLengthMeters(points), 3);
    }

    // -- selection lifecycle --

    [Fact]
    public void Present_SelectedRoute_MarkedAndNamedInStatus()
    {
        Guid id = Guid.NewGuid();
        var routes = new List<CartographerRouteSnapshot>
        {
            Route(id, "Ore road", points: Line(2)),
            Route(Guid.NewGuid(), "Other", points: Line(2)),
        };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, routes, id);

        Assert.Equal(id, viewModel.EffectiveSelectedId);
        Assert.False(viewModel.SelectionLost);
        Assert.Equal("Selected: Ore road", viewModel.StatusLine);
        Assert.Single(viewModel.Rows, row => row.Text.StartsWith("[SEL] ", StringComparison.Ordinal));
    }

    [Fact]
    public void Present_DeletedSelectedRoute_InvalidatesExplicitly()
    {
        Guid id = Guid.NewGuid();
        var before = new List<CartographerRouteSnapshot> { Route(id, "Doomed", points: Line(2)) };
        RoutePickerPresenter.ViewModel first = RoutePickerPresenter.Present(true, before, id);
        Assert.Equal(id, first.EffectiveSelectedId);

        var after = new List<CartographerRouteSnapshot> { Route(Guid.NewGuid(), "Different", points: Line(2)) };
        RoutePickerPresenter.ViewModel second = RoutePickerPresenter.Present(true, after, first.EffectiveSelectedId);

        Assert.Null(second.EffectiveSelectedId);
        Assert.True(second.SelectionLost);
        Assert.Contains("no longer available", second.StatusLine);
        Assert.DoesNotContain(second.Rows, row => row.Text.StartsWith("[SEL] ", StringComparison.Ordinal));
    }

    [Fact]
    public void Present_SelectedRouteArchivedMidSession_InvalidatesExplicitly()
    {
        Guid id = Guid.NewGuid();
        var after = new List<CartographerRouteSnapshot> { Route(id, "Shelved", archived: true, points: Line(2)) };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, after, id);

        Assert.Null(viewModel.EffectiveSelectedId);
        Assert.True(viewModel.SelectionLost);
        Assert.Contains("no longer available", viewModel.StatusLine);
    }

    [Fact]
    public void Present_SelectedRouteLosesGeometry_InvalidatesExplicitly()
    {
        Guid id = Guid.NewGuid();
        var after = new List<CartographerRouteSnapshot> { Route(id, "Erased", points: Line(1)) };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, after, id);

        Assert.Null(viewModel.EffectiveSelectedId);
        Assert.True(viewModel.SelectionLost);
        Assert.Single(viewModel.Rows);
        Assert.False(viewModel.Rows[0].Eligible);
    }

    [Fact]
    public void Present_RenamedSelectedRoute_KeepsSelectionShowsNewName()
    {
        Guid id = Guid.NewGuid();
        var after = new List<CartographerRouteSnapshot> { Route(id, "New name", points: Line(2)) };

        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(true, after, id);

        Assert.Equal(id, viewModel.EffectiveSelectedId);
        Assert.False(viewModel.SelectionLost);
        Assert.Equal("Selected: New name", viewModel.StatusLine);
        Assert.Contains("[SEL] New name", viewModel.Rows[0].Text);
    }

    // -- unreadable catalog (fail closed) --

    [Fact]
    public void Present_UnreadableWithSelection_ClearsExplicitly()
    {
        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(false, null, Guid.NewGuid());

        Assert.Empty(viewModel.Rows);
        Assert.Null(viewModel.EffectiveSelectedId);
        Assert.True(viewModel.SelectionLost);
        Assert.Contains("not readable right now — selection cleared", viewModel.StatusLine);
    }

    [Fact]
    public void Present_UnreadableWithoutSelection_JustStatesIt()
    {
        RoutePickerPresenter.ViewModel viewModel = RoutePickerPresenter.Present(false, null, null);

        Assert.Empty(viewModel.Rows);
        Assert.False(viewModel.SelectionLost);
        Assert.Contains("not readable right now", viewModel.StatusLine);
    }
}
