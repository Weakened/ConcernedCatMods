using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>The one question asked immediately before a body is built, and the
/// only door to building one.
///
/// <b>Why a lease is a parameter and not a courtesy.</b> The arbiter hands out
/// a <see cref="BodyLease"/> on a grant and only on a grant, so a role that was
/// refused has nothing to pass and cannot reach this method with a value. That
/// closes the half of the never-coexist rule a discipline cannot close. It does
/// not close the other half on its own, which is the point of the second check
/// below.
///
/// <b>Why <see cref="BodyLease.IsActive"/> is re-asked here rather than
/// trusted.</b> Taking a lease proves permission existed at the moment of the
/// claim. Building a body needs permission to exist at the moment of the build,
/// and the two are not the same instant: a claim is taken by a runtime that
/// then waits for ground to load, for a census to finish, or for a player to
/// confirm, and in between the world can unload, the holder can dispose the
/// lease in a <c>finally</c>, or another kind of body can be claimed for the
/// same identity. A lease that has expired in that gap still exists as an
/// object and still answers every question about who it was for. Only
/// <see cref="BodyLease.IsActive"/> knows it is worthless, and only asking it
/// here - last, after every other refusal, with nothing between this and the
/// instantiation - makes the answer current.
///
/// <b>Why the whole gate is game-free.</b> Every refusal on this list is a
/// decision about permission, identity and counting, none of which needs Unity.
/// It is therefore testable exactly, including the expiry race, rather than
/// being read and believed.</summary>
internal static class NpcBodyBuildGate
{
    /// <summary>Decides whether one body may be built now.</summary>
    /// <param name="lease">The permission the arbiter granted. Null is a
    /// refusal, not a default: a caller with no lease was never granted
    /// one.</param>
    /// <param name="contract">The role's body contract, already validated at
    /// registration. Re-checked here because a caller reaching this method with
    /// an invalid one would be about to register a prefab under an empty
    /// name.</param>
    /// <param name="tally">What the census found for this identity.</param>
    /// <param name="prefabReady">The role's prefab is built and registered.
    /// </param>
    /// <param name="prefabFailure">Why it is not, for the refusal
    /// sentence.</param>
    /// <param name="groundLoaded">The destination is inside loaded ground. A
    /// body built into unloaded ground is a body nobody owns.</param>
    internal static NpcBuildPermission May(
        BodyLease? lease,
        NpcBodyContract contract,
        NpcBodyTally tally,
        bool prefabReady,
        string? prefabFailure,
        bool groundLoaded)
    {
        if (lease == null)
        {
            return NpcBuildPermission.Refused(
                "no lease was supplied, so nothing granted permission for this body");
        }

        if (!contract.TryValidate(out string contractReason))
        {
            return NpcBuildPermission.Refused("the role's body contract is not usable: " + contractReason);
        }

        if (contract.Kind != lease.Kind)
        {
            return NpcBuildPermission.Refused(
                "the lease permits a " + lease.Kind + " body and this contract builds a " + contract.Kind + " one");
        }

        if (lease.Identity.IsEmpty)
        {
            return NpcBuildPermission.Refused("the lease names no identity");
        }

        if (!tally.Identity.Equals(lease.Identity))
        {
            // Counting one identity's bodies and building another's is how two
            // bodies end up carrying one key: the census that would have
            // refused was taken for somebody else.
            return NpcBuildPermission.Refused(
                "the census counted " + tally.Identity + " and the lease is for " + lease.Identity);
        }

        if (!prefabReady)
        {
            return NpcBuildPermission.Refused(
                "the role's prefab is not registered"
                + (string.IsNullOrEmpty(prefabFailure) ? string.Empty : " (" + prefabFailure + ")"));
        }

        if (!tally.MaySpawn)
        {
            return NpcBuildPermission.Refused(
                "the census says " + tally.Presence + ", and a body is built only when a finished census found none");
        }

        if (!groundLoaded)
        {
            return NpcBuildPermission.Refused("that ground is not loaded, so a body built there would be owned by nobody");
        }

        // LAST, and nothing may be added below it. Everything above is a fact
        // that was already true when this method was entered; this is the one
        // question whose answer can have changed since the claim, so it is the
        // one asked closest to the build.
        if (!lease.IsActive)
        {
            return NpcBuildPermission.Refused(
                "the lease for " + lease.Identity + " is no longer held, so permission to build this body has gone");
        }

        return NpcBuildPermission.Granted();
    }
}
