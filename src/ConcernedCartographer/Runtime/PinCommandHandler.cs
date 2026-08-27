using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Map;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Executes `cc_pins` subcommands against one world's pin store.
/// Proximity-selection mirrors the road tools: operations target the
/// managed pin nearest the player unless stated otherwise.</summary>
internal sealed class PinCommandHandler
{
    private const float DefaultSelectRadiusMeters = 15f;
    private const float DefaultDuplicateRadiusMeters = 25f;

    private readonly PinStore _store;
    private readonly PinOperations _operations;
    private readonly PinAdapter _adapter;
    private readonly ManualLogSource _log;
    private readonly Action _resyncMap;

    public PinCommandHandler(PinStore store, PinOperations operations, PinAdapter adapter, ManualLogSource log, Action resyncMap)
    {
        _store = store;
        _operations = operations;
        _adapter = adapter;
        _log = log;
        _resyncMap = resyncMap;
    }

    public PinOperations Operations => _operations;

    public string Execute(string[] args, Vector3 playerPosition)
    {
        var position = new RoadPoint(playerPosition.x, playerPosition.y, playerPosition.z);
        string subcommand = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        string remainder = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";

        switch (subcommand)
        {
            case "status":
                return Status(position);
            case "list":
                return ListPins(position, remainder);
            case "adopt":
                return Adopt(playerPosition, remainder);
            case "adoptall":
                return AdoptAll(remainder);
            case "create":
                return Create(position, remainder);
            case "name":
                return EditNearest(position, $"rename to \"{remainder}\"", pin => pin.Name = remainder);
            case "icon":
                return SetIcon(position, remainder);
            case "icons":
                return ListIcons(remainder);
            case "category":
                return EditNearest(position, $"category \"{remainder}\"", pin => pin.Category = remainder);
            case "color":
                return SetColor(position, remainder);
            case "size":
                return SetSize(position, remainder);
            case "note":
                return EditNearest(position, "note", pin => pin.Notes = remainder);
            case "tag+":
                return EditNearest(position, $"tag +{remainder}", pin =>
                {
                    string tag = remainder.Trim();
                    if (tag.Length > 0 && !pin.Tags.Contains(tag))
                    {
                        pin.Tags.Add(tag);
                    }
                });
            case "tag-":
                return EditNearest(position, $"tag -{remainder}", pin => pin.Tags.Remove(remainder.Trim()));
            case "setstatus":
                return SetStatus(position, remainder);
            case "check":
                return EditNearest(position, "check", pin => pin.Checked = true);
            case "uncheck":
                return EditNearest(position, "uncheck", pin => pin.Checked = false);
            case "scope":
                return SetScope(position, remainder);
            case "move":
                return Move(position);
            case "dup":
                return Duplicate(position);
            case "archive":
                return Archive(position, archived: true);
            case "unarchive":
                return Archive(position, archived: false);
            case "delete":
                return Delete(position);
            case "restore":
                return Restore();
            case "deleted":
                return ListDeleted();
            case "dups":
                return ListDuplicates(remainder);
            case "merge":
                return Merge(position, remainder);
            case "undo":
                return UndoRedo(undo: true);
            case "redo":
                return UndoRedo(undo: false);
            case "coords":
                return Coordinates(position);
            default:
                return "Usage: cc_pins [" + string.Join("|", "status", "list", "adopt", "adoptall", "create",
                    "name", "icon", "icons", "category", "color", "size", "note", "tag+", "tag-", "setstatus",
                    "check", "uncheck", "scope", "move", "dup", "archive", "unarchive", "delete", "restore",
                    "deleted", "dups", "merge", "undo", "redo", "coords") + "] ...";
        }
    }

    private string Status(RoadPoint position)
    {
        int living = 0;
        int archived = 0;
        int tombstones = 0;
        foreach (AtlasPin pin in _store.All)
        {
            if (pin.Deleted)
            {
                tombstones++;
            }
            else if (pin.Archived)
            {
                archived++;
            }
            else
            {
                living++;
            }
        }

        int adoptable = _adapter.ListAdoptable().Count;
        string nearest = TryFindNearest(position, DefaultSelectRadiusMeters, includeArchived: true, out AtlasPin? nearestPin, out float distance)
            ? $"Nearest: {Describe(nearestPin!)}, {distance:0.#} m away."
            : "No managed pin nearby.";
        return $"Pins: {living} active, {archived} archived, {tombstones} deleted, {adoptable} adoptable vanilla. " +
            $"Undo {_operations.UndoCount}/redo {_operations.RedoCount}. {nearest}";
    }

    private string ListPins(RoadPoint position, string filter)
    {
        var matches = new List<(float Distance, AtlasPin Pin)>();
        string needle = filter.Trim().ToLowerInvariant();
        foreach (AtlasPin pin in _store.Living)
        {
            if (pin.Archived || !MatchesFilter(pin, needle))
            {
                continue;
            }

            matches.Add((pin.Position.HorizontalDistanceTo(position), pin));
        }

        matches.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var builder = new StringBuilder();
        builder.Append($"{matches.Count} pin(s)");
        int shown = Math.Min(matches.Count, 15);
        for (int index = 0; index < shown; index++)
        {
            builder.Append($"\n  {matches[index].Distance,7:0.#} m  {Describe(matches[index].Pin)}");
        }

        if (matches.Count > shown)
        {
            builder.Append($"\n  ... and {matches.Count - shown} more (refine the filter).");
        }

        return builder.ToString();
    }

    private string Adopt(Vector3 playerPosition, string radiusText)
    {
        float radius = ParseRadius(radiusText, DefaultSelectRadiusMeters);
        List<Minimap.PinData> adoptable = _adapter.ListAdoptable();
        Minimap.PinData? best = null;
        float bestDistance = radius;
        foreach (Minimap.PinData candidate in adoptable)
        {
            float distance = Vector2.Distance(
                new Vector2(candidate.m_pos.x, candidate.m_pos.z),
                new Vector2(playerPosition.x, playerPosition.z));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best is null)
        {
            return $"No adoptable vanilla pin within {radius:0.#} m (foreign and system pins are never adopted).";
        }

        AtlasPin? adopted = _adapter.Adopt(_store, best);
        if (adopted is null)
        {
            return "Adoption failed; see the log.";
        }

        _log.LogInfo($"Adopted vanilla pin as {adopted.Id}.");
        return $"Adopted \"{adopted.Name}\" ({adopted.IconId}) as a managed pin. Its position, icon, and checked state are unchanged.";
    }

    private string AdoptAll(string confirm)
    {
        List<Minimap.PinData> adoptable = _adapter.ListAdoptable();
        if (adoptable.Count == 0)
        {
            return "No adoptable vanilla pins.";
        }

        if (!string.Equals(confirm.Trim(), "confirm", StringComparison.OrdinalIgnoreCase))
        {
            return $"Would adopt {adoptable.Count} vanilla pin(s), preserving position/icon/name/checked state. " +
                "Run 'cc_pins adoptall confirm' to proceed.";
        }

        int adopted = 0;
        foreach (Minimap.PinData pin in adoptable)
        {
            if (_adapter.Adopt(_store, pin) is not null)
            {
                adopted++;
            }
        }

        _log.LogInfo($"Batch-adopted {adopted} vanilla pin(s).");
        return $"Adopted {adopted} vanilla pin(s).";
    }

    private string Create(RoadPoint position, string name)
    {
        AtlasPin pin = _store.Create(created =>
        {
            created.Name = name.Trim();
            created.Position = position;
        });
        _adapter.SyncPin(_store, pin.Id);
        return $"Created {Describe(pin)} at your position.";
    }

    private string EditNearest(RoadPoint position, string description, Action<AtlasPin> edit)
    {
        if (!TryFindNearest(position, DefaultSelectRadiusMeters, includeArchived: true, out AtlasPin? pin, out _))
        {
            return NoNearbyPin();
        }

        _operations.BatchEdit(new[] { pin!.Id }, edit, description);
        _adapter.SyncPin(_store, pin.Id);
        return $"Updated {Describe(pin)} ({description}).";
    }

    private string SetIcon(RoadPoint position, string iconId)
    {
        string wanted = iconId.Trim();
        if (!IconRegistry.TryResolve(wanted, out IconRegistry.IconDefinition definition))
        {
            return $"Unknown icon '{wanted}'. Try 'cc_pins icons {wanted}' to search the registry.";
        }

        return EditNearest(position, $"icon {definition.Id}", pin => pin.IconId = definition.Id);
    }

    private string ListIcons(string query)
    {
        var builder = new StringBuilder("Icons:");
        foreach (IconRegistry.IconDefinition definition in IconRegistry.Search(query))
        {
            builder.Append($"\n  {definition.Id}  ({definition.DisplayName}, {definition.DefaultCategory})");
        }

        return builder.ToString();
    }

    private string SetColor(RoadPoint position, string colorText)
    {
        string trimmed = colorText.Trim().TrimStart('#');
        if (string.Equals(trimmed, "clear", StringComparison.OrdinalIgnoreCase))
        {
            return EditNearest(position, "color cleared", pin => pin.ColorArgb = null);
        }

        if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb) ||
            (trimmed.Length != 6 && trimmed.Length != 8))
        {
            return "Usage: cc_pins color RRGGBB | AARRGGBB | clear";
        }

        if (trimmed.Length == 6)
        {
            rgb |= 0xFF000000;
        }

        int argb = unchecked((int)rgb);
        return EditNearest(position, $"color #{trimmed}", pin => pin.ColorArgb = argb);
    }

    private string SetSize(RoadPoint position, string sizeText)
    {
        if (!float.TryParse(sizeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
        {
            return "Usage: cc_pins size <0.5..2.0>";
        }

        float clamped = Mathf.Clamp(size, 0.5f, 2f);
        return EditNearest(position, $"size {clamped:0.##}", pin => pin.SizeScale = clamped);
    }

    private string SetStatus(RoadPoint position, string statusText)
    {
        if (!Enum.TryParse(statusText.Trim(), ignoreCase: true, out AtlasPinStatus status) ||
            !Enum.IsDefined(typeof(AtlasPinStatus), status))
        {
            return "Usage: cc_pins setstatus none|todo|inprogress|done|warning";
        }

        return EditNearest(position, $"status {status}", pin => pin.Status = status);
    }

    private string SetScope(RoadPoint position, string scopeText)
    {
        if (!Enum.TryParse(scopeText.Trim(), ignoreCase: true, out AtlasScope scope) ||
            !Enum.IsDefined(typeof(AtlasScope), scope))
        {
            return "Usage: cc_pins scope private|table|server (sharing intent; sync arrives in v0.6)";
        }

        return EditNearest(position, $"scope {scope}", pin => pin.Scope = scope);
    }

    private string Move(RoadPoint position)
    {
        if (!TryFindNearest(position, 100f, includeArchived: true, out AtlasPin? pin, out _))
        {
            return "No managed pin within 100 m.";
        }

        _operations.Move(pin!.Id, position);
        _adapter.SyncPin(_store, pin.Id);
        return $"Moved {Describe(pin)} to your position. 'cc_pins undo' reverts.";
    }

    private string Duplicate(RoadPoint position)
    {
        if (!TryFindNearest(position, DefaultSelectRadiusMeters, includeArchived: false, out AtlasPin? pin, out _))
        {
            return NoNearbyPin();
        }

        AtlasPin? copy = _operations.Duplicate(pin!.Id);
        if (copy is null)
        {
            return "Duplicate failed.";
        }

        _adapter.SyncPin(_store, copy.Id);
        return $"Duplicated as {Describe(copy)} (offset 4 m east).";
    }

    private string Archive(RoadPoint position, bool archived)
    {
        AtlasPin? target = null;
        float best = DefaultSelectRadiusMeters;
        foreach (AtlasPin pin in _store.Living)
        {
            if (pin.Archived == archived)
            {
                continue;
            }

            float distance = pin.Position.HorizontalDistanceTo(position);
            if (distance <= best)
            {
                best = distance;
                target = pin;
            }
        }

        if (target is null)
        {
            return archived ? NoNearbyPin() : $"No archived pin within {DefaultSelectRadiusMeters:0.#} m.";
        }

        _operations.SetArchived(target.Id, archived);
        _adapter.SyncPin(_store, target.Id);
        return $"{(archived ? "Archived" : "Unarchived")} {Describe(target)}.";
    }

    private string Delete(RoadPoint position)
    {
        if (!TryFindNearest(position, DefaultSelectRadiusMeters, includeArchived: true, out AtlasPin? pin, out _))
        {
            return NoNearbyPin();
        }

        _operations.Delete(pin!.Id);
        _adapter.SyncPin(_store, pin.Id);
        return $"Deleted {Describe(pin)}. 'cc_pins restore' or 'cc_pins undo' brings it back.";
    }

    private string Restore()
    {
        List<AtlasPin> deleted = _operations.RecentlyDeleted(1);
        if (deleted.Count == 0)
        {
            return "Nothing in the recently-deleted list.";
        }

        _operations.RestoreDeleted(deleted[0].Id);
        _adapter.SyncPin(_store, deleted[0].Id);
        return $"Restored {Describe(deleted[0])}.";
    }

    private string ListDeleted()
    {
        var builder = new StringBuilder("Recently deleted:");
        List<AtlasPin> deleted = _operations.RecentlyDeleted();
        if (deleted.Count == 0)
        {
            return "Recently deleted: none.";
        }

        foreach (AtlasPin pin in deleted)
        {
            builder.Append($"\n  {Describe(pin)} (deleted {pin.DeletedUtc:HH:mm} UTC)");
        }

        return builder.ToString();
    }

    private string ListDuplicates(string radiusText)
    {
        float radius = ParseRadius(radiusText, DefaultDuplicateRadiusMeters);
        List<List<AtlasPin>> groups = _operations.FindDuplicateGroups(radius);
        if (groups.Count == 0)
        {
            return $"No likely duplicates within {radius:0.#} m of each other.";
        }

        var builder = new StringBuilder($"{groups.Count} duplicate group(s):");
        foreach (List<AtlasPin> group in groups)
        {
            builder.Append($"\n  keep {Describe(group[0])} <- merge {group.Count - 1} other(s)");
        }

        builder.Append("\nStand near a group and run 'cc_pins merge confirm'.");
        return builder.ToString();
    }

    private string Merge(RoadPoint position, string confirm)
    {
        List<List<AtlasPin>> groups = _operations.FindDuplicateGroups(DefaultDuplicateRadiusMeters);
        List<AtlasPin>? nearest = null;
        float best = float.MaxValue;
        foreach (List<AtlasPin> group in groups)
        {
            float distance = group[0].Position.HorizontalDistanceTo(position);
            if (distance < best)
            {
                best = distance;
                nearest = group;
            }
        }

        if (nearest is null)
        {
            return "No duplicate group found.";
        }

        if (!string.Equals(confirm.Trim(), "confirm", StringComparison.OrdinalIgnoreCase))
        {
            return $"Would merge {nearest.Count - 1} pin(s) into {Describe(nearest[0])}, preserving notes and provenance. " +
                "Run 'cc_pins merge confirm' to proceed.";
        }

        var duplicateIds = new List<AtlasId>();
        for (int index = 1; index < nearest.Count; index++)
        {
            duplicateIds.Add(nearest[index].Id);
        }

        _operations.Merge(nearest[0].Id, duplicateIds);
        _resyncMap();
        return $"Merged {duplicateIds.Count} duplicate(s) into {Describe(nearest[0])}. 'cc_pins undo' reverts.";
    }

    private string UndoRedo(bool undo)
    {
        bool changed = undo ? _operations.Undo(out string summary) : _operations.Redo(out summary);
        if (changed)
        {
            _resyncMap();
        }

        return summary;
    }

    private string Coordinates(RoadPoint position)
    {
        if (!TryFindNearest(position, DefaultSelectRadiusMeters, includeArchived: true, out AtlasPin? pin, out _))
        {
            return NoNearbyPin();
        }

        string coordinates = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.#}, {1:0.#}, {2:0.#}",
            pin!.Position.X, pin.Position.Y, pin.Position.Z);
        try
        {
            GUIUtility.systemCopyBuffer = coordinates;
        }
        catch
        {
            // Clipboard access is cosmetic.
        }

        return $"{Describe(pin)} at ({coordinates}) — copied to clipboard.";
    }

    private bool TryFindNearest(RoadPoint position, float maxRadius, bool includeArchived, out AtlasPin? pin, out float distance)
    {
        pin = null;
        distance = float.MaxValue;
        foreach (AtlasPin candidate in _store.Living)
        {
            if (candidate.Archived && !includeArchived)
            {
                continue;
            }

            float d = candidate.Position.HorizontalDistanceTo(position);
            if (d < distance)
            {
                distance = d;
                pin = candidate;
            }
        }

        return pin is not null && distance <= maxRadius;
    }

    private static bool MatchesFilter(AtlasPin pin, string needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        if (pin.Name.ToLowerInvariant().Contains(needle) ||
            pin.Category.ToLowerInvariant().Contains(needle) ||
            pin.IconId.ToLowerInvariant().Contains(needle))
        {
            return true;
        }

        foreach (string tag in pin.Tags)
        {
            if (tag.ToLowerInvariant().Contains(needle))
            {
                return true;
            }
        }

        return false;
    }

    private static float ParseRadius(string text, float fallback)
    {
        return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius)
            ? Mathf.Clamp(radius, 1f, 200f)
            : fallback;
    }

    private static string NoNearbyPin()
    {
        return $"No managed pin within {DefaultSelectRadiusMeters:0.#} m. 'cc_pins list' shows what exists; 'cc_pins adopt' adopts a vanilla pin.";
    }

    private static string Describe(AtlasPin pin)
    {
        string name = pin.Name.Length == 0 ? "(unnamed)" : $"\"{pin.Name}\"";
        string extras = "";
        if (pin.Archived)
        {
            extras += ", archived";
        }

        if (pin.Deleted)
        {
            extras += ", deleted";
        }

        return $"{name} [{pin.IconId}, {pin.Source}{extras}]";
    }
}
