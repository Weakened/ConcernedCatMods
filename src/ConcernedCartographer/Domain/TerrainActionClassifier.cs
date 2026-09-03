using System;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>ROAD SOURCE AUTHORITY, identity edition (RC10, DEF-v1.0-007).
///
/// Classifies a captured terrain operation by the ACTUAL local-player
/// action identity: the placed TerrainOp's prefab name (the instantiated
/// piece, e.g. "path_v2(Clone)"), the piece's localization token
/// ($piece_pathen…) as fallback, and the player's currently selected build
/// piece as corroboration. Settings flags are deliberately NOT inputs:
/// the game gives "Level ground" (mud_road_v2) and "Pathen" (path_v2)
/// near-identical smooth-and-paint-Dirt settings, so m_level/m_raise/
/// m_smooth/PaintType can never distinguish them — identity can.
///
/// Authority rules:
///  1. ONLY Pathen may create Dirt road data; ONLY Paved road may create
///     Paved road data. Paint must agree with identity (both halves
///     required); PaintType.Dirt alone is NEVER authority.
///  2. Level, Raise, Cultivate, Reset paint, digging, and every unknown
///     operation create zero road data; when they clear paint they erase
///     covered recorded ink of both kinds instead.
///  3. When the selected-piece identity is available and disagrees with
///     the operation, road creation is refused (fail closed).</summary>
internal static class TerrainActionClassifier
{
    public static TerrainActionClassification Classify(
        string? operationObjectName,
        string? pieceNameToken,
        string? selectedPieceName,
        bool paintCleared,
        TerrainPaintKind paint)
    {
        string operationName = NormalizePrefabName(operationObjectName);
        TerrainActionCategory category = CategoryOfPrefab(operationName);
        if (category == TerrainActionCategory.Unknown)
        {
            category = CategoryOfPieceToken(pieceNameToken);
        }

        RoadKind? roadKind = null;
        if (paintCleared)
        {
            if (category == TerrainActionCategory.Pathen && paint == TerrainPaintKind.Dirt)
            {
                roadKind = Roads.RoadKind.Dirt;
            }
            else if (category == TerrainActionCategory.PavedRoad && paint == TerrainPaintKind.Paved)
            {
                roadKind = Roads.RoadKind.Paved;
            }
        }

        // Corroboration: a real hoe placement runs synchronously inside
        // Player.PlacePiece, so the selected piece IS the placed piece. A
        // road-authorized op whose selection disagrees was not the local
        // player's deliberate road action — refuse it. Absent selection
        // (no local player, mod-driven placement) skips the check.
        bool selectionMismatch = false;
        if (roadKind is not null && !string.IsNullOrEmpty(selectedPieceName))
        {
            string selectionName = NormalizePrefabName(selectedPieceName);
            TerrainActionCategory selectionCategory = CategoryOfPrefab(selectionName);
            if (selectionCategory != category)
            {
                selectionMismatch = true;
                roadKind = null;
            }
        }

        bool erasesRoads = paintCleared && roadKind is null;
        string description = Describe(
            category, operationName, paint, roadKind, erasesRoads, selectionMismatch, selectedPieceName);
        return new TerrainActionClassification(category, roadKind, erasesRoads, selectionMismatch, description);
    }

    /// <summary>Strips Unity's "(Clone)" instantiation suffixes and
    /// whitespace, lower-cases invariantly.</summary>
    public static string NormalizePrefabName(string? objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return string.Empty;
        }

        string name = objectName!.Trim();
        while (name.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
        }

        return name.ToLowerInvariant();
    }

    private static TerrainActionCategory CategoryOfPrefab(string normalizedName)
    {
        switch (normalizedName)
        {
            // Verified in-game (RC10): path_v2 is the hoe's "Pathen".
            case "path_v2":
            case "path":
                return TerrainActionCategory.Pathen;
            case "paved_road_v2":
            case "paved_road":
                return TerrainActionCategory.PavedRoad;

            // Verified in-game (RC10): mud_road_v2 is the hoe's "Level
            // ground" — the name is a historical misnomer, NOT a road.
            case "mud_road_v2":
            case "mud_road":
                return TerrainActionCategory.LevelGround;
            case "raise_v2":
            case "raise":
                return TerrainActionCategory.RaiseGround;
            case "cultivate_v2":
            case "cultivate":
                return TerrainActionCategory.Cultivate;
            case "replant_v2":
            case "replant":
                return TerrainActionCategory.Replant;
            case "digg_v2":
            case "digg":
                return TerrainActionCategory.Digging;
            default:
                return TerrainActionCategory.Unknown;
        }
    }

    private static TerrainActionCategory CategoryOfPieceToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return TerrainActionCategory.Unknown;
        }

        switch (token!.Trim().TrimStart('$').ToLowerInvariant())
        {
            case "piece_pathen":
                return TerrainActionCategory.Pathen;
            case "piece_pavedroad":
                return TerrainActionCategory.PavedRoad;
            case "piece_level":
                return TerrainActionCategory.LevelGround;
            case "piece_raise":
                return TerrainActionCategory.RaiseGround;
            case "piece_cultivate":
                return TerrainActionCategory.Cultivate;
            case "piece_replant":
                return TerrainActionCategory.Replant;
            default:
                return TerrainActionCategory.Unknown;
        }
    }

    private static string Describe(
        TerrainActionCategory category,
        string operationName,
        TerrainPaintKind paint,
        RoadKind? roadKind,
        bool erasesRoads,
        bool selectionMismatch,
        string? selectedPieceName)
    {
        string identity = operationName.Length == 0 ? "<unnamed op>" : operationName;
        string verdict = roadKind is RoadKind kind
            ? $"{kind} road"
            : erasesRoads ? "no road (erases covered ink)" : "no road";
        string mismatch = selectionMismatch
            ? $" [REFUSED: selected piece '{NormalizePrefabName(selectedPieceName)}' disagrees]"
            : string.Empty;
        return $"{CategoryLabel(category)} ({identity}) paint={paint} => {verdict}{mismatch}";
    }

    private static string CategoryLabel(TerrainActionCategory category)
    {
        switch (category)
        {
            case TerrainActionCategory.Pathen: return "pathen";
            case TerrainActionCategory.PavedRoad: return "paved-road";
            case TerrainActionCategory.LevelGround: return "level-ground";
            case TerrainActionCategory.RaiseGround: return "raise-ground";
            case TerrainActionCategory.Cultivate: return "cultivate";
            case TerrainActionCategory.Replant: return "replant";
            case TerrainActionCategory.Digging: return "digging";
            default: return "unknown-action";
        }
    }
}
