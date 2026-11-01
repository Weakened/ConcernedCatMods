using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConcernedForeman.Tests;

/// <summary>D19 (#282): the felling port and its command, read from source
/// because they bind the game. Pins what this leaf promises: one hit per call
/// built from his own axe, nothing taken or created, no durability written, an
/// off-by-default switch, and a worker that must be free.</summary>
public sealed class SmallTreeFellingWiringTests
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

    private static string Port => Code(Source("src", "ConcernedForeman", "Runtime", "Collection", "WorldFellingPort.cs"));

    [Fact]
    public void ThePortLandsExactlyOneHitBuiltFromHisOwnAxe()
    {
        string port = Port;
        Assert.Single(Regex.Matches(port, @"\.\s*Damage\s*\("));
        Assert.Contains("tree.Damage(hit);", port);
        Assert.Contains("hit.m_toolTier = (short)axe.m_shared.m_toolTier;", port);
        Assert.Contains("hit.m_damage.Add(axe.GetDamage());", port);
        Assert.Contains("hit.SetAttacker(worker);", port);

        // The axe comes out of his own inventory, never from a prefab or a clone.
        Assert.Contains("humanoid.GetInventory().GetAllItems()", port);
        Assert.DoesNotContain("Clone(", port);
        Assert.DoesNotContain("GetItemPrefab", port);
    }

    [Fact]
    public void ThisLeafTakesNothingCreatesNothingAndWritesNoDurability()
    {
        string port = Port;
        foreach (string forbidden in new[]
        {
            ".Pickup(", "AddItem(", "RemoveItem(", "Instantiate(", "Destroy(", "m_durability", "SetOwner(", "m_autoPickup",
        })
        {
            Assert.DoesNotContain(forbidden, port);
        }
    }

    [Fact]
    public void EverySiteRuleIsAskedImmediatelyBeforeTheHit()
    {
        string port = Port;
        int check = port.IndexOf("SmallTreeFelling.CheckSite(site, _reachMetres)", StringComparison.Ordinal);
        int hit = port.IndexOf("tree.Damage(hit);", StringComparison.Ordinal);
        Assert.True(check > 0 && hit > check, "the site check must come before the hit");
        Assert.Contains("ownedHere: view!.IsOwner()", port);
        Assert.Contains("nearestBuiltPieceMetres: NearestPieceMetres(treeAt)", port);
        Assert.Contains("nearestPlayerMetres: NearestPlayerMetres(treeAt)", port);
        Assert.Contains("piece.IsPlacedByPlayer()", port);

        // The game's piece search is a sphere; it must reach well past the flat
        // clearance so a piece across a slope is still measured.
        Assert.Contains("Piece.GetAllPiecesInRadius(at, SmallTreeFelling.ClearanceMetres * 4f, _pieces);", port);
    }

    [Fact]
    public void TheCommandIsOffByDefaultAndNeedsAFreeWorkerAndAnExplicitArea()
    {
        string settings = Code(Source("src", "ConcernedForeman", "Runtime", "Collection", "CollectionWorldFacts.cs"));
        Assert.Matches(new Regex(@"""ThorsteinFellsSmallTrees"",\s*false,"), settings);

        string runtime = Code(Source("src", "ConcernedForeman", "Runtime", "Collection", "CollectionRuntime.cs"));
        Assert.Contains("case \"fell\":", runtime);
        int fell = runtime.IndexOf("private string Fell()", StringComparison.Ordinal);
        Assert.True(fell > 0);
        string body = runtime.Substring(fell, runtime.IndexOf("private WorldFellingPort? _felling;", fell, StringComparison.Ordinal) - fell);
        Assert.Contains("_settings.ThorsteinFellsSmallTrees.Value", body);
        Assert.Contains("Modes.Mode != ActorMode.Resting", body);
        Assert.Contains("Modes.JobId != null", body);
        Assert.Contains("scope!.Source != WorkScopeSource.DefaultCampCircle", body);
    }
}
