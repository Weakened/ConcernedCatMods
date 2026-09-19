using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why the accounting refuses to start a pick. Zero means it does not.
/// </summary>
internal enum PickGuard
{
    /// <summary>Nothing refuses.</summary>
    None = 0,

    /// <summary>A pick is already in flight. One body picks one thing.</summary>
    Busy = 1,

    /// <summary>This source was picked and the world has not yet said so.
    ///
    /// <b>The one that stops material being minted.</b> The game's pick raises
    /// two routed messages, not one: the first drops the items, and a
    /// <i>second</i> is what finally marks the source picked. Between them the
    /// source still reports that it can be picked and the game's own guard is
    /// still open, so a second pick of the same source yields a second full
    /// load out of nothing. Nothing about the world changes in that window, so
    /// the only thing that can refuse is a record of what we just did.</summary>
    AwaitingConfirmation = 2,

    /// <summary>The request itself is malformed - no source, or an expected
    /// yield that is not positive.</summary>
    Malformed = 3,
}

/// <summary>The part of a pick that can be decided without the game: whether it
/// may start, what may be counted towards it, and what it finally took (#381).
///
/// <b>Why this is a separate, game-free type.</b> Everything the port does that
/// can mint material is here, because the port itself binds Unity and cannot be
/// exercised by any test in this repository. Each of the three rules below was a
/// real defect found by review in the first version of the port, and each is now
/// a decision over values that a test can drive:
///
/// <b>One.</b> A source is refused until the world confirms it was picked - see
/// <see cref="PickGuard.AwaitingConfirmation"/>.
///
/// <b>Two.</b> What a finished pick took is handed back once, by
/// <see cref="Finish"/>, and the counter is cleared with it. The first version
/// left it readable, so a refused start reported the previous pick's count and a
/// caller crediting on it credited units it never gathered.
///
/// <b>Three.</b> Only a single unit may be credited at a time, and never more
/// than the pick expected. The game drops each unit of the main yield as its own
/// stack of one, so a larger stack arriving inside the window is somebody else's
/// - most likely a player emptying their inventory next to him. <b>Stated
/// honestly: this is a bound, not a proof of provenance.</b> A player dropping
/// single units one at a time beside a source he is picking would still be
/// counted, and nothing available to this layer can tell those apart. What the
/// bound guarantees is that the error can never exceed what the source was
/// expected to give, so a pick can be short but never generous.</summary>
internal sealed class PickAccounting
{
    private readonly Dictionary<string, bool> _awaitingConfirmation =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    private string _source = string.Empty;
    private int _expected;
    private int _taken;

    /// <summary>Whether a pick is in flight.</summary>
    public bool InFlight { get; private set; }

    /// <summary>What the pick in flight has gathered so far. Zero when none is.
    /// </summary>
    public int Taken => InFlight ? _taken : 0;

    /// <summary>How many sources are picked but unconfirmed. For a status line,
    /// and for a test to see that the record is not growing without bound.
    /// </summary>
    public int AwaitingConfirmation => _awaitingConfirmation.Count;

    /// <summary>May a pick of this source start now?</summary>
    public PickGuard MayBegin(string? sourceKey, int expectedUnits)
    {
        if (string.IsNullOrEmpty(sourceKey) || expectedUnits <= 0)
        {
            return PickGuard.Malformed;
        }

        if (InFlight)
        {
            return PickGuard.Busy;
        }

        return _awaitingConfirmation.ContainsKey(sourceKey!)
            ? PickGuard.AwaitingConfirmation
            : PickGuard.None;
    }

    /// <summary>Records that a pick of this source has been made. Called only
    /// after the interaction actually happened, so the record and the world
    /// agree about what was done.</summary>
    public void Began(string sourceKey, int expectedUnits)
    {
        _source = sourceKey ?? string.Empty;
        _expected = expectedUnits > 0 ? expectedUnits : 0;
        _taken = 0;
        InFlight = true;
        _awaitingConfirmation[_source] = true;
    }

    /// <summary>Offers one gathered stack. Answers whether it counts.</summary>
    public bool MayCredit(int stackSize)
    {
        if (!InFlight || stackSize != 1 || _taken >= _expected)
        {
            return false;
        }

        _taken++;
        return true;
    }

    /// <summary>Takes back a credit the world then refused.
    ///
    /// The port asks before it takes, because taking something it would not
    /// count is quietly removing it from the world. When the take itself
    /// refuses - a full inventory, an item already gone - the credit has to come
    /// back off, or a shortfall becomes invisible and the pick reports units
    /// still lying on the ground.</summary>
    public void Uncredit()
    {
        if (InFlight && _taken > 0)
        {
            _taken--;
        }
    }

    /// <summary>Whether the pick has gathered everything it expected.</summary>
    public bool IsComplete => InFlight && _taken >= _expected;

    /// <summary>Ends the pick and hands back what it took, once. The counter is
    /// cleared with it, so nothing can read a finished pick's total again and
    /// mistake it for a live one.</summary>
    public int Finish()
    {
        int took = _taken;
        _taken = 0;
        _expected = 0;
        _source = string.Empty;
        InFlight = false;
        return took;
    }

    /// <summary>The world has said this source is picked, so the record of it is
    /// no longer needed. Called with whatever the port observed, so a source
    /// that was never ours is simply not there to forget.</summary>
    public void Confirmed(string? sourceKey)
    {
        if (!string.IsNullOrEmpty(sourceKey))
        {
            _awaitingConfirmation.Remove(sourceKey!);
        }
    }

    /// <summary>Forgets everything, because the world it described has gone. The
    /// unconfirmed record goes with it: the sources it named do not exist in the
    /// next world, and a record that outlived them would refuse picks of
    /// whatever inherited their names.</summary>
    public void ForgetWorld()
    {
        _awaitingConfirmation.Clear();
        Finish();
    }

    /// <summary>The source a pick is in flight for, or empty.</summary>
    public string InFlightSource => InFlight ? _source : string.Empty;
}
