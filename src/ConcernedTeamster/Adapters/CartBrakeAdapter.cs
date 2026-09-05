using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Brake;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>The parking brake's only game-touching surface (CT-012) — and
/// Teamster's only sanctioned mutation: while engaged, the cart's root
/// rigidbody gets <see cref="RigidbodyConstraints.FreezeAll"/>; on release
/// the captured pre-engage constraints are restored and the body woken.
/// Everything is runtime component state — Valheim persists ZDO data, not
/// Unity component properties, so a reloaded world is brake-free by
/// construction (no ZDO.Set, no sidecar write exists anywhere in this
/// adapter; see CART_INTERNALS.md). Fail closed: any surprise returns
/// false/Unavailable and the lifecycle releases.</summary>
public static class CartBrakeAdapter
{
    /// <summary>Reads the lifecycle facts for one cart. Never throws; any
    /// failure yields the fail-closed facts.</summary>
    public static BrakeFacts ReadFacts(string? cartId)
    {
        if (!CartAdapter.CapabilityEnabled)
        {
            return BrakeFacts.Unavailable;
        }

        try
        {
            return ReadFactsCore(cartId);
        }
        catch
        {
            return BrakeFacts.Unavailable;
        }
    }

    /// <summary>Freezes the cart's root body, returning the captured
    /// pre-engage constraints through <paramref name="captured"/>. False
    /// (and no mutation) on any failure.</summary>
    public static bool TryEngage(string cartId, out int captured)
    {
        captured = 0;
        if (!CartAdapter.CapabilityEnabled)
        {
            return false;
        }

        try
        {
            return EngageCore(cartId, out captured);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Restores the captured constraints and wakes the body. True
    /// when the cart was found and restored; false when it no longer exists
    /// (nothing to restore — the constraint died with the object).</summary>
    public static bool TryRelease(string cartId, int captured)
    {
        if (!CartAdapter.CapabilityEnabled)
        {
            return false;
        }

        try
        {
            return ReleaseCore(cartId, captured);
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BrakeFacts ReadFactsCore(string? cartId)
    {
        bool inWorld = Player.m_localPlayer != null;
        if (cartId is null || !inWorld)
        {
            return new BrakeFacts(
                capabilityOk: true, inWorld, cartExists: false,
                isLocalAuthority: false, isAttached: false, float.MaxValue);
        }

        Vagon? vagon = CartAdapter.TryFindCartById(cartId) as Vagon;
        if (vagon == null)
        {
            return new BrakeFacts(
                capabilityOk: true, inWorld, cartExists: false,
                isLocalAuthority: false, isAttached: false, float.MaxValue);
        }

        ZNetView view = vagon.GetComponent<ZNetView>();
        bool isLocalAuthority = view != null && view.IsValid() && view.IsOwner();
        bool isAttached = vagon.IsAttached();
        float distance = Vector3.Distance(
            vagon.transform.position, Player.m_localPlayer.transform.position);

        return new BrakeFacts(
            capabilityOk: true, inWorld, cartExists: true,
            isLocalAuthority, isAttached, distance);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool EngageCore(string cartId, out int captured)
    {
        captured = 0;
        Vagon? vagon = CartAdapter.TryFindCartById(cartId) as Vagon;
        if (vagon == null)
        {
            return false;
        }

        Rigidbody body = vagon.GetComponent<Rigidbody>();
        if (body == null)
        {
            return false;
        }

        captured = (int)body.constraints;
        body.constraints = RigidbodyConstraints.FreezeAll;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReleaseCore(string cartId, int captured)
    {
        Vagon? vagon = CartAdapter.TryFindCartById(cartId) as Vagon;
        if (vagon == null)
        {
            return false;
        }

        Rigidbody body = vagon.GetComponent<Rigidbody>();
        if (body == null)
        {
            return false;
        }

        body.constraints = (RigidbodyConstraints)captured;
        body.WakeUp();
        return true;
    }
}
