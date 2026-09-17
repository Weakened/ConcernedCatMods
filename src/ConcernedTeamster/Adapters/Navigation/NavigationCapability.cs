using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Navigation;

/// <summary>Every game member Gunnar's cart navigation adapters touch, verified
/// once against the running game (#314). When anything is missing the
/// capability is off: the path source answers Unavailable, the probe answers
/// Unreadable, and every plan is refused - navigation fails closed, and nothing
/// else in Teamster is affected.
///
/// The same members are checked against the installed binary, offline, by
/// <c>scripts/audit-teamster-navigation-api.ps1</c>; this table, the adapters'
/// NoInlining cores and that script change together
/// (<c>docs/mods/concerned-teamster/CART_ROUTES.md</c>, "Game surface").</summary>
internal static class NavigationCapability
{
    private static GameCapabilityReport? _report;

    /// <summary>Probed on first use, so no plugin wiring is needed.</summary>
    public static GameCapabilityReport Report => _report ??= Build();

    public static bool Enabled => Report.Enabled;

    /// <summary>Probes again, for an explicit startup log line.</summary>
    public static GameCapabilityReport Probe()
    {
        _report = Build();
        return _report;
    }

    private static GameCapabilityReport Build()
    {
        try
        {
            var missing = new List<string>();
            Type? pathfinding = Game("Pathfinding", missing);
            Type? agentType = Game("Pathfinding+AgentType", missing);
            Type? zoneSystem = Game("ZoneSystem", missing);
            Type? heightmap = Game("Heightmap", missing);
            Type? floating = Game("Floating", missing);
            Type? liquidType = Game("LiquidType", missing);
            Type? piece = Game("Piece", missing);
            Type? door = Game("Door", missing);
            Type? cart = Game("Vagon", missing);
            Type? vector3 = Named("UnityEngine.Vector3, UnityEngine.CoreModule", missing);
            Type? quaternion = Named("UnityEngine.Quaternion, UnityEngine.CoreModule", missing);
            Type? transform = Named("UnityEngine.Transform, UnityEngine.CoreModule", missing);
            Type? layerMask = Named("UnityEngine.LayerMask, UnityEngine.CoreModule", missing);
            Type? physics = Named("UnityEngine.Physics, UnityEngine.PhysicsModule", missing);
            Type? raycastHit = Named("UnityEngine.RaycastHit, UnityEngine.PhysicsModule", missing);
            Type? collider = Named("UnityEngine.Collider, UnityEngine.PhysicsModule", missing);
            Type? rigidbody = Named("UnityEngine.Rigidbody, UnityEngine.PhysicsModule", missing);
            Type? trigger = Named("UnityEngine.QueryTriggerInteraction, UnityEngine.PhysicsModule", missing);
            if (missing.Count > 0)
            {
                return new GameCapabilityReport(Array.Empty<string>(), missing);
            }

            Type vectorList = typeof(List<>).MakeGenericType(vector3!);
            Type pieceList = typeof(List<>).MakeGenericType(piece!);
            var requirements = new List<GameMemberRequirement>
            {
                new("Pathfinding", pathfinding, "instance", GameMemberKind.StaticProperty, pathfinding),
                new("Pathfinding", pathfinding, "GetPath", GameMemberKind.InstanceMethod, typeof(bool),
                    new[] { vector3, vector3, vectorList, agentType, typeof(bool), typeof(bool), typeof(bool) }),
                new("ZoneSystem", zoneSystem, "instance", GameMemberKind.StaticProperty, zoneSystem),
                new("ZoneSystem", zoneSystem, "IsZoneLoaded", GameMemberKind.InstanceMethod, typeof(bool), new[] { vector3 }),
                new("ZoneSystem", zoneSystem, "m_waterLevel", GameMemberKind.InstanceField, typeof(float)),
                new("Heightmap", heightmap, "FindHeightmap", GameMemberKind.StaticMethod, heightmap, new[] { vector3 }),
                new("Heightmap", heightmap, "IsLava", GameMemberKind.InstanceMethod, typeof(bool),
                    new[] { vector3, typeof(float) }),
                new("Floating", floating, "GetLiquidLevel", GameMemberKind.StaticMethod, typeof(float),
                    new[] { vector3, typeof(float), liquidType }),
                new("Piece", piece, "GetAllPiecesInRadius", GameMemberKind.StaticMethod, typeof(void),
                    new[] { vector3, typeof(float), pieceList }),
                new("Door", door, "m_name", GameMemberKind.InstanceField, typeof(string)),
                new("Vagon", cart, "m_attachPoint", GameMemberKind.InstanceField, transform),
                new("Vagon", cart, "m_wheels", GameMemberKind.InstanceField, rigidbody!.MakeArrayType()),
                new("Physics", physics, "RaycastNonAlloc", GameMemberKind.StaticMethod, typeof(int),
                    new[] { vector3, vector3, raycastHit!.MakeArrayType(), typeof(float), typeof(int), trigger }),
                new("Physics", physics, "BoxCastNonAlloc", GameMemberKind.StaticMethod, typeof(int),
                    new[] { vector3, vector3, vector3, raycastHit.MakeArrayType(), quaternion, typeof(float), typeof(int), trigger }),
                new("Physics", physics, "OverlapBoxNonAlloc", GameMemberKind.StaticMethod, typeof(int),
                    new[] { vector3, vector3, collider!.MakeArrayType(), quaternion, typeof(int), trigger }),
                new("LayerMask", layerMask, "NameToLayer", GameMemberKind.StaticMethod, typeof(int), new[] { typeof(string) }),
            };

            return GameMemberProbe.Probe(requirements);
        }
        catch (Exception exception)
        {
            return new GameCapabilityReport(
                Array.Empty<string>(), new[] { $"navigation capability probe failed ({exception.GetType().Name})" });
        }
    }

    private static Type? Game(string name, List<string> missing) => Named(name + ", assembly_valheim", missing, name);

    private static Type? Named(string qualifiedName, List<string> missing, string? label = null)
    {
        Type? resolved = null;
        try
        {
            resolved = Type.GetType(qualifiedName, throwOnError: false);
        }
        catch
        {
            // Reported as missing below.
        }

        if (resolved is null)
        {
            missing.Add($"{label ?? qualifiedName} (type not found)");
        }

        return resolved;
    }
}
