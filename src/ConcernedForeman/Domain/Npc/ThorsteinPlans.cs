using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Thorstein's two shared-interruption plans (#379), one per job, and
/// the world they belong to. Opened when a world's settlement record is open,
/// closed when the world goes. Game-free: the runtime supplies the scope, the
/// world epoch and the measurements.</summary>
internal sealed class ThorsteinPlans
{
    private readonly ThorsteinPlanFiles _files;

    internal ThorsteinPlans(string root)
    {
        _files = new ThorsteinPlanFiles(root);
        Collect = new ThorsteinWorkPlan(ForemanNpcRole.Id, ThorsteinWorkPlan.CollectJobId);
        Build = new ThorsteinWorkPlan(ForemanNpcRole.Id, ThorsteinWorkPlan.BuildJobId);
    }

    internal ThorsteinWorkPlan Collect { get; }

    internal ThorsteinWorkPlan Build { get; }

    /// <summary>The world load the plans are open in; unknown when none is.
    /// </summary>
    internal NpcWorldEpoch World { get; private set; }

    /// <summary>What he carries for each job, by item name; null when it
    /// cannot be read. Set by the runtime that owns the job.</summary>
    internal Func<IReadOnlyList<KeyValuePair<string, int>>?>? MeasureCollect { get; set; }

    internal Func<IReadOnlyList<KeyValuePair<string, int>>?>? MeasureBuild { get; set; }

    /// <summary>Opens both plans for this world's settlement. A plan that was
    /// open when the last session ended comes back Recovered and is revalidated
    /// by the next start; nothing moves here.</summary>
    internal void OnWorldLoaded(SettlementScope? scope, NpcWorldEpoch world)
    {
        World = world;
        if (scope == null || !scope.Value.IsComplete || world.IsUnknown)
        {
            string why = world.IsUnknown
                ? "the shared NPC runtime gave this world load no epoch"
                : "no settlement record is open in this world";
            Collect.Open(null, why);
            Build.Open(null, why);
            return;
        }

        Collect.Open(_files.ResolvePath(scope.Value, ThorsteinWorkPlan.CollectJobId), null);
        Build.Open(_files.ResolvePath(scope.Value, ThorsteinWorkPlan.BuildJobId), null);
    }

    internal void OnWorldUnloaded()
    {
        World = NpcWorldEpoch.Unknown;
        Collect.Close("the world closed");
        Build.Close("the world closed");
    }

    /// <summary>The human exit for one job's stopped plan: <c>collect</c> or
    /// <c>build</c>.</summary>
    internal string Resolve(string? job)
    {
        if (!string.IsNullOrEmpty(job) && !string.Equals(job, "build", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(job, "collect", StringComparison.OrdinalIgnoreCase))
        {
            return "Usage: cf_collect resolve [collect | build]. Nothing was resolved.";
        }

        bool build = string.Equals(job, "build", StringComparison.OrdinalIgnoreCase);
        ThorsteinWorkPlan plan = build ? Build : Collect;
        Func<IReadOnlyList<KeyValuePair<string, int>>?>? measure = build ? MeasureBuild : MeasureCollect;
        plan.Resolve(World, measure?.Invoke(), out string sentence);
        return sentence;
    }

    internal string Describe() => Collect.Describe() + "\n" + Build.Describe();
}
