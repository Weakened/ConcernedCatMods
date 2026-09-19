using System;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>What a build order is for. One entry today, and the enum exists so
/// that the second one is a row rather than a rewrite.</summary>
internal enum BuildOrderKind
{
    /// <summary>Not an order. The default, and never buildable.</summary>
    None = 0,

    /// <summary>A small vanilla cottage: floor, enclosing walls, a doorway, a
    /// simple roof, one bed.</summary>
    Shelter = 1,
}

/// <summary>The place and the facing a player chose for a build order, and
/// whether they actually confirmed it.
///
/// <b>This type is the authority, and it is the only authority.</b> #280's rule
/// is that a piece is placed on a player's say-so and its checks are made
/// explicitly, not that an NPC finds a nice spot. So the worker never chooses a
/// site: he is handed one of these, he validates it, and he builds there or
/// refuses. A marker that nobody confirmed produces no placements at all
/// (<see cref="ShelterBlueprint.PlaceAt"/> returns an empty list for one), so a
/// default-constructed marker cannot become a cottage at the world origin.
///
/// <b>Why the confirmation is a field rather than a convention.</b> The menu
/// lets a player move the marker and turn it before committing, which means an
/// unconfirmed marker with a perfectly good position exists for as long as they
/// are deciding. "It has a position, so they must have meant it" is precisely
/// the reasoning that builds a house where somebody was still aiming.
///
/// <b>Immutable.</b> Moving a marker replaces it. A confirmed order that could
/// be moved underneath the worker is an order whose authority is a memory of
/// something the player agreed to somewhere else.</summary>
internal readonly struct BuildOrderMarker
{
    private BuildOrderMarker(BuildOrderKind kind, SitePoint at, float yaw, bool confirmed)
    {
        Kind = kind;
        At = at;
        Yaw = ShelterBlueprint.Wrap(yaw);
        IsConfirmed = confirmed;
    }

    /// <summary>What is to be built here.</summary>
    internal BuildOrderKind Kind { get; }

    /// <summary>The middle of the shelter's floor, in world space.</summary>
    internal SitePoint At { get; }

    /// <summary>Which way the order faces, in world degrees, folded into
    /// [0, 360).</summary>
    internal float Yaw { get; }

    /// <summary>Whether the player confirmed it. Nothing is built for a marker
    /// where this is false.</summary>
    internal bool IsConfirmed { get; }

    /// <summary>A marker a player is still aiming: it has a place and a facing
    /// and it authorises nothing.</summary>
    internal static BuildOrderMarker Proposed(BuildOrderKind kind, SitePoint at, float yaw) =>
        new BuildOrderMarker(kind, at, yaw, confirmed: false);

    /// <summary>The player's confirmation. The one call in this product that
    /// turns a position into permission to place real pieces.</summary>
    /// <returns>A confirmed marker, or this one unchanged when there is nothing
    /// here to confirm - an order with no kind, or a place that is not a
    /// place.</returns>
    internal BuildOrderMarker Confirm()
    {
        if (Kind == BuildOrderKind.None || !IsFinite(At) || float.IsNaN(Yaw))
        {
            return this;
        }

        return new BuildOrderMarker(Kind, At, Yaw, confirmed: true);
    }

    /// <summary>Withdrawing the confirmation. Cancelling an order keeps the
    /// marker so a player can see where it was, and takes the authority away.
    /// </summary>
    internal BuildOrderMarker Withdraw() => new BuildOrderMarker(Kind, At, Yaw, confirmed: false);

    private static bool IsFinite(SitePoint point) =>
        !float.IsNaN(point.X) && !float.IsInfinity(point.X) &&
        !float.IsNaN(point.Y) && !float.IsInfinity(point.Y) &&
        !float.IsNaN(point.Z) && !float.IsInfinity(point.Z);

    public override string ToString() => Kind == BuildOrderKind.None
        ? "<no build order>"
        : string.Format(
            CultureInfo.InvariantCulture,
            "{0} at {1} facing {2:0}{3}",
            Kind, At, Yaw, IsConfirmed ? string.Empty : " (not confirmed)");
}
