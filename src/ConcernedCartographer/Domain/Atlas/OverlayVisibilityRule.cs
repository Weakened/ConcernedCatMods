namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The one-presentation-at-a-time rule for every CC map layer
/// (roads and routes; RC8-1 refined by RC10 feedback 7). The player owns a
/// single layer switch; the TEXTURE overlay additionally hides while the
/// vector presentation draws the same layer on the large map, and returns
/// the moment the vector layer is inactive (map closed, feature off,
/// failed). The visible checkbox always shows the USER's switch, never
/// the suppression state — a checkbox that reads ON while the texture is
/// suppressed is telling the truth, because the vector ink is on
/// screen.</summary>
internal static class OverlayVisibilityRule
{
    /// <summary>Whether the texture overlay should be enabled.</summary>
    public static bool EffectiveTexture(bool userEnabled, bool vectorPresentationActive)
    {
        return userEnabled && !vectorPresentationActive;
    }

    /// <summary>What the user-facing checkbox must display.</summary>
    public static bool CheckboxShows(bool userEnabled)
    {
        return userEnabled;
    }
}
