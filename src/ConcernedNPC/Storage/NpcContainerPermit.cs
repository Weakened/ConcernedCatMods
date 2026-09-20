using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>Permission to move things one way, into or out of one container,
/// once.
///
/// <b>What it guarantees, and it is the whole reason the type exists: there is
/// no way to hold one of these for a container that refused.</b> The constructor
/// is private and <see cref="Issue"/> is the only mint, and
/// <see cref="Issue"/> re-asks the container's own access at the moment of
/// minting rather than trusting anything a caller passed in. A job that was
/// refused therefore has nothing to hand to the thing that records a transfer,
/// and "ask permission first" stops being a discipline every future leaf has to
/// remember. This is the same shape as the body lease the arbiter hands out, for
/// the same reason.
///
/// <b>One direction.</b> Never both. A permit authorises a take or a deposit,
/// and a job that does both against one chest asks twice - because the player's
/// two trusts are separate, and a permit that meant "whatever you like with this
/// chest" would quietly merge them.
///
/// <b>Once.</b> A permit is spent by the transfer that uses it, and a spent one
/// is refused. That is what stops an interruption turning into a duplicate: a
/// job that comes back not knowing whether its transfer happened cannot replay
/// the old permit, it has to ask again - and asking again re-reads the
/// container, which is the only honest way to find out.
///
/// <b>One world load.</b> A permit carries the epoch it was issued in, because
/// the container key inside it is a name that a reload reassigns to something
/// else.</summary>
internal sealed class NpcContainerPermit
{
    private NpcContainerPermit(string containerKey, string describe, NpcContainerUse use, NpcWorldEpoch epoch)
    {
        ContainerKey = containerKey;
        Describe = describe;
        Use = use;
        Epoch = epoch;
    }

    /// <summary>The container this authorises, as the role names it. Only
    /// meaningful inside <see cref="Epoch"/>.</summary>
    internal string ContainerKey { get; }

    /// <summary>What to call it in a sentence shown to a player.</summary>
    internal string Describe { get; }

    /// <summary>Exactly one of take or deposit.</summary>
    internal NpcContainerUse Use { get; }

    /// <summary>The world load this was issued in.</summary>
    internal NpcWorldEpoch Epoch { get; }

    /// <summary>Whether the transfer this authorises has already been
    /// recorded.</summary>
    internal bool IsSpent { get; private set; }

    /// <summary>Whether this permit still means anything in the world that is
    /// loaded now. False after a reload, always: the key it carries names a
    /// different object.</summary>
    internal bool IsValidIn(NpcWorldEpoch world) => !IsSpent && world.Matches(Epoch);

    /// <summary>Spends it. True the first time and false every time after, so
    /// the caller that gets true is the only one that may record a
    /// transfer.</summary>
    internal bool TryConsume()
    {
        if (IsSpent)
        {
            return false;
        }

        IsSpent = true;
        return true;
    }

    /// <summary>The only mint. Returns null for every refusal, and the refusal
    /// says which.
    ///
    /// The container's <c>Access</c> is read here, now, rather than taken from
    /// the caller - the seam's own comment says permission is asked at the
    /// moment of use and never cached into a decision made earlier, and this is
    /// where that is made true. Between the tick that chose this chest and this
    /// one, the player can have walked into it, a ward can have gone up, the
    /// chest can have been destroyed.</summary>
    internal static NpcContainerPermit? Issue(
        INpcContainer? container, NpcContainerUse use, NpcWorldEpoch world, out NpcContainerRefusal refusal)
    {
        refusal = NpcContainerRefusal.Gone;

        if (container == null)
        {
            return null;
        }

        if (use != NpcContainerUse.Take && use != NpcContainerUse.Deposit)
        {
            // Off authorises nothing, and Both is two trusts in one token.
            refusal = NpcContainerRefusal.UseNotAllowed;
            return null;
        }

        string key;
        string describe;
        NpcWorldEpoch named;
        NpcContainerAccess access;
        try
        {
            key = container.Key ?? string.Empty;
            describe = container.Describe ?? string.Empty;
            named = container.Epoch;
            access = container.Access;
        }
        catch (System.Exception)
        {
            // A container that cannot say what it is has not said yes.
            refusal = NpcContainerRefusal.Gone;
            return null;
        }

        if (!world.Matches(named))
        {
            // The name was minted in another world load, so it now points at
            // whatever loaded into that slot this time. Refused, never resolved.
            refusal = NpcContainerRefusal.NotThisContainer;
            return null;
        }

        NpcContainerRefusal verdict = Verdict(access, use);
        if (verdict != NpcContainerRefusal.None)
        {
            refusal = verdict;
            return null;
        }

        refusal = NpcContainerRefusal.None;
        return new NpcContainerPermit(key, describe, use, world);
    }

    /// <summary>The one refusal to report, when several apply.
    ///
    /// <b>The lowest-numbered one.</b> The enum is written in the order a person
    /// would want to hear them: is it even there, is it the one we meant, did
    /// you switch it on, did you switch on <i>this</i>, then the world's own
    /// objections, then distance - which is not a fault at all. So the rule is
    /// mechanical rather than a list of special cases, and adding a value to the
    /// enum puts it in its place without touching this.</summary>
    private static NpcContainerRefusal Verdict(NpcContainerAccess access, NpcContainerUse use)
    {
        NpcContainerRefusal world = access.Refusal;
        NpcContainerRefusal bySetting = Allowance(access.Allowed, use);

        if (world == NpcContainerRefusal.None)
        {
            return bySetting;
        }

        if (bySetting == NpcContainerRefusal.None)
        {
            return world;
        }

        return (int)world <= (int)bySetting ? world : bySetting;
    }

    /// <summary>What the player's own setting says, read from the allowance
    /// alone.
    ///
    /// <b>Deliberately not <c>Access.Permits</c>.</b> That property answers the
    /// only question a caller normally has - may this happen right now - and to
    /// do so it requires both halves, so it reads false for a container the
    /// player fully enabled that happens to be behind a ward. Using it here
    /// would report every warded chest as one the player never switched on, and
    /// send them to fix the wrong thing. Two facts, asked separately, reported
    /// as whichever matters more.</summary>
    private static NpcContainerRefusal Allowance(NpcContainerUse allowed, NpcContainerUse use)
    {
        if ((allowed & NpcContainerUse.Both) == NpcContainerUse.Off)
        {
            return NpcContainerRefusal.NotEnabled;
        }

        return (allowed & use) == use ? NpcContainerRefusal.None : NpcContainerRefusal.UseNotAllowed;
    }
}
