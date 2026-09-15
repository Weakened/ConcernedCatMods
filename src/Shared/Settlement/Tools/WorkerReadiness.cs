using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Tools;

/// <summary>Why a worker cannot start work.
///
/// <see cref="Unspecified"/> is zero, so a verdict nobody filled in is a refusal
/// that says it is a bug rather than a plausible-sounding reason.</summary>
internal enum ReadinessRefusal
{
    /// <summary>Refused with no reason recorded — a defect, named so it shows up
    /// as one.</summary>
    Unspecified = 0,

    /// <summary>Nobody has been recruited. Recruitment is a separate, permanent
    /// thing from readiness; this is the one that never comes back once
    /// granted.</summary>
    NotRecruited = 1,

    /// <summary>A tool the job needs was never handed over.</summary>
    ToolMissing = 2,

    /// <summary>A tool was handed over and is no longer usable — broken, or
    /// otherwise refused by the game right now. Recorded separately from
    /// missing because the player's next move is different: find it and repair
    /// it, rather than find one at all.</summary>
    ToolUnusable = 3,

    /// <summary>A handover was interrupted and this build does not know whether
    /// the item moved. Work stops until a person resolves it, because acting
    /// either way risks losing or duplicating a real tool.</summary>
    HandoverUncertain = 4,
}

/// <summary>Whether a worker can start, and if not, precisely what is wrong.</summary>
internal readonly struct ReadinessVerdict
{
    private ReadinessVerdict(bool isReady, ReadinessRefusal refusal, ToolKind tool)
    {
        IsReady = isReady;
        Refusal = refusal;
        Tool = tool;
    }

    public bool IsReady { get; }

    public ReadinessRefusal Refusal { get; }

    /// <summary>Which tool the refusal is about, when it is about one.</summary>
    public ToolKind Tool { get; }

    internal static ReadinessVerdict Ready() =>
        new(true, ReadinessRefusal.Unspecified, ToolKind.None);

    internal static ReadinessVerdict Refused(ReadinessRefusal refusal, ToolKind tool = ToolKind.None) =>
        new(false, refusal, tool);

    /// <summary>One sentence a player can act on. Every refusal names the next
    /// move, because "Thorstein needs attention" without one is the notice that
    /// trains people to ignore notices.</summary>
    public string Describe()
    {
        switch (Refusal)
        {
            case ReadinessRefusal.NotRecruited:
                return "Nobody is employed here yet.";

            case ReadinessRefusal.ToolMissing when Tool == ToolKind.Axe:
                return "He has no axe. Give him one and he can cut.";

            case ReadinessRefusal.ToolMissing when Tool == ToolKind.Hammer:
                return "He has no hammer. Give him one and he can build.";

            case ReadinessRefusal.ToolMissing:
                // Reachable without a kind -- a missing ledger, or a caller that
                // did not say what the job needs. Naming a hammer here would be
                // a guess, and an earlier version made it.
                return "He is not equipped for that job.";

            case ReadinessRefusal.ToolUnusable when Tool == ToolKind.Axe:
                return "His axe will not cut any more. It needs repairing, or a better one.";

            case ReadinessRefusal.ToolUnusable:
                return "His hammer will not build any more. It needs repairing, or a better one.";

            case ReadinessRefusal.HandoverUncertain:
                return "A tool handover was interrupted and it is not recorded whether the item " +
                    "changed hands. Nothing has been taken or given back. Check your inventory " +
                    "and his, then resolve it — this build will not guess.";

            default:
                return IsReady
                    ? "Ready to work."
                    : "Not ready, and no reason was recorded — that is a bug, please report it.";
        }
    }

    public override string ToString() => Describe();
}

/// <summary>Answers "can this worker start", from the ledger plus a live look at
/// the tools themselves.
///
/// <b>Readiness is not recruitment, and the difference is deliberate.</b>
/// Recruitment is permanent: once somebody has been taken on, losing an axe does
/// not un-hire them, and a completed introduction never replays. Readiness is a
/// live question asked again before every job, because a tool wears out while it
/// is used. Conflating the two would mean a broken hammer silently un-did a
/// story the player finished.
///
/// <b>Durability is asked of the item, never of the record.</b>
/// <see cref="ToolSpecimen.DurabilityAtIssue"/> is a snapshot for showing the
/// player what they handed over; using it to answer "can this still be used"
/// would be exactly the zero-wear infinite tool that is ruled out. The live
/// answer comes from <see cref="IToolCondition"/>, and an adapter that cannot
/// answer says so rather than assuming.</summary>
internal interface IToolCondition
{
    /// <summary>True when this exact issued tool can still do its job right now.
    ///
    /// An implementation that cannot establish the answer — the item not
    /// loaded, the worker not present, the game not in a state to be asked —
    /// must return <b>false</b>. A worker standing still because we were not
    /// sure is visible and harmless; one swinging a tool we could not check is
    /// neither.</summary>
    bool IsUsable(ToolHolding holding);
}

/// <summary>A condition that answers "no" to everything. The fallback whenever a
/// runtime has not been given a real one.</summary>
internal sealed class UnknownToolCondition : IToolCondition
{
    internal static readonly UnknownToolCondition Instance = new();

    public bool IsUsable(ToolHolding holding) => false;
}

internal static class WorkerReadiness
{
    /// <summary>What the first proof's jobs need. An axe to cut and a hammer to
    /// build — the owner's "basic axe and hammer", and nothing more, so that
    /// getting started is a short errand rather than a shopping list.</summary>
    public static readonly IReadOnlyList<ToolKind> ForBuilding =
        new[] { ToolKind.Axe, ToolKind.Hammer };

    /// <summary>What felling alone needs. Picking up a fallen branch needs
    /// neither, which is why this is asked per job rather than once: making a
    /// worker fetch a hammer before collecting loose wood would be a rule the
    /// game does not have.</summary>
    public static readonly IReadOnlyList<ToolKind> ForFelling = new[] { ToolKind.Axe };

    /// <summary>Nothing. Gathering loose wood off the ground is bare hands.</summary>
    public static readonly IReadOnlyList<ToolKind> ForGathering = new ToolKind[0];

    /// <summary>Assesses one worker against one job's requirements.</summary>
    public static ReadinessVerdict Assess(
        WorkerId worker,
        bool isRecruited,
        ToolLedger ledger,
        IReadOnlyList<ToolKind> required,
        IToolCondition condition)
    {
        if (!isRecruited)
        {
            return ReadinessVerdict.Refused(ReadinessRefusal.NotRecruited);
        }

        if (ledger == null)
        {
            return ReadinessVerdict.Refused(ReadinessRefusal.ToolMissing);
        }

        // An unresolved handover stops everything, not just the job that needs
        // that tool. Until a person says where the item went, acting at all
        // risks compounding the problem.
        if (ledger.HasUncertainHandover(worker))
        {
            return ReadinessVerdict.Refused(ReadinessRefusal.HandoverUncertain);
        }

        if (required == null)
        {
            // The one fail-OPEN path an earlier version had: a null requirement
            // list fell back to "needs nothing" and answered Ready. A caller
            // that did not say what the job needs has not established that the
            // job can be done.
            return ReadinessVerdict.Refused(ReadinessRefusal.ToolMissing);
        }

        IToolCondition live = condition ?? UnknownToolCondition.Instance;

        foreach (ToolKind kind in required)
        {
            if (!ledger.TryGetHeld(worker, kind, out ToolHolding holding))
            {
                return ReadinessVerdict.Refused(ReadinessRefusal.ToolMissing, kind);
            }

            if (!live.IsUsable(holding))
            {
                return ReadinessVerdict.Refused(ReadinessRefusal.ToolUnusable, kind);
            }
        }

        return ReadinessVerdict.Ready();
    }
}
