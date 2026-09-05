using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>Pure per-trip aggregation (CT-018). Distance is the sum of XZ
/// hops between consecutive samples; grade/speed aggregates skip NaN
/// markers honestly (NaN result when nothing was finite).</summary>
public static class TripSummarizer
{
    public static TripSummary Summarize(Trip trip)
    {
        float distance = 0f;
        float massSum = 0f;
        float worstAbsGrade = float.NaN;
        float speedSum = 0f;
        int speedCount = 0;

        for (int index = 0; index < trip.Samples.Count; index++)
        {
            TripSample sample = trip.Samples[index];
            massSum += sample.TotalMass;

            if (index > 0)
            {
                TripSample previous = trip.Samples[index - 1];
                float deltaX = sample.PositionX - previous.PositionX;
                float deltaZ = sample.PositionZ - previous.PositionZ;
                distance += (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            }

            if (!float.IsNaN(sample.GradePercent))
            {
                float magnitude = Math.Abs(sample.GradePercent);
                if (float.IsNaN(worstAbsGrade) || magnitude > worstAbsGrade)
                {
                    worstAbsGrade = magnitude;
                }
            }

            if (!float.IsNaN(sample.SpeedMetersPerSecond))
            {
                speedSum += sample.SpeedMetersPerSecond;
                speedCount++;
            }
        }

        int count = trip.Samples.Count;
        return new TripSummary(
            trip.Id,
            trip.CartId,
            trip.StartTimeSeconds,
            trip.EndTimeSeconds - trip.StartTimeSeconds,
            distance,
            count > 0 ? massSum / count : 0f,
            worstAbsGrade,
            speedCount > 0 ? speedSum / speedCount : float.NaN);
    }
}
