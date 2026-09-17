using System;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>What the world says now about where an accepted scope came from.
/// </summary>
internal readonly struct ScopeObservation
{
    public ScopeObservation(bool sourcePresent, int currentRevision, Guid currentEpoch)
    {
        SourcePresent = sourcePresent;
        CurrentRevision = currentRevision;
        CurrentEpoch = currentEpoch;
    }

    /// <summary>The harvest designation still exists, or a respawn anchor can
    /// still be read. False also when it could not be asked.</summary>
    public bool SourcePresent { get; }

    /// <summary>The source's revision now, computed the same way as at
    /// acceptance (<see cref="ScopeRevision"/>).</summary>
    public int CurrentRevision { get; }

    public Guid CurrentEpoch { get; }
}

internal enum ScopeCheck
{
    Unspecified = 0,
    Valid = 1,

    /// <summary>The designation was redrawn or the anchor moved (a new bed).
    /// </summary>
    Changed = 2,

    /// <summary>No part of the scope's ground is loaded.</summary>
    Unloaded = 3,

    /// <summary>Deleted, unreadable, or from another world load.</summary>
    Invalid = 4,
}

/// <summary>Scope revalidation at a safe checkpoint (GATHER-02, CONTRACTS.md
/// §4): before a reservation, before a pickup, before a delivery leg.
///
/// It only ever answers; it never moves or widens anything. A changed scope
/// pauses the order with <c>ScopeChanged</c>, an unloaded one with
/// <c>ScopeUnloaded</c>, and an invalid one with <c>ScopeInvalid</c>. There is
/// no fallback from an assigned area to the default circle, and no silent
/// expansion.</summary>
internal static class ScopeCheckpoint
{
    public static ScopeCheck Revalidate(WorkScope accepted, ScopeObservation now, Func<SitePoint, bool> isLoaded)
    {
        if (accepted == null)
        {
            return ScopeCheck.Invalid;
        }

        if (now.CurrentEpoch == Guid.Empty || now.CurrentEpoch != accepted.WorldLoadEpoch || !now.SourcePresent)
        {
            return ScopeCheck.Invalid;
        }

        if (now.CurrentRevision != accepted.SourceRevision)
        {
            return ScopeCheck.Changed;
        }

        return AnyLoaded(accepted, isLoaded) ? ScopeCheck.Valid : ScopeCheck.Unloaded;
    }

    /// <summary>The centre and eight points on a ring at 70 % of the radius. A
    /// scope with none of them loaded has nothing a worker could see.</summary>
    public static bool AnyLoaded(WorkScope scope, Func<SitePoint, bool> isLoaded)
    {
        if (isLoaded == null)
        {
            return false;
        }

        if (isLoaded(scope.Centre))
        {
            return true;
        }

        float ring = scope.RadiusMetres * 0.7f;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * (Math.PI / 4d);
            var sample = new SitePoint(
                scope.Centre.X + (float)(Math.Cos(angle) * ring),
                scope.Centre.Y,
                scope.Centre.Z + (float)(Math.Sin(angle) * ring));
            if (isLoaded(sample))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Stable revisions for what a scope is copied from, so "has it changed
/// since acceptance" is an equality test that survives nothing but the thing
/// itself staying the same. FNV-1a over invariant round-trip text: the same
/// designation or anchor gives the same number in every session.</summary>
internal static class ScopeRevision
{
    public static int ForArea(SitePoint centre, float radius) =>
        Hash("area|" + Format(centre) + "|" + radius.ToString("R", CultureInfo.InvariantCulture));

    public static int ForAnchor(string anchorKind, SitePoint point) =>
        Hash("anchor|" + (anchorKind ?? string.Empty) + "|" + Format(point));

    private static string Format(SitePoint point) =>
        point.X.ToString("R", CultureInfo.InvariantCulture) + ";" +
        point.Y.ToString("R", CultureInfo.InvariantCulture) + ";" +
        point.Z.ToString("R", CultureInfo.InvariantCulture);

    private static int Hash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }
}
