using System;
using System.Globalization;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Collection;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>What was observed around one take of a drop the order's own pick
/// spawned: what the game returned, what the worker's inventory actually
/// gained, and whether the drop's network object went away.</summary>
internal readonly struct TakeObservation
{
    public TakeObservation(int expected, bool threw, bool pickupReturnedTrue, int countBefore, int countAfter, bool dropGone)
    {
        Expected = expected;
        Threw = threw;
        PickupReturnedTrue = pickupReturnedTrue;
        CountBefore = countBefore;
        CountAfter = countAfter;
        DropGone = dropGone;
    }

    /// <summary>The traced drop's stack.</summary>
    public int Expected { get; }

    public bool Threw { get; }

    /// <summary>What <c>Humanoid.Pickup</c> returned. Evidence, never the
    /// verdict.</summary>
    public bool PickupReturnedTrue { get; }

    public int CountBefore { get; }

    public int CountAfter { get; }

    /// <summary>The drop's network object is gone, which is how the game says
    /// the item left the world.</summary>
    public bool DropGone { get; }

    public int Delta => CountAfter - CountBefore;
}

/// <summary>Where a traced drop is held in the settlement record between its
/// pick and its take (#282 review). <see cref="MaterialCustodyLedger.FinishPickup"/>
/// books every drop of a <see cref="PickupOutcome.Picked"/> result at
/// <see cref="CustodyPlace.SourceGround"/> under <b>the drop's own id</b>, in
/// the source's world load, so the take that moves it to the worker has to name
/// exactly that location. Naming the source instead (its prefab and id) finds
/// nothing there, and every take is refused. One function, so the booking and
/// the take cannot drift apart again.</summary>
internal static class PickupTakes
{
    public static CustodyLocation GroundOf(SpawnedDrop drop, SourceKey source) =>
        new CustodyLocation(CustodyPlace.SourceGround, drop.SessionId, source.WorldLoadEpoch);
}

/// <summary>The rule that turns one take's observation into a transfer outcome
/// (CONTRACTS.md §5.2 step 5, §5.3).
///
/// <b>Measured, never claimed.</b> The game's own boolean is not the verdict:
/// only an inventory that gained exactly the drop's stack, with the drop gone
/// from the world and nothing thrown, is <c>Completed</c>. Only a take that
/// plainly did nothing — no throw, a refusal, no change, the drop still lying
/// there — is <c>Refused</c>. Everything else is <c>Uncertain</c>: it is never
/// retried and never compensated, and the order stops with the evidence.
///
/// It lives here, game-free, because it is the classification the whole
/// conservation argument rests on, and it is one of the places a single
/// inverted condition would silently mint or lose a unit.</summary>
internal static class TakeClassification
{
    public static TransferOutcome Classify(TakeObservation observation, out int accepted, out string evidence)
    {
        int delta = observation.Delta;
        evidence = "inventory " + observation.CountBefore.ToString(CultureInfo.InvariantCulture) + " -> " +
            observation.CountAfter.ToString(CultureInfo.InvariantCulture) + " (expected +" +
            observation.Expected.ToString(CultureInfo.InvariantCulture) + "), the game " +
            (observation.PickupReturnedTrue ? "took it" : "refused it") + ", the drop is " +
            (observation.DropGone ? "gone" : "still in the world") +
            (observation.Threw ? ", and the take threw" : string.Empty);

        if (!observation.Threw && observation.PickupReturnedTrue && observation.Expected > 0 &&
            delta == observation.Expected && observation.DropGone)
        {
            accepted = observation.Expected;
            return TransferOutcome.Completed;
        }

        if (!observation.Threw && !observation.PickupReturnedTrue && delta == 0 && !observation.DropGone)
        {
            accepted = 0;
            return TransferOutcome.Refused;
        }

        accepted = Math.Max(0, delta);
        return TransferOutcome.Uncertain;
    }
}
