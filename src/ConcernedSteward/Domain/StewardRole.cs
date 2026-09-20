using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Domain;

/// <summary>Who the Steward is, and the one thing about him that is allowed to
/// change later.
///
/// <b>He has no name yet, and that is a decision rather than an omission.</b>
/// #340 says the proper name is owner-TBD. Inventing a placeholder that reads
/// like a real name is the failure mode worth avoiding: it gets into a player's
/// save, into screenshots, into a changelog, and then renaming him is a
/// migration instead of a text edit.
///
/// So the two are separated, permanently:
///
/// <list type="bullet">
/// <item><b><see cref="RoleKey"/> is the identity.</b> It is what the worker
/// key, the sidecar and the body's own network object are stamped with, and it
/// is the role — <c>steward</c> — never a person. It must never change, because
/// changing it orphans every saved body and every recruitment record that used
/// the old one.</item>
/// <item><b><see cref="DisplayNameFallback"/> is presentation.</b> It is what a
/// player is shown until somebody names him, and it is deliberately a
/// description rather than a name. Replacing it is a one-line change with no
/// data consequence at all.</item>
/// </list>
///
/// Keeping the two apart is the whole point. When the owner names him, that is
/// an edit to one string in this file; nothing in a save has to move.</summary>
internal static class StewardRole
{
    /// <summary>The Steward's permanent identity slug. <b>Never change this.</b>
    /// It is the worker half of <see cref="Worker"/>, the key stamped into a
    /// saved body, and the row key in the settlement record.</summary>
    public const string RoleKey = "steward";

    /// <summary>What a player sees. <b>The owner has named her: Sunniva</b>
    /// (#382).
    ///
    /// This is the change this file was written for. When #340 shipped, the
    /// name was owner-TBD and the value here was the description "the Steward",
    /// deliberately, so that naming her later would be an edit to one string
    /// rather than a migration. It is: <see cref="RoleKey"/>,
    /// <see cref="Worker"/> and <see cref="BodyPrefabName"/> are all unchanged,
    /// so every saved body and every record written under the old build is found
    /// by exactly the same identity it always was.
    ///
    /// The name is still not spelled into a sentence anywhere else. Every line
    /// the runtime builds refers to this constant, so renaming her again costs
    /// the same one edit it cost this time.</summary>
    public const string DisplayNameFallback = "Sunniva";

    /// <summary>The same, for the start of a sentence. Identical now that she
    /// has a proper name rather than a description; kept as its own constant
    /// because a future name might not be.</summary>
    public const string DisplayNameFallbackCapitalised = "Sunniva";

    /// <summary>The product half of the worker key.</summary>
    public const string Product = WorkerKey.StewardProduct;

    /// <summary>The Steward, across products: <c>steward/steward</c>.
    ///
    /// Taken from the shared table rather than built here, so the set of worker
    /// identities stays enumerable in one place beside Thorstein's and
    /// Gunnar's. Both halves are the role because there is no person yet; when
    /// he is named, <b>this key does not change</b> — a saved body is found by
    /// it, so the name lives in <see cref="DisplayNameFallback"/> where editing
    /// it costs nothing.</summary>
    public static WorkerKey Worker => WorkerKey.Steward;

    /// <summary>The Steward's identity inside a settlement record.</summary>
    public static WorkerId WorkerIdentity => new WorkerId(RoleKey);

    /// <summary>The Steward's role as the settlement layer records it.
    ///
    /// <c>WorkerRoles</c> in the shared recruitment area knows only
    /// <c>labourer</c>, and this product does not adopt that file — a steward
    /// is not a labourer and widening a shared enum from here would change
    /// Foreman's vocabulary for a product Foreman does not know about.</summary>
    public const string Role = "steward";

    /// <summary>The name his body's prefab is registered under.
    ///
    /// <b>Never change this either.</b> A saved body is found by its prefab
    /// name when a world's objects are created, and the host destroys any saved
    /// object whose prefab is not registered — so renaming it would delete
    /// every existing Steward and everything in his pack. It lives here beside
    /// the identity slug rather than in the prefab factory because it is the
    /// same kind of thing: a contract with a player's save file, not an
    /// implementation detail of the factory that happens to build it.</summary>
    public const string BodyPrefabName = "CS_Steward";

    /// <summary>The prefix of every key the Steward's body stores itself under
    /// in its own network object, trailing dot included.
    ///
    /// <b>Never change this either, and it is not a new fact.</b> It is the
    /// common head of the three literals <c>StewardBody</c> has
    /// always written — <c>tcc.steward.key</c>, <c>tcc.steward.inventory</c>,
    /// <c>tcc.steward.revision</c> — named here so it can be handed to Concerned
    /// NPC as <c>NpcBodyContract.ZdoKeyPrefix</c> without a second spelling of
    /// it appearing anywhere. Those three literals are deliberately left where
    /// they are rather than recomposed from this constant: a saved body is read
    /// by them, and a refactor that rebuilt them from parts would be a change to
    /// a player's save file wearing the clothes of a tidy-up.
    /// <c>StewardNpcRoleTests</c> asserts the three still compose from this
    /// prefix, so the two cannot drift apart unnoticed.</summary>
    public const string BodyKeyPrefix = "tcc.steward.";

    /// <summary>The job id every upkeep job is held under. One identity, one
    /// runtime, one job at a time — held through the shared arbiter since the
    /// Concerned NPC adoption, and by this product's own mode owner before it.
    /// </summary>
    public const string UpkeepJobId = "steward/upkeep";

    /// <summary>Validates that a name an adapter read back out of a save is the
    /// Steward's. Ordinal, because an identity is bytes and not text.</summary>
    public static bool IsSteward(string? workerKey) =>
        string.Equals(workerKey, Worker.Value, StringComparison.Ordinal);
}
