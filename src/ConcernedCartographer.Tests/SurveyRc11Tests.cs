using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC11 blockers 9/11/13: durable rejection, stable-identity
/// dedupe, and humanized names across the survey pipeline.</summary>
public class SurveyRc11Tests
{
    private static readonly DateTime Now = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static (SurveyEngine Engine, PinStore Pins) Fixture()
    {
        var rules = new SurveyRuleSet();
        rules.AddRule(new SurveyRule("raspberrybush*", "cc:resource", "Resources", 30f, 120f));
        rules.AddRule(new SurveyRule("silvervein*", "cc:mine", "Resources", 0f, 0f));
        var engine = new SurveyEngine { Rules = rules, BaseExclusionRadiusMeters = 0f };
        return (engine, new PinStore(() => Now));
    }

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    [Fact]
    public void Reject_MovesToRejected_AndTheSameObjectNeverReoffers()
    {
        // THE RC11 blocker 9 regression: reject, then the next sweep
        // offers the exact same physical object again.
        (SurveyEngine engine, PinStore pins) = Fixture();
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now));

        Assert.True(engine.Reject(engine.Observations[0].Id, Now));
        Assert.Empty(engine.Observations);
        Assert.Single(engine.Rejected);
        Assert.True(engine.RejectedDirty);

        for (int sweep = 0; sweep < 5; sweep++)
        {
            Assert.Equal(SurveyEngine.OfferResult.RejectedEarlier, engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now));
        }

        Assert.Empty(engine.Observations);
    }

    [Fact]
    public void RestoreRejected_ReturnsToPending_AndCanBeOfferedNoMore()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now);
        engine.Reject(engine.Observations[0].Id, Now);

        Assert.True(engine.RestoreRejected(0, Now));
        Assert.Single(engine.Observations);
        Assert.Empty(engine.Rejected);
        Assert.Equal("Raspberry Bush", engine.Observations[0].SuggestedName);

        // Restored-to-pending still suppresses re-offers (pending key).
        Assert.Equal(SurveyEngine.OfferResult.DuplicateIdentity, engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now));
    }

    [Fact]
    public void AcceptRejected_CreatesThePin_AndRetiresTheIdentity()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now);
        engine.Reject(engine.Observations[0].Id, Now);

        Assert.True(engine.AcceptRejected(0, pins));
        Assert.Equal(1, pins.Count);
        Assert.Empty(engine.Rejected);
        Assert.Equal(SurveyEngine.OfferResult.DuplicateIdentity, engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now));
    }

    [Fact]
    public void SameObject_RepeatedSweeps_NeverDuplicatePending_EvenWithZeroRadiusRules()
    {
        // RC11 blocker 13: silvervein rule has duplicate radius 0 — the
        // stable location identity must still dedupe repeated sweeps.
        (SurveyEngine engine, PinStore pins) = Fixture();
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("silvervein(Clone)", P(-40f, 7f), pins, Now));
        for (int sweep = 0; sweep < 10; sweep++)
        {
            Assert.Equal(SurveyEngine.OfferResult.DuplicateIdentity, engine.Offer("silvervein(Clone)", P(-40f, 7f), pins, Now));
        }

        Assert.Single(engine.Observations);

        // A DIFFERENT vein of the same kind still offers.
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("silvervein(Clone)", P(-90f, 7f), pins, Now));
    }

    [Fact]
    public void RejectedList_PersistsThroughTheCodec_AndKeepsSuppressing()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now);
        engine.Offer("silvervein(Clone)", P(50f, 60f), pins, Now);
        engine.RejectAll(Now);

        var lines = new List<string>(SurveyRejectedCodec.Serialize(engine.Rejected));
        List<SurveyEngine.RejectedObservation> reloaded = SurveyRejectedCodec.Parse(lines, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal(2, reloaded.Count);

        // A fresh session (restart): load the persisted list — the same
        // objects stay suppressed, and names survived humanized.
        (SurveyEngine fresh, PinStore freshPins) = Fixture();
        fresh.LoadRejected(reloaded);
        Assert.False(fresh.RejectedDirty);
        Assert.Equal("Raspberry Bush", fresh.Rejected[0].SuggestedName);
        Assert.Equal(SurveyEngine.OfferResult.RejectedEarlier, fresh.Offer("RaspberryBush(Clone)", P(10f, 20f), freshPins, Now));
        Assert.Equal(SurveyEngine.OfferResult.RejectedEarlier, fresh.Offer("silvervein(Clone)", P(50f, 60f), freshPins, Now));
    }

    [Fact]
    public void RejectedCodec_SkipsMalformedRows()
    {
        var lines = new[]
        {
            SurveyRejectedCodec.Header,
            "raspberrybush\tRaspberry Bush\tcc:resource\tResources\t10\t30\t20\t638000000000000000\t1",
            "too\tfew\tfields\t1",
            "raspberrybush\tX\tcc:resource\tResources\tNaN\t30\t20\t638000000000000000\t1",
        };

        List<SurveyEngine.RejectedObservation> entries = SurveyRejectedCodec.Parse(lines, out int malformed);
        Assert.Single(entries);
        Assert.Equal(2, malformed);
    }

    [Fact]
    public void RuleEnableDisable_RoundtripsThroughTheFileFormat_AndDisabledRulesMatchNothing()
    {
        // RC11 blocker 10: UI-editable rules; enabled rows keep the RC10
        // 5-field shape so untouched starter files still normalize.
        var set = new SurveyRuleSet();
        set.AddRule(new SurveyRule("raspberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("beehive*", "cc:resource", "Resources", 60f, 240f));
        set.SetRuleEnabled(1, false);

        Assert.True(set.TryMatch("raspberrybush", out _));
        Assert.False(set.TryMatch("beehive", out _));

        var lines = new List<string>(set.Serialize());
        Assert.Contains(lines, line => line.EndsWith("\toff", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("raspberrybush", StringComparison.Ordinal) && line.EndsWith("\toff", StringComparison.Ordinal));

        SurveyRuleSet reloaded = SurveyRuleSet.Parse(lines, out int malformed);
        Assert.Equal(0, malformed);
        Assert.True(reloaded.Rules[0].Enabled);
        Assert.False(reloaded.Rules[1].Enabled);
        Assert.False(reloaded.TryMatch("beehive", out _));

        reloaded.SetRuleEnabled(1, true);
        Assert.True(reloaded.TryMatch("beehive", out _));
    }

    [Fact]
    public void RemoveRuleAt_DeletesExactlyThatRule()
    {
        var set = new SurveyRuleSet();
        set.AddRule(new SurveyRule("a*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("b*", "cc:mine", "Resources", 30f, 120f));

        SurveyRule? removed = set.RemoveRuleAt(0);
        Assert.Equal("a*", removed!.Pattern);
        Assert.Single(set.Rules);
        Assert.Null(set.RemoveRuleAt(5));
    }

    [Fact]
    public void ResetSession_ForgetsPendingButLoadedRejectionsRemain()
    {
        (SurveyEngine engine, PinStore pins) = Fixture();
        engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now);
        engine.Offer("silvervein(Clone)", P(50f, 60f), pins, Now);
        engine.Reject(engine.Observations[0].Id, Now);

        engine.ResetSession();
        Assert.Empty(engine.Observations);
        Assert.Single(engine.Rejected);
        Assert.Equal(SurveyEngine.OfferResult.RejectedEarlier, engine.Offer("RaspberryBush(Clone)", P(10f, 20f), pins, Now));
        Assert.Equal(SurveyEngine.OfferResult.Added, engine.Offer("silvervein(Clone)", P(50f, 60f), pins, Now));
    }
}

/// <summary>RC11 blockers 11/14: the mechanical humanizer.</summary>
public class NameHumanizerTests
{
    [Theory]
    [InlineData("RaspberryBush(Clone)", "Raspberry Bush")]
    [InlineData("raspberrybush", "Raspberry Bush")]
    [InlineData("BlueberryBush", "Blueberry Bush")]
    [InlineData("Pickable_SeedCarrot(Clone)", "Seed Carrot")]
    [InlineData("Pickable_Thistle", "Thistle")]
    [InlineData("silvervein(Clone)", "Silver Vein")]
    [InlineData("mudpile2", "Mud Pile")]
    [InlineData("rock4_copper(Clone)", "Rock Copper")]
    [InlineData("MineRock_Tin", "Mine Rock Tin")]
    [InlineData("TrollCave02", "Troll Cave")]
    [InlineData("sunkencrypt4", "Sunken Crypt")]
    [InlineData("gucksack", "Guck Sack")]
    [InlineData("Beehive", "Beehive")]
    [InlineData("RuneStone_Boars", "Rune Stone Boars")]
    [InlineData("TreasureChest_meadows", "Treasure Chest Meadows")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Humanize_SplitsCasesUnderscoresDigits_AndExpandsKnownCompounds(string? raw, string expected)
    {
        Assert.Equal(expected, NameHumanizer.Humanize(raw));
    }

    [Fact]
    public void SurveyObservations_CarryHumanizedNames_IntoPins()
    {
        var rules = new SurveyRuleSet();
        rules.AddRule(new SurveyRule("raspberrybush*", "cc:resource", "Resources", 30f, 120f));
        var engine = new SurveyEngine { Rules = rules, BaseExclusionRadiusMeters = 0f };
        var pins = new PinStore();

        engine.Offer("RaspberryBush(Clone)", new RoadPoint(1f, 30f, 1f), pins, DateTime.UtcNow);
        Assert.Equal("Raspberry Bush", engine.Observations[0].SuggestedName);

        engine.Accept(engine.Observations[0].Id, pins);
        foreach (AtlasPin pin in pins.Living)
        {
            Assert.Equal("Raspberry Bush", pin.Name);
        }
    }
}
