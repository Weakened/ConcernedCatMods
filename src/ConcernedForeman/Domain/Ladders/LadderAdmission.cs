using System;

namespace TheConcernedCat.ConcernedForeman.Domain.Ladders;

/// <summary>Why a piece is, or is not, one this feature will let you climb
/// (CF-LAD-004, `docs/mods/concerned-foreman/LADDERS.md` L2).</summary>
internal enum LadderVerdict
{
    /// <summary>A prefab name the feature already knows is a ladder. The two
    /// buildable pieces are here, and they are admitted without measuring,
    /// because they are the deliverable: a measurement quirk must not be able
    /// to exclude the piece a player just built.</summary>
    KnownPiece,

    /// <summary>Not a name we know, but it carries a <c>Ladder</c> and it
    /// measures like one.</summary>
    MeasuresLikeALadder,

    /// <summary>Nothing could be measured: no mesh, no collider, or numbers
    /// that are not finite. Not a refusal of the piece so much as an absence of
    /// evidence, so it is never remembered.</summary>
    NotMeasured,

    /// <summary>Shorter than a ladder. You walk up it.</summary>
    TooShort,

    /// <summary>Wider than any ladder. The domain refuses to measure a piece
    /// this wide as well (<see cref="TheConcernedCat.Ladders.LadderGeometry.TryMeasure"/>).</summary>
    TooWide,

    /// <summary>Deep enough to be furniture rather than a ladder: a ladder is a
    /// thin plane fixed to something.</summary>
    TooDeep,

    /// <summary>Wider than it is tall, so it is lying down, leaning past what
    /// this climb can hold, or it is a hatch rather than a ladder.</summary>
    LyingDown,
}

/// <summary>Which pieces this feature will let you climb.
///
/// Two rules, in order:
///
/// <list type="number">
/// <item>A <b>name we know</b> is admitted. The list is data (<see cref="KnownLadders"/>),
/// not branches, so adding a piece after an in-game audit is a string, and a
/// name that does not exist in the loaded game simply never matches — it is
/// never looked up, so it can never throw.</item>
/// <item>Anything else that carries a <c>Ladder</c> component is admitted only
/// if it <b>measures like a ladder</b>: tall enough to be worth climbing,
/// narrow enough to be one, thin enough to be one, and standing up.</item>
/// </list>
///
/// <b>The loaded game is the authority.</b> The names below are a shortcut for
/// the pieces the brief names, never a requirement: every measurement comes
/// from the object actually standing in the world, and a ladder this list has
/// never heard of is climbable if it measures like one. The thresholds are
/// deliberately generous — a refused ladder is a ladder that still teleports,
/// which is vanilla and therefore safe, but it is also a promise broken, so
/// they err towards admitting.
///
/// Nothing here knows about Unity: the runtime reads the numbers off the piece
/// and this decides.</summary>
internal static class LadderAdmission
{
    /// <summary>The pieces the brief names, by prefab name
    /// (LADDERS.md §1, from the game's own asset manifest). Buildable, vanilla,
    /// and the primary deliverable of this feature.
    ///
    /// Matching ignores case and the <c>(Clone)</c> Unity adds to an
    /// instantiated prefab. A name that is not in the loaded game costs nothing
    /// and breaks nothing.</summary>
    internal static readonly string[] KnownLadders =
    {
        // The wooden step ladder from the hammer's build menu.
        "wood_stepladder",
        // The grausten stone ladder. Both spellings the manifest and the
        // localisation key suggest, because only the loaded game can say which
        // one the prefab actually carries, and an extra string costs nothing.
        "Piece_grausten_stone_ladder",
        "grausten_stone_ladder",
        "piece_grausten_stoneladder",
    };

    /// <summary>Shorter than this and it is a step, not a ladder. The domain's
    /// own threshold, so there is one truth about it.</summary>
    internal const float MinimumHeightMetres = TheConcernedCat.Ladders.LadderGeometry.MinimumClimbableHeight;

    /// <summary>Wider than this and the domain refuses to measure it anyway.</summary>
    internal const float MaximumWidthMetres = 4f;

    /// <summary>How deep a piece may be, front to back, and still be a ladder.
    /// Generous: a piece turned 45° to the world axes smears its own width into
    /// its depth, and this is measured from a world-axis bounding box.</summary>
    internal const float MaximumDepthMetres = 1.2f;

    /// <summary>How tall a prop must be relative to its widest horizontal
    /// extent before it counts as standing up. At 1 it would refuse every
    /// leaning ladder; below this it would admit hatches and platforms.</summary>
    internal const float StandingHeightToWidthRatio = 0.9f;

    /// <summary>Is this one of the pieces we already know? Case-insensitive,
    /// and <c>(Clone)</c>-insensitive, because that is how the name arrives
    /// from an instantiated prefab.</summary>
    internal static bool IsKnown(string? prefabName)
    {
        string cleaned = Clean(prefabName);
        if (cleaned.Length == 0)
        {
            return false;
        }

        for (int index = 0; index < KnownLadders.Length; index++)
        {
            if (string.Equals(cleaned, Clean(KnownLadders[index]), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The verdict on one piece.</summary>
    /// <param name="prefabName">The piece's prefab name, or any name at all:
    /// an unknown one is not an error, it just falls through to the
    /// measurement.</param>
    /// <param name="buildable">Whether it carries a <c>Piece</c>, meaning a
    /// player put it there with a hammer. A buildable piece that carries a
    /// <c>Ladder</c> is something someone deliberately placed to go up, so it
    /// is not asked to prove it is thin and upright as well; a prop found
    /// standing in the world is.</param>
    /// <param name="heightMetres">World height, foot to head.</param>
    /// <param name="widestMetres">The wider of its two horizontal extents.</param>
    /// <param name="thinnestMetres">The narrower of its two horizontal extents.</param>
    internal static LadderVerdict Decide(
        string? prefabName,
        bool buildable,
        float heightMetres,
        float widestMetres,
        float thinnestMetres)
    {
        if (IsKnown(prefabName))
        {
            return LadderVerdict.KnownPiece;
        }

        if (!IsFinite(heightMetres) || !IsFinite(widestMetres) || !IsFinite(thinnestMetres) ||
            heightMetres <= 0f || widestMetres <= 0f || thinnestMetres < 0f)
        {
            return LadderVerdict.NotMeasured;
        }

        if (heightMetres < MinimumHeightMetres)
        {
            return LadderVerdict.TooShort;
        }

        if (widestMetres > MaximumWidthMetres)
        {
            return LadderVerdict.TooWide;
        }

        if (buildable)
        {
            // A hammer-placed piece carrying a Ladder is a ladder by intent.
            return LadderVerdict.MeasuresLikeALadder;
        }

        if (thinnestMetres > MaximumDepthMetres)
        {
            return LadderVerdict.TooDeep;
        }

        if (heightMetres < widestMetres * StandingHeightToWidthRatio)
        {
            return LadderVerdict.LyingDown;
        }

        return LadderVerdict.MeasuresLikeALadder;
    }

    /// <summary>Does this verdict make the piece climbable.</summary>
    internal static bool Admits(this LadderVerdict verdict) =>
        verdict == LadderVerdict.KnownPiece || verdict == LadderVerdict.MeasuresLikeALadder;

    /// <summary>Whether a verdict is worth remembering. "I could not measure
    /// it" is not: a piece whose art had not streamed in yet must get another
    /// chance, or one bad moment would make it teleport forever.</summary>
    internal static bool WorthRemembering(this LadderVerdict verdict) =>
        verdict != LadderVerdict.NotMeasured;

    /// <summary>A sentence a person can read in a log.</summary>
    internal static string Explain(LadderVerdict verdict)
    {
        switch (verdict)
        {
            case LadderVerdict.KnownPiece:
                return "a ladder piece this feature knows";
            case LadderVerdict.MeasuresLikeALadder:
                return "not a piece this feature knows, but it measures like a ladder";
            case LadderVerdict.NotMeasured:
                return "nothing could be measured on it, so it keeps vanilla behaviour for now";
            case LadderVerdict.TooShort:
                return "shorter than " + MinimumHeightMetres.ToString("0.##") + " m, so you walk up it";
            case LadderVerdict.TooWide:
                return "wider than " + MaximumWidthMetres.ToString("0.##") + " m, which no ladder is";
            case LadderVerdict.TooDeep:
                return "deeper than " + MaximumDepthMetres.ToString("0.##") + " m, so it is furniture, not a ladder";
            case LadderVerdict.LyingDown:
                return "wider than it is tall, so it is lying down rather than standing";
            default:
                return "no reason was recorded";
        }
    }

    /// <summary>The prefab name without Unity's <c>(Clone)</c> and without
    /// surrounding space. Never throws, and never returns null.</summary>
    internal static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        string trimmed = name!.Trim();
        const string clone = "(Clone)";
        while (trimmed.EndsWith(clone, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - clone.Length).TrimEnd();
        }

        return trimmed;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
