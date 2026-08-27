using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Route tools: interactive map modes (freehand draw, partial
/// erase, waypoint placement with optional road-aware snapping/routing)
/// plus the `cc_routes` console surface. Interactive input arrives from the
/// runtime as cursor world positions; everything lands in the tested
/// operations layer.</summary>
internal sealed class RouteCommandHandler
{
    public enum MapMode
    {
        None,
        Draw,
        Erase,
        Waypoint,
    }

    private readonly RouteStore _store;
    private readonly RouteOperations _operations;
    private readonly RoadAtlas _roads;
    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly Action _redraw;

    public RouteCommandHandler(
        RouteStore store,
        RouteOperations operations,
        RoadAtlas roads,
        CartographerSettings settings,
        ManualLogSource log,
        Action redraw)
    {
        _store = store;
        _operations = operations;
        _roads = roads;
        _settings = settings;
        _log = log;
        _redraw = redraw;
    }

    public MapMode Mode { get; private set; } = MapMode.None;
    public AtlasId ActiveRouteId { get; private set; }
    public bool SnapEnabled { get; private set; } = true;
    public RouteOperations Operations => _operations;

    /// <summary>Interactive input while the large map is open and a mode is
    /// active. Draw/erase act while held; waypoint acts on click.</summary>
    public void HandleMapFrame(RoadPoint cursorWorld, bool actionHeld, bool actionClicked)
    {
        switch (Mode)
        {
            case MapMode.Draw when actionHeld:
                if (_operations.AppendPoint(ActiveRouteId, cursorWorld))
                {
                    _redraw();
                }

                break;
            case MapMode.Erase when actionHeld:
                int removed = 0;
                foreach (AtlasRoute route in new List<AtlasRoute>(_store.Living))
                {
                    removed += _operations.EraseNear(route.Id, cursorWorld, _settings.RouteEraseRadius.Value, out _);
                }

                if (removed > 0)
                {
                    _redraw();
                }

                break;
            case MapMode.Waypoint when actionClicked:
                AddWaypoint(cursorWorld);
                break;
        }
    }

    private void AddWaypoint(RoadPoint cursorWorld)
    {
        RoadPoint target = cursorWorld;
        if (SnapEnabled &&
            _roads.TryGetNearestPointOnRoads(cursorWorld, _settings.RouteSnapRadius.Value, out RoadPoint snapped, out _))
        {
            target = snapped;
        }

        if (!_store.TryGet(ActiveRouteId, out AtlasRoute route))
        {
            return;
        }

        if (SnapEnabled && route.Points.Count > 0)
        {
            RoadPoint last = route.Points[route.Points.Count - 1];
            List<RoadPoint>? path = RoadGraphRouter.FindPath(_roads, last, target, _settings.RouteSnapRadius.Value);
            if (path is not null && path.Count > 2)
            {
                for (int index = 1; index < path.Count; index++)
                {
                    _operations.AppendPoint(ActiveRouteId, path[index]);
                }

                _redraw();
                return;
            }
        }

        _operations.AppendPoint(ActiveRouteId, target);
        _redraw();
    }

    public string Execute(string[] args, Vector3 playerPosition)
    {
        var player = new RoadPoint(playerPosition.x, playerPosition.y, playerPosition.z);
        string subcommand = args.Length == 0 ? "list" : args[0].ToLowerInvariant();
        string remainder = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";

        switch (subcommand)
        {
            case "list":
                return ListRoutes(player);
            case "draw":
            case "waypoint":
                AtlasRoute started = _operations.StartRoute(
                    subcommand == "draw" ? RouteKind.Freehand : RouteKind.Waypoint,
                    remainder.Trim().Length == 0 ? "New route" : remainder.Trim());
                ActiveRouteId = started.Id;
                Mode = subcommand == "draw" ? MapMode.Draw : MapMode.Waypoint;
                return subcommand == "draw"
                    ? $"Drawing \"{started.Name}\": open the large map and hold {_settings.RouteDrawModifier.Value}+LeftClick to draw. 'cc_routes stop' finishes."
                    : $"Waypoint route \"{started.Name}\": {_settings.RouteDrawModifier.Value}+LeftClick places waypoints (snap {(SnapEnabled ? "on" : "off")}). 'cc_routes stop' finishes.";
            case "erase":
                Mode = MapMode.Erase;
                return $"Erase mode: hold {_settings.RouteDrawModifier.Value}+LeftClick on the map to erase route ink " +
                    $"({_settings.RouteEraseRadius.Value:0.#} m radius). 'cc_routes stop' finishes.";
            case "stop":
                Mode = MapMode.None;
                _redraw();
                return "Route mode off.";
            case "snap":
                if (remainder.Trim().ToLowerInvariant() == "on")
                {
                    SnapEnabled = true;
                }
                else if (remainder.Trim().ToLowerInvariant() == "off")
                {
                    SnapEnabled = false;
                }
                else
                {
                    return "Usage: cc_routes snap on|off";
                }

                return $"Road-aware snapping {(SnapEnabled ? "on" : "off")}.";
            case "measure":
                return Measure(player);
            case "name":
                return EditNearest(player, route => route.Name = remainder.Trim(), $"renamed to \"{remainder.Trim()}\"");
            case "style":
                if (!Enum.TryParse(remainder.Trim(), ignoreCase: true, out RouteStyle style) ||
                    !Enum.IsDefined(typeof(RouteStyle), style))
                {
                    return "Usage: cc_routes style solid|dashed|dotted";
                }

                return EditNearest(player, route => route.Style = style, $"style {style}");
            case "status":
                if (!Enum.TryParse(remainder.Trim(), ignoreCase: true, out RouteStatus status) ||
                    !Enum.IsDefined(typeof(RouteStatus), status))
                {
                    return "Usage: cc_routes status planned|active|done";
                }

                return EditNearest(player, route => route.Status = status, $"status {status}");
            case "color":
                string trimmed = remainder.Trim().TrimStart('#');
                if (string.Equals(trimmed, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    return EditNearest(player, route => route.ColorArgb = null, "color cleared");
                }

                if ((trimmed.Length == 6 || trimmed.Length == 8) &&
                    uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                {
                    if (trimmed.Length == 6)
                    {
                        rgb |= 0xFF000000;
                    }

                    int argb = unchecked((int)rgb);
                    return EditNearest(player, route => route.ColorArgb = argb, $"color #{trimmed}");
                }

                return "Usage: cc_routes color RRGGBB|AARRGGBB|clear";
            case "lock":
                return LockNearest(player, locked: true);
            case "unlock":
                return LockNearest(player, locked: false);
            case "archive":
                return ArchiveNearest(player, archived: true);
            case "unarchive":
                return ArchiveNearest(player, archived: false);
            case "delete":
                if (!TryFindNearest(player, out AtlasRoute? doomed, out _))
                {
                    return NoNearbyRoute();
                }

                Mode = Mode == MapMode.None ? Mode : MapMode.None;
                _operations.Delete(doomed!.Id);
                _redraw();
                return $"Deleted route \"{doomed.Name}\". 'cc_routes undo' or 'cc_routes restore' reverts.";
            case "restore":
                foreach (AtlasRoute route in _store.All)
                {
                    if (route.Deleted)
                    {
                        _operations.RestoreDeleted(route.Id);
                        _redraw();
                        return $"Restored route \"{route.Name}\".";
                    }
                }

                return "No deleted route to restore.";
            case "split":
                return SplitNearest(player);
            case "merge":
                return MergeNearest(player);
            case "undo":
                bool undone = _operations.Undo(out string undoSummary);
                if (undone)
                {
                    _redraw();
                }

                return undoSummary;
            case "redo":
                bool redone = _operations.Redo(out string redoSummary);
                if (redone)
                {
                    _redraw();
                }

                return redoSummary;
            default:
                return "Usage: cc_routes [list|draw <name>|waypoint <name>|erase|stop|snap on/off|measure|name|style|status|color|lock|unlock|archive|unarchive|delete|restore|split|merge|undo|redo]";
        }
    }

    private string ListRoutes(RoadPoint player)
    {
        var builder = new StringBuilder();
        int count = 0;
        foreach (AtlasRoute route in _store.Living)
        {
            count++;
            if (count <= 12)
            {
                RouteEstimator.Estimate estimate = RouteEstimator.Compute(
                    route.Points, _roads,
                    _settings.RouteOnRoadTolerance.Value,
                    _settings.RouteOffRoadSpeed.Value,
                    _settings.RouteOnRoadSpeed.Value);
                builder.Append($"\n  \"{route.Name}\" [{route.Kind}, {route.Style}, {route.Status}" +
                    $"{(route.Locked ? ", locked" : "")}{(route.Archived ? ", archived" : "")}] " +
                    $"{route.Points.Count} pts, {estimate.DistanceMeters:0} m");
            }
        }

        string mode = Mode == MapMode.None ? "" : $" Mode: {Mode} (cc_routes stop ends it).";
        return count == 0
            ? "No routes yet. 'cc_routes draw <name>' or 'cc_routes waypoint <name>' starts one." + mode
            : $"{count} route(s):{builder}{mode}";
    }

    private string Measure(RoadPoint player)
    {
        if (!TryFindNearest(player, out AtlasRoute? route, out _))
        {
            return NoNearbyRoute();
        }

        RouteEstimator.Estimate estimate = RouteEstimator.Compute(
            route!.Points, _roads,
            _settings.RouteOnRoadTolerance.Value,
            _settings.RouteOffRoadSpeed.Value,
            _settings.RouteOnRoadSpeed.Value);
        return $"\"{route.Name}\": {estimate.DistanceMeters:0} m, {estimate.OnRoadFraction:P0} on roads, " +
            $"≈{estimate.EstimatedMinutes:0.#} min at {_settings.RouteOffRoadSpeed.Value:0.#}/{_settings.RouteOnRoadSpeed.Value:0.#} m/s.";
    }

    private string EditNearest(RoadPoint player, Action<AtlasRoute> edit, string description)
    {
        if (!TryFindNearest(player, out AtlasRoute? route, out _))
        {
            return NoNearbyRoute();
        }

        _operations.EditMetadata(route!.Id, edit, description);
        _redraw();
        return $"Route \"{route.Name}\": {description}.";
    }

    private string LockNearest(RoadPoint player, bool locked)
    {
        if (!TryFindNearest(player, out AtlasRoute? route, out _))
        {
            return NoNearbyRoute();
        }

        _operations.SetLocked(route!.Id, locked);
        return $"Route \"{route.Name}\" {(locked ? "locked (geometry edits rejected)" : "unlocked")}.";
    }

    private string ArchiveNearest(RoadPoint player, bool archived)
    {
        AtlasRoute? target = null;
        float best = 100f;
        foreach (AtlasRoute route in _store.Living)
        {
            if (route.Archived == archived)
            {
                continue;
            }

            float distance = DistanceToRoute(route, player);
            if (distance <= best)
            {
                best = distance;
                target = route;
            }
        }

        if (target is null)
        {
            return archived ? NoNearbyRoute() : "No archived route within 100 m.";
        }

        _operations.SetArchived(target.Id, archived);
        _redraw();
        return $"Route \"{target.Name}\" {(archived ? "archived (hidden from the map)" : "unarchived")}.";
    }

    private string SplitNearest(RoadPoint player)
    {
        if (!TryFindNearest(player, out AtlasRoute? route, out _))
        {
            return NoNearbyRoute();
        }

        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int index = 1; index < route!.Points.Count - 1; index++)
        {
            float distance = route.Points[index].HorizontalDistanceTo(player);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        if (nearestIndex < 0)
        {
            return "That route is too short to split.";
        }

        AtlasRoute? tail = _operations.Split(route.Id, nearestIndex);
        if (tail is null)
        {
            return route.Locked ? "The route is locked." : "Split failed.";
        }

        _redraw();
        return $"Split \"{route.Name}\" at the point nearest you.";
    }

    private string MergeNearest(RoadPoint player)
    {
        AtlasRoute? first = null;
        AtlasRoute? second = null;
        float bestFirst = float.MaxValue;
        float bestSecond = float.MaxValue;
        foreach (AtlasRoute route in _store.Living)
        {
            float distance = DistanceToRoute(route, player);
            if (distance < bestFirst)
            {
                second = first;
                bestSecond = bestFirst;
                first = route;
                bestFirst = distance;
            }
            else if (distance < bestSecond)
            {
                second = route;
                bestSecond = distance;
            }
        }

        if (first is null || second is null || bestSecond > 100f)
        {
            return "Stand near the two routes to merge (both within 100 m).";
        }

        if (!_operations.Merge(first.Id, second.Id))
        {
            return "Merge failed (locked or empty route).";
        }

        _redraw();
        return $"Merged \"{second.Name}\" into \"{first.Name}\". 'cc_routes undo' reverts.";
    }

    private bool TryFindNearest(RoadPoint player, out AtlasRoute? route, out float distance)
    {
        route = null;
        distance = float.MaxValue;
        if (Mode != MapMode.None && _store.TryGet(ActiveRouteId, out AtlasRoute active) && !active.Deleted)
        {
            route = active;
            distance = 0f;
            return true;
        }

        foreach (AtlasRoute candidate in _store.Living)
        {
            float d = DistanceToRoute(candidate, player);
            if (d < distance)
            {
                distance = d;
                route = candidate;
            }
        }

        return route is not null && distance <= 100f;
    }

    private static float DistanceToRoute(AtlasRoute route, RoadPoint position)
    {
        float best = float.MaxValue;
        for (int index = 0; index < Math.Max(1, route.Points.Count - 1); index++)
        {
            if (route.Points.Count == 0)
            {
                break;
            }

            RoadPoint start = route.Points[index];
            RoadPoint end = index + 1 < route.Points.Count ? route.Points[index + 1] : start;
            best = Math.Min(best, RoadGeometry.HorizontalDistanceToSegment(position, start, end));
        }

        return best;
    }

    private static string NoNearbyRoute()
    {
        return "No route within 100 m. 'cc_routes list' shows what exists.";
    }
}
