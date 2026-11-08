using System;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>A chest the player designated - the deposit's destination (D15)
/// or the take's source (D20) - as the shared library is allowed to see it.
///
/// <b>One class for both directions, on purpose.</b> It was nested in the
/// deposit port; the take port needs exactly the same answers (the player's
/// mark, ownership, in-use, ward, privacy, reach) and a second copy is a second
/// place one of them could quietly differ. Which use is asked for is the
/// permit's argument, not this type's.
///
/// <b><see cref="Access"/> is a property and is re-read on every get</b>,
/// which is the contract <see cref="INpcContainer"/> states and the reason
/// the permit mint can be trusted: between the tick that chose this chest and
/// this one, the player can have walked into it, a ward can have gone up, the
/// chest can have been destroyed, ownership can have migrated.</summary>
internal sealed class DesignatedChest : INpcContainer
{
    private readonly Container? _container;
    private readonly string _expectedKey;
    private readonly NpcWorldEpoch _epoch;
    private readonly Func<Container?, NpcContainerUse> _allowance;
    private readonly Func<Vector3?> _workerPosition;
    private readonly float _reachMetres;

    public DesignatedChest(
        Container? container,
        string expectedKey,
        NpcWorldEpoch epoch,
        Func<Container?, NpcContainerUse> allowance,
        Func<Vector3?> workerPosition,
        float reachMetres)
    {
        _container = container;
        _expectedKey = expectedKey ?? string.Empty;
        _epoch = epoch;
        _allowance = allowance;
        _workerPosition = workerPosition;
        _reachMetres = reachMetres;
    }

    public string Key => _expectedKey;

    public NpcWorldEpoch Epoch => _epoch;

    public string Describe => "the chest you chose";

    public NpcPoint Position
    {
        get
        {
            try
            {
                if (_container == null)
                {
                    return default;
                }

                Vector3 at = _container.transform.position;
                return new NpcPoint(at.x, at.y, at.z);
            }
            catch (Exception)
            {
                return default;
            }
        }
    }

    /// <summary>What the player allows and what the world allows, as of now.
    /// The order is the order a player would fix them in, and <b>every
    /// unknown refuses</b>.</summary>
    public NpcContainerAccess Access
    {
        get
        {
            try
            {
                if (_container == null || _container.GetInventory() == null)
                {
                    return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
                }

                if (!string.Equals(LiveKeyOf(_container), _expectedKey, StringComparison.Ordinal)
                    || _expectedKey.Length == 0)
                {
                    // Something stands there; it is not what was chosen.
                    return new NpcContainerAccess(
                        NpcContainerUse.Off, NpcContainerRefusal.NotThisContainer);
                }

                NpcContainerUse allowed = _allowance(_container);

                if (!_container.IsOwner())
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.NotOwnedHere);
                }

                // A non-owner write is discarded silently, and an open chest
                // reloads over what was added. A chest riding a cart that is
                // in use counts as in use too.
                if (_container.IsInUse()
                    || (_container.m_wagon != null && _container.m_wagon.InUse()))
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.InUse);
                }

                if (!WardAllows(_container))
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.WardDenied);
                }

                if (!PrivacyAllows(_container))
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.PrivacyDenied);
                }

                return WithinReach()
                    ? new NpcContainerAccess(allowed, NpcContainerRefusal.None)
                    : new NpcContainerAccess(allowed, NpcContainerRefusal.OutOfReach);
            }
            catch (Exception)
            {
                // A container that cannot say what it is has not said yes.
                return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
            }
        }
    }

    private bool WithinReach()
    {
        Vector3? worker = _workerPosition();
        if (!worker.HasValue || _container == null)
        {
            return false;
        }

        Vector3 flat = worker.Value - _container.transform.position;
        flat.y = 0f;
        return flat.magnitude <= _reachMetres;
    }

    /// <summary>The container's identity within this world load: its own
    /// network record's id. Empty when there is none, which the caller reads
    /// as "not the chest that was chosen".</summary>
    internal static string LiveKeyOf(Container? container)
    {
        try
        {
            ZNetView? view = container == null ? null : container.GetComponent<ZNetView>();
            ZDO? record = view == null || !view.IsValid() ? null : view.GetZDO();
            return record == null ? string.Empty : record.m_uid.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>The container's ward check, exactly where vanilla makes it
    /// before opening one, without the flash.</summary>
    private static bool WardAllows(Container container) =>
        !container.m_checkGuardStone
        || PrivateArea.CheckAccess(container.transform.position, 0f, flash: false, wardCheck: false);

    /// <summary>Vanilla's container privacy, mirrored because the method is
    /// private: public for everyone, private for its builder, and anything
    /// else for nobody here.</summary>
    private static bool PrivacyAllows(Container container)
    {
        switch (container.m_privacy)
        {
            case Container.PrivacySetting.Public:
                return true;
            case Container.PrivacySetting.Private:
            {
                Game game = Game.instance;
                Piece? piece = container.GetComponent<Piece>();
                return game != null
                    && piece != null
                    && piece.GetCreator() == game.GetPlayerProfile().GetPlayerID();
            }

            default:
                return false;
        }
    }
}
