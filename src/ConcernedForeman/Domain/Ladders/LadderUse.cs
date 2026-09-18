namespace TheConcernedCat.ConcernedForeman.Domain.Ladders;

/// <summary>What pressing Use on a ladder should do.</summary>
internal enum LadderUseOutcome
{
    /// <summary>Nothing at all: the game does exactly what it does without this
    /// mod installed, including the teleport.</summary>
    VanillaTeleport,

    /// <summary>Suppress the teleport and do nothing else. The player is
    /// already on a ladder, and vanilla's Use would have moved them off it by
    /// force.</summary>
    AlreadyClimbing,

    /// <summary>Suppress the teleport if, and only if, the climb actually
    /// starts. If it does not, vanilla's teleport is still vanilla's to
    /// do.</summary>
    TryToClimb,
}

/// <summary>The whole policy of the Use interaction, in one function with no
/// Unity in it (CF-LAD-004).
///
/// The Harmony prefix that carries it out is four lines, because everything
/// that could be got wrong is here where a test can hold it:
///
/// <list type="bullet">
/// <item><b><c>Ladders/Enabled = false</c> is vanilla.</b> Not "vanilla-ish":
/// the prefix hands the method straight back, and at load with the feature off
/// the patch is never installed at all.</item>
/// <item><b><c>Ladders/UseTeleport = true</c> is vanilla too</b>, in every
/// state, including while somebody is climbing. A player who asks for the
/// teleport back gets the teleport back, and the climb they were in ends
/// safely through <c>ClimbSafety</c> like any other thing that moves a
/// character off a ladder.</item>
/// <item><b>A climber's Use never teleports them.</b> Vanilla's Use on a ladder
/// is a teleport to the other end; pressing it while climbing would fling the
/// player off the run they are on.</item>
/// </list></summary>
internal static class LadderUse
{
    /// <summary>The decision, in the order that matters. The two settings come
    /// first because "off means off" outranks every other consideration.</summary>
    internal static LadderUseOutcome Decide(bool laddersEnabled, bool useTeleport, bool alreadyClimbing)
    {
        if (!laddersEnabled || useTeleport)
        {
            return LadderUseOutcome.VanillaTeleport;
        }

        if (alreadyClimbing)
        {
            return LadderUseOutcome.AlreadyClimbing;
        }

        return LadderUseOutcome.TryToClimb;
    }
}
