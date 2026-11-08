using System;
namespace TheConcernedCat.Settlement.Housing;
internal enum ResidentRoutineStage { Paused, Rest, Morning, Day, Evening }
/// <summary>A modest local routine, driven by vanilla's observed day fraction.
/// It grants no work or movement. Unknown clock/housing pauses the last pose.</summary>
internal static class ResidentRoutine
{
    internal static ResidentRoutineStage At(float dayFraction, bool admitted, bool housingVerified)
    {
        if (!admitted || !housingVerified || float.IsNaN(dayFraction) || float.IsInfinity(dayFraction) || dayFraction < 0 || dayFraction > 1)
            return ResidentRoutineStage.Paused;
        if (dayFraction <= .25f || dayFraction >= .75f) return ResidentRoutineStage.Rest;
        if (dayFraction < .35f) return ResidentRoutineStage.Morning;
        if (dayFraction >= .65f) return ResidentRoutineStage.Evening;
        return ResidentRoutineStage.Day;
    }
}
