using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What happened at one step of a collection job.
///
/// <b>The sequence is not ours.</b> Which target is next, how many trips a job
/// takes, and when it has to be planned again are the shared runtime's job
/// driver's. What is here is the other half: what became of one step once Gunnar
/// arrived, and where the material physically went - which the driver
/// deliberately does not know, because material accounting is a role's.</summary>
internal enum StopOutcome
{
    /// <summary>Nobody said. Read as an interruption, because a step that
    /// cannot say what it did is not a step that can be assumed to have
    /// worked.</summary>
    Unspecified = 0,

    /// <summary>He took it, and the material is his.</summary>
    Taken = 1,

    /// <summary>It was not there any more. The material he is already carrying
    /// stays carried, and nothing is owed for this one.</summary>
    Gone = 2,

    /// <summary>He could not get to it after bounded local recovery. Still owed,
    /// so the next round plans for it again.
    ///
    /// <b>This is where a portal would go, and does not.</b> He walks. A stop he
    /// cannot walk to is reported, never reached another way, and a job whose
    /// stops are all like this ends in
    /// <see cref="CollectionAttention.NoRouteOnFoot"/>.</summary>
    Unreachable = 3,

    /// <summary>The place refused when he got there - a ward, an owner, a
    /// location the world built.</summary>
    Refused = 4,

    /// <summary>Something ended the round: authority, the body, the world, the
    /// player.</summary>
    Interrupted = 5,
}

/// <summary>What one step did, and where what he took ended up.</summary>
internal readonly struct StopResult
{
    public StopResult(StopOutcome outcome, CargoPlace landed = CargoPlace.Carried)
    {
        Outcome = outcome;
        Landed = landed;
    }

    public StopOutcome Outcome { get; }

    /// <summary>On his back, or stowed in the assigned cart. Asked rather than
    /// assumed, because the cart is the only reason a trip can be bigger than he
    /// is, and a ledger that recorded everything as carried would have nothing
    /// to account for when the cart is destroyed.</summary>
    public CargoPlace Landed { get; }

    public static StopResult Took(CargoPlace landed = CargoPlace.Carried) =>
        new StopResult(StopOutcome.Taken, landed);

    public static StopResult Did(StopOutcome outcome) => new StopResult(outcome);
}

/// <summary>What a deposit did.</summary>
internal enum DepositOutcome
{
    Unspecified = 0,

    /// <summary>Everything he carried went in.</summary>
    Deposited = 1,

    /// <summary>Some of it went in and the rest stays with him. Never
    /// deleted.</summary>
    PartlyDeposited = 2,

    /// <summary>The container refused, or he could not get to it. He keeps what
    /// he has.</summary>
    Refused = 3,

    /// <summary>Whether it went in could not be established. Nothing is written
    /// off and nothing is compensated; the job stops and a person looks.
    /// </summary>
    Uncertain = 4,
}

/// <summary>What one deposit actually moved, measured on both sides.</summary>
internal readonly struct DepositResult
{
    public DepositResult(DepositOutcome outcome, IReadOnlyList<KeyValuePair<string, int>>? moved, string detail = "")
    {
        Outcome = outcome;
        Moved = moved ?? Array.Empty<KeyValuePair<string, int>>();
        Detail = detail ?? string.Empty;
    }

    public DepositOutcome Outcome { get; }

    /// <summary>Per item, how many units were <b>observed</b> to arrive - never
    /// how many were intended to.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Moved { get; }

    public string Detail { get; }
}

/// <summary>Why a collection job is asking for a person. Zero means it is not.
///
/// The shared runtime's driver says a job stopped and hands back a sentence in
/// the register of evidence; this is Gunnar's own word for the same fact, which
/// is what a status line and a log entry are keyed on.</summary>
internal enum CollectionAttention
{
    /// <summary>Nothing needs attention.</summary>
    Unspecified = 0,

    /// <summary>The work area could not be resolved - no provider, an unreadable
    /// shape, a deleted designation. Refused; never replaced by a radius nobody
    /// asked for.</summary>
    AreaUnavailable = 1,

    /// <summary>Nothing in the area could be reached on foot after bounded local
    /// recovery. He does not teleport, with or without a cart, and never to
    /// recover a stuck route.</summary>
    NoRouteOnFoot = 2,

    /// <summary>The destination container refused, or could not be reached, so
    /// he is holding what he gathered.</summary>
    DestinationUnavailable = 3,

    /// <summary>Nothing he may use holds what the job needs.</summary>
    ShortOfMaterial = 4,

    /// <summary>The assigned cart was destroyed. The job stops where it is, the
    /// accounting is preserved, and nothing replaces what it held.</summary>
    CartDestroyed = 5,

    /// <summary>Authority, the world or the body went away under him.</summary>
    Interrupted = 6,

    /// <summary>The job took more rounds than it is allowed and still has work
    /// left. Not a failure: a very large job, worked as far as one order
    /// goes.</summary>
    MoreThanOneOrdersWorth = 7,
}

/// <summary>Gunnar's material accounting across one collection job: what he
/// took, where it is, and what a destroyed cart did to it (#381).
///
/// <b>Why it is separate from the job driver.</b> The shared runtime sequences
/// the work and deliberately knows nothing about what the work yields - it has
/// no idea what a cart is or what stone is for. Conservation is therefore this
/// product's to keep, and this is the smallest thing that keeps it: every step
/// the driver hands back is recorded here by name, so a retry after an
/// interruption is answered rather than applied twice, and
/// <see cref="CargoLedger.IsConserved"/> can be asked after every one.</summary>
internal sealed class CollectionAccount
{
    private readonly CargoLedger _ledger = new CargoLedger();

    /// <summary>The ledger. Handed out so a test and a status line can ask it
    /// questions; nothing outside this type writes to it during a job.</summary>
    public CargoLedger Ledger => _ledger;

    public int Serviced { get; private set; }

    public int Skipped { get; private set; }

    /// <summary>Records what became of one step.</summary>
    /// <param name="step">The driver's own name for this step, which is unique
    /// within the job and stable across a retry.</param>
    public CargoOutcome Record(string step, string itemPrefab, int units, StopResult result)
    {
        switch (result.Outcome)
        {
            case StopOutcome.Taken:
                CargoOutcome taken = _ledger.Take(step, itemPrefab, units);
                if (taken == CargoOutcome.Recorded && result.Landed == CargoPlace.InCart)
                {
                    // Straight into the cart. Recorded as a move rather than as
                    // a different kind of take, so the total never depends on
                    // where it landed.
                    _ledger.Move(step + ":stow", itemPrefab, units, CargoPlace.Carried, CargoPlace.InCart);
                }

                if (taken == CargoOutcome.Recorded)
                {
                    Serviced++;
                }

                return taken;
            case StopOutcome.Gone:
            case StopOutcome.Refused:
            case StopOutcome.Unreachable:
                Skipped++;
                return CargoOutcome.Unspecified;
            default:
                return CargoOutcome.Unspecified;
        }
    }

    /// <summary>Everything he is holding, on his back and in the cart, in a
    /// deterministic order. What one trip to the chest is for.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Load()
    {
        IReadOnlyList<KeyValuePair<string, int>> back = _ledger.Holding(CargoPlace.Carried);
        IReadOnlyList<KeyValuePair<string, int>> cart = _ledger.Holding(CargoPlace.InCart);
        if (cart.Count == 0)
        {
            return back;
        }

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < back.Count; index++)
        {
            totals[back[index].Key] = back[index].Value;
        }

        for (int index = 0; index < cart.Count; index++)
        {
            totals.TryGetValue(cart[index].Key, out int already);
            totals[cart[index].Key] = already + cart[index].Value;
        }

        var combined = new List<KeyValuePair<string, int>>(totals.Count);
        foreach (KeyValuePair<string, int> entry in totals)
        {
            combined.Add(entry);
        }

        combined.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return combined;
    }

    /// <summary>Records what a deposit moved. A refusal and an unmeasurable
    /// answer both move nothing: he still has it, or nobody knows, and writing
    /// either down as fact is how material is lost or invented.</summary>
    public void RecordDeposit(string step, DepositResult result)
    {
        if (result.Outcome == DepositOutcome.Refused || result.Outcome == DepositOutcome.Uncertain)
        {
            return;
        }

        for (int index = 0; index < result.Moved.Count; index++)
        {
            KeyValuePair<string, int> entry = result.Moved[index];
            if (entry.Value <= 0)
            {
                continue;
            }

            // Off his back first, then out of the cart. A deposit that measured
            // more than either place holds moves what it can and leaves the
            // discrepancy visible, because inventing the difference is the one
            // thing this ledger exists to make impossible.
            int fromBack = Least(entry.Value, _ledger.At(entry.Key, CargoPlace.Carried));
            if (fromBack > 0)
            {
                _ledger.Move(step + ":deposit:" + entry.Key, entry.Key, fromBack,
                    CargoPlace.Carried, CargoPlace.Delivered);
            }

            int fromCart = Least(entry.Value - fromBack, _ledger.At(entry.Key, CargoPlace.InCart));
            if (fromCart > 0)
            {
                _ledger.Move(step + ":unload:" + entry.Key, entry.Key, fromCart,
                    CargoPlace.InCart, CargoPlace.Delivered);
            }
        }
    }

    /// <summary>The cart was destroyed. Everything it held is on the ground
    /// where it stood, exactly as the game left it: nothing is re-credited,
    /// nothing is re-acquired, and the totals do not move.</summary>
    public CargoOutcome CartDestroyed(string step) => _ledger.CartDestroyed(step);

    private static int Least(int left, int right) => left < right ? left : right;
}
