using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>One job's material, at one place, of one kind. The unit of
/// conservation: every recorded unit is in exactly one of these.</summary>
public readonly struct NpcHolding
{
    private readonly string? _jobId;

    internal NpcHolding(string? jobId, NpcCustodyLocation location, NpcMaterial material, int count)
    {
        _jobId = jobId ?? string.Empty;
        Location = location;
        Material = material;
        Count = count;
    }

    /// <summary>The job the units are held for. Never null.</summary>
    public string JobId => _jobId ?? string.Empty;

    public NpcCustodyLocation Location { get; }

    public NpcMaterial Material { get; }

    public int Count { get; }

    public override string ToString() =>
        JobId + " " + Material + " x" + Count.ToString(CultureInfo.InvariantCulture) + " at " + Location;
}
