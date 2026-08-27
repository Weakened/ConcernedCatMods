using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Display-only clustering for semantic zoom: buckets pins into
/// grid cells and folds crowded cells into cluster markers. Entities are
/// never mutated, merged, or hidden in the store — clustering decides only
/// what the renderer shows at the current zoom tier. Pins in
/// <c>alwaysVisible</c> (selection, search hits) are always rendered as
/// themselves. Output ordering is deterministic for testability.</summary>
internal static class PinClusterer
{
    public sealed class Cluster
    {
        public Cluster(RoadPoint center, string dominantIconId, string dominantCategory, List<AtlasPin> members)
        {
            Center = center;
            DominantIconId = dominantIconId;
            DominantCategory = dominantCategory;
            Members = members;
        }

        public RoadPoint Center { get; }
        public string DominantIconId { get; }
        public string DominantCategory { get; }
        public List<AtlasPin> Members { get; }
    }

    public sealed class Result
    {
        public List<AtlasPin> Singles { get; } = new();
        public List<Cluster> Clusters { get; } = new();
    }

    public static Result Compute(
        IEnumerable<AtlasPin> pins,
        float cellMeters,
        int minClusterSize = 3,
        ISet<Guid>? alwaysVisible = null)
    {
        var result = new Result();
        if (cellMeters <= 0f)
        {
            result.Singles.AddRange(pins);
            return result;
        }

        var buckets = new SortedDictionary<long, List<AtlasPin>>();
        foreach (AtlasPin pin in pins)
        {
            if (alwaysVisible is not null && alwaysVisible.Contains(pin.Id.Value))
            {
                result.Singles.Add(pin);
                continue;
            }

            int cellX = (int)Math.Floor(pin.Position.X / cellMeters);
            int cellZ = (int)Math.Floor(pin.Position.Z / cellMeters);
            long key = ((long)(uint)cellX << 32) ^ (uint)cellZ;
            if (!buckets.TryGetValue(key, out List<AtlasPin>? bucket))
            {
                bucket = new List<AtlasPin>();
                buckets.Add(key, bucket);
            }

            bucket.Add(pin);
        }

        foreach (KeyValuePair<long, List<AtlasPin>> entry in buckets)
        {
            List<AtlasPin> bucket = entry.Value;
            if (bucket.Count < minClusterSize)
            {
                result.Singles.AddRange(bucket);
                continue;
            }

            float sumX = 0f;
            float sumZ = 0f;
            var iconCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var categoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (AtlasPin pin in bucket)
            {
                sumX += pin.Position.X;
                sumZ += pin.Position.Z;
                Count(iconCounts, pin.IconId);
                if (pin.Category.Length > 0)
                {
                    Count(categoryCounts, pin.Category);
                }
            }

            var center = new RoadPoint(sumX / bucket.Count, 0f, sumZ / bucket.Count);
            result.Clusters.Add(new Cluster(center, Dominant(iconCounts), Dominant(categoryCounts), bucket));
        }

        return result;
    }

    private static void Count(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int current);
        counts[key] = current + 1;
    }

    private static string Dominant(Dictionary<string, int> counts)
    {
        string best = "";
        int bestCount = 0;
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (entry.Value > bestCount ||
                (entry.Value == bestCount && string.CompareOrdinal(entry.Key, best) < 0))
            {
                best = entry.Key;
                bestCount = entry.Value;
            }
        }

        return best;
    }
}
