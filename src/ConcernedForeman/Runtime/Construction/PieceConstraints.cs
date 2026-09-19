using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>Every placement constraint a vanilla <c>Piece</c> can declare, read
/// off the piece and either judged or refused.
///
/// <b>Why this exists, in one sentence.</b> <c>Player.TryPlacePiece</c> weighs a
/// dozen constraints through the local player's placement ghost, an NPC cannot
/// drive that ghost, and the alternative to reimplementing them was a
/// containment that lived in a data file - "the four wood pieces we happen to
/// use declare none of them" - which would have evaporated the day somebody
/// added a fifth, with no test going red and no validator complaining. So the
/// containment is here instead: <b>a piece declaring anything this runtime does
/// not judge is refused, by name</b>.
///
/// <b>The field list is the game's, not a guess.</b> It was read out of the
/// installed <c>assembly_valheim</c>'s own metadata, which is also where the
/// types came from - <c>m_onlyInBiome</c> is a <c>Heightmap.Biome</c>,
/// <c>m_spaceRequirement</c> a float, <c>m_blockingPieces</c> a list,
/// <c>m_mustConnectTo</c> a single reference, and the rest booleans. A field
/// that disappears in a game update is a compile error here rather than a check
/// that silently stops running.
///
/// <b>Two constraints are judged.</b> Dungeons, through
/// <c>Character.InInterior(point)</c>; and biome, through
/// <c>Heightmap.FindBiome(point)</c> against the piece's own mask. Both are
/// exact reimplementations of a yes/no the game asks the same way.
///
/// <b>Everything else is refused when declared</b>, including the two whose
/// restrictive value is <i>false</i> rather than true - <c>m_allowedInDeepSnow</c>
/// and <c>m_enabled</c> - because a permission that is off is as much a
/// constraint as a prohibition that is on. The one to watch is deep snow: the
/// game surfaces no "is there deep snow here" helper (only
/// <c>Player.m_deepSnowBuildHeight</c> and a paint mask), so a piece that
/// forbids deep snow is refused everywhere until somebody implements that test.
/// If the first play session shows the wood set declaring it, <b>that</b> is the
/// next thing to write, and it is a named follow-up rather than a
/// surprise.</summary>
internal static class PieceConstraints
{
    /// <summary>Judges one placement against the piece's own declared
    /// constraints.</summary>
    /// <param name="piece">The prefab's piece component.</param>
    /// <param name="at">Where it would go.</param>
    /// <param name="constraint">The constraint that failed, or the one this
    /// runtime does not judge. Empty when everything passed.</param>
    internal static ProbeAnswer Judge(Piece? piece, Vector3 at, out string constraint)
    {
        constraint = string.Empty;
        if (piece == null)
        {
            constraint = "a piece component to read its constraints from";
            return ProbeAnswer.CouldNotTell;
        }

        // 1. The two that are judged, because the game asks them as a plain
        //    yes/no about a point and this runtime can ask the same question.
        if (!piece.m_allowedInDungeons && Character.InInterior(at))
        {
            constraint = "it may not be built inside a dungeon, and that place is inside one";
            return ProbeAnswer.No;
        }

        if (piece.m_onlyInBiome != Heightmap.Biome.None)
        {
            Heightmap.Biome here = Heightmap.FindBiome(at);
            if ((piece.m_onlyInBiome & here) == 0)
            {
                constraint = "it may only be built in " + piece.m_onlyInBiome + ", and that place is " + here;
                return ProbeAnswer.No;
            }
        }

        // 2. Everything else. Each is a constraint vanilla's placement ghost
        //    weighs and this runtime does not, so declaring one is a refusal
        //    that names it rather than a piece placed where the game would have
        //    said no.
        foreach ((bool declared, string named) in Unjudged(piece))
        {
            if (declared)
            {
                constraint = named;
                return ProbeAnswer.CouldNotTell;
            }
        }

        return ProbeAnswer.Yes;
    }

    /// <summary>The constraints this runtime does not implement, each with the
    /// sentence a player reads. <b>The list is the audit</b>: adding a judged
    /// constraint means moving a row out of here, and a game update that adds a
    /// field means adding one.</summary>
    private static IEnumerable<(bool Declared, string Named)> Unjudged(Piece piece)
    {
        yield return (!piece.m_enabled, "that it is not a piece the game currently offers");
        yield return (piece.m_isUpgrade, "that it is an upgrade to something already standing");
        yield return (piece.m_repairPiece, "that it repairs rather than builds");
        yield return (piece.m_removePiece, "that it removes rather than builds");
        yield return (piece.m_groundPiece, "that it is a piece of ground rather than a building piece");
        yield return (piece.m_groundOnly, "that it may only be built directly on the ground");
        yield return (piece.m_cultivatedGroundOnly, "that it may only be built on cultivated ground");
        yield return (piece.m_vegetationGroundOnly, "that it may only be built on vegetation");
        yield return (piece.m_waterPiece, "that it is built on water");
        yield return (piece.m_noInWater, "that it may not be built in water");
        yield return (piece.m_notOnWood, "that it may not be built on wood");
        yield return (piece.m_notOnTiltingSurface, "that it may not be built on a slope");
        yield return (piece.m_inCeilingOnly, "that it may only be built on a ceiling");
        yield return (piece.m_notOnFloor, "that it may not be built on a floor");
        yield return (piece.m_onlyInTeleportArea, "that it may only be built in a teleport area");
        yield return (piece.m_requireDeepSnow, "that it needs deep snow");
        yield return (!piece.m_allowedInDeepSnow, "that it may not be built in deep snow");
        yield return (piece.m_spaceRequirement > 0f, "a clear space around it that is measured its own way");
        yield return (piece.m_mustConnectTo != null, "that it must connect to something in particular");
        yield return (
            piece.m_blockingPieces != null && piece.m_blockingPieces.Count > 0,
            "pieces it may not be built near");
    }
}
