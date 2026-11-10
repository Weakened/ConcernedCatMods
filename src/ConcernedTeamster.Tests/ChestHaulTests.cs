using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>A manual chest-to-chest haul run tour by tour (#381 item 4):
/// provision, travel, unload, reconcile - and every way it stops with what he
/// holds still on him, never balanced, retried or assumed elsewhere.</summary>
public sealed class ChestHaulTests
{
    private static readonly CollectionPoint Source = new CollectionPoint(0f, 0f, 0f);
    private static readonly CollectionPoint Destination = new CollectionPoint(40f, 0f, 0f);
    private static readonly CollectionTourLimits Limits = CollectionTourLimits.Default(CollectionLimits.Default);

    private static HaulManifest TwoTours() => HaulManifestTests.Planned(
        new HaulCapacity(450f, 32),
        CartCapacity.None,
        new[] { new HaulCargo("Stone", 2f, 50, null, 300), new HaulCargo("Wood", 2f, 50, 30, 100) });

    private static ChestHaul Start(HaulManifest? manifest = null) =>
        new ChestHaul(manifest ?? TwoTours(), Source, Destination, Limits);

    /// <summary>A world that does what the haul asks, measuring exactly what it
    /// was asked for unless told otherwise, and counting him honestly.</summary>
    private sealed class World
    {
        public CollectionPoint At = new CollectionPoint(20f, 0f, 0f);
        public float Now;
        public readonly Dictionary<string, int> Carried = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> InDestination = new Dictionary<string, int>(StringComparer.Ordinal);
        public Func<string, int, (HaulLegEnd, int)> Take = (item, units) => (HaulLegEnd.Moved, units);
        public Func<string, int, (HaulLegEnd, int)> Unload = (item, units) => (HaulLegEnd.Moved, units);
        public readonly List<string> Log = new List<string>();

        public void Step(ChestHaul haul)
        {
            Now += 0.05f;
            HaulInstruction order = haul.Tick(At, Now);
            switch (order.Order)
            {
                case HaulOrder.Walk:
                    At = order.Target;
                    Log.Add("walk " + (order.Target.X < 1f ? "source" : "destination"));
                    break;
                case HaulOrder.Take:
                    (HaulLegEnd took, int units) = Take(order.Item, order.Units);
                    if (took == HaulLegEnd.Moved)
                    {
                        Add(Carried, order.Item, units);
                    }

                    Log.Add("take " + order.Item + " " + order.Units);
                    haul.TakeEnded(took, units, string.Empty);
                    break;
                case HaulOrder.Unload:
                    (HaulLegEnd put, int moved) = Unload(order.Item, order.Units);
                    if (put == HaulLegEnd.Moved)
                    {
                        Add(Carried, order.Item, -moved);
                        Add(InDestination, order.Item, moved);
                    }

                    Log.Add("unload " + order.Item + " " + order.Units);
                    haul.UnloadEnded(put, moved, string.Empty);
                    break;
                case HaulOrder.Reconcile:
                    var counted = new List<KeyValuePair<string, int>>();
                    foreach (KeyValuePair<string, int> entry in Carried)
                    {
                        if (entry.Value != 0)
                        {
                            counted.Add(entry);
                        }
                    }

                    Log.Add("reconcile");
                    haul.Reconciled(counted);
                    break;
            }
        }

        public void RunToEnd(ChestHaul haul, int most = 500)
        {
            for (int step = 0; step < most && !haul.IsOver; step++)
            {
                Step(haul);
            }

            Assert.True(haul.IsOver, "the haul never ended");
        }

        private static void Add(Dictionary<string, int> into, string item, int units) =>
            into[item] = (into.TryGetValue(item, out int had) ? had : 0) + units;
    }

    [Fact]
    public void EachTourIsProvisionTravelUnloadReconcileAndTheWholeManifestArrives()
    {
        ChestHaul haul = Start();
        var world = new World();
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Finished, haul.Phase);
        Assert.Equal(2, haul.ToursCompleted);
        Assert.Equal(300, world.InDestination["Stone"]);
        Assert.Equal(30, world.InDestination["Wood"]);
        Assert.Equal(330, haul.TotalDelivered);
        Assert.Empty(haul.Holding);
        Assert.Equal(new[]
        {
            "walk source", "take Stone 225", "walk destination", "unload Stone 225", "reconcile",
            "walk source", "take Stone 75", "take Wood 30", "walk destination", "unload Stone 75", "unload Wood 30",
            "reconcile",
        }, world.Log.ToArray());
    }

    [Fact]
    public void AnOrderIsIssuedOnceAndThenHeldUntilItIsReported()
    {
        ChestHaul haul = Start();
        haul.Tick(Source, 0f);
        Assert.Equal(ChestHaulPhase.Provisioning, haul.Phase);

        Assert.Equal(HaulOrder.Take, haul.Tick(Source, 0.1f).Order);
        Assert.Equal(HaulOrder.Hold, haul.Tick(Source, 0.2f).Order);
        Assert.Equal(HaulOrder.Hold, haul.Tick(Source, 0.3f).Order);
    }

    [Fact]
    public void WhatIsUnloadedIsWhatWasMeasuredTakingNotWhatWasPlanned()
    {
        ChestHaul haul = Start();
        var world = new World { Take = (item, units) => (HaulLegEnd.Moved, item == "Stone" ? units - 5 : units) };
        world.RunToEnd(haul);

        Assert.Contains("unload Stone 220", world.Log);
        Assert.Contains("unload Stone 70", world.Log);
        Assert.Equal(ChestHaulPhase.Finished, haul.Phase);
        Assert.Equal(290, haul.DeliveredOf("Stone"));
        Assert.Contains("320 of 330 planned unit(s) delivered", haul.Describe());
    }

    [Fact]
    public void AnUncertainTakeStopsAtOnceAndAttemptsNothingMore()
    {
        ChestHaul haul = Start();
        var world = new World { Take = (_, _) => (HaulLegEnd.Uncertain, 0) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("a person decides", haul.Reason);
        Assert.Single(world.Log, entry => entry.StartsWith("take", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Log, entry => entry.StartsWith("unload", StringComparison.Ordinal));
    }

    [Fact]
    public void ATourThatTakesNothingStopsWithoutWalkingToTheDestination()
    {
        ChestHaul haul = Start();
        var world = new World { Take = (_, _) => (HaulLegEnd.Refused, 0) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("nothing could be taken", haul.Reason);
        Assert.DoesNotContain("walk destination", world.Log);
    }

    [Fact]
    public void ARefusedLineIsSkippedAndTheRestOfTheTourStillRuns()
    {
        HaulManifest manifest = HaulManifestTests.Planned(
            new HaulCapacity(450f, 32), CartCapacity.None,
            new[] { new HaulCargo("Wood", 2f, 50, 10, 10), new HaulCargo("Stone", 2f, 50, 10, 10) });
        ChestHaul haul = Start(manifest);
        var world = new World { Take = (item, units) => item == "Wood" ? (HaulLegEnd.Refused, 0) : (HaulLegEnd.Moved, units) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Finished, haul.Phase);
        Assert.Equal(10, world.InDestination["Stone"]);
        Assert.DoesNotContain("unload Wood 10", world.Log);
    }

    [Fact]
    public void AFullDestinationStopsTheHaulAndWhatItDidNotTakeStaysInHim()
    {
        ChestHaul haul = Start();
        var world = new World { Unload = (_, units) => (HaulLegEnd.Moved, units - 25) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("did not take everything", haul.Reason);
        Assert.Single(world.Log.FindAll(entry => entry == "reconcile"));
        Assert.Equal(new[] { new HaulLoad("Stone", 25) }, haul.Holding);
        Assert.Equal(25, world.Carried["Stone"]);
        Assert.Equal(haul.TakenOf("Stone"), haul.DeliveredOf("Stone") + world.Carried["Stone"]);
    }

    [Fact]
    public void AnUncertainUnloadStopsAtOnce()
    {
        ChestHaul haul = Start();
        var world = new World { Unload = (_, _) => (HaulLegEnd.Uncertain, 0) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("went in could not be established", haul.Reason);
        Assert.DoesNotContain("reconcile", world.Log);
    }

    [Fact]
    public void MoreArrivingThanHeCarriedForTheHaulIsNeverBalanced()
    {
        ChestHaul haul = Start();
        var world = new World { Unload = (_, units) => (HaulLegEnd.Moved, units + 1) };
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("a person decides", haul.Reason);
        Assert.Equal(0, haul.DeliveredOf("Stone"));
    }

    [Fact]
    public void AReconcileThatDoesNotAgreeOrCannotCountStopsTheHaul()
    {
        ChestHaul haul = Start();
        var world = new World();
        while (haul.Phase != ChestHaulPhase.Reconciling)
        {
            world.Step(haul);
        }

        Assert.Equal(HaulOrder.Reconcile, haul.Tick(Destination, 99f).Order);
        haul.Reconciled(new[] { new KeyValuePair<string, int>("Stone", 3) });
        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("3 Stone", haul.Reason);

        ChestHaul blind = Start();
        var other = new World();
        while (blind.Phase != ChestHaulPhase.Reconciling)
        {
            other.Step(blind);
        }

        blind.Tick(Destination, 99f);
        blind.Reconciled(null);
        Assert.Equal(ChestHaulPhase.Stopped, blind.Phase);
        Assert.Contains("could not be counted", blind.Reason);
    }

    [Fact]
    public void SomethingElseOfTheCargoAppearingOnHimStopsTheHaul()
    {
        // He began holding none of the cargo, so wood nobody measured is a move
        // nobody measured.
        ChestHaul haul = Start();
        var world = new World();
        world.Carried["Wood"] = 4;
        world.RunToEnd(haul);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Contains("4 Wood", haul.Reason);
    }

    [Fact]
    public void AWalkThatStopsGettingCloserGivesUpWithAReasonAndNoTeleport()
    {
        ChestHaul haul = Start();
        var stuck = new CollectionPoint(20f, 0f, 0f);
        HaulInstruction first = haul.Tick(stuck, 0f);
        Assert.Equal(HaulOrder.Walk, first.Order);
        Assert.Equal(Source, first.Target);

        haul.Tick(stuck, Limits.StallSeconds - 0.1f);
        Assert.False(haul.IsOver);
        haul.Tick(stuck, Limits.StallSeconds + 0.1f);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.StartsWith("NEEDS ATTENTION", haul.Reason);
    }

    [Fact]
    public void CancelStopsAndLateReportsChangeNothing()
    {
        ChestHaul haul = Start();
        haul.Tick(Source, 0f);
        haul.Tick(Source, 0.1f);
        haul.Cancel("you stopped it");
        haul.TakeEnded(HaulLegEnd.Moved, 225, string.Empty);

        Assert.Equal(ChestHaulPhase.Stopped, haul.Phase);
        Assert.Equal("you stopped it", haul.Reason);
        Assert.Equal(0, haul.TakenOf("Stone"));
        Assert.Equal(HaulOrder.None, haul.Tick(Source, 1f).Order);
    }

    [Fact]
    public void AReportWithNoOrderOutstandingIsIgnored()
    {
        ChestHaul haul = Start();
        haul.Tick(Source, 0f);
        haul.TakeEnded(HaulLegEnd.Moved, 225, string.Empty);
        haul.UnloadEnded(HaulLegEnd.Moved, 225, string.Empty);
        haul.Reconciled(Array.Empty<KeyValuePair<string, int>>());

        Assert.Equal(ChestHaulPhase.Provisioning, haul.Phase);
        Assert.Equal(0, haul.TakenOf("Stone"));
    }

    [Fact]
    public void AnUnplannedManifestIsNeverRun()
    {
        HaulManifest refused = HaulManifestTests.Planned(new HaulCapacity(450f, 32), CartCapacity.None, Array.Empty<HaulCargo>());
        Assert.Throws<ArgumentException>(() => new ChestHaul(refused, Source, Destination, Limits));
    }
}
