using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Where something is. Horizontal distance is what every area, reach
/// and arrival question in this repository already means by distance, and this
/// type keeps the height only so a stop can be walked to.</summary>
internal readonly struct CollectionPoint : IEquatable<CollectionPoint>
{
    public CollectionPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    /// <summary>Horizontal distance, squared - the comparison form, so ordering
    /// never pays for a square root it does not need.</summary>
    public float FlatDistanceSquaredTo(CollectionPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return (dx * dx) + (dz * dz);
    }

    public float FlatDistanceTo(CollectionPoint other) => (float)Math.Sqrt(FlatDistanceSquaredTo(other));

    public bool Equals(CollectionPoint other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is CollectionPoint other && Equals(other);

    public override int GetHashCode() => (X, Y, Z).GetHashCode();

    public override string ToString() => X.ToString("0.#") + ", " + Y.ToString("0.#") + ", " + Z.ToString("0.#");
}

/// <summary>One thing the survey found, already judged.</summary>
internal sealed class CollectionCandidate
{
    public CollectionCandidate(
        string key,
        CollectionPoint where,
        CollectableVerdict verdict,
        CollectionSiteClause site,
        string itemPrefab,
        int units,
        float unitKilograms)
    {
        Key = string.IsNullOrEmpty(key) ? throw new ArgumentException("A candidate needs a key.", nameof(key)) : key;
        Where = where;
        Verdict = verdict;
        Site = site;
        ItemPrefab = itemPrefab ?? string.Empty;
        Units = units > 0 ? units : 0;
        UnitKilograms = unitKilograms > 0f && !float.IsNaN(unitKilograms) ? unitKilograms : 0f;
    }

    /// <summary>Stable for one world load, and the tie-break that makes every
    /// ordering deterministic. Never persisted: an in-session id names an object
    /// in this scene and nothing else.</summary>
    public string Key { get; }

    public CollectionPoint Where { get; }

    public CollectableVerdict Verdict { get; }

    public CollectionSiteClause Site { get; }

    /// <summary>What taking it would give.</summary>
    public string ItemPrefab { get; }

    /// <summary>How many units taking it would give.</summary>
    public int Units { get; }

    /// <summary>What one unit weighs, read from the game's own item data.
    /// </summary>
    public float UnitKilograms { get; }

    /// <summary>What the whole candidate weighs.</summary>
    public float Kilograms => Units * UnitKilograms;

    /// <summary>Eligible in itself and the place allows it. Both halves, because
    /// a perfectly good stone inside somebody's ward is not a target.</summary>
    public bool IsTakeable =>
        Verdict.IsEligible && Site == CollectionSiteClause.Unspecified && Units > 0 && UnitKilograms > 0f;

    /// <summary>The right kind of thing with nothing left of it. Counted
    /// separately so "this area is picked clean" and "there was never anything
    /// here" stay different answers.</summary>
    public bool IsExhausted => Verdict.FailedClause == CollectionClause.State;

    public override string ToString() => Key + " " + Verdict + " @ " + Where;
}

/// <summary>How the looking went. The distinction that stops a half-finished
/// look being reported as an empty area.</summary>
internal enum SurveyOutcome
{
    /// <summary>Nobody looked.</summary>
    Unspecified = 0,

    /// <summary>The whole area was examined.</summary>
    Finished = 1,

    /// <summary>The candidate ceiling was reached. Real candidates, and more
    /// behind them: worked in rounds, never refused.</summary>
    Truncated = 2,

    /// <summary>Part of the area is not loaded, so what is there could not be
    /// established. Never "nothing here".</summary>
    NotLoaded = 3,

    /// <summary>The area itself could not be resolved - no provider, an
    /// unreadable shape, a deleted designation. Refuses; never falls back to a
    /// radius nobody asked for.</summary>
    AreaUnavailable = 4,
}

/// <summary>What one look at the work area found.</summary>
internal sealed class CollectionSurvey
{
    public CollectionSurvey(
        SurveyOutcome outcome,
        IReadOnlyList<CollectionCandidate>? candidates,
        string detail = "")
    {
        Outcome = outcome;
        Candidates = candidates ?? Array.Empty<CollectionCandidate>();
        Detail = detail ?? string.Empty;
    }

    public static CollectionSurvey Unavailable(string detail) =>
        new CollectionSurvey(SurveyOutcome.AreaUnavailable, null, detail);

    public SurveyOutcome Outcome { get; }

    public IReadOnlyList<CollectionCandidate> Candidates { get; }

    public string Detail { get; }

    /// <summary>Nothing here, and the looking actually finished. An unfinished
    /// look with no candidates is not an empty area, which is the mistake that
    /// makes an NPC declare a job done while standing next to the job.</summary>
    public bool IsExhaustedArea
    {
        get
        {
            if (Outcome != SurveyOutcome.Finished)
            {
                return false;
            }

            for (int index = 0; index < Candidates.Count; index++)
            {
                if (Candidates[index].IsTakeable)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
