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
    /// <summary>How long an unconfirmed source stays refused when nothing ever
    /// confirms it.
    ///
    /// <b>Why there has to be one.</b> Confirmation arrives from the world, and
    /// there are two ways it never does: the second routed message lands after
    /// the pick has already finished (its key then leaks for the life of the
    /// world load), and a source that <i>respawns</i> is pickable again yet
    /// still carries a record saying it is not - refused for ever. Without a
    /// horizon the record grows without bound and retires sources permanently.
    ///
    /// <b>Why it cannot mint.</b> The window this record exists to close is
    /// <i>one routed-RPC turn</i> - the following frame, in the only
    /// configuration this runtime may run in (host, no peers) - and the port's
    /// gather window is two seconds. A minute is that window with four orders
    /// of magnitude of room. And expiry alone never picks anything: the port
    /// re-reads <c>CanBePicked()</c> first, so a source whose confirmation did
    /// arrive is refused by the world itself whatever this record says.
    ///
    /// <b>Stated honestly:</b> if a host could stall its own RPC queue for a
    /// full minute while still running frames, an expired entry would be a
    /// source that is genuinely mid-settle. Nothing available to this layer
    /// could tell that apart from a respawn, and a game stalled that long is
    /// not one anybody is playing.</summary>
    internal const float SettleHorizonSeconds = 60f;

    /// <summary>The most unconfirmed sources held at once. A ceiling under the
    /// horizon above, for a caller that keeps no clock.
    ///
    /// Eviction is oldest-first, and one body picks one thing: the entry that
    /// could still be inside its settle window is the newest, never the oldest.
    /// At this size an evicted key was recorded more than a hundred picks ago,
    /// against a window one frame wide.</summary>
    internal const int MostUnconfirmedSources = 128;

    /// <summary>When a pick was recorded: the caller's clock, for the horizon,
    /// and the order it was recorded in, for the ceiling. The order is kept
    /// separately because a caller that keeps no clock stamps every record with
    /// the same time, and "oldest" has to mean something even then.</summary>
    private readonly struct Recorded
    {
        public Recorded(float at, long order)
        {
            At = at;
            Order = order;
        }

        public float At { get; }

        public long Order { get; }
    }

    private readonly Dictionary<string, Recorded> _awaitingConfirmation =
        new Dictionary<string, Recorded>(StringComparer.Ordinal);

    private long _recordedSoFar;

    private string _source = string.Empty;
    private int _expected;
    private int _taken;

    /// <summary>Whether a pick is in flight.</summary>
    public bool InFlight { get; private set; }

    /// <summary>What the pick in flight has gathered so far. Zero when none is.
    /// </summary>
    public int Taken => InFlight ? _taken : 0;

    /// <summary>How many sources are picked but unconfirmed. For a status line,
    /// and for a test to see that the record is not growing without bound -
    /// which it now cannot: see <see cref="SettleHorizonSeconds"/> and
    /// <see cref="MostUnconfirmedSources"/>.</summary>
    public int AwaitingConfirmation => _awaitingConfirmation.Count;

    /// <summary>May a pick of this source start now?</summary>
    /// <param name="nowSeconds">The caller's clock, for the settle horizon. A
    /// caller that keeps none leaves it at zero and nothing ever expires, which
    /// is the refusing direction rather than the minting one.</param>
    public PickGuard MayBegin(string? sourceKey, int expectedUnits, float nowSeconds = 0f)
    {
        if (string.IsNullOrEmpty(sourceKey) || expectedUnits <= 0)
        {
            return PickGuard.Malformed;
        }

        if (InFlight)
        {
            return PickGuard.Busy;
        }

        RetireSettled(nowSeconds);

        return _awaitingConfirmation.ContainsKey(sourceKey!)
            ? PickGuard.AwaitingConfirmation
            : PickGuard.None;
    }

    /// <summary>Records that a pick of this source has been made. Called only
    /// after the interaction actually happened, so the record and the world
    /// agree about what was done.</summary>
    /// <param name="nowSeconds">The caller's clock, stamped on the record so
    /// the settle horizon can retire it.</param>
    public void Began(string sourceKey, int expectedUnits, float nowSeconds = 0f)
    {
        _source = sourceKey ?? string.Empty;
        _expected = expectedUnits > 0 ? expectedUnits : 0;
        _taken = 0;
        InFlight = true;
        _awaitingConfirmation[_source] = new Recorded(nowSeconds, ++_recordedSoFar);
        EvictOldestPastTheCeiling();
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

    /// <summary>Releases the pick in flight, because the job it belonged to has
    /// gone. <b>The unconfirmed record stays.</b>
    ///
    /// <b>The defect this verb exists to close.</b> A job being cancelled is an
    /// ordinary caller event, and the port used to answer it by forgetting the
    /// world - wiping the only record standing between one source and two
    /// yields. Inside the settle window neither the source nor the game's own
    /// guard refuses, so <c>Began - Interact - Forget - Began</c> on the same
    /// source minted a second full yield out of nothing. The job ending says
    /// nothing at all about whether that source has settled: it still exists,
    /// and it is still mid-settle.</summary>
    public void ForgetJob()
    {
        Finish();
    }

    /// <summary>Forgets everything, because the world it described has gone. The
    /// unconfirmed record goes with it: the sources it named do not exist in the
    /// next world, and a record that outlived them would refuse picks of
    /// whatever inherited their names.
    ///
    /// Only a world going away may do this. A job ending is
    /// <see cref="ForgetJob"/>.</summary>
    public void ForgetWorld()
    {
        _awaitingConfirmation.Clear();
        Finish();
    }

    /// <summary>The source a pick is in flight for, or empty.</summary>
    public string InFlightSource => InFlight ? _source : string.Empty;

    /// <summary>Drops every record older than the settle horizon.
    ///
    /// <b>A clock that has gone backwards keeps its record.</b> This used to
    /// drop it, on the reasoning that a clock going backwards is a world that
    /// reloaded under us. That reasoning was wrong twice over. The house clock
    /// here is <c>Time.time</c>, which counts from process start and does not
    /// reset on a world load, so the branch would not have detected the thing
    /// it named - and a world going away is <see cref="ForgetWorld"/>'s job,
    /// which is the whole point of splitting that verb from
    /// <see cref="ForgetJob"/>. Worse, dropping a record is the *minting*
    /// direction: it is what lets a source be picked twice. An impossible
    /// elapsed time means we cannot say whether the window has passed, and the
    /// safe answer to that is to keep refusing.</summary>
    private void RetireSettled(float nowSeconds)
    {
        if (_awaitingConfirmation.Count == 0)
        {
            return;
        }

        List<string>? settled = null;
        foreach (KeyValuePair<string, Recorded> entry in _awaitingConfirmation)
        {
            float elapsed = nowSeconds - entry.Value.At;
            if (elapsed >= SettleHorizonSeconds)
            {
                (settled ??= new List<string>()).Add(entry.Key);
            }
        }

        if (settled == null)
        {
            return;
        }

        for (int index = 0; index < settled.Count; index++)
        {
            _awaitingConfirmation.Remove(settled[index]);
        }
    }

    /// <summary>Holds the record to <see cref="MostUnconfirmedSources"/>,
    /// oldest-recorded first and never the pick in flight.
    ///
    /// <b>Why this cannot forget a source that is still mid-settle.</b> One
    /// body picks one thing, so records are made one at a time and in order,
    /// and the one that could still be inside its window is the one just made.
    /// Eviction takes the <i>lowest</i> order there is, which at this ceiling
    /// was recorded more than a hundred picks ago - against a window that
    /// closes on the next routed-RPC turn.</summary>
    private void EvictOldestPastTheCeiling()
    {
        while (_awaitingConfirmation.Count > MostUnconfirmedSources)
        {
            string oldest = string.Empty;
            long oldestOrder = 0L;
            bool found = false;

            foreach (KeyValuePair<string, Recorded> entry in _awaitingConfirmation)
            {
                if (string.Equals(entry.Key, _source, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!found || entry.Value.Order < oldestOrder)
                {
                    oldest = entry.Key;
                    oldestOrder = entry.Value.Order;
                    found = true;
                }
            }

            if (!found)
            {
                return;
            }

            _awaitingConfirmation.Remove(oldest);
        }
    }
}
