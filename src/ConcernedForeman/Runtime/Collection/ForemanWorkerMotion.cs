using System;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

internal enum BodyLookupOutcome
{
    Unspecified = 0,
    Found = 1,
    Missing = 2,

    /// <summary>More than one body claims the identity. Neither is used or
    /// destroyed automatically (D9).</summary>
    Duplicated = 3,
}

/// <summary>Finds Thorstein's body among the live worker actors.
///
/// A body that carries the contract's identity field (<c>tcc.worker.key</c>,
/// CONTRACTS.md §5.6, written by agent D's persistent body) is preferred. Until
/// that exists, the single unkeyed body the <c>cf_worker spawn</c> spike creates
/// is used. Two candidates of the same standing are a duplicate, and a duplicate
/// is refused rather than resolved by picking one.</summary>
internal static class ForemanWorkerBodies
{
    internal const string WorkerKeyField = "tcc.worker.key";

    internal static BodyLookupOutcome Find(WorkerKey worker, out ForemanWorkerAI? body)
    {
        body = null;
        ForemanWorkerAI? keyed = null;
        ForemanWorkerAI? unkeyed = null;
        int keyedCount = 0;
        int unkeyedCount = 0;

        foreach (BaseAI ai in BaseAI.BaseAIInstances)
        {
            if (!(ai is ForemanWorkerAI candidate) || candidate == null)
            {
                continue;
            }

            ZNetView view = candidate.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                continue;
            }

            string stored = view.GetZDO().GetString(WorkerKeyField, string.Empty);
            if (stored.Length == 0)
            {
                unkeyed = candidate;
                unkeyedCount++;
            }
            else if (string.Equals(stored, worker.Value, StringComparison.Ordinal))
            {
                keyed = candidate;
                keyedCount++;
            }
        }

        if (keyedCount > 1 || (keyedCount == 0 && unkeyedCount > 1))
        {
            return BodyLookupOutcome.Duplicated;
        }

        body = keyedCount == 1 ? keyed : unkeyed;
        return body != null ? BodyLookupOutcome.Found : BodyLookupOutcome.Missing;
    }
}

/// <summary>Walking Thorstein (the C1 <see cref="IWorkerMotion"/> seam) over
/// <see cref="ForemanWorkerAI"/>: only the job holding his actor mode may send
/// him anywhere or stop him, arrival is by distance, and he moves only through
/// the worker actor's budgeted vanilla motor.</summary>
internal sealed class ForemanWorkerMotion : IWorkerMotion
{
    private readonly ActorModeOwner _modes;
    private readonly Func<ForemanWorkerAI?> _body;
    private Vector3 _lastPosition;

    public ForemanWorkerMotion(ActorModeOwner modes, Func<ForemanWorkerAI?> body)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public WorkerKey Worker => _modes.Worker;

    public bool IsPresent
    {
        get
        {
            ForemanWorkerAI? body = _body();
            return body != null && !body.IsFaulted && body.IsOwnedAndValid;
        }
    }

    public Vector3 Position
    {
        get
        {
            ForemanWorkerAI? body = _body();
            if (body != null)
            {
                _lastPosition = body.transform.position;
            }

            return _lastPosition;
        }
    }

    public WorkerWalkStatus Status => IsPresent ? _body()!.WalkStatus : WorkerWalkStatus.Idle;

    public WorkerDeferralReason LastDeferral
    {
        get
        {
            ForemanWorkerAI? body = _body();
            return body != null ? body.DeferredReason : WorkerDeferralReason.None;
        }
    }

    public bool WalkTo(Vector3 point, float arrivalTolerance, string jobId)
    {
        if (!_modes.IsHeldBy(jobId) || !IsPresent)
        {
            return false;
        }

        return _body()!.SetJobGoal(point, arrivalTolerance);
    }

    public void Stop(string jobId)
    {
        if (!_modes.IsHeldBy(jobId))
        {
            return;
        }

        ForemanWorkerAI? body = _body();
        if (body != null)
        {
            body.ClearJobGoal();
        }
    }
}

/// <summary>The game-free loop's motion port over the C1 seam: the same calls
/// in <see cref="SitePoint"/> terms.</summary>
internal sealed class CollectionMotionBridge : ICollectionMotion
{
    private readonly IWorkerMotion _motion;

    public CollectionMotionBridge(IWorkerMotion motion)
    {
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
    }

    public bool IsPresent => _motion.IsPresent;

    public SitePoint Position => NaturalSourceClassifier.ToSitePoint(_motion.Position);

    public CollectionWalkStatus Status
    {
        get
        {
            switch (_motion.Status)
            {
                case WorkerWalkStatus.Idle:
                    return CollectionWalkStatus.Idle;
                case WorkerWalkStatus.Walking:
                    return CollectionWalkStatus.Walking;
                case WorkerWalkStatus.Arrived:
                    return CollectionWalkStatus.Arrived;
                case WorkerWalkStatus.Deferred:
                    return CollectionWalkStatus.Deferred;
                default:
                    return CollectionWalkStatus.Unspecified;
            }
        }
    }

    public WorkerDeferralReason LastDeferral => _motion.LastDeferral;

    public bool WalkTo(SitePoint point, float arrivalTolerance, string jobId) =>
        _motion.WalkTo(NaturalSourceClassifier.ToVector3(point), arrivalTolerance, jobId);

    public void Stop(string jobId) => _motion.Stop(jobId);
}
