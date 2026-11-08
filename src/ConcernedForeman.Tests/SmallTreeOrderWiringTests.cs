using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConcernedForeman.Tests;

/// <summary>#282 leaf 2 (D19 inside a collection order), read from source
/// because the adapters bind the game: the order's strike re-asks every D19
/// gate, journals before the one pinned hit, traces only the Wood that hit
/// dropped up to the order's limit, and takes it through the existing pinned
/// pickup; the survey offers trees only with felling on in an explicit area;
/// and the runtime really wires the seam (a helper with no caller is not done).
/// </summary>
public sealed class SmallTreeOrderWiringTests
{
    private static string Source(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(path, "ConcernedCatMods.sln")))
        {
            path = Directory.GetParent(path)!.FullName;
        }

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static string Code(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);

    private static string Collection(string file) => Code(Source("src", "ConcernedForeman", "Runtime", "Collection", file));

    private static string Between(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing: " + from);
        int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, "missing after " + from + ": " + to);
        return text.Substring(start, end - start);
    }

    private static int At(string text, string what)
    {
        int index = text.IndexOf(what, StringComparison.Ordinal);
        Assert.True(index >= 0, "missing: " + what);
        return index;
    }

    [Fact]
    public void TheOneHitLivesInStrikeOnceWhichBothTheCommandAndAnOrderReach()
    {
        string port = Collection("WorldFellingPort.cs");
        string strike = Between(port, "internal FellOutcome StrikeOnce(", "internal static bool IsSmallTreeHash(");
        Assert.Single(Regex.Matches(port, @"\.\s*Damage\s*\("));
        Assert.Contains("tree.Damage(hit);", strike);

        string command = Between(port, "public FellReport TryFellNearest(", "private static FellReport Refused(");
        Assert.True(At(command, "SmallTreeFelling.CheckSite(site, _reachMetres)") < At(command, "StrikeOnce("));
    }

    [Fact]
    public void AnOrdersStrikeReasksEveryGateAndJournalsBeforeAndAfterTheHit()
    {
        string pickup = Collection("WorldSourcePickupPort.cs");
        string strike = Between(pickup, "public FellStrikeResult TryStrike(", "private static FellStrikeResult StrikeRefused(");

        int authority = At(strike, "_authority() != WorkAuthorityVerdict.Granted");
        int reservation = At(strike, "_reservations.IsHeldBy(source, order)");
        int classify = At(strike, "SmallTreeFelling.Classify(facts)");
        int site = At(strike, "SmallTreeFelling.CheckSite(site, _parameters.PickupReachMetres)");
        int begin = At(strike, "_custody.BeginPickup(order, source)");
        int hit = At(strike, "_felling.StrikeOnce(");
        int finish = At(strike, "_custody.FinishPickup(pickup.Value, result)");
        Assert.True(authority < reservation && reservation < classify && classify < site && site < begin && begin < hit && hit < finish,
            "authority, reservation, classification and the site rules come first, then the journal, then the hit, then its result");

        // D19's own gates, from the order: the switch, its explicit area, the
        // tree inside it, ownership, the ward, both clearances and his own axe.
        Assert.Contains("featureEnabled: _felling.Enabled", strike);
        Assert.Contains("explicitArea: definition.Scope.Source != WorkScopeSource.DefaultCampCircle", strike);
        Assert.Contains("insideArea: definition.Scope.Contains(point)", strike);
        Assert.Contains("ownedHere: view.IsOwner()", strike);
        Assert.Contains("nearestPlayerMetres: WorldFellingPort.NearestPlayerMetres(treeAt)", strike);
        Assert.Contains("nearestBuiltPieceMetres: _felling.NearestPieceMetres(treeAt)", strike);
        Assert.Contains("WorldFellingPort.BestAxe(humanoid)", strike);
        Assert.Contains("FellSiteRefusal.PlayerTooClose", strike);
    }

    [Fact]
    public void OnlyTheWoodHisOwnHitDroppedOwnedHereAndWithinTheLimitIsTraced()
    {
        string pickup = Collection("WorldSourcePickupPort.cs");
        string strike = Between(pickup, "public FellStrikeResult TryStrike(", "private static FellStrikeResult StrikeRefused(");

        // Only drops StrikeOnce reported, only on a fell.
        Assert.Contains("if (outcome == FellOutcome.Felled)", strike);
        Assert.Contains("foreach (ItemDrop candidate in _spawned)", strike);
        Assert.DoesNotContain("ItemDrop.s_instances", strike);

        // Wood, owned here, and never more than the order may take.
        Assert.Contains("string woodPrefab = CollectedResources.ItemPrefabName(CollectedResource.Wood);", strike);
        Assert.Contains("dropView.IsOwner()", strike);
        Assert.Contains("if (!wood || stack > room)", strike);

        // The take is the existing one: no pickup, no add, no destroy here.
        foreach (string forbidden in new[] { ".Pickup(", "AddItem(", "Destroy(", "Instantiate(", "m_durability" })
        {
            Assert.DoesNotContain(forbidden, strike);
        }

        Assert.Single(Regex.Matches(pickup, @"\.\s*Pickup\s*\("));

        // #282 review: the take names where the pick booked the drop, the drop's
        // own id, never the source's.
        string take = Between(pickup, "public int TryTakeDrop(", "public void ReleaseUntakenDrops(");
        Assert.Contains("PickupTakes.GroundOf(drop, traced.Source),", take);
        Assert.DoesNotContain("traced.Source.ToString()", take);
    }

    [Fact]
    public void TheSurveyOffersTreesOnlyWithFellingOnInAnExplicitArea()
    {
        string survey = Collection("WorldSourceSurvey.cs");
        Assert.Contains(
            "bool trees = _felling != null && _felling.Enabled && scope.Source != WorkScopeSource.DefaultCampCircle;",
            survey);
        Assert.Contains("bool tree = trees && WorldFellingPort.IsSmallTreeHash(hash);", survey);
        Assert.Contains("builtPieceClearanceMetres: BuiltPieceClearance(position)", survey);
        Assert.Contains("fromMiddle = _felling!.NearestPieceMetres(middle);", survey);
        Assert.Contains(": _felling!.NearestPieceMetres(position);", survey);
    }

    [Fact]
    public void TheRuntimeWiresTheSeamIntoTheOrderLoop()
    {
        string runtime = Collection("CollectionRuntime.cs");
        string world = Between(runtime, "private sealed class WorldState", "public Guid Epoch { get; }");
        Assert.Contains("WorldFellingPort felling = runtime.FellingPort(Parameters);", world);
        Assert.Contains("new WorldSourceSurvey(Directory, runtime._site, felling)", world);
        Assert.Matches(new Regex(@"runtime\._log,\s*Pickup\);"), world);
        Assert.Matches(new Regex(@"runtime\._log,\s*felling\);"), world);
    }
}
