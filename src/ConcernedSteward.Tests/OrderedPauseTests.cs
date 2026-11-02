using System.IO;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;
public sealed partial class StewardMaintenancePlanTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Ordered_pause_preserves_the_production_cursor_and_resumes_without_a_second_fetch(int rawPhase)
    {
        using var folder = new TemporaryFolder();
        var context = Open(folder.Path);
        var f = new StewardFixture(plan: context.Plan, sharedDriver: true, adoption: context.Adoption);
        f.Thresholds = new MaintenanceThresholds(600, 600, 10);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), f.Pack, Wood);
        var fire = f.AddFire("fire-1", fuel: 5f);
        if (rawPhase >= 6)
        {
            for(int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
            Assert.Equal(UpkeepPhase.ToTarget,f.Loop.Phase);
            f.Fires.Replace(FakeFires.Fuelled(fire,10f)); // carried surplus must return
        }
        var wanted = (UpkeepPhase)rawPhase;
        for (int i = 0; i < 50 && f.Loop.Phase != wanted; i++) f.Run();
        Assert.Equal(wanted, f.Loop.Phase);
        var before = Load(folder.Path, context.Scope);
        var reservedTarget = f.Loop.Reservation.Target;
        int depot = f.Depot.Count(Wood), pack = f.Pack.Count(Wood), fed = f.Fires.Fed.Count;
        int walks = f.Motion.WalkedTo.Count, stops = f.Motion.Stops;
        Assert.True(f.Loop.PauseForOrder(out string failure), failure);
        var paused = Load(folder.Path, context.Scope);
        Assert.Equal(before.Phase, paused.Phase);
        Assert.True(paused.CarriesTheSameWorkAs(before));
        string path = new TheConcernedCat.ConcernedSteward.Domain.Persistence.StewardPlanFiles(folder.Path).ResolvePath(context.Scope);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(f.Loop.PauseForOrder(out failure), failure); // idempotent, no second note
        Assert.Equal(bytes, File.ReadAllBytes(path));
        f.Now += 10000;
        f.Run(3);
        Assert.True(f.Loop.IsOrderPaused);
        Assert.Equal(wanted, f.Loop.Phase);
        Assert.Equal(reservedTarget, f.Loop.Reservation.Target);
        Assert.Equal(depot, f.Depot.Count(Wood)); Assert.Equal(pack, f.Pack.Count(Wood));
        Assert.Equal(fed, f.Fires.Fed.Count); Assert.Equal(walks, f.Motion.WalkedTo.Count);
        Assert.Equal(stops + 1, f.Motion.Stops);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(f.Loop.ResumeOrder(out failure), failure);
        if (wanted == UpkeepPhase.ToDepot || wanted == UpkeepPhase.ToTarget)
        {
            f.Motion.WalkStatus = WorkerWalkStatus.Walking;
            f.Run();
            Assert.Equal(wanted, f.Loop.Phase); // paused wall time did not expire the walk deadline
            f.Motion.WalkStatus = WorkerWalkStatus.Arrived;
        }
        f.RunUntilTripEnds(cap: 100);
        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(5, f.Loop.Custody.Withdrawn);
        Assert.Equal(rawPhase >= 6 ? 0 : 5, f.Fires.Fed.Count);
        Assert.Equal(rawPhase >= 6 ? 50 : 45, f.Depot.Count(Wood));
        f.AssertConserved(50);
    }

    [Fact]
    public void Authority_loss_outranks_pause_and_forbids_resuming_the_old_cursor()
    {
        using var folder = new TemporaryFolder(); var context = Open(folder.Path);
        var f = new StewardFixture(plan: context.Plan, sharedDriver: true, adoption: context.Adoption);
        f.Thresholds = new MaintenanceThresholds(600, 600, 10);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), f.Pack, Wood);
        f.AddFire("fire-1", fuel: 5f);
        for (int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
        Assert.Equal(UpkeepPhase.ToTarget,f.Loop.Phase);
        Assert.True(f.Loop.PauseForOrder(out _));
        int depot=f.Depot.Count(Wood), pack=f.Pack.Count(Wood);
        f.Authority=WorkAuthorityVerdict.NotHost; f.Run();
        Assert.False(f.Loop.IsOrderPaused); Assert.False(f.Loop.ResumeOrder(out _));
        Assert.Equal(depot,f.Depot.Count(Wood)); Assert.Equal(pack,f.Pack.Count(Wood));
        Assert.Empty(f.Fires.Fed);
    }

    [Fact]
    public void Optional_note_failure_does_not_restart_or_lose_the_durable_job()
    {
        using var folder = new TemporaryFolder();
        var codec = new PauseNoteCodec(); var context = Open(folder.Path,codec);
        var f = new StewardFixture(plan:context.Plan,sharedDriver:true,adoption:context.Adoption);
        f.Thresholds=new MaintenanceThresholds(600,600,10);
        context.Plan.OnWorldLoaded(context.Scope,Evidence(context.World),f.Pack,Wood);
        f.AddFire("fire-1",fuel:5f);
        for(int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
        Assert.Equal(UpkeepPhase.ToTarget,f.Loop.Phase);
        var before=Load(folder.Path,context.Scope);
        codec.Refuse=true; Assert.True(f.Loop.PauseForOrder(out _)); f.Run(3);
        Assert.True(Load(folder.Path,context.Scope).CarriesTheSameWorkAs(before));
        Assert.Empty(f.Fires.Fed); Assert.Equal(5,f.Pack.Count(Wood));
        codec.Refuse=false; Assert.True(f.Loop.ResumeOrder(out _)); f.RunUntilTripEnds();
        Assert.Equal(5,f.Loop.Custody.Withdrawn); Assert.Equal(5,f.Fires.Fed.Count);
        f.AssertConserved(50);
    }

    [Fact]
    public void A_real_session_pause_is_cleared_by_world_load_without_moving_anything()
    {
        using var folder = new TemporaryFolder(); var context=Open(folder.Path);
        var f=new StewardFixture(plan:context.Plan,sharedDriver:true,adoption:context.Adoption);
        f.Thresholds=new MaintenanceThresholds(600,600,10);
        context.Plan.OnWorldLoaded(context.Scope,Evidence(context.World),f.Pack,Wood);
        f.AddFire("fire-1",fuel:5f);
        for(int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
        Assert.True(f.Loop.PauseForOrder(out _)); f.Run(); Assert.True(f.Loop.IsOrderPaused);
        int depot=f.Depot.Count(Wood),pack=f.Pack.Count(Wood);
        f.Loop.OnWorldLoaded(f.Pack,Wood);
        Assert.False(f.Loop.IsOrderPaused); Assert.False(f.Loop.ResumeOrder(out _));
        Assert.Equal(depot,f.Depot.Count(Wood)); Assert.Equal(pack,f.Pack.Count(Wood));
        Assert.Empty(f.Fires.Fed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_or_attention_outranks_an_existing_pause_without_losing_held_fuel(bool attention)
    {
        using var folder=new TemporaryFolder(); var context=Open(folder.Path);
        var f=new StewardFixture(plan:context.Plan,sharedDriver:true,adoption:context.Adoption);
        f.Thresholds=new MaintenanceThresholds(600,600,10);
        context.Plan.OnWorldLoaded(context.Scope,Evidence(context.World),f.Pack,Wood);
        f.AddFire("fire-1",fuel:5f);
        for(int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
        Assert.True(f.Loop.PauseForOrder(out _));
        if(attention) f.Loop.RequireAttention("inspect this job"); else f.Loop.Stop("runtime disabled");
        Assert.False(f.Loop.IsOrderPaused); Assert.False(f.Loop.ResumeOrder(out _));
        Assert.Equal(45,f.Depot.Count(Wood)); Assert.Equal(5,f.Pack.Count(Wood));
        Assert.Empty(f.Fires.Fed); f.AssertConserved(50);
    }

    private sealed class PauseNoteCodec : INpcPlanCodec
    {
        private readonly StewardPlanCodec _inner=new(StewardNpcRole.Id,StewardRole.UpkeepJobId);
        internal bool Refuse;
        public IReadOnlyList<string>? Encode(NpcPlanState state)=>Refuse?null:_inner.Encode(state);
        public bool TryDecode(IReadOnlyList<string> lines,out NpcPlanState? state,out string failure)=>
            _inner.TryDecode(lines,out state,out failure);
    }

    [Fact]
    public void Unsafe_or_absent_plan_cannot_be_paused_and_reload_discards_session_pause()
    {
        using var folder = new TemporaryFolder(); var context = Open(folder.Path);
        var f = new StewardFixture(plan: context.Plan, sharedDriver: true, adoption: context.Adoption);
        f.Thresholds = new MaintenanceThresholds(600, 600, 10);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World),f.Pack,Wood);
        Assert.False(f.Loop.PauseForOrder(out _)); Assert.False(f.Loop.ResumeOrder(out _));
        f.AddFire("fire-1",fuel:5f);
        for(int i=0;i<50 && f.Loop.Phase!=UpkeepPhase.ToTarget;i++) f.Run();
        Assert.True(context.Plan.TryIntend("unanswered movement",out _));
        Assert.False(f.Loop.PauseForOrder(out _));
        context.Plan.NeedsAttention("inspect the pending movement"); f.Run();
        Assert.False(f.Loop.IsOrderPaused); Assert.False(f.Loop.ResumeOrder(out _));
        f.Loop.OnWorldLoaded(f.Pack,Wood);
        Assert.False(f.Loop.IsOrderPaused);
    }
}

public sealed class PauseClockTests
{
    [Fact]
    public void Paused_clock_holds_deadlines_and_rejects_invalid_or_rewound_time()
    {
        var clock = new UpkeepActiveClock();
        Assert.False(clock.Read(-1,false,out _));
        Assert.True(clock.Read(100,false,out float now)); Assert.Equal(100,now);
        Assert.True(clock.Read(101,false,out now)); Assert.Equal(101,now);
        Assert.True(clock.Read(10000,true,out now)); Assert.Equal(101,now);
        Assert.True(clock.Read(20000,true,out now)); Assert.Equal(101,now);
        Assert.True(clock.Read(20001,false,out now)); Assert.Equal(101,now);
        Assert.True(clock.Read(20002,false,out now)); Assert.Equal(102,now);
        Assert.False(clock.Read(float.NaN,false,out now)); Assert.Equal(102,now);
        Assert.False(clock.Read(float.PositiveInfinity,false,out now)); Assert.Equal(102,now);
        Assert.False(clock.Read(0,false,out now)); Assert.Equal(102,now);
        clock.Reset(); Assert.True(clock.Read(0,false,out now)); Assert.Equal(0,now);
    }
}
