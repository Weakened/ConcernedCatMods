using System;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The build loop's placement port over the product's one placer.
///
/// <b>This is the join #380 was missing, and it is deliberately one line long.</b>
/// <see cref="WorldPiecePlacer"/> - the gate followed by the installer, with
/// nothing in between - was written, tested and had no production caller at all,
/// so a player could confirm an order and watch nothing happen. Everything in
/// this type is a translation: the loop's vocabulary in, the placer's vocabulary
/// out, and no decision of its own. A decision here would be a second placement
/// policy invisible to the tests that prove the first.
///
/// <b>The direct call is the point.</b> This calls
/// <see cref="WorldPiecePlacer.Place"/> on the concrete sealed class, not through
/// an interface, so <c>PlacerReachabilityTests</c> can read the compiled call out
/// of the assembly and fail if it is ever removed again.</summary>
internal sealed class GatedPiecePlacer : IPiecePlacer
{
    private readonly WorldPiecePlacer _placer;

    internal GatedPiecePlacer(WorldPiecePlacer placer)
    {
        _placer = placer ?? throw new ArgumentNullException(nameof(placer));
    }

    /// <summary>How many pieces have actually been put in the world through
    /// this placer, for the status line.</summary>
    internal int Placed => _placer.Placed;

    /// <summary>How many the gate refused.</summary>
    internal int Refused => _placer.Refused;

    /// <inheritdoc />
    public PiecePlaced Place(CostedPiece piece, MaterialTally carried, bool authorised, out string reason,
        BuildCommit? commit = null)
    {
        PlacementReport report = _placer.Place(piece, carried, authorised, commit);
        reason = report.Reason;
        switch (report.Result)
        {
            case PlacementResult.Placed:
                return PiecePlaced.Placed;
            case PlacementResult.Refused:
                return PiecePlaced.Refused;
            case PlacementResult.Failed:
                return PiecePlaced.Failed;
            default:
                // The placer reported nothing. Reading that as a placement would
                // pay for a piece nobody claims exists.
                reason = reason.Length != 0 ? reason : "the placer did not say what happened";
                return PiecePlaced.Failed;
        }
    }
}

/// <summary>The build loop's walking port over the C1 worker-motion seam.
///
/// <b>The job id is held here and never handed to the loop.</b>
/// <c>IWorkerMotion</c> refuses a walk from anything that does not hold
/// Thorstein's actor mode, and the hold belongs to the job. A loop that could
/// name the job id could send a body another job was standing in; a loop that
/// cannot has to be given one of these by whoever took the hold.</summary>
internal sealed class WorkerBuildWalk : IBuildWalk
{
    private readonly IWorkerMotion _motion;
    private readonly Func<string?> _jobId;

    /// <param name="motion">The one seam that moves Thorstein.</param>
    /// <param name="jobId">The job currently holding him, or null. Read live
    /// rather than captured: a hold that was released while an order was running
    /// must stop the walking, not keep a stale permission alive.</param>
    internal WorkerBuildWalk(IWorkerMotion motion, Func<string?> jobId)
    {
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
        _jobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
    }

    /// <inheritdoc />
    public bool IsPresent => _jobId() != null && _motion.IsPresent;

    /// <inheritdoc />
    public SitePoint Position => SitePoints.ToSitePoint(_motion.Position);

    /// <inheritdoc />
    public BuildWalkStatus Status
    {
        get
        {
            switch (_motion.Status)
            {
                case WorkerWalkStatus.Idle:
                    return BuildWalkStatus.Idle;
                case WorkerWalkStatus.Walking:
                    return BuildWalkStatus.Walking;
                case WorkerWalkStatus.Arrived:
                    return BuildWalkStatus.Arrived;
                case WorkerWalkStatus.Deferred:
                    return BuildWalkStatus.Deferred;
                default:
                    return BuildWalkStatus.Unspecified;
            }
        }
    }

    /// <inheritdoc />
    public string Deferral => Describe(_motion.LastDeferral);

    /// <inheritdoc />
    public bool WalkTo(SitePoint point, float arrivalTolerance)
    {
        string? job = _jobId();
        return job != null && _motion.WalkTo(SitePoints.ToVector3(point), arrivalTolerance, job);
    }

    /// <inheritdoc />
    public void Stop()
    {
        string? job = _jobId();
        if (job != null)
        {
            _motion.Stop(job);
        }
    }

    /// <summary>The planner's reason in the player's words. Every value is
    /// covered by name rather than by ToString, because a player reading
    /// "OutsideLoadedGround" has been told nothing they can act on.</summary>
    internal static string Describe(WorkerDeferralReason reason)
    {
        switch (reason)
        {
            case WorkerDeferralReason.Unreachable:
                return "there is no way for him to walk there";
            case WorkerDeferralReason.TooFar:
                return "it is further away than he is allowed to plan for";
            case WorkerDeferralReason.Hazardous:
                return "the way there is dangerous";
            case WorkerDeferralReason.NoAuthority:
                return "this runtime may not move him here";
            case WorkerDeferralReason.OutsideLoadedGround:
                return "it is outside loaded ground, and there is no building out of sight";
            default:
                return string.Empty;
        }
    }
}
