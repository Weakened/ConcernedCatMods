using System;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>#334: the scoped ladder motor - the ONLY thing that moves a worker
/// body other than vanilla's own ground motor, under the owner's 2026-11-01
/// decision (WORKER_LADDER_AUTHORITY_2026_11_01.md, DECISIONS.md D14.6).
///
/// <b>What it may do, all of it.</b> For one explicitly ordered worker's own
/// body, captured once and never re-pointed at another body:
/// <list type="number">
/// <item>suspend vanilla's ground motor - the body's AI reports sleeping, so
/// <c>Character.UpdateMotion</c> skips walking, and its rigidbody is made
/// kinematic so gravity and contacts do not act on it - and remember the
/// rigidbody's original kinematic flag;</item>
/// <item>write that rigidbody's position, one step of the checked ladder path
/// at a time, and only after a capsule sweep from where the body is to where
/// it is going finds nothing new in the way;</item>
/// <item>give the body back: the original kinematic flag restored and the
/// lease ended, so the next tick is vanilla's ground motor again.</item>
/// </list>
///
/// It never writes a velocity, a force, a rotation or a transform, never
/// touches another object, never takes ownership, sends no RPC and snaps
/// nothing: a position it was not handed by the checked traversal is not a
/// position it can write. A body that is interrupted part-way up is given back
/// where it is - vanilla physics then has it - and is never placed at a guessed
/// landing. <c>tools/validate_repo.py</c>'s #400 capability audit pins the two
/// kinematic writes and the one position write here by exact spelling, and its
/// #334 worker-climb audit pins the sweep in front of the write and confines
/// who may create or start a lease.</summary>
internal sealed class WorkerClimbMotor : IWorkerLadderMotor
{
    private readonly ForemanWorkerAI _ai;
    private readonly Rigidbody _body;
    private readonly Func<WorkPoint, WorkPoint, bool> _clear;
    private bool _wasKinematic;
    private bool _restored;

    private WorkerClimbMotor(
        ForemanWorkerAI ai, Rigidbody body, WorkerKey worker, Guid bodyEpoch, Func<WorkPoint, WorkPoint, bool> clear)
    {
        _ai = ai;
        _body = body;
        Worker = worker;
        BodyEpoch = bodyEpoch;
        _clear = clear;
    }

    public WorkerKey Worker { get; }

    public Guid BodyEpoch { get; }

    public bool GroundMotorSuspended =>
        !_restored && _ai != null && _body != null && _ai.IsOwnedAndValid && !_ai.IsFaulted &&
        _ai.IsClimbingWith(this) && _body.isKinematic;

    /// <summary>Takes the ladder lease on <paramref name="ai"/>'s own body.
    /// Null when the body cannot be leased: missing, faulted, unowned, already
    /// leased, or without its own rigidbody.</summary>
    internal static WorkerClimbMotor? TrySuspend(
        ForemanWorkerAI ai, WorkerKey worker, Guid bodyEpoch, Func<WorkPoint, WorkPoint, bool> clear)
    {
        if (ai == null || clear == null || worker.IsEmpty || bodyEpoch == Guid.Empty || ai.IsFaulted ||
            !ai.IsOwnedAndValid)
        {
            return null;
        }

        Rigidbody body = ai.GetComponent<Rigidbody>();
        if (body == null)
        {
            return null;
        }

        var lease = new WorkerClimbMotor(ai, body, worker, bodyEpoch, clear);
        if (!ai.TryBeginClimb(lease))
        {
            return null;
        }

        lease._wasKinematic = body.isKinematic;
        lease._body.isKinematic = true;
        return lease;
    }

    public bool TryPlaceOwnBody(WorkPoint position)
    {
        if (!GroundMotorSuspended || !position.IsFinite)
        {
            return false;
        }

        Vector3 at = _body.position;
        var from = new WorkPoint(at.x, at.y, at.z);
        var target = new Vector3(position.X, position.Y, position.Z);
        if (!_clear(from, position))
        {
            return false;
        }

        _body.position = target;
        return true;
    }

    public bool TryRestore()
    {
        if (_restored)
        {
            return true;
        }

        // A body that no longer exists has nothing left to restore; holding the
        // identity for it would hold it forever.
        if (_ai == null || _body == null)
        {
            _restored = true;
            return true;
        }

        _body.isKinematic = _wasKinematic;
        _ai.EndClimb(this);
        _restored = !_ai.IsClimbingWith(this);
        return _restored;
    }
}
