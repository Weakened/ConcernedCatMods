using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>The only thing in this library that can move a body, and it cannot
/// exist without the lease that permits the body it moves.
///
/// <b>Why a separate object and not the mind itself.</b> While the mind
/// implemented <see cref="INpcBodyMotor"/>, holding a mind and being allowed to
/// drive it were the same thing: a consumer that got a mind from anywhere - and
/// a static list handed out every role's - could walk it. An explicit interface
/// implementation would not have closed that, because a cast reaches one. So
/// the verbs moved off the mind, and the only way to an
/// <see cref="INpcBodyMotor"/> for a real body is
/// <see cref="NpcBodyMind.TryDrive"/>, which refuses without an active lease
/// for that body's own identity. This type is internal, so a consumer cannot
/// construct one either.
///
/// <b>Why the lease is re-asked on every command, not once here.</b> Same
/// reason <c>NpcBodyBuildGate</c> re-asks it: a lease proves permission existed
/// when the motor was taken, and walking a body needs permission to exist now.
/// A runtime holds a motor across many ticks, and in between the world can
/// unload, the holder's own <c>finally</c> can dispose the lease, or the
/// identity can be claimed for the other kind of body.
///
/// <b>What expiry does, and why it is not simply "refuse".</b> Vanilla never
/// clears a direction by itself, so a body that was walking when its lease
/// ended would keep walking forever if the motor only ignored further commands.
/// The first command after expiry therefore halts the body, says so once
/// through the error log, and permits nothing afterwards.</summary>
internal sealed class NpcLeasedMotor : INpcBodyMotor
{
    private readonly NpcBodyMind _mind;
    private readonly BodyLease _lease;
    private bool _expired;

    internal NpcLeasedMotor(NpcBodyMind mind, BodyLease lease)
    {
        _mind = mind;
        _lease = lease;
    }

    /// <summary>False once the lease has gone, whatever the body itself says.
    /// The interface defines this as "it must not be commanded at all", which
    /// is exactly true of a body this caller no longer holds, so a planner
    /// written against the seam already handles it.</summary>
    public bool IsOwnedAndValid => Permitted() && _mind.IsOwnedAndValid;

    public bool IsFaulted => _mind.IsFaulted;

    public bool MotorCommanded => _mind.MotorCommanded;

    public bool HasFoundPath => _mind.HasFoundPath;

    public bool TryFindPath(Vector3 point) => Permitted() && _mind.TryFindPath(point);

    public void WalkTo(Vector3 point, float arrivalRadiusMetres)
    {
        if (Permitted())
        {
            _mind.WalkTo(point, arrivalRadiusMetres);
        }
    }

    public void SteerToward(Vector3 point)
    {
        if (Permitted())
        {
            _mind.SteerToward(point);
        }
    }

    public void Face(Vector3 direction)
    {
        if (Permitted())
        {
            _mind.Face(direction);
        }
    }

    public void Halt()
    {
        if (Permitted())
        {
            _mind.Halt();
        }
    }

    public override string ToString() =>
        _lease + (_expired ? " (motor expired)" : string.Empty);

    private bool Permitted()
    {
        if (_expired)
        {
            return false;
        }

        if (_lease.IsActive)
        {
            return true;
        }

        _expired = true;

        // A body nobody holds must not keep walking. Refusing the command and
        // leaving it moving would be worse than either answer.
        try
        {
            _mind.Halt();
        }
        catch (Exception)
        {
            // Saying what happened matters more than this did.
        }

        NpcBodyMind.RaiseErrorLog("An NPC body was commanded after the lease for " + _lease.Identity
            + " ended. It has been stopped and will not be driven through this motor again.");
        return false;
    }
}
