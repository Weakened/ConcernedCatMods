namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>The two words a role lends the planner so it can write steps in the
/// role's own language.
///
/// <b>Why the planner has to be told.</b> A plan is a list of steps, and a step
/// carries the role's token for what is done. The planner writes two kinds of
/// step nobody else can write for it - open that chest and take this out, go to
/// that target and do the thing - and it has no business inventing a name for
/// either. So the role hands in its two words and the planner uses them
/// verbatim. Nothing here ever reads them, compares them, or branches on
/// them.
///
/// <b>Why not a constant.</b> Because a constant here would be a durable name
/// owned by this package, and this package owns no durable names. That is not a
/// style rule: a name in here is a name the roles would then have to keep
/// spelling the same way forever, across a library that ships on its own release
/// cadence, and the source audit fails the build for one.</summary>
public readonly struct JobStepActions
{
    public JobStepActions(string? collect, string? service)
    {
        Collect = collect ?? string.Empty;
        Service = service ?? string.Empty;
    }

    /// <summary>What the role calls taking material out of a container.</summary>
    public string Collect { get; }

    /// <summary>What the role calls doing the thing to a target, for targets
    /// that do not name their own action.</summary>
    public string Service { get; }

    /// <summary>Both words present. A planner given half a vocabulary refuses
    /// rather than writing a step nobody can carry out.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Collect) && !string.IsNullOrEmpty(Service);
}
