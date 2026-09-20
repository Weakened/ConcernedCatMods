using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The all-or-nothing startup probe of Gunnar's worker runtime
/// (CONTRACTS.md §2.5): every game and engine member the attach seam, the
/// worker body and the authority reader use, verified by reflection over types
/// resolved by name, so a changed game disables hauling with one line instead of
/// failing to load. The same list is checked against the installed game, off
/// game, by <c>scripts/audit-teamster-hauling-api.ps1</c>; the two change
/// together.</summary>
internal static class HaulingCapabilityProbe
{
    public static GameCapabilityReport Run()
    {
        try
        {
            var missingTypes = new List<string>();
            Type? vagon = Game("Vagon", missingTypes);
            Type? netView = Game("ZNetView", missingTypes);
            Type? zdo = Game("ZDO", missingTypes);
            Type? zdoId = Game("ZDOID", missingTypes);
            Type? zdoMan = Game("ZDOMan", missingTypes);
            Type? zdoVars = Game("ZDOVars", missingTypes);
            Type? player = Game("Player", missingTypes);
            Type? character = Game("Character", missingTypes);
            Type? baseAi = Game("BaseAI", missingTypes);
            Type? container = Game("Container", missingTypes);
            Type? chair = Game("Chair", missingTypes);
            Type? inventory = Game("Inventory", missingTypes);
            Type? znet = Game("ZNet", missingTypes);
            Type? znetPeer = Game("ZNetPeer", missingTypes);
            Type? znetScene = Game("ZNetScene", missingTypes);
            Type? zoneSystem = Game("ZoneSystem", missingTypes);
            Type? floating = Game("Floating", missingTypes);
            Type? humanoid = Game("Humanoid", missingTypes);
            Type? pickable = Game("Pickable", missingTypes);
            Type? itemDrop = Game("ItemDrop", missingTypes);
            Type? itemData = Game("ItemDrop+ItemData", missingTypes);
            Type? liquidType = Game("LiquidType", missingTypes);
            Type? heightmap = Game("Heightmap", missingTypes);
            Type? game = Game("Game", missingTypes);
            Game("Catapult", missingTypes);
            Game("SiegeMachine", missingTypes);
            Type? gameObject = Engine("UnityEngine.GameObject, UnityEngine.CoreModule", missingTypes);
            Type? transform = Engine("UnityEngine.Transform, UnityEngine.CoreModule", missingTypes);
            Type? vector3 = Engine("UnityEngine.Vector3, UnityEngine.CoreModule", missingTypes);
            Type? rigidbody = Engine("UnityEngine.Rigidbody, UnityEngine.PhysicsModule", missingTypes);
            Type? joint = Engine("UnityEngine.Joint, UnityEngine.PhysicsModule", missingTypes);
            Type? configurableJoint = Engine("UnityEngine.ConfigurableJoint, UnityEngine.PhysicsModule", missingTypes);
            Type? constraints = Engine("UnityEngine.RigidbodyConstraints, UnityEngine.PhysicsModule", missingTypes);
            if (missingTypes.Count > 0)
            {
                return new GameCapabilityReport(Array.Empty<string>(), missingTypes);
            }

            Type vagonList = typeof(List<>).MakeGenericType(vagon!);
            Type zdoList = typeof(List<>).MakeGenericType(zdo!);
            Type peerList = typeof(List<>).MakeGenericType(znetPeer!);
            Type byRefInt = typeof(int).MakeByRefType();
            Type byRefFloat = typeof(float).MakeByRefType();

            var requirements = new List<GameMemberRequirement>
            {
                // The attach seam itself (CONTRACTS.md §2.5 list first).
                new("Vagon", vagon, "AttachTo", GameMemberKind.InstanceMethod, typeof(void), new[] { gameObject }),
                new("Vagon", vagon, "Detach", GameMemberKind.InstanceMethod, typeof(void)),
                new("Vagon", vagon, "m_attachJoin", GameMemberKind.InstanceField, configurableJoint),
                new("Vagon", vagon, "m_bodies", GameMemberKind.InstanceField, rigidbody!.MakeArrayType()),
                new("Vagon", vagon, "m_attachPoint", GameMemberKind.InstanceField, transform),
                new("Vagon", vagon, "m_attachOffset", GameMemberKind.InstanceField, vector3),
                new("Vagon", vagon, "m_detachDistance", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_breakForce", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_playerExtraPullMass", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "InUse", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Vagon", vagon, "IsAttached", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Vagon", vagon, "m_nview", GameMemberKind.InstanceField, netView),
                new("Vagon", vagon, "m_body", GameMemberKind.InstanceField, rigidbody),
                new("Vagon", vagon, "m_container", GameMemberKind.InstanceField, container),
                new("Vagon", vagon, "m_chair", GameMemberKind.InstanceField, chair),
                new("Vagon", vagon, "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_itemWeightMassFactor", GameMemberKind.InstanceField, typeof(float)),
                new("Vagon", vagon, "m_instances", GameMemberKind.StaticField, vagonList),
                new("ZDOVars", zdoVars, "s_attachJointHash", GameMemberKind.StaticField, typeof(int)),

                // Network identity and ownership, read only (plus the worker's own key).
                new("ZNetView", netView, "IsValid", GameMemberKind.InstanceMethod, typeof(bool)),
                new("ZNetView", netView, "IsOwner", GameMemberKind.InstanceMethod, typeof(bool)),
                new("ZNetView", netView, "GetZDO", GameMemberKind.InstanceMethod, zdo),
                new("ZNetView", netView, "Destroy", GameMemberKind.InstanceMethod, typeof(void)),
                new("ZNetView", netView, "m_persistent", GameMemberKind.InstanceField, typeof(bool)),
                new("ZDO", zdo, "GetBool", GameMemberKind.InstanceMethod, typeof(bool), new[] { typeof(int), typeof(bool) }),
                new("ZDO", zdo, "GetString", GameMemberKind.InstanceMethod, typeof(string), new[] { typeof(string), typeof(string) }),
                new("ZDO", zdo, "Set", GameMemberKind.InstanceMethod, typeof(void), new[] { typeof(string), typeof(string) }),
                new("ZDO", zdo, "GetOwner", GameMemberKind.InstanceMethod, typeof(long)),
                new("ZDO", zdo, "m_uid", GameMemberKind.InstanceField, zdoId),
                new("ZDOMan", zdoMan, "instance", GameMemberKind.StaticProperty, zdoMan),
                new("ZDOMan", zdoMan, "GetZDO", GameMemberKind.InstanceMethod, zdo, new[] { zdoId }),
                new("ZDOMan", zdoMan, "GetSessionID", GameMemberKind.StaticMethod, typeof(long)),
                new("ZDOMan", zdoMan, "GetAllZDOsWithPrefabIterative", GameMemberKind.InstanceMethod, typeof(bool), new[] { typeof(string), zdoList, byRefInt }),

                // Gunnar's body and motor.
                new("Character", character, "m_originalMass", GameMemberKind.InstanceField, typeof(float)),
                new("Character", character, "IsDead", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Character", character, "SetWalk", GameMemberKind.InstanceMethod, typeof(void), new[] { typeof(bool) }),
                new("Character", character, "SetRun", GameMemberKind.InstanceMethod, typeof(void), new[] { typeof(bool) }),
                new("BaseAI", baseAi, "UpdateAI", GameMemberKind.InstanceMethod, typeof(bool), new[] { typeof(float) }),
                new("BaseAI", baseAi, "MoveTo", GameMemberKind.InstanceMethod, typeof(bool), new[] { typeof(float), vector3, typeof(float), typeof(bool) }),
                new("BaseAI", baseAi, "MoveTowards", GameMemberKind.InstanceMethod, typeof(void), new[] { vector3, typeof(bool) }),
                new("BaseAI", baseAi, "StopMoving", GameMemberKind.InstanceMethod, typeof(void)),
                new("BaseAI", baseAi, "LookTowards", GameMemberKind.InstanceMethod, typeof(void), new[] { vector3 }),
                new("BaseAI", baseAi, "FoundPath", GameMemberKind.InstanceMethod, typeof(bool)),
                new("BaseAI", baseAi, "m_canBeAlerted", GameMemberKind.InstanceField, typeof(bool)),
                new("Player", player, "m_localPlayer", GameMemberKind.StaticField, player),
                new("Player", player, "GetHoverObject", GameMemberKind.InstanceMethod, gameObject),

                // Cart contents (evidence only) and use.
                new("Container", container, "IsInUse", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Chair", chair, "IsInUse", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Container", container, "GetInventory", GameMemberKind.InstanceMethod, inventory),
                new("Inventory", inventory, "GetTotalWeight", GameMemberKind.InstanceMethod, typeof(float)),
                new("Inventory", inventory, "NrOfItems", GameMemberKind.InstanceMethod, typeof(int)),

                // Work authority and world lifecycle.
                new("ZNet", znet, "instance", GameMemberKind.StaticProperty, znet),
                new("ZNet", znet, "IsServer", GameMemberKind.InstanceMethod, typeof(bool)),
                new("ZNet", znet, "IsDedicated", GameMemberKind.InstanceMethod, typeof(bool)),
                new("ZNet", znet, "GetPeers", GameMemberKind.InstanceMethod, peerList),
                new("ZNetScene", znetScene, "instance", GameMemberKind.StaticProperty, znetScene),
                new("Game", game, "instance", GameMemberKind.StaticProperty, game),
                new("Game", game, "IsShuttingDown", GameMemberKind.InstanceMethod, typeof(bool)),

                // Ground, water and loaded area (parking and the placeholder planner).
                new("ZoneSystem", zoneSystem, "instance", GameMemberKind.StaticProperty, zoneSystem),
                new("ZoneSystem", zoneSystem, "IsZoneLoaded", GameMemberKind.InstanceMethod, typeof(bool), new[] { vector3 }),
                new("ZoneSystem", zoneSystem, "GetGroundHeight", GameMemberKind.InstanceMethod, typeof(bool), new[] { vector3, byRefFloat }),
                new("Floating", floating, "GetLiquidLevel", GameMemberKind.StaticMethod, typeof(float), new[] { vector3, typeof(float), liquidType }),
                new("Heightmap", heightmap, "GetHeight", GameMemberKind.StaticMethod, typeof(bool), new[] { vector3, byRefFloat }),

                // The collection carve-out (#381). Every member GunnarCollectionPort
                // binds, verified at start-up like the rest: a changed game
                // disables collection with one line rather than throwing out of
                // a worker tick. The audit script checks the same list against
                // the installed game; the two change together.
                new("Pickable", pickable, "Interact", GameMemberKind.InstanceMethod, typeof(bool),
                    new[] { humanoid!, typeof(bool), typeof(bool) }),
                new("Pickable", pickable, "CanBePicked", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Pickable", pickable, "m_nview", GameMemberKind.InstanceField, netView),
                new("Pickable", pickable, "m_itemPrefab", GameMemberKind.InstanceField, gameObject),
                new("Pickable", pickable, "m_amount", GameMemberKind.InstanceField, typeof(int)),
                new("Pickable", pickable, "m_tarPreventsPicking", GameMemberKind.InstanceField, typeof(bool)),
                new("Humanoid", humanoid, "Pickup", GameMemberKind.InstanceMethod, typeof(bool),
                    new[] { gameObject!, typeof(bool), typeof(bool) }),
                new("Character", character, "m_nview", GameMemberKind.InstanceField, netView),
                new("ItemDrop", itemDrop, "m_itemData", GameMemberKind.InstanceField, itemData),
                new("ItemDrop+ItemData", itemData, "m_stack", GameMemberKind.InstanceField, typeof(int)),

                // Engine members the joint and body checks read.
                new("Joint", joint, "connectedBody", GameMemberKind.InstanceProperty, rigidbody),
                new("Joint", joint, "currentForce", GameMemberKind.InstanceProperty, vector3),
                new("Rigidbody", rigidbody, "isKinematic", GameMemberKind.InstanceProperty, typeof(bool)),
                new("Rigidbody", rigidbody, "useGravity", GameMemberKind.InstanceProperty, typeof(bool)),
                new("Rigidbody", rigidbody, "detectCollisions", GameMemberKind.InstanceProperty, typeof(bool)),
                new("Rigidbody", rigidbody, "mass", GameMemberKind.InstanceProperty, typeof(float)),
                new("Rigidbody", rigidbody, "constraints", GameMemberKind.InstanceProperty, constraints),
                new("Rigidbody", rigidbody, "linearVelocity", GameMemberKind.InstanceProperty, vector3),
            };

            return GameMemberProbe.Probe(requirements);
        }
        catch (Exception exception)
        {
            return new GameCapabilityReport(
                Array.Empty<string>(),
                new[] { "hauling capability probe failed (" + exception.GetType().Name + ")" });
        }
    }

    private static Type? Game(string name, List<string> missing) => Resolve(name + ", assembly_valheim", name, missing);

    private static Type? Engine(string qualifiedName, List<string> missing) =>
        Resolve(qualifiedName, qualifiedName.Substring(0, qualifiedName.IndexOf(',')), missing);

    private static Type? Resolve(string qualifiedName, string label, List<string> missing)
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

        if (resolved == null)
        {
            missing.Add(label + " (type not found)");
        }

        return resolved;
    }
}
