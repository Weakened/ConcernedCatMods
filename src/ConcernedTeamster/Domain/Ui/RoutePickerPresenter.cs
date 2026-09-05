using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless route picker (CT-022): lists the current world's
/// Cartographer routes for read-only selection. The documented eligibility
/// rule: a route is ELIGIBLE when it is not archived and has at least two
/// points (one segment — anything less cannot be profiled). Archived routes
/// are hidden entirely (Cartographer's own UI treats archived as put away);
/// too-short routes are listed with an explicit reason so a freshly drawn
/// route is never a mystery. Selection is held by the route's stable id:
/// a rename refreshes the displayed name and keeps the selection, while
/// deletion, archiving, geometry loss, or an unreadable catalog invalidate
/// it with an explicit status line — never a crash, never a stale ghost.
/// Fail closed: when the catalog is unreadable the selection clears rather
/// than survive into a world it may not belong to.</summary>
public static class RoutePickerPresenter
{
    public const int MinimumEligiblePoints = 2;

    public sealed class Row
    {
        public Row(Guid routeId, string text, bool eligible)
        {
            RouteId = routeId;
            Text = text;
            Eligible = eligible;
        }

        public Guid RouteId { get; }

        public string Text { get; }

        /// <summary>Only eligible rows are click-selectable in the panel.</summary>
        public bool Eligible { get; }
    }

    public sealed class ViewModel
    {
        public ViewModel(string statusLine, IReadOnlyList<Row> rows, Guid? effectiveSelectedId, bool selectionLost)
        {
            StatusLine = statusLine;
            Rows = rows;
            EffectiveSelectedId = effectiveSelectedId;
            SelectionLost = selectionLost;
        }

        /// <summary>Always states the surface's condition explicitly:
        /// unreadable catalog, empty world, lost selection, or the current
        /// selection's name.</summary>
        public string StatusLine { get; }

        public IReadOnlyList<Row> Rows { get; }

        /// <summary>The selection after validation against the current
        /// catalog; null when nothing is selected or it was invalidated.
        /// Callers store this back — the presenter is the only validator.</summary>
        public Guid? EffectiveSelectedId { get; }

        /// <summary>True when a previously held selection was invalidated by
        /// THIS presentation (deleted, archived, geometry lost, unreadable).</summary>
        public bool SelectionLost { get; }
    }

    public static ViewModel Present(
        bool readable, IReadOnlyList<CartographerRouteSnapshot>? routes, Guid? selectedId)
    {
        if (!readable || routes is null)
        {
            return new ViewModel(
                selectedId is null
                    ? "Cartographer routes are not readable right now."
                    : "Cartographer routes are not readable right now — selection cleared.",
                Array.Empty<Row>(),
                effectiveSelectedId: null,
                selectionLost: selectedId is not null);
        }

        var visible = new List<CartographerRouteSnapshot>(routes.Count);
        foreach (CartographerRouteSnapshot route in routes)
        {
            if (route is not null && !route.Archived)
            {
                visible.Add(route);
            }
        }

        if (visible.Count == 0)
        {
            return new ViewModel(
                selectedId is null
                    ? "No routes in this world yet — draw one in Cartographer."
                    : "Selected route is no longer available in Cartographer — pick another.",
                Array.Empty<Row>(),
                effectiveSelectedId: null,
                selectionLost: selectedId is not null);
        }

        visible.Sort((left, right) =>
        {
            int comparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
        });

        Guid? effective = null;
        string? selectedName = null;
        var rows = new Row[visible.Count];
        for (int index = 0; index < visible.Count; index++)
        {
            CartographerRouteSnapshot route = visible[index];
            bool eligible = route.Points.Count >= MinimumEligiblePoints;
            bool isSelected = eligible && selectedId is not null && route.Id == selectedId.Value;
            if (isSelected)
            {
                effective = route.Id;
                selectedName = DisplayName(route);
            }

            string text = (isSelected ? "[SEL] " : "      ") + DisplayName(route) +
                (eligible
                    ? "  " + HorizontalLengthMeters(route.Points).ToString("F0", CultureInfo.InvariantCulture) +
                        " m  (" + route.Points.Count.ToString(CultureInfo.InvariantCulture) + " pts)"
                    : "  (no usable geometry)");
            rows[index] = new Row(route.Id, text, eligible);
        }

        bool lost = selectedId is not null && effective is null;
        string status = lost
            ? "Selected route is no longer available in Cartographer — pick another."
            : effective is null
                ? "Pick a route to profile."
                : "Selected: " + selectedName;
        return new ViewModel(status, rows, effective, lost);
    }

    /// <summary>Ground-plan polyline length: the sum of horizontal (XZ)
    /// segment distances. Height differences are grade material for the
    /// profiler (CT-023), not length material for the picker.</summary>
    public static float HorizontalLengthMeters(IReadOnlyList<CartographerRoutePoint> points)
    {
        float total = 0f;
        for (int index = 1; index < points.Count; index++)
        {
            float dx = points[index].X - points[index - 1].X;
            float dz = points[index].Z - points[index - 1].Z;
            total += (float)Math.Sqrt((dx * dx) + (dz * dz));
        }

        return total;
    }

    private static string DisplayName(CartographerRouteSnapshot route)
    {
        return route.Name.Length > 0 ? route.Name : "(unnamed route)";
    }
}
