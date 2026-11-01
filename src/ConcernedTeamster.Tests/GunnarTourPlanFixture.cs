using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>A world of sources, Gunnar and one chest, counted unit by unit
/// (#379). Every unit has its own number, so "the chest received each unit
/// exactly once" is a question about numbers rather than totals: a replayed
/// deposit, a unit recreated from a name or one silently dropped all show.
///
/// <b>It never mints.</b> A pick moves the source's own units onto him, a
/// deposit moves his own units into the chest, a death moves them to the ground.
/// So the conservation check is not a tautology of the fake: what it catches is
/// the plan telling the runtime to do something twice - a second deposit of a
/// load that already arrived is recorded in <see cref="Received"/> as the same
/// number twice, because <see cref="ReplayDeposit"/> is what a replaying plan
/// would have the runtime do.</summary>
internal sealed class TourWorld
{
    public const string Stone = "Stone";
    public const string Wood = "Wood";

    private readonly Dictionary<int, string> _materials = new Dictionary<int, string>();

    public TourWorld(int sources, int unitsPerSource)
    {
        int next = 0;
        for (int source = 0; source < sources; source++)
        {
            var units = new List<int>();
            for (int unit = 0; unit < unitsPerSource; unit++)
            {
                _materials[next] = source % 2 == 0 ? Stone : Wood;
                units.Add(next++);
            }

            Sources.Add(units);
        }

        Total = next;
    }

    private TourWorld(TourWorld other)
    {
        _materials = new Dictionary<int, string>(other._materials);
        foreach (List<int> source in other.Sources)
        {
            Sources.Add(new List<int>(source));
        }

        Gunnar.AddRange(other.Gunnar);
        Chest.AddRange(other.Chest);
        Received.AddRange(other.Received);
        Ground.AddRange(other.Ground);
        Total = other.Total;
        DepositsThatMoved = other.DepositsThatMoved;
    }

    public List<List<int>> Sources { get; } = new List<List<int>>();

    public List<int> Gunnar { get; } = new List<int>();

    public List<int> Chest { get; } = new List<int>();

    /// <summary>Every arrival in the chest, in order, including a repeat.
    /// </summary>
    public List<int> Received { get; } = new List<int>();

    public List<int> Ground { get; } = new List<int>();

    public int Total { get; }

    public int DepositsThatMoved { get; private set; }

    public TourWorld Snapshot() => new TourWorld(this);

    public int NextSource()
    {
        for (int index = 0; index < Sources.Count; index++)
        {
            if (Sources[index].Count != 0)
            {
                return index;
            }
        }

        return -1;
    }

    public void Pick(int source)
    {
        Gunnar.AddRange(Sources[source]);
        Sources[source].Clear();
    }

    public void Deposit()
    {
        if (Gunnar.Count == 0)
        {
            return;
        }

        DepositsThatMoved++;
        Chest.AddRange(Gunnar);
        Received.AddRange(Gunnar);
        Gunnar.Clear();
    }

    /// <summary>What a plan that replayed a deposit would make the runtime do:
    /// credit the chest with the last load again. Never called by a passing
    /// test; present so the fixture's own check can be shown to catch it.
    /// </summary>
    public void ReplayDeposit(IEnumerable<int> load) => Received.AddRange(load);

    public void DieAndDrop()
    {
        Ground.AddRange(Gunnar);
        Gunnar.Clear();
    }

    public IReadOnlyList<KeyValuePair<string, int>> Carried() =>
        Gunnar
            .GroupBy(unit => _materials[unit])
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .ToList();

    public void AssertConserved()
    {
        int inSources = Sources.Sum(source => source.Count);
        Assert.Equal(Total, inSources + Gunnar.Count + Chest.Count + Ground.Count);
        Assert.Equal(Received.Count, Received.Distinct().Count());
        Assert.Equal(Chest.OrderBy(unit => unit), Received.OrderBy(unit => unit));
    }

    public void AssertAllInTheChestOnce()
    {
        AssertConserved();
        Assert.Equal(Total, Chest.Count);
        Assert.Empty(Gunnar);
        Assert.All(Sources, source => Assert.Empty(source));
    }
}

/// <summary>A codec that stops writing after a number of successful writes -
/// the process dying, from the plan's point of view. Every later write is
/// refused, so the run stops at the first one it attempts.</summary>
internal sealed class KillingCodec : INpcPlanCodec
{
    private readonly INpcPlanCodec _inner;

    public KillingCodec(INpcPlanCodec inner, int writesAllowed)
    {
        _inner = inner;
        Allowed = writesAllowed;
    }

    public int Allowed { get; set; }

    public int Writes { get; private set; }

    /// <summary>The phase and custody of every write that succeeded, in order.
    /// </summary>
    public List<(NpcPlanPhase Phase, NpcPlanCustody Custody, int Done)> Written { get; } =
        new List<(NpcPlanPhase, NpcPlanCustody, int)>();

    public IReadOnlyList<string>? Encode(NpcPlanState state)
    {
        if (Writes >= Allowed)
        {
            return null;
        }

        IReadOnlyList<string>? lines = _inner.Encode(state);
        if (lines != null)
        {
            Writes++;
            Written.Add((state.Phase, state.Custody, state.TargetsDone));
        }

        return lines;
    }

    public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason) =>
        _inner.TryDecode(lines, out state, out reason);
}

/// <summary>The tour runtime's order of operations, game-free: the same
/// announce, act, conclude sequence <c>GunnarCollectionRuntime.Tour.cs</c>
/// follows (and <c>validate_repo.py</c>'s #379 audit pins there), run against a
/// <see cref="TourWorld"/>. A plan call that returns false is the process
/// dying, so the script stops at once - which is exactly what makes "the world
/// moved and the conclusion was not written" one of the instants it covers.
/// </summary>
internal sealed class TourScript
{
    public TourScript(GunnarTourPlan plan, TourWorld world, NpcWorldEpoch epoch)
    {
        Plan = plan;
        World = world;
        Epoch = epoch;
    }

    public GunnarTourPlan Plan { get; }

    public TourWorld World { get; }

    public NpcWorldEpoch Epoch { get; }

    public string Area { get; set; } = "area 0.0,0.0,0.0 r20 rev1";

    public string Chest { get; set; } = "chest-1";

    public string Said { get; private set; } = string.Empty;

    public TourPlanFacts Facts(int bodies = 1, bool mayWork = true) =>
        new TourPlanFacts(Epoch, bodies, World.Carried(), mayWork, 0f);

    public bool Start()
    {
        bool started = Plan.TryStart(Facts(), Area, Chest, out string said);
        Said = said;
        return started;
    }

    public bool Pick(int source)
    {
        if (!Plan.IntendPick("about to pick source " + source, out string unwritten))
        {
            Said = unwritten;
            return false;
        }

        World.Pick(source);
        bool concluded = Plan.ConcludePick(World.Carried(), "picked source " + source, out string failure);
        Said = failure;
        return concluded;
    }

    public bool Deposit()
    {
        if (!Plan.IntendDeposit("about to take what he carries to the chest", out string unwritten))
        {
            Said = unwritten;
            return false;
        }

        World.Deposit();
        bool concluded = Plan.ConcludeDeposit(true, World.Carried(), "deposited", out string failure);
        Said = failure;
        return concluded;
    }

    public bool Finish() => Plan.Ended(true, World.Carried(), string.Empty);

    public bool Stop(string why) => Plan.Ended(false, World.Carried(), why);

    /// <summary>The scripted tour: begin, the phases, two picks, one trip to
    /// the chest, settled. False as soon as any step was not written.</summary>
    public bool RunWholeTour() =>
        Start() && Pick(0) && Pick(1) && Deposit() && Finish();

    /// <summary>Carries a plan that may resume to the end: what he holds goes
    /// to the chest first, then every remaining source, then the last trip,
    /// then settled.</summary>
    public void ResumeToTheEnd()
    {
        Assert.True(Start(), "a resumable plan did not start: " + Said);
        CarryOnToTheEnd();
    }

    /// <summary>The rest of <see cref="ResumeToTheEnd"/>, for a plan that has
    /// already been started.</summary>
    public void CarryOnToTheEnd()
    {
        if (World.Gunnar.Count != 0)
        {
            Assert.True(Deposit(), Said);
        }

        for (int source = World.NextSource(); source >= 0; source = World.NextSource())
        {
            Assert.True(Pick(source), Said);
        }

        if (World.Gunnar.Count != 0)
        {
            Assert.True(Deposit(), Said);
        }

        Assert.True(Finish(), Plan.Describe());
    }
}

/// <summary>A folder that is deleted afterwards.</summary>
internal sealed class TourPlanFolder : IDisposable
{
    public TourPlanFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tcc-tour-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string PlanPath => GunnarTourPlanFiles.PathFor(Path, 4242L, out _)!;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The plan as the file holds it, decoded without the library's
    /// recovery step - so a pending custody reads as pending.</summary>
    public NpcPlanState? Raw()
    {
        if (!File.Exists(PlanPath))
        {
            return null;
        }

        var codec = new GunnarTourPlanCodec(new GunnarTourPlan().Identity, GunnarTourPlan.JobId);
        Assert.True(codec.TryDecode(File.ReadAllLines(PlanPath), out NpcPlanState? state, out string reason), reason);
        return state;
    }
}

internal static class TourPlans
{
    public static GunnarTourPlan Opened(string path, INpcPlanCodec? codec = null)
    {
        var plan = new GunnarTourPlan(codec);
        plan.Open(path, null);
        Assert.True(plan.HasJournal);
        return plan;
    }

    public static IReadOnlyList<NpcMaterialStack> Load(TourWorld world) => GunnarTourPlan.Measured(world.Carried())!;
}
