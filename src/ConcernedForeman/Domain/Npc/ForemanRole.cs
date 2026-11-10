using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Thorstein's durable facts, in one place, because every one of them
/// is already on a player's disk.
///
/// <b>Nothing here is new.</b> Each constant is the exact text a shipped build
/// of Concerned Foreman already writes or reads: the identity is the
/// <c>WorkerKey</c> the settlement journal rows carry, the prefab name is the
/// one <c>ForemanWorkerPrefab</c> registers and every saved body is found by,
/// and the key prefix is the common head of the three literals
/// his body (<c>WorkerBody</c>, <c>NpcBody</c> since #372) has always stored itself under. They are gathered here so
/// that handing them to Concerned NPC as an <c>NpcBodyContract</c> does not
/// require a second spelling of any of them to appear anywhere.
///
/// <b>Why that matters more than tidiness.</b> The host destroys any saved
/// object whose prefab is not registered when a world's objects are created, so
/// a changed prefab name deletes every existing Thorstein — with the player's
/// axe, hammer and gathered stone inside him — before any migration code could
/// run. One tier down, a changed key prefix leaves the body standing and empties
/// it with no evidence at all. Neither is recoverable. So: <b>never change a
/// constant in this file.</b>
///
/// <b>Game-free on purpose.</b> This is the Domain half of the adoption, so the
/// test project can link it and assert byte-for-byte agreement with the
/// adapters that do the writing (<c>ForemanNpcAdoptionTests</c>). The adapters now
/// refer to these constants rather than repeating the literals, which is what
/// makes the agreement a compiler fact for the prefab name rather than a
/// review note.</summary>
internal static class ForemanRole
{
    /// <summary>Thorstein's permanent identity slug. It is the worker half of
    /// <see cref="Worker"/>, the value stamped into a saved body under
    /// <c>tcc.worker.key</c>, and the row key in the settlement record.</summary>
    public const string RoleKey = "thorstein";

    /// <summary>The product half of the worker key.</summary>
    public const string Product = WorkerKey.ForemanProduct;

    /// <summary>Thorstein, across products: <c>foreman/thorstein</c>. Taken from
    /// the shared table rather than rebuilt here, so the set of worker
    /// identities stays enumerable in one place beside Gunnar's and the
    /// Steward's.</summary>
    public static WorkerKey Worker => WorkerKey.Thorstein;

    /// <summary>Thorstein's identity inside a settlement record.</summary>
    public static WorkerId WorkerIdentity => new WorkerId(RoleKey);

    /// <summary>The name his body's prefab is registered under.
    ///
    /// <c>ForemanWorkerPrefab.PrefabName</c> is this constant, not a second copy
    /// of the same text, so the name the game registers and the name handed to
    /// Concerned NPC cannot drift apart.</summary>
    public const string BodyPrefabName = "CF_SettlementWorker";

    /// <summary>The prefix of every key his body stores itself under in its own
    /// network object, trailing dot included.
    ///
    /// It is the common head of the three literals his body has always
    /// written — <c>tcc.worker.key</c>, <c>tcc.worker.inventory</c>,
    /// <c>tcc.worker.revision</c>. Since #372 the body that writes them is
    /// Concerned NPC's <c>NpcBody</c>, which composes them from this prefix; the
    /// three stay spelled out in <c>ForemanSavedBodies</c>, where this product
    /// reads a saved body without loading it. A saved body is read by them, so
    /// both sides are pinned as literals - <c>ForemanNpcAdoptionTests</c> here
    /// and <c>BodyGoldenFixtureTests</c> in the library - and a change to either
    /// is a failing test rather than a change to a player's save file.</summary>
    public const string BodyKeyPrefix = "tcc.worker.";

    /// <summary>Validates that a key an adapter read back out of a save is
    /// Thorstein's. Ordinal, because an identity is bytes and not text.</summary>
    public static bool IsThorstein(string? workerKey) =>
        string.Equals(workerKey, Worker.Value, StringComparison.Ordinal);
}
