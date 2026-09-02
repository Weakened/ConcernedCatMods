using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC10 ROAD SOURCE AUTHORITY, identity edition (DEF-v1.0-007).
///
/// The fourth owner report proved settings-flag heuristics cannot work:
/// in the live game the hoe's "Level ground" places <c>mud_road_v2</c> and
/// "Pathen" places <c>path_v2</c>, BOTH as smooth-and-paint-Dirt TerrainOps
/// with m_level and m_raise false. The classifier therefore takes NO
/// settings flags at all — only the placed prefab identity, the piece name
/// token, the selected-piece corroboration, and the paint (as agreement,
/// never authority). These tests pin the failure mode and the authority
/// rules.</summary>
public class TerrainActionClassifierTests
{
    private static TerrainActionClassification Classify(
        string? opName,
        TerrainPaintKind paint,
        string? token = null,
        string? selected = null,
        bool paintCleared = true)
    {
        return TerrainActionClassifier.Classify(opName, token, selected, paintCleared, paint);
    }

    [Fact]
    public void LevelGround_CarryingDirtPaint_NeverCreatesRoad()
    {
        // THE P1 REGRESSION: a Level Ground op exactly as the game delivers
        // it — mud_road_v2 clone, m_paintCleared, PaintType.Dirt, and
        // (unreliably) no level/raise flags. The classifier has no flag
        // inputs, so the old failure mode is structurally impossible: the
        // prefab identity alone must classify this as terraforming.
        TerrainActionClassification verdict = Classify("mud_road_v2(Clone)", TerrainPaintKind.Dirt);

        Assert.Equal(TerrainActionCategory.LevelGround, verdict.Category);
        Assert.Null(verdict.RoadKind);
        Assert.True(verdict.ErasesRoads);
    }

    [Theory]
    [InlineData("mud_road_v2(Clone)")]
    [InlineData("mud_road_v2")]
    [InlineData("mud_road")]
    [InlineData("MUD_ROAD_V2(CLONE)")]
    [InlineData("mud_road_v2(Clone)(Clone)")]
    public void LevelGround_AllNameForms_NeverCreateRoad(string opName)
    {
        TerrainActionClassification verdict = Classify(opName, TerrainPaintKind.Dirt);

        Assert.Equal(TerrainActionCategory.LevelGround, verdict.Category);
        Assert.Null(verdict.RoadKind);
    }

    [Theory]
    [InlineData("raise_v2(Clone)", "RaiseGround")]
    [InlineData("cultivate_v2(Clone)", "Cultivate")]
    [InlineData("replant_v2(Clone)", "Replant")]
    [InlineData("digg_v2(Clone)", "Digging")]
    public void OtherTerraformActions_NeverCreateRoads_AndEraseCoveredInk(
        string opName, string expectedName)
    {
        var expected = Enum.Parse<TerrainActionCategory>(expectedName);

        // Raise paints Dirt, cultivate paints Cultivate, digging paints
        // Dirt — none of it is road authority; all of it erases stale ink.
        TerrainPaintKind paint = expected == TerrainActionCategory.Cultivate
            ? TerrainPaintKind.Cultivate
            : TerrainPaintKind.Dirt;
        TerrainActionClassification verdict = Classify(opName, paint);

        Assert.Equal(expected, verdict.Category);
        Assert.Null(verdict.RoadKind);
        Assert.True(verdict.ErasesRoads);
    }

    [Fact]
    public void Pathen_WithDirtPaint_CreatesDirtRoad()
    {
        TerrainActionClassification verdict = Classify("path_v2(Clone)", TerrainPaintKind.Dirt);

        Assert.Equal(TerrainActionCategory.Pathen, verdict.Category);
        Assert.Equal(RoadKind.Dirt, verdict.RoadKind);
        Assert.False(verdict.ErasesRoads);
    }

    [Fact]
    public void PavedRoad_WithPavedPaint_CreatesPavedRoad()
    {
        TerrainActionClassification verdict = Classify("paved_road_v2(Clone)", TerrainPaintKind.Paved);

        Assert.Equal(TerrainActionCategory.PavedRoad, verdict.Category);
        Assert.Equal(RoadKind.Paved, verdict.RoadKind);
    }

    [Theory]
    [InlineData("path_v2(Clone)", "Paved")]
    [InlineData("path_v2(Clone)", "Cultivate")]
    [InlineData("path_v2(Clone)", "None")]
    [InlineData("paved_road_v2(Clone)", "Dirt")]
    [InlineData("paved_road_v2(Clone)", "Other")]
    public void RoadIdentity_WithDisagreeingPaint_IsRefused(string opName, string paintName)
    {
        // Identity AND paint must agree: if a game update ever changes what
        // path_v2 paints, the mod inks nothing instead of inking wrong.
        TerrainActionClassification verdict = Classify(opName, Enum.Parse<TerrainPaintKind>(paintName));

        Assert.Null(verdict.RoadKind);
        Assert.True(verdict.ErasesRoads);
    }

    [Fact]
    public void DirtPaintAlone_OnUnknownOperation_IsNeverAuthority()
    {
        // Arbitrary Dirt terrain paint — native dirt patches, spawn areas,
        // modded terraformers — must never become a road.
        TerrainActionClassification verdict = Classify("odins_auto_leveler(Clone)", TerrainPaintKind.Dirt);

        Assert.Equal(TerrainActionCategory.Unknown, verdict.Category);
        Assert.Null(verdict.RoadKind);
        Assert.True(verdict.ErasesRoads);
    }

    [Fact]
    public void WithoutPaintClear_NothingIsRoadOrEraser()
    {
        TerrainActionClassification verdict = Classify(
            "path_v2(Clone)", TerrainPaintKind.Dirt, paintCleared: false);

        Assert.Null(verdict.RoadKind);
        Assert.False(verdict.ErasesRoads);
    }

    [Fact]
    public void SelectionMismatch_RefusesRoadCreation()
    {
        // A road-authorized op while the player's actual selection is a
        // different piece was not a deliberate local road action.
        TerrainActionClassification verdict = Classify(
            "path_v2(Clone)", TerrainPaintKind.Dirt, selected: "wood_wall");

        Assert.True(verdict.SelectionMismatch);
        Assert.Null(verdict.RoadKind);
        Assert.True(verdict.ErasesRoads);
        Assert.Contains("REFUSED", verdict.Description);
    }

    [Theory]
    [InlineData("path_v2")]
    [InlineData("path_v2(Clone)")]
    public void SelectionAgreement_AllowsRoadCreation(string selected)
    {
        TerrainActionClassification verdict = Classify(
            "path_v2(Clone)", TerrainPaintKind.Dirt, selected: selected);

        Assert.False(verdict.SelectionMismatch);
        Assert.Equal(RoadKind.Dirt, verdict.RoadKind);
    }

    [Fact]
    public void MissingSelection_TrustsOperationIdentityAlone()
    {
        TerrainActionClassification verdict = Classify(
            "path_v2(Clone)", TerrainPaintKind.Dirt, selected: null);

        Assert.Equal(RoadKind.Dirt, verdict.RoadKind);
    }

    [Fact]
    public void SelectedPathen_CannotPromoteALevelOperation()
    {
        // The op identity governs: leveling while pathen is somehow
        // reported as selected still creates nothing.
        TerrainActionClassification verdict = Classify(
            "mud_road_v2(Clone)", TerrainPaintKind.Dirt, selected: "path_v2");

        Assert.Equal(TerrainActionCategory.LevelGround, verdict.Category);
        Assert.Null(verdict.RoadKind);
    }

    [Theory]
    [InlineData("$piece_pathen", "Pathen")]
    [InlineData("$piece_level", "LevelGround")]
    [InlineData("$piece_pavedroad", "PavedRoad")]
    [InlineData("$piece_raise", "RaiseGround")]
    public void PieceToken_ClassifiesWhenPrefabNameIsForeign(string token, string expectedName)
    {
        var expected = Enum.Parse<TerrainActionCategory>(expectedName);

        // Some mods rename instantiated objects; the Piece localization
        // token still identifies the action. Level via token stays no-road.
        TerrainActionClassification verdict = Classify(
            "SomeWrappedObject(Clone)", TerrainPaintKind.Dirt, token: token);

        Assert.Equal(expected, verdict.Category);
        if (expected == TerrainActionCategory.Pathen)
        {
            Assert.Equal(RoadKind.Dirt, verdict.RoadKind);
        }
        else
        {
            Assert.Null(verdict.RoadKind);
        }
    }

    [Fact]
    public void PrefabName_OutranksPieceToken()
    {
        // mud_road_v2 with a lying pathen token must stay Level Ground.
        TerrainActionClassification verdict = Classify(
            "mud_road_v2(Clone)", TerrainPaintKind.Dirt, token: "$piece_pathen");

        Assert.Equal(TerrainActionCategory.LevelGround, verdict.Category);
        Assert.Null(verdict.RoadKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnusableNames_ClassifyAsUnknown_NeverRoad(string? opName)
    {
        TerrainActionClassification verdict = Classify(opName, TerrainPaintKind.Dirt);

        Assert.Equal(TerrainActionCategory.Unknown, verdict.Category);
        Assert.Null(verdict.RoadKind);
    }

    [Fact]
    public void Description_ExposesIdentityForDiagnostics()
    {
        TerrainActionClassification verdict = Classify("mud_road_v2(Clone)", TerrainPaintKind.Dirt);

        Assert.Contains("level-ground", verdict.Description);
        Assert.Contains("mud_road_v2", verdict.Description);
        Assert.Contains("no road", verdict.Description);
    }
}
