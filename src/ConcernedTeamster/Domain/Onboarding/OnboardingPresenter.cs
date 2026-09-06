namespace TheConcernedCat.ConcernedTeamster.Domain.Onboarding;

/// <summary>Decides whether the first-run pointer to the Cart Status button
/// shows (CT-034): a pure function of two facts, so the whole "shows once,
/// dismisses forever" contract is provable without a game session. There is
/// no hidden state here — persistence of the dismissed flag is the caller's
/// job (a config entry), which keeps this class trivially re-evaluable every
/// frame with no drift risk.</summary>
public static class OnboardingPresenter
{
    /// <summary>Visible only when the player has never dismissed it AND a
    /// cart is currently nearby. Once dismissed, always Hidden regardless of
    /// proximity — dismissal is permanent, never re-triggered by walking
    /// away and back. Not near a cart never shows it, so first launch is
    /// silent until the player is actually somewhere the hint is relevant.</summary>
    public static OnboardingVisibility Evaluate(bool dismissedForever, bool isNearCart)
    {
        if (dismissedForever)
        {
            return OnboardingVisibility.Hidden;
        }

        return isNearCart ? OnboardingVisibility.Visible : OnboardingVisibility.Hidden;
    }
}
