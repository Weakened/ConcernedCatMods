using System;
using System.Collections.Generic;

namespace TheConcernedCat.Workers.Traversal;

/// <summary>The game-free implementation of the traversal seam: the one a worker
/// runtime drives, and the one the tests exercise.
///
/// It carries an actor along a published link and reports where it has got to.
/// It does not move anything — it has no idea what a body is. The runtime reads
/// <see cref="TraversalProgress"/> and puts its own body there, which is what
/// keeps every rule about how a body may be moved inside the product that owns
/// the body.
///
/// <b>The player does not use this one.</b> The player's climb controller
/// implements <see cref="ILadderTraversal"/> itself, over a live character, with
/// the ladder domain's own geometry and safety checks. Two implementations of
/// one seam is the point of decision L6; a shared implementation would drag the
/// player's climb into worker code, which is the thing the seam exists to
/// prevent.
///
/// <b>Nothing here throws at a caller that is mid-traversal.</b> A stale handle,
/// a retired link, a nonsense delta and a double exit all answer rather than
/// throw, because the one unforgivable outcome is a body left hanging on
/// something that is no longer there.</summary>
internal sealed class LinkTraversal : ILadderTraversal
{
    private const float AtAnEnd = 0.001f;

    private readonly TraversalLinkNetwork _network;
    private readonly TraversalCapabilities _capabilities;
    private readonly TraversalLimits _limits;
    private readonly Dictionary<TraversalHandle, float> _inFlight = new Dictionary<TraversalHandle, float>();

    public LinkTraversal(
        TraversalLinkNetwork network,
        TraversalCapabilities capabilities,
        TraversalLimits? limits = null)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _limits = (limits ?? TraversalLimits.Default).Validate();
    }

    /// <summary>How many traversals this runtime is holding. A runtime shutting
    /// down with a non-zero count has left somebody on a ladder.</summary>
    public int ActiveCount => _inFlight.Count;

    public TraversalVerdict CanTraverse(in TraversalRequest request)
    {
        // The actor's own gates first: none of them needs the link, and all of
        // them are refusals whether or not it still exists.
        if (!request.FeatureEnabled)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.Disabled);
        }

        if (request.AlreadyTraversing)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.AlreadyTraversing);
        }

        if (!request.ActorReady)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.ActorBusy);
        }

        // Workers only, and it fails closed: a verdict nobody computed is
        // Unspecified, which is not Granted.
        if (request.Actor.IsWorker && request.Authority != WorkAuthorityVerdict.Granted)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.NoAuthority);
        }

        if (!_network.TryGet(request.LinkId, out TraversalLink link, out _))
        {
            return TraversalVerdict.Refuse(TraversalRefusal.LinkGone);
        }

        if (!_capabilities.Can(request.Actor, link.Requires))
        {
            return TraversalVerdict.Refuse(TraversalRefusal.NotCapable);
        }

        if (!TraversalLinkNetwork.WithinHeightAllowance(request.Actor, link, _limits))
        {
            return TraversalVerdict.Refuse(TraversalRefusal.TooTall);
        }

        TraversalDirection standingAt = link.DirectionFrom(request.Standing, _limits);
        if (standingAt == TraversalDirection.Unspecified)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.NotAtAnEndpoint);
        }

        // An asked-for direction is honoured only when the actor is actually at
        // that end. Asking to go up from the top is a caller bug, and answering
        // it would drag a body through the ladder.
        if (request.Direction != TraversalDirection.Unspecified && request.Direction != standingAt)
        {
            return TraversalVerdict.Refuse(TraversalRefusal.NotAtAnEndpoint);
        }

        return TraversalVerdict.Allow(link, standingAt);
    }

    public TraversalEntry Enter(in TraversalRequest request)
    {
        TraversalVerdict verdict = CanTraverse(request);
        if (!verdict.Allowed)
        {
            return TraversalEntry.Refused(verdict);
        }

        // The generation is read here rather than carried from CanTraverse so
        // that the handle names the link as it is at the moment of taking hold.
        if (!_network.TryGet(request.LinkId, out TraversalLink link, out int generation) || generation <= 0)
        {
            return TraversalEntry.Refused(TraversalVerdict.Refuse(TraversalRefusal.LinkGone));
        }

        var handle = new TraversalHandle(link.Id, generation, request.Actor, verdict.Direction);
        _inFlight[handle] = 0f;
        return TraversalEntry.Begin(verdict, handle);
    }

    public TraversalProgress Traverse(in TraversalHandle handle, TraversalIntent intent, float deltaSeconds)
    {
        if (!handle.IsValid || !_inFlight.TryGetValue(handle, out float metres))
        {
            return TraversalProgress.Lost;
        }

        // The link is re-checked every single step. This is the branch that
        // makes "the ladder was destroyed under it" a one-step answer rather
        // than something discovered at the top.
        if (!_network.IsCurrent(handle) || !_network.TryGet(handle.LinkId, out TraversalLink link, out _))
        {
            _inFlight.Remove(handle);
            return TraversalProgress.Lost;
        }

        float span = link.Height;
        if (span <= 0f || float.IsNaN(span))
        {
            _inFlight.Remove(handle);
            return TraversalProgress.Lost;
        }

        float step = 0f;
        if (!float.IsNaN(deltaSeconds) && deltaSeconds > 0f)
        {
            if (intent == TraversalIntent.Advance)
            {
                step = _limits.TraversalSpeedMetresPerSecond * deltaSeconds;
            }
            else if (intent == TraversalIntent.Reverse)
            {
                step = -_limits.TraversalSpeedMetresPerSecond * deltaSeconds;
            }
        }

        metres = Math.Min(Math.Max(metres + step, 0f), span);
        _inFlight[handle] = metres;

        TraversalPhase phase;
        if (step > 0f)
        {
            phase = metres >= span - AtAnEnd ? TraversalPhase.ReachedFarEnd : TraversalPhase.Advancing;
        }
        else if (step < 0f)
        {
            phase = metres <= AtAnEnd ? TraversalPhase.ReturnedToStart : TraversalPhase.Reversing;
        }
        else
        {
            phase = TraversalPhase.Holding;
        }

        float fraction = metres / span;
        return new TraversalProgress(phase, fraction, metres, link.EstimatedPositionAt(handle.Direction, fraction));
    }

    public TraversalRelease Exit(in TraversalHandle handle, TraversalExitKind kind)
    {
        bool held = handle.IsValid && _inFlight.Remove(handle);

        // A handle nobody is holding releases nothing and places nothing. That
        // covers a double exit, a handle from another runtime, and an exit after
        // the traversal was already lost.
        if (!held)
        {
            return TraversalRelease.Release(TraversalExitKind.Interrupted);
        }

        // A body is placed only when the thing it is stepping off is still
        // there. A ladder that went while somebody was on it ends in a fall from
        // where they are, never in a placement computed from a link that no
        // longer exists.
        bool linkStillThere = _network.TryGet(handle.LinkId, out TraversalLink link, out _);

        switch (kind)
        {
            case TraversalExitKind.Arrived when linkStillThere:
                return TraversalRelease.Step(TraversalExitKind.Arrived, link.ExitFor(handle.Direction));

            case TraversalExitKind.TurnedBack when linkStillThere:
                return TraversalRelease.Step(TraversalExitKind.TurnedBack, link.EntryFor(handle.Direction));

            case TraversalExitKind.LetGo:
                return TraversalRelease.Release(TraversalExitKind.LetGo);

            default:
                // Arrived or TurnedBack with the link gone, Interrupted, and the
                // unspecified value a caller never means to send: all of them
                // end the same safe way.
                return TraversalRelease.Release(TraversalExitKind.Interrupted);
        }
    }

    /// <summary>Let everybody go, for a reason that is not theirs: the runtime
    /// stopping, the world unloading, the setting being switched off while
    /// somebody was half way up. Returns how many were holding on.</summary>
    public int ReleaseEveryone()
    {
        int held = _inFlight.Count;
        _inFlight.Clear();
        return held;
    }
}
