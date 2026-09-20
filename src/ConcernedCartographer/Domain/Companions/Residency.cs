using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>What is known about a recorded home point right now.
///
/// The three-way answer is the whole point. A profile records a spawn point,
/// not a bed — the recorded position survives the bed being destroyed or
/// unclaimed — so "is there still a bed there" has to be asked of the world.
/// And the world can only answer while it is loaded. Collapsing
/// <see cref="Unknown"/> into <see cref="Gone"/> would march the companion back
/// to the world's starting point every time the player walked away from home,
/// which is exactly the wandering this design is meant to prevent.</summary>
internal enum AnchorValidity
{
    /// <summary>The claim was checked and something is still there.</summary>
    Valid = 0,

    /// <summary>The ground there is not loaded, so nothing can be checked.
    /// Treated as "keep believing what we believed", never as evidence.</summary>
    Unknown = 1,

    /// <summary>The ground there IS loaded and there is no bed. This is the
    /// only answer that may move the companion off a claimed bed.</summary>
    Gone = 2,
}

/// <summary>What has become of the seat the companion is sitting on.
///
/// Furniture is not permanent and it is not his. A bench gets taken down, a
/// stool gets picked up, and the player sits in the chair he was using - all
/// three are ordinary, and all three have to end with him somewhere sensible
/// rather than perched in mid-air or overlapping the person who owns the
/// place.</summary>
internal enum SeatStatus
{
    /// <summary>He is not using a seat, so there is nothing to lose.</summary>
    NotSeated = 0,

    /// <summary>The seat is still there and still free.</summary>
    Held = 1,

    /// <summary>The seat is gone, or somebody is in it. He gives it up.</summary>
    Lost = 2,
}

/// <summary>What a furniture sweep found, compared with what he is doing now.
///
/// The placement planner is deliberately still. It re-plans when the home point
/// moves and when a seat is lost, and never merely because a fresh sweep liked
/// a different spot of the same rank — that twitchiness is what makes a
/// companion look like he is pacing between sessions. An upgrade is the single
/// exception and it only ever ratchets one way, up the planner's own scale:
/// bare ground, then a seat, then warmth, then a seat by the fire.
///
/// Carrying the comparison as a value rather than a bool is what lets the rule
/// be stated once, here, instead of in the adapter that happens to do the
/// sweeping.</summary>
internal readonly struct SeatUpgrade
{
    /// <summary>No sweep happened this pass. The overwhelmingly common case:
    /// the sweep runs on a slow interval, and not at all once he is seated.
    /// </summary>
    public static readonly SeatUpgrade NotSurveyed = default;

    public SeatUpgrade(CompanionPose placed, CompanionPose offered)
        : this(ValueOf(placed), ValueOf(offered))
    {
        Placed = placed;
        Offered = offered;
    }

    /// <summary>The comparison the runtime actually makes: the value of the spot
    /// he is in as the world stands NOW, against the best on offer, both on
    /// <c>PlacementPlanner.ValueOf</c>'s scale. The pose he was placed in is not
    /// enough - break the fire beside him and his "by the fire" pose is still
    /// recorded while the spot has gone cold, and he would never look
    /// elsewhere.</summary>
    public SeatUpgrade(int currentValue, int offeredValue)
    {
        Surveyed = true;
        CurrentValue = currentValue;
        OfferedValue = offeredValue;
        Placed = default;
        Offered = default;
    }

    /// <summary>A pose on the same scale, for callers that only know poses: a
    /// seat 1, a fire 2, the ground 0.</summary>
    private static int ValueOf(CompanionPose pose)
    {
        switch (pose)
        {
            case CompanionPose.SitByFire:
                return 2;
            case CompanionPose.SitOnSeat:
                return 1;
            default:
                return 0;
        }
    }

    public int CurrentValue { get; }

    public int OfferedValue { get; }

    /// <summary>Whether a sweep actually ran. A sweep that ran and found
    /// nothing better is not the same as no sweep, and neither moves him.
    /// </summary>
    public bool Surveyed { get; }

    /// <summary>The pose he is in.</summary>
    public CompanionPose Placed { get; }

    /// <summary>The best pose available where he is.</summary>
    public CompanionPose Offered { get; }

    /// <summary>Strictly better, or nothing. Equal rank never moves him, so a
    /// sweep that breaks a tie differently from last time costs nothing; worse
    /// rank never moves him either, so a chair being carried away does not
    /// shuffle him sideways onto the grass — losing the seat does that, and
    /// that is a different signal with its own backoff.</summary>
    public bool IsWorthMoving => Surveyed && OfferedValue > CurrentValue;
}

/// <summary>What the runtime should do about the companion actor this pass.</summary>
internal enum ResidencyAction
{
    /// <summary>Nothing to change.</summary>
    None = 0,

    /// <summary>Build the actor; there isn't one.</summary>
    Place = 1,

    /// <summary>Tear the actor down and build it at the new home.</summary>
    Rehome = 2,

    /// <summary>Tear the actor down and leave it down.</summary>
    Remove = 3,
}

/// <summary>Everything the residency decision needs, gathered by the adapter
/// so the decision itself can be tested without a game.</summary>
internal readonly struct ResidencyInputs
{
    public ResidencyInputs(
        bool companionRecruited,
        bool visibilityEnabled,
        bool actorPresent,
        bool presentationSupported,
        CompanionAnchor currentAnchor,
        CompanionAnchor placedAnchor,
        AnchorValidity validity,
        SeatStatus seat = SeatStatus.NotSeated,
        SeatUpgrade upgrade = default)
    {
        CompanionRecruited = companionRecruited;
        VisibilityEnabled = visibilityEnabled;
        ActorPresent = actorPresent;
        PresentationSupported = presentationSupported;
        CurrentAnchor = currentAnchor;
        PlacedAnchor = placedAnchor;
        Validity = validity;
        Seat = seat;
        Upgrade = upgrade;
    }

    /// <summary>Whether the player actually welcomed the companion. Someone who
    /// chose Tools-only has every feature and no companion.</summary>
    public bool CompanionRecruited { get; }

    /// <summary>The player's visibility setting. Presentation only.</summary>
    public bool VisibilityEnabled { get; }

    public bool ActorPresent { get; }

    /// <summary>False once the presentation adapter has reported that it cannot
    /// build an actor on this build. Disables the actor and nothing else.</summary>
    public bool PresentationSupported { get; }

    /// <summary>The home point resolved this pass.</summary>
    public CompanionAnchor CurrentAnchor { get; }

    /// <summary>The home point the existing actor was built against.</summary>
    public CompanionAnchor PlacedAnchor { get; }

    public AnchorValidity Validity { get; }

    /// <summary>What has become of the seat he is on, if any.</summary>
    public SeatStatus Seat { get; }

    /// <summary>What a furniture sweep offered this pass, if one ran.</summary>
    public SeatUpgrade Upgrade { get; }
}

/// <summary>Decides whether the companion should be standing somewhere, and
/// whether "somewhere" has changed.
///
/// Separated from the adapter because the interesting cases are all about
/// <i>not</i> acting: a bed whose zone is not loaded must not move anybody, a
/// hidden companion must not lose his home, and a home point that jitters by a
/// few centimetres must not cause a rebuild every five seconds.</summary>
internal static class ResidencyPlanner
{
    /// <summary>How far the home point must move before the companion follows
    /// it. Matches the shared placement rules' tolerance: below this, a
    /// re-resolved anchor is the same anchor.</summary>
    public const float MoveTolerance = PlacementRules.DefaultAnchorMoveTolerance;

    public static ResidencyAction Decide(ResidencyInputs inputs)
    {
        // Presentation is downstream of everything. None of these removes
        // access, progress or the companion's existence in the save — they
        // only decide whether anything is drawn.
        if (!inputs.CompanionRecruited || !inputs.VisibilityEnabled || !inputs.PresentationSupported)
        {
            return inputs.ActorPresent ? ResidencyAction.Remove : ResidencyAction.None;
        }

        if (!inputs.CurrentAnchor.IsValid)
        {
            // No home point resolved yet. An actor already standing somewhere
            // stays there — a momentary resolution failure is not a reason to
            // make somebody vanish.
            return inputs.ActorPresent ? ResidencyAction.None : ResidencyAction.None;
        }

        if (!inputs.ActorPresent)
        {
            return ResidencyAction.Place;
        }

        // A seat that was taken or taken away is given up at once, and only
        // then. Re-planning re-probes the furniture, so the next spot is
        // whatever is actually free - another seat, or the ground beside it.
        // The check is deliberately before the anchor comparison: the home
        // point has not moved, so nothing below would notice.
        if (inputs.Seat == SeatStatus.Lost)
        {
            return ResidencyAction.Rehome;
        }

        // Somebody built him a chair. Nothing above notices, because nothing
        // above changed: the actor exists, the home point has not moved, and
        // the seat he is on - the ground - cannot be lost. Without this he
        // sits beside a new bench until a relog, which is the whole of #306.
        //
        // Strictly better only. That is a ratchet in a world that holds still,
        // and it was once described here as one that can fire at most twice in
        // a companion's life - ground to fire, fire to seat. That is false, and
        // #306 is where it showed: the rank on the left is scored against the
        // world as it stands NOW, not against the pose he was placed in, so
        // anything that lowers it re-cocks the ratchet. The fire beside him
        // goes out and he is no longer by a fire; a player sits in his chair
        // and he is no longer on a seat; dusk rewrites the wish list outright,
        // every day, for everybody.
        //
        // So the honest bound is: strictly better stops him pacing between two
        // equally good spots, and nothing more. What stops a flickering camp
        // walking him back and forth all evening is how often he is allowed to
        // look at all - the adapter's business, and SeatSweepGate's floor.
        if (inputs.Upgrade.IsWorthMoving)
        {
            return ResidencyAction.Rehome;
        }

        // The anchor was checked and is gone. This is the only answer that may
        // move a companion off a bed the player claimed.
        if (inputs.Validity == AnchorValidity.Gone &&
            inputs.PlacedAnchor.Kind == AnchorKind.ClaimedBed &&
            inputs.CurrentAnchor.Kind != AnchorKind.ClaimedBed)
        {
            return ResidencyAction.Rehome;
        }

        return inputs.CurrentAnchor.DiffersFrom(inputs.PlacedAnchor, MoveTolerance)
            ? ResidencyAction.Rehome
            : ResidencyAction.None;
    }

    /// <summary>Which home point to believe, given what the world could tell
    /// us. <paramref name="fallback"/> is the world's starting point.
    ///
    /// The rule is short: only a checked-and-empty bed loses. Unknown keeps the
    /// bed, because the alternative — assuming a bed is gone because its chunk
    /// happens to be unloaded — relocates the companion every time the player
    /// travels.</summary>
    public static CompanionAnchor Resolve(
        CompanionAnchor bed, AnchorValidity validity, CompanionAnchor fallback)
    {
        if (!bed.IsValid)
        {
            return fallback;
        }

        return validity == AnchorValidity.Gone ? fallback : bed;
    }
}
