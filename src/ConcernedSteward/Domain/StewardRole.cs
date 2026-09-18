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

    /// <summary>What a player sees until the owner names him.
    ///
    /// A description, not a name, so nobody mistakes it for one and so it reads
    /// correctly in every sentence the runtime builds: "the Steward is at the
    /// depot", "the Steward has nothing to do". When he is named, this becomes
    /// the name and every sentence keeps working.</summary>
    public const string DisplayNameFallback = "the Steward";

    /// <summary>The same, capitalised for the start of a sentence.</summary>
    public const string DisplayNameFallbackCapitalised = "The Steward";

    /// <summary>The product half of the worker key, matching
    /// <see cref="WorkerKey.ForemanProduct"/> and
    /// <see cref="WorkerKey.TeamsterProduct"/>.</summary>
    public const string Product = "steward";

    /// <summary>The Steward, across products: <c>steward/steward</c>.
    ///
    /// Both halves are the role because there is no person yet. That reads
    /// oddly beside <c>foreman/thorstein</c> and it is still right: the left
    /// half names the runtime that owns the body and the right half names the
    /// identity, and today those are the same word. When he is named, the left
    /// half stays and the right half <b>still stays</b> — a saved body is found
    /// by this key, so the key survives the naming exactly as it survives a
    /// reload.</summary>
    public static WorkerKey Worker => new WorkerKey(Product, RoleKey);

    /// <summary>The Steward's identity inside a settlement record.</summary>
    public static WorkerId WorkerIdentity => new WorkerId(RoleKey);

    /// <summary>The Steward's role as the settlement layer records it.
    ///
    /// <c>WorkerRoles</c> in the shared recruitment area knows only
    /// <c>labourer</c>, and this product does not adopt that file — a steward
    /// is not a labourer and widening a shared enum from here would change
    /// Foreman's vocabulary for a product Foreman does not know about.</summary>
    public const string Role = "steward";

    /// <summary>The job id every upkeep job is held under, for the actor-mode
    /// owner. One identity, one runtime, one job at a time.</summary>
    public const string UpkeepJobId = "steward/upkeep";

    /// <summary>Validates that a name an adapter read back out of a save is the
    /// Steward's. Ordinal, because an identity is bytes and not text.</summary>
    public static bool IsSteward(string? workerKey) =>
        string.Equals(workerKey, Worker.Value, StringComparison.Ordinal);
}
