using System;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The start gate of a manual chest-to-chest haul (#381 item 4): Gunnar,
/// the switches and what else is going on. The two containers and the filter are
/// <c>HaulRequestGate</c>'s (<c>GunnarCollectionAccountTests</c>,
/// <c>HaulManifestTests</c>); each move's own gate and the permit mint are D20's
/// and D15's and are asked again at every take and every unload.</summary>
public sealed class ChestHaulGateTests
{
    private static ChestHaulRequest Ready(
        bool collectionEnabled = true,
        bool takeEnabled = true,
        bool haulEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerPresent = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool somethingInFlight = false,
        bool interruptedTourOpen = false,
        bool cartAssigned = false,
        bool carriedReadable = true,
        bool carryingCargo = false) =>
        new ChestHaulRequest(
            collectionEnabled, takeEnabled, haulEnabled, worldIsUp, authority, seamAvailable, workerPresent,
            workerRecordUnwritable, workerRecordUnreadable, somethingInFlight, interruptedTourOpen, cartAssigned,
            carriedReadable, carryingCargo);

    [Fact]
    public void AReadyRequestIsAdmitted() => Assert.Equal(ChestHaulRefusal.None, ChestHaulGate.Evaluate(Ready()));

    [Fact]
    public void ADefaultedRequestIsRefused() =>
        Assert.Equal(ChestHaulRefusal.CollectionOff, ChestHaulGate.Evaluate(default));

    [Fact]
    public void TheHaulIsOffByDefaultAndASwitchOfItsOwn()
    {
        Assert.False(GunnarCollectionDefaults.ChestHaulEnabled);
        Assert.Equal(ChestHaulRefusal.HaulOff, ChestHaulGate.Evaluate(Ready(haulEnabled: false)));
        Assert.Equal(ChestHaulRefusal.TakeOff, ChestHaulGate.Evaluate(Ready(takeEnabled: false)));
        Assert.Equal(ChestHaulRefusal.CollectionOff, ChestHaulGate.Evaluate(Ready(collectionEnabled: false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AnythingButGrantedAuthorityRefuses(int verdict)
    {
        Assert.NotEqual(WorkAuthorityVerdict.Granted, (WorkAuthorityVerdict)verdict);
        Assert.Equal(ChestHaulRefusal.WorkRefused, ChestHaulGate.Evaluate(Ready(authority: (WorkAuthorityVerdict)verdict)));
    }

    [Fact]
    public void EachFactRefusesOnItsOwn()
    {
        Assert.Equal(ChestHaulRefusal.WorldIsGoingAway, ChestHaulGate.Evaluate(Ready(worldIsUp: false)));
        Assert.Equal(ChestHaulRefusal.SeamUnavailable, ChestHaulGate.Evaluate(Ready(seamAvailable: false)));
        Assert.Equal(ChestHaulRefusal.WorkerRecordUnwritable, ChestHaulGate.Evaluate(Ready(workerRecordUnwritable: true)));
        Assert.Equal(ChestHaulRefusal.WorkerRecordUnreadable, ChestHaulGate.Evaluate(Ready(workerRecordUnreadable: true)));
        Assert.Equal(ChestHaulRefusal.NoWorker, ChestHaulGate.Evaluate(Ready(workerPresent: false)));
        Assert.Equal(ChestHaulRefusal.AlreadyWorking, ChestHaulGate.Evaluate(Ready(somethingInFlight: true)));
        Assert.Equal(ChestHaulRefusal.InterruptedTourOpen, ChestHaulGate.Evaluate(Ready(interruptedTourOpen: true)));
        Assert.Equal(ChestHaulRefusal.CartAssigned, ChestHaulGate.Evaluate(Ready(cartAssigned: true)));
        Assert.Equal(ChestHaulRefusal.CarriedUnreadable, ChestHaulGate.Evaluate(Ready(carriedReadable: false)));
        Assert.Equal(ChestHaulRefusal.AlreadyCarryingCargo, ChestHaulGate.Evaluate(Ready(carryingCargo: true)));
    }

    [Fact]
    public void TheSwitchesAreAskedBeforeAnythingAboutTheWorld() =>
        Assert.Equal(ChestHaulRefusal.HaulOff, ChestHaulGate.Evaluate(Ready(
            haulEnabled: false, worldIsUp: false, authority: WorkAuthorityVerdict.NotHost, cartAssigned: true)));

    [Fact]
    public void EveryRefusalHasASentence()
    {
        foreach (ChestHaulRefusal refusal in Enum.GetValues(typeof(ChestHaulRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(ChestHaulGate.Describe(refusal, WorkAuthorityVerdict.NotHost)));
        }
    }
}
