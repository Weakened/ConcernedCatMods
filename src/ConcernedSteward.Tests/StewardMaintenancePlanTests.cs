using System.Collections.Generic;
using System.IO;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>The #379 production adoption, proved through the real upkeep loop.
/// The assertions are deliberately about both records and world-side counts:
/// a plan file that looks tidy while a second executor duplicated wood would
/// not satisfy any of these tests.</summary>
public sealed class StewardMaintenancePlanTests
{
    private const string Wood = StewardFixture.Wood;

    [Fact]
    public void The_shipped_runtime_constructs_and_drives_the_plan_around_the_only_executor()
    {
        string runtime = File.ReadAllText(Path.Combine(
            ProductSources.Product, "Runtime", "StewardRuntime.cs"));
        string loop = File.ReadAllText(Path.Combine(
            ProductSources.Product, "Domain", "Upkeep", "UpkeepLoop.cs"));

        Assert.Contains("new StewardMaintenancePlan(recordRoot, _npc.Registry, _npc.Identity)", runtime);
        Assert.Contains("new UpkeepLoop(UpkeepLimits.Default, _journal, Report, _plan, _npc)", runtime);
        Assert.Contains("_plan.OnWorldLoaded(scope.Value, planEvidence, _pack, _carryingName)", runtime);
        Assert.Contains("_npc.World));", runtime);
        Assert.Contains("_loop.RequireAttention(", runtime);

        Assert.Contains("_plan.TryBegin(", loop);
        Assert.Contains("_plan.TryIntend(", loop);
        Assert.Contains("_plan.TryConclude(", loop);
        Assert.Contains("_plan.TryPrepareReturn(", loop);
        Assert.DoesNotContain("MoveTo(", File.ReadAllText(Path.Combine(
            ProductSources.Product, "Domain", "Upkeep", "StewardMaintenancePlan.cs")));
        Assert.DoesNotContain("FeedOneUnit(", File.ReadAllText(Path.Combine(
            ProductSources.Product, "Domain", "Upkeep", "StewardMaintenancePlan.cs")));
    }

    [Fact]
    public void A_real_maintenance_round_constructs_and_settles_a_durable_plan()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 9f);

        fixture.RunUntilTripEnds();

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(NpcPlanPhase.Settled, saved.Phase);
        Assert.Equal(NpcPlanCustody.Clear, saved.Custody);
        Assert.Equal(1, saved.TargetsDone);
        Assert.Empty(saved.Carried);
        Assert.Empty(saved.Reservations);
        Assert.Single(fixture.Fires.Fed);
        Assert.Equal(49, fixture.Depot.Count(Wood));
        fixture.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_batched_tour_records_every_target_and_settles_each_exactly_once()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var pack = new FakeStore("the Steward's pack");
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), pack, Wood);

        Assert.True(
            context.Plan.TryBegin(
                context.World,
                "depot-key",
                new FuelTargetKey("fire-0", StewardFixture.Epoch),
                Wood,
                plannedUnits: 5,
                targetCount: 5,
                out string beginFailure),
            beginFailure);
        Assert.True(
            context.Plan.TryIntend("withdraw the measured batch", out string withdrawalIntentFailure),
            withdrawalIntentFailure);
        Assert.True(
            context.Plan.TryConclude(
                true,
                Wood,
                carried: 5,
                targetDone: false,
                note: "the measured batch is in the pack",
                out string withdrawalConclusionFailure),
            withdrawalConclusionFailure);
        Assert.True(context.Plan.TryRouteToTarget(out string routeFailure), routeFailure);
        Assert.True(context.Plan.TryEnterExecuting(out string executingFailure), executingFailure);

        for (int target = 0; target < 5; target++)
        {
            Assert.True(
                context.Plan.TryIntend("feed target " + target, out string intentFailure),
                intentFailure);
            Assert.True(
                context.Plan.TryConclude(
                    true,
                    Wood,
                    carried: 4 - target,
                    targetDone: true,
                    note: "target settled",
                    out string concludeFailure),
                concludeFailure);
        }

        Assert.True(
            context.Plan.TryBeginReconciliation(
                Wood, 0, "the batch is measured", out string reconcileFailure),
            reconcileFailure);
        Assert.True(
            context.Plan.TryFinish(Wood, 0, "the batch is complete", out string finishFailure),
            finishFailure);

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(5, saved.TargetsTotal);
        Assert.Equal(5, saved.TargetsDone);
        Assert.Equal(NpcPlanPhase.Settled, saved.Phase);
        Assert.Empty(saved.Reservations);
        Assert.Empty(saved.Carried);
    }

    [Fact]
    public void An_incomplete_tour_cannot_be_written_as_settled_after_its_remainder_returns()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var pack = new FakeStore("the Steward's pack");
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), pack, Wood);

        Assert.True(context.Plan.TryBegin(
            context.World,
            "depot-key",
            new FuelTargetKey("fire-0", StewardFixture.Epoch),
            Wood,
            plannedUnits: 2,
            targetCount: 2,
            out string beginFailure), beginFailure);
        Assert.True(context.Plan.TryIntend("withdraw two", out string withdrawalIntentFailure),
            withdrawalIntentFailure);
        Assert.True(context.Plan.TryConclude(
            true, Wood, 2, false, "two are measured in the pack",
            out string withdrawalConclusionFailure), withdrawalConclusionFailure);
        Assert.True(context.Plan.TryRouteToTarget(out string routeFailure), routeFailure);
        Assert.True(context.Plan.TryEnterExecuting(out string executingFailure), executingFailure);
        Assert.True(context.Plan.TryIntend("feed one", out string feedIntentFailure),
            feedIntentFailure);
        Assert.True(context.Plan.TryConclude(
            true, Wood, 1, true, "only the first target was serviced",
            out string feedConclusionFailure), feedConclusionFailure);
        Assert.True(context.Plan.TryBeginReconciliation(
            Wood, 0, "the unused unit returned", out string reconciliationFailure),
            reconciliationFailure);

        Assert.False(context.Plan.TryFinish(
            Wood, 0, "the incomplete tour must not settle", out string finishFailure));
        Assert.Contains("remain unresolved", finishFailure);

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(NpcPlanPhase.Reconciling, saved.Phase);
        Assert.Equal(2, saved.TargetsTotal);
        Assert.Equal(1, saved.TargetsDone);
        Assert.Empty(saved.Carried);
        Assert.Empty(saved.Reservations);
    }

    [Fact]
    public void Shared_driver_skip_settles_the_durable_target_and_returns_the_exact_remainder()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(
            limits: new UpkeepLimits(32, 50, 1, 15f, 90f, 3),
            plan: context.Plan,
            sharedDriver: true,
            adoption: context.Adoption);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        FuelTargetObservation refilled =
            fixture.AddFire("refilled", fuel: 1f, x: 8f, z: 0f);

        for (int step = 0; step < 40; step++)
        {
            if (fixture.Loop.Phase == UpkeepPhase.ToTarget
                && fixture.Loop.CurrentTarget.Value == "refilled")
            {
                break;
            }

            fixture.Run();
        }

        Assert.Equal("refilled", fixture.Loop.CurrentTarget.Value);
        fixture.Fires.Replace(FakeFires.Fuelled(refilled, 10f));
        fixture.RunUntilTripEnds(cap: 40);

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(NpcPlanPhase.Settled, saved.Phase);
        Assert.Equal(2, saved.TargetsTotal);
        Assert.Equal(2, saved.TargetsDone);
        Assert.Empty(saved.Carried);
        Assert.Empty(saved.Reservations);
        Assert.Equal(18, fixture.Loop.Custody.Withdrawn);
        Assert.Equal(9, fixture.Loop.Custody.Burned);
        Assert.Equal(9, fixture.Loop.Custody.Returned);
        Assert.Equal(41, fixture.Depot.Count(Wood));
        fixture.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void An_authority_stop_before_withdrawal_refunds_the_empty_plan_and_later_work_can_restart()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 9f);

        fixture.Run(1); // selected and reserved, but has not reached the depot
        fixture.Authority = WorkAuthorityVerdict.NotHost;
        fixture.Run(1);

        Assert.Equal(UpkeepPhase.Idle, fixture.Loop.Phase);
        Assert.Equal(NpcPlanPhase.Refunded, Load(folder.Path, context.Scope).Phase);
        Assert.Equal(50, fixture.Depot.Count(Wood));
        Assert.Equal(0, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);

        fixture.Authority = WorkAuthorityVerdict.Granted;
        fixture.RunUntilTripEnds();

        Assert.Equal(NpcPlanPhase.Settled, Load(folder.Path, context.Scope).Phase);
        Assert.Single(fixture.Fires.Fed);
        fixture.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_clean_reload_returns_the_measured_pack_and_never_replays_the_old_fire()
    {
        using var folder = new TemporaryFolder();
        PlanContext first = Open(folder.Path);
        var fixture = new StewardFixture(plan: first.Plan, planWorld: first.World);
        first.Plan.OnWorldLoaded(first.Scope, Evidence(first.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);

        fixture.Run(3); // selected, reached the depot, withdrew ten
        Assert.Equal(10, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);

        NpcWorldEpoch reloadedWorld = first.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, first.Registry, StewardNpcRole.Id);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recovered.OnWorldLoaded(first.Scope, Evidence(reloadedWorld), fixture.Pack, Wood);
        recoveredLoop.OnWorldLoaded(fixture.Pack, Wood);

        for (int step = 0; step < 8; step++)
        {
            Tick(recoveredLoop, fixture, reloadedWorld, 100f + (step * 10f));
            if (recoveredLoop.Phase == UpkeepPhase.Idle
                && recoveredLoop.Custody.Carried == 0)
            {
                break;
            }
        }

        Assert.Equal(UpkeepPhase.Idle, recoveredLoop.Phase);
        Assert.Empty(fixture.Fires.Fed);
        Assert.Equal(0, fixture.Pack.Count(Wood));
        Assert.Equal(50, fixture.Depot.Count(Wood));
        Assert.Equal(NpcPlanPhase.Refunded, Load(folder.Path, first.Scope).Phase);
    }

    [Fact]
    public void A_reload_of_a_pending_movement_stops_and_does_not_replay_it()
    {
        using var folder = new TemporaryFolder();
        PlanContext first = Open(folder.Path);
        var fixture = new StewardFixture(plan: first.Plan, planWorld: first.World);
        first.Plan.OnWorldLoaded(first.Scope, Evidence(first.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(2); // at the depot, before the withdrawal tick
        Assert.True(first.Plan.TryIntend("crash after the durable intent", out string failure), failure);

        NpcWorldEpoch reloadedWorld = first.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, first.Registry, StewardNpcRole.Id);
        recovered.OnWorldLoaded(first.Scope, Evidence(reloadedWorld), fixture.Pack, Wood);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recoveredLoop.OnWorldLoaded(fixture.Pack, Wood);
        Tick(recoveredLoop, fixture, reloadedWorld, 100f);

        NpcPlanState saved = Load(folder.Path, first.Scope);
        Assert.True(recovered.IsBlocked);
        Assert.Equal(UpkeepPhase.NeedsAttention, recoveredLoop.Phase);
        Assert.Equal(NpcPlanPhase.NeedsAttention, saved.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, saved.Custody);
        Assert.Equal(50, fixture.Depot.Count(Wood));
        Assert.Equal(0, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);
    }

    [Fact]
    public void A_pack_that_disagrees_with_the_plan_stays_as_evidence_and_never_moves()
    {
        using var folder = new TemporaryFolder();
        PlanContext first = Open(folder.Path);
        var fixture = new StewardFixture(plan: first.Plan, planWorld: first.World);
        first.Plan.OnWorldLoaded(first.Scope, Evidence(first.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(3);
        Assert.Equal(10, fixture.Pack.Count(Wood));

        // The world and the durable plan now disagree. Recovery may describe
        // that disagreement; it may not pick either side as the truth.
        fixture.Pack.Put(Wood, 2);
        NpcWorldEpoch reloadedWorld = first.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, first.Registry, StewardNpcRole.Id);
        recovered.OnWorldLoaded(first.Scope, Evidence(reloadedWorld), fixture.Pack, Wood);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recoveredLoop.OnWorldLoaded(fixture.Pack, Wood);
        Tick(recoveredLoop, fixture, reloadedWorld, 100f);

        NpcPlanState evidence = Load(folder.Path, first.Scope);
        Assert.Equal(NpcPlanPhase.NeedsAttention, evidence.Phase);
        Assert.Equal(10, evidence.CarriedUnits); // the missing eight were not silently forgotten
        Assert.Equal(2, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);
        Assert.Equal(UpkeepPhase.NeedsAttention, recoveredLoop.Phase);
    }

    [Fact]
    public void A_failed_plan_receipt_after_withdrawal_stops_with_no_retry_or_compensation()
    {
        using var folder = new TemporaryFolder();
        var codec = new OneWriteFailureCodec(
            new StewardPlanCodec(StewardNpcRole.Id, StewardRole.UpkeepJobId), failAt: 6);
        PlanContext context = Open(folder.Path, codec);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);

        fixture.Run(3); // write 6 is the conclusion after the real withdrawal

        Assert.Equal(UpkeepPhase.NeedsAttention, fixture.Loop.Phase);
        Assert.Equal(40, fixture.Depot.Count(Wood));
        Assert.Equal(10, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);
        Assert.Equal(50, fixture.Depot.Count(Wood) + fixture.Pack.Count(Wood));

        fixture.Run(5);
        Assert.Equal(40, fixture.Depot.Count(Wood));
        Assert.Equal(10, fixture.Pack.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(NpcPlanPhase.NeedsAttention, saved.Phase);
        Assert.NotEqual(NpcPlanCustody.Clear, saved.Custody);

        string resolved = fixture.Loop.Acknowledge(fixture.Pack, Wood);
        Assert.Contains("Nothing was recreated", resolved);
        Assert.Equal(10, fixture.Loop.Custody.Carried);
        fixture.Run(3);

        Assert.Equal(0, fixture.Pack.Count(Wood));
        Assert.Equal(50, fixture.Depot.Count(Wood));
        Assert.Empty(fixture.Fires.Fed);
        Assert.Equal(NpcPlanPhase.Refunded, Load(folder.Path, context.Scope).Phase);
    }

    [Fact]
    public void Death_keeps_the_carried_plan_as_evidence_until_a_person_resolves_it()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(3);

        // The runtime's body callback has already let vanilla drop the item
        // instances before it records this loss. No item is constructed here.
        fixture.Loop.Custody.RecordLost(10);
        fixture.Pack.Put(Wood, 0);
        fixture.Loop.RequireAttention("The Steward died; the carried items are on the ground.");

        NpcPlanState saved = Load(folder.Path, context.Scope);
        Assert.Equal(NpcPlanPhase.NeedsAttention, saved.Phase);
        Assert.Equal(10, saved.CarriedUnits);
        Assert.Equal(10, fixture.Loop.Custody.Unaccounted);
        Assert.Empty(fixture.Fires.Fed);

        string path = new StewardPlanFiles(folder.Path).ResolvePath(context.Scope);
        string before = File.ReadAllText(path);
        string refused = fixture.Loop.Acknowledge(new UnavailableStore(), Wood);

        Assert.Contains("could not be counted", refused);
        Assert.True(context.Plan.IsBlocked);
        Assert.Equal(before, File.ReadAllText(path));

        string resolved = fixture.Loop.Acknowledge(fixture.Pack, Wood);
        Assert.Contains("Nothing was recreated", resolved);
        Assert.False(context.Plan.IsBlocked);
        Assert.False(fixture.Loop.Custody.HasLoss);
        Assert.Equal(NpcPlanPhase.Refunded, Load(folder.Path, context.Scope).Phase);
    }

    [Fact]
    public void A_partial_loss_restored_after_reload_cannot_bypass_an_unavailable_pack()
    {
        using var folder = new TemporaryFolder();
        PlanContext first = Open(folder.Path);
        var fixture = new StewardFixture(plan: first.Plan, planWorld: first.World);
        first.Plan.OnWorldLoaded(first.Scope, Evidence(first.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(3);
        Assert.Equal(10, fixture.Pack.Count(Wood));

        fixture.Loop.Custody.RecordLost(1);
        fixture.Pack.Put(Wood, 9);
        fixture.Loop.RequireAttention(
            "One unit was lost while the remaining carried load still needs accounting.");
        Assert.Equal(9, fixture.Loop.Custody.Carried);
        Assert.Equal(1, fixture.Loop.Custody.Unaccounted);

        NpcWorldEpoch reloadedWorld = first.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, first.Registry, StewardNpcRole.Id);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recoveredLoop.Custody.RestoreLoss(1);
        var unavailable = new UnavailableStore();
        recovered.OnWorldLoaded(first.Scope, Evidence(reloadedWorld), unavailable, Wood);
        recoveredLoop.OnWorldLoaded(unavailable, Wood);

        string path = new StewardPlanFiles(folder.Path).ResolvePath(first.Scope);
        string before = File.ReadAllText(path);
        string answer = recoveredLoop.Acknowledge(unavailable, Wood);

        Assert.Contains("could not be counted", answer);
        Assert.True(recovered.IsBlocked);
        Assert.True(recoveredLoop.Custody.HasLoss);
        Assert.Equal(UpkeepPhase.NeedsAttention, recoveredLoop.Phase);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(Directory.GetFiles(folder.Path, "*.corrupt*").Any());
        Assert.Equal(9, fixture.Pack.Count(Wood));
    }

    [Fact]
    public void A_pending_plan_cannot_be_resolved_while_the_live_pack_is_unavailable()
    {
        using var folder = new TemporaryFolder();
        PlanContext first = Open(folder.Path);
        var fixture = new StewardFixture(plan: first.Plan, planWorld: first.World);
        first.Plan.OnWorldLoaded(first.Scope, Evidence(first.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(2); // at the depot, immediately before the withdrawal
        Assert.True(first.Plan.TryIntend("crash after the durable intent", out string failure), failure);

        NpcWorldEpoch reloadedWorld = first.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, first.Registry, StewardNpcRole.Id);
        var unavailable = new UnavailableStore();
        recovered.OnWorldLoaded(first.Scope, Evidence(reloadedWorld), unavailable, Wood);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recoveredLoop.OnWorldLoaded(unavailable, Wood);

        string path = new StewardPlanFiles(folder.Path).ResolvePath(first.Scope);
        string before = File.ReadAllText(path);
        string answer = recoveredLoop.Acknowledge(unavailable, Wood);

        Assert.Contains("could not be counted", answer);
        Assert.True(recovered.IsBlocked);
        Assert.Equal(UpkeepPhase.NeedsAttention, recoveredLoop.Phase);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(Directory.GetFiles(folder.Path, "*.corrupt*").Any());
    }

    [Fact]
    public void An_unreadable_plan_cannot_be_quarantined_while_the_live_pack_is_unavailable()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(1);

        string path = new StewardPlanFiles(folder.Path).ResolvePath(context.Scope);
        string[] lines = File.ReadAllLines(path);
        File.WriteAllLines(path, lines.Take(lines.Length - 1));

        NpcWorldEpoch reloadedWorld = context.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, context.Registry, StewardNpcRole.Id);
        var unavailable = new UnavailableStore();
        recovered.OnWorldLoaded(context.Scope, Evidence(reloadedWorld), unavailable, Wood);
        var recoveredLoop = new UpkeepLoop(
            UpkeepLimits.Default, new MemoryUpkeepJournal(), plan: recovered);
        recoveredLoop.OnWorldLoaded(unavailable, Wood);

        string before = File.ReadAllText(path);
        string answer = recoveredLoop.Acknowledge(unavailable, Wood);

        Assert.Contains("could not be counted", answer);
        Assert.True(recovered.IsBlocked);
        Assert.Equal(UpkeepPhase.NeedsAttention, recoveredLoop.Phase);
        Assert.True(File.Exists(path));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(Directory.GetFiles(folder.Path, "*.corrupt*").Any());
    }

    [Fact]
    public void An_unreadable_plan_is_quarantined_only_by_explicit_resolution()
    {
        using var folder = new TemporaryFolder();
        PlanContext context = Open(folder.Path);
        var fixture = new StewardFixture(plan: context.Plan, planWorld: context.World);
        context.Plan.OnWorldLoaded(context.Scope, Evidence(context.World), fixture.Pack, Wood);
        fixture.AddFire("fire-1", fuel: 0f);
        fixture.Run(1);

        string path = new StewardPlanFiles(folder.Path).ResolvePath(context.Scope);
        string[] lines = File.ReadAllLines(path);
        File.WriteAllLines(path, lines.Take(lines.Length - 1)); // remove the completeness trailer

        NpcWorldEpoch reloadedWorld = context.Registry.BeginWorldLoad(out _);
        var recovered = new StewardMaintenancePlan(
            folder.Path, context.Registry, StewardNpcRole.Id);
        recovered.OnWorldLoaded(context.Scope, Evidence(reloadedWorld), fixture.Pack, Wood);

        Assert.True(recovered.IsBlocked);
        Assert.True(File.Exists(path));
        Assert.False(Directory.GetFiles(folder.Path, "*.corrupt*").Any());

        Assert.True(recovered.Resolve(Wood, fixture.Pack.Count(Wood), out string failure), failure);
        Assert.False(recovered.IsBlocked);
        Assert.True(Directory.GetFiles(folder.Path, "*.corrupt*").Any());
        Assert.Equal(NpcPlanPhase.Refunded, Load(folder.Path, context.Scope).Phase);
    }

    private static PlanContext Open(string root, INpcPlanCodec? codec = null)
    {
        var registry = new NpcRoleRegistry();
        var role = new StewardNpcRole(root);
        var adoption = new StewardNpcAdoption(registry, role);
        Assert.True(adoption.Register().IsRegistered);
        NpcWorldEpoch world = adoption.NoteWorldLoaded();
        var plan = new StewardMaintenancePlan(root, registry, role.Identity, codec);
        return new PlanContext(
            registry,
            world,
            plan,
            new SettlementScope(379L, new SettlementId("home")),
            adoption);
    }

    private static NpcPlanEvidence Evidence(NpcWorldEpoch world) =>
        new NpcPlanEvidence(
            world,
            bodiesAnswering: 1,
            bodyDied: false,
            areaIsReadable: true,
            areaMoved: false,
            mayWork: true,
            containersAvailable: true,
            routeAvailable: true,
            playerPaused: false,
            at: 0f);

    private static void Tick(
        UpkeepLoop loop, StewardFixture fixture, NpcWorldEpoch world, float now) =>
        loop.Tick(new UpkeepTick(
            now,
            tendingEnabled: true,
            WorkAuthorityVerdict.Granted,
            fixture.Scope.Resolve(),
            fixture.Fires,
            fixture.Depot,
            fixture.Pack,
            fixture.Motion,
            world));

    private static NpcPlanState Load(string root, SettlementScope scope)
    {
        Assert.True(NpcPlanJournal.TryOpen(
            new StewardPlanFiles(root).ResolvePath(scope),
            new StewardPlanCodec(StewardNpcRole.Id, StewardRole.UpkeepJobId),
            out NpcPlanJournal? journal,
            out string failure), failure);
        NpcPlanLoad loaded = journal!.Load();
        Assert.True(loaded.IsLoaded, loaded.Failure);
        return loaded.Plan!;
    }

    private readonly struct PlanContext
    {
        internal PlanContext(
            NpcRoleRegistry registry,
            NpcWorldEpoch world,
            StewardMaintenancePlan plan,
            SettlementScope scope,
            StewardNpcAdoption adoption)
        {
            Registry = registry;
            World = world;
            Plan = plan;
            Scope = scope;
            Adoption = adoption;
        }

        internal NpcRoleRegistry Registry { get; }
        internal NpcWorldEpoch World { get; }
        internal StewardMaintenancePlan Plan { get; }
        internal SettlementScope Scope { get; }
        internal StewardNpcAdoption Adoption { get; }
    }

    private sealed class UnavailableStore : IItemStorePort
    {
        public string Describe => "an unavailable Steward pack";

        public bool IsAvailable => false;

        public IReadOnlyCollection<string> ItemNames => Array.Empty<string>();

        public int Count(string fuelItemName) => -1;

        public int RoomFor(string fuelItemName, int count) => 0;

        public void MoveTo(IItemStorePort destination, string fuelItemName, int count) =>
            throw new InvalidOperationException("An unavailable pack cannot move material.");
    }

    private sealed class OneWriteFailureCodec : INpcPlanCodec
    {
        private readonly INpcPlanCodec _inner;
        private readonly int _failAt;
        private int _writes;

        internal OneWriteFailureCodec(INpcPlanCodec inner, int failAt)
        {
            _inner = inner;
            _failAt = failAt;
        }

        public IReadOnlyList<string>? Encode(NpcPlanState state)
        {
            _writes++;
            return _writes == _failAt ? null : _inner.Encode(state);
        }

        public bool TryDecode(
            IReadOnlyList<string> lines, out NpcPlanState? state, out string reason) =>
            _inner.TryDecode(lines, out state, out reason);
    }
}
