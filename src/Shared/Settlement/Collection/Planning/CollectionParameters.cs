using System;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>Every number the collection loop plans inside, in one place, with
/// the reason for each (GATHER-04..06, CONTRACTS.md §6 and §7).
///
/// None of these is a vanilla value presented as one. Where a number comes
/// from the installed game it says so and names the evidence; where it is this
/// slice's own choice it says that instead, so nobody later mistakes a planning
/// budget for a property of Valheim.
///
/// Immutable, so a loop cannot have a limit changed under it mid-order; the
/// one player-facing setting (<see cref="WorkerCarryWeight"/>) is read when a
/// loop is built.</summary>
internal sealed class CollectionParameters
{
    /// <summary><c>Collection/WorkerCarryWeight</c>: 100, i.e. 50 Stone or 50
    /// Wood at their audited 2.0 each (CONTRACTS.md §5.6). This slice's own NPC
    /// budget, deliberately a third of the player's 300: vanilla gives a
    /// non-player humanoid no carry limit at all, so any number is ours.</summary>
    public const float DefaultWorkerCarryWeight = 100f;

    public const float MinWorkerCarryWeight = 10f;

    public const float MaxWorkerCarryWeight = 300f;

    public CollectionParameters(
        float workerCarryWeight = DefaultWorkerCarryWeight,
        float pickupReachMetres = 2f,
        float sourceArrivalToleranceMetres = 1.5f,
        float deliveryArrivalToleranceMetres = 2.5f,
        float surveyCellSizeMetres = 8f,
        int surveyCellsPerTick = 32,
        int surveyEntriesPerTick = 4096,
        int surveyCandidatesPerTick = 16,
        int surveyMaxSources = 256,
        float surveyDeadlineSeconds = 20f,
        float snapshotMaxAgeSeconds = 120f,
        int maxFruitlessSurveys = 2,
        float routeDetourFactor = 1.3f,
        float climbCostFactor = 2f,
        float pickupEffortMetres = 2f,
        float returnTripWeight = 0.5f,
        int walkMaxFailures = 3,
        float walkFirstRetrySeconds = 2f,
        float walkMaxRetrySeconds = 20f,
        float walkLegDeadlineSeconds = 90f,
        int transferMaxFailures = 3,
        float transferFirstRetrySeconds = 2f,
        float transferMaxRetrySeconds = 20f,
        float readinessRecheckSeconds = 60f,
        float absentWorkerGraceSeconds = 3f)
    {
        if (!(workerCarryWeight >= MinWorkerCarryWeight) || workerCarryWeight > MaxWorkerCarryWeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerCarryWeight),
                "The worker carry weight is " + MinWorkerCarryWeight + "-" + MaxWorkerCarryWeight + ".");
        }

        if (!(pickupReachMetres > 0f) || !(sourceArrivalToleranceMetres > 0f) ||
            sourceArrivalToleranceMetres > pickupReachMetres)
        {
            // Arriving must imply being within reach, or a worker that "arrived"
            // could still be refused the pick every time and walk in circles.
            throw new ArgumentOutOfRangeException(
                nameof(sourceArrivalToleranceMetres), "Arrival at a source must be within pickup reach.");
        }

        if (!(deliveryArrivalToleranceMetres > 0f) || !(surveyCellSizeMetres > 0f) ||
            !(surveyDeadlineSeconds > 0f) || !(snapshotMaxAgeSeconds > 0f) || !(walkLegDeadlineSeconds > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryArrivalToleranceMetres), "Distances and times must be positive.");
        }

        if (surveyCellsPerTick < 1 || surveyEntriesPerTick < 1 || surveyCandidatesPerTick < 1 ||
            surveyMaxSources < 1 || maxFruitlessSurveys < 1 || walkMaxFailures < 1 || transferMaxFailures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(surveyCellsPerTick), "Budgets and ceilings must be at least one.");
        }

        if (routeDetourFactor < 1f || climbCostFactor < 0f || pickupEffortMetres < 0f || returnTripWeight < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(routeDetourFactor), "Cost weights must be non-negative, detour at least 1.");
        }

        WorkerCarryWeight = workerCarryWeight;
        PickupReachMetres = pickupReachMetres;
        SourceArrivalToleranceMetres = sourceArrivalToleranceMetres;
        DeliveryArrivalToleranceMetres = deliveryArrivalToleranceMetres;
        SurveyCellSizeMetres = surveyCellSizeMetres;
        SurveyCellsPerTick = surveyCellsPerTick;
        SurveyEntriesPerTick = surveyEntriesPerTick;
        SurveyCandidatesPerTick = surveyCandidatesPerTick;
        SurveyMaxSources = surveyMaxSources;
        SurveyDeadlineSeconds = surveyDeadlineSeconds;
        SnapshotMaxAgeSeconds = snapshotMaxAgeSeconds;
        MaxFruitlessSurveys = maxFruitlessSurveys;
        RouteDetourFactor = routeDetourFactor;
        ClimbCostFactor = climbCostFactor;
        PickupEffortMetres = pickupEffortMetres;
        ReturnTripWeight = returnTripWeight;
        WalkMaxFailures = walkMaxFailures;
        WalkFirstRetrySeconds = walkFirstRetrySeconds;
        WalkMaxRetrySeconds = walkMaxRetrySeconds;
        WalkLegDeadlineSeconds = walkLegDeadlineSeconds;
        TransferMaxFailures = transferMaxFailures;
        TransferFirstRetrySeconds = transferFirstRetrySeconds;
        TransferMaxRetrySeconds = transferMaxRetrySeconds;
        ReadinessRecheckSeconds = readinessRecheckSeconds;
        AbsentWorkerGraceSeconds = absentWorkerGraceSeconds;
    }

    public static CollectionParameters Default { get; } = new CollectionParameters();

    /// <summary>The NPC weight budget for carried Stone and Wood. Tools the
    /// worker holds are not counted against it: the owner's figure is "50 Stone
    /// or 50 Wood", which is materials only.</summary>
    public float WorkerCarryWeight { get; }

    /// <summary>≤ 2 m flat between the worker and the source at the instant of
    /// the pick (CONTRACTS.md §6 site clauses). The player's own interaction
    /// reach is 5 m (<c>Player.m_maxInteractDistance</c>); the worker is held
    /// to less, so what he picks is visibly what he is standing at.</summary>
    public float PickupReachMetres { get; }

    /// <summary>How close the walk to a source counts as arrived: inside
    /// <see cref="PickupReachMetres"/>, so arrival always permits the pick.
    /// </summary>
    public float SourceArrivalToleranceMetres { get; }

    /// <summary>How close the walk to the destination counts as arrived. A
    /// chest is a solid piece whose pivot the body cannot stand on, so this is
    /// wider than the source tolerance. Our number; the deposit port may apply
    /// its own reach check at the moment of the transfer.</summary>
    public float DeliveryArrivalToleranceMetres { get; }

    /// <summary>The survey's load-accounting grid. Each cell is sampled at its
    /// centre and four corners, so a zone boundary through a cell marks it
    /// unloaded rather than silently half-observed.</summary>
    public float SurveyCellSizeMetres { get; }

    /// <summary>Cells whose loaded state is checked per worker tick (0.05 s).
    /// </summary>
    public int SurveyCellsPerTick { get; }

    /// <summary>Loaded network objects examined per tick while discovering
    /// candidates. Each examination is a prefab-hash comparison.</summary>
    public int SurveyEntriesPerTick { get; }

    /// <summary>Candidates classified per tick. Classification reads the
    /// object's components and asks the ward question, which is the expensive
    /// part.</summary>
    public int SurveyCandidatesPerTick { get; }

    /// <summary>At most this many sources per survey; beyond it the snapshot is
    /// marked <c>TruncatedByBudget</c> rather than claiming it saw everything.
    /// </summary>
    public int SurveyMaxSources { get; }

    /// <summary>A survey that has not finished by then ends truncated.</summary>
    public float SurveyDeadlineSeconds { get; }

    /// <summary>A snapshot older than this is surveyed again before the next
    /// reservation: sources respawn, and the player picks things up.</summary>
    public float SnapshotMaxAgeSeconds { get; }

    /// <summary>Consecutive surveys that find nothing collectable before the
    /// order pauses instead of surveying again.</summary>
    public int MaxFruitlessSurveys { get; }

    /// <summary>Route cost estimate: horizontal distance times this factor.
    /// An estimate, not a path query: no pathfinding is spent on choosing.
    /// </summary>
    public float RouteDetourFactor { get; }

    /// <summary>Route cost per metre of height difference.</summary>
    public float ClimbCostFactor { get; }

    /// <summary>The pick's own effort, in metres of walking.</summary>
    public float PickupEffortMetres { get; }

    /// <summary>How much the extra distance a source adds to the walk back
    /// weighs against the walk to it.</summary>
    public float ReturnTripWeight { get; }

    /// <summary>CONTRACTS.md §7, walk to source: 3 failures, 2 s doubling to
    /// 20 s.</summary>
    public int WalkMaxFailures { get; }

    public float WalkFirstRetrySeconds { get; }

    public float WalkMaxRetrySeconds { get; }

    /// <summary>A single walk leg that has not arrived by then counts as a
    /// failure. Ninety seconds covers the 64 m planning horizon at a slow
    /// walk with detours.</summary>
    public float WalkLegDeadlineSeconds { get; }

    /// <summary>CONTRACTS.md §7, transfers: 3 attempts on Refused with backoff;
    /// Uncertain is never retried.</summary>
    public int TransferMaxFailures { get; }

    public float TransferFirstRetrySeconds { get; }

    public float TransferMaxRetrySeconds { get; }

    /// <summary>Readiness (recruited, usable axe and hammer) is asked at every
    /// trip start and at least this often while working.</summary>
    public float ReadinessRecheckSeconds { get; }

    /// <summary>When the worker's own tick has not run for this long, his body
    /// is not being simulated here and the order pauses.</summary>
    public float AbsentWorkerGraceSeconds { get; }
}
