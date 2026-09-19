using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>That a role can actually run a job, and that running one still does
/// not hand it the sequence.
///
/// <b>Why this file exists.</b> The wave-two review found the library could not
/// be adopted, and the body half was answered first. Everything under
/// <c>Planning/</c>, <c>Routing/</c>, <c>Custody/</c>, <c>Storage/</c>,
/// <c>Reservations/</c> and most of <c>Work/</c> was still internal, so the
/// three role leaves about to be written could name none of it. The answer is
/// not a folder made public: it is a small data surface plus
/// <see cref="NpcJobDriver"/>, and the thing that has to be proved is that the
/// small surface is actually enough.
///
/// <b>How it proves it.</b> The consumer types at the bottom of this file -
/// the area, the chest, the ground and the role - are written as a product
/// would write them, using public types only. If any member they touch were
/// internal, this file would not compile, and that is the proof rather than any
/// assertion in it. On top of that, every member on the path is pinned by
/// reflection, so narrowing one fails a test instead of failing a role agent.
///
/// <b>The honest limit, the same one the body half has.</b> This assembly
/// <i>links</i> the library's sources rather than referencing the assembly, so
/// internals are visible here in a way they never are to a product. Two places
/// in this file deliberately use that - obtaining a world epoch through the
/// process-wide registry the way a product does, and nothing else - and the
/// consumer types touch nothing internal at all. What the file cannot prove is
/// that a separate assembly compiles; what it can prove is that every member
/// the sequence needs is public, and it does.</summary>
public sealed class JobAdoptionSurfaceTests : IDisposable
{
    private readonly NpcWorldEpoch _world;

    public JobAdoptionSurfaceTests()
    {
        // Exactly what a role does: the library mints the epoch and the role
        // receives it. A role may not invent one.
        NpcRoleRegistry.Shared.EndWorldLoad();
        _world = NpcRoleRegistry.Shared.BeginWorldLoad(out _);
    }

    public void Dispose()
    {
        NpcRoleRegistry.Shared.EndWorldLoad();
        NpcJobBooks.Forget();
    }

    [Fact]
    public void A_role_can_reach_every_step_of_running_a_job()
    {
        // The whole of what a role's runtime calls, and nothing on it may be
        // internal. Each entry is one call or one read in that sequence.
        AssertPublic(typeof(NpcJobDriver), "For");
        AssertPublic(typeof(NpcJobDriver), "Next");
        AssertPublic(typeof(NpcJobDriver), "Done");
        AssertPublic(typeof(NpcJobDriver), "Skipped");
        AssertPublic(typeof(NpcJobDriver), "Failed");
        AssertPublic(typeof(NpcJobDriver), "Abandon");
        AssertPublic(typeof(NpcJobDriver), "Progress");
        AssertPublic(typeof(NpcJobDriver), "Verdict");
        AssertPublic(typeof(NpcJobDriver), "Reason");
        AssertPublic(typeof(NpcJobDriver), "Manifest");
        AssertPublic(typeof(NpcJobDriver), "Shortfall");
        AssertPublic(typeof(NpcJobDriver), "Carrying");
        AssertPublic(typeof(NpcJobDriver), "LeftForAnotherRound");
        AssertPublic(typeof(NpcJobDriver), "LastRound");
        AssertPublic(typeof(NpcJobDriver), "LastLook");
        AssertPublic(typeof(NpcJobDriver), "Steps");

        // What a role describes its work with, and must therefore construct.
        AssertPublicConstructor(typeof(JobTarget));
        AssertPublicConstructor(typeof(JobManifest));
        AssertPublicConstructor(typeof(JobManifestLine));
        AssertPublicConstructor(typeof(SourceStock));
        AssertPublicConstructor(typeof(StockLine));
        AssertPublicConstructor(typeof(NpcCarryCapacity));
        AssertPublicConstructor(typeof(JobStepActions));
        AssertPublicConstructor(typeof(NpcJobOrder));
        AssertPublicConstructor(typeof(AreaSample));
        AssertPublicConstructor(typeof(NpcContainerAccess));
        AssertPublicConstructor(typeof(NpcPoint));

        // What it reads off a step to carry one out.
        AssertPublic(typeof(PlannedStep), "Step");
        AssertPublic(typeof(PlannedStep), "IsCollect");
        AssertPublic(typeof(PlannedStep), "Source");
        AssertPublic(typeof(PlannedStep), "Target");
        AssertPublic(typeof(PlannedStep), "Moves");
        AssertPublic(typeof(JobStep), "Action");
        AssertPublic(typeof(JobStep), "Subject");
        AssertPublic(typeof(JobStep), "At");
        AssertPublic(typeof(JobStep), "Units");
        AssertPublic(typeof(SourceStock), "Container");

        // What it reads at the end of a round.
        AssertPublic(typeof(JobReconciliation), "IsComplete");
        AssertPublic(typeof(JobReconciliation), "HasUnfinishedWork");
        AssertPublic(typeof(JobReconciliation), "LeftOver");
        AssertPublic(typeof(JobReconciliation), "Outstanding");
        AssertPublic(typeof(JobReconciliation), "LeftForAnotherRound");
        AssertPublic(typeof(AreaScanReport), "Outcome");
        AssertPublic(typeof(AreaScanReport), "IsConclusive");
    }

    [Fact]
    public void A_role_drives_a_whole_job_and_never_sequences_one()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        for (int index = 0; index < 6; index++)
        {
            role.Wants("post" + index.ToString(), Point(10f + index, 0f), 5);
        }

        NpcJobDriver driver = Drive(role, new NpcCarryCapacity(10));

        var collected = new List<PlannedStep>();
        var serviced = new List<PlannedStep>();
        Run(driver, role, collected, serviced);

        Assert.Equal(NpcJobProgress.Finished, driver.Progress);
        Assert.Equal(string.Empty, driver.Reason);

        // Every target was serviced, and it took more than one trip - which is
        // the behaviour the whole pipeline exists for. A loop that fetched per
        // target would have collected six times.
        Assert.Equal(6, serviced.Count);
        Assert.True(collected.Count > 0);
        Assert.True(collected.Count < serviced.Count);

        // The role was handed its own words back, never the library's, and
        // never a position from inside the round.
        foreach (PlannedStep step in collected)
        {
            Assert.Equal("take", step.Step.Action);
            Assert.NotNull(step.Source.Container);
            Assert.Equal("supply", step.Source.Container!.Key);
        }

        foreach (PlannedStep step in serviced)
        {
            Assert.Equal("do", step.Step.Action);
            Assert.StartsWith("post", step.Target.Key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_job_with_nothing_left_to_do_finishes_without_doing_anything()
    {
        var role = new AdoptedRole(_world);
        role.Wants("post0", Point(10f, 0f), 0);
        role.Complete("post0");

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);
        NpcJobAdvance advance = driver.Next(Point(0f, 0f));

        Assert.Equal(NpcJobProgress.Finished, advance.Progress);
        Assert.Equal(JobPlanVerdict.NothingToDo, advance.Verdict);
        Assert.False(advance.HasStep);

        // And the evidence behind it, which is the difference between an area
        // that is empty and one the player walked away from.
        Assert.True(driver.LastLook.IsConclusive);
    }

    [Fact]
    public void A_job_nothing_can_provision_stops_with_a_sentence_and_is_not_retried()
    {
        var role = new AdoptedRole(_world);
        role.Wants("post0", Point(10f, 0f), 40);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);
        NpcJobAdvance advance = driver.Next(Point(0f, 0f));

        Assert.Equal(NpcJobProgress.Stopped, advance.Progress);
        Assert.Equal(JobPlanVerdict.ShortOfMaterial, advance.Verdict);
        Assert.NotEqual(string.Empty, advance.Reason);

        // The shortfall is what discriminates the two kinds of short, and it is
        // readable. Not the verdict's name: that is not a sentence.
        Assert.False(driver.Shortfall.IsEmpty);
        Assert.Equal(40, driver.Shortfall.RequiredOf("wood"));

        // Asking again changes nothing and starts no round.
        Assert.Equal(NpcJobProgress.Stopped, driver.Next(Point(0f, 0f)).Progress);
        Assert.Equal(0, driver.Rounds);
    }

    [Fact]
    public void A_look_that_ran_out_of_budget_waits_rather_than_stopping()
    {
        var role = new AdoptedRole(_world);
        role.Wants("post0", Point(10f, 0f), 0);

        var order = new NpcJobOrder(
            new NpcIdentity("product", "worker"),
            "job",
            role.Area,
            _world,
            NpcCarryCapacity.Unlimited,
            new JobStepActions("take", "do"),
            allowance: 0);
        NpcJobDriver driver = NpcJobDriver.For(order, role);

        NpcJobAdvance advance = driver.Next(Point(0f, 0f));

        Assert.Equal(NpcJobProgress.Waiting, advance.Progress);

        // Never a reason to stop a job, and deliberately not a round spent: a
        // job that burned its rounds on budget-exhausted looks would give up
        // for no reason at all.
        Assert.Equal(0, driver.Rounds);
        Assert.Equal(NpcJobProgress.Waiting, driver.Next(Point(0f, 0f)).Progress);
    }

    [Fact]
    public void A_target_that_somebody_else_did_is_skipped_and_costs_nothing_owed()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        role.Wants("post0", Point(10f, 0f), 5);
        role.Wants("post1", Point(20f, 0f), 5);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);

        // Between planning and walking, the player builds post1 himself.
        NpcJobAdvance first = driver.Next(Point(0f, 0f));
        Assert.Equal(NpcJobProgress.Do, first.Progress);
        role.Complete("post1");

        var collected = new List<PlannedStep>();
        var serviced = new List<PlannedStep>();
        Carry(driver, role, first, collected, serviced);
        Run(driver, role, collected, serviced);

        Assert.Equal(NpcJobProgress.Finished, driver.Progress);

        // One serviced, one skipped, and nothing owed for the one that was
        // skipped - which is what makes a skip different from a failure, and
        // why the job is finished rather than planning a round for it.
        Assert.Single(serviced);
        Assert.Equal(1, driver.Rounds);
        Assert.Equal(1, driver.LastRound.Skipped);
        Assert.True(driver.LastRound.Outstanding.IsEmpty);
        Assert.True(driver.LastRound.IsComplete);
    }

    [Fact]
    public void A_chest_the_player_shuts_before_the_job_starts_is_refused_at_planning()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        role.Wants("post0", Point(10f, 0f), 5);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);

        // Permission is re-read at the moment of use, never cached into a plan
        // made a minute ago.
        chest.Shut(NpcContainerRefusal.WardDenied);

        var collected = new List<PlannedStep>();
        var serviced = new List<PlannedStep>();
        Run(driver, role, collected, serviced);

        Assert.Empty(collected);
        Assert.Empty(serviced);
        Assert.Equal(NpcJobProgress.Stopped, driver.Progress);
        Assert.NotEqual(string.Empty, driver.Reason);
    }

    [Fact]
    public void Two_jobs_in_one_world_do_not_both_get_the_same_target()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var first = new AdoptedRole(_world);
        first.Offer(chest, "wood", 500);
        first.Wants("post0", Point(10f, 0f), 5);

        var second = new AdoptedRole(_world);
        second.Offer(chest, "wood", 500);
        second.Wants("post0", Point(10f, 0f), 5);

        NpcJobDriver one = Drive(first, NpcCarryCapacity.Unlimited, "job-one");
        NpcJobDriver two = Drive(second, NpcCarryCapacity.Unlimited, "job-two");

        Assert.Equal(NpcJobProgress.Do, one.Next(Point(0f, 0f)).Progress);

        // The second job wanted a target the first is holding. It is told to
        // wait - not refused for good, because the first may give it up - and
        // it is told that without the role having supplied a book, seen a
        // reservation, or known that reservations exist.
        Assert.Equal(NpcJobProgress.Waiting, two.Next(Point(0f, 0f)).Progress);

        // And giving the first job up frees it.
        one.Abandon();
        Assert.Equal(NpcJobProgress.Do, two.Next(Point(0f, 0f)).Progress);
    }

    [Fact]
    public void A_job_that_takes_more_than_one_round_carries_its_surplus_forward()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        role.Wants("post0", Point(10f, 0f), 5);
        role.Wants("post1", Point(20f, 0f), 5);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);

        // Collect everything, then find that one target is gone before it is
        // serviced: the material for it stays on his back, and the books say so.
        NpcJobAdvance advance = driver.Next(Point(0f, 0f));
        while (advance.Progress == NpcJobProgress.Do && advance.Step.IsCollect)
        {
            driver.Done();
            advance = driver.Next(Point(0f, 0f));
        }

        Assert.Equal(NpcJobProgress.Do, advance.Progress);
        role.Complete(advance.Step.Target.Key);
        driver.Skipped();

        var collected = new List<PlannedStep>();
        var serviced = new List<PlannedStep>();
        Run(driver, role, collected, serviced);

        // Ten were fetched for two posts and five were spent on the one that
        // was still wanted. The other five are on his back, and that is the
        // number the next round is planned against rather than fetching a
        // second load of it.
        Assert.Equal(5, driver.Carrying.RequiredOf("wood"));
        Assert.Equal(5, driver.LastRound.LeftOver.RequiredOf("wood"));
    }

    [Fact]
    public void The_round_after_a_partial_one_spends_the_surplus_instead_of_fetching_it_again()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        role.Wants("post0", Point(10f, 0f), 5);
        role.Wants("post1", Point(20f, 0f), 5);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);

        int collects = 0;
        bool failedOnce = false;
        for (int guard = 0; guard < 500; guard++)
        {
            NpcJobAdvance advance = driver.Next(Point(0f, 0f));
            if (advance.Progress != NpcJobProgress.Do)
            {
                break;
            }

            if (advance.Step.IsCollect)
            {
                collects++;
                driver.Done();
                continue;
            }

            // The second post goes wrong once. The material fetched for it is
            // on his back and is still owed, so there has to be another round.
            if (!failedOnce && string.Equals(advance.Step.Target.Key, "post1", StringComparison.Ordinal))
            {
                failedOnce = true;
                driver.Failed();
                continue;
            }

            role.Complete(advance.Step.Target.Key);
            driver.Done();
        }

        Assert.True(failedOnce);
        Assert.Equal(NpcJobProgress.Finished, driver.Progress);
        Assert.Equal(2, driver.Rounds);

        // The whole point. Ten wood were fetched once, five were spent, and the
        // second round was planned against the five already on his back - so it
        // opened no chest at all. A driver that did not thread the carry would
        // have walked to the chest a second time for wood he was holding, and
        // this is the only assertion in this file that can tell the difference.
        Assert.Equal(1, collects);
        Assert.True(driver.Carrying.IsEmpty);
    }

    [Fact]
    public void A_chest_the_player_shuts_mid_round_is_never_opened_again()
    {
        var chest = new AdoptedChest("supply", _world, Point(1f, 0f), NpcContainerUse.Take);
        var role = new AdoptedRole(_world);
        role.Offer(chest, "wood", 500);
        role.Wants("post0", Point(10f, 0f), 5);
        role.Wants("post1", Point(20f, 0f), 5);

        // One post per trip, so the round opens the chest twice. The player
        // shuts it between the two.
        NpcJobDriver driver = Drive(role, new NpcCarryCapacity(5));

        int collects = 0;
        for (int guard = 0; guard < 500; guard++)
        {
            NpcJobAdvance advance = driver.Next(Point(0f, 0f));
            if (advance.Progress != NpcJobProgress.Do)
            {
                break;
            }

            if (advance.Step.IsCollect)
            {
                collects++;

                // A ward goes up the moment he has finished at the chest the
                // first time. Permission is re-read before the next visit, not
                // taken from the plan that chose it a minute ago.
                chest.Shut(NpcContainerRefusal.WardDenied);
                driver.Done();
                continue;
            }

            role.Complete(advance.Step.Target.Key);
            driver.Done();
        }

        // Opened once, and never again after it refused. A driver that trusted
        // the plan's own choice of chest would have opened it twice.
        Assert.Equal(1, collects);
    }

    [Fact]
    public void A_round_that_achieved_nothing_stops_the_job_rather_than_being_planned_again()
    {
        var role = new AdoptedRole(_world);
        role.Wants("post0", Point(10f, 0f), 0);
        role.Wants("post1", Point(20f, 0f), 0);

        NpcJobDriver driver = Drive(role, NpcCarryCapacity.Unlimited);

        int attempts = 0;
        for (int guard = 0; guard < 500; guard++)
        {
            NpcJobAdvance advance = driver.Next(Point(0f, 0f));
            if (advance.Progress != NpcJobProgress.Do)
            {
                break;
            }

            // Everything he reaches goes wrong. Nothing is done and nothing is
            // given up on, so the round achieved literally nothing.
            attempts++;
            driver.Failed();
        }

        Assert.Equal(NpcJobProgress.Stopped, driver.Progress);
        Assert.NotEqual(string.Empty, driver.Reason);

        // One round, not eight. A driver that planned again while there was
        // unfinished work would walk the same round over and over against a
        // world that answers the same way every time - which is what the round
        // loop keying on the verdict, and on a round having achieved something,
        // exists to prevent. Two attempts is the one round's two steps.
        Assert.Equal(1, driver.Rounds);
        Assert.Equal(2, attempts);
        Assert.True(driver.LastRound.HasUnfinishedWork);
        Assert.False(driver.LastRound.IsComplete);
    }

    private NpcJobDriver Drive(AdoptedRole role, NpcCarryCapacity capacity, string jobId = "job")
    {
        var order = new NpcJobOrder(
            new NpcIdentity("product", "worker"),
            jobId,
            role.Area,
            _world,
            capacity,
            new JobStepActions("take", "do"));
        return NpcJobDriver.For(order, role);
    }

    private static void Run(
        NpcJobDriver driver, AdoptedRole role, List<PlannedStep> collected, List<PlannedStep> serviced)
    {
        for (int guard = 0; guard < 500; guard++)
        {
            NpcJobAdvance advance = driver.Next(Point(0f, 0f));
            if (advance.Progress != NpcJobProgress.Do)
            {
                return;
            }

            Carry(driver, role, advance, collected, serviced);
        }

        Assert.Fail("the driver never stopped asking for steps");
    }

    /// <summary>What a role's runtime does with one step: read the role's own
    /// words off it, do the thing, say what became of it.</summary>
    private static void Carry(
        NpcJobDriver driver,
        AdoptedRole role,
        in NpcJobAdvance advance,
        List<PlannedStep> collected,
        List<PlannedStep> serviced)
    {
        PlannedStep step = advance.Step;
        if (step.IsCollect)
        {
            collected.Add(step);
            driver.Done();
            return;
        }

        serviced.Add(step);
        role.Complete(step.Target.Key);
        driver.Done();
    }

    private static NpcPoint Point(float x, float z) => new NpcPoint(x, 0f, z);

    private static void AssertPublic(Type type, string member)
    {
        Assert.True(type.IsPublic, type.FullName + " is not public, so no product can name it.");
        MemberInfo[] found = type.GetMember(
            member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        Assert.True(
            found.Length > 0,
            type.FullName + "." + member + " is not public. A role's runtime calls it, so narrowing it makes the "
            + "job surface undriveable from outside - which is the finding this file exists to keep closed.");
    }

    private static void AssertPublicConstructor(Type type)
    {
        Assert.True(type.IsPublic, type.FullName + " is not public, so no product can name it.");
        Assert.True(
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0,
            type.FullName + " has no public constructor. A role has to state this one - it is a fact about the "
            + "role's own work that nothing in this library could compute - so it must be constructable.");
    }

    /// <summary>A work area, as a product would write one. Every member it
    /// implements is public; nothing here reaches into the library.</summary>
    private sealed class AdoptedArea : INpcWorkArea
    {
        public bool Contains(NpcPoint point) => point.HorizontalDistanceTo(BoundingCentre) <= BoundingRadiusMetres;

        public NpcPoint BoundingCentre => new NpcPoint(0f, 0f, 0f);

        public float BoundingRadiusMetres => 64f;

        public int Revision => 1;

        public string Describe => "the work area";
    }

    /// <summary>A chest, as a product would write one: a key, a place, and a
    /// permission re-read on every call rather than cached.</summary>
    private sealed class AdoptedChest : INpcContainer
    {
        private NpcContainerUse _allowed;
        private NpcContainerRefusal _refusal;

        internal AdoptedChest(string key, NpcWorldEpoch epoch, NpcPoint at, NpcContainerUse allowed)
        {
            Key = key;
            Epoch = epoch;
            Position = at;
            _allowed = allowed;
            _refusal = NpcContainerRefusal.None;
        }

        public string Key { get; }

        public NpcWorldEpoch Epoch { get; }

        public NpcPoint Position { get; }

        public string Describe => "the supply chest";

        public NpcContainerAccess Access => new NpcContainerAccess(_allowed, _refusal);

        internal void Shut(NpcContainerRefusal refusal) => _refusal = refusal;
    }

    /// <summary>The ground, as a product's adapter would answer for it.</summary>
    private sealed class AdoptedGround : INpcAreaProbe
    {
        public AreaSample Probe(NpcPoint point) =>
            new AreaSample(AreaSampleVerdict.Standable, point, AreaRejection.None);
    }

    /// <summary>A role, as a product would write one. It says what is worth
    /// doing, what doing it takes, what the chests hold and when a thing is
    /// done - and nothing at all about the order any of it happens in.</summary>
    private sealed class AdoptedRole : INpcJobRole
    {
        private readonly NpcWorldEpoch _world;
        private readonly List<JobTarget> _targets = new List<JobTarget>();
        private readonly List<SourceStock> _sources = new List<SourceStock>();
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);

        internal AdoptedRole(NpcWorldEpoch world)
        {
            _world = world;
        }

        internal AdoptedArea Area { get; } = new AdoptedArea();

        public INpcAreaProbe? Probe { get; } = new AdoptedGround();

        public INpcSourceAvailability? Availability => null;

        internal void Wants(string key, NpcPoint at, int wood)
        {
            JobManifest needs = wood <= 0
                ? JobManifest.Empty
                : new JobManifest(new[] { new JobManifestLine("wood", wood) });
            _targets.Add(new JobTarget(key, _world, at, string.Empty, 0, needs));
        }

        internal void Offer(AdoptedChest chest, string item, int units) =>
            _sources.Add(new SourceStock(chest, new[] { new StockLine(item, units) }));

        internal void Complete(string key) => _done.Add(key);

        public IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world) => _targets;

        public IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world) => _sources;

        public StopStatus Observe(in RouteStop stop) =>
            _done.Contains(stop.Key) ? StopStatus.AlreadyDone : StopStatus.Actionable;
    }
}
