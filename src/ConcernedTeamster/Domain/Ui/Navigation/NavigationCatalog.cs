using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

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

    // Labels resolve through the catalog when the dictionary first builds;
    // the type initializes lazily on first navigation use, which in game is
    // after Plugin.Awake has loaded any translation overrides.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FocusItem>> Panels =
        new Dictionary<string, IReadOnlyList<FocusItem>>
        {
            [CartStatusPanel] = new[]
            {
                new FocusItem("status.trips", TeamsterStrings.Get("nav.trips"), isButton: true),
                new FocusItem("status.routes", TeamsterStrings.Get("nav.routes"), isButton: true),
                new FocusItem("status.brake", TeamsterStrings.Get("nav.brake"), isButton: true),
                new FocusItem("status.manifest", TeamsterStrings.Get("nav.manifest"), isButton: true),
                new FocusItem("status.guidance", TeamsterStrings.Get("nav.guidance"), isButton: true),
                new FocusItem("status.close", TeamsterStrings.Get("nav.close"), isButton: true),
            },
            [CargoManifestPanel] = new[]
            {
                new FocusItem("manifest.sort", TeamsterStrings.Get("nav.sortColumn"), isButton: true),
                new FocusItem("manifest.filter", TeamsterStrings.Get("nav.filter"), isButton: false), // text field
                new FocusItem("manifest.close", TeamsterStrings.Get("nav.close"), isButton: true),
            },
            [TripHistoryPanel] = new[]
            {
                new FocusItem("trips.sort", TeamsterStrings.Get("nav.sortColumn"), isButton: true),
                new FocusItem("trips.mass", TeamsterStrings.Get("nav.hypotheticalMass"), isButton: false), // text field
                new FocusItem("trips.selectA", TeamsterStrings.Get("nav.selectA"), isButton: true),
                new FocusItem("trips.selectB", TeamsterStrings.Get("nav.selectB"), isButton: true),
                new FocusItem("trips.delete", TeamsterStrings.Get("nav.delete"), isButton: true),
                new FocusItem("trips.close", TeamsterStrings.Get("nav.close"), isButton: true),
            },
            [RecoveryGuidancePanel] = new[]
            {
                new FocusItem("guidance.close", TeamsterStrings.Get("nav.close"), isButton: true),
            },
            [RoutePickerPanel] = new[]
            {
                new FocusItem("routes.list", TeamsterStrings.Get("nav.routeList"), isButton: true),
                new FocusItem("routes.clear", TeamsterStrings.Get("nav.clear"), isButton: true),
                new FocusItem("routes.report", TeamsterStrings.Get("nav.report"), isButton: true),
                new FocusItem("routes.close", TeamsterStrings.Get("nav.close"), isButton: true),
            },
            [RouteReportPanel] = new[]
            {
                new FocusItem("report.close", TeamsterStrings.Get("nav.close"), isButton: true),
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
