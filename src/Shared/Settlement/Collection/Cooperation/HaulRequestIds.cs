using System;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Mints the haul id and the request ids one run of a cooperative
/// order sends to the haul provider.
///
/// A request id names one <i>attempt</i>. The loop keeps the message it built
/// and re-sends that exact message when a reply is lost, so a retry carries the
/// same id and payload and the provider answers AlreadySatisfied instead of
/// applying it twice; a new decision builds a new message with the next id.
///
/// Each run mixes in a nonce: after a pause or a reload the counter starts
/// again, and without the nonce "the third request of this order" would reuse
/// an id the provider may still remember with another payload. Everything stays
/// a settlement slug of at most 48 characters, whatever the order id's length.
/// </summary>
internal sealed class HaulRequestIds
{
    private const int MaxOrderPartLength = 20;

    private int _next;

    public HaulRequestIds(OrderId order, Guid runNonce)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("Request ids belong to an order.", nameof(order));
        }

        if (runNonce == Guid.Empty)
        {
            throw new ArgumentException("A run needs a nonce.", nameof(runNonce));
        }

        string orderPart = order.Value.Length <= MaxOrderPartLength
            ? order.Value
            : Fnv1a32(order.Value).ToString("x8", CultureInfo.InvariantCulture);
        HaulId = "h-" + orderPart + "-" + runNonce.ToString("N").Substring(0, 8);
    }

    /// <summary>The haul id for every leg of this run.</summary>
    public string HaulId { get; }

    public int Issued => _next;

    /// <summary>The next attempt's id: <c>{haulId}-{kind}{n}</c>.</summary>
    public string Next(char kind)
    {
        if (kind < 'a' || kind > 'z')
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A request kind is one lowercase letter.");
        }

        _next++;
        return HaulId + "-" + kind + _next.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The next transfer's request id in the custody journal, in the
    /// same run namespace.</summary>
    public RequestId NextTransfer() => new RequestId(Next('t'));

    private static uint Fnv1a32(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
