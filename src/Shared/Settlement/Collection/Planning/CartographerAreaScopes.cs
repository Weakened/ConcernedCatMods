using System;
using System.Collections.Generic;
using TheConcernedCat.Interop.WorkArea;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>The player's choice of a Concerned Cartographer work area for the
/// next collection order, made in one world load and never carried into
/// another.</summary>
internal sealed class WorkAreaChoice
{
    public WorkAreaChoice(string areaId, string name, Guid worldLoadEpoch)
    {
        AreaId = areaId ?? throw new ArgumentNullException(nameof(areaId));
        Name = name ?? string.Empty;
        WorldLoadEpoch = worldLoadEpoch;
    }

    public string AreaId { get; }

    public string Name { get; }

    public Guid WorldLoadEpoch { get; }
}

/// <summary>#296 slice 3: everything collection decides about a Cartographer
/// work area, without the game (the runtime supplies the provider lookups).
///
/// The rules it holds, because they are the ones that must not drift:
/// <list type="bullet">
/// <item>A chosen area is asked for again whenever a scope is built; an area
/// that is unavailable, gone, from another world, unreadable or too large is a
/// refusal with its reason. Nothing here ever substitutes the default circle,
/// the harvest area or any other shape for a chosen area.</item>
/// <item>A choice made in another world load is no choice.</item>
/// <item>Re-observing an accepted scope asks the provider by the area's id; a
/// redrawn area is "changed", an absent or unreadable one "not present".</item>
/// </list>
/// Nothing here writes anything anywhere.</summary>
internal static class CartographerAreaScopes
{
    /// <summary>Chooses an area by its exact id, or by its name ignoring case.
    /// A name two areas share is refused rather than resolved by order.</summary>
    public static bool TryChoose(
        WorkAreaLookup listed, string typed, Guid worldLoadEpoch, out WorkAreaChoice? choice, out string message)
    {
        choice = null;
        if (listed == null || !listed.IsOk)
        {
            message = "No work area chosen: " + (listed?.Reason ?? "Concerned Cartographer did not answer") + ".";
            return false;
        }

        string wanted = (typed ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            message = "Name the work area to use.";
            return false;
        }

        WorkAreaSummary? match = null;
        foreach (WorkAreaSummary area in listed.Areas)
        {
            if (string.Equals(area.Id, wanted, StringComparison.Ordinal))
            {
                match = area;
                break;
            }
        }

        if (match == null)
        {
            foreach (WorkAreaSummary area in listed.Areas)
            {
                if (!string.Equals(area.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null)
                {
                    message = "No work area chosen: more than one area is called \"" + wanted + "\". Rename one on the map.";
                    return false;
                }

                match = area;
            }
        }

        if (match == null)
        {
            message = "No work area chosen: there is no work area called \"" + wanted + "\" in this world.";
            return false;
        }

        choice = new WorkAreaChoice(match.Id, match.Name, worldLoadEpoch);
        message = "Thorstein's next collection order will use the work area \"" + match.Name +
            "\" from your map, exactly as drawn. If it cannot be read when the order starts, the order is refused; " +
            "it never falls back to the camp circle.";
        return true;
    }

    /// <summary>The next choice in a fixed cycle: no area (the usual harvest
    /// area or camp circle), then each listed area in the provider's order.
    /// </summary>
    public static bool TryCycle(
        WorkAreaLookup listed, WorkAreaChoice? current, Guid worldLoadEpoch, out WorkAreaChoice? next, out string message)
    {
        next = null;
        if (listed == null || !listed.IsOk)
        {
            message = "No work area chosen: " + (listed?.Reason ?? "Concerned Cartographer did not answer") + ".";
            return false;
        }

        IReadOnlyList<WorkAreaSummary> areas = listed.Areas;
        int at = -1;
        if (current != null && current.WorldLoadEpoch == worldLoadEpoch)
        {
            for (int index = 0; index < areas.Count; index++)
            {
                if (string.Equals(areas[index].Id, current.AreaId, StringComparison.Ordinal))
                {
                    at = index;
                    break;
                }
            }
        }

        int following = at + 1;
        if (following >= areas.Count)
        {
            message = areas.Count == 0
                ? "There are no work areas on your map in this world. Draw one with Concerned Cartographer's Areas button."
                : "No work area chosen: orders use the harvest area or the camp circle again.";
            return true;
        }

        WorkAreaSummary area = areas[following];
        next = new WorkAreaChoice(area.Id, area.Name, worldLoadEpoch);
        message = "Thorstein's next collection order will use the work area \"" + area.Name + "\" from your map.";
        return true;
    }

    /// <summary>A scope for the chosen area, asked for now. False with the
    /// player's reason; never another shape.</summary>
    public static bool TryBuild(
        WorkAreaChoice? choice,
        Func<string, WorkAreaLookup> describe,
        float groundY,
        Guid worldLoadEpoch,
        out WorkScope? scope,
        out string failure)
    {
        scope = null;
        if (describe == null)
        {
            throw new ArgumentNullException(nameof(describe));
        }

        if (choice == null || choice.WorldLoadEpoch != worldLoadEpoch || worldLoadEpoch == Guid.Empty)
        {
            failure = "no work area from your map is chosen in this session; choose it again (cf_collect area <name>)";
            return false;
        }

        WorkAreaLookup described;
        try
        {
            described = describe(choice.AreaId);
        }
        catch (Exception exception)
        {
            failure = "the work area \"" + choice.Name + "\" could not be read (" + exception.GetType().Name + ")";
            return false;
        }

        if (described == null || !described.IsOk || described.Area?.Boundary == null)
        {
            failure = "the work area \"" + choice.Name + "\" cannot be used: " +
                (described?.Reason ?? "Concerned Cartographer did not answer") +
                ". The order is refused rather than moved to another area";
            return false;
        }

        WorkAreaSummary area = described.Area;
        return WorkScope.TryForWorkArea(
            area.Id, area.Name, area.Revision, area.Boundary!, groundY, worldLoadEpoch, out scope, out failure);
    }

    /// <summary>What the provider says now about an accepted Cartographer
    /// scope. Anything that cannot be asked, or a scope that lost its
    /// boundary, is "not present".</summary>
    public static ScopeObservation Observe(WorkScope scope, Func<string, WorkAreaLookup>? describe, Guid currentEpoch)
    {
        if (scope == null || scope.IsMissingBoundary || describe == null)
        {
            return new ScopeObservation(false, 0, currentEpoch);
        }

        WorkAreaLookup described;
        try
        {
            described = describe(scope.AreaId);
        }
        catch (Exception)
        {
            return new ScopeObservation(false, 0, currentEpoch);
        }

        if (described == null || !described.IsOk || described.Area == null)
        {
            return new ScopeObservation(false, 0, currentEpoch);
        }

        return new ScopeObservation(true, ScopeRevision.ForWorkArea(described.Area.Id, described.Area.Revision), currentEpoch);
    }
}
