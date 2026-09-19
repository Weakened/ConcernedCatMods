using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>One piece of a blueprint, in the blueprint's own frame.
///
/// <b>Offsets are local, and that is the point.</b> A build order is a place
/// and a facing the player chose, and the blueprint is the same shape wherever
/// it is put. Keeping the offsets local means the order carries two numbers and
/// the shape carries none, so a marker dragged three metres does not rewrite a
/// blueprint.
///
/// <b>The prefab name is a real vanilla name and nothing else.</b> This product
/// never invents a piece, never substitutes one, and never spawns a replacement
/// for one it could not find: a name that does not resolve is a refusal that
/// says which name, because a shelter built out of something else is not the
/// shelter the player agreed to.</summary>
internal readonly struct BlueprintPiece
{
    internal BlueprintPiece(
        int index, string prefab, BuildPhase phase, float east, float up, float north, float yaw)
    {
        Index = index;
        Prefab = prefab ?? string.Empty;
        Phase = phase;
        East = east;
        Up = up;
        North = north;
        Yaw = yaw;
    }

    /// <summary>Where this piece sits in the blueprint's build order. Stable for
    /// the life of the blueprint and used as the piece's name inside an order.
    /// <b>Never written to disk</b> - progress is re-read from the world, so
    /// renumbering the blueprint costs nothing and can never orphan a save.
    /// </summary>
    internal int Index { get; }

    /// <summary>The vanilla prefab to place.</summary>
    internal string Prefab { get; }

    /// <summary>Which part of the shelter it belongs to.</summary>
    internal BuildPhase Phase { get; }

    /// <summary>Metres to the marker's right.</summary>
    internal float East { get; }

    /// <summary>Metres above the marker.</summary>
    internal float Up { get; }

    /// <summary>Metres along the marker's facing.</summary>
    internal float North { get; }

    /// <summary>Degrees clockwise from the marker's facing.</summary>
    internal float Yaw { get; }

    /// <summary>A piece that could be placed: it names something and belongs to
    /// a phase. <b>Safe on a default struct</b>, which is the one value a caller
    /// never means to hand over and the one a bug always does: the auto-property
    /// is null there rather than empty, and a guard that threw instead of
    /// answering would turn a fail-closed check into a crash.</summary>
    internal bool IsValid =>
        !string.IsNullOrEmpty(Prefab) && Phase != BuildPhase.Unspecified;

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "#{0} {1} ({2})", Index, Prefab, Phase);
}

/// <summary>A blueprint piece resolved against one marker: where it actually
/// goes, and which way round.</summary>
internal readonly struct PiecePlacement
{
    internal PiecePlacement(BlueprintPiece piece, SitePoint at, float yaw)
    {
        Piece = piece;
        At = at;
        Yaw = yaw;
    }

    /// <summary>Which blueprint piece this is.</summary>
    internal BlueprintPiece Piece { get; }

    /// <summary>Where it goes, in world space.</summary>
    internal SitePoint At { get; }

    /// <summary>Which way it faces, in world degrees.</summary>
    internal float Yaw { get; }

    /// <summary>The name this placement is known by inside one order. It is the
    /// blueprint index, not the position, because a target that <i>moved</i> is
    /// still that target and a name made of coordinates would quietly become a
    /// second one.</summary>
    internal string Key => Piece.Index.ToString(CultureInfo.InvariantCulture);

    internal bool IsValid => Piece.IsValid;

    public override string ToString() => Piece + " at " + At;
}

/// <summary>The one build order this leaf offers: a small deterministic vanilla
/// cottage.
///
/// <b>Seventeen pieces, always the same seventeen.</b> Four floor panels, seven
/// walls, one door, four roof panels and a bed. Nothing here is generated, nothing
/// is chosen at run time, and the same marker always produces the same shelter -
/// which is what makes a build order something a player can agree to in advance.
///
/// <b>The measurements, and where they come from.</b> Vanilla's wood building
/// set is on a two-metre grid: <c>wood_floor</c> and <c>wood_wall</c> are 2x2 m
/// panels. The doorway is one wall slot wide because <c>wood_door</c>'s snap
/// points sit at +/-1 m either side and 1 m up - the measurement this repository
/// already records in <c>src/Shared/Companions/Surroundings/DoorPortal.cs</c> and
/// <c>CompanionDoors.cs</c> for a different question about the same piece. So a
/// door exactly replaces one wall panel, and the footprint is 4x4 m with walls
/// 2 m tall.
///
/// <b>What only a play test can settle, said here rather than discovered.</b>
/// Two things. First, the marker is defined as the centre of the floor's top
/// surface, and whether a vanilla piece's own origin sits at its centre or at
/// its base is an engine fact no test in this repository can check - if the
/// offsets are half a panel out, every piece is refused by the placement gate
/// with a named reason rather than placed wrongly, which is the failure mode
/// this design chooses. Second, snapping: the runtime places at a computed
/// point and does not use vanilla's snap-point search, so pieces meet where the
/// arithmetic says they meet.
///
/// <b>Why the roof is flat panels rather than pitched roof pieces.</b> A pitched
/// <c>wood_roof</c> needs a ridge piece and a pitch-matched offset per panel,
/// and neither can be computed without the prefab in front of you - it would be
/// invented geometry wearing the name of a measurement. A flat deck of
/// <c>wood_floor</c> one storey up is real vanilla building, uses a piece
/// already in the manifest, and genuinely provides roof cover, which is the
/// property that makes the shelter a shelter. It is the smaller claim, and it is
/// the one that can be true.</summary>
internal static class ShelterBlueprint
{
    /// <summary>The grid the wood set is built on.</summary>
    internal const float PanelMetres = 2f;

    /// <summary>Half a panel: the distance from the middle of the shelter to the
    /// middle of a panel beside it.</summary>
    private const float Half = PanelMetres / 2f;

    /// <summary>The outer face, two panels from the middle.</summary>
    private const float Edge = PanelMetres;

    /// <summary>Wall panels are 2 m tall and sit on the floor, so their middles
    /// are 1 m up.</summary>
    private const float WallMiddle = PanelMetres / 2f;

    /// <summary>The roof deck sits on top of the walls.</summary>
    private const float RoofDeck = PanelMetres;

    /// <summary>The vanilla names this blueprint uses. They are grouped here so
    /// that the one list a reader has to check against the game is four lines
    /// long rather than seventeen.</summary>
    private const string Floor = "wood_floor";

    private const string Wall = "wood_wall";

    private const string Door = "wood_door";

    private const string Bed = "bed";

    private static readonly BlueprintPiece[] Shelter = Build();

    /// <summary>The shelter, in build order.</summary>
    internal static IReadOnlyList<BlueprintPiece> Pieces => Shelter;

    /// <summary>The distinct vanilla prefabs the shelter is made of, in the
    /// order they are first needed. What the recipe reader has to be able to
    /// resolve before an order may be accepted.</summary>
    internal static IReadOnlyList<string> Prefabs
    {
        get
        {
            var seen = new List<string>();
            foreach (BlueprintPiece piece in Shelter)
            {
                if (!seen.Contains(piece.Prefab))
                {
                    seen.Add(piece.Prefab);
                }
            }

            return seen;
        }
    }

    /// <summary>Every piece of one phase, in build order.</summary>
    internal static IReadOnlyList<BlueprintPiece> Of(BuildPhase phase)
    {
        var kept = new List<BlueprintPiece>();
        foreach (BlueprintPiece piece in Shelter)
        {
            if (piece.Phase == phase)
            {
                kept.Add(piece);
            }
        }

        return kept;
    }

    /// <summary>Where every piece goes, for one confirmed marker.
    ///
    /// Returns an empty list for a marker nobody confirmed, rather than a
    /// shelter at the origin: a placement list is a thing an NPC will act on,
    /// and the one thing it must never be is the result of a default struct.
    /// </summary>
    internal static IReadOnlyList<PiecePlacement> PlaceAt(BuildOrderMarker marker)
    {
        if (!marker.IsConfirmed)
        {
            return Array.Empty<PiecePlacement>();
        }

        double radians = marker.Yaw * Math.PI / 180d;
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);

        var placements = new PiecePlacement[Shelter.Length];
        for (int index = 0; index < Shelter.Length; index++)
        {
            BlueprintPiece piece = Shelter[index];

            // Rotate the local offset about the vertical axis by the marker's
            // own facing. East is the marker's right and north its forward, so a
            // marker turned ninety degrees turns the whole cottage with it and
            // the door still faces the way the player pointed it.
            float x = (piece.East * cos) + (piece.North * sin);
            float z = (piece.North * cos) - (piece.East * sin);

            placements[index] = new PiecePlacement(
                piece,
                new SitePoint(marker.At.X + x, marker.At.Y + piece.Up, marker.At.Z + z),
                Wrap(marker.Yaw + piece.Yaw));
        }

        return placements;
    }

    /// <summary>Degrees folded back into [0, 360), so two facings that are the
    /// same facing compare equal.</summary>
    internal static float Wrap(float degrees)
    {
        float wrapped = degrees % 360f;
        return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    private static BlueprintPiece[] Build()
    {
        var pieces = new List<BlueprintPiece>();
        int index = 0;

        // Foundation: a 4x4 m deck of four panels.
        foreach (float east in new[] { -Half, Half })
        {
            foreach (float north in new[] { -Half, Half })
            {
                pieces.Add(new BlueprintPiece(index++, Floor, BuildPhase.Foundation, east, 0f, north, 0f));
            }
        }

        // Walls: eight panel slots around the deck, one of which is the doorway.
        // The doorway is the south-east slot - a definite choice rather than a
        // centred one, because a 4 m wall is two 2 m panels and there is no
        // centre slot to put a door in without inventing a narrower piece.
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, -Half, WallMiddle, Edge, 0f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, Half, WallMiddle, Edge, 0f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, -Edge, WallMiddle, -Half, 90f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, -Edge, WallMiddle, Half, 90f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, Edge, WallMiddle, -Half, 90f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, Edge, WallMiddle, Half, 90f));
        pieces.Add(new BlueprintPiece(index++, Wall, BuildPhase.Walls, -Half, WallMiddle, -Edge, 0f));
        pieces.Add(new BlueprintPiece(index++, Door, BuildPhase.Walls, Half, WallMiddle, -Edge, 0f));

        // Roof: the same deck, one storey up.
        foreach (float east in new[] { -Half, Half })
        {
            foreach (float north in new[] { -Half, Half })
            {
                pieces.Add(new BlueprintPiece(index++, Floor, BuildPhase.Roof, east, RoofDeck, north, 0f));
            }
        }

        // Interior: the one thing that makes it a shelter rather than a box.
        pieces.Add(new BlueprintPiece(index, Bed, BuildPhase.Interior, 0f, 0f, 0f, 0f));

        return pieces.ToArray();
    }
}
