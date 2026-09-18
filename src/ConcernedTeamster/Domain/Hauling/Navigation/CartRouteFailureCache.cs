using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>One haul leg that did not get where it was going, as remembered: the
/// target it was for, and - when the cart physically stuck, rather than the leg
/// simply failing - where the cart was held and which way it was going.
/// </summary>
internal readonly struct CartRouteSetback
{
    public CartRouteSetback(
        WorkPoint target, bool hasPlace, WorkPoint place, float headingX, float headingZ,
        HaulAttentionReason reason, float until, float pauseSeconds, int strikes)
    {
        Target = target;
        HasPlace = hasPlace;
        Place = place;
        HeadingX = headingX;
        HeadingZ = headingZ;
        Reason = reason;
        Until = until;
        PauseSeconds = pauseSeconds;
        Strikes = strikes;
    }

    public WorkPoint Target { get; }

    /// <summary>Whether the cart was held somewhere, so the place and heading
    /// mean something.</summary>
    public bool HasPlace { get; }

    /// <summary>Where the cart was held.</summary>
    public WorkPoint Place { get; }

    /// <summary>The way it was going, flat and of unit length.</summary>
    public float HeadingX { get; }

    public float HeadingZ { get; }

    public HaulAttentionReason Reason { get; }

    /// <summary>Until when, on the caller's clock, it is avoided.</summary>
    public float Until { get; }

    public float PauseSeconds { get; }

    /// <summary>How many times in a row the same trouble struck, counting this.
    /// </summary>
    public int Strikes { get; }

    public bool IsActiveAt(float now) => now < Until;
}

/// <summary>What Gunnar has learned lately about ways a loaded cart does not go,
/// so a blocked leg is never retried into the same obstruction (CART-06).
///
/// The same idea as the companions' walk setbacks, copied into Teamster (the
/// products share no game-facing code): a navmesh route is deterministic, so
/// asking again from nearly the same place gives the same way through the same
/// snag. What is learned is "the cart sticks there, going that way": a route is
/// refused while it passes that place heading the same way, or ends at a target
/// that recently failed. Passing the place another way, or getting round it by a
/// corrected line, is still allowed - that is how a recovery finds another way.
///
/// The same trouble again is avoided for longer: a minute the first time,
/// doubling to five, remembered for ten more so that "again" is recognised. The
/// numbers are the companions', proven in game against a wall the navmesh did
/// not know about; nothing is permanent, because the player may clear the way.
/// Positions and times only; the clock is the caller's, in seconds.</summary>
internal sealed class CartRouteFailureCache
{
    public const float FirstPauseSeconds = 60f;

    public const float LongestPauseSeconds = 300f;

    public const float RecallSeconds = 600f;

    public const int Capacity = 8;

    /// <summary>No further above or below than this is the same place.</summary>
    public const float SameLevelMetres = 1.5f;

    /// <summary>Headings within sixty degrees are the same way.</summary>
    public const float SameWayCosine = 0.5f;

    private const float ShortestHeadingMetres = 0.05f;

    private readonly List<CartRouteSetback> _setbacks = new List<CartRouteSetback>();

    /// <param name="samePlaceMetres">A route passing this close, flat, to where
    /// the cart was held goes through the same place: the verified corridor's
    /// half width, so a route the cart's own body would drag over that spot.
    /// </param>
    /// <param name="sameTargetMetres">Two targets this close, flat, are the same
    /// parking spot: the cart standing on either would overlap the other.
    /// </param>
    public CartRouteFailureCache(float samePlaceMetres, float sameTargetMetres)
    {
        if (!(samePlaceMetres > 0f) || !(sameTargetMetres > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(samePlaceMetres), "Matching distances must be positive.");
        }

        SamePlaceMetres = samePlaceMetres;
        SameTargetMetres = sameTargetMetres;
    }

    public float SamePlaceMetres { get; }

    public float SameTargetMetres { get; }

    public IReadOnlyList<CartRouteSetback> Remembered => _setbacks;

    /// <summary>A cache matched to one cart: both radii are its corridor's half
    /// width - the cart's half width plus its side clearance. Closer than that,
    /// the cart's own body covers the remembered spot. It stays well under the
    /// cart length that staging candidates are spaced by, so a neighbouring
    /// candidate is never mistaken for a failed one.</summary>
    public static CartRouteFailureCache For(CartFootprint footprint, HaulLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        float halfCorridor = Math.Max(0.1f, (footprint.WidthMetres * 0.5f) + limits.SideClearanceMetres);
        return new CartRouteFailureCache(halfCorridor, halfCorridor);
    }

    /// <summary>Remembers a leg to <paramref name="target"/> that failed without
    /// the cart being held anywhere in particular.</summary>
    public CartRouteSetback Remember(WorkPoint target, HaulAttentionReason reason, float now)
    {
        return Add(target, false, default, 0f, 0f, reason, now);
    }

    /// <summary>Remembers a leg to <paramref name="target"/> whose cart was held
    /// at <paramref name="stuckAt"/> while going towards
    /// <paramref name="headingTowards"/>. With no usable heading only the target
    /// is avoided.</summary>
    public CartRouteSetback Remember(
        WorkPoint target, WorkPoint stuckAt, WorkPoint headingTowards, HaulAttentionReason reason, float now)
    {
        if (!TryHeading(stuckAt, headingTowards, out float headingX, out float headingZ) &&
            !TryHeading(stuckAt, target, out headingX, out headingZ))
        {
            return Add(target, false, default, 0f, 0f, reason, now);
        }

        return Add(target, true, stuckAt, headingX, headingZ, reason, now);
    }

    /// <summary>Whether a leg ending at <paramref name="target"/> failed lately.
    /// </summary>
    public bool RefusesSpot(WorkPoint target, float now)
    {
        foreach (CartRouteSetback setback in _setbacks)
        {
            if (setback.IsActiveAt(now) &&
                setback.Target.HorizontalDistanceTo(target) <= SameTargetMetres &&
                setback.Target.VerticalDistanceTo(target) <= SameLevelMetres)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="route"/>, corner to corner, passes a
    /// place where a cart stuck lately, heading the way it stuck.</summary>
    public bool RefusesRoute(IReadOnlyList<WorkPoint> route, float now)
    {
        if (route == null || route.Count < 2)
        {
            return false;
        }

        foreach (CartRouteSetback setback in _setbacks)
        {
            if (!setback.HasPlace || !setback.IsActiveAt(now))
            {
                continue;
            }

            for (int leg = 0; leg + 1 < route.Count; leg++)
            {
                if (PassesTheSameWay(route[leg], route[leg + 1], setback))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Forgets everything: a person has looked at the problem, or a new
    /// world has loaded.</summary>
    public void Clear()
    {
        _setbacks.Clear();
    }

    /// <summary>A minute the first time, doubling each time after, up to
    /// <see cref="LongestPauseSeconds"/>.</summary>
    public static float PauseFor(int strikes)
    {
        float pause = FirstPauseSeconds;
        for (int strike = 1; strike < strikes && pause < LongestPauseSeconds; strike++)
        {
            pause *= 2f;
        }

        return Math.Min(pause, LongestPauseSeconds);
    }

    private CartRouteSetback Add(
        WorkPoint target, bool hasPlace, WorkPoint place, float headingX, float headingZ,
        HaulAttentionReason reason, float now)
    {
        _setbacks.RemoveAll(setback => now >= setback.Until + RecallSeconds);

        int strikes = 0;
        for (int index = _setbacks.Count - 1; index >= 0; index--)
        {
            CartRouteSetback before = _setbacks[index];
            bool sameTarget = before.Target.HorizontalDistanceTo(target) <= SameTargetMetres &&
                before.Target.VerticalDistanceTo(target) <= SameLevelMetres;
            bool samePlace = hasPlace && before.HasPlace &&
                before.Place.HorizontalDistanceTo(place) <= SamePlaceMetres &&
                before.Place.VerticalDistanceTo(place) <= SameLevelMetres &&
                (before.HeadingX * headingX) + (before.HeadingZ * headingZ) >= SameWayCosine;
            if (!sameTarget && !samePlace)
            {
                continue;
            }

            strikes = Math.Max(strikes, before.Strikes);
            _setbacks.RemoveAt(index);
        }

        strikes++;
        float pause = PauseFor(strikes);
        var setback = new CartRouteSetback(target, hasPlace, place, headingX, headingZ, reason, now + pause, pause, strikes);

        while (_setbacks.Count >= Capacity)
        {
            int soonest = 0;
            for (int index = 1; index < _setbacks.Count; index++)
            {
                if (_setbacks[index].Until < _setbacks[soonest].Until)
                {
                    soonest = index;
                }
            }

            _setbacks.RemoveAt(soonest);
        }

        _setbacks.Add(setback);
        return setback;
    }

    private static bool TryHeading(WorkPoint from, WorkPoint to, out float headingX, out float headingZ)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float length = CartRouteGeometry.FlatLength(dx, dz);
        if (length < ShortestHeadingMetres || float.IsNaN(length))
        {
            headingX = 0f;
            headingZ = 0f;
            return false;
        }

        headingX = dx / length;
        headingZ = dz / length;
        return true;
    }

    /// <summary>Whether going straight from <paramref name="from"/> to
    /// <paramref name="to"/> takes the cart through the place it stuck, the way
    /// it stuck: heading within sixty degrees, passing within
    /// <see cref="SamePlaceMetres"/> of it, at that level. A leg that ends before
    /// the place or starts well beyond it does not go through it.</summary>
    private bool PassesTheSameWay(WorkPoint from, WorkPoint to, CartRouteSetback setback)
    {
        if (!TryHeading(from, to, out float alongX, out float alongZ) ||
            (alongX * setback.HeadingX) + (alongZ * setback.HeadingZ) < SameWayCosine)
        {
            return false;
        }

        float length = from.HorizontalDistanceTo(to);
        float offsetX = setback.Place.X - from.X;
        float offsetZ = setback.Place.Z - from.Z;
        float ahead = (offsetX * alongX) + (offsetZ * alongZ);
        if (ahead > length + SamePlaceMetres || ahead < -SamePlaceMetres)
        {
            return false;
        }

        float travelled = Math.Max(0f, Math.Min(length, ahead));
        float nearestX = from.X + (alongX * travelled);
        float nearestZ = from.Z + (alongZ * travelled);
        float sideX = setback.Place.X - nearestX;
        float sideZ = setback.Place.Z - nearestZ;
        if ((sideX * sideX) + (sideZ * sideZ) > SamePlaceMetres * SamePlaceMetres)
        {
            return false;
        }

        float nearestY = length > 1e-4f ? from.Y + ((to.Y - from.Y) * (travelled / length)) : from.Y;
        return Math.Abs(nearestY - setback.Place.Y) <= SameLevelMetres;
    }
}
