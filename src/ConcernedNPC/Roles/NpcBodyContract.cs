namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>The durable facts of one role's body, supplied <b>by the role</b>:
/// the name its prefab is registered under, the prefix of the keys it stores
/// itself beneath, and which of the two kinds of body it is.
///
/// <b>What this type guarantees: that unifying the code never unifies the
/// keys.</b> Every one of these facts is a string literal in somebody's shipped
/// product today, and every saved NPC in every existing world is found again by
/// them. They are data passed in here, never constants declared here.
/// <c>ConcernedNPC</c> supplies no default for either string and no fallback for
/// a missing one.
///
/// <b>What the audit that backs this actually checks</b>, stated plainly
/// because the version of this sentence that said "fails the build if anything
/// in it so much as writes a prefab name" claimed more than it did.
/// <c>ContractLiteralTests</c> reads this library's own sources and refuses
/// four things: a string literal that is one of the three shipped prefab names
/// or is prefab-shaped; a literal containing a mod key prefix; an identifier
/// shaped like a prefab name; and - in every file but the plugin entry point,
/// which legitimately owns this package's own BepInEx identity - any string
/// constant, interpolated string or verbatim string at all, because each of
/// those is a way to assemble a name that reading literals cannot see.
/// Adjacent literal concatenation is folded before the literal checks, so
/// splitting a name in two does not hide it. What it cannot catch is a name
/// assembled from values computed at run time; the constant ban is what makes
/// that hard rather than impossible, and this comment is what stops the next
/// reader believing it is airtight.
///
/// <b>Why that rule is worth a test.</b> The host destroys any saved object
/// whose prefab is not registered when a world's objects are created - it logs
/// "Destroyed invalid prefab ZDO" and moves on. So if this library ever
/// registered bodies under a name of its own, the first load after the change
/// would silently delete every existing Thorstein, Gunnar and Steward, with the
/// player's axe, hammer and gathered stone inside them, before any migration
/// code could run. One tier down, changing <see cref="ZdoKeyPrefix"/> leaves the
/// body standing but empties it with no evidence at all. Neither is
/// recoverable, so neither is left to reviewer attention.
///
/// The code is unified. The keys never are. And each role still registers its
/// own prefab, from its own plugin start, forever.</summary>
public readonly struct NpcBodyContract
{
    private readonly string? _prefabName;
    private readonly string? _zdoKeyPrefix;

    private NpcBodyContract(NpcBodyKind kind, string prefabName, string zdoKeyPrefix)
    {
        Kind = kind;
        _prefabName = prefabName;
        _zdoKeyPrefix = zdoKeyPrefix;
    }

    /// <summary>Which of the two ways this role's body exists. Drives every
    /// other rule on this type, and the arbiter's never-coexist refusal.</summary>
    public NpcBodyKind Kind { get; }

    /// <summary>The exact name the role registers its prefab under - today
    /// <c>CF_SettlementWorker</c>, <c>CT_TeamsterWorker</c> or <c>CS_Steward</c>
    /// - and the name saved bodies are found by. Empty for a presentation body,
    /// which has no registered prefab. <b>Changing it for a shipped role deletes
    /// every existing body of that role on the next load.</b></summary>
    public string PrefabName => _prefabName ?? string.Empty;

    /// <summary>The prefix, ending in a dot, of every key this role's body
    /// stores itself beneath in its own network object - today <c>tcc.worker.</c>
    /// or <c>tcc.steward.</c>. Empty for a presentation body, which stores
    /// nothing. Two roles may share a prefix (Foreman and Teamster do); they may
    /// never share a <see cref="PrefabName"/>, because the prefab is what
    /// separates their bodies before the key is ever read.</summary>
    public string ZdoKeyPrefix => _zdoKeyPrefix ?? string.Empty;

    /// <summary>The contract for a local-only figure: no prefab, no keys,
    /// nothing saved. Takes no arguments because there is nothing durable about
    /// a presentation body to get wrong.</summary>
    public static NpcBodyContract ForPresentation() =>
        new NpcBodyContract(NpcBodyKind.Presentation, string.Empty, string.Empty);

    /// <summary>The contract for a body the world saves. Both facts are
    /// required and neither has a default: a role that cannot name its prefab
    /// and its key prefix has no business constructing a persistent body.
    /// </summary>
    /// <param name="prefabName">The name the role registers its prefab under.</param>
    /// <param name="zdoKeyPrefix">The role's key prefix, including the trailing dot.</param>
    public static NpcBodyContract ForWorker(string prefabName, string zdoKeyPrefix) =>
        new NpcBodyContract(NpcBodyKind.Worker, prefabName ?? string.Empty, zdoKeyPrefix ?? string.Empty);

    /// <summary>Builds a contract from facts whose shape is not known in
    /// advance - a kind and two strings that came from somewhere else.
    /// <b>Internal</b>: a role uses one of the two named factories above, which
    /// cannot produce a contradictory contract. This exists because
    /// <see cref="Validate"/> has to refuse combinations those two cannot
    /// build - a presentation body carrying durable facts, a kind that is
    /// neither - and a check no caller can reach is a check nobody can
    /// prove.</summary>
    internal static NpcBodyContract Compose(NpcBodyKind kind, string? prefabName, string? zdoKeyPrefix) =>
        new NpcBodyContract(kind, prefabName ?? string.Empty, zdoKeyPrefix ?? string.Empty);

    /// <summary>Whether this contract is internally consistent, and if not,
    /// exactly which fact is wrong. Checked before anything is registered, so a
    /// role with a malformed contract is refused whole rather than half
    /// installed.</summary>
    /// <summary>Whether this contract is internally consistent, and if not,
    /// exactly which fact is wrong - the same check registration runs, exposed
    /// so a role can ask before it offers itself rather than having to register
    /// and read the refusal back out of the outcome.</summary>
    public bool TryValidate(out string reason) => Validate(out reason);

    internal bool Validate(out string reason)
    {
        switch (Kind)
        {
            case NpcBodyKind.Presentation:
                // A presentation body is never saved, so durable facts on one
                // are not harmless extra detail - they are a sign that somebody
                // intends to persist a figure the safety rules say is local
                // only. Refuse rather than ignore.
                if (PrefabName.Length != 0)
                {
                    reason = "a presentation body has no registered prefab, but this contract names '"
                        + PrefabName + "'";
                    return false;
                }

                if (ZdoKeyPrefix.Length != 0)
                {
                    reason = "a presentation body stores nothing in the world, but this contract carries the "
                        + "key prefix '" + ZdoKeyPrefix + "'";
                    return false;
                }

                reason = string.Empty;
                return true;

            case NpcBodyKind.Worker:
                if (!IsValidPrefabName(PrefabName))
                {
                    reason = "a worker body needs the exact prefab name its role registers, 1-64 characters of "
                        + "letters, digits or '_'; saved bodies are found by it and a wrong one deletes them. "
                        + "Received: '" + PrefabName + "'";
                    return false;
                }

                if (!IsValidKeyPrefix(ZdoKeyPrefix))
                {
                    // The shape only. This message deliberately shows no
                    // example: the literals audit forbids every prefix-shaped
                    // string in this library, including one written down to be
                    // helpful, and an example here would be the first crack.
                    reason = "a worker body needs its role's key prefix, lower-case letters, digits, '-' or '.' "
                        + "ending in a dot; a wrong one empties the body with no evidence. Received: '"
                        + ZdoKeyPrefix + "'";
                    return false;
                }

                reason = string.Empty;
                return true;

            default:
                reason = "a body contract must state its kind; Unspecified is not a body";
                return false;
        }
    }

    public override string ToString() =>
        Kind == NpcBodyKind.Worker
            ? "Worker(" + PrefabName + ", " + ZdoKeyPrefix + "*)"
            : Kind.ToString();

    private static bool IsValidPrefabName(string value)
    {
        if (value.Length == 0 || value.Length > 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool allowed = (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidKeyPrefix(string value)
    {
        if (value.Length < 2 || value.Length > 64 || value[value.Length - 1] != '.' || value[0] == '.')
        {
            return false;
        }

        char previous = '\0';
        foreach (char character in value)
        {
            bool allowed = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-'
                || character == '.';
            if (!allowed || (character == '.' && previous == '.'))
            {
                return false;
            }

            previous = character;
        }

        return true;
    }
}
