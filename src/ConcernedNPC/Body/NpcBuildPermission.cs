namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Whether a body may be built right now, and if not, the sentence
/// saying why.
///
/// A refusal always carries a reason and a grant never does, so a caller that
/// logs the reason unconditionally prints nothing on the happy path and the
/// exact cause on every other - which is the failure mode the three runtimes
/// this replaces each got wrong in a different way.</summary>
internal readonly struct NpcBuildPermission
{
    private NpcBuildPermission(bool granted, string refusal)
    {
        IsGranted = granted;
        Refusal = refusal;
    }

    internal bool IsGranted { get; }

    /// <summary>Why the build was refused, or empty on a grant.</summary>
    internal string Refusal { get; }

    internal static NpcBuildPermission Granted() => new NpcBuildPermission(true, string.Empty);

    internal static NpcBuildPermission Refused(string reason) =>
        new NpcBuildPermission(false, string.IsNullOrEmpty(reason) ? "no reason was given" : reason);

    public override string ToString() => IsGranted ? "granted" : "refused: " + Refusal;
}
