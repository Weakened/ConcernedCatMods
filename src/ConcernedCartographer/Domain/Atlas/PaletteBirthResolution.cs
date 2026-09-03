namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pure decision core for resolving a palette birth (RC12
/// blocker 5). The <see cref="PaletteBirthTracker{THandle}"/> reports the
/// newborn handle when its vanilla naming flow closes; this rule decides
/// what the adapter must DO with it so that confirming a name always
/// leaves exactly one visible managed marker:
///
///  - the rendering survived naming → adopt it in place (the normal path);
///  - the rendering vanished but another adoptable pin appeared at the
///    same spot → the game (or another mod) replaced the object during
///    the naming close; adopt the replacement instead of duplicating;
///  - the rendering vanished and it carried a committed (non-empty) name
///    → the player pressed Enter on a real name and something removed the
///    pin out from under the flow; recreate the marker as a managed
///    entity so the confirmed marker cannot disappear;
///  - the rendering vanished without a name → the naming flow was
///    cancelled; honor the cancel and create nothing.</summary>
internal static class PaletteBirthResolution
{
    public enum Action
    {
        /// <summary>Adopt the born rendering as-is (normal path).</summary>
        AdoptBorn,

        /// <summary>Adopt the adoptable pin found at the born position.</summary>
        AdoptReplacement,

        /// <summary>Create a managed entity from the born pin's last state
        /// and give it a fresh rendering.</summary>
        RecreateManaged,

        /// <summary>Naming was cancelled; create nothing.</summary>
        DropCancelled,

        /// <summary>The born rendering exists but is not adoptable (foreign
        /// or already tracked); leave it alone.</summary>
        DropForeign,
    }

    public static Action Decide(
        bool bornStillOnMap,
        bool bornAdoptable,
        bool replacementAtPositionExists,
        string committedName)
    {
        if (bornStillOnMap)
        {
            return bornAdoptable ? Action.AdoptBorn : Action.DropForeign;
        }

        if (replacementAtPositionExists)
        {
            return Action.AdoptReplacement;
        }

        return string.IsNullOrEmpty(committedName) ? Action.DropCancelled : Action.RecreateManaged;
    }
}
