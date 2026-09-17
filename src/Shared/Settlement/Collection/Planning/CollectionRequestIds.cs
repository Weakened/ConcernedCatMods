using System;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>Mints the request ids of the collection loop's own transfers (taking
/// a drop, depositing a load): the idempotence keys of CONTRACTS.md §5.2.
///
/// <b>Unique per attempt, across reloads.</b> A retry after a refusal is a new
/// attempt with a new id; an attempt is never replayed under an old one. The
/// id carries a nonce from the world-load epoch, so a counter that restarts at
/// zero after a reload cannot collide with a row already in the journal.
///
/// <b>Always a valid settlement slug.</b> At most 48 characters of a-z, 0-9 and
/// single dashes. A long order id is shortened with a hash of the whole id, so
/// two long orders never share a prefix by truncation.</summary>
internal sealed class CollectionRequestIds
{
    private const int MaxOrderPart = 20;

    private readonly string _nonce;
    private long _counter;

    public CollectionRequestIds(Guid worldLoadEpoch)
    {
        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("Request ids belong to one world load.", nameof(worldLoadEpoch));
        }

        _nonce = worldLoadEpoch.ToString("N").Substring(0, 6);
    }

    /// <summary><c>take</c> or <c>deposit</c>; any other kind is a bug.</summary>
    public RequestId Next(OrderId order, string kind)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("A request id belongs to an order.", nameof(order));
        }

        string letter;
        switch (kind)
        {
            case "take":
                letter = "t";
                break;
            case "deposit":
                letter = "d";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Unknown transfer kind.");
        }

        _counter++;
        string value = OrderPart(order) + "-" + _nonce + "-" + letter +
            _counter.ToString(CultureInfo.InvariantCulture);
        return new RequestId(value);
    }

    private static string OrderPart(OrderId order)
    {
        string text = order.Value;
        if (text.Length <= MaxOrderPart)
        {
            return text;
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            string head = text.Substring(0, MaxOrderPart - 9).TrimEnd('-');
            return head + "-" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
