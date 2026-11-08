using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>The construction loop's materials, witnessed by Thorstein's shared
/// plan for the <see cref="ThorsteinWorkPlan.BuildJobId"/> job (#379).
///
/// <b>A wrapper, so the loop and the custody path are untouched.</b> The three
/// movements - a draw from the supply chest, a commit that places a piece and
/// pays for it, a put-back - each have the plan's intent written before the
/// inner materials touch anything, and its measured conclusion after. The inner
/// <see cref="IBuildMaterials"/> still journals CommitStarted / CommitFinished
/// and its transfers in the settlement record exactly as before (#398), which
/// stays the custody truth. A refused intent means the movement does not
/// happen: a draw is refused, a commit fails without placing, a put-back
/// leaves what he carries on him and says so.</summary>
internal sealed class WitnessedBuildMaterials : IBuildMaterials
{
    private readonly IBuildMaterials _inner;
    private readonly ThorsteinWorkPlan _plan;
    private readonly Func<NpcPlanEvidence> _evidence;
    private string _tag = string.Empty;

    internal WitnessedBuildMaterials(IBuildMaterials inner, ThorsteinWorkPlan plan, Func<NpcPlanEvidence> evidence)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!string.Equals(plan.JobId, ThorsteinWorkPlan.BuildJobId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The build witness keeps the building plan.", nameof(plan));
        }

        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
    }

    internal ThorsteinWorkPlan Plan => _plan;

    /// <summary>What he carries for the order, by item name, as the plan
    /// records it.</summary>
    internal IReadOnlyList<KeyValuePair<string, int>> Measure()
    {
        MaterialTally carried = _inner.Carried;
        var counts = new List<KeyValuePair<string, int>>(carried.Kinds);
        foreach (string item in carried.Items)
        {
            counts.Add(new KeyValuePair<string, int>(item, carried.UnitsOf(item)));
        }

        return counts;
    }

    public void BeginOrder(string tag)
    {
        _inner.BeginOrder(tag);
        _tag = tag ?? string.Empty;
        Running(out _);
    }

    public bool TrySupply(out SitePoint at, out string refusal) => _inner.TrySupply(out at, out refusal);

    public MaterialTally Carried => _inner.Carried;

    public bool IsReserved(CostedPiece piece) => _inner.IsReserved(piece);

    public BuildDraw Draw(IReadOnlyList<CostedPiece> pieces)
    {
        if (!Announce("about to draw material for " + (pieces?.Count ?? 0) + " piece(s)", out string why))
        {
            return BuildDraw.Refused(why);
        }

        BuildDraw draw;
        try
        {
            draw = _inner.Draw(pieces!);
        }
        catch (Exception)
        {
            _plan.Conclude(false, Measure(), "the draw threw", out _);
            throw;
        }

        _plan.Conclude(true, Measure(), "drew: " + draw, out _);
        return draw;
    }

    public bool Commit(CostedPiece piece, Func<bool> place, out MaterialTally spent, out string failure)
    {
        if (!Announce("about to place and pay for a piece", out string why))
        {
            spent = new MaterialTally();
            failure = why;
            return false;
        }

        bool committed;
        try
        {
            committed = _inner.Commit(piece, place, out spent, out failure);
        }
        catch (Exception)
        {
            _plan.Conclude(false, Measure(), "the commit threw", out _);
            throw;
        }

        // The settlement record's CommitStarted/CommitFinished is the custody
        // answer. A commit it could not finish is held there as a named repair,
        // and the plan says nobody can vouch for it either.
        bool established = committed || failure.Length != 0;
        if (!_plan.Conclude(established, Measure(), committed ? "placed and paid" : "not placed: " + failure, out string stopped) &&
            committed)
        {
            failure = stopped;
        }

        return committed;
    }

    public MaterialTally PutBack(out string failure)
    {
        if (!Announce("about to put back what he carries", out string why))
        {
            failure = why;
            return new MaterialTally();
        }

        MaterialTally back;
        try
        {
            back = _inner.PutBack(out failure);
        }
        catch (Exception)
        {
            _plan.Conclude(false, Measure(), "the put-back threw", out _);
            throw;
        }

        _plan.Conclude(true, Measure(), "put back " + back.Describe(), out _);
        return back;
    }

    /// <summary>The order stopped or finished. Called by the runtime, which is
    /// the one that sees the loop's ending.</summary>
    internal void Ended(bool finished, string why) => _plan.Ended(finished, Measure(), why);

    /// <summary>Authority loss or a hand-over: the plan waits as it stands.
    /// </summary>
    internal void Suspend(string why) => _plan.Suspend(why);

    private bool Announce(string note, out string why)
    {
        if (!Running(out why))
        {
            return false;
        }

        return _plan.Intend(note + " (" + _tag + ")", out why);
    }

    /// <summary>The plan runs before any movement: a fresh order begins one, a
    /// waiting or reloaded one is revalidated and continued.</summary>
    private bool Running(out string why)
    {
        if (_plan.IsRunning)
        {
            why = string.Empty;
            return true;
        }

        return _plan.Start(_evidence(), "build:" + _tag, "the supply chest", Measure(), out why);
    }
}
