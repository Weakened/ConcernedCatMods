using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>The three durable key names one role's body stores itself under,
/// composed from that role's own prefix and nothing else.
///
/// <b>This type is the hinge the whole migration turns on.</b> Two shipped
/// products write three keys each into their own network object, and every
/// worker saved in every existing world is found again by them:
/// <c>&lt;prefix&gt;key</c>, <c>&lt;prefix&gt;inventory</c> and
/// <c>&lt;prefix&gt;revision</c>, where the prefix is the role's - two roles
/// share one today and a third does not. The code that reads and writes them is
/// unified here. The names are not: the prefix arrives in
/// <see cref="NpcBodyContract.ZdoKeyPrefix"/>, from the role, and this library
/// supplies no default for it and no fallback for a missing one.
///
/// <b>What a wrong name costs.</b> Nothing throws. The body stands up, reads an
/// empty identity and an empty inventory, and saves them back over the real
/// ones on the first change - so a player loses a tool, a hammer and a
/// half-carried load with no error anywhere. That is why this is a separate
/// type with its own golden test rather than three concatenations at three call
/// sites, and why <see cref="TryFor"/> refuses instead of guessing.
///
/// <b>Why the three suffixes live here and the prefix does not.</b> The
/// suffixes are identical in both shipped products - the drift between them is
/// entirely in the prefix - so unifying them changes no byte on anybody's disk,
/// while unifying the prefix would orphan every saved body of one of the two.
/// The rule the architecture states as "the code is unified, the keys never
/// are" is exactly this split, and it is the reason a suffix may be a literal
/// here while a prefix may not.</summary>
internal readonly struct NpcBodyFields
{
    private readonly string? _key;
    private readonly string? _inventory;
    private readonly string? _revision;

    private NpcBodyFields(string key, string inventory, string revision)
    {
        _key = key;
        _inventory = inventory;
        _revision = revision;
    }

    /// <summary>The key the body's identity is stored under.</summary>
    internal string Key => _key ?? string.Empty;

    /// <summary>The key the body's carried inventory is stored under, in
    /// vanilla's own inventory package format - the bytes a chest stores.
    /// </summary>
    internal string Inventory => _inventory ?? string.Empty;

    /// <summary>The key the body's write counter is stored under.</summary>
    internal string Revision => _revision ?? string.Empty;

    /// <summary>A defaulted value, which names nothing. A body holding one is
    /// inert; it never reads and never writes.</summary>
    internal bool IsEmpty => string.IsNullOrEmpty(_key);

    /// <summary>Composes the three names for a worker contract, or says why it
    /// cannot.
    ///
    /// Refuses a presentation contract outright rather than returning empty
    /// names: a presentation body stores nothing anywhere, and a caller that
    /// asked for its keys has confused the two kinds - which is the confusion
    /// the never-coexist rule exists to prevent, arriving one layer lower.
    /// </summary>
    internal static bool TryFor(NpcBodyContract contract, out NpcBodyFields fields, out string reason)
    {
        fields = default;

        if (!contract.TryValidate(out reason))
        {
            return false;
        }

        if (contract.Kind != NpcBodyKind.Worker)
        {
            reason = "only a body the world saves stores anything, and this contract is for a "
                + contract.Kind + " body, which stores nothing";
            return false;
        }

        string prefix = contract.ZdoKeyPrefix;
        fields = new NpcBodyFields(prefix + "key", prefix + "inventory", prefix + "revision");
        reason = string.Empty;
        return true;
    }

    public override string ToString() => IsEmpty ? "<none>" : Key + ", " + Inventory + ", " + Revision;
}
