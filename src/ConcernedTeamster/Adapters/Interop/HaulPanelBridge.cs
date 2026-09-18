using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Interop;

/// <summary>What Gunnar's panel needs from the game, and nothing else (#317).
///
/// It reads: the cart the player is looking at (so Assign can be offered), the
/// spot the player is standing on (the destination they choose for a haul of
/// their own), and the haul service's own snapshot. It writes nothing: every
/// action goes to the haul runtime's command surface, the same one the console
/// uses. <c>here</c> and <c>go</c> are the panel's own two verbs, because a
/// destination is a place a player points at rather than coordinates they type.
/// </summary>
internal sealed class HaulPanelBridge
{
    private readonly Func<string[], string> _runtime;
    private readonly Func<IHaulService?> _service;
    private readonly Func<bool> _haulingEnabled;
    private readonly Func<bool> _seamAvailable;
    private readonly HashSet<string> _ownHauls = new HashSet<string>(StringComparer.Ordinal);

    private Vector3? _destination;

    internal HaulPanelBridge(
        Func<string[], string> runtime, Func<IHaulService?> service, Func<bool> haulingEnabled, Func<bool> seamAvailable)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _haulingEnabled = haulingEnabled ?? throw new ArgumentNullException(nameof(haulingEnabled));
        _seamAvailable = seamAvailable ?? throw new ArgumentNullException(nameof(seamAvailable));
    }

    /// <summary>Carries out one panel command. Marking a destination and
    /// hauling to it are the panel's own two verbs (a destination is a place a
    /// player points at); everything else is the haul runtime's own command,
    /// word for word what the console would send.</summary>
    internal string Execute(HaulPanelCommand command)
    {
        switch (command)
        {
            case HaulPanelCommand.SetDestination:
                return SetDestination();
            case HaulPanelCommand.Go:
                return Go();
            case HaulPanelCommand.Assign:
                return _runtime(new[] { "assign" });
            case HaulPanelCommand.Confirm:
                return _runtime(new[] { "confirm" });
            case HaulPanelCommand.Release:
                return _runtime(new[] { "release" });
            case HaulPanelCommand.Stop:
                return _runtime(new[] { "stop" });
            case HaulPanelCommand.Detach:
                return _runtime(new[] { "detach" });
            default:
                return _runtime(new[] { "status" });
        }
    }

    internal HaulPanelFacts Facts()
    {
        IHaulService? service = _service();
        HaulSnapshot snapshot = service != null
            ? service.Snapshot
            : new HaulSnapshot(string.Empty, HaulPhase.Unassigned, 0, HaulAttentionReason.Unspecified, false, true, true, false, null, null);
        CartLease? lease = service?.ActiveLease;
        string leaseLabel = lease != null && lease.IsActive
            ? lease.Cart.SessionId + DistanceTo(snapshot.CartPosition)
            : string.Empty;

        return new HaulPanelFacts(
            _haulingEnabled(),
            _seamAvailable(),
            service?.Authority ?? WorkAuthorityVerdict.RuntimeDisabled,
            service != null && service.WorkerAvailable,
            leaseLabel,
            HoveredCartLabel(),
            DestinationLabel,
            snapshot.Phase,
            snapshot.Attention,
            snapshot.Attached,
            orderDriven: snapshot.HaulId.Length > 0 && !_ownHauls.Contains(snapshot.HaulId));
    }

    internal string DestinationLabel =>
        _destination.HasValue
            ? string.Format(
                CultureInfo.InvariantCulture, "the spot you marked at ({0:0}, {1:0})", _destination.Value.x, _destination.Value.z)
            : string.Empty;

    /// <summary>A world went away: a destination and the hauls this panel
    /// started belonged to it.</summary>
    internal void Forget()
    {
        _destination = null;
        _ownHauls.Clear();
    }

    private string SetDestination()
    {
        Player? player = Player.m_localPlayer;
        if (player == null)
        {
            return "There is no local player, so there is nowhere to mark.";
        }

        _destination = player.transform.position;
        return "Destination marked at " + DestinationLabel + ". Press the haul button to send Gunnar there.";
    }

    private string Go()
    {
        if (!_destination.HasValue)
        {
            return "Mark a destination first: stand where the cart should end up and press Set destination.";
        }

        Vector3 destination = _destination.Value;
        string answer = _runtime(new[]
        {
            "go",
            destination.x.ToString("0.##", CultureInfo.InvariantCulture),
            destination.z.ToString("0.##", CultureInfo.InvariantCulture),
        });

        // Remember the haul this panel started, so a haul another mod's order
        // is driving can be told apart from the player's own.
        IHaulService? service = _service();
        string haulId = service != null ? service.Snapshot.HaulId : string.Empty;
        if (haulId.Length > 0)
        {
            _ownHauls.Add(haulId);
        }

        return answer;
    }

    private static string HoveredCartLabel()
    {
        try
        {
            Player? player = Player.m_localPlayer;
            GameObject? hovering = player == null ? null : player.GetHoverObject();
            Vagon? cart = hovering == null ? null : hovering.GetComponentInParent<Vagon>();
            if (cart == null || player == null)
            {
                return string.Empty;
            }

            float distance = Vector3.Distance(player.transform.position, cart.transform.position);
            return string.Format(CultureInfo.InvariantCulture, "a cart {0:0.#} m away", distance);
        }
        catch (Exception)
        {
            // Looking at something unreadable is simply "no cart".
            return string.Empty;
        }
    }

    private static string DistanceTo(WorkPoint? cart)
    {
        Player? player = Player.m_localPlayer;
        if (!cart.HasValue || player == null)
        {
            return string.Empty;
        }

        Vector3 position = player.transform.position;
        var here = new WorkPoint(position.x, position.y, position.z);
        return string.Format(
            CultureInfo.InvariantCulture, " ({0:0.#} m away)", here.HorizontalDistanceTo(cart.Value));
    }
}
