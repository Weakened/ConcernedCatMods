using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Navigation;

/// <summary>The deterministic focus order of every Teamster panel (CT-031),
/// the single source of truth a controller walks and the buttons-first audit
/// checks. Each panel lists its focusable elements in traversal order;
/// building a <see cref="FocusRing"/> from one gives the live navigation.
/// Adding a panel or a control means adding it here, so the reachability and
/// buttons-first tests fail until the new element is placed in the order.
/// The catalog is reconciled against the shipped panels: text-entry controls
/// (the manifest filter, the trip test-mass field) are modeled with
/// <c>isButton: false</c> so the buttons-first audit is real, not circular —
/// every panel must still offer at least one button.</summary>
public static class NavigationCatalog
{
    public const string CartStatusPanel = "cart-status";
    public const string CargoManifestPanel = "cargo-manifest";
    public const string TripHistoryPanel = "trip-history";
    public const string RecoveryGuidancePanel = "recovery-guidance";
    public const string RoutePickerPanel = "route-picker";
    public const string RouteReportPanel = "route-report";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FocusItem>> Panels =
        new Dictionary<string, IReadOnlyList<FocusItem>>
        {
            [CartStatusPanel] = new[]
            {
                new FocusItem("status.trips", "Trips", isButton: true),
                new FocusItem("status.routes", "Routes", isButton: true),
                new FocusItem("status.brake", "Engage/Release brake", isButton: true),
                new FocusItem("status.manifest", "Manifest", isButton: true),
                new FocusItem("status.guidance", "Guidance", isButton: true),
                new FocusItem("status.close", "Close", isButton: true),
            },
            [CargoManifestPanel] = new[]
            {
                new FocusItem("manifest.sort", "Sort column", isButton: true),
                new FocusItem("manifest.filter", "Filter", isButton: false), // text field
                new FocusItem("manifest.close", "Close", isButton: true),
            },
            [TripHistoryPanel] = new[]
            {
                new FocusItem("trips.sort", "Sort column", isButton: true),
                new FocusItem("trips.mass", "Hypothetical mass", isButton: false), // text field
                new FocusItem("trips.selectA", "Select A", isButton: true),
                new FocusItem("trips.selectB", "Select B", isButton: true),
                new FocusItem("trips.delete", "Delete", isButton: true),
                new FocusItem("trips.close", "Close", isButton: true),
            },
            [RecoveryGuidancePanel] = new[]
            {
                new FocusItem("guidance.close", "Close", isButton: true),
            },
            [RoutePickerPanel] = new[]
            {
                new FocusItem("routes.list", "Route list", isButton: true),
                new FocusItem("routes.clear", "Clear", isButton: true),
                new FocusItem("routes.report", "Report", isButton: true),
                new FocusItem("routes.close", "Close", isButton: true),
            },
            [RouteReportPanel] = new[]
            {
                new FocusItem("report.close", "Close", isButton: true),
            },
        };

    /// <summary>All panel ids, for the reachability/buttons-first tests.</summary>
    public static IEnumerable<string> PanelIds => Panels.Keys;

    public static bool TryGetOrder(string panelId, out IReadOnlyList<FocusItem> items)
    {
        return Panels.TryGetValue(panelId, out items!);
    }

    /// <summary>Builds a fresh focus ring for a panel, or an empty ring for
    /// an unknown panel id (fail safe — never throws).</summary>
    public static FocusRing RingFor(string panelId)
    {
        return TryGetOrder(panelId, out IReadOnlyList<FocusItem> items)
            ? new FocusRing(items)
            : new FocusRing(System.Array.Empty<FocusItem>());
    }
}
