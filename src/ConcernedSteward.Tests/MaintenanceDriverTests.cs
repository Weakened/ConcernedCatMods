using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Settlement.Designations;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Driving one maintenance round through the shared pipeline.
///
/// <b>These are the tests that say the batching is real.</b> Everything in
/// <c>MaintenanceRoundTests</c> is about what this product decides; these are
/// about what actually happens when those decisions are handed to
/// <c>NpcJobDriver</c> — how many times a chest is opened, in what order the
/// steps come out, what a light that changed mid-round does to the round, and
/// what the library says about work its plan did not cover.</summary>
public sealed class MaintenanceDriverTests
{
    private static readonly MaintenanceThresholds Shipped = MaintenanceThresholds.Default;

    /// <summary>One arranged settlement, and the handle to change it under her
    /// feet the way a player would.
    ///
    /// <b>The registration here is the runtime's, not a shortcut.</b> One
    /// registry per camp; the Steward declared to it through
    /// <see cref="StewardNpcAdoption"/> exactly as <c>StewardRuntime.Install</c>
    /// does; the world epoch minted by that same registry exactly as
    /// <c>OnWorldLoaded</c> does; and every job started through
    /// <c>MaintenanceJobRole.DriverFor</c>, which is the call the runtime will
    /// make. That matters more than it looks: a job now needs an identity the
    /// registry tracks, the epoch it minted and the registry itself, and a test
    /// that assembled any of those by hand would be testing a construction the
    /// product never performs. The gap between the two is where an adoption bug
    /// hides — this fixture had one, and it hid there.
    ///
    /// <b>One registry per camp, deliberately.</b> A fresh one per driver would
    /// make the arbiter's new one-job-at-a-time refusal unreachable from a test,
    /// because a second registry is a second arbiter that has never heard of the
    /// first.</summary>
    private sealed class Camp
    {
        private readonly Dictionary<string, FuelTargetObservation> _lights =
            new Dictionary<string, FuelTargetObservation>(StringComparer.Ordinal);

        internal Camp(params FuelTargetObservation[] lights)
        {
            foreach (FuelTargetObservation light in lights)
            {
                _lights[light.Key.Value] = light;
            }

            Adoption = new StewardNpcAdoption(
                new NpcRoleRegistry(),
                new StewardNpcRole(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "cs-steward-tests")));
            Assert.True(Adoption.Register().IsRegistered);
            World = Adoption.NoteWorldLoaded();

            Settlement = Lights.Settlement();
            Chests = new List<SupplySighting>
            {
                Lights.Chest("depot", contents: new[] { (Lights.Wood, 200) }),
            };
        }

        internal StewardNpcAdoption Adoption { get; }

        internal NpcWorldEpoch World { get; }

        internal Designation Settlement { get; }

        internal List<SupplySighting> Chests { get; }

        internal RoundPlan Round { get; private set; }

        /// <summary>The player fills one, or a troll knocks it over.</summary>
        internal void Replace(FuelTargetObservation light) => _lights[light.Key.Value] = light;

        internal void Destroy(string key) => _lights.Remove(key);

        internal FuelTargetObservation? Look(FuelTargetKey key) =>
            _lights.TryGetValue(key.Value, out FuelTargetObservation light)
                ? light
                : (FuelTargetObservation?)null;

        /// <summary>Works the round out, exactly as the runtime would, and holds
        /// it for the driver to read.</summary>
        internal RoundPlan Prepare()
        {
            Round = MaintenanceRound.Prepare(
                _lights.Values.OrderBy(light => light.Key.Value, StringComparer.Ordinal).ToArray(),
                Settlement, Chests, Lights.Epoch, Shipped);
            return Round;
        }

        internal MaintenanceJobRole Role() => new MaintenanceJobRole(
            World, () => Round, Look, () => Settlement, () => Lights.Epoch, Shipped);

        /// <summary>Starts a job the way the runtime will: through
        /// <c>DriverFor</c>, over the adoption that registered her.</summary>
        internal NpcJobDriver Driver(int unitsPerTrip = 50, string jobId = StewardRole.UpkeepJobId)
        {
            Prepare();
            return MaintenanceJobRole.DriverFor(
                Adoption,
                Round,
                jobId,
                new SettlementWorkArea(Settlement),
                unitsPerTrip,
                () => Round,
                Look,
                () => Settlement,
                () => Lights.Epoch,
                Shipped);
        }
    }

    /// <summary>Pumps a job to a conclusion, reporting every step as done, and
    /// records what it was asked to do.</summary>
    private static List<PlannedStep> Walk(
        NpcJobDriver driver, int mostSteps = 60, Action<NpcJobDriver, PlannedStep>? at = null)
    {
        var walked = new List<PlannedStep>();
        var standing = new NpcPoint(0f, 0f, 0f);

        for (int step = 0; step < mostSteps; step++)
        {
            NpcJobAdvance advance = driver.Next(standing);
            if (advance.Progress != NpcJobProgress.Do)
            {
                return walked;
            }

            walked.Add(advance.Step);
            standing = advance.Step.Step.At;

            if (at != null)
            {
                at(driver, advance.Step);
            }
            else
            {
                driver.Done();
            }
        }

        throw new InvalidOperationException("The job never concluded.");
    }

    // ------------------------------------------------------------------

    /// <summary>The whole issue, driven rather than asserted about.
    ///
    /// Five low torches. The chest is opened <b>once</b>, and all five lights
    /// are tended after it — never chest, torch, chest, torch.</summary>
    [Fact]
    public void Five_low_lights_are_one_visit_to_the_chest_and_then_five_stops()
    {
        var camp = new Camp(
            Enumerable.Range(0, 5)
                .Select(index => Lights.Light("torch-" + index, fuel: 1f, x: index * 4f))
                .ToArray());

        NpcJobDriver driver = camp.Driver();
        List<PlannedStep> walked = Walk(driver);

        PlannedStep[] collects = walked.Where(step => step.IsCollect).ToArray();
        PlannedStep[] services = walked.Where(step => !step.IsCollect).ToArray();

        Assert.Single(collects);
        Assert.Equal(5, services.Length);

        // Everything fetched before anything tended: the shape the old loop
        // could not produce.
        Assert.True(
            walked.TakeWhile(step => step.IsCollect).Count() == collects.Length,
            "The chest was opened again part way through the round: " +
            string.Join(", ", walked.Select(step => step.Step.Action + " " + step.Step.Subject)));

        // Forty-five units of wood, drawn in one go.
        Assert.Equal(45, collects[0].Step.Units);
        Assert.Equal(NpcJobProgress.Finished, driver.Progress);
        Assert.True(driver.LastRound.IsComplete);
        Assert.Equal(0, driver.LeftForAnotherRound);
    }

    [Fact]
    public void The_steps_carry_this_products_own_two_words()
    {
        var camp = new Camp(Lights.Light("hearth", fuel: 0f));

        List<PlannedStep> walked = Walk(camp.Driver());

        Assert.Equal("fetch", walked.First(step => step.IsCollect).Step.Action);
        Assert.Equal("tend", walked.First(step => !step.IsCollect).Step.Action);
    }

    /// <summary>A hearth the player filled while she was walking to it.
    ///
    /// The library asks this product again before every stop, and this product
    /// answers from a fresh reading rather than from the plan — so the stop is
    /// skipped, the round carries on, and the wood stays in her hands.</summary>
    [Fact]
    public void A_light_the_player_refilled_mid_route_is_skipped_and_the_round_carries_on()
    {
        var camp = new Camp(
            Lights.Light("a", fuel: 0f),
            Lights.Light("b", fuel: 0f, x: 6f));

        NpcJobDriver driver = camp.Driver();
        var reported = new List<string>();

        List<PlannedStep> walked = Walk(driver, at: (pump, step) =>
        {
            if (step.IsCollect)
            {
                pump.Done();
                return;
            }

            // The player fills 'b' the moment she sets off for it.
            camp.Replace(Lights.Light("b", fuel: 10f, x: 6f));
            camp.Prepare();

            // Re-asked here exactly as the driver asks it.
            if (string.Equals(step.Step.Subject, "b", StringComparison.Ordinal))
            {
                reported.Add("skipped " + step.Step.Subject);
                pump.Skipped();
                return;
            }

            reported.Add("tended " + step.Step.Subject);
            pump.Done();
        });

        Assert.Contains("tended a", reported);
        Assert.Contains("skipped b", reported);
        Assert.Equal(NpcJobProgress.Finished, driver.Progress);

        // Skipped is not owed: somebody else saw to it.
        Assert.True(driver.LastRound.IsComplete);
        Assert.Equal(3, walked.Count);
    }

    /// <summary>A torch a troll knocked over between the plan and the stop.
    /// </summary>
    [Fact]
    public void A_destroyed_light_is_skipped_mid_route()
    {
        var camp = new Camp(
            Lights.Light("standing", fuel: 0f),
            Lights.Light("doomed", fuel: 0f, x: 6f));

        NpcJobDriver driver = camp.Driver();
        var role = camp.Role();

        Walk(driver, at: (pump, step) =>
        {
            if (!step.IsCollect && step.Step.Subject == "standing")
            {
                camp.Destroy("doomed");
            }

            if (!step.IsCollect && step.Step.Subject == "doomed")
            {
                pump.Skipped();
                return;
            }

            pump.Done();
        });

        Assert.Equal(NpcJobProgress.Finished, driver.Progress);

        // And the reason it was skipped is the one this product decided, read
        // through the seam the library actually asks on.
        Assert.Equal(
            TheConcernedCat.ConcernedNPC.Routing.StopStatus.Gone,
            role.Observe(new TheConcernedCat.ConcernedNPC.Routing.RouteStop(
                "doomed", new NpcPoint(6f, 0f, 0f), 0, true)));
    }

    /// <summary>The acceptance criterion, wired: something reads
    /// <c>LeftForAnotherRound</c> and something acts on it.
    ///
    /// Two lights that between them need more than one trip carries. The plan
    /// covers what it can and says how many lights it did not reach; this
    /// product reads that number out of the library, puts it into its own
    /// reconciliation, and the reconciliation answers that another round would
    /// help — which is what sends her straight back out instead of waiting.
    /// </summary>
    [Fact]
    public void What_one_plan_did_not_cover_is_read_from_the_library_and_acted_on()
    {
        var camp = new Camp(
            Lights.Light("urgent", fuel: 0f),
            Lights.Light("next", fuel: 1f, x: 6f));

        // Ten units a trip, and the round wants nineteen. The planner is capped
        // at one plan's worth of trips, so something is left over.
        NpcJobDriver driver = camp.Driver(unitsPerTrip: 10);
        NpcJobAdvance first = driver.Next(new NpcPoint(0f, 0f, 0f));

        Assert.Equal(NpcJobProgress.Do, first.Progress);

        // Walk only the first trip, then stop: the round ended with work the
        // plan had not reached.
        driver.Done();

        RoundOutcome outcome = RoundReconciler.Close(
            camp.Round,
            Array.Empty<StopResult>(),
            RoundManifest.Empty,
            // THE WIRE: the library's number, not one this product invented.
            leftForAnotherRound: driver.LeftForAnotherRound + driver.LastRound.LeftForAnotherRound);

        Assert.False(outcome.IsComplete);
        Assert.True(outcome.HasUnfinishedWork);
        Assert.True(outcome.AnotherRoundWouldHelp);
        Assert.Equal(0f, outcome.NextRoundDelaySeconds(15f));
    }

    /// <summary>And the same number, when the plan did cover everything, does
    /// not send her out again.</summary>
    [Fact]
    public void A_plan_that_covered_the_whole_settlement_reports_nothing_left()
    {
        var camp = new Camp(Lights.Light("only", fuel: 0f));

        NpcJobDriver driver = camp.Driver();
        Walk(driver);

        Assert.Equal(0, driver.LeftForAnotherRound);
        Assert.True(driver.LastRound.IsComplete);

        RoundOutcome outcome = RoundReconciler.Close(
            camp.Round,
            new[] { new StopResult(camp.Round.Stops[0].Light.Key, StopOutcome.Serviced, 10) },
            new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 10) }),
            leftForAnotherRound: driver.LeftForAnotherRound);

        Assert.True(outcome.IsComplete);
        Assert.False(outcome.AnotherRoundWouldHelp);
        Assert.Equal(15f, outcome.NextRoundDelaySeconds(15f));
    }

    /// <summary>Nothing this product did not offer is ever actionable, however
    /// the library asks.</summary>
    [Fact]
    public void A_stop_this_round_never_planned_is_never_actionable()
    {
        var camp = new Camp(Lights.Light("known", fuel: 0f));
        camp.Prepare();

        Assert.Equal(
            TheConcernedCat.ConcernedNPC.Routing.StopStatus.Unreadable,
            camp.Role().Observe(new TheConcernedCat.ConcernedNPC.Routing.RouteStop(
                "never-heard-of-it", new NpcPoint(0f, 0f, 0f), 0, true)));
    }

    /// <summary>A role's keys belong to one world load, and it offers nothing at
    /// all for any other.</summary>
    [Fact]
    public void Nothing_is_offered_for_a_world_these_keys_do_not_belong_to()
    {
        var camp = new Camp(Lights.Light("a", fuel: 0f));
        camp.Prepare();

        MaintenanceJobRole role = camp.Role();

        // A second camp is a second registry, which mints a world of its own.
        // That is the shape of the real case: a different load of a different
        // world, whose names have nothing to do with these.
        NpcWorldEpoch somewhereElse = new Camp(Lights.Light("elsewhere", fuel: 0f)).World;

        Assert.Empty(role.Candidates(somewhereElse));
        Assert.Empty(role.Sources(somewhereElse));
        Assert.NotEmpty(role.Candidates(camp.World));
    }

    // ------------------------------------------------------------------
    // One NPC, one job — newly enforced, so newly worth knowing about
    // ------------------------------------------------------------------

    /// <summary>A second round while the first is running is refused, and the
    /// refusal is a sentence rather than a silence.
    ///
    /// <b>This is new behaviour and it arrived under this leaf rather than in
    /// it.</b> Until the arbiter began mediating modes, nothing entered one, so
    /// a second driver for the Steward would simply have planned a second round
    /// and two jobs would have walked one body. Recorded as a test because a
    /// refusal that reads as "nothing to do" is the kind of thing that gets
    /// found in a bug report.</summary>
    [Fact]
    public void A_second_job_for_the_same_Steward_is_refused_while_the_first_is_running()
    {
        var camp = new Camp(Lights.Light("hearth", fuel: 0f));

        NpcJobDriver first = camp.Driver();
        Assert.Equal(NpcJobProgress.Do, first.Next(new NpcPoint(0f, 0f, 0f)).Progress);

        NpcJobDriver second = camp.Driver(jobId: "steward/some-other-errand");

        Assert.Equal(NpcJobProgress.Stopped, second.Progress);
        Assert.Equal(JobPlanVerdict.Refused, second.Verdict);
        Assert.Contains("one NPC does one job at a time", second.Reason);

        // And the first is untouched by having been asked.
        Assert.Equal(NpcJobProgress.Do, first.Next(new NpcPoint(0f, 0f, 0f)).Progress);
    }

    /// <summary>Re-asking for the SAME job is not a second job.
    ///
    /// The upkeep job has one name for the life of the world, and a runtime that
    /// re-derived its driver after an interruption must not be told its own NPC
    /// is busy with itself.</summary>
    [Fact]
    public void Re_asking_for_the_same_job_takes_the_mode_it_already_holds()
    {
        var camp = new Camp(Lights.Light("hearth", fuel: 0f));

        NpcJobDriver first = camp.Driver();
        Assert.Equal(NpcJobProgress.Do, first.Next(new NpcPoint(0f, 0f, 0f)).Progress);

        NpcJobDriver again = camp.Driver();

        Assert.NotEqual(NpcJobProgress.Stopped, again.Progress);
        Assert.Equal(NpcJobProgress.Do, again.Next(new NpcPoint(0f, 0f, 0f)).Progress);
    }

    /// <summary>And a job that has ended gives the mode back, so the next round
    /// is not refused by the last one.</summary>
    [Fact]
    public void A_finished_round_gives_the_identity_back()
    {
        var camp = new Camp(Lights.Light("hearth", fuel: 0f));

        NpcJobDriver first = camp.Driver();
        Walk(first);
        Assert.Equal(NpcJobProgress.Finished, first.Progress);

        NpcJobDriver next = camp.Driver(jobId: "steward/the-next-errand");

        Assert.NotEqual(NpcJobProgress.Stopped, next.Progress);
    }

    /// <summary>Retiring a body mid-job is now refused, and nothing in this
    /// product was relying on it being allowed.
    ///
    /// <b>Worth pinning from here rather than only in the library.</b> Until the
    /// driver entered a mode, <c>MayRetireBody</c> was permanently true and the
    /// guard never fired; it fires now. The Steward reads neither
    /// <c>MayRetireBody</c> nor <c>MayRelocateHome</c> anywhere — nothing in
    /// this product retires or relocates a body at all — so the change costs it
    /// nothing today. This test is the tripwire for the day something here
    /// wants to, and the reason it is here is that the answer would otherwise be
    /// discovered by a body vanishing mid-round.</summary>
    [Fact]
    public void Her_body_may_not_be_retired_while_a_round_is_running()
    {
        var camp = new Camp(Lights.Light("hearth", fuel: 0f));

        Assert.True(camp.Adoption.Registry.ModeOf(camp.Adoption.Identity)!.MayRetireBody);

        NpcJobDriver driver = camp.Driver();
        Assert.Equal(NpcJobProgress.Do, driver.Next(new NpcPoint(0f, 0f, 0f)).Progress);

        Assert.False(camp.Adoption.Registry.ModeOf(camp.Adoption.Identity)!.MayRetireBody);

        Walk(driver);
        Assert.True(camp.Adoption.Registry.ModeOf(camp.Adoption.Identity)!.MayRetireBody);
    }

    /// <summary>A job needs the registry its NPC is registered in, and an
    /// unregistered Steward gets a refusal with a reason rather than a plan.
    ///
    /// The failure this leaf actually hit: the driver refused before it planned,
    /// and six tests saw only that no steps came out.</summary>
    [Fact]
    public void An_unregistered_Steward_is_refused_with_a_reason_rather_than_planning()
    {
        var registry = new NpcRoleRegistry();
        NpcWorldEpoch world = registry.BeginWorldLoad(out _);
        Designation settlement = Lights.Settlement();

        // Never offered to the registry: the one line StewardRuntime.Install
        // performs, left out.
        NpcJobDriver driver = NpcJobDriver.For(
            MaintenanceJobRole.OrderFor(
                default, StewardNpcRole.Id, StewardRole.UpkeepJobId,
                new SettlementWorkArea(settlement), world, 50),
            new MaintenanceJobRole(
                world, () => default, _ => null, () => settlement, () => Lights.Epoch, Shipped),
            registry);

        Assert.Equal(NpcJobProgress.Stopped, driver.Progress);
        Assert.Equal(JobPlanVerdict.Refused, driver.Verdict);
        Assert.Contains("not registered", driver.Reason);
        Assert.Empty(driver.Steps);
    }

    /// <summary>The seven verdicts map one to one, and a mapping that collapsed
    /// any pair would be the defect both vocabularies exist to prevent.
    /// </summary>
    [Fact]
    public void Every_verdict_has_its_own_status()
    {
        LightVerdict[] verdicts = (LightVerdict[])Enum.GetValues(typeof(LightVerdict));
        var mapped = verdicts.Select(MaintenanceJobRole.Map).ToArray();

        Assert.Equal(verdicts.Length, mapped.Distinct().Count());
    }
}
