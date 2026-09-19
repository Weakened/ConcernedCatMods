using System;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedSteward.Domain.Npc;

/// <summary>Where the Steward's data lives, answered by the Steward.
///
/// <b>The library never composes a path and this is how it stays true.</b>
/// <see cref="INpcDataPaths"/> asks by purpose; this answers with an absolute
/// directory the product already owns, and refuses every purpose token, because
/// today the Steward keeps exactly one file and the library reads and writes
/// none of it. When a leaf declares its first purpose constant, the answer to
/// it is added here — deliberately, by somebody who can see
/// <c>StewardRecordStore</c> and its
/// <c>&lt;scope&gt;.steward.tsv</c> naming beside it.
///
/// <b>Why refusing every purpose is the safe answer rather than a stub.</b> A
/// role that guessed a path for an unknown token would let a shared runtime
/// drop a file into this product's data root that this product's own loader
/// does not know about, which is the shape of the defect the interface's own
/// documentation names (#343). Refusing switches the feature off; guessing
/// invents a file.</summary>
internal sealed class StewardDataPaths : INpcDataPaths
{
    internal StewardDataPaths(string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            throw new ArgumentException("The Steward's data root is required.", nameof(root));
        }

        Root = root;
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public bool TryResolveFile(string purpose, out string absolutePath)
    {
        // No purpose token exists yet, in this library or anywhere else. An
        // unrecognised one is a refusal and never a composed name.
        _ = purpose;
        absolutePath = string.Empty;
        return false;
    }
}

/// <summary>The Steward, as Concerned NPC sees him.
///
/// <b>Three facts, all of them already on a player's disk, none of them new.</b>
/// The identity is <c>steward/steward</c>, character for character the value of
/// the <c>WorkerKey</c> this product already writes into its record rows. The
/// prefab name is <see cref="StewardRole.BodyPrefabName"/>, which every saved
/// Steward body is found by. The key prefix is <c>tcc.steward.</c>, which
/// composes exactly the three keys <c>StewardBody</c> already
/// reads and writes: <c>tcc.steward.key</c>, <c>tcc.steward.inventory</c> and
/// <c>tcc.steward.revision</c>.
///
/// <b>Nothing here changes a byte.</b> This type states durable facts that
/// already exist; it does not introduce one. That is the whole shape of the
/// adoption rule in <c>docs/mods/concerned-npc/ARCHITECTURE.md</c> §3 — the code
/// is unified, the keys never are — and it is why registering the Steward with
/// the library is not a migration.
///
/// <b>What this does NOT do.</b> It does not hand the library a prefab and it
/// does not ask the library to build a body. The Steward still registers his own
/// prefab, from his own plugin start, through
/// <c>StewardWorkerPrefab</c> — because a prefab registered under
/// a different name, or later than the main menu, deletes every existing saved
/// Steward on the next load. Moving the body half onto
/// <c>NpcWorkerPrefabFactory</c> is #372's adoption and is deliberately not part
/// of #382.</summary>
internal sealed class StewardNpcRole : INpcRole
{
    internal StewardNpcRole(string dataRoot)
    {
        Paths = new StewardDataPaths(dataRoot);
    }

    /// <summary>The Steward's identity, in the library's vocabulary.
    ///
    /// <c>steward/steward</c>: the same text as <c>WorkerKey.Steward.Value</c>,
    /// which is what this product's journal rows and capability boundary already
    /// carry. The conversion is total in both directions by construction, and
    /// <c>StewardNpcRoleTests</c> asserts it rather than assuming it.</summary>
    internal static NpcIdentity Id { get; } =
        new NpcIdentity(StewardRole.Product, StewardRole.RoleKey);

    /// <inheritdoc />
    public NpcIdentity Identity => Id;

    /// <inheritdoc />
    public NpcBodyContract Body { get; } =
        NpcBodyContract.ForWorker(StewardRole.BodyPrefabName, StewardRole.BodyKeyPrefix);

    /// <inheritdoc />
    public INpcDataPaths Paths { get; }
}
