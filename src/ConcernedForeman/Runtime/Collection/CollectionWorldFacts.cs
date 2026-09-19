using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary><c>[Collection]</c> settings.</summary>
internal sealed class CollectionSettings
{
    private CollectionSettings(ConfigEntry<float> workerCarryWeight, ConfigEntry<bool> thorsteinCollects)
    {
        WorkerCarryWeight = workerCarryWeight;
        ThorsteinCollects = thorsteinCollects;
    }

    /// <summary><c>Collection/WorkerCarryWeight</c> (CONTRACTS.md §5.6).</summary>
    public ConfigEntry<float> WorkerCarryWeight { get; }

    /// <summary><c>Collection/ThorsteinCollects</c> (#380). <b>Off by default,
    /// and that is a change to what this product already does.</b>
    ///
    /// Thorstein's primary role becomes construction once Gunnar owns the
    /// hauling, so his collection capability stays in the code and stops being
    /// something he does unasked. It is a setting rather than a deletion because
    /// the code is good, it is tested, and a player who wants it back should not
    /// have to wait for a release.
    ///
    /// <b>It gates starting an order and nothing else.</b> Status, preview,
    /// pause, resume, rebind and cancel keep working with it off, because an
    /// order already in flight holds real material out of a player's chest and a
    /// setting that stranded it would be the opposite of conserving.</summary>
    public ConfigEntry<bool> ThorsteinCollects { get; }

    public static CollectionSettings Bind(ConfigFile config)
    {
        ConfigEntry<float> carry = config.Bind(
            "Collection",
            "WorkerCarryWeight",
            CollectionParameters.DefaultWorkerCarryWeight,
            new ConfigDescription(
                "How much gathered Stone and Wood Thorstein carries per trip, by the game's item weight " +
                "(Stone and Wood weigh 2.0 each, so 100 is 50 units). This is the mod's own budget for a worker: " +
                "vanilla gives non-player characters no carry limit. Read when a world loads.",
                new AcceptableValueRange<float>(
                    CollectionParameters.MinWorkerCarryWeight, CollectionParameters.MaxWorkerCarryWeight)));
        ConfigEntry<bool> collects = config.Bind(
            "Collection",
            "ThorsteinCollects",
            false,
            new ConfigDescription(
                "Whether Thorstein will accept a new collection order. Off by default: his work is " +
                "building now, and Gunnar does the collecting and hauling. Turning it off never " +
                "abandons an order already running - you can still see it, pause it, resume it and " +
                "cancel it, so nothing that came out of a chest is stranded. Read when a command is " +
                "given, so it takes effect at once."));
        return new CollectionSettings(carry, collects);
    }
}

/// <summary>The world facts the collection loop asks at its checkpoints, read
/// from the installed game. Every answer that cannot be established refuses.
/// </summary>
internal sealed class CollectionWorldFacts : ICollectionWorld
{
    private readonly Func<bool> _runtimeEnabled;
    private readonly SettlementDiskReader _reader;
    private readonly WorkScopeBuilder _scopes;
    private readonly Func<ForemanWorkerAI?> _body;
    private readonly Func<Guid> _epoch;
    private readonly Dictionary<CollectedResource, float> _unitWeights = new Dictionary<CollectedResource, float>();

    public CollectionWorldFacts(
        Func<bool> runtimeEnabled, SettlementDiskReader reader, WorkScopeBuilder scopes, Func<ForemanWorkerAI?> body,
        Func<Guid> epoch)
    {
        _runtimeEnabled = runtimeEnabled;
        _reader = reader;
        _scopes = scopes;
        _body = body;
        _epoch = epoch;
    }

    /// <summary>D3 authority, from the live network state.</summary>
    public static WorkAuthorityFacts ReadAuthorityFacts(bool runtimeEnabled)
    {
        ZNet net = ZNet.instance;
        bool world = net != null && ZNetScene.instance != null;
        if (!world)
        {
            return new WorkAuthorityFacts(runtimeEnabled, false, false, false, 0);
        }

        int peers;
        try
        {
            peers = net!.GetPeers().Count;
        }
        catch (Exception)
        {
            // Unknown is not zero.
            peers = -1;
        }

        return new WorkAuthorityFacts(runtimeEnabled, true, net!.IsServer(), net.IsDedicated(), peers);
    }

    public WorkAuthorityVerdict EvaluateAuthority()
    {
        try
        {
            return WorkAuthorityPolicy.Evaluate(ReadAuthorityFacts(_runtimeEnabled()));
        }
        catch (Exception)
        {
            return WorkAuthorityVerdict.Unspecified;
        }
    }

    /// <summary>D12: recruited, holding a usable issued axe and hammer, checked
    /// on the live items. A record that cannot be read is not a recruitment.
    /// </summary>
    public ReadinessVerdict AssessReadiness(WorkerId worker)
    {
        if (!_reader.TryRead(out SettlementRegister register, out ReplayResult replay, out _))
        {
            return WorkerReadiness.Assess(worker, isRecruited: false, null!, WorkerReadiness.ForBuilding, UnknownToolCondition.Instance);
        }

        ForemanWorkerAI? body = _body();
        IToolCondition condition = body != null
            ? new WorldToolCondition(body.GetComponent<Humanoid>())
            : UnknownToolCondition.Instance;
        return WorkerReadiness.Assess(worker, register.Employs(worker), replay.Tools, WorkerReadiness.ForBuilding, condition);
    }

    public ScopeObservation ObserveScope(WorkScope scope) => _scopes.Observe(scope, _epoch());

    public bool IsLoaded(SitePoint point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        return zones != null && zones.IsZoneLoaded(NaturalSourceClassifier.ToVector3(point));
    }

    /// <summary>The game's own item weight, read once per world.</summary>
    public float UnitWeight(CollectedResource resource)
    {
        if (_unitWeights.TryGetValue(resource, out float cached))
        {
            return cached;
        }

        ObjectDB database = ObjectDB.instance;
        ItemDrop? prefab = database != null
            ? database.GetItemPrefab(CollectedResources.ItemPrefabName(resource))?.GetComponent<ItemDrop>()
            : null;
        float weight = prefab != null ? prefab.m_itemData.m_shared.m_weight : 0f;
        if (weight > 0f)
        {
            _unitWeights[resource] = weight;
        }

        return weight;
    }
}
