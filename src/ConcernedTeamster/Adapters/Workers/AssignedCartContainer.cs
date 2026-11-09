using System;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The container of the cart the player assigned Gunnar, as the shared
/// library is allowed to see it - the cart-assisted tour's destination (#381,
/// owner decision 2026-10-31, `DECISIONS.md` <b>D22</b>).
///
/// <b>The player's explicit act is the assignment, not a container mark.</b>
/// `ct_haul assign` and `confirm` name one cart; the allowance this subject is
/// handed answers <c>Deposit</c> only while the hauling runtime's lease still
/// names exactly this cart and is doing nothing with it, and <c>Off</c>
/// otherwise. It never answers <c>Take</c>: D22 grants loading into the cart and
/// nothing out of it.
///
/// <b>It is <see cref="DesignatedChest"/>'s questions, asked of a cart.</b> A
/// cart's container sits on a child object and the network record lives on the
/// cart's root (vanilla's <c>m_rootObjectOverride</c>), so the chest's own key
/// reading does not apply; the identity here is the cart's, and the container
/// must be that cart's own <c>m_container</c>, riding that cart.
///
/// <b><see cref="Access"/> is re-read on every get</b>, the contract
/// <see cref="INpcContainer"/> states: between the tick that chose to walk here
/// and this one, the player can have hitched the cart, opened it, pushed it, a
/// ward can have gone up, ownership can have migrated.</summary>
internal sealed class AssignedCartContainer : INpcContainer
{
    private readonly Vagon? _cart;
    private readonly string _expectedKey;
    private readonly NpcWorldEpoch _epoch;
    private readonly Func<Vagon?, NpcContainerUse> _allowance;
    private readonly Func<Vector3?> _workerPosition;
    private readonly Vector3 _parkedAt;
    private readonly float _reachMetres;
    private readonly float _mostDriftMetres;

    /// <param name="expectedKey">The cart's own network id as the lease names
    /// it. A live cart whose id differs is refused.</param>
    /// <param name="parkedAt">Where the cart stood when the tour began. A cart
    /// further than <paramref name="mostDriftMetres"/> from it is refused: a
    /// container that moved fails closed.</param>
    public AssignedCartContainer(
        Vagon? cart,
        string expectedKey,
        NpcWorldEpoch epoch,
        Func<Vagon?, NpcContainerUse> allowance,
        Func<Vector3?> workerPosition,
        Vector3 parkedAt,
        float reachMetres,
        float mostDriftMetres)
    {
        _cart = cart;
        _expectedKey = expectedKey ?? string.Empty;
        _epoch = epoch;
        _allowance = allowance;
        _workerPosition = workerPosition;
        _parkedAt = parkedAt;
        _reachMetres = reachMetres;
        _mostDriftMetres = mostDriftMetres;
    }

    public string Key => _expectedKey;

    public NpcWorldEpoch Epoch => _epoch;

    public string Describe => "the cart assigned to him";

    public NpcPoint Position
    {
        get
        {
            try
            {
                if (_cart == null)
                {
                    return default;
                }

                Vector3 at = _cart.transform.position;
                return new NpcPoint(at.x, at.y, at.z);
            }
            catch (Exception)
            {
                return default;
            }
        }
    }

    /// <summary>The cart's container, or null. It is the cart's own
    /// <c>m_container</c> and it must say it rides that cart; anything else is
    /// not the assigned cart's bed.</summary>
    internal static Container? BedOf(Vagon? cart)
    {
        try
        {
            Container? bed = cart == null ? null : cart.m_container;
            return bed != null && ReferenceEquals(bed.m_wagon, cart) ? bed : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>What the assignment allows and what the world allows, as of
    /// now. The order is the order a player would fix them in, and <b>every
    /// unknown refuses</b>.</summary>
    public NpcContainerAccess Access
    {
        get
        {
            try
            {
                Container? bed = BedOf(_cart);
                if (_cart == null || bed == null || bed.GetInventory() == null)
                {
                    return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
                }

                if (_expectedKey.Length == 0
                    || !string.Equals(LiveKeyOf(_cart), _expectedKey, StringComparison.Ordinal)
                    || !Parked())
                {
                    // Something stands there; it is not the cart he was told to
                    // fill, or not where it was when he was told.
                    return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.NotThisContainer);
                }

                NpcContainerUse allowed = _allowance(_cart);

                ZNetView? view = _cart.m_nview;
                if (view == null || !view.IsValid() || !view.IsOwner() || !bed.IsOwner())
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.NotOwnedHere);
                }

                // Hitched to anybody, or open: vanilla's own in-use answer for the
                // cart covers both, and the container's own is asked as well.
                if (bed.IsInUse() || _cart.InUse() || _cart.IsAttached())
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.InUse);
                }

                if (bed.m_checkGuardStone
                    && !PrivateArea.CheckAccess(bed.transform.position, 0f, flash: false, wardCheck: false))
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.WardDenied);
                }

                if (!PrivacyAllows(bed))
                {
                    return new NpcContainerAccess(allowed, NpcContainerRefusal.PrivacyDenied);
                }

                return WithinReach()
                    ? new NpcContainerAccess(allowed, NpcContainerRefusal.None)
                    : new NpcContainerAccess(allowed, NpcContainerRefusal.OutOfReach);
            }
            catch (Exception)
            {
                // A cart that cannot say what it is has not said yes.
                return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
            }
        }
    }

    /// <summary>The cart's identity within this world load: its own network
    /// record's id, the same text the lease's key was made from. Empty when
    /// there is none.</summary>
    internal static string LiveKeyOf(Vagon? cart)
    {
        try
        {
            ZNetView? view = cart == null ? null : cart.m_nview;
            ZDO? record = view == null || !view.IsValid() ? null : view.GetZDO();
            return record == null ? string.Empty : record.m_uid.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private bool Parked()
    {
        if (_cart == null)
        {
            return false;
        }

        Vector3 drift = _cart.transform.position - _parkedAt;
        return !float.IsNaN(drift.sqrMagnitude) && drift.magnitude <= _mostDriftMetres;
    }

    /// <summary>He loads standing where a puller stands at the handle, the
    /// point the hauling runtime walks him to before a hitch - never walking into
    /// the cart's body, which would shove an unbraked cart.</summary>
    private bool WithinReach()
    {
        Vector3? worker = _workerPosition();
        if (!worker.HasValue || _cart == null)
        {
            return false;
        }

        Transform attach = _cart.m_attachPoint;
        Vector3 handle = attach != null ? attach.position - _cart.m_attachOffset : _cart.transform.position;
        Vector3 flat = worker.Value - handle;
        flat.y = 0f;
        return flat.magnitude <= _reachMetres;
    }

    /// <summary>Vanilla's container privacy, mirrored because the method is
    /// private, exactly as <see cref="DesignatedChest"/> mirrors it.</summary>
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
