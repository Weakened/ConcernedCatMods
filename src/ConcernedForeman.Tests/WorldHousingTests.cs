using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Housing;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>#286's housing survey, against stubbed vanilla.
///
/// <b>Why this file exists.</b> Two rounds of independent review found defects
/// in this adapter — the ownership read, both budgets, the loaded-ground check
/// and the bed key — and no test could have caught any of them, because the
/// product file was not compiled into any test project. Every case below is one
/// of those defects.</summary>
[Collection("vanilla-stubs")]
public sealed class WorldHousingTests : IDisposable
{
    private const long Me = 7001L;
    private const long SomebodyElse = 9002L;

    private readonly List<string> _log = new();

    public WorldHousingTests()
    {
        Reset();
        Game.instance = new Game { Profile = new PlayerProfile { PlayerId = Me } };
    }

    public void Dispose() => Reset();

    private static void Reset()
    {
        Piece.s_allPieces.Clear();
        Cover.Answers.Clear();
        EffectArea.Warm.Clear();
        Game.instance = null;
    }

    private static Designation Area(float radius = 20f) =>
        new Designation(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), radius, null);

    private WorldHousing Survey(bool groundLoaded = true) =>
        new WorldHousing(_ => groundLoaded, _log.Add);

    /// <summary>A bed the way a vanilla prefab is built: `Bed` and `Piece` on one
    /// GameObject, with a live `ZNetView` beside them.</summary>
    private static Bed PlaceBed(
        float x,
        float z,
        float y = 0f,
        bool roof = true,
        float cover = 1f,
        bool warm = true,
        long owner = 0L,
        bool current = false,
        bool valid = true,
        int layer = 0)
    {
        var host = new GameObject("bed");
        host.layer = layer;
        host.transform.position = new Vector3(x, y, z);

        var piece = new Piece();
        host.Add(piece);
        var bed = new Bed { SpawnPoint = new Vector3(x, y, z), Current = current };
        host.Add(bed);
        var view = new ZNetView { Valid = valid };
        host.Add(view);

        if (owner != 0L)
        {
            view.GetZDO().Set(ZDOVars.s_owner, owner);
        }

        Cover.Set(bed.SpawnPoint, cover, roof);
        if (warm)
        {
            EffectArea.Warm.Add(Cover.Key(host.transform.position));
        }

        Piece.s_allPieces.Add(piece);
        return bed;
    }

    /// <summary>A built piece that is not a bed.</summary>
    private static void PlaceWall(float x, float z)
    {
        var host = new GameObject("wall");
        host.transform.position = new Vector3(x, 0f, z);
        var piece = new Piece();
        host.Add(piece);
        Piece.s_allPieces.Add(piece);
    }

    // ---- ownership, three ways, and the one that cannot be answered ---------

    [Fact]
    public void AnUnclaimedBedUnderAGoodRoofByAFireIsFree()
    {
        PlaceBed(1f, 1f);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(1, capacity.Habitable);
        Assert.Equal(0, capacity.Occupied);
    }

    [Fact]
    public void YourCurrentBedIsYoursAndYourOldOnesAreFreeAgain()
    {
        // The severe one: nothing in vanilla ever clears s_owner, so reading it
        // as a plain flag retired every bed the player had ever slept in and
        // pinned capacity at zero for good.
        PlaceBed(1f, 1f, owner: Me, current: true);
        PlaceBed(3f, 1f, owner: Me, current: false);
        PlaceBed(5f, 1f, owner: Me, current: false);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(2, capacity.Habitable);
        Assert.Equal(1, capacity.Occupied);
        Assert.Equal(0, capacity.ClaimedByOthers);
    }

    [Fact]
    public void ABedAnotherPlayerOwnsIsNeitherFreeNorTheSettlementsOccupancy()
    {
        PlaceBed(1f, 1f, owner: SomebodyElse);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(0, capacity.Habitable);
        Assert.Equal(0, capacity.Occupied);
        Assert.Equal(1, capacity.ClaimedByOthers);
    }

    [Fact]
    public void WithNoProfileAnOwnedBedIsUnmeasuredRatherThanSomebodyElses()
    {
        // With no profile to compare against, the old code answered
        // "somebody else already sleeps in it" — in a single-player world, about
        // every bed the player had ever used.
        PlaceBed(1f, 1f, owner: Me, current: true);
        Game.instance!.Profile = null!;

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(1, capacity.Unmeasured);
        Assert.Equal(0, capacity.ClaimedByOthers);
        Assert.DoesNotContain("somebody else", capacity.Describe());
    }

    [Fact]
    public void YourOwnCurrentBedWithNoRoofIsStillToldItNeedsOne()
    {
        PlaceBed(1f, 1f, owner: Me, current: true, roof: false);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(0, capacity.Occupied);
        Assert.Contains("needs a roof", capacity.Describe());
    }

    // ---- vanilla's own bed rules, read through the real call shapes ---------

    [Fact]
    public void TheCoverThresholdIsAskedAtTheSpawnPointNotThePiece()
    {
        PlaceBed(1f, 1f, cover: 0.79f);

        Assert.Contains("too open to the weather", Survey().Measure(Area()).Describe());
    }

    [Fact]
    public void WithoutAFireNearTheBedItIsRefused()
    {
        PlaceBed(1f, 1f, warm: false);

        Assert.Contains("no fire near enough", Survey().Measure(Area()).Describe());
    }

    [Fact]
    public void ABedWhoseNetworkObjectIsNotLiveIsUnmeasuredNotUninhabitable()
    {
        PlaceBed(1f, 1f, valid: false);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(1, capacity.Unmeasured);
        Assert.Equal(0, capacity.Habitable);
    }

    // ---- what the survey collects ------------------------------------------

    [Fact]
    public void TheBuildMenusPlacementPreviewIsNotABed()
    {
        // The hammer's preview is a real Bed on a live GameObject whose ZNetView
        // has been destroyed, and vanilla puts it on the ghost layer.
        // Piece.GetAllPiecesInRadius skips that layer, so the fix is structural.
        PlaceBed(1f, 1f);
        PlaceBed(2f, 2f, layer: Piece.GhostLayer);

        Assert.Equal(1, Survey().Measure(Area()).Beds.Count);
    }

    [Fact]
    public void ABedOutsideTheCircleIsNotListedAtAll()
    {
        PlaceBed(1f, 1f);
        PlaceBed(40f, 0f);

        HousingCapacity capacity = Survey().Measure(Area(radius: 20f));

        Assert.Single(capacity.Beds);
        Assert.DoesNotContain("outside the settlement", capacity.Describe());
    }

    [Fact]
    public void ABedUpstairsIsStillFoundBecauseTheQueryReachesUpward()
    {
        PlaceBed(1f, 1f, y: 12f);

        Assert.Single(Survey().Measure(Area()).Beds);
    }

    [Fact]
    public void ANeighboursLargeBuildOutsideTheCircleCannotCrowdOutYourBeds()
    {
        // The budget used to be charged for every piece the query sphere
        // returned, and that sphere reaches far outside the circle — so a big
        // build next door exhausted it before the walk reached the player's own
        // beds, and the readout said the settlement housed nobody.
        for (int i = 0; i < 5000; i++)
        {
            PlaceWall(30f + (i % 10), 0f);
        }

        PlaceBed(1f, 1f);

        HousingCapacity capacity = Survey().Measure(Area(radius: 20f));

        Assert.Equal(1, capacity.Habitable);
        Assert.False(capacity.Truncated);
        Assert.DoesNotContain("houses nobody", capacity.Describe());
    }

    // ---- the answer names a place, and names it uniquely --------------------

    [Fact]
    public void TwoBedsOnDifferentFloorsAreToldApart()
    {
        // Without the height, a two-storey longhouse produced two identical
        // lines and the player could not tell which floor to fix — in the class
        // whose vertical reach exists to find the upstairs bed.
        PlaceBed(10f, -4f, y: 0f);
        PlaceBed(10f, -4f, y: 6f, roof: false);

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(2, capacity.Beds.Count);
        Assert.NotEqual(capacity.Beds[0].Key, capacity.Beds[1].Key);
        Assert.Contains("height", capacity.Beds[0].Key);
    }

    [Fact]
    public void TheKeyIsAPlaceRatherThanANetworkId()
    {
        PlaceBed(123f, -456f);

        Assert.Contains("the bed at 123, -456", Survey().Measure(Area(radius: 600f)).Describe());
    }

    // ---- failure states the class exists to produce -------------------------

    [Fact]
    public void GroundThatIsNotAllLoadedQualifiesTheCount()
    {
        PlaceBed(1f, 1f);

        string text = Survey(groundLoaded: false).Measure(Area()).Describe();

        Assert.Contains("not loaded", text);
    }

    [Fact]
    public void AnEmptySettlementWithLoadedGroundMaySaySoPlainly()
    {
        Assert.Contains("houses nobody", Survey().Measure(Area()).Describe());
    }

    [Fact]
    public void AnEmptySettlementWithUnloadedGroundMayNot()
    {
        Assert.DoesNotContain("houses nobody", Survey(groundLoaded: false).Measure(Area()).Describe());
    }

    [Fact]
    public void AGroundCheckThatThrowsQualifiesTheCountRatherThanTakingTheCommandDown()
    {
        PlaceBed(1f, 1f);

        var survey = new WorldHousing(_ => throw new InvalidOperationException("no zones"), _log.Add);
        string text = survey.Measure(Area()).Describe();

        Assert.Contains("not loaded", text);
    }

    [Fact]
    public void MoreBedsThanOneCommandProbesIsSaidInTheReadoutAndTheLog()
    {
        for (int i = 0; i < 70; i++)
        {
            PlaceBed(i % 10, i / 10);
        }

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.True(capacity.Truncated);
        Assert.Contains("at least", capacity.Describe());
        Assert.Contains(_log, line => line.Contains("beds are inside the settlement"));
    }

    [Fact]
    public void ExactlyTheBedBudgetIsACompleteSurvey()
    {
        for (int i = 0; i < 64; i++)
        {
            PlaceBed(i % 8, i / 8);
        }

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.Equal(64, capacity.Beds.Count);
        Assert.False(capacity.Truncated);
        Assert.Empty(_log);
    }

    [Fact]
    public void MoreInSettlementPiecesThanOneCommandWalksSaysWhichCapFired()
    {
        for (int i = 0; i < 4100; i++)
        {
            PlaceWall((i % 30) - 15f, ((i / 30) % 30) - 15f);
        }

        HousingCapacity capacity = Survey().Measure(Area());

        Assert.True(capacity.Truncated);
        Assert.DoesNotContain("houses nobody", capacity.Describe());
        Assert.Contains(_log, line => line.Contains("pieces are inside the settlement"));
    }
}
