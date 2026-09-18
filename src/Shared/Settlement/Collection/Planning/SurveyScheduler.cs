using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>One candidate the probe found: a network object whose prefab hash
/// is one of the two allowlisted ones, inside or near the scope, with the facts
/// read from it. The scheduler, not the probe, decides what it is.</summary>
internal readonly struct SurveyCandidate
{
    public SurveyCandidate(
        SourceKey key,
        NaturalSourceFacts facts,
        bool ownedHere,
        bool inInterior,
        LocationStanding location,
        AreaAccess ward,
        int estimatedYield)
    {
        Key = key;
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        OwnedHere = ownedHere;
        InInterior = inInterior;
        Location = location;
        Ward = ward;
        EstimatedYield = estimatedYield;
    }

    public SourceKey Key { get; }

    public NaturalSourceFacts Facts { get; }

    public bool OwnedHere { get; }

    public bool InInterior { get; }

    public LocationStanding Location { get; }

    public AreaAccess Ward { get; }

    /// <summary>What the game's own drop scaling says one pick gives, or 0 when
    /// it could not be read.</summary>
    public int EstimatedYield { get; }
}

/// <summary>How far one discovery step got.</summary>
internal readonly struct DiscoveryStep
{
    public DiscoveryStep(int entriesExamined, bool finished, bool sceneMoved = false)
    {
        EntriesExamined = entriesExamined;
        Finished = finished;
        SceneMoved = sceneMoved;
    }

    public int EntriesExamined { get; }

    /// <summary>Every object of the pass has been examined.</summary>
    public bool Finished { get; }

    /// <summary>The world gained or lost objects while the pass ran, so the
    /// pass did not see everything that is there. The snapshot says so rather
    /// than claiming the ground is empty (GATHER-03).</summary>
    public bool SceneMoved { get; }
}

/// <summary>The game side of a survey (the Foreman adapter implements it over
/// the loaded scene). Every method answers; none throws for game reasons.
/// </summary>
internal interface ISurveyProbe
{
    /// <summary>True only when the ground at the point is loaded. Unknown is
    /// false.</summary>
    bool IsLoaded(SitePoint point);

    /// <summary>Starts a discovery pass over what is loaded now.</summary>
    void Begin(WorkScope scope);

    /// <summary>Examines at most <paramref name="maxEntries"/> loaded objects and
    /// appends at most <paramref name="maxCandidates"/> candidates.</summary>
    DiscoveryStep Discover(int maxEntries, int maxCandidates, List<SurveyCandidate> into);
}

/// <summary>What one survey saw, per resource, kept apart so "nothing found"
/// is never said about ground that was not looked at (GATHER-03).</summary>
internal sealed class SurveyAccounting
{
    private readonly int[] _available = new int[3];
    private readonly int[] _exhausted = new int[3];
    private readonly int[] _inaccessible = new int[3];
    private readonly int[] _unknown = new int[3];

    public int TotalCells { get; internal set; }

    public int LoadedCells { get; internal set; }

    public int UnloadedCells { get; internal set; }

    /// <summary>Objects with an allowlisted prefab that failed shape, tag,
    /// configuration, yield or provenance: a look-alike or a modded object.
    /// </summary>
    public int RejectedNotNatural { get; internal set; }

    public int OutsideScope { get; internal set; }

    public int EntriesExamined { get; internal set; }

    public bool TruncatedByBudget { get; internal set; }

    public int Available(CollectedResource resource) => _available[Index(resource)];

    public int Exhausted(CollectedResource resource) => _exhausted[Index(resource)];

    public int Inaccessible(CollectedResource resource) => _inaccessible[Index(resource)];

    public int Unknown(CollectedResource resource) => _unknown[Index(resource)];

    internal void Count(CollectedResource resource, SourceAvailability availability)
    {
        int index = Index(resource);
        switch (availability)
        {
            case SourceAvailability.Available:
                _available[index]++;
                break;
            case SourceAvailability.Exhausted:
                _exhausted[index]++;
                break;
            case SourceAvailability.Inaccessible:
                _inaccessible[index]++;
                break;
            default:
                _unknown[index]++;
                break;
        }
    }

    private static int Index(CollectedResource resource)
    {
        int index = (int)resource;
        if (index < 1 || index > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(resource), "Not a collectable resource.");
        }

        return index;
    }
}

/// <summary>A bounded survey of one scope (GATHER-03), advanced a budgeted
/// amount per worker tick.
///
/// <b>Three phases, each with its own budget.</b> First the scope is cut into
/// cells and each cell is asked whether its ground is loaded, at its centre and
/// four corners, so a zone edge through a cell counts it unloaded rather than
/// half-seen. Then the probe discovers candidates among the loaded objects, a
/// bounded number of entries and candidates per tick. Each candidate is
/// classified here by <see cref="NaturalSourcePredicate"/>, so the allowlist
/// decision is the tested one and not a copy in the adapter.
///
/// <b>What it never claims.</b> A survey that ran out of time or hit its source
/// cap is <c>TruncatedByBudget</c>; unloaded cells are counted. Exhausted,
/// inaccessible and unknown sources are recorded as such and never merged into
/// "none".</summary>
internal sealed class SurveyScheduler
{
    private readonly CollectionParameters _parameters;
    private readonly ISurveyProbe _probe;
    private readonly List<SitePoint> _cells;
    private readonly List<SourceObservation> _observations = new List<SourceObservation>();
    private readonly HashSet<SourceKey> _seen = new HashSet<SourceKey>();
    private readonly List<SurveyCandidate> _buffer = new List<SurveyCandidate>();
    private readonly SurveyProvenance _provenance;
    private readonly float _startedAt;

    private int _cellCursor;
    private bool _discoveryBegun;

    public SurveyScheduler(
        CollectionParameters parameters, WorkScope scope, ISurveyProbe probe, int revision,
        SurveyProvenance provenance, float startedAt)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (provenance == SurveyProvenance.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(provenance), "A survey says who surveyed.");
        }

        Revision = revision;
        _provenance = provenance;
        _startedAt = startedAt;
        _cells = CellsFor(scope, parameters.SurveyCellSizeMetres);
        Accounting.TotalCells = _cells.Count;
    }

    public WorkScope Scope { get; }

    public int Revision { get; }

    public SurveyAccounting Accounting { get; } = new SurveyAccounting();

    public bool IsComplete => Snapshot != null;

    public SurveySnapshot? Snapshot { get; private set; }

    public int TicksUsed { get; private set; }

    /// <summary>Advances the survey by one tick's budget.</summary>
    public void Step(float now)
    {
        if (IsComplete)
        {
            return;
        }

        TicksUsed++;

        if (now - _startedAt >= _parameters.SurveyDeadlineSeconds)
        {
            Accounting.TruncatedByBudget = true;
            Complete(now);
            return;
        }

        if (_cellCursor < _cells.Count)
        {
            int budget = _parameters.SurveyCellsPerTick;
            while (budget-- > 0 && _cellCursor < _cells.Count)
            {
                if (IsCellLoaded(_cells[_cellCursor]))
                {
                    Accounting.LoadedCells++;
                }
                else
                {
                    Accounting.UnloadedCells++;
                }

                _cellCursor++;
            }

            if (_cellCursor < _cells.Count)
            {
                return;
            }
        }

        if (!_discoveryBegun)
        {
            _discoveryBegun = true;
            if (Accounting.LoadedCells == 0)
            {
                // Nothing of the scope is loaded, so there is nothing to look
                // at: the snapshot says so through its unloaded cells.
                Complete(now);
                return;
            }

            _probe.Begin(Scope);
        }

        _buffer.Clear();
        DiscoveryStep step = _probe.Discover(
            _parameters.SurveyEntriesPerTick, _parameters.SurveyCandidatesPerTick, _buffer);
        Accounting.EntriesExamined += Math.Max(0, step.EntriesExamined);
        if (step.SceneMoved)
        {
            // Objects appeared or went away while this pass ran: what it did
            // not examine is unknown, never "nothing".
            Accounting.TruncatedByBudget = true;
        }

        int processed = 0;
        foreach (SurveyCandidate candidate in _buffer)
        {
            if (processed++ >= _parameters.SurveyCandidatesPerTick)
            {
                // A probe that returns more than it was allowed has broken the
                // budget; what it over-delivered is not trusted as complete.
                Accounting.TruncatedByBudget = true;
                break;
            }

            if (!Record(candidate, now))
            {
                Accounting.TruncatedByBudget = true;
                Complete(now);
                return;
            }
        }

        if (step.Finished)
        {
            Complete(now);
        }
    }

    /// <summary>Returns false when the source cap is reached.</summary>
    private bool Record(SurveyCandidate candidate, float now)
    {
        if (candidate.Key.IsEmpty || candidate.Key.WorldLoadEpoch != Scope.WorldLoadEpoch)
        {
            // A key from another world load is never trusted.
            Accounting.RejectedNotNatural++;
            return true;
        }

        var site = new SourceSiteFacts(
            ownedHere: candidate.OwnedHere,
            inScope: Scope.Contains(candidate.Key.Position),
            inInterior: candidate.InInterior,
            location: candidate.Location,
            ward: candidate.Ward,
            // Reach, capacity and the local player are pick-time questions.
            // A survey never answers them, so it never pretends to: the
            // availability rule below reads none of these three.
            workerDistanceMetres: float.NaN,
            capacityFits: false,
            localPlayerPresent: false);

        if (!site.InScope)
        {
            Accounting.OutsideScope++;
            return true;
        }

        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(candidate.Facts);
        SourceAvailability? availability = NaturalSourcePredicate.SurveyAvailability(verdict, site);
        if (availability == null)
        {
            Accounting.RejectedNotNatural++;
            return true;
        }

        if (!_seen.Add(candidate.Key))
        {
            return true;
        }

        if (_observations.Count >= _parameters.SurveyMaxSources)
        {
            return false;
        }

        SourceAvailability recorded = availability.Value;
        if (recorded == SourceAvailability.Available && candidate.EstimatedYield < 1)
        {
            // Without the game's own yield there is no honest capacity plan.
            recorded = SourceAvailability.Unknown;
        }

        _observations.Add(new SourceObservation(
            candidate.Key, verdict.Kind, verdict.Yields, candidate.EstimatedYield, recorded,
            SourceReachability.NotChecked, Revision, now));
        Accounting.Count(verdict.Yields, recorded);
        return true;
    }

    private void Complete(float now)
    {
        // Stable order, so two surveys of the same world read the same way.
        _observations.Sort((left, right) =>
        {
            int bySession = string.CompareOrdinal(left.Key.SessionId, right.Key.SessionId);
            return bySession != 0 ? bySession : string.CompareOrdinal(left.Key.PrefabName, right.Key.PrefabName);
        });

        Snapshot = new SurveySnapshot(
            Scope, _observations.ToArray(), _provenance, Revision, Accounting.TruncatedByBudget,
            Accounting.UnloadedCells, now);
    }

    private bool IsCellLoaded(SitePoint centre)
    {
        float half = _parameters.SurveyCellSizeMetres / 2f;
        return _probe.IsLoaded(centre)
            && _probe.IsLoaded(new SitePoint(centre.X - half, centre.Y, centre.Z - half))
            && _probe.IsLoaded(new SitePoint(centre.X + half, centre.Y, centre.Z - half))
            && _probe.IsLoaded(new SitePoint(centre.X - half, centre.Y, centre.Z + half))
            && _probe.IsLoaded(new SitePoint(centre.X + half, centre.Y, centre.Z + half));
    }

    /// <summary>The centres of the grid cells that overlap the scope circle, in
    /// row order. Deterministic, and bounded by the scope's own radius limit.
    /// </summary>
    internal static List<SitePoint> CellsFor(WorkScope scope, float cellSize)
    {
        var cells = new List<SitePoint>();
        float radius = scope.RadiusMetres;
        SitePoint centre = scope.Centre;
        int perSide = (int)Math.Ceiling((2f * radius) / cellSize);
        float originX = centre.X - radius;
        float originZ = centre.Z - radius;

        for (int row = 0; row < perSide; row++)
        {
            float minZ = originZ + (row * cellSize);
            float maxZ = minZ + cellSize;
            for (int column = 0; column < perSide; column++)
            {
                float minX = originX + (column * cellSize);
                float maxX = minX + cellSize;
                float nearestX = Math.Max(minX, Math.Min(centre.X, maxX));
                float nearestZ = Math.Max(minZ, Math.Min(centre.Z, maxZ));
                float dx = nearestX - centre.X;
                float dz = nearestZ - centre.Z;
                if ((dx * dx) + (dz * dz) <= radius * radius)
                {
                    cells.Add(new SitePoint(minX + (cellSize / 2f), centre.Y, minZ + (cellSize / 2f)));
                }
            }
        }

        return cells;
    }
}
