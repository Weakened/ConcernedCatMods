using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers.Felling;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>The game side of the bounded solo survey: which loaded network
/// objects are candidate stones and branches, with their facts.
///
/// <b>Why the scene's instance table and not a physics query.</b> A physics
/// query only finds live colliders, and a picked branch deactivates its
/// colliders with its model: a physics survey could never report a branch as
/// exhausted, only as missing, which is exactly the merge GATHER-03 forbids.
/// The scene's own table (<c>ZNetScene.m_instances</c>) holds every
/// instantiated network object in loaded ground, picked or not, and a prefab
/// hash comparison per entry is the cheapest identity test there is (C1).
///
/// <b>Bounded.</b> <see cref="Begin"/> copies the table's values once (so a
/// scene change between ticks cannot break an enumerator); each
/// <see cref="Discover"/> examines at most its entry budget and describes at
/// most its candidate budget. Ward answers are cached per 8 m cell for the
/// pass: a cell with no foreign ward anywhere near answers for every point in
/// it, anything else is asked per point.</summary>
internal sealed class WorldSourceSurvey : ISurveyProbe
{
    private const float WardCellSize = 8f;

    private readonly SourceDirectory _directory;
    private readonly IDesignationSite _site;
    private readonly WorldFellingPort? _felling;
    private readonly List<ZNetView> _entries = new List<ZNetView>();
    private readonly Dictionary<(long X, long Z), AreaAccess> _wardByCell = new Dictionary<(long, long), AreaAccess>();
    private readonly Dictionary<(long X, long Z), float> _piecesByCell = new Dictionary<(long, long), float>();

    private WorkScope? _scope;
    private int _cursor;
    private int _sceneCountAtBegin;

    /// <param name="felling">D19 (#282): when given, and its switch is on, and
    /// the scope is an explicit work area (never the default camp circle), the
    /// four small trees are surveyed too. Null surveys stones and branches only.
    /// </param>
    public WorldSourceSurvey(SourceDirectory directory, IDesignationSite site, WorldFellingPort? felling = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _felling = felling;
    }

    public bool IsLoaded(SitePoint point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        return zones != null && zones.IsZoneLoaded(NaturalSourceClassifier.ToVector3(point));
    }

    public void Begin(WorkScope scope)
    {
        _scope = scope;
        _cursor = 0;
        _entries.Clear();
        _wardByCell.Clear();
        _piecesByCell.Clear();
        _directory.Reset(scope.WorldLoadEpoch);

        ZNetScene scene = ZNetScene.instance;
        if (scene != null)
        {
            _entries.AddRange(scene.m_instances.Values);
        }

        _sceneCountAtBegin = _entries.Count;
    }

    public DiscoveryStep Discover(int maxEntries, int maxCandidates, List<SurveyCandidate> into)
    {
        WorkScope? scope = _scope;
        if (scope == null)
        {
            return new DiscoveryStep(0, true);
        }

        int examined = 0;
        int found = 0;
        float reach = scope.RadiusMetres + 1f;
        bool trees = _felling != null && _felling.Enabled && scope.Source != WorkScopeSource.DefaultCampCircle;
        while (_cursor < _entries.Count && examined < maxEntries && found < maxCandidates)
        {
            ZNetView view = _entries[_cursor++];
            examined++;

            // Destroyed since the copy, or not a network object any more.
            if (view == null || !view.IsValid())
            {
                continue;
            }

            int hash = view.GetZDO().GetPrefab();
            bool tree = trees && WorldFellingPort.IsSmallTreeHash(hash);
            if (!tree && !NaturalSourceClassifier.IsAllowlistedHash(hash))
            {
                continue;
            }

            Vector3 position = view.transform.position;
            SitePoint point = NaturalSourceClassifier.ToSitePoint(position);
            if (scope.Centre.HorizontalDistanceTo(point) > reach)
            {
                continue;
            }

            // #296 slice 3: a Cartographer area's circle only bounds the walk;
            // a source outside the drawn boundary is not in the area.
            if (scope.OutsideBoundary(point))
            {
                continue;
            }

            if (tree)
            {
                SmallTreeFacts treeFacts = WorldFellingPort.ReadFacts(view, out _, out SmallTreeKind? kind);
                if (kind == null)
                {
                    continue;
                }

                var treeKey = new SourceKey(
                    kind.Prefab, NaturalSourceClassifier.FormatId(view.GetZDO().m_uid), scope.WorldLoadEpoch, point);
                into.Add(new SurveyCandidate(
                    treeKey,
                    treeFacts,
                    ownedHere: view.IsOwner(),
                    inInterior: Character.InInterior(position),
                    location: WorldLocationSense.At(position),
                    ward: WardAt(position),
                    builtPieceClearanceMetres: BuiltPieceClearance(position),
                    estimatedYield: SmallTreeSources.MaxWood(kind)));
                _directory.Remember(treeKey, view);
                found++;
                continue;
            }

            if (!NaturalSourceClassifier.TryDescribe(
                    view, scope.WorldLoadEpoch, out SourceKey key, out NaturalSourceFacts facts, out int yield))
            {
                continue;
            }

            into.Add(new SurveyCandidate(
                key,
                facts,
                ownedHere: view.IsOwner(),
                inInterior: Character.InInterior(position),
                location: WorldLocationSense.At(position),
                ward: WardAt(position),
                estimatedYield: yield));
            _directory.Remember(key, view);
            found++;
        }

        // The copy this pass walks is one moment of the scene. Zones fill in
        // over many frames (ZNetScene creates a budgeted handful of objects per
        // frame), so a pass that ends while the scene is still changing has not
        // seen everything there is: say so, rather than let "nothing found"
        // stand for ground that was never examined (GATHER-03).
        ZNetScene current = ZNetScene.instance;
        bool sceneMoved = current == null || current.m_instances.Count != _sceneCountAtBegin;
        return new DiscoveryStep(examined, _cursor >= _entries.Count, sceneMoved);
    }

    /// <summary>The flat distance to the nearest player-built piece, asked once
    /// per 8 m cell where no piece is near. A tree is at most half a cell's
    /// diagonal from its cell's middle, so a middle whose nearest piece is that
    /// much further than the clearance proves the clearance for every tree in
    /// the cell, and the lower bound is what is reported. Anywhere nearer, the
    /// tree itself is measured. The strike measures again before every hit.
    /// </summary>
    private float BuiltPieceClearance(Vector3 position)
    {
        const float halfDiagonal = WardCellSize * 0.7072f;
        long cellX = (long)Math.Floor(position.x / WardCellSize);
        long cellZ = (long)Math.Floor(position.z / WardCellSize);
        (long X, long Z) cell = (cellX, cellZ);
        if (!_piecesByCell.TryGetValue(cell, out float fromMiddle))
        {
            var middle = new Vector3((cellX + 0.5f) * WardCellSize, position.y, (cellZ + 0.5f) * WardCellSize);
            fromMiddle = _felling!.NearestPieceMetres(middle);
            _piecesByCell[cell] = fromMiddle;
        }

        return fromMiddle - halfDiagonal >= TheConcernedCat.Workers.Felling.SmallTreeFelling.ClearanceMetres
            ? fromMiddle - halfDiagonal
            : _felling!.NearestPieceMetres(position);
    }

    private AreaAccess WardAt(Vector3 position)
    {
        long cellX = (long)Math.Floor(position.x / WardCellSize);
        long cellZ = (long)Math.Floor(position.z / WardCellSize);

        // Keyed by the cell itself: a hash of it could collide, and a collision
        // would carry one cell's "no ward here" to another cell a ward denies.
        (long X, long Z) cell = (cellX, cellZ);
        if (!_wardByCell.TryGetValue(cell, out AreaAccess cellAccess))
        {
            var centre = new SitePoint(
                (cellX + 0.5f) * WardCellSize, position.y, (cellZ + 0.5f) * WardCellSize);
            cellAccess = _site.CheckAccess(centre, WardCellSize * 0.7072f);
            _wardByCell[cell] = cellAccess;
        }

        // No foreign ward reaches the cell at all: every point in it is free.
        // Otherwise the point itself is asked, because a ward covering part of
        // a cell says nothing about the rest of it.
        return cellAccess == AreaAccess.Granted
            ? AreaAccess.Granted
            : _site.CheckAccess(NaturalSourceClassifier.ToSitePoint(position), 0f);
    }
}
