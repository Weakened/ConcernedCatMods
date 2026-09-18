using System;
using System.Collections.Generic;

namespace TheConcernedCat.Workers.Traversal;

/// <summary>What an actor is able to do besides walk on the ground.
///
/// A flag set rather than a boolean because the seam is deliberately not
/// "ladders": the day a rope, a hatch or a pole becomes traversable, it is a new
/// flag here and no change anywhere else.</summary>
[Flags]
internal enum TraversalCapability
{
    /// <summary>Walks on the ground and nothing else. <b>The default for every
    /// worker</b>, and the value a capability nobody computed has: an absent
    /// grant is never a grant (`CLAUDE.md`: worker authority fails closed).
    /// </summary>
    None = 0,

    /// <summary>May climb a ladder link.</summary>
    Ladders = 1,
}

/// <summary>Who is asking to use a link.
///
/// The player and a worker are both callers of the same seam
/// (`docs/mods/concerned-foreman/LADDERS.md` decision L6), and they are gated by
/// different rules: the player's climbing is the player's own setting, and a
/// worker's is the NPC gate that has not been passed yet. So the actor says
/// which it is, and <see cref="TraversalCapabilities"/> applies the right
/// rule.</summary>
internal readonly struct TraversalActor
{
    private TraversalActor(bool isWorker, WorkerKey worker, TraversalCapability capability)
    {
        IsWorker = isWorker;
        Worker = worker;
        Capability = capability;
    }

    /// <summary>The person at the keyboard, driving their own body. Never
    /// subject to the NPC gate, because nothing here is deciding for them.
    /// </summary>
    public static TraversalActor LocalPlayer(TraversalCapability capability = TraversalCapability.Ladders) =>
        new TraversalActor(false, default, capability);

    /// <summary>A worker body owned by an opted-in worker runtime. Its
    /// capability is whatever it was granted, and nothing by default.</summary>
    public static TraversalActor OfWorker(WorkerKey worker, TraversalCapability capability = TraversalCapability.None)
    {
        if (worker.IsEmpty)
        {
            throw new ArgumentException("A worker actor needs a worker.", nameof(worker));
        }

        return new TraversalActor(true, worker, capability);
    }

    public bool IsWorker { get; }

    /// <summary>Meaningless unless <see cref="IsWorker"/>.</summary>
    public WorkerKey Worker { get; }

    /// <summary>What the caller believes this actor can do. It is a claim, not a
    /// permission: <see cref="TraversalCapabilities.Allowed"/> is what decides.
    /// </summary>
    public TraversalCapability Capability { get; }

    /// <summary>Which body this is, for telling two traversals apart. Ordinal,
    /// and never a game-assigned number.</summary>
    public string Identity => IsWorker ? Worker.Value : "player";

    public override string ToString() => IsWorker ? Worker.ToString() : "the local player";
}

/// <summary>Which actors may use which links, and the one switch that keeps NPC
/// climbing off.
///
/// Two gates, both of which must pass for a worker:
///
/// <list type="number">
/// <item><b>The setting.</b> <c>Ladders/NpcClimbing</c> is <c>false</c> by
/// default (`LADDERS.md` §8) and stays false until the NPC gate G5 is passed.
/// While it is false no worker climbs anything, however it was granted.</item>
/// <item><b>The grant.</b> Not every NPC climbs even then. A worker has
/// <see cref="TraversalCapability.None"/> until something explicitly grants it,
/// and an unknown worker is refused rather than assumed — the same fail-closed
/// direction as <see cref="WorkAuthorityPolicy"/>.</item>
/// </list>
///
/// The player passes through both: their climbing is governed by
/// <c>Ladders/Enabled</c>, which is not this class's business. Putting the
/// player through the NPC switch would be a bug that turns a setting about
/// workers into a setting about the person playing.</summary>
internal sealed class TraversalCapabilities
{
    private readonly Dictionary<string, TraversalCapability> _granted =
        new Dictionary<string, TraversalCapability>(StringComparer.Ordinal);

    public TraversalCapabilities(bool npcClimbingEnabled = false)
    {
        NpcClimbingEnabled = npcClimbingEnabled;
    }

    /// <summary><c>Ladders/NpcClimbing</c>. False by default and false until the
    /// G5 gate says otherwise.</summary>
    public bool NpcClimbingEnabled { get; }

    public int GrantCount => _granted.Count;

    /// <summary>Let this worker use these kinds of link. Additive: granting
    /// twice adds, it does not replace, so two runtimes cannot silently take a
    /// capability away from each other.</summary>
    public void Grant(WorkerKey worker, TraversalCapability capability)
    {
        if (worker.IsEmpty)
        {
            throw new ArgumentException("A grant needs a worker.", nameof(worker));
        }

        _granted.TryGetValue(worker.Value, out TraversalCapability existing);
        _granted[worker.Value] = existing | capability;
    }

    /// <summary>Take a capability away again — a worker retired, or a runtime
    /// shutting down. Revoking something never granted is not an error.</summary>
    public void Revoke(WorkerKey worker, TraversalCapability capability)
    {
        if (worker.IsEmpty || !_granted.TryGetValue(worker.Value, out TraversalCapability existing))
        {
            return;
        }

        TraversalCapability left = existing & ~capability;
        if (left == TraversalCapability.None)
        {
            _granted.Remove(worker.Value);
            return;
        }

        _granted[worker.Value] = left;
    }

    /// <summary>What this actor may actually do here and now, after both gates.
    /// </summary>
    public TraversalCapability Allowed(in TraversalActor actor)
    {
        if (!actor.IsWorker)
        {
            return actor.Capability;
        }

        if (!NpcClimbingEnabled)
        {
            return TraversalCapability.None;
        }

        if (actor.Worker.IsEmpty || !_granted.TryGetValue(actor.Worker.Value, out TraversalCapability granted))
        {
            return TraversalCapability.None;
        }

        // The grant is the ceiling and the caller's claim is the floor: a
        // runtime that has temporarily dropped a capability (a worker carrying
        // something in both hands) may not have it handed back by the table.
        return granted & actor.Capability;
    }

    public bool Can(in TraversalActor actor, TraversalCapability capability) =>
        capability != TraversalCapability.None && (Allowed(actor) & capability) == capability;

    /// <summary>One sentence about why an actor cannot use a kind of link.
    /// Written for a log, not for a player: no player is ever shown a refusal
    /// about a worker's capabilities.</summary>
    public string Explain(in TraversalActor actor, TraversalCapability capability)
    {
        if (Can(actor, capability))
        {
            return actor + " may use " + capability + " links.";
        }

        if (!actor.IsWorker)
        {
            return "The player has not been given " + capability + " traversal.";
        }

        if (!NpcClimbingEnabled)
        {
            return "NPC climbing is off (Ladders/NpcClimbing); no worker climbs anything.";
        }

        return actor + " has not been granted " + capability + " traversal.";
    }
}
