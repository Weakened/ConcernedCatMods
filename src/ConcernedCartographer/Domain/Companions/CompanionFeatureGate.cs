using System;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>The affordances this product <i>adds</i> to Valheim, named so the
/// gate can talk about them one at a time.
///
/// Only things Concerned Cartographer invented are on this list. The vanilla
/// map, vanilla pins, vanilla input, the mod's settings, its privacy controls,
/// its recovery and backup tooling and its crash reporting are not affordances
/// this product added on top of the game's own — they are how a player
/// configures and repairs the mod — so they are not enumerated here and the
/// gate has no vocabulary for locking them.</summary>
internal enum CartographerFeature
{
    /// <summary>The Atlas drawer: layers, search, saved views.</summary>
    Atlas = 0,

    /// <summary>The enhanced marker palette.</summary>
    Markers = 1,

    Routes = 2,

    Survey = 3,

    Share = 4,

    QuickPin = 5,

    /// <summary>The Pin Workbench editor.</summary>
    Workbench = 6,
}

/// <summary>Decides whether the product's added affordances are available.
///
/// The gate has exactly one locked state and a long list of reasons to be
/// open, which is the point. It is constructed from an unlock answer that the
/// shared <c>UnlockPolicy</c> already biased towards granting, and then this
/// type adds three more escape hatches on top:
///
/// <list type="bullet">
/// <item>Companions turned off in config — the player opted out of the whole
/// idea, so the tools behave exactly as they did before this feature
/// existed.</item>
/// <item>No scope — the world or character could not be identified. That is a
/// fact about our adapters, never evidence about the player, so it opens.</item>
/// <item>Not yet resolved — nothing has loaded. Opening here means a player
/// never watches their toolbar appear a second late.</item>
/// </list>
///
/// Put together: the only way to see a locked toolbar is to be a genuinely new
/// character, in a world we could identify, with companions enabled, who has
/// not finished or skipped the introduction. Everyone else — including every
/// existing player on upgrade — keeps what they had.</summary>
internal readonly struct CompanionFeatureGate
{
    private CompanionFeatureGate(bool unlocked, GateReason reason)
    {
        IsUnlocked = unlocked;
        Reason = reason;
    }

    /// <summary>The state before anything has loaded. Open.</summary>
    public static readonly CompanionFeatureGate Unresolved =
        new CompanionFeatureGate(true, GateReason.NotResolvedYet);

    /// <summary>Companions disabled in config. Open, and no compass either.</summary>
    public static readonly CompanionFeatureGate CompanionsDisabled =
        new CompanionFeatureGate(true, GateReason.CompanionsDisabled);

    /// <summary>The world or character could not be identified. Open.</summary>
    public static readonly CompanionFeatureGate NoScope =
        new CompanionFeatureGate(true, GateReason.NoScope);

    public static CompanionFeatureGate FromUnlock(bool unlocked)
    {
        return new CompanionFeatureGate(
            unlocked, unlocked ? GateReason.Unlocked : GateReason.IntroductionPending);
    }

    public bool IsUnlocked { get; }

    public GateReason Reason { get; }

    /// <summary>Whether one named affordance may be used right now.</summary>
    public bool Allows(CartographerFeature feature)
    {
        if (!Enum.IsDefined(typeof(CartographerFeature), feature))
        {
            // An unrecognised affordance is a bug in the caller, not a reason
            // to take something away from a player.
            return true;
        }

        return IsUnlocked;
    }

    /// <summary>The localization key for the sentence shown when a locked
    /// affordance is used. It names both ways out — meet Hulgi, or turn on
    /// Tools-only — because a player who cannot find the compass must never be
    /// stuck with it as their only option.</summary>
    public string LockedNoticeKey => "companion.locked";

    public enum GateReason
    {
        NotResolvedYet = 0,
        CompanionsDisabled = 1,
        NoScope = 2,
        Unlocked = 3,
        IntroductionPending = 4,
    }
}
