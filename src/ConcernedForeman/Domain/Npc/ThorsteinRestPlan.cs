using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>How Thorstein rests at home (#295 S5).</summary>
internal enum ThorsteinRestPose
{
    /// <summary>Standing beside the respawn point, as before S5. Only while no
    /// spot to sit has been found yet: never the answer once one has.</summary>
    Stand = 0,

    /// <summary>Sitting on the ground near home.</summary>
    SitOnGround = 1,

    /// <summary>Sitting on the ground within a fire's warmth, facing it.</summary>
    SitByFire = 2,
}

/// <summary>Where Thorstein sits and how, as the planner chose it.</summary>
internal readonly struct ThorsteinRestChoice
{
    internal ThorsteinRestChoice(
        ThorsteinRestPose pose, WorldPoint spot, int value, string reason, bool stillLoading = false)
    {
        Pose = pose;
        Spot = spot;
        Value = value;
        Reason = reason ?? string.Empty;
        StillLoading = stillLoading;
    }

    internal ThorsteinRestPose Pose { get; }

    /// <summary>The ground he sits on. Meaningless while standing.</summary>
    internal WorldPoint Spot { get; }

    /// <summary>The planner's own score for the spot: 2 when warm, 0 when not.
    /// </summary>
    internal int Value { get; }

    /// <summary>Why, in a few words, for <c>cf_worker status</c>.</summary>
    internal string Reason { get; }

    /// <summary>Standing only because some of the ground around home is not
    /// loaded yet: worth asking again soon.</summary>
    internal bool StillLoading { get; }

    internal bool IsSitting => Pose != ThorsteinRestPose.Stand;

    internal static ThorsteinRestChoice Standing(string reason, bool stillLoading = false) =>
        new ThorsteinRestChoice(ThorsteinRestPose.Stand, default, 0, reason, stillLoading);

    public override string ToString() => Pose switch
    {
        ThorsteinRestPose.SitByFire => "sitting by the fire",
        ThorsteinRestPose.SitOnGround => "sitting on the ground",
        _ => "standing (" + Reason + ")",
    };
}

/// <summary>Camp idle for Thorstein's figure at home (#295 S5): where he sits,
/// in what pose, and when he moves. Game-free; the probe that answers what the
/// ground is like is <c>ThorsteinRestProbe</c>.
///
/// <b>The shared rule, not a copy of it.</b> The spot is chosen by the
/// companion <see cref="PlacementPlanner"/> Hulgi already uses: a bounded,
/// deterministic ring sweep around home that prefers warmth, refuses water,
/// steep or occupied ground, a bed, a doorway and a fire itself, and defers
/// rather than guessing while any of it is unloaded. Only the band is
/// Thorstein's own: closer in than Hulgi's three to ten metres, because he
/// lives beside the bed rather than at the edge of camp, and on the same floor.
///
/// <b>No seat, no bed.</b> His probe does not offer seats, so the planner never
/// answers <see cref="CompanionPose.SitOnSeat"/>; a chair pose needs the seat
/// to be watched for a player sitting down, which is a slice of its own. An
/// answer this build cannot show is mapped to standing, not to a sitting pose
/// floating at a chair's height.
///
/// <b>Stable.</b> Once seated he moves only when the spot stops being usable
/// (something was built on it) or a better one appears (a fire was lit), so the
/// same camp keeps him in the same place.</summary>
internal static class ThorsteinRestPlan
{
    /// <summary>Closer than Hulgi's 3-10 m band, and on the same floor: a bed
    /// upstairs is no reason to sit in the cellar.</summary>
    internal static readonly PlacementRules Rules =
        new PlacementRules(minimumRadius: 1.5f, maximumRadius: 6f, maximumHeightDelta: 1.5f);

    /// <summary>How often a seated Thorstein looks again for a better spot.
    /// </summary>
    internal const float ReplanSeconds = 30f;

    /// <summary>How often a standing Thorstein asks again while the ground
    /// around home is still loading. Once the whole band has been seen and
    /// refused - the starting temple, a cramped hut - he asks only as often as
    /// a seated one, so a camp with nowhere to sit is not swept every few
    /// seconds for as long as the world is up.</summary>
    internal const float RetrySeconds = 5f;

    /// <summary>The planner's answer as a pose this build can show.</summary>
    internal static ThorsteinRestChoice Choose(PlacementResult result)
    {
        if (!result.Found)
        {
            return (result.BlockedBy & PlacementRejection.NotLoaded) != 0
                ? ThorsteinRestChoice.Standing("the ground around home is still loading", stillLoading: true)
                : ThorsteinRestChoice.Standing("no spot near home to sit (" + result.BlockedBy + ")");
        }

        switch (result.Pose)
        {
            case CompanionPose.SitByFire:
                return new ThorsteinRestChoice(ThorsteinRestPose.SitByFire, result.Position, result.Value, "warm");
            case CompanionPose.SitOnGround:
                return new ThorsteinRestChoice(ThorsteinRestPose.SitOnGround, result.Position, result.Value, "cold");
            default:
                return ThorsteinRestChoice.Standing("a " + result.Pose + " answer this build does not show");
        }
    }

    /// <summary>Whether to move him from where he is to <paramref name="best"/>.
    /// </summary>
    /// <param name="current">Where he is now.</param>
    /// <param name="currentUsable">Whether the ground under him still passes
    /// every rule, asked again now.</param>
    /// <param name="currentValue">His spot's score now: a fire that went out
    /// lowers it, and that alone is no reason to get up.</param>
    /// <param name="best">The planner's answer now.</param>
    internal static bool ShouldMove(
        ThorsteinRestChoice current, bool currentUsable, int currentValue, ThorsteinRestChoice best)
    {
        if (!best.IsSitting)
        {
            // Nothing better is known. Stay put, sitting or standing.
            return false;
        }

        if (!current.IsSitting || !currentUsable)
        {
            return true;
        }

        return best.Value > currentValue;
    }

    /// <summary>Whether a seated Thorstein must get up with nowhere better to
    /// go: his spot no longer passes (something was built on it) and the
    /// planner has no other. He then stands beside the respawn point again
    /// rather than staying seated inside whatever was built.</summary>
    internal static bool ShouldStandUp(ThorsteinRestChoice current, bool currentUsable, ThorsteinRestChoice best) =>
        current.IsSitting && !currentUsable && !best.IsSitting;

    /// <summary>Seconds until he looks again.</summary>
    internal static float NextLookIn(ThorsteinRestChoice current) =>
        !current.IsSitting && current.StillLoading ? RetrySeconds : ReplanSeconds;
}
