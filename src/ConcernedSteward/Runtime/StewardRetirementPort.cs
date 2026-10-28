using System;
using TheConcernedCat.ConcernedSteward.Domain.Retirement;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>What the retire verb did.</summary>
internal enum StewardRetireOutcome
{
    /// <summary>Nothing was asked. Never a removal.</summary>
    Unspecified = 0,

    /// <summary>Her body left the world.</summary>
    Retired = 1,

    /// <summary>The verdict refused; nothing was touched.</summary>
    Refused = 2,

    /// <summary>The body is not one this game may remove: no valid network
    /// object, or one another peer owns. Nothing was touched.</summary>
    NotOurs = 3,
}

/// <summary>The one place Concerned Steward may take a body out of the world:
/// <b>Sunniva's own worker body</b>, empty-handed, with no round open (owner
/// decision 2026-10-28, <c>docs/mods/concerned-steward/DECISIONS.md</c> S1).
///
/// <b>Pinned, not trusted.</b> <c>tools/validate_repo.py</c>'s "S1 retire audit"
/// counts every destruction in this product, allows exactly one body removal,
/// here, inside <see cref="Retire"/>, below a <c>StewardRetirement.Decide(</c>
/// and a refusing <c>if (!StewardRetirement.Allows(</c>, and allows this verb one
/// caller. It is a text audit: it establishes that the refusal is written above
/// the removal, not that control flow obeys it - that is
/// <c>StewardRetirementTests</c>' job.
///
/// <b>Never a vanilla object.</b> The only thing removed is the network object
/// of a <see cref="StewardBody"/> - a <c>CS_Steward</c> body, this product's own
/// prefab - and only when this game owns it. No ownership is taken, nothing is
/// dropped, and nothing else in the world is touched.</summary>
internal static class StewardRetirementPort
{
    /// <summary>Removes her body if, and only if, the verdict allows it.</summary>
    /// <param name="body">Her bound, loaded body.</param>
    /// <param name="ledger">Everything the runtime knows that the body does
    /// not: the fuel ledger, the loop, the durable plan and the record. The
    /// pack itself is read here, from the body, so a caller cannot state it.
    /// </param>
    /// <param name="answer">The sentence for the player.</param>
    internal static StewardRetireOutcome Retire(
        StewardBody? body, in StewardRetireFacts ledger, out string answer)
    {
        if (body == null)
        {
            answer = "She has no body here to retire.";
            return StewardRetireOutcome.NotOurs;
        }

        // The pack is the body's own record, and an inert, unloaded or unsaved
        // one is unreadable - never "empty".
        bool readable = body.IsLoaded && body.Fault == null && body.LastChangePersisted;
        int held = readable ? body.ItemCount : 0;
        var facts = new StewardRetireFacts(
            held,
            readable,
            ledger.FuelCarried,
            ledger.RoundOpen,
            ledger.PlanOpen,
            ledger.UnresolvedSteps,
            ledger.UnresolvedLoss);

        StewardRetireVerdict verdict = StewardRetirement.Decide(facts);
        if (!StewardRetirement.Allows(verdict))
        {
            answer = StewardRetirement.Describe(verdict, held);
            return StewardRetireOutcome.Refused;
        }

        ZNetView? view = body.View;
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            answer = "Refused: this game does not own her body, so it is not this game's to remove.";
            return StewardRetireOutcome.NotOurs;
        }

        try
        {
            view.Destroy();
        }
        catch (Exception exception)
        {
            answer = "Her body could not be removed (" + exception.GetType().Name + "); nothing else " +
                "was touched.";
            return StewardRetireOutcome.Refused;
        }

        answer = "Her worker body has left the world, holding nothing. She is still taken on; " +
            "\"cs_steward recruit\" brings her back.";
        return StewardRetireOutcome.Retired;
    }
}
