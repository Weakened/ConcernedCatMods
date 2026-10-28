using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>#419: living beside TheGreatestMap, HUDCompass and Round
/// Minimap. The seams are pure rules (detection, who may delete a marker,
/// where the ink sits, when the minimap ink is held off) plus a few source
/// pins on the game-facing wiring that a domain test cannot reach.</summary>
public class NeighborMapCompatibilityTests
{
    // ------------------------------------------------------------ detection

    [Fact]
    public void EachVerifiedGuidIsRecognisedExactly()
    {
        NeighborMapModPresence presence = NeighborMapMods.Detect(new[]
        {
            "DeathMonger.TheGreatestMap",
            "neobotics.valheim_mod.hudcompass",
            "ithilias.roundminimap",
        });

        Assert.True(presence.TheGreatestMap);
        Assert.True(presence.HudCompass);
        Assert.True(presence.RoundMinimap);
        Assert.True(presence.Any);
    }

    [Fact]
    public void GuidCaseDoesNotMatterButSpellingDoes()
    {
        Assert.True(NeighborMapMods.Detect(new[] { "deathmonger.thegreatestmap" }).TheGreatestMap);
        Assert.True(NeighborMapMods.Detect(new[] { " Ithilias.RoundMinimap " }).RoundMinimap);

        NeighborMapModPresence lookalikes = NeighborMapMods.Detect(new[]
        {
            "DeathMonger.TheGreatestMapAddon",
            "TheGreatestMap",
            "DeathMonger.TheGreatestPortal",
            "neobotics.valheim_mod.hudcompass2",
            "someone.roundminimap",
            "",
        });

        Assert.False(lookalikes.TheGreatestMap);
        Assert.False(lookalikes.HudCompass);
        Assert.False(lookalikes.RoundMinimap);
        Assert.False(lookalikes.Any);
    }

    [Fact]
    public void NothingLoadedMeansNoNeighbourBehaviour()
    {
        foreach (NeighborMapModPresence presence in new[]
        {
            NeighborMapMods.Detect(null),
            NeighborMapMods.Detect(Array.Empty<string>()),
            NeighborMapMods.Detect(new string[] { null! }),
        })
        {
            Assert.False(presence.Any);
            Assert.False(presence.OwnsVanillaPinFlow);
            Assert.False(presence.RemovesForeignPinsWithoutPointing);
            Assert.False(presence.ReshapesSmallMap);
        }
    }

    [Fact]
    public void BehaviourFlagsFollowWhatEachModWasVerifiedToDo()
    {
        NeighborMapModPresence greatestMap = NeighborMapMods.Detect(new[] { NeighborMapMods.TheGreatestMapGuid });
        Assert.True(greatestMap.OwnsVanillaPinFlow);
        Assert.True(greatestMap.RemovesForeignPinsWithoutPointing);
        Assert.False(greatestMap.ReshapesSmallMap);

        // HUDCompass draws its own markers and never removes or adopts ours.
        NeighborMapModPresence hudCompass = NeighborMapMods.Detect(new[] { NeighborMapMods.HudCompassGuid });
        Assert.False(hudCompass.OwnsVanillaPinFlow);
        Assert.False(hudCompass.RemovesForeignPinsWithoutPointing);
        Assert.False(hudCompass.ReshapesSmallMap);

        NeighborMapModPresence roundMinimap = NeighborMapMods.Detect(new[] { NeighborMapMods.RoundMinimapGuid });
        Assert.True(roundMinimap.ReshapesSmallMap);
        Assert.False(roundMinimap.OwnsVanillaPinFlow);
        Assert.False(roundMinimap.RemovesForeignPinsWithoutPointing);
    }

    // --------------------------------------------------- who may delete

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void WithoutAPinClearingNeighbourTheRuleIsUnchanged(bool explicitDelete, bool bound, bool deleted)
    {
        foreach (bool pointer in new[] { true, false })
        {
            Assert.Equal(
                PinTombstoneRule.Decide(explicitDelete, bound, deleted),
                PinTombstoneRule.Decide(explicitDelete, bound, deleted, pointer, neighbourRemovesPinsWithoutPointing: false));
        }
    }

    [Fact]
    public void ThePlayerPointingAtAMarkerStillDeletesItBesideTheGreatestMap()
    {
        Assert.Equal(
            PinTombstoneRule.Verdict.Tombstone,
            PinTombstoneRule.Decide(
                explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: false,
                pointerInitiated: true, neighbourRemovesPinsWithoutPointing: true));
    }

    [Fact]
    public void AMarkerClearedByTheNeighbourWithoutPointingIsKeptAndRedrawn()
    {
        // tgm_clearlocal / tgm_import remove every hand-placeable pin that is
        // not The Greatest Map's own, Cartographer's markers included.
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(
                explicitVanillaDelete: true, sessionBound: true, alreadyDeleted: false,
                pointerInitiated: false, neighbourRemovesPinsWithoutPointing: true));
    }

    [Fact]
    public void PointingNeverOverridesTheBoundSessionOrExactlyOnceGuards()
    {
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(true, sessionBound: false, alreadyDeleted: false, pointerInitiated: true, neighbourRemovesPinsWithoutPointing: true));
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(true, sessionBound: true, alreadyDeleted: true, pointerInitiated: true, neighbourRemovesPinsWithoutPointing: true));
        Assert.Equal(
            PinTombstoneRule.Verdict.KeepAndRebind,
            PinTombstoneRule.Decide(false, sessionBound: true, alreadyDeleted: false, pointerInitiated: true, neighbourRemovesPinsWithoutPointing: true));
    }

    // ------------------------------------------------- where the ink sits

    [Fact]
    public void BelowPinsIsTheShippedPlacementWhateverTheHierarchy()
    {
        foreach (bool pinRoot in new[] { true, false })
        {
            foreach (bool inside in new[] { true, false })
            {
                Assert.Equal(
                    RoadLayerParent.MapImage,
                    RoadLayerPlacement.Resolve(RoadLayerPosition.BelowPins, pinRoot, inside));
            }
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void AbovePinsNeedsAPinRootInsideTheLargeMapAndFallsBackOtherwise(
        bool pinRootAvailable, bool parentInsideLargeMap, bool expectAfterPinRoot)
    {
        Assert.Equal(
            expectAfterPinRoot ? RoadLayerParent.AfterPinRoot : RoadLayerParent.MapImage,
            RoadLayerPlacement.Resolve(RoadLayerPosition.AbovePins, pinRootAvailable, parentInsideLargeMap));
    }

    [Fact]
    public void TheDefaultSettingIsTheShippedPlacement()
    {
        Assert.Equal(RoadLayerPosition.BelowPins, default(RoadLayerPosition));
        Assert.Equal(ReshapedMinimapRoads.Hide, default(ReshapedMinimapRoads));
    }

    [Theory]
    [InlineData(2f, 1f, 2f)]
    [InlineData(1.5f, 0.5f, 3f)]
    [InlineData(-1f, 1f, -1f)]
    public void FrameScaleIsTheRatioOfWorldScales(float image, float parent, float expected)
    {
        Assert.Equal(expected, RoadLayerPlacement.FrameScale(image, parent), 5);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(1f, 1e-9f)]
    [InlineData(1f, float.NaN)]
    [InlineData(1f, float.PositiveInfinity)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.NegativeInfinity, 1f)]
    public void ADegenerateScaleNeverProducesAnInfiniteFrame(float image, float parent)
    {
        Assert.Equal(1f, RoadLayerPlacement.FrameScale(image, parent));
    }

    // --------------------------------------- the reshaped minimap

    [Fact]
    public void InkIsHeldOffOnlyWhileAnInstalledReshaperIsReshapingTheMinimap()
    {
        Assert.True(MinimapReshapeRule.SuppressTexture(
            reshaperInstalled: true, reshapeActive: true, ReshapedMinimapRoads.Hide, largeMapOpen: false));

        Assert.False(MinimapReshapeRule.SuppressTexture(
            reshaperInstalled: false, reshapeActive: true, ReshapedMinimapRoads.Hide, largeMapOpen: false));
        Assert.False(MinimapReshapeRule.SuppressTexture(
            reshaperInstalled: true, reshapeActive: false, ReshapedMinimapRoads.Hide, largeMapOpen: false));
    }

    [Fact]
    public void TheLargeMapAndThePlayersShowChoiceAreNeverHeld()
    {
        Assert.False(MinimapReshapeRule.SuppressTexture(
            reshaperInstalled: true, reshapeActive: true, ReshapedMinimapRoads.Hide, largeMapOpen: true));
        Assert.False(MinimapReshapeRule.SuppressTexture(
            reshaperInstalled: true, reshapeActive: true, ReshapedMinimapRoads.Show, largeMapOpen: false));
    }

    // --------------------------------------------- game-facing wiring

    [Fact]
    public void ADeletionIsReportedAfterTheCallAndOnlyIfThePinReallyLeft()
    {
        // TheGreatestMap's RemovePin prefix refuses to remove its own
        // markers without Shift; a prefix-time report would tombstone a
        // marker that is still on the map.
        string watch = Source("Map", "PinDeletionWatch.cs");

        Assert.Contains("postfix: new HarmonyMethod(typeof(PinDeletionWatch), nameof(AfterRemovePin))", watch, StringComparison.Ordinal);
        int after = watch.IndexOf("private static void AfterRemovePin(", StringComparison.Ordinal);
        Assert.True(after >= 0);
        string afterBody = watch.Substring(after);
        int stillListed = afterBody.IndexOf("StillListed?.Invoke(pin) == true", StringComparison.Ordinal);
        int report = afterBody.IndexOf("ExplicitDelete?.Invoke(pin,", StringComparison.Ordinal);
        Assert.True(stillListed >= 0 && report > stillListed, "the still-listed check must come before the report");

        int before = watch.IndexOf("private static void BeforeRemovePin(", StringComparison.Ordinal);
        string beforeBody = watch.Substring(before, after - before);
        Assert.DoesNotContain("ExplicitDelete", beforeBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePointerScopeAlwaysClosesAndAnUnknownScopeAttributesNothing()
    {
        string watch = Source("Map", "PinDeletionWatch.cs");

        Assert.Contains("finalizer: new HarmonyMethod(typeof(PinDeletionWatch), nameof(LeavePointerRemoval))", watch, StringComparison.Ordinal);
        Assert.Contains("new[] { typeof(Vector3), typeof(float) }", watch, StringComparison.Ordinal);
        Assert.Contains("PointerScopeInstalled && _pointerDepth > 0", watch, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeFeedsTheNeighbourIntoTheDeletionDecision()
    {
        string adapter = Source("Map", "PinAdapter.cs");
        Assert.Contains("pointerInitiated: pointerInitiated,", adapter, StringComparison.Ordinal);
        Assert.Contains("neighbourRemovesPinsWithoutPointing: neighbourRemovesPinsWithoutPointing)", adapter, StringComparison.Ordinal);

        string runtime = Source("Runtime", "CartographerRuntime.cs");
        Assert.Contains("_compatibility.Neighbors.RemovesForeignPinsWithoutPointing", runtime, StringComparison.Ordinal);
        Assert.Contains("PinDeletionWatch.StillListed = pin => _pinAdapter.ContainsPin(pin);", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void AModThatAdoptsHandPlacedMarkersIsTreatedAsAPinManager()
    {
        // One pin, one owner: with TheGreatestMap present, the palette and
        // the adopt prompts step aside exactly as for Pinnacle.
        string registry = Source("Runtime", "CompatibilityRegistry.cs");
        int detect = registry.IndexOf("Neighbors = NeighborMapMods.Detect(Chainloader.PluginInfos.Keys);", StringComparison.Ordinal);
        int flag = registry.IndexOf("if (Neighbors.OwnsVanillaPinFlow)", StringComparison.Ordinal);
        int set = registry.IndexOf("PinManagerPresent = true;", flag < 0 ? 0 : flag, StringComparison.Ordinal);
        Assert.True(detect >= 0 && flag > detect && set > flag);
    }

    [Fact]
    public void TheCompatReportNamesTheThreeModsOnlyOnAnExactGuid()
    {
        // Review of 5dc885c: the report used substring matching, so a
        // "DeathMonger.TheGreatestMapAddon" would have been listed as The
        // Greatest Map with none of its behaviour applied.
        string registry = Source("Runtime", "CompatibilityRegistry.cs");
        Assert.Contains("\"The Greatest Map\", exact: true,", registry, StringComparison.Ordinal);
        Assert.Contains("\"HUDCompass\", exact: true,", registry, StringComparison.Ordinal);
        Assert.Contains("\"Round Minimap\", exact: true,", registry, StringComparison.Ordinal);
        Assert.Contains("string.Equals(guid, known.GuidFragment, StringComparison.Ordinal)", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectionNeverReachesIntoAnotherModsTypes()
    {
        foreach (string file in new[]
        {
            Source("Runtime", "CompatibilityRegistry.cs"),
            Source("Map", "MinimapReshapeProbe.cs"),
            Source("Domain", "Atlas", "NeighborMapMods.cs"),
        })
        {
            Assert.DoesNotContain("TypeByName", file, StringComparison.Ordinal);
            Assert.DoesNotContain("Type.GetType(", file, StringComparison.Ordinal);
            Assert.DoesNotContain("GetField(", file, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProperty(", file, StringComparison.Ordinal);
            Assert.DoesNotContain("AccessTools", file, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheMinimapHoldJoinsTheExistingSuppressionAndIsReleasedWhenDisabled()
    {
        string renderer = Source("Map", "RoadOverlayRenderer.cs");
        Assert.Contains("bool suppress = _vectorLayer.IsActive || MinimapTextureHeld;", renderer, StringComparison.Ordinal);

        string runtime = Source("Runtime", "CartographerRuntime.cs");
        Assert.Contains("_routeRenderer.TickVisibility(_renderer.VectorLayerActive || _renderer.MinimapTextureHeld);", runtime, StringComparison.Ordinal);
        int disabled = runtime.IndexOf("_renderer.MinimapTextureHeld = false;", StringComparison.Ordinal);
        int fallback = runtime.IndexOf("_renderer.EnsureTextureFallback();", StringComparison.Ordinal);
        Assert.True(disabled >= 0 && fallback > disabled, "a disabled mod must release the hold before restoring the texture");
    }

    private static string Source(params string[] parts)
    {
        var segments = new List<string> { RepoRoot, "src", "ConcernedCartographer" };
        segments.AddRange(parts);
        return File.ReadAllText(Path.Combine(segments.ToArray()));
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
