namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>What the tools panel may offer right now (#295 S4). Game-free, so
/// that "the panel never offers what the console would refuse for an obvious
/// reason" is tested rather than hoped.
///
/// The panel itself decides nothing. Every button sends the same words the
/// console sends (<c>cf_worker spawn|despawn</c>,
/// <c>cf_settle give|takeback axe|hammer</c>), so a refusal there is the
/// console's refusal, with the console's reason. This only greys out a button
/// that has nothing to do.</summary>
internal readonly struct ThorsteinToolsPanelFacts
{
    internal ThorsteinToolsPanelFacts(bool workerPresent, bool axeHeld, bool hammerHeld, bool handoverUncertain)
    {
        WorkerPresent = workerPresent;
        AxeHeld = axeHeld;
        HammerHeld = hammerHeld;
        HandoverUncertain = handoverUncertain;
    }

    /// <summary>His worker body is in loaded ground. A tool can only be given
    /// to it, never to the figure at home, which holds nothing.</summary>
    internal bool WorkerPresent { get; }

    /// <summary>The ledger says an axe given to him is held by him.</summary>
    internal bool AxeHeld { get; }

    internal bool HammerHeld { get; }

    /// <summary>A handover is unresolved: nothing more is moved until it is.
    /// </summary>
    internal bool HandoverUncertain { get; }
}

/// <summary>Which buttons are live.</summary>
internal readonly struct ThorsteinToolsPanelButtons
{
    internal ThorsteinToolsPanelButtons(
        bool bringHim, bool sendHome, bool giveAxe, bool giveHammer, bool takeAxe, bool takeHammer)
    {
        BringHim = bringHim;
        SendHome = sendHome;
        GiveAxe = giveAxe;
        GiveHammer = giveHammer;
        TakeAxe = takeAxe;
        TakeHammer = takeHammer;
    }

    internal bool BringHim { get; }

    internal bool SendHome { get; }

    internal bool GiveAxe { get; }

    internal bool GiveHammer { get; }

    internal bool TakeAxe { get; }

    internal bool TakeHammer { get; }
}

internal static class ThorsteinToolsPanelPlan
{
    internal static ThorsteinToolsPanelButtons Decide(in ThorsteinToolsPanelFacts facts)
    {
        bool moves = facts.WorkerPresent && !facts.HandoverUncertain;
        return new ThorsteinToolsPanelButtons(
            bringHim: !facts.WorkerPresent,
            sendHome: facts.WorkerPresent,
            giveAxe: moves && !facts.AxeHeld,
            giveHammer: moves && !facts.HammerHeld,
            takeAxe: moves && facts.AxeHeld,
            takeHammer: moves && facts.HammerHeld);
    }

    /// <summary>The second click of a give. The first only names the exact
    /// item in the player's hand; nothing moves until the same give is
    /// clicked again (#295: "explicit inventory selection and transfer
    /// confirmation").</summary>
    internal static bool IsConfirmation(string? armedKind, string kind) =>
        armedKind != null && armedKind == kind;
}
