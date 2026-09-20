using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>An answer to "may this NPC use that chest, for this, now": either a
/// permit or a refusal that names the thing to go and change.
///
/// <b>What it guarantees: the two halves cannot both be read.</b> A granted
/// authorisation carries a permit and no refusal; a refused one carries a
/// refusal and no permit. The constructors are the only place that pairing is
/// decided.</summary>
internal readonly struct NpcContainerAuthorization
{
    private NpcContainerAuthorization(
        NpcContainerPermit? permit, NpcContainerRefusal refusal, NpcContainerUse requested, string reason)
    {
        Permit = permit;
        Refusal = refusal;
        Requested = requested;
        Reason = reason;
    }

    /// <summary>The permit, or null. Null for every refusal.</summary>
    internal NpcContainerPermit? Permit { get; }

    /// <summary>Why not, or <see cref="NpcContainerRefusal.None"/>.</summary>
    internal NpcContainerRefusal Refusal { get; }

    /// <summary>What was asked for.</summary>
    internal NpcContainerUse Requested { get; }

    /// <summary>What happened, in a sentence.
    ///
    /// <b>Evidence, not dialogue.</b> This is the register the shipped gate
    /// already writes in - "not the same chest", "in use", "access denied" -
    /// raised to name which access was denied, because a player told only that
    /// it does not work has been told nothing. A role is free to render its own
    /// wording from <see cref="Refusal"/>; this is what it renders from, and
    /// what goes in a log when nobody does.</summary>
    internal string Reason { get; }

    /// <summary>The one question a caller asks.</summary>
    internal bool IsGranted => Permit != null && Refusal == NpcContainerRefusal.None;

    internal static NpcContainerAuthorization Granted(NpcContainerPermit permit, NpcContainerUse requested) =>
        new NpcContainerAuthorization(permit, NpcContainerRefusal.None, requested, string.Empty);

    internal static NpcContainerAuthorization Refused(
        NpcContainerRefusal refusal, NpcContainerUse requested, string reason) =>
        new NpcContainerAuthorization(null, refusal, requested, reason ?? string.Empty);
}

/// <summary>Where a container's permission is decided, and the only place it is.
///
/// <b>Two jobs, deliberately apart.</b> <see cref="Judge"/> turns what a role's
/// adapter saw into the access a container reports; <see cref="Authorize"/>
/// turns that access plus a player's allowance into a permit or a refusal. The
/// first is about the world and is the same for every use; the second is about
/// one direction of one transfer and is asked again every time.
///
/// <b>What is not here, and never will be: a way to find a container.</b> There
/// is no "the nearest enabled chest", no candidate list, no search. A job names
/// the containers it uses and the player names the ones an NPC may touch, and
/// the intersection of those two is the whole of what is allowed. Anything that
/// could pick a chest by distance would be the mechanism for depositing a
/// player's cargo into an arbitrary nearby one, and the cheapest way to
/// guarantee it never happens is for the code not to exist - which is why this
/// file does no distance arithmetic at all, and why the only distance fact in
/// the model arrives pre-answered from the adapter as
/// <see cref="NpcContainerSighting.WithinReach"/>.</summary>
internal static class NpcContainerGate
{
    /// <summary>Turns a sighting into the access a container reports, given what
    /// the player allowed for it.
    ///
    /// <b>Order is the enum's order, and that is the rule rather than a list of
    /// cases.</b> Existence first, then identity, then ownership, then who has
    /// it open, then the ward, then privacy, then reach. Reach last because it
    /// is the only one that is not a fault: walk closer and ask again.
    ///
    /// <b>The ward and privacy are two refusals, not one.</b> The shipped gate
    /// collapses them into "access denied", and the fix for each is a different
    /// object in the world. Adopting this gate will make some already-working
    /// containers refuse - a depot inside somebody else's ward, a chest set to
    /// Private and built by another character - so the refusal has to say which,
    /// or a player is left to guess.</summary>
    internal static NpcContainerAccess Judge(NpcContainerUse allowed, NpcContainerSighting now)
    {
        return new NpcContainerAccess(allowed, WorldRefusal(now));
    }

    /// <summary>Asks for permission to do one thing to one container, right now.
    ///
    /// Reads the container's access at this moment - never a decision made on an
    /// earlier tick - and hands back a permit only if both the player and the
    /// world say yes.</summary>
    internal static NpcContainerAuthorization Authorize(
        INpcContainer? container, NpcContainerUse use, NpcWorldEpoch world)
    {
        NpcContainerPermit? permit = NpcContainerPermit.Issue(container, use, world, out NpcContainerRefusal refusal);
        if (permit != null)
        {
            return NpcContainerAuthorization.Granted(permit, use);
        }

        string name = Name(container);
        return NpcContainerAuthorization.Refused(refusal, use, name + " " + Because(refusal, use));
    }

    private static NpcContainerRefusal WorldRefusal(NpcContainerSighting now)
    {
        if (!now.Exists)
        {
            return NpcContainerRefusal.Gone;
        }

        if (!now.IdentityMatches)
        {
            return NpcContainerRefusal.NotThisContainer;
        }

        if (!now.OwnedHere)
        {
            return NpcContainerRefusal.NotOwnedHere;
        }

        if (now.InUse)
        {
            return NpcContainerRefusal.InUse;
        }

        if (!now.WardAllows)
        {
            return NpcContainerRefusal.WardDenied;
        }

        if (!now.PrivacyAllows)
        {
            return NpcContainerRefusal.PrivacyDenied;
        }

        if (now.ReachAsked && !now.WithinReach)
        {
            return NpcContainerRefusal.OutOfReach;
        }

        return NpcContainerRefusal.None;
    }

    private static string Name(INpcContainer? container)
    {
        if (container == null)
        {
            return "that container";
        }

        try
        {
            string describe = container.Describe ?? string.Empty;
            return describe.Length > 0 ? describe : "that container";
        }
        catch (System.Exception)
        {
            return "that container";
        }
    }

    /// <summary>One clause per refusal, naming what a person would go and
    /// change. Not a phrase table for a user interface and not localised text: a
    /// role renders its own wording, and this is the evidence line that goes in
    /// a log and into an attention reason when nobody does - which is exactly
    /// what the shipped gate's short strings are used for today.</summary>
    private static string Because(NpcContainerRefusal refusal, NpcContainerUse use)
    {
        switch (refusal)
        {
            case NpcContainerRefusal.Gone:
                return "is not there";

            case NpcContainerRefusal.NotThisContainer:
                return "is not the container that was marked; the world was reloaded and the name now points " +
                    "somewhere else";

            case NpcContainerRefusal.NotEnabled:
                return "has not been opened to helpers; every container starts closed to them";

            case NpcContainerRefusal.UseNotAllowed:
                return use == NpcContainerUse.Take
                    ? "may be put into, but not taken from"
                    : "may be taken from, but not put into";

            case NpcContainerRefusal.NotOwnedHere:
                return "is not ours to write to from here, and a write that is not ours is thrown away unseen";

            case NpcContainerRefusal.InUse:
                return "is open; anything put in now would be overwritten when that window closes";

            case NpcContainerRefusal.WardDenied:
                return "is inside a ward that refuses. The ward is what to change";

            case NpcContainerRefusal.PrivacyDenied:
                return "is private to whoever built it. The chest's own privacy setting is what to change";

            case NpcContainerRefusal.OutOfReach:
                return "is too far away from where he is standing";

            default:
                return "was not permitted";
        }
    }
}
