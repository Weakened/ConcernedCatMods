using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>What releasing a cart's joint should do. Zero is unspecified.
/// </summary>
internal enum JointReleaseDecision
{
    Unspecified = 0,

    /// <summary>Call the cart's own detach: the joint is Gunnar's, or connected
    /// to nothing, or there is no joint (a stale flag is cleared).</summary>
    Detach = 1,

    /// <summary>Leave it: another living body holds the cart.</summary>
    LeaveAlone = 2,
}

/// <summary>What one cart's joint is connected to, read by the seam.</summary>
internal readonly struct JointHolderFacts
{
    public JointHolderFacts(
        bool jointExists,
        bool connectedToNothing,
        bool connectedToAttachedBody,
        bool connectedToBoundBody,
        bool connectedToWorkerBody)
    {
        JointExists = jointExists;
        ConnectedToNothing = connectedToNothing;
        ConnectedToAttachedBody = connectedToAttachedBody;
        ConnectedToBoundBody = connectedToBoundBody;
        ConnectedToWorkerBody = connectedToWorkerBody;
    }

    public bool JointExists { get; }

    /// <summary>The joint's connected body is null or destroyed.</summary>
    public bool ConnectedToNothing { get; }

    /// <summary>The joint is connected to the rigidbody Gunnar's verified attach
    /// connected, remembered by the seam whatever the current body binding.
    /// </summary>
    public bool ConnectedToAttachedBody { get; }

    /// <summary>The joint is connected to the body currently bound as Gunnar.
    /// </summary>
    public bool ConnectedToBoundBody { get; }

    /// <summary>The joint is connected to any Teamster worker body (a Gunnar
    /// body, bound or not).</summary>
    public bool ConnectedToWorkerBody { get; }

    /// <summary>Gunnar's, by any of the evidence: an unbound, dying or duplicated
    /// Gunnar is still Gunnar.</summary>
    public bool IsGunnars => ConnectedToAttachedBody || ConnectedToBoundBody || ConnectedToWorkerBody;
}

/// <summary>The attach seam's own decisions, kept pure so they are tested
/// directly rather than re-implemented by a fake (review R-313 M2): the
/// last-instant guards before the cart's attach, the verification after it,
/// and whether a joint is Gunnar's to release.</summary>
internal static class HitchSeamRules
{
    /// <summary>The two guards re-checked immediately before the attach call:
    /// a non-owner attach writes a replicated flag it has no authority over, and
    /// the attach detaches every loaded cart first. Null when both hold.
    /// </summary>
    public static AttachResult? GuardBeforeAttach(bool viewValid, bool isOwner, bool anyJointOnClient)
    {
        if (!viewValid || !isOwner)
        {
            return AttachResult.Refused(HitchRefusal.NotOwnedHere, "this client does not own the cart at the moment of attaching");
        }

        if (anyJointOnClient)
        {
            return AttachResult.Refused(HitchRefusal.OtherJointOnClient, "a cart on this client holds a joint at the moment of attaching");
        }

        return null;
    }

    /// <summary>Verification right after the attach call (D4, D5). Null when
    /// verified; otherwise the exact problem, and the caller detaches again.
    /// </summary>
    public static string? VerifyAfterAttach(
        bool jointExists,
        bool connectedToPuller,
        bool cartReportsAttached,
        bool attachFlagSet,
        float pullerMassKg,
        float expectedMassKg,
        HaulExecutionLimits execution)
    {
        if (execution == null)
        {
            throw new ArgumentNullException(nameof(execution));
        }

        if (!jointExists)
        {
            return "no joint was created";
        }

        if (!connectedToPuller)
        {
            return "the joint is not connected to Gunnar's rigidbody";
        }

        if (!cartReportsAttached)
        {
            return "the cart does not report itself attached";
        }

        if (!attachFlagSet)
        {
            return "the cart's attach flag is not set";
        }

        if (!execution.MassesAgree(pullerMassKg, expectedMassKg))
        {
            return FormattableString.Invariant($"Gunnar weighs {pullerMassKg:0.##} kg attached, expected {expectedMassKg:0.##} kg");
        }

        return null;
    }

    /// <summary>Whether a release may call the cart's detach. Gunnar's joint is
    /// released whatever the body binding says (an unbound or dying Gunnar is
    /// still Gunnar); a joint connected to nothing, or no joint, is released too
    /// (vanilla's next update would do the same); only a joint another living
    /// body holds is left alone.</summary>
    public static JointReleaseDecision DecideRelease(JointHolderFacts facts)
    {
        if (!facts.JointExists || facts.ConnectedToNothing || facts.IsGunnars)
        {
            return JointReleaseDecision.Detach;
        }

        return JointReleaseDecision.LeaveAlone;
    }
}
