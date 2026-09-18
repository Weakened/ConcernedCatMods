using System;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Tools;
using UnityEngine;
using static ConcernedForeman.Tests.ForemanFixtures;

namespace ConcernedForeman.Tests;

/// <summary>What a chest is, and what a tool is: the two classifications the
/// record depends on and the game answers.</summary>
public sealed class AdapterIdentityTests : IDisposable
{
    public AdapterIdentityTests()
    {
        ObjectDB.instance = new ObjectDB();
        Player.m_localPlayer = null;
    }

    public void Dispose() => Player.m_localPlayer = null;

    // ------------------------------------------------------------------
    // Which chest
    // ------------------------------------------------------------------

    [Fact]
    public void AChestIsIdentifiedByItsOwnNetworkObjectAndNotByWhereItStands()
    {
        Container chest = Chest("000000000000002a:0000007b");

        Assert.Equal("000000000000002a:0000007b", SettlementTargets.TryIdentify(chest));

        // The same chest moved is the same chest; a different object is not.
        chest.transform.position = new Vector3(120f, 30f, -40f);
        Assert.Equal("000000000000002a:0000007b", SettlementTargets.TryIdentify(chest));
        Assert.NotEqual(SettlementTargets.TryIdentify(chest), SettlementTargets.TryIdentify(Chest("000000000000002a:0000007c")));
    }

    [Fact]
    public void AChestOnACartIsIdentifiedByTheObjectVanillaConsidersToBeIt()
    {
        // Container.Awake honours m_rootObjectOverride, so a chest on a wagon is
        // the wagon's object. Identifying the child instead would name something
        // the game does not treat as the container.
        Vagon cart = Cart();
        Container container = cart.m_container!;
        var root = new GameObject("Cart_root");
        ZNetView rootView = root.Add(new ZNetView());
        rootView.Zdo = new ZDO { m_uid = new ZDOID(9L, 9u) };
        container.m_rootObjectOverride = root;

        Assert.Equal("0000000000000009:00000009", SettlementTargets.TryIdentify(container));
    }

    [Fact]
    public void AChestWithNoValidNetworkObjectCannotBeIdentifiedAtAll()
    {
        Container chest = Chest();
        chest.GetComponent<ZNetView>()!.Valid = false;
        Assert.Null(SettlementTargets.TryIdentify(chest));

        var bare = new GameObject("piece_chest_wood").Add(new Container());
        Assert.Null(SettlementTargets.TryIdentify(bare));
    }

    [Fact]
    public void LookingAtSomethingThatIsNotAChestIsARefusalWithASentence()
    {
        Assert.False(SettlementTargets.TryResolveHoveredContainer(out _, out _, out string noPlayer));
        Assert.Contains("no local player", noPlayer);

        var player = new GameObject("Player").Add(new Player());
        Player.m_localPlayer = player;
        Assert.False(SettlementTargets.TryResolveHoveredContainer(out _, out _, out string nothing));
        Assert.Contains("not looking at anything", nothing);

        player.Hovering = new GameObject("rock");
        Assert.False(SettlementTargets.TryResolveHoveredContainer(out _, out _, out string notAChest));
        Assert.Contains("not a container", notAChest);

        Container chest = Chest();
        player.Hovering = chest.gameObject;
        Assert.True(SettlementTargets.TryResolveHoveredContainer(out Container? found, out string? key, out _));
        Assert.Same(chest, found);
        Assert.Equal(SettlementTargets.TryIdentify(chest), key);
    }

    // ------------------------------------------------------------------
    // Which tool
    // ------------------------------------------------------------------

    [Fact]
    public void AnAxeIsTheToolWhoseDominantDamageIsChopping()
    {
        Assert.Equal(ToolKind.Axe, ToolClassifier.Classify(Axe()));

        ItemDrop.ItemData sword = Axe();
        sword.m_shared.m_damages = new HitData.DamageTypes { m_slash = 50f, m_chop = 10f };
        Assert.Equal(ToolKind.None, ToolClassifier.Classify(sword));

        ItemDrop.ItemData pickaxe = Axe();
        pickaxe.m_shared.m_damages = new HitData.DamageTypes { m_pickaxe = 40f, m_chop = 5f };
        Assert.Equal(ToolKind.None, ToolClassifier.Classify(pickaxe));
    }

    [Fact]
    public void AHammerBuildsStructuresAndAHoeDoesNot()
    {
        Assert.Equal(ToolKind.Hammer, ToolClassifier.Classify(Hammer()));

        // A hoe carries a piece table too, and every piece in it is a terrain
        // operation. A worker handed one as his "hammer" could build nothing.
        var ground = new GameObject("raise");
        ground.Add(new TerrainOp());
        ground.Add(new Piece());
        var table = new GameObject("_HoePieceTable");
        PieceTable pieces = table.Add(new PieceTable());
        pieces.m_pieces.Add(ground);

        ItemDrop.ItemData hoe = Hammer();
        hoe.m_shared.m_buildPieces = pieces;
        Assert.Equal(ToolKind.None, ToolClassifier.Classify(hoe));
    }

    [Fact]
    public void ADescribedToolCarriesWhatTheRecordNeedsToFindItAgain()
    {
        Assert.True(ToolClassifier.TryDescribe(Axe(), out ToolSpecimen specimen));
        Assert.Equal(ToolKind.Axe, specimen.Kind);
        Assert.Equal("$item_axe_bronze", specimen.ItemKey);
        Assert.Equal(2, specimen.ToolTier);
        Assert.Equal(80f, specimen.DurabilityAtIssue);

        ItemDrop.ItemData nameless = Axe();
        nameless.m_shared.m_name = string.Empty;
        Assert.False(ToolClassifier.TryDescribe(nameless, out _));

        ItemDrop.ItemData broken = Axe();
        broken.m_durability = float.NaN;
        Assert.False(ToolClassifier.TryDescribe(broken, out _));
    }

    [Fact]
    public void UsabilityIsAskedOfTheRealItemAndAWornToolIsNotUsable()
    {
        ItemDrop.ItemData axe = Axe();
        Assert.True(ToolClassifier.IsStillUsable(axe));

        axe.m_durability = 0f;
        Assert.False(ToolClassifier.IsStillUsable(axe));

        // Something that cannot wear at all is always usable: vanilla's rule.
        ItemDrop.ItemData indestructible = Axe();
        indestructible.m_shared.m_useDurability = false;
        indestructible.m_durability = 0f;
        Assert.True(ToolClassifier.IsStillUsable(indestructible));
    }
}
