using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What removing a worker body from the world may do. Zero is the
/// unspecified value: a verdict nobody computed never removes anything.</summary>
internal enum RetireVerdict
{
    /// <summary>Nobody decided. Never a grant.</summary>
    Unspecified = 0,

    /// <summary>He is holding nothing. Remove the body.</summary>
    MayRetire = 1,

    /// <summary>He is holding things, and removing the body would destroy them.
    /// Refused unless the player says so explicitly.</summary>
    RefusedCarrying = 2,

    /// <summary>What he is holding could not be read. Unknown refuses: "I could
    /// not count his inventory" is not "his inventory is empty".</summary>
    RefusedUnreadable = 3,

    /// <summary>The player asked for it explicitly, knowing what it costs.
    /// Removes the body <b>and destroys what it holds</b>.</summary>
    ForcedAndLost = 4,
}

/// <summary>Whether a worker body may be removed from the world, given what it
/// is holding (#381).
///
/// <b>Why this exists at all, and why it arrived with the collection wiring.</b>
/// Retiring a body destroys its network object, and a character's inventory
/// lives in that object: nothing is dropped on the ground. That was harmless
/// while nothing could put anything into Gunnar. The moment an ordered pick
/// could, <c>ct_haul retire</c> became a way to delete gathered material
/// silently - and material conservation is this product's hard rule, not a
/// preference. So the verb that removes a body now has to ask.
///
/// <b>Why refusal rather than dropping what he holds.</b> Concerned Foreman's
/// §5a already decided this shape for a worker that carries real material, and
/// it decided it twice over: <i>death</i> drops everything through vanilla's own
/// drop and records it as lost, while <i>despawn</i> - the deliberate removal,
/// which is what retire is - is <b>refused while he carries anything</b>. Death
/// is vanilla acting on its own; a deliberate drop would be this product
/// spawning item instances, which is a capability nobody authorized and is not
/// one of the calls the 2026-09-19 carve-out granted. So the precedent for this
/// verb is the refusal, and inventing the drop would be inventing an
/// authorization.
///
/// <b>Why there is an escape hatch, and why it can never be blocked.</b>
/// Foreman pairs its refusal with recovery commands that empty the worker; this
/// slice has none, because the deposit half is unwired. A bare refusal would
/// therefore trap a body forever - and retire is the only way to resolve a
/// duplicate Gunnar. So an explicit forcing word retires anyway and says plainly
/// what it costs. <see cref="Decide"/> allows every forced case without
/// exception, which is the property that keeps the refusal from becoming a trap;
/// <c>WorkerRetirementTests</c> pins it over every combination.
///
/// <b>Anything he holds, not "collected material".</b> Nothing at this layer can
/// tell a picked stone from anything else in his inventory, and pretending
/// otherwise would be a provenance claim. Counting everything refuses more
/// often, which is the safe direction.</summary>
internal static class WorkerRetirement
{
    /// <summary>The word that makes a retire explicit. A separate word rather
    /// than a repeat of the command, so it cannot be reached by somebody
    /// impatiently running retire twice.</summary>
    public const string ForcingWord = "force";

    /// <summary>Whether the player spelled the forcing word. Anything else -
    /// no argument, a typo, a different word - is not forcing, which is the
    /// refusing direction.</summary>
    public static bool IsForcing(IReadOnlyList<string>? args, int from)
    {
        if (args == null || from < 0)
        {
            return false;
        }

        for (int index = from; index < args.Count; index++)
        {
            string word = args[index];
            if (word == null)
            {
                continue;
            }

            word = word.Trim();
            if (word.StartsWith("--", StringComparison.Ordinal))
            {
                word = word.Substring(2);
            }

            if (string.Equals(word, ForcingWord, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Decides.</summary>
    /// <param name="forced">Whether the player spelled the forcing word.</param>
    /// <param name="itemsHeld">How many items the body holds. Meaningless when
    /// <paramref name="inventoryReadable"/> is false.</param>
    /// <param name="inventoryReadable">Whether the inventory could be counted at
    /// all. False is not zero.</param>
    public static RetireVerdict Decide(bool forced, int itemsHeld, bool inventoryReadable)
    {
        bool empty = inventoryReadable && itemsHeld <= 0;
        if (forced)
        {
            // Forced ALWAYS proceeds - that is what stops the refusal below from
            // trapping a body nothing can empty. It reports the loss when there
            // is, or might be, something to lose.
            return empty ? RetireVerdict.MayRetire : RetireVerdict.ForcedAndLost;
        }

        if (!inventoryReadable)
        {
            return RetireVerdict.RefusedUnreadable;
        }

        return empty ? RetireVerdict.MayRetire : RetireVerdict.RefusedCarrying;
    }

    /// <summary>Whether the body may actually be removed. The caller asks this
    /// rather than comparing verdicts itself, so adding a verdict cannot quietly
    /// become a grant.</summary>
    public static bool Allows(RetireVerdict verdict) =>
        verdict == RetireVerdict.MayRetire || verdict == RetireVerdict.ForcedAndLost;

    /// <summary>One sentence a player can act on. The forced one states the loss
    /// outright; <c>WorkerRetirementTests</c> fails if it stops doing so.
    /// </summary>
    public static string Describe(RetireVerdict verdict, int itemsHeld)
    {
        switch (verdict)
        {
            case RetireVerdict.MayRetire:
                return "He is holding nothing.";
            case RetireVerdict.RefusedCarrying:
                return "Refused: he is carrying " + (itemsHeld == 1 ? "1 thing" : itemsHeld + " things") +
                    ", and removing his body would destroy " + (itemsHeld == 1 ? "it" : "them") +
                    " - nothing is dropped on the ground. There is no way to empty him yet, so if you " +
                    "mean to throw it away, say so: 'ct_haul retire " + ForcingWord + "'.";
            case RetireVerdict.RefusedUnreadable:
                return "Refused: what he is carrying could not be read, and removing his body would " +
                    "destroy whatever it is. 'ct_haul retire " + ForcingWord + "' removes him anyway.";
            case RetireVerdict.ForcedAndLost:
                return "Retired as you asked. What he was carrying was destroyed with his body and is " +
                    "lost - it was not dropped on the ground.";
            default:
                return "Refused for a reason nobody recorded; that is a bug.";
        }
    }
}
