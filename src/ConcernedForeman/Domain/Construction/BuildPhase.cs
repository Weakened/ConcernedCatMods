namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>Which part of a shelter a piece belongs to, and therefore when it
/// may be built and which trip it is fetched for.
///
/// <b>Why a phase is not just documentation.</b> Two rules hang off it and
/// neither is cosmetic. A roof panel placed before the walls that hold it up is
/// refused by the game, so a phase is an <i>ordering constraint</i>: nothing in
/// a later phase is offered as work until every piece of the earlier ones is
/// standing. And a shelter that does not fit one carry has to be split into
/// trips somehow; splitting it by phase is the split a player watching can
/// read, which is the whole of #380's "coherent batches - foundation, walls,
/// roof, interior - rather than arbitrary single-piece trips".
///
/// <b>Where the ordering is enforced.</b> In this product, not in the shared NPC
/// runtime: <see cref="ShelterBuildLoop"/> builds only pieces that
/// <see cref="ConstructionProgress"/> allows, and ConstructionProgress walks the
/// phases in <see cref="BuildPhases.InOrder"/>, so only the earliest unfinished
/// phase's pieces are offered as work. Normally the loop draws one batch per
/// phase from the supply chest. Nothing
/// hands Thorstein's work to the shared runtime today (#377's two production
/// roles are Sunniva's upkeep and Gunnar's collection tour), so
/// <see cref="BuildPhases.PriorityOf"/> is not read by any production code. It
/// is the number a phase would carry as a target priority if construction were
/// ever handed to that runtime, and its tests pin that it agrees with the order
/// above.
///
/// <b>Unspecified is zero and is never a phase.</b> A default-constructed piece
/// must not silently become a foundation piece.</summary>
internal enum BuildPhase
{
    /// <summary>Not a phase. The default, and never buildable.</summary>
    Unspecified = 0,

    /// <summary>The floor the rest stands on.</summary>
    Foundation = 1,

    /// <summary>The enclosing walls and the doorway.</summary>
    Walls = 2,

    /// <summary>What keeps the weather out.</summary>
    Roof = 3,

    /// <summary>What makes it a shelter rather than a box: the bed.</summary>
    Interior = 4,
}

/// <summary>The order the phases run in, and the priority each one carries.
/// </summary>
internal static class BuildPhases
{
    /// <summary>Every real phase, in build order. <b>This list is the order</b>
    /// - nothing else sorts phases, and a phase added to the enum without being
    /// added here is caught by <c>ShelterBlueprintTests</c> rather than quietly
    /// built last.</summary>
    internal static readonly BuildPhase[] InOrder =
    {
        BuildPhase.Foundation,
        BuildPhase.Walls,
        BuildPhase.Roof,
        BuildPhase.Interior,
    };

    /// <summary>The priority a phase would carry if construction were ever
    /// handed to the shared runtime (nothing does that today; see the class
    /// remarks).
    ///
    /// Higher is serviced sooner, which is that runtime's convention, so the
    /// numbers run <i>down</i> as the build runs on. The gap of a hundred is
    /// deliberate: it leaves room for a later leaf to put something between two
    /// phases without renumbering, and renumbering would be free only because
    /// <b>this number is never written to disk</b>. It must stay that way - a
    /// durable priority would make re-ordering the build a data migration.
    /// </summary>
    internal static int PriorityOf(BuildPhase phase)
    {
        switch (phase)
        {
            case BuildPhase.Foundation:
                return 400;
            case BuildPhase.Walls:
                return 300;
            case BuildPhase.Roof:
                return 200;
            case BuildPhase.Interior:
                return 100;
            default:
                return 0;
        }
    }

    /// <summary>The phase after this one, or <see cref="BuildPhase.Unspecified"/>
    /// when there is none.</summary>
    internal static BuildPhase After(BuildPhase phase)
    {
        for (int index = 0; index < InOrder.Length - 1; index++)
        {
            if (InOrder[index] == phase)
            {
                return InOrder[index + 1];
            }
        }

        return BuildPhase.Unspecified;
    }

    /// <summary>A player-facing word for a phase. Lower case, because it is used
    /// inside a sentence.</summary>
    internal static string Describe(BuildPhase phase)
    {
        switch (phase)
        {
            case BuildPhase.Foundation:
                return "the foundation";
            case BuildPhase.Walls:
                return "the walls";
            case BuildPhase.Roof:
                return "the roof";
            case BuildPhase.Interior:
                return "the inside";
            default:
                return "nothing";
        }
    }
}
