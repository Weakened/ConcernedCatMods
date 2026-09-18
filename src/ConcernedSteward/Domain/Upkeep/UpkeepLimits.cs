using System;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>Every bound the upkeep loop runs inside.
///
/// They are gathered in one type so that "bounded" is something a test can read
/// off a value rather than something a comment claims about scattered
/// constants. Each one exists because the unbounded version of it is a real
/// failure a player would meet: an unbounded scan is a frame-rate problem, an
/// unbounded trip is a worker who empties your chest into one fire, and an
/// unbounded retry is a worker who never reports that he is stuck.</summary>
internal readonly struct UpkeepLimits
{
    public UpkeepLimits(
        int maxTargetsScanned,
        int maxUnitsPerTrip,
        int maxConcurrentJobs,
        float scanIntervalSeconds,
        float phaseLimitSeconds,
        int maxFailuresPerPhase)
    {
        if (maxTargetsScanned < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTargetsScanned), maxTargetsScanned,
                "A scan that may look at no fires can never find one.");
        }

        if (maxUnitsPerTrip < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxUnitsPerTrip), maxUnitsPerTrip,
                "A trip that may carry nothing is not a trip.");
        }

        if (maxConcurrentJobs != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentJobs), maxConcurrentJobs,
                "One body can be in one place, so this slice runs exactly one job. " +
                "A second job would need a second body, and there is one Steward.");
        }

        if (!(scanIntervalSeconds > 0f) || float.IsInfinity(scanIntervalSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanIntervalSeconds), scanIntervalSeconds,
                "A scan interval must be a positive, finite number of seconds.");
        }

        if (!(phaseLimitSeconds > 0f) || float.IsInfinity(phaseLimitSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(phaseLimitSeconds), phaseLimitSeconds,
                "A phase limit must be a positive, finite number of seconds.");
        }

        if (maxFailuresPerPhase < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFailuresPerPhase), maxFailuresPerPhase,
                "A phase must be allowed to fail at least once before it gives up.");
        }

        MaxTargetsScanned = maxTargetsScanned;
        MaxUnitsPerTrip = maxUnitsPerTrip;
        MaxConcurrentJobs = maxConcurrentJobs;
        ScanIntervalSeconds = scanIntervalSeconds;
        PhaseLimitSeconds = phaseLimitSeconds;
        MaxFailuresPerPhase = maxFailuresPerPhase;
    }

    /// <summary>The most fires one scan may examine.
    ///
    /// The adapter hands over what it found inside the settlement area; this
    /// caps how much of that the domain will consider, so a player who has
    /// built two hundred torches inside their walls costs the same per scan as
    /// one who has built eight. The cap applies <i>after</i> a deterministic
    /// sort, so the fires it keeps are the ones that most need tending, not
    /// whichever the engine happened to enumerate first.</summary>
    public int MaxTargetsScanned { get; }

    /// <summary>The most units of fuel one trip may withdraw.
    ///
    /// This is the difference between a Steward and a bug report. Without it, a
    /// bonfire with a large capacity beside a full chest is a single job that
    /// carries the whole chest — correct by conservation and appalling to
    /// watch, and it leaves nothing for anything else.</summary>
    public int MaxUnitsPerTrip { get; }

    /// <summary>Always one. Present as a value so a test can pin it rather than
    /// so a caller can raise it.</summary>
    public int MaxConcurrentJobs { get; }

    /// <summary>How long to wait between scans while idle. A fire loses one
    /// unit every <c>m_secPerFuel</c> seconds — three, for a hearth — so a fire
    /// cannot go from comfortable to out within one interval, and scanning
    /// faster would only spend frames confirming that nothing changed.</summary>
    public float ScanIntervalSeconds { get; }

    /// <summary>How long any one phase may take before the job gives up and
    /// says so. Walking is the long one; a path that has not produced arrival
    /// in this long is not going to.</summary>
    public float PhaseLimitSeconds { get; }

    /// <summary>How many times one phase may fail before the job stops trying.
    /// Paired with <c>BoundedRetry</c>'s doubling backoff, so a failing phase
    /// neither hammers the game nor retries forever.</summary>
    public int MaxFailuresPerPhase { get; }

    /// <summary>The shipped values.
    ///
    /// Thirty-two fires is a generous village and a cheap sort. Ten units is
    /// one vanilla hearth's entire capacity, so a single trip can always finish
    /// a hearth and can never strip a chest. Fifteen seconds between scans is
    /// five times faster than a hearth burns one unit. Ninety seconds is far
    /// longer than any walk inside a settlement and short enough that a stuck
    /// Steward is noticed in the same play session.</summary>
    public static UpkeepLimits Default => new(
        maxTargetsScanned: 32,
        maxUnitsPerTrip: 10,
        maxConcurrentJobs: 1,
        scanIntervalSeconds: 15f,
        phaseLimitSeconds: 90f,
        maxFailuresPerPhase: 3);
}
