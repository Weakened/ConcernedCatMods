using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>One immutable recorded trip (CT-016): a bounded sample sequence
/// from attach to detach for one cart.</summary>
public sealed class Trip
{
    public Trip(int id, string cartId, IReadOnlyList<TripSample> samples)
    {
        Id = id;
        CartId = cartId;
        Samples = samples;
    }

    /// <summary>Sidecar-local sequence number (oldest lowest); reassigned
    /// on prune so ids stay dense.</summary>
    public int Id { get; }

    public string CartId { get; }

    public IReadOnlyList<TripSample> Samples { get; }

    public double StartTimeSeconds => Samples.Count > 0 ? Samples[0].TimeSeconds : 0d;

    public double EndTimeSeconds => Samples.Count > 0 ? Samples[Samples.Count - 1].TimeSeconds : 0d;
}
