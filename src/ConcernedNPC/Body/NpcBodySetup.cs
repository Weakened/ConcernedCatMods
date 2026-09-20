using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Everything a body needs to know about itself when the host builds
/// it out of a save: which contract it belongs to, which key names that gives
/// it, and what it stores.
///
/// Composed once, at registration, from facts the role supplied, so the
/// composition can be refused there rather than in a <c>Start</c> that has no
/// caller to tell.</summary>
internal readonly struct NpcBodySetup
{
    private NpcBodySetup(NpcBodyContract contract, NpcBodyKeeps keeps, NpcBodyFields fields)
    {
        Contract = contract;
        Keeps = keeps;
        Fields = fields;
    }

    internal NpcBodyContract Contract { get; }

    internal NpcBodyKeeps Keeps { get; }

    internal NpcBodyFields Fields { get; }

    internal bool IsEmpty => Fields.IsEmpty;

    /// <summary>Composes a setup, or says exactly which fact is wrong.</summary>
    internal static bool TryCompose(
        NpcBodyContract contract, NpcBodyKeeps keeps, out NpcBodySetup setup, out string reason)
    {
        setup = default;

        if (keeps != NpcBodyKeeps.IdentityOnly && keeps != NpcBodyKeeps.IdentityAndInventory)
        {
            reason = "a role must say what its body stores; leaving it unstated would make this library "
                + "choose whether a player's carried items survive a reload";
            return false;
        }

        if (!NpcBodyFields.TryFor(contract, out NpcBodyFields fields, out reason))
        {
            return false;
        }

        setup = new NpcBodySetup(contract, keeps, fields);
        reason = string.Empty;
        return true;
    }

    public override string ToString() => Contract + " " + Keeps;
}
