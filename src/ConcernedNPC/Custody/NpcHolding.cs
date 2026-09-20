using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>One job's material, at one place, of one kind. The unit of
/// conservation: every recorded unit is in exactly one of these.</summary>
internal readonly struct NpcHolding
{
    internal NpcHolding(string? jobId, NpcCustodyLocation location, NpcMaterial material, int count)
    {
        JobId = jobId ?? string.Empty;
        Location = location;
        Material = material;
        Count = count;
    }

    internal string JobId { get; }

    internal NpcCustodyLocation Location { get; }

    internal NpcMaterial Material { get; }

    internal int Count { get; }

    public override string ToString() =>
        JobId + " " + Material + " x" + Count.ToString(CultureInfo.InvariantCulture) + " at " + Location;
}
