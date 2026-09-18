using System;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>What a worker should be seen holding, decided from the ledger and
/// nothing else (CF-SET-007, #284).
///
/// <b>Why this is a type and not two lines in the adapter.</b> The issue's first
/// acceptance criterion is that what is visible matches what is reserved and is
/// "never a decoration that disagrees with the ledger". The only way to be sure
/// of that is for the decision to have exactly one input — the ledger's own
/// count of what is in <see cref="CustodyPlace.Worker"/> — and to be testable
/// without a game. An adapter that decided for itself, from an inventory scan or
/// from what it last put in his hand, would be a second source of truth, and the
/// two would drift the first time a transfer failed part-way.
///
/// Vanilla has no "carry" concept, so the presentation is one item in a hand.
/// A worker carrying two resources therefore cannot show both, which makes the
/// choice a real decision rather than a formality.</summary>
internal readonly struct CarriedDisplay : IEquatable<CarriedDisplay>
{
    private CarriedDisplay(CollectedResource resource, int count)
    {
        Resource = resource;
        Count = count;
    }

    /// <summary>What he should be holding, or
    /// <see cref="CollectedResource.Unspecified"/> for empty hands.</summary>
    public CollectedResource Resource { get; }

    /// <summary>How much of it the ledger says he has. Zero when nothing should
    /// be shown. Carried so a diagnostic can print what the hand is claiming
    /// without asking the ledger a second time and getting a different
    /// answer.</summary>
    public int Count { get; }

    public bool ShowsSomething => Resource != CollectedResource.Unspecified && Count > 0;

    /// <summary>Empty hands. The answer whenever the ledger says he carries
    /// nothing, and the answer whenever anything at all is unclear — an
    /// unreadable view, an order that has ended, a count that makes no sense.
    /// Showing nothing is always honest; showing the wrong thing is not.
    /// </summary>
    public static CarriedDisplay Nothing => default;

    /// <summary>What the worker should be holding for this order.
    ///
    /// <b>The rule: the most of one thing.</b> Ties go to the lower enum value,
    /// which makes the choice deterministic rather than dependent on the order
    /// the resources happen to be asked about — a worker whose hand flickered
    /// between a stone and a log every time the ledger revision changed would be
    /// worse than one holding neither.
    ///
    /// A negative count is treated as no count: the ledger returning one is a
    /// bug somewhere else, and the honest presentation of a bug is empty hands
    /// rather than a hand holding minus three stone.</summary>
    public static CarriedDisplay For(IMaterialCustodyView? view, OrderId order)
    {
        if (view == null)
        {
            return Nothing;
        }

        CollectedResource best = CollectedResource.Unspecified;
        int bestCount = 0;

        foreach (CollectedResource resource in CollectedResources.All)
        {
            int count;
            try
            {
                count = view.CountAt(order, CustodyPlace.Worker, resource);
            }
            catch (Exception)
            {
                // A view that throws is a view that cannot be believed. It is
                // not worth taking a presentation pass down for, and it is
                // certainly not worth guessing from.
                return Nothing;
            }

            if (count > bestCount)
            {
                best = resource;
                bestCount = count;
            }
        }

        return bestCount > 0 ? new CarriedDisplay(best, bestCount) : Nothing;
    }

    public bool Equals(CarriedDisplay other) => Resource == other.Resource && Count == other.Count;

    public override bool Equals(object? obj) => obj is CarriedDisplay other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Resource * 397) ^ Count;
        }
    }

    public override string ToString() =>
        ShowsSomething ? Count.ToString() + " " + Resource : "nothing";
}
