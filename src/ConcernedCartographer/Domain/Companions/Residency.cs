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
        AnchorValidity validity)
    {
        CompanionRecruited = companionRecruited;
        VisibilityEnabled = visibilityEnabled;
        ActorPresent = actorPresent;
        PresentationSupported = presentationSupported;
        CurrentAnchor = currentAnchor;
        PlacedAnchor = placedAnchor;
        Validity = validity;
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
