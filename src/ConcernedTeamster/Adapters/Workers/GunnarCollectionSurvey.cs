using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The game side of Gunnar's survey (#381): which loaded network objects
/// inside the area are loose stones and fallen branches, with the facts
/// <see cref="GunnarTargetPredicate"/> decides on.
///
/// <b>Nothing here decides.</b> Every read is a field, a component or a vanilla
/// query, and every comparison lives in the tested predicate. This is Concerned
/// Foreman's proven survey shape (<c>WorldSourceSurvey</c>,
/// <c>NaturalSourceClassifier</c>) re-written for this product, because products
/// never share game code (`DECISIONS.md` D2).
///
/// <b>Why the scene's instance table and not a physics query.</b> A physics
/// query finds live colliders only, and a picked branch hides its model: a
/// physics survey could report a branch as missing but never as used up. The
/// scene's own table holds every instantiated network object in loaded ground,
/// and a prefab-hash comparison is the cheapest identity test there is.
///
/// <b>Bounded.</b> At most <see cref="CollectionLimits.MostCandidatesPerSurvey"/>
/// candidates are described per look; a look that reaches the ceiling says
/// <see cref="SurveyOutcome.Truncated"/>, so the rest are worked in later rounds
/// and never mistaken for an empty area. Ground in the area that is not loaded
/// makes the look <see cref="SurveyOutcome.NotLoaded"/>: what is there could not
/// be established.
///
/// <b>Reads only.</b> The ward query is asked with <c>flash: false</c>, so no
/// guard stone lights up and nothing is sent anywhere.</summary>
internal sealed class GunnarCollectionSurvey
{
    private static readonly int StoneHash = GunnarCollectionAllowlist.LooseStonePrefab.GetStableHashCode();
    private static readonly int BranchHash = GunnarCollectionAllowlist.BranchPrefab.GetStableHashCode();

    private readonly CollectionLimits _limits;
    private readonly List<ZNetView> _scene = new List<ZNetView>();
    private readonly List<CollectionCandidate> _found = new List<CollectionCandidate>();
    private readonly Dictionary<string, ZNetView> _views = new Dictionary<string, ZNetView>(StringComparer.Ordinal);

    public GunnarCollectionSurvey(CollectionLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>How many sources the last look kept a handle on.</summary>
    internal int Remembered => _views.Count;

    /// <summary>Forgets every handle. A world going away makes every key
    /// meaningless.</summary>
    internal void Forget() => _views.Clear();

    /// <summary>One look at the area.</summary>
    /// <param name="loadKilograms">What one whole load may weigh, so a single
    /// source too heavy for any load is refused as over the carry limit rather
    /// than planned and never finished.</param>
    internal CollectionSurvey Look(CollectionArea? area, float loadKilograms)
    {
        if (area == null)
        {
            return CollectionSurvey.Unavailable("no area was set");
        }

        ZNetScene scene = ZNetScene.instance;
        ZoneSystem zones = ZoneSystem.instance;
        if (scene == null || zones == null)
        {
            return CollectionSurvey.Unavailable("no world is loaded");
        }

        _views.Clear();
        _found.Clear();
        _scene.Clear();
        _scene.AddRange(scene.m_instances.Values);

        CollectionPoint centre = area.Centre;
        float reach = area.BoundingRadiusMetres;
        bool truncated = false;
        for (int index = 0; index < _scene.Count; index++)
        {
            ZNetView view = _scene[index];
            if (view == null || !view.IsValid())
            {
                continue;
            }

            ZDO zdo = view.GetZDO();
            int hash = zdo.GetPrefab();
            if (hash != StoneHash && hash != BranchHash)
            {
                continue;
            }

            Vector3 position = view.transform.position;
            var where = new CollectionPoint(position.x, position.y, position.z);
            if (centre.FlatDistanceTo(where) > reach)
            {
                continue;
            }

            if (_found.Count >= _limits.MostCandidatesPerSurvey)
            {
                truncated = true;
                break;
            }

            string key = KeyOf(zdo.m_uid);
            string prefab = hash == StoneHash ? GunnarCollectionAllowlist.LooseStonePrefab : GunnarCollectionAllowlist.BranchPrefab;
            CollectableFacts facts = ReadFacts(view, zdo, prefab, out Pickable? pickable, out float unitKilograms);
            CollectableVerdict verdict = GunnarTargetPredicate.Classify(facts);
            int units = pickable != null ? pickable.m_amount : 0;
            var site = new CollectableSite(
                ownedHere: view.IsOwner(),
                insideWorkArea: area.Contains(where),
                ward: WardAt(position),
                location: TeamsterLocationSense.At(position),
                fitsCarryLimit: units > 0 && unitKilograms > 0f && units * unitKilograms <= loadKilograms);

            _found.Add(new CollectionCandidate(
                key,
                where,
                verdict,
                GunnarTargetPredicate.CheckSite(site),
                facts.YieldItemPrefabName ?? string.Empty,
                units,
                unitKilograms));
            _views[key] = view;
        }

        // The deterministic order the driver's tie-break relies on: nearest the
        // centre first, then by key.
        _found.Sort((left, right) =>
        {
            int byDistance = centre.FlatDistanceSquaredTo(left.Where).CompareTo(centre.FlatDistanceSquaredTo(right.Where));
            return byDistance != 0 ? byDistance : string.CompareOrdinal(left.Key, right.Key);
        });

        SurveyOutcome outcome = truncated ? SurveyOutcome.Truncated
            : AreaFullyLoaded(zones, area) ? SurveyOutcome.Finished
            : SurveyOutcome.NotLoaded;
        return new CollectionSurvey(outcome, new List<CollectionCandidate>(_found),
            _found.Count.ToString(CultureInfo.InvariantCulture) + " candidate(s) in " + area);
    }

    /// <summary>The live source for a key the last look minted, after its
    /// identity is proved again: still valid, same prefab, same id.</summary>
    internal bool TryResolve(string key, out Pickable pickable, out string failure)
    {
        pickable = null!;
        if (string.IsNullOrEmpty(key) || !_views.TryGetValue(key, out ZNetView? view) || view == null || !view.IsValid())
        {
            failure = "the source is gone";
            return false;
        }

        ZDO zdo = view.GetZDO();
        int hash = zdo.GetPrefab();
        if ((hash != StoneHash && hash != BranchHash) || !string.Equals(KeyOf(zdo.m_uid), key, StringComparison.Ordinal))
        {
            _views.Remove(key);
            failure = "the object under that key is no longer the source that was surveyed";
            return false;
        }

        Pickable? found = view.GetComponent<Pickable>();
        if (found == null)
        {
            failure = "the source has no pickable part";
            return false;
        }

        pickable = found;
        failure = string.Empty;
        return true;
    }

    /// <summary>Whether a planned stop is still worth walking to, read live.
    /// Gone and picked are different answers: only one of them may end a stop
    /// for good, and the driver reads them apart.</summary>
    internal StopStatus Observe(string key)
    {
        try
        {
            if (!TryResolve(key, out Pickable pickable, out _))
            {
                return StopStatus.Gone;
            }

            if (pickable.GetPicked())
            {
                return StopStatus.AlreadyDone;
            }

            ZNetView view = pickable.GetComponent<ZNetView>();
            if (view == null || !view.IsOwner())
            {
                // Another machine owns it now: refused, not gone - it is still
                // there, and still not his to pick.
                return StopStatus.Refused;
            }

            return pickable.CanBePicked() ? StopStatus.Actionable : StopStatus.AlreadyDone;
        }
        catch (Exception)
        {
            return StopStatus.Unreadable;
        }
    }

    /// <summary>The in-session id text of a network object. Valid for this
    /// world load only.</summary>
    internal static string KeyOf(ZDOID id) =>
        unchecked((ulong)id.UserID).ToString("x16", CultureInfo.InvariantCulture) + ":" +
        id.ID.ToString("x8", CultureInfo.InvariantCulture);

    private static CollectableFacts ReadFacts(
        ZNetView view, ZDO zdo, string networkPrefab, out Pickable? pickable, out float unitKilograms)
    {
        GameObject root = view.gameObject;
        Pickable[] pickables = root.GetComponentsInChildren<Pickable>(includeInactive: true);
        pickable = pickables.Length > 0 ? pickables[0] : null;

        GameObject? yieldPrefab = pickable != null ? pickable.m_itemPrefab : null;
        ItemDrop? yieldDrop = yieldPrefab != null ? yieldPrefab.GetComponent<ItemDrop>() : null;
        ItemDrop.ItemData.SharedData? shared = yieldDrop != null && yieldDrop.m_itemData != null
            ? yieldDrop.m_itemData.m_shared
            : null;
        unitKilograms = shared != null ? shared.m_weight : 0f;

        return new CollectableFacts(
            hasValidWorldObject: zdo != null,
            networkPrefabName: networkPrefab,
            takeableComponentCount: pickables.Length,
            forbiddenComponents: ForbiddenComponentsIn(root),
            bearsFood: shared != null && shared.m_food > 0f,
            yieldIsAnItem: yieldDrop != null,
            yieldItemPrefabName: yieldPrefab != null ? Utils.GetPrefabName(yieldPrefab) : null,
            yieldAmount: pickable != null ? pickable.m_amount : 0,
            extraDropsEmpty: pickable == null || pickable.m_extraDrops == null || pickable.m_extraDrops.IsEmpty(),
            respawnTimeMinutes: pickable != null ? pickable.m_respawnTimeMinutes : 0f,
            hasHideWhenTaken: pickable != null && pickable.m_hideWhenPicked != null,
            hasCreator: zdo != null && zdo.GetLong(ZDOVars.s_creator, 0L) != 0L,
            alreadyTaken: pickable != null && pickable.GetPicked(),
            visible: pickable != null && pickable.GetEnabled == 1,
            takeableNow: pickable != null && pickable.CanBePicked(),
            markedByPlayer: false);
    }

    private static IReadOnlyList<string> ForbiddenComponentsIn(GameObject root)
    {
        List<string>? found = null;
        void Check<T>(string name)
            where T : Component
        {
            if (root.GetComponentInChildren<T>(includeInactive: true) != null)
            {
                (found ??= new List<string>()).Add(name);
            }
        }

        Check<Piece>("a built piece");
        Check<WearNTear>("structural health");
        Check<Plant>("a cultivated plant");
        Check<Destructible>("a destructible");
        Check<ItemDrop>("a dropped item pile");
        Check<Container>("a container");
        Check<ItemStand>("a display stand");
        Check<Procreation>("a breeding pair");
        Check<Character>("a creature");
        Check<PickableItem>("a carried item");
        return found ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private static WardStanding WardAt(Vector3 position)
    {
        try
        {
            // flash: false - asking must not light a guard stone up, which is a
            // message to everybody near it.
            return PrivateArea.CheckAccess(position, 0f, flash: false, wardCheck: false)
                ? WardStanding.Granted
                : WardStanding.Denied;
        }
        catch (Exception)
        {
            return WardStanding.Unknown;
        }
    }

    /// <summary>Whether every zone the area touches is loaded: the centre and
    /// eight points on its edge. Coarse on purpose - a false "not loaded" makes a
    /// look inconclusive, which only ever means asking again.</summary>
    private static bool AreaFullyLoaded(ZoneSystem zones, CollectionArea area)
    {
        CollectionPoint centre = area.Centre;
        float radius = area.BoundingRadiusMetres;
        if (!zones.IsZoneLoaded(new Vector3(centre.X, centre.Y, centre.Z)))
        {
            return false;
        }

        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4d;
            var edge = new Vector3(
                centre.X + (float)(Math.Cos(angle) * radius), centre.Y, centre.Z + (float)(Math.Sin(angle) * radius));
            if (!zones.IsZoneLoaded(edge))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Whether a point belongs to one of the world's locations
/// (`DECISIONS.md` D10: they are excluded, the start temple included). Concerned
/// Foreman's <c>WorldLocationSense</c>, re-written here because products never
/// share game code (D2).
///
/// <b>Why not <c>Location.IsInsideLocation</c> alone.</b> That reads the loaded
/// location components, which the game may create a frame or more after the
/// zone's other objects; in that window a temple stone would read as outside.
/// So the loaded answer is kept only as a fast yes, and the world's own registry
/// of placed locations (<c>ZoneSystem.m_locationInstances</c>), filled at world
/// generation, answers the rest for the point's zone and its eight neighbours.
///
/// <b>Unknown refuses</b>, the same shape the ward clause has.</summary>
internal static class TeamsterLocationSense
{
    internal static LocationStanding At(Vector3 position)
    {
        try
        {
            if (Location.IsInsideLocation(position, 0f))
            {
                return LocationStanding.Inside;
            }

            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || zones.m_locationInstances == null)
            {
                return LocationStanding.Unknown;
            }

            Vector2s centre = ZoneSystem.GetZone(position);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var zone = new Vector2s(centre.x + dx, centre.y + dz);
                    if (!zones.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance))
                    {
                        continue;
                    }

                    if (instance.m_location == null)
                    {
                        return LocationStanding.Unknown;
                    }

                    if (Utils.DistanceXZ(instance.m_position, position) <= Math.Max(0f, instance.m_location.m_exteriorRadius))
                    {
                        return LocationStanding.Inside;
                    }
                }
            }

            return LocationStanding.Outside;
        }
        catch (Exception)
        {
            return LocationStanding.Unknown;
        }
    }
}
