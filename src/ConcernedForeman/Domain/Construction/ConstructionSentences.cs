using System.Globalization;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>Everything a build order says to a player, in one place.
///
/// <b>Why they are together.</b> A refusal is only useful if it names the thing
/// the player can change, and that is a property of the whole set rather than of
/// any one sentence: "it did not work" three times over is what a scattered set
/// of messages turns into. Keeping them here also keeps them out of the shared
/// NPC runtime, which must never name a shelter.
///
/// <b>No sentence here ever claims something happened in game.</b> Each says
/// what was refused, what is missing or what is being waited for.</summary>
internal static class ConstructionSentences
{
    /// <summary>What the order is, priced, for the confirmation prompt. This is
    /// the number a player agrees to, so it is the real total from the real
    /// recipes and never a rounded one.</summary>
    internal static string Offer(ShelterPlan plan)
    {
        if (!plan.IsPlanned)
        {
            return "That build order cannot be offered: " + plan.Refusal + ".";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Shelter at {0} facing {1:0} degrees: {2} pieces, {3}. Confirm to authorise it.",
            plan.Marker.At,
            plan.Marker.Yaw,
            plan.Pieces.Count,
            plan.Total.Describe());
    }

    /// <summary>The missing-material list. <b>Per item</b>, because "you are
    /// eight short" without saying of what is not a list.</summary>
    internal static string ShortOfMaterial(MaterialTally? missing)
    {
        if (missing == null || missing.IsEmpty)
        {
            return "The order is waiting for material, though nothing was named as missing. " +
                "That is a defect: it should have said what.";
        }

        return "The order is waiting: " + missing.Describe() +
            " is not in any container Thorstein may use. Put it in one, or enable the container " +
            "that has it, and the order goes on by itself.";
    }

    /// <summary>A chest that exists and could not be opened. The order waits -
    /// it never reaches for a different chest, because the player named this
    /// one.</summary>
    internal static string ContainerUnavailable(string? detail)
    {
        string why = string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")";
        return "The order is waiting: a container it needs could not be opened" + why +
            ". Nothing else will be used instead.";
    }

    /// <summary>What phase he is on, for the panel.</summary>
    internal static string Working(ConstructionProgress progress)
    {
        if (progress.IsComplete)
        {
            return "The shelter is finished.";
        }

        BuildPhase phase = progress.CurrentPhase;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Building {0}: {1} of {2} pieces still to place, {3} still needed.",
            BuildPhases.Describe(phase),
            progress.Remaining.Count,
            progress.Plan.Pieces.Count,
            progress.RemainingTotal.Describe());
    }

    /// <summary>A build that stopped, and what was preserved.
    ///
    /// <b>The conservation sentence is the load-bearing half.</b> An interrupted
    /// build must say where the material went, or a player has no way to tell a
    /// refund from a loss.</summary>
    internal static string Stopped(string reason, MaterialTally? refunded, MaterialTally? stillCarried)
    {
        string text = "The build order stopped: " + reason;
        if (refunded != null && !refunded.IsEmpty)
        {
            text += " " + refunded.Describe() + " went back where it came from.";
        }

        if (stillCarried != null && !stillCarried.IsEmpty)
        {
            text += " Thorstein is still carrying " + stillCarried.Describe() +
                "; it is in his own inventory and nothing has been lost.";
        }

        if ((refunded == null || refunded.IsEmpty) && (stillCarried == null || stillCarried.IsEmpty))
        {
            text += " Nothing had been taken out of a container.";
        }

        return text;
    }

    /// <summary>Pieces something is standing in the way of.
    ///
    /// <b>This sentence is a boundary, not a status line.</b> The
    /// placement-clearance policy is a parked owner decision
    /// (<c>docs/settlement/cart-and-collection/DECISIONS.md</c> D13), so an
    /// obstructed site is something a person resolves and never something the
    /// build loop resolves: nothing is cleared, levelled, terraformed or
    /// destroyed to make a placement succeed. Saying so in the refusal is how a
    /// player knows the order is waiting on them rather than stuck.</summary>
    internal static string Obstructed(System.Collections.Generic.IReadOnlyList<CostedPiece>? blocked)
    {
        if (blocked == null || blocked.Count == 0)
        {
            return "Something is in the way, though nothing was named. That is a defect: it should have " +
                "said which piece and where.";
        }

        var text = new System.Text.StringBuilder();
        text.Append(blocked.Count == 1 ? "One piece cannot go up" : blocked.Count + " pieces cannot go up")
            .Append(" because something is already there: ");
        int named = 0;
        foreach (CostedPiece piece in blocked)
        {
            if (named == 3)
            {
                text.Append(" and ").Append(blocked.Count - 3).Append(" more");
                break;
            }

            if (named > 0)
            {
                text.Append(", ");
            }

            text.Append(piece.Placement.Piece.Prefab).Append(" at ").Append(piece.Placement.At);
            named++;
        }

        return text.Append(
            ". Nothing will be cleared, levelled or taken down to make room - move what is in the way, " +
            "or cancel the order. Nothing has been taken out of a container for those pieces.").ToString();
    }

    /// <summary>A placement the gate refused, naming the check that refused it.
    /// </summary>
    internal static string Refused(PiecePlacement placement, string check)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} at {1} was not placed: {2}. Nothing was taken out of a container for it.",
            placement.Piece.Prefab,
            placement.At,
            string.IsNullOrEmpty(check) ? "the check could not be made" : check);
    }
}
