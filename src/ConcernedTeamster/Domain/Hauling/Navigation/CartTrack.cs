using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Where Gunnar and the cart behind him are at one moment of a route,
/// as predicted before anything moves.</summary>
internal readonly struct CartPose
{
    public CartPose(WorkPoint puller, WorkPoint axle, float headingX, float headingZ, float articulationDegrees)
    {
        Puller = puller;
        Axle = axle;
        HeadingX = headingX;
        HeadingZ = headingZ;
        ArticulationDegrees = articulationDegrees;
    }

    /// <summary>The hitch: where Gunnar stands, holding the handle.</summary>
    public WorkPoint Puller { get; }

    /// <summary>The middle of the cart's axle.</summary>
    public WorkPoint Axle { get; }

    /// <summary>The cart's flat forward direction, axle towards hitch.</summary>
    public float HeadingX { get; }

    public float HeadingZ { get; }

    /// <summary>The widest angle, since the previous pose, between the way
    /// Gunnar walked and the way the cart pointed.</summary>
    public float ArticulationDegrees { get; }

    /// <summary>The centre of the cart's box - its front edge at the hitch -
    /// at <paramref name="height"/>.</summary>
    public WorkPoint BoxCentre(float lengthMetres, float height)
    {
        float half = lengthMetres * 0.5f;
        return new WorkPoint(Puller.X - (HeadingX * half), height, Puller.Z - (HeadingZ * half));
    }
}

/// <summary>Predicts the cart's track behind Gunnar - the reason a path a
/// person can walk is not proof a cart fits.
///
/// Vanilla's hitch holds the cart's handle at Gunnar's body and lets it swivel,
/// and a cart's wheels roll forward but do not slide sideways. So the axle is
/// always exactly one hitch length behind the handle, and it only ever moves
/// towards where the handle is: the classic trailer curve. On a straight pull
/// the cart follows Gunnar's line; in a turn it cuts inside the corner, by more
/// the longer the hitch and the sharper the turn. Pure geometry in small fixed
/// steps; the same inputs always give the same track.</summary>
internal static class CartTrackPredictor
{
    /// <summary>The pose at the start of a route. When the cart's position is
    /// known it trails from where it actually is; otherwise it is assumed to be
    /// lined up straight behind Gunnar along the route's first stretch.
    /// </summary>
    public static CartPose Start(IReadOnlyList<WorkPoint> puller, float hitchLengthMetres, WorkPoint? cartPosition)
    {
        WorkPoint origin = puller[0];
        float headingX;
        float headingZ;
        if (cartPosition.HasValue && cartPosition.Value.IsFinite &&
            CartRouteGeometry.FlatDistance(origin, cartPosition.Value) > CartRouteGeometry.SamePointMetres &&
            CartRouteGeometry.TryFlatDirection(cartPosition.Value, origin, out headingX, out headingZ))
        {
            // Trails from the cart's real side.
        }
        else if (!TryFirstDirection(puller, out headingX, out headingZ))
        {
            headingX = 0f;
            headingZ = 1f;
        }

        float hitch = Math.Max(0f, hitchLengthMetres);
        var axle = new WorkPoint(origin.X - (headingX * hitch), origin.Y, origin.Z - (headingZ * hitch));
        return new CartPose(origin, axle, headingX, headingZ, 0f);
    }

    /// <summary>Walks the hitch from <paramref name="from"/>'s puller position
    /// to <paramref name="to"/> and returns the pose there. When
    /// <paramref name="record"/> is given, it also stores the poses reached at
    /// <paramref name="parts"/> - 1 evenly spread points in between (the final
    /// pose is the return value), for sweeping a turn in shorter pieces.</summary>
    public static CartPose Advance(
        CartPose from, WorkPoint to, float hitchLengthMetres, CartPose[]? record = null, int parts = 1)
    {
        float fromX = from.Puller.X;
        float fromZ = from.Puller.Z;
        float length = CartRouteGeometry.FlatLength(to.X - fromX, to.Z - fromZ);
        int steps = Math.Max(1, (int)Math.Ceiling(length / CartRouteGeometry.PredictionStepMetres));
        if (record != null && parts > steps)
        {
            parts = steps;
        }

        float travelX = from.HeadingX;
        float travelZ = from.HeadingZ;
        if (length > 1e-4f)
        {
            travelX = (to.X - fromX) / length;
            travelZ = (to.Z - fromZ) / length;
        }

        float hitch = Math.Max(0f, hitchLengthMetres);
        float axleX = from.Axle.X;
        float axleZ = from.Axle.Z;
        float headingX = from.HeadingX;
        float headingZ = from.HeadingZ;
        float widest = 0f;
        int nextPart = 1;

        for (int step = 1; step <= steps; step++)
        {
            float t = (float)step / steps;
            float pullerX = fromX + ((to.X - fromX) * t);
            float pullerZ = fromZ + ((to.Z - fromZ) * t);

            if (hitch > 1e-4f)
            {
                float dx = pullerX - axleX;
                float dz = pullerZ - axleZ;
                float distance = CartRouteGeometry.FlatLength(dx, dz);
                if (distance > 1e-6f)
                {
                    headingX = dx / distance;
                    headingZ = dz / distance;
                    axleX = pullerX - (headingX * hitch);
                    axleZ = pullerZ - (headingZ * hitch);
                }
            }
            else
            {
                axleX = pullerX;
                axleZ = pullerZ;
                headingX = travelX;
                headingZ = travelZ;
            }

            float articulation = length > 1e-4f
                ? CartRouteGeometry.AngleDegrees(travelX, travelZ, headingX, headingZ)
                : 0f;
            if (articulation > widest)
            {
                widest = articulation;
            }

            if (record != null && nextPart < parts &&
                step == Math.Max(1, (int)Math.Round((double)steps * nextPart / parts)))
            {
                var pullerAt = CartRouteGeometry.Lerp(from.Puller, to, t);
                record[nextPart - 1] = new CartPose(
                    pullerAt, new WorkPoint(axleX, pullerAt.Y, axleZ), headingX, headingZ, widest);
                nextPart++;
            }
        }

        return new CartPose(to, new WorkPoint(axleX, to.Y, axleZ), headingX, headingZ, widest);
    }

    private static bool TryFirstDirection(IReadOnlyList<WorkPoint> puller, out float x, out float z)
    {
        for (int index = 1; index < puller.Count; index++)
        {
            if (CartRouteGeometry.TryFlatDirection(puller[0], puller[index], out x, out z))
            {
                return true;
            }
        }

        x = 0f;
        z = 0f;
        return false;
    }
}
