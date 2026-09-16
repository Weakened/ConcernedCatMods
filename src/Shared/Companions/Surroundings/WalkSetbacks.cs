using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.Companions.Surroundings;

/// <summary>One walk that did not get where it was going, as he remembers it:
/// the spot it was for, and - when a step was refused, rather than the walk
/// simply taking too long - where he was stopped and which way he was going.
/// </summary>
internal readonly struct WalkSetback
{
    public WalkSetback(
        WorldPoint spot, bool hasPlace, WorldPoint stoppedAt, float headingX, float headingZ,
        float until, float pauseSeconds, int strikes)
    {
        Spot = spot;
        HasPlace = hasPlace;
        StoppedAt = stoppedAt;
        HeadingX = headingX;
        HeadingZ = headingZ;
        Until = until;
        PauseSeconds = pauseSeconds;
        Strikes = strikes;
    }

    /// <summary>Where the walk was going.</summary>
    public WorldPoint Spot { get; }

    /// <summary>Whether something physically stopped him, so the place and the
    /// heading below mean something.</summary>
    public bool HasPlace { get; }

    /// <summary>Where he stood when the step was refused.</summary>
    public WorldPoint StoppedAt { get; }

    /// <summary>The way he was going, flat and of unit length.</summary>
    public float HeadingX { get; }

    public float HeadingZ { get; }

    /// <summary>Until when, on the caller's clock, he leaves it alone.</summary>
    public float Until { get; }

    /// <summary>How long it is left alone this time.</summary>
    public float PauseSeconds { get; }

    /// <summary>How many times in a row the same trouble has stopped him,
    /// counting this one.</summary>
    public int Strikes { get; }

    public bool IsActiveAt(float now)
    {
        return now < Until;
    }
}

/// <summary>What he has learned lately about walks that do not work, so that
/// one wall does not have him get up, walk into it and sit down again for every
/// spot around the fire behind it.
///
/// The spot alone is not the lesson. Routes come from the game's navmesh, and
/// when it believes in a way through that his body does not fit - under a
/// raised wall, seen in game at <c>b35c2d8</c> - every spot beyond that gap is
/// routed through it. What he learned is "I cannot get through there, going
/// that way": so a walk is refused while it goes to a spot that failed, or
/// passes the place he was stopped heading the same way. A walk away from that
/// place, or across it another way, is still fine - that is how he gets to the
/// seat around the other side.
///
/// Being stopped by the same trouble again leaves it alone for longer, so a
/// wall the navmesh does not know about costs a bump now and then rather than
/// one a minute. Nothing here is permanent: the player may take the wall down.
///
/// Positions and times only; no engine types. The clock is the caller's,
/// in seconds.</summary>
internal sealed class WalkSetbacks
{
    /// <summary>How long a spot, and a way past a place, is left alone the
    /// first time.</summary>
    public const float FirstPauseSeconds = 60f;

    /// <summary>The longest anything is left alone. Long enough that a wall he
    /// cannot learn about stops being a routine, short enough that a wall the
    /// player took down does not keep him away for the evening.</summary>
    public const float LongestPauseSeconds = 300f;

    /// <summary>How long after its pause ends a setback still counts, so that
    /// walking into the same wall again is "again" rather than a first time.
    /// </summary>
    public const float RecallSeconds = 600f;

    /// <summary>How many setbacks he keeps. When there are more, the one whose
    /// pause ended first is forgotten.</summary>
    public const int Capacity = 8;

    /// <summary>Two spots this close, flat, are the same spot.</summary>
    public const float SameSpotMetres = 1.5f;

    /// <summary>A route passing this close, flat, to where he was stopped goes
    /// through the same place.</summary>
    public const float SamePlaceMetres = 1.0f;

    /// <summary>And no further than this above or below it: the floor above a
    /// blocked passage is not the passage.</summary>
    public const float SameLevelMetres = 1.5f;

    /// <summary>Headings within sixty degrees of each other are the same way.
    /// </summary>
    public const float SameWayCosine = 0.5f;

    /// <summary>Shorter than this, flat, a heading says nothing about which
    /// way.</summary>
    private const float ShortestHeadingMetres = 0.05f;

    /// <summary>A leg that starts more than this past where he was stopped
    /// starts on the far side of whatever stopped him - which was never more
    /// than a step and a look ahead in front of him.</summary>
    private const float PastPlaceMetres = 0.5f;

    private readonly List<WalkSetback> _setbacks = new List<WalkSetback>();

    /// <summary>Everything remembered, including setbacks whose pause is over
    /// but which still count as "again".</summary>
    public IReadOnlyList<WalkSetback> Remembered => _setbacks;

    /// <summary>Remembers a walk to <paramref name="spot"/> that did not get
    /// there without anything in particular stopping it - it took too long.
    /// Only the spot is left alone.</summary>
    public WalkSetback Remember(WorldPoint spot, float now)
    {
        return Add(spot, false, default, 0f, 0f, now);
    }

    /// <summary>Remembers a walk to <paramref name="spot"/> that was stopped at
    /// <paramref name="stoppedAt"/> while going towards
    /// <paramref name="headingTowards"/>. With no usable heading - he was
    /// stopped on the very point he was heading for - only the spot is left
    /// alone.</summary>
    public WalkSetback Remember(WorldPoint spot, WorldPoint stoppedAt, WorldPoint headingTowards, float now)
    {
        if (!TryHeading(stoppedAt, headingTowards, out float headingX, out float headingZ) &&
            !TryHeading(stoppedAt, spot, out headingX, out headingZ))
        {
            return Add(spot, false, default, 0f, 0f, now);
        }

        return Add(spot, true, stoppedAt, headingX, headingZ, now);
    }

    /// <summary>Whether a walk to <paramref name="spot"/> is one that failed
    /// lately.</summary>
    public bool RefusesSpot(WorldPoint spot, float now)
    {
        foreach (WalkSetback setback in _setbacks)
        {
            if (setback.IsActiveAt(now) &&
                setback.Spot.HorizontalDistanceTo(spot) <= SameSpotMetres &&
                setback.Spot.VerticalDistanceTo(spot) <= SameLevelMetres)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="route"/>, corner to corner, goes through
    /// a place that stopped him lately, heading the way it stopped him.</summary>
    public bool RefusesRoute(IReadOnlyList<WorldPoint> route, float now)
    {
        if (route == null || route.Count < 2)
        {
            return false;
        }

        foreach (WalkSetback setback in _setbacks)
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

    public void Clear()
    {
        _setbacks.Clear();
    }

    private WalkSetback Add(WorldPoint spot, bool hasPlace, WorldPoint stoppedAt, float headingX, float headingZ, float now)
    {
        // Forget what no longer even counts as "again".
        _setbacks.RemoveAll(setback => now >= setback.Until + RecallSeconds);

        // The same trouble as before - the same spot, or the same place passed
        // the same way - is again: it takes over from the old one, and is left
        // alone for longer.
        int strikes = 0;
        for (int index = _setbacks.Count - 1; index >= 0; index--)
        {
            WalkSetback before = _setbacks[index];
            bool sameSpot = before.Spot.HorizontalDistanceTo(spot) <= SameSpotMetres &&
                before.Spot.VerticalDistanceTo(spot) <= SameLevelMetres;
            bool samePlace = hasPlace && before.HasPlace &&
                before.StoppedAt.HorizontalDistanceTo(stoppedAt) <= SamePlaceMetres &&
                before.StoppedAt.VerticalDistanceTo(stoppedAt) <= SameLevelMetres &&
                (before.HeadingX * headingX) + (before.HeadingZ * headingZ) >= SameWayCosine;
            if (!sameSpot && !samePlace)
            {
                continue;
            }

            strikes = Math.Max(strikes, before.Strikes);
            _setbacks.RemoveAt(index);
        }

        strikes++;
        float pause = PauseFor(strikes);
        var setback = new WalkSetback(spot, hasPlace, stoppedAt, headingX, headingZ, now + pause, pause, strikes);

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

    private static bool TryHeading(WorldPoint from, WorldPoint to, out float headingX, out float headingZ)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float length = (float)Math.Sqrt((dx * dx) + (dz * dz));
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

    /// <summary>Whether walking straight from <paramref name="from"/> to
    /// <paramref name="to"/> takes him through the place that stopped him, the
    /// way it stopped him: heading within sixty degrees of it, passing within
    /// <see cref="SamePlaceMetres"/> of where he stood, at that level. A leg
    /// that ends before it gets there, or starts beyond it, does not go
    /// through it; the legs either side are asked for themselves.</summary>
    private static bool PassesTheSameWay(WorldPoint from, WorldPoint to, WalkSetback setback)
    {
        if (!TryHeading(from, to, out float alongX, out float alongZ) ||
            (alongX * setback.HeadingX) + (alongZ * setback.HeadingZ) < SameWayCosine)
        {
            return false;
        }

        float length = from.HorizontalDistanceTo(to);
        float offsetX = setback.StoppedAt.X - from.X;
        float offsetZ = setback.StoppedAt.Z - from.Z;
        float ahead = (offsetX * alongX) + (offsetZ * alongZ);
        if (ahead > length + ShortestHeadingMetres || ahead < -PastPlaceMetres)
        {
            return false;
        }

        float travelled = Math.Max(0f, ahead);
        float nearestX = from.X + (alongX * travelled);
        float nearestZ = from.Z + (alongZ * travelled);
        float sideX = setback.StoppedAt.X - nearestX;
        float sideZ = setback.StoppedAt.Z - nearestZ;
        if ((sideX * sideX) + (sideZ * sideZ) > SamePlaceMetres * SamePlaceMetres)
        {
            return false;
        }

        float nearestY = from.Y + ((to.Y - from.Y) * (travelled / length));
        return Math.Abs(nearestY - setback.StoppedAt.Y) <= SameLevelMetres;
    }
}
