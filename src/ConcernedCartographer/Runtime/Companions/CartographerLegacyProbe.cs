using System;
using BepInEx.Logging;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Answers the shared layer's one product-specific question: has this
/// installation been used before?
///
/// Only the product knows what its own old data looks like, which is why this
/// lives here and not in the shared layer. The looking itself is
/// <see cref="CartographerEvidenceScan"/>, in the domain and under test against
/// a real filesystem; the interpretation is <see cref="LegacyEvidenceRule"/>,
/// tested against every combination without one. What is left here is the two
/// things that need the running process: where this product's directories are,
/// and when the process started.
///
/// <b>Both directories, always.</b> The atlas moved out of the settings folder
/// in #304, and a profile that has not started this build yet — or whose
/// relocation could not finish — still has all of it in the old place. Reading
/// only the new one would classify a year-old player as new, and
/// <c>LegacyEvidence.None</c> does not unlock.</summary>
internal sealed class CartographerLegacyProbe
{
    /// <summary>When this process began. Everything this build writes for
    /// itself is younger than this; anything older was already there.</summary>
    private static readonly DateTime ProcessStartUtc = ResolveProcessStartUtc();

    private static DateTime ResolveProcessStartUtc()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            // Unknown start time must not turn an old file into a new one, so
            // it resolves to "everything predates us" - the generous answer.
            return DateTime.MaxValue;
        }
    }

    private readonly ManualLogSource _log;

    public CartographerLegacyProbe(ManualLogSource log)
    {
        _log = log;
    }

    public LegacyEvidence Evaluate(long worldUid)
    {
        LegacyEvidenceFacts facts = Gather(worldUid);
        return LegacyEvidenceRule.Evaluate(facts);
    }

    internal LegacyEvidenceFacts Gather(long worldUid)
    {
        try
        {
            return CartographerEvidenceScan.Gather(
                new[] { CartographerPaths.Data, CartographerPaths.Config },
                worldUid,
                ProcessStartUtc);
        }
        catch (Exception exception)
        {
            // Unreadable is not the same as empty, and must never be treated
            // as proof of a new player.
            _log.LogWarning(
                "Could not check for existing Concerned Cartographer data, so your tools stay " +
                $"available: {SafeLogText.Describe(exception)}");
            return LegacyEvidenceFacts.Failed;
        }
    }
}
