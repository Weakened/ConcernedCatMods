using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

internal enum CartGroundStatus
{
    Unspecified = 0,

    /// <summary>A solid surface was found near the height asked about.
    /// </summary>
    Surface = 1,

    /// <summary>The ground there is loaded and readable, and nothing solid is
    /// there to stand on: a hole, a chasm, the far side of a bridge's edge.
    /// </summary>
    NoSurface = 2,

    /// <summary>That ground is not loaded, so nothing about it is known.
    /// </summary>
    Unloaded = 3,

    /// <summary>The probe failed; nothing about that ground is known.</summary>
    Unreadable = 4,
}

/// <summary>What is under one point: the supporting surface nearest the height
/// the route expects there, and any liquid over it.</summary>
internal readonly struct CartGroundSample
{
    public CartGroundSample(CartGroundStatus status, float height, float upDot, float liquidDepthMetres, bool lava)
    {
        Status = status;
        Height = height;
        UpDot = upDot;
        LiquidDepthMetres = liquidDepthMetres;
        Lava = lava;
    }

    public CartGroundStatus Status { get; }

    /// <summary>The surface's height; meaningful only for
    /// <see cref="CartGroundStatus.Surface"/>.</summary>
    public float Height { get; }

    /// <summary>The upward component of the surface normal: 1 is flat.</summary>
    public float UpDot { get; }

    /// <summary>How deep water or another liquid stands over the surface (or
    /// over the expected height when there is no surface). Zero or less is dry.
    /// </summary>
    public float LiquidDepthMetres { get; }

    /// <summary>Molten ground no cart may cross.</summary>
    public bool Lava { get; }

    public static CartGroundSample Solid(float height, float upDot) =>
        new CartGroundSample(CartGroundStatus.Surface, height, upDot, 0f, false);

    public static CartGroundSample Missing(CartGroundStatus status) =>
        new CartGroundSample(status, 0f, 0f, 0f, false);
}

internal enum CartClearanceStatus
{
    Unspecified = 0,
    Clear = 1,
    Blocked = 2,

    /// <summary>The probe failed; clearance is not known.</summary>
    Unreadable = 3,
}

/// <summary>One clearance probe's answer.</summary>
internal readonly struct CartClearanceSample
{
    public CartClearanceSample(CartClearanceStatus status, float blockedAtMetres, string obstacle)
    {
        Status = status;
        BlockedAtMetres = blockedAtMetres;
        Obstacle = obstacle ?? string.Empty;
    }

    public CartClearanceStatus Status { get; }

    /// <summary>How far along the sweep the box first touched something; zero
    /// when it overlapped from the start. Meaningful only when blocked.</summary>
    public float BlockedAtMetres { get; }

    /// <summary>What it touched, for the log. Never shown as a player sentence.
    /// </summary>
    public string Obstacle { get; }

    public static CartClearanceSample Clear => new CartClearanceSample(CartClearanceStatus.Clear, 0f, string.Empty);

    public static CartClearanceSample Unreadable => new CartClearanceSample(CartClearanceStatus.Unreadable, 0f, string.Empty);

    public static CartClearanceSample BlockedAt(float metres, string obstacle) =>
        new CartClearanceSample(CartClearanceStatus.Blocked, metres, obstacle);
}

/// <summary>The terrain and obstacle port (agent B's adapter implements it
/// over the running game). Everything a cart route needs to know about the
/// world, asked one bounded question at a time, so the evaluator stays
/// game-free and every question can be counted against a budget.
///
/// Implementations never throw: a failure is an Unreadable answer. The
/// evaluator still guards every call, so a probe that does throw becomes a
/// refusal, never an escape into the game's update loop.</summary>
internal interface ICartTerrainProbe
{
    /// <summary>Whether the ground at this point is loaded and simulated.
    /// </summary>
    bool IsLoaded(WorkPoint point);

    /// <summary>The supporting surface at (<paramref name="x"/>,
    /// <paramref name="z"/>) nearest <paramref name="nearHeight"/>: roofs and
    /// overhangs well above it are not ground, and ground far below it is a gap.
    /// </summary>
    CartGroundSample SampleGround(float x, float z, float nearHeight);

    /// <summary>Sweeps the cart's box - <paramref name="halfWidth"/> across and
    /// <paramref name="halfLength"/> along (<paramref name="headingX"/>,
    /// <paramref name="headingZ"/>), from its lowest clearance height to its top
    /// - from centre <paramref name="from"/> to centre <paramref name="to"/>,
    /// following their ground heights. The leased cart, Gunnar, characters and
    /// triggers are not obstacles. Something already inside the box where the
    /// sweep starts blocks it unless <paramref name="ignoreStartOverlaps"/>,
    /// which is only for the very first stretch of a route: the cart is already
    /// standing there.</summary>
    CartClearanceSample SweepBox(
        WorkPoint from, WorkPoint to, float headingX, float headingZ, float halfWidth, float halfLength,
        bool ignoreStartOverlaps);

    /// <summary>Whether anything solid stands inside the cart's box at one pose.
    /// </summary>
    CartClearanceSample CheckBox(WorkPoint centre, float headingX, float headingZ, float halfWidth, float halfLength);

    /// <summary>Clears <paramref name="doorways"/> and fills it with every
    /// doorway within <paramref name="radius"/>. False when the doors could not
    /// be read.</summary>
    bool TryFindDoorways(WorkPoint centre, float radius, List<CartDoorway> doorways);
}
