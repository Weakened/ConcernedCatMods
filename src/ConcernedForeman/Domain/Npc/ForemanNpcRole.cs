using System;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Where Concerned Foreman's data lives, answered by Concerned Foreman.
///
/// <b>The library never composes a path, and this is how that stays true.</b>
/// <see cref="INpcDataPaths"/> asks by purpose; this answers with an absolute
/// directory this product already owns, and refuses every purpose token, because
/// the library reads and writes none of Foreman's files. When a leaf declares
/// its first purpose constant, the answer to it is added here — deliberately, by
/// somebody who can see <c>SettlementRegisterStore</c> and <c>JournalStore</c>
/// and their naming beside it.
///
/// <b>Why refusing an unknown purpose is safer than guessing one.</b> A role
/// that invented a path for a token it did not recognise would let a shared
/// runtime drop a file into this product's data root that this product's own
/// loader knows nothing about (#343). Refusing switches the feature off;
/// guessing invents a file.</summary>
internal sealed class ForemanDataPaths : INpcDataPaths
{
    internal ForemanDataPaths(string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            throw new ArgumentException("Concerned Foreman's data root is required.", nameof(root));
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

/// <summary>Thorstein, as Concerned NPC sees him.
///
/// <b>Three facts, all of them already on a player's disk, none of them new.</b>
/// The identity is <c>foreman/thorstein</c>, character for character the value of
/// the <c>WorkerKey</c> this product already writes into its journal rows and
/// passes across the capability boundary. The prefab name is
/// <see cref="ForemanRole.BodyPrefabName"/>, which every saved worker body is
/// found by. The key prefix is <see cref="ForemanRole.BodyKeyPrefix"/>, which
/// composes exactly the three keys <c>WorkerBody</c> already reads and writes.
///
/// <b>Nothing here changes a byte.</b> This type states durable facts that
/// already exist; it introduces none. That is the whole shape of the adoption
/// rule in <c>docs/mods/concerned-npc/ARCHITECTURE.md</c> §3 — the code is
/// unified, the keys never are — and it is why registering Thorstein with the
/// library is not a migration.
///
/// <b>What this does NOT do.</b> It does not hand the library a prefab and it
/// does not ask the library to build a body. Foreman still registers its own
/// prefab, from its own plugin start, through <c>ForemanWorkerPrefab</c> —
/// because a prefab registered under a different name, or later than the main
/// menu, deletes every existing saved worker on the next load. Moving the body
/// half onto <c>NpcWorkerPrefabFactory</c> is a separate adoption and is
/// deliberately not part of this one.</summary>
internal sealed class ForemanNpcRole : INpcRole
{
    internal ForemanNpcRole(string dataRoot)
    {
        Paths = new ForemanDataPaths(dataRoot);
    }

    /// <summary>Thorstein's identity, in the library's vocabulary.
    ///
    /// <c>foreman/thorstein</c>: the same text as
    /// <c>WorkerKey.Thorstein.Value</c>, which is what this product's journal
    /// rows and capability boundary already carry. The conversion is total in
    /// both directions by construction, and <c>ForemanNpcAdoptionTests</c> asserts it
    /// rather than assuming it.</summary>
    internal static NpcIdentity Id { get; } =
        new NpcIdentity(ForemanRole.Product, ForemanRole.RoleKey);

    /// <inheritdoc />
    public NpcIdentity Identity => Id;

    /// <inheritdoc />
    public NpcBodyContract Body { get; } =
        NpcBodyContract.ForWorker(ForemanRole.BodyPrefabName, ForemanRole.BodyKeyPrefix);

    /// <inheritdoc />
    public INpcDataPaths Paths { get; }
}
