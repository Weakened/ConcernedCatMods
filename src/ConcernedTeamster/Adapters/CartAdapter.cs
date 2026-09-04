using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>The single seam over the game's cart implementation (CT-002).
/// Read-only: it observes carts and never calls interaction, RPC, or any
/// mutating member. Every member it touches is verified once at startup by
/// <see cref="ProbeCapability"/> against the running game; when anything is
/// missing the capability disables and <see cref="TryCreateSnapshot"/> returns
/// null forever (fail closed). Only this folder may name Valheim types; the
/// verified surface and its semantics live in CART_INTERNALS.md.</summary>
public static class CartAdapter
{
    private static GameCapabilityReport? _capability;

    /// <summary>True after a successful probe; snapshots are refused
    /// otherwise.</summary>
    public static bool CapabilityEnabled => _capability is not null && _capability.Enabled;

    /// <summary>Probes every game member the adapter compiled against.
    /// Called once from plugin startup; the caller logs the outcome as one
    /// line. Types are resolved by name so that even wholesale removal of a
    /// game type degrades to a disabled capability instead of a type-load
    /// crash.</summary>
    public static GameCapabilityReport ProbeCapability()
    {
        GameCapabilityReport report = BuildReport();
        _capability = report;
        return report;
    }

    private static GameCapabilityReport BuildReport()
    {
        try
        {
            var missingTypes = new List<string>();
            Type? vagon = ResolveGameType("Vagon", missingTypes);
            Type? container = ResolveGameType("Container", missingTypes);
            Type? inventory = ResolveGameType("Inventory", missingTypes);
            Type? netView = ResolveGameType("ZNetView", missingTypes);
            Type? zdo = ResolveGameType("ZDO", missingTypes);
            Type? zdoid = ResolveGameType("ZDOID", missingTypes);
            Type? player = ResolveGameType("Player", missingTypes);
            Type? character = ResolveGameType("Character", missingTypes);
            if (missingTypes.Count > 0)
            {
                return new GameCapabilityReport(Array.Empty<string>(), missingTypes);
            }

            // This table and CreateSnapshotCore must change together: every
            // game member the accessor touches appears here, and both must
            // match the verified table in CART_INTERNALS.md.
            var requirements = new List<GameMemberRequirement>
            {
                new("Vagon", vagon, "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_itemWeightMassFactor", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_container", GameMemberKind.InstanceField, container),
                new("Vagon", vagon, "IsAttached", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Vagon", vagon, "IsAttached", GameMemberKind.InstanceMethod, typeof(bool), new[] { character }),
                new("Container", container, "GetInventory", GameMemberKind.InstanceMethod, inventory),
                new("Inventory", inventory, "GetTotalWeight", GameMemberKind.InstanceMethod, typeof(float)),
                new("ZNetView", netView, "IsValid", GameMemberKind.InstanceMethod, typeof(bool)),
                new("ZNetView", netView, "GetZDO", GameMemberKind.InstanceMethod, zdo),
                new("ZDO", zdo, "m_uid", GameMemberKind.InstanceField, zdoid),
                new("Player", player, "m_localPlayer", GameMemberKind.StaticField, player),
            };
            return GameMemberProbe.Probe(requirements);
        }
        catch (Exception exception)
        {
            return new GameCapabilityReport(
                Array.Empty<string>(),
                new[] { $"capability probe failed ({exception.GetType().Name})" });
        }
    }

    private static Type? ResolveGameType(string typeName, List<string> missingTypes)
    {
        Type? resolved = null;
        try
        {
            resolved = Type.GetType(typeName + ", assembly_valheim", throwOnError: false);
        }
        catch
        {
            // Treated as unresolved below; the probe result is the report.
        }

        if (resolved is null)
        {
            missingTypes.Add($"{typeName} (type not found)");
        }

        return resolved;
    }

    /// <summary>Maps a live cart component to an immutable snapshot, or null:
    /// capability disabled, not a cart, destroyed, no network identity, or
    /// any game-side surprise. Never throws.</summary>
    public static CartSnapshot? TryCreateSnapshot(object? cartComponent)
    {
        if (cartComponent is null || !CapabilityEnabled)
        {
            return null;
        }

        try
        {
            return CreateSnapshotCore(cartComponent);
        }
        catch
        {
            // Fail closed: a game change or lifecycle race yields no
            // snapshot, never an exception in a caller.
            return null;
        }
    }

    /// <summary>The only method that touches cart members directly.
    /// NoInlining keeps its member tokens out of every caller, so when the
    /// game removes a member, nothing resolves them unless the probe already
    /// verified they exist (JIT isolation for fail-closed startup).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CartSnapshot? CreateSnapshotCore(object cartComponent)
    {
        Vagon? vagon = cartComponent as Vagon;
        // Unity's == overload treats destroyed components as null; reference
        // null checks would miss them, so Unity objects use == throughout.
        if (vagon == null)
        {
            return null;
        }

        ZNetView view = vagon.GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            return null;
        }

        ZDO? zdo = view.GetZDO();
        if (zdo is null)
        {
            return null;
        }

        string cartId = zdo.m_uid.ToString();
        float baseMass = vagon.m_baseMass;
        float itemWeightMassFactor = vagon.m_itemWeightMassFactor;

        float cargoWeight = 0f;
        Container container = vagon.m_container;
        if (container != null)
        {
            Inventory? cargo = container.GetInventory();
            if (cargo is not null)
            {
                cargoWeight = cargo.GetTotalWeight();
            }
        }

        bool isAttached = vagon.IsAttached();
        bool isPulledByLocalPlayer = false;
        Player localPlayer = Player.m_localPlayer;
        if (localPlayer != null)
        {
            isPulledByLocalPlayer = vagon.IsAttached(localPlayer);
        }

        return CartSnapshot.Create(
            cartId,
            baseMass,
            cargoWeight,
            itemWeightMassFactor,
            isAttached,
            isPulledByLocalPlayer);
    }
}
