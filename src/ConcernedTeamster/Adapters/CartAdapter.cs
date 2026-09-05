using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;
using TheConcernedCat.ConcernedTeamster.Domain.Cargo;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using UnityEngine;

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

            // CT-003: the cart registry's exact List<Vagon> shape, and the
            // Unity 6 velocity property (the pre-6 "velocity" name is
            // [Obsolete] on this build — see CART_INTERNALS.md). Unity types
            // are resolved by name for the same reason game types are: the
            // probe path must never contain a type token that could fail to
            // load before the probe can report.
            Type vagonList = typeof(List<>).MakeGenericType(vagon!);
            Type? rigidbody = ResolveNamedType(
                "UnityEngine.Rigidbody, UnityEngine.PhysicsModule", "UnityEngine.Rigidbody", missingTypes);
            Type? vector3 = ResolveNamedType(
                "UnityEngine.Vector3, UnityEngine.CoreModule", "UnityEngine.Vector3", missingTypes);
            // CT-004 terrain members (heights + paint; verified in
            // CART_INTERNALS.md).
            Type? heightmap = ResolveGameType("Heightmap", missingTypes);
            Type? transform = ResolveNamedType(
                "UnityEngine.Transform, UnityEngine.CoreModule", "UnityEngine.Transform", missingTypes);
            Type? color = ResolveNamedType(
                "UnityEngine.Color, UnityEngine.CoreModule", "UnityEngine.Color", missingTypes);
            // CT-006 cargo manifest members.
            Type? itemData = ResolveGameType("ItemDrop+ItemData", missingTypes);
            Type? sharedData = ResolveGameType("ItemDrop+ItemData+SharedData", missingTypes);
            Type? gameObjectType = ResolveNamedType(
                "UnityEngine.GameObject, UnityEngine.CoreModule", "UnityEngine.GameObject", missingTypes);
            // CT-016 world identity for sidecar keying.
            Type? znet = ResolveGameType("ZNet", missingTypes);
            if (missingTypes.Count > 0)
            {
                return new GameCapabilityReport(Array.Empty<string>(), missingTypes);
            }

            Type itemDataList = typeof(List<>).MakeGenericType(itemData!);

            // This table and the accessor cores (CreateSnapshotCore,
            // TryReadVelocityCore, CollectNearbyCartsCore, HasLocalPlayerCore,
            // ReadManifestCore, TerrainAdapter.ReadGroundCore) must change
            // together: every game member any core touches appears here, and
            // both must match CART_INTERNALS.md.
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
                new("Vagon", vagon, "m_instances", GameMemberKind.StaticField, vagonList),
                new("Rigidbody", rigidbody, "linearVelocity", GameMemberKind.InstanceProperty, vector3),
                new("Vagon", vagon, "m_attachPoint", GameMemberKind.InstanceField, transform),
                new("Heightmap", heightmap, "GetHeight", GameMemberKind.StaticMethod, typeof(bool),
                    new[] { vector3, typeof(float).MakeByRefType() }),
                new("Heightmap", heightmap, "FindHeightmap", GameMemberKind.StaticMethod, heightmap,
                    new[] { vector3 }),
                new("Heightmap", heightmap, "WorldToVertex", GameMemberKind.InstanceMethod, typeof(void),
                    new[] { vector3, typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }),
                new("Heightmap", heightmap, "GetPaintMask", GameMemberKind.InstanceMethod, color,
                    new[] { typeof(int), typeof(int) }),
                new("Inventory", inventory, "GetAllItems", GameMemberKind.InstanceMethod, itemDataList),
                new("ItemData", itemData, "m_stack", GameMemberKind.InstanceField, typeof(int)),
                new("ItemData", itemData, "m_shared", GameMemberKind.InstanceField, sharedData),
                new("ItemData", itemData, "m_dropPrefab", GameMemberKind.InstanceField, gameObjectType),
                new("ItemData", itemData, "GetWeight", GameMemberKind.InstanceMethod, typeof(float),
                    new[] { typeof(int) }),
                new("ItemData", itemData, "GetNonStackedWeight", GameMemberKind.InstanceMethod, typeof(float)),
                new("SharedData", sharedData, "m_name", GameMemberKind.InstanceField, typeof(string)),
                // CT-012 parking brake members: vanilla authority check and
                // the runtime-only physics constraint property.
                new("ZNetView", netView, "IsOwner", GameMemberKind.InstanceMethod, typeof(bool)),
                new("Rigidbody", rigidbody, "constraints", GameMemberKind.InstanceProperty,
                    ResolveNamedType(
                        "UnityEngine.RigidbodyConstraints, UnityEngine.PhysicsModule",
                        "UnityEngine.RigidbodyConstraints", missingTypes)),
                new("ZNet", znet, "instance", GameMemberKind.StaticProperty, znet),
                new("ZNet", znet, "GetWorldUID", GameMemberKind.InstanceMethod, typeof(long)),
            };
            if (missingTypes.Count > 0)
            {
                return new GameCapabilityReport(Array.Empty<string>(), missingTypes);
            }

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
        return ResolveNamedType(typeName + ", assembly_valheim", typeName, missingTypes);
    }

    private static Type? ResolveNamedType(string assemblyQualifiedName, string label, List<string> missingTypes)
    {
        Type? resolved = null;
        try
        {
            resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        }
        catch
        {
            // Treated as unresolved below; the probe result is the report.
        }

        if (resolved is null)
        {
            missingTypes.Add($"{label} (type not found)");
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
        bool cargoDataAvailable = false;
        Container container = vagon.m_container;
        if (container != null)
        {
            Inventory? cargo = container.GetInventory();
            if (cargo is not null)
            {
                cargoWeight = cargo.GetTotalWeight();
                cargoDataAvailable = true;
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
            cargoDataAvailable,
            itemWeightMassFactor,
            isAttached,
            isPulledByLocalPlayer);
    }

    /// <summary>Maps one cart component to full telemetry (snapshot, motion,
    /// terrain, timestamp), or null when the cart is gone or unreadable.
    /// Shaped to plug directly into the domain sampler, whose store supplies
    /// the previous sample for grade smoothing — so smoothing state inherits
    /// the sampler's reset and eviction lifecycle. Never throws.</summary>
    public static CartTelemetry? TrySampleCart(
        object? cartComponent,
        double sampleTimeSeconds,
        IReadOnlyDictionary<string, CartTelemetry> previousByCartId)
    {
        if (cartComponent is null || !CapabilityEnabled)
        {
            return null;
        }

        try
        {
            CartSnapshot? snapshot = CreateSnapshotCore(cartComponent);
            if (snapshot is null)
            {
                return null;
            }

            bool velocityAvailable = TryReadVelocityCore(
                cartComponent, out float speedMetersPerSecond, out float verticalSpeedMetersPerSecond);
            TryReadPositionCore(cartComponent, out float positionX, out float positionZ);

            TerrainAdapter.GroundReading ground = TerrainAdapter.TryReadGround(cartComponent);
            float previousSmoothedPercent = float.NaN;
            GradeDirection previousDirection = GradeDirection.Level;
            if (previousByCartId is not null &&
                previousByCartId.TryGetValue(snapshot.CartId, out CartTelemetry previous) &&
                previous.GradeAvailable)
            {
                previousSmoothedPercent = previous.SmoothedGradePercent;
                previousDirection = previous.GradeDirection;
            }

            float smoothedGradePercent = GradeMath.Smooth(previousSmoothedPercent, ground.InstantGradePercent);
            GradeDirection gradeDirection = GradeMath.ClassifyDirection(smoothedGradePercent, previousDirection);

            return CartTelemetry.Create(
                snapshot,
                velocityAvailable,
                speedMetersPerSecond,
                verticalSpeedMetersPerSecond,
                ground.GradeAvailable,
                ground.InstantGradePercent,
                smoothedGradePercent,
                gradeDirection,
                ground.Surface,
                sampleTimeSeconds,
                positionX,
                positionZ);
        }
        catch
        {
            // Fail closed, same contract as TryCreateSnapshot.
            return null;
        }
    }

    /// <summary>Fills the (cleared) buffer with live carts within the options
    /// radius of the local player, nearest first, at most MaxTrackedCarts.
    /// Leaves the buffer empty when there is no local player or the
    /// capability is off. Never throws.</summary>
    public static void CollectNearbyCarts(List<object> buffer, TelemetrySamplerOptions options)
    {
        if (!CapabilityEnabled)
        {
            return;
        }

        try
        {
            CollectNearbyCartsCore(buffer, options);
        }
        catch
        {
            // A partial buffer is fine: every entry is re-validated by
            // TrySampleCart, and the next tick retries.
        }
    }

    /// <summary>True while a local player exists — the pump's session signal:
    /// its false-transition (logout, world switch) resets the sampler so no
    /// other world's carts can ever be shown. Never throws.</summary>
    public static bool HasLocalPlayer()
    {
        if (!CapabilityEnabled)
        {
            return false;
        }

        try
        {
            return HasLocalPlayerCore();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads the cart container into an immutable manifest, or
    /// null when the cart or its container is unreadable (no container is
    /// different from an empty one, which yields an empty manifest).
    /// Individual broken items become explicit unreadable-slot markers
    /// instead of skewing totals. Read-only: no container, inventory, stack,
    /// or item member is ever written. Never throws.</summary>
    public static CargoManifest? TryReadManifest(object? cartComponent, double captureTimeSeconds)
    {
        if (cartComponent is null || !CapabilityEnabled)
        {
            return null;
        }

        try
        {
            return ReadManifestCore(cartComponent, captureTimeSeconds);
        }
        catch
        {
            // Fail closed, same contract as TryCreateSnapshot.
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CargoManifest? ReadManifestCore(object cartComponent, double captureTimeSeconds)
    {
        Vagon? vagon = cartComponent as Vagon;
        if (vagon == null)
        {
            return null;
        }

        Container container = vagon.m_container;
        if (container == null)
        {
            return null;
        }

        Inventory? inventory = container.GetInventory();
        if (inventory is null)
        {
            return null;
        }

        List<ItemDrop.ItemData>? items = inventory.GetAllItems();
        if (items is null || items.Count == 0)
        {
            return CargoManifest.CreateEmpty(captureTimeSeconds);
        }

        var entries = new List<CargoEntry>(items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            ItemDrop.ItemData? item = items[index];
            if (item is null)
            {
                continue;
            }

            try
            {
                string? token = item.m_shared?.m_name;
                // The prefab is an asset reference; its name is the stable
                // item id. Reading .name allocates, which is why manifest
                // refreshes are tracker-bounded, never per-frame.
                string? itemId = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                entries.Add(CargoEntry.Create(
                    itemId ?? token,
                    token,
                    item.m_stack,
                    item.GetNonStackedWeight(),
                    item.GetWeight(),
                    weightKnown: true));
            }
            catch
            {
                // A broken modded item must not hide the rest of the cargo
                // nor silently skew totals.
                entries.Add(CargoEntry.CreateUnreadable(index));
            }
        }

        return CargoManifest.Create(entries, captureTimeSeconds);
    }

    /// <summary>Finds a live tracked cart by its snapshot cart id, or null.
    /// Bounded by the instance registry size; callers keep it off the
    /// per-frame path (the manifest tracker calls at most once per second).
    /// Never throws.</summary>
    public static object? TryFindCartById(string? cartId)
    {
        if (string.IsNullOrEmpty(cartId) || !CapabilityEnabled)
        {
            return null;
        }

        try
        {
            return FindCartByIdCore(cartId!);
        }
        catch
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? FindCartByIdCore(string cartId)
    {
        List<Vagon> instances = Vagon.m_instances;
        if (instances is null)
        {
            return null;
        }

        for (int index = 0; index < instances.Count; index++)
        {
            Vagon cart = instances[index];
            if (cart == null)
            {
                continue;
            }

            ZNetView view = cart.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                continue;
            }

            ZDO? zdo = view.GetZDO();
            if (zdo is null)
            {
                continue;
            }

            if (zdo.m_uid.ToString() == cartId)
            {
                return cart;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryReadPositionCore(object cartComponent, out float positionX, out float positionZ)
    {
        positionX = 0f;
        positionZ = 0f;
        Vagon? vagon = cartComponent as Vagon;
        if (vagon == null)
        {
            return;
        }

        Vector3 position = vagon.transform.position;
        positionX = position.x;
        positionZ = position.z;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasLocalPlayerCore()
    {
        return Player.m_localPlayer != null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryReadVelocityCore(
        object cartComponent, out float speedMetersPerSecond, out float verticalSpeedMetersPerSecond)
    {
        speedMetersPerSecond = 0f;
        verticalSpeedMetersPerSecond = 0f;
        Vagon? vagon = cartComponent as Vagon;
        if (vagon == null)
        {
            return false;
        }

        // The root rigidbody is the same object the cart caches privately in
        // Awake (verified in CART_INTERNALS.md), reachable via public API.
        Rigidbody body = vagon.GetComponent<Rigidbody>();
        if (body == null)
        {
            return false;
        }

        Vector3 velocity = body.linearVelocity;
        speedMetersPerSecond = velocity.magnitude;
        verticalSpeedMetersPerSecond = velocity.y;
        return true;
    }

    private static readonly List<float> DistanceBuffer = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectNearbyCartsCore(List<object> buffer, TelemetrySamplerOptions options)
    {
        Player localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return;
        }

        List<Vagon> instances = Vagon.m_instances;
        if (instances is null)
        {
            return;
        }

        Vector3 origin = localPlayer.transform.position;
        float radiusSquared = options.SearchRadiusMeters * options.SearchRadiusMeters;
        int cap = options.MaxTrackedCarts;

        // Bounded nearest-first insertion with a reused parallel distance
        // list: no sort delegates, no per-tick allocations once capacities
        // are grown. Main-thread only, like all Unity access.
        DistanceBuffer.Clear();
        for (int index = 0; index < instances.Count; index++)
        {
            Vagon cart = instances[index];
            if (cart == null)
            {
                continue;
            }

            float distanceSquared = (cart.transform.position - origin).sqrMagnitude;
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            int insertAt = buffer.Count;
            while (insertAt > 0 && DistanceBuffer[insertAt - 1] > distanceSquared)
            {
                insertAt--;
            }

            if (insertAt >= cap)
            {
                continue;
            }

            buffer.Insert(insertAt, cart);
            DistanceBuffer.Insert(insertAt, distanceSquared);
            if (buffer.Count > cap)
            {
                buffer.RemoveAt(buffer.Count - 1);
                DistanceBuffer.RemoveAt(DistanceBuffer.Count - 1);
            }
        }

        DistanceBuffer.Clear();
    }
}
