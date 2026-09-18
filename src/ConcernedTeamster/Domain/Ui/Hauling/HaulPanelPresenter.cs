using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;

/// <summary>What Gunnar's panel knows, gathered by the adapter from his
/// runtime. Plain values only, so the panel's words are decided and tested
/// without the game.</summary>
internal sealed class HaulPanelFacts
{
    public HaulPanelFacts(
        bool haulingEnabled,
        bool seamAvailable,
        WorkAuthorityVerdict authority,
        bool workerAvailable,
        string leaseCartLabel,
        string hoveredCartLabel,
        string destinationLabel,
        HaulPhase phase,
        HaulAttentionReason attention,
        bool attached,
        bool orderDriven)
    {
        HaulingEnabled = haulingEnabled;
        SeamAvailable = seamAvailable;
        Authority = authority;
        WorkerAvailable = workerAvailable;
        LeaseCartLabel = leaseCartLabel ?? string.Empty;
        HoveredCartLabel = hoveredCartLabel ?? string.Empty;
        DestinationLabel = destinationLabel ?? string.Empty;
        Phase = phase;
        Attention = attention;
        Attached = attached;
        OrderDriven = orderDriven;
    }

    public bool HaulingEnabled { get; }

    /// <summary>The cart attachment this game build offers is the one Gunnar
    /// was built for.</summary>
    public bool SeamAvailable { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool WorkerAvailable { get; }

    /// <summary>The assigned cart, described for a person ("the cart 6 m
    /// north-east, 34 items"); empty when none is assigned.</summary>
    public string LeaseCartLabel { get; }

    /// <summary>The cart the player is looking at; empty when none.</summary>
    public string HoveredCartLabel { get; }

    /// <summary>Where the player last said the load should go; empty when
    /// nowhere yet.</summary>
    public string DestinationLabel { get; }

    public HaulPhase Phase { get; }

    public HaulAttentionReason Attention { get; }

    public bool Attached { get; }

    /// <summary>Another mod's collection order is driving this haul.</summary>
    public bool OrderDriven { get; }
}

/// <summary>One line of the panel: a catalog key and the values it formats.
/// </summary>
internal readonly struct HaulPanelLine
{
    public HaulPanelLine(string key, params string[] arguments)
    {
        Key = key;
        Arguments = arguments ?? Array.Empty<string>();
    }

    public string Key { get; }

    public string[] Arguments { get; }
}

internal sealed class HaulPanelView
{
    public HaulPanelView(
        IReadOnlyList<HaulPanelLine> lines, bool canAssign, bool canRelease, bool canSetDestination, bool canStart,
        bool canStop, bool canDetach)
    {
        Lines = lines;
        CanAssign = canAssign;
        CanRelease = canRelease;
        CanSetDestination = canSetDestination;
        CanStart = canStart;
        CanStop = canStop;
        CanDetach = canDetach;
    }

    public IReadOnlyList<HaulPanelLine> Lines { get; }

    public bool CanAssign { get; }

    public bool CanRelease { get; }

    public bool CanSetDestination { get; }

    public bool CanStart { get; }

    public bool CanStop { get; }

    public bool CanDetach { get; }
}

/// <summary>Gunnar's panel in words and buttons (#317, COOP-04): what he is
/// doing, the one reason he stopped, the cart he holds, the cart you are
/// looking at, and where the load is meant to go. Pure: the panel only renders
/// what this decides.
///
/// Stopping is always offered while he holds a cart - giving up control is safe
/// even without authority - and starting is offered only when he is actually
/// free to take a leg. When another mod's collection order is driving him, the
/// panel says so rather than hiding the controls.</summary>
internal static class HaulPanelPresenter
{
    public static HaulPanelView Present(HaulPanelFacts facts)
    {
        if (facts == null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        var lines = new List<HaulPanelLine>();
        bool hasLease = facts.LeaseCartLabel.Length > 0;
        bool granted = facts.Authority == WorkAuthorityVerdict.Granted;

        if (!facts.HaulingEnabled)
        {
            lines.Add(new HaulPanelLine(HaulPanelKeys.HaulingOff));
        }
        else if (!facts.SeamAvailable)
        {
            lines.Add(new HaulPanelLine(HaulPanelKeys.SeamGone));
        }
        else if (!granted)
        {
            lines.Add(new HaulPanelLine(HaulPanelKeys.Authority, WorkAuthorityPolicy.Describe(facts.Authority)));
        }

        lines.Add(new HaulPanelLine(HaulPanelKeys.State, HaulPanelKeys.ForPhase(facts.Phase)));
        if (facts.Attention != HaulAttentionReason.Unspecified)
        {
            lines.Add(new HaulPanelLine(HaulPanelKeys.Reason, HaulPanelKeys.ForReason(facts.Attention)));
        }

        lines.Add(hasLease
            ? new HaulPanelLine(HaulPanelKeys.Lease, facts.LeaseCartLabel)
            : new HaulPanelLine(HaulPanelKeys.NoLease));
        lines.Add(facts.HoveredCartLabel.Length > 0
            ? new HaulPanelLine(HaulPanelKeys.Hovered, facts.HoveredCartLabel)
            : new HaulPanelLine(HaulPanelKeys.NoHovered));
        lines.Add(facts.DestinationLabel.Length > 0
            ? new HaulPanelLine(HaulPanelKeys.Destination, facts.DestinationLabel)
            : new HaulPanelLine(HaulPanelKeys.NoDestination));
        if (facts.OrderDriven)
        {
            lines.Add(new HaulPanelLine(HaulPanelKeys.OrderDriven));
        }

        bool usable = facts.HaulingEnabled && facts.SeamAvailable && granted && facts.WorkerAvailable;
        bool free = facts.Phase == HaulPhase.Ready || facts.Phase == HaulPhase.Waiting;
        return new HaulPanelView(
            lines,
            canAssign: usable && !hasLease && facts.HoveredCartLabel.Length > 0,
            canRelease: hasLease,
            canSetDestination: usable && hasLease,
            canStart: usable && hasLease && free && facts.DestinationLabel.Length > 0 && !facts.OrderDriven,
            // Letting go is only ever giving up control, so it needs no
            // authority: a player must always be able to stop him.
            canStop: hasLease && facts.Phase != HaulPhase.Unassigned && facts.Phase != HaulPhase.Ready,
            canDetach: hasLease && facts.Attached);
    }
}
