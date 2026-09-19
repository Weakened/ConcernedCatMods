using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Which contract a body of a given prefab belongs to, so a body that
/// the game itself created can find its own key names.
///
/// <b>Why a lookup exists at all.</b> A body loaded out of a save is built by
/// the host, from the registered prefab, with no mod code in the call. Its
/// component therefore cannot be handed anything: it wakes holding only what
/// the prefab carries. The two products this replaces solved that with a
/// compile-time constant per product, which a shared runtime cannot have - a
/// key prefix here would be exactly the unified key the architecture forbids.
///
/// <b>Why by prefab name, and not by a field on the component.</b> A public
/// field set on the prefab clone would be copied into every instance by Unity's
/// own instantiation, which is very probably true and is a property no test on
/// this side of the boundary can prove; getting it wrong produces a body that
/// reads an empty identity and saves it back over a real one, silently, which
/// is the single most expensive failure in this leaf. The prefab name, by
/// contrast, is the fact the host used to create the object in the first place,
/// it is on the instance by construction, and this library already has it
/// because the role supplied it. So the lookup is keyed on the thing that
/// cannot be absent.
///
/// <b>Registration is permanent and one-way.</b> Prefabs are registered once,
/// at plugin start, and never unregistered - a prefab missing when a world's
/// objects are created is how saved bodies are destroyed. There is deliberately
/// no removal here and no clear on world unload, for the same reason.
///
/// A second registration of the same name with the same facts is accepted and
/// changes nothing, so two roles racing is harmless. A second registration with
/// <i>different</i> facts is refused: two setups for one prefab name would make
/// which keys a body reads depend on which role started first.</summary>
internal static class NpcBodyContracts
{
    private static readonly Dictionary<string, NpcBodySetup> Known =
        new Dictionary<string, NpcBodySetup>(StringComparer.Ordinal);

    private static readonly object Gate = new object();

    /// <summary>How many prefabs this process has registered a setup for.
    /// </summary>
    internal static int Count
    {
        get
        {
            lock (Gate)
            {
                return Known.Count;
            }
        }
    }

    /// <summary>Records what bodies of this prefab are. Idempotent for
    /// identical facts; refuses a conflicting second registration and says what
    /// it conflicts with.</summary>
    internal static bool TryRemember(NpcBodyContract contract, NpcBodyKeeps keeps, out string reason)
    {
        if (contract.Kind != NpcBodyKind.Worker)
        {
            reason = "only a body the world saves is created by the host and needs to find its own keys";
            return false;
        }

        if (!NpcBodySetup.TryCompose(contract, keeps, out NpcBodySetup setup, out reason))
        {
            return false;
        }

        lock (Gate)
        {
            if (Known.TryGetValue(contract.PrefabName, out NpcBodySetup existing))
            {
                if (string.Equals(existing.Contract.ZdoKeyPrefix, contract.ZdoKeyPrefix, StringComparison.Ordinal)
                    && existing.Keeps == keeps)
                {
                    reason = string.Empty;
                    return true;
                }

                reason = "bodies of that prefab are already registered with different facts; one prefab "
                    + "cannot store itself under two key prefixes, or keep two different things";
                return false;
            }

            Known.Add(contract.PrefabName, setup);
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>What a body of <paramref name="prefabName"/> is. False for a
    /// name nobody registered, which is how a body built by something other
    /// than this library goes inert instead of reading somebody else's
    /// keys.</summary>
    internal static bool TryFind(string? prefabName, out NpcBodySetup setup)
    {
        setup = default;
        if (string.IsNullOrEmpty(prefabName))
        {
            return false;
        }

        lock (Gate)
        {
            return Known.TryGetValue(prefabName!, out setup);
        }
    }

    /// <summary>Whether this contract is the one a role actually registered for
    /// its prefab - the same name <b>and</b> the same key prefix.
    ///
    /// <b>What it is for.</b> Every public way to reach live bodies is filtered
    /// by a contract, and this is what makes that filter mean "a role's own
    /// bodies" rather than "whatever prefab name the caller typed". A contract
    /// nobody registered names no role, so it has no bodies; a contract naming a
    /// registered prefab under a different key prefix is not that role's
    /// contract and is refused rather than quietly treated as it.
    ///
    /// <b>What it is not.</b> It is not proof of who is asking. This library
    /// cannot tell one loaded assembly from another, so a consumer that writes
    /// another product's exact prefab name and key prefix into its own source
    /// still matches. That is a deliberate act naming another product's durable
    /// facts, not the accident this guards - which is a role handed every role's
    /// bodies and told in a doc comment to filter them itself.</summary>
    internal static bool IsRegistered(NpcBodyContract contract) =>
        contract.Kind == NpcBodyKind.Worker
        && TryFind(contract.PrefabName, out NpcBodySetup setup)
        && string.Equals(setup.Contract.ZdoKeyPrefix, contract.ZdoKeyPrefix, StringComparison.Ordinal);

    /// <summary>Strips the suffix the host appends to an instantiated object's
    /// name, so an instance answers with the prefab it came from.
    ///
    /// Exact and anchored at the end, and applied once: a prefab whose own name
    /// happens to contain the suffix keeps it, and a doubly suffixed name loses
    /// one level rather than being scrubbed until it matches something.
    /// </summary>
    internal static string PrefabNameOf(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName))
        {
            return string.Empty;
        }

        string suffix = "(Clone)";
        string name = instanceName!;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
    }
}
