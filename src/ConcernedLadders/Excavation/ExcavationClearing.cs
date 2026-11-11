using System.Collections.Generic;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Clears the ground a dig has just been placed on (#567, owner playtest 2026-11-11: "digging should ignore pickable
/// objects and others and just destroy them and they shouldn't prevent digging, unless there's a more graceful way"). The
/// only place in the product that removes a world object, reached only from <see cref="ExcavationRuntime"/>'s commit, after
/// every gate has passed and the game has placed the dig, with the lists <see cref="ExcavationWorldChecks.Survey"/> made
/// for that dig in the same press.
///
/// <list type="bullet">
/// <item><b>Pickables</b> are picked first, through the game's own pick (<c>Pickable.Interact</c>), so whatever they yield
/// drops for the player as a hand pick would - the graceful way - and are then removed with the rest, so a bush that would
/// regrow does not float over the hole.</item>
/// <item><b>Every other networked world object</b> found there (rocks, stumps, bushes, logs, trees) is removed the way the
/// game removes its own: ownership claimed, then <c>ZNetScene.Destroy</c>, which deletes the saved object only for its owner
/// (read from the installed game). Building pieces, loose items, carts, ships and anything without a network object never
/// reach here (the survey sorts them out).</item>
/// </list>
/// Nothing is created here, and an undo does not bring a removed object back.</summary>
internal static class ExcavationClearing
{
    /// <summary>Picks, then removes, the pickables; removes the rest. Returns how many objects were removed.</summary>
    internal static int Clear(Player player, List<GameObject> toPick, List<GameObject> toRemove)
    {
        int removed = 0;
        foreach (GameObject target in toPick)
        {
            Pickable? pickable = target != null ? target.GetComponent<Pickable>() : null;
            if (pickable != null && pickable.CanBePicked()) pickable.Interact(player, false, false);
            removed += Remove(target);
        }

        foreach (GameObject target in toRemove) removed += Remove(target);
        toPick.Clear();
        toRemove.Clear();
        return removed;
    }

    /// <summary>Removes earth blocks (#577), the way the rest is removed: those a new hole's floor covers, from the dig's
    /// commit, and those a dig placed, from its undo. Returns how many were removed.</summary>
    internal static int RemoveFill(IReadOnlyList<GameObject> blocks)
    {
        int removed = 0;
        foreach (GameObject block in blocks)
        {
            if (block != null && EarthFillPlacement.IsFill(block)) removed += Remove(block);
        }

        return removed;
    }

    private static int Remove(GameObject? target)
    {
        if (target == null) return 0;
        ZNetView view = target.GetComponent<ZNetView>();
        ZNetScene scene = ZNetScene.instance;
        if (view == null || !view.IsValid() || scene == null) return 0;
        view.ClaimOwnership();
        scene.Destroy(target);
        return 1;
    }
}
