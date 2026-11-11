using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 2: the excavation adapter changes terrain in exactly one way. A text audit of the product
/// source (not the compiled DLL): it pins the one terrain-tool call to one file and refuses the ways of changing the
/// world this slice is not authorized to use. It cannot see a call reached through a delegate or reflection invoke;
/// the reflection the adapter does use is pinned to reads.</summary>
public sealed class ExcavationSourceAuditTests
{
    private static string ProductRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }
        string root = directory?.FullName ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        return Path.Combine(root, "src", "ConcernedLadders");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string? subfolder = null)
    {
        string root = ProductRoot();
        string start = subfolder == null ? root : Path.Combine(root, subfolder);
        foreach (string file in Directory.EnumerateFiles(start, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) continue;
            yield return (relative, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>Removes // and /* */ comments so documentation that names a call is not counted as one. String
    /// literals are left alone (none of the audited tokens appears in one).</summary>
    private static string StripComments(string text) =>
        Regex.Replace(text, @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    [Fact]
    public void TheTerrainToolCallIsMadeOnceInOneFile()
    {
        var hits = Sources().Select(s => (s.Path, N: Count(s.Text, @"\bSpawnOnHitTerrain\s*\("))).Where(h => h.N > 0).ToList();
        Assert.Single(hits);
        Assert.Equal("Excavation/ExcavationRuntime.cs", hits[0].Path);
        Assert.Equal(1, hits[0].N);
    }

    [Theory]
    [InlineData(@"\bInvokeRPC\s*\(")]
    [InlineData(@"\bInvokeRoutedRPC\s*\(")]
    [InlineData(@"\bSetHeight\s*\(")]
    [InlineData(@"\bApplyOperation\s*\(")]
    [InlineData(@"\bGetAndCreateTerrainCompiler\s*\(")]
    [InlineData(@"\bClaimOwnership\s*\(")]
    [InlineData(@"\bSetOwner\s*\(")]
    [InlineData(@"\bGetZDO\s*\(")]
    [InlineData(@"\bZDOMan\b")]
    [InlineData(@"\bInstantiate\s*\(")]
    [InlineData(@"\bSetValue\s*\(")]
    [InlineData(@"\bPrefabManager\b")]
    [InlineData(@"\bDestroyImmediate\s*\(")]
    [InlineData(@"\.Damage\s*\(")]
    [InlineData(@"\bm_levelDelta\s*\[")]
    [InlineData(@"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)")]
    public void TheAdapterNeverChangesTheWorldAnyOtherWay(string pattern)
    {
        foreach ((string path, string text) in Sources("Excavation"))
        {
            // #567: the one object-removal site claims ownership before the game's own destroy; pinned below.
            if (path == "Excavation/ExcavationClearing.cs" && pattern == @"\bClaimOwnership\s*\(") continue;
            // #577: the one object-creation site places earth blocks; pinned in TheEarthFillIsTheOneCreationSite.
            if (path == "Excavation/EarthFillPlacement.cs" && pattern == @"\bInstantiate\s*\(") continue;
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
        foreach ((string path, string text) in Sources("Domain/Excavation"))
        {
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
    }

    [Fact]
    public void OnlyTheAdaptersOwnContainerAndTheClearedGroundAreEverDestroyed()
    {
        var hits = Sources("Excavation").Select(s => (s.Path, N: Count(s.Text, @"\bDestroy\s*\("))).Where(h => h.N > 0).OrderBy(h => h.Path).ToList();
        Assert.Equal(2, hits.Count);
        Assert.Equal(("Excavation/ExcavationClearing.cs", 1), hits[0]);
        Assert.Equal(("Excavation/ExcavationTerrainOps.cs", 1), hits[1]);
        Assert.Matches(@"Object\.Destroy\s*\(\s*_container\s*\)", SourceText("Excavation/ExcavationTerrainOps.cs"));
    }

    /// <summary>#567 (owner 2026-11-11: objects should not prevent digging): the one place a world object is removed. Ownership
    /// is claimed right before the game's own destroy; pickables are picked first through the game's own pick; and the
    /// clearing is reached once, from the dig's commit, after its batch was placed and before the charge, with the lists the
    /// survey made. The survey sends building pieces, loose items, carts, ships, moving bodies and anything without a network
    /// object away before anything is listed.</summary>
    [Fact]
    public void TheClearingIsTheOneRemovalSiteAndOnlyTheDigReachesIt()
    {
        string clearing = SourceText("Excavation/ExcavationClearing.cs");
        Assert.Equal(1, Count(clearing, @"view\.ClaimOwnership\s*\(\s*\)\s*;\s*scene\.Destroy\s*\(\s*target\s*\)\s*;"));
        Assert.Equal(1, Count(clearing, @"\bClaimOwnership\s*\("));
        Assert.Equal(1, Count(clearing, @"if\s*\(\s*pickable\s*!=\s*null\s*&&\s*pickable\.CanBePicked\s*\(\s*\)\s*\)\s*pickable\.Interact\s*\(\s*player\s*,\s*false\s*,\s*false\s*\)\s*;"));
        Assert.Equal(1, Count(clearing, @"\.Interact\s*\("));
        Assert.DoesNotMatch(@"\bInstantiate\s*\(|WearNTear|ItemDrop|Vagon|\bShip\b|\.Damage\s*\(", clearing);
        var calls = Hits(@"ExcavationClearing\.Clear\s*\(");
        Assert.True(calls.Count == 1 && calls[0].Path == "Excavation/ExcavationRuntime.cs" && calls[0].N == 1, string.Join(", ", calls));
        string commit = MethodBody(SourceText("Excavation/ExcavationRuntime.cs"), CommitSignature);
        int placed = Regex.Match(commit, @"if\s*\(\s*placed\s*==\s*null\s*\)\s*return").Index;
        int cleared = Regex.Match(commit, @"ExcavationClearing\.Clear\s*\(\s*player\s*,\s*_pick\s*,\s*_remove\s*\)").Index;
        int charged = Regex.Match(commit, ChargeCall).Index;
        Assert.True(placed > 0 && cleared > placed && charged > cleared, "Clear only after the batch was placed, and before the charge.");
        string survey = MethodBody(SourceText("Excavation/ExcavationWorldChecks.cs"), @"internal\s+int\s+Survey\s*\(");
        int piece = Regex.Match(survey, @"GetComponentInParent<WearNTear>\(\)\s*!=\s*null\s*\)\s*\{\s*pieces\+\+\s*;\s*continue\s*;").Index;
        int item = Regex.Match(survey, @"GetComponentInParent<ItemDrop>\(\)\s*!=\s*null\s*\)\s*continue\s*;").Index;
        int block = Regex.Match(survey, @"if\s*\(\s*vehicle\s*\|\|\s*moving\s*\|\|\s*view\s*==\s*null\s*\|\|\s*!view\.IsValid\s*\(\s*\)\s*\)").Index;
        int listed = Regex.Match(survey, @"toRemove\.Add\s*\(").Index;
        Assert.True(piece > 0 && item > piece && block > item && listed > block, "Pieces, items and blockers are sorted out before anything is listed.");
        Assert.Matches(@"bool\s+vehicle\s*=\s*hit\.GetComponentInParent<Vagon>\(\)\s*!=\s*null\s*\|\|\s*hit\.GetComponentInParent<Ship>\(\)\s*!=\s*null\s*;", survey);
    }

    /// <summary>#577 (owner 2026-11-11: "I want to implement the earth fill idea"): the one place a world object is created.
    /// One instantiation of the block prefab and one scale through the game's own sync, and nothing else of the world's;
    /// reached only through the dig commit's fill (after its batch was placed, before the charge) and the undo's (after
    /// its batch was placed). The blocks are removed only through the pinned clearing site, which removes nothing but
    /// blocks on that path. The survey sorts the blocks out before anything is listed for clearing.</summary>
    [Fact]
    public void TheEarthFillIsTheOneCreationSite()
    {
        string placement = SourceText("Excavation/EarthFillPlacement.cs");
        Assert.Equal(1, Count(placement, @"\bInstantiate\s*\("));
        Assert.Equal(1, Count(placement, @"GameObject\s+spawned\s*=\s*Object\.Instantiate\s*\(\s*prefab\s*,\s*new\s+Vector3\s*\(\s*x\s*,\s*y\s*,\s*z\s*\)\s*,\s*Quaternion\.identity\s*\)\s*;"));
        Assert.Equal(1, Count(placement, @"view\.SetLocalScale\s*\(\s*new\s+Vector3\s*\(\s*1f\s*,\s*block\.Height\s*,\s*1f\s*\)\s*\)\s*;"));
        Assert.DoesNotMatch(@"\bDestroy\w*\s*\(|ClaimOwnership|SetOwner|InvokeRPC|InvokeRoutedRPC|GetZDO|\.Damage\s*\(|SetCreator|AddComponent|\.Set\s*\(|SpawnOnHitTerrain|\.Interact\s*\(", placement);
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        // Placing: once in the dig's fill, once in the undo's; nowhere else in the product.
        var places = Hits(@"EarthFillPlacement\.Place\s*\(");
        Assert.True(places.Count == 1 && places[0].Path == "Excavation/ExcavationRuntime.cs" && places[0].N == 2, string.Join(", ", places));
        Assert.Equal(1, Count(MethodBody(runtime, @"private\s+int\s+Fill\s*\("), @"EarthFillPlacement\.Place\s*\("));
        Assert.Equal(1, Count(MethodBody(runtime, @"private\s+void\s+Unfill\s*\("), @"EarthFillPlacement\.Place\s*\("));
        // Removing blocks: once in each, through the clearing site.
        var removes = Hits(@"ExcavationClearing\.RemoveFill\s*\(");
        Assert.True(removes.Count == 1 && removes[0].Path == "Excavation/ExcavationRuntime.cs" && removes[0].N == 2, string.Join(", ", removes));
        Assert.Equal(1, Count(MethodBody(runtime, @"private\s+int\s+Fill\s*\("), @"ExcavationClearing\.RemoveFill\s*\("));
        Assert.Equal(1, Count(MethodBody(runtime, @"private\s+void\s+Unfill\s*\("), @"ExcavationClearing\.RemoveFill\s*\("));
        string removeFill = MethodBody(SourceText("Excavation/ExcavationClearing.cs"), @"internal\s+static\s+int\s+RemoveFill\s*\(");
        Assert.Matches(@"if\s*\(\s*block\s*!=\s*null\s*&&\s*EarthFillPlacement\.IsFill\s*\(\s*block\s*\)\s*\)\s*removed\s*\+=\s*Remove\s*\(\s*block\s*\)\s*;", removeFill);
        // The dig fills once, from its commit, after its batch was placed and before the charge; with the fill off it does nothing.
        Assert.Equal(1, Count(runtime, @"(?<!\bint\s+)\bFill\s*\(\s*placed\s*\)"));
        string commit = MethodBody(runtime, CommitSignature);
        int placedAt = Regex.Match(commit, @"if\s*\(\s*placed\s*==\s*null\s*\)\s*return").Index;
        int filledAt = Regex.Match(commit, @"int\s+filled\s*=\s*Fill\s*\(\s*placed\s*\)\s*;").Index;
        int chargedAt = Regex.Match(commit, ChargeCall).Index;
        Assert.True(placedAt > 0 && filledAt > placedAt && chargedAt > filledAt, "Fill only after the batch was placed, and before the charge.");
        Assert.Matches(@"^\{\s*GameObject\?\s+prefab\s*=\s*FillPrefab\?\.Invoke\s*\(\s*\)\s*;\s*if\s*\(\s*!FillEnabled\s*\|\|\s*prefab\s*==\s*null\s*\)\s*\{\s*_fills\.Clear\s*\(\s*\)\s*;\s*return\s+0\s*;\s*\}", MethodBody(runtime, @"private\s+int\s+Fill\s*\("));
        // The undo unfills once, after its own batch was placed.
        Assert.Equal(1, Count(runtime, @"(?<!\bvoid\s+)\bUnfill\s*\(\s*record\s*\)"));
        string undo = MethodBody(runtime, UndoSignature);
        int undonePlaced = Regex.Match(undo, @"DigRecord\?\s+placed\s*=\s*Place\s*\(\s*DigFlightKind\.Undo").Index;
        int unfilled = Regex.Match(undo, @"Unfill\s*\(\s*record\s*\)\s*;").Index;
        Assert.True(undonePlaced > 0 && unfilled > undonePlaced, "Unfill only after the undo's batch was placed.");
        // The survey sorts blocks out before blockers and anything listed for clearing.
        string survey = MethodBody(SourceText("Excavation/ExcavationWorldChecks.cs"), @"internal\s+int\s+Survey\s*\(");
        int item = Regex.Match(survey, @"GetComponentInParent<ItemDrop>\(\)\s*!=\s*null\s*\)\s*continue\s*;").Index;
        int fill = Regex.Match(survey, @"EarthFillPlacement\.IsFill\s*\(\s*view\.gameObject\s*\)\s*\)\s*\{\s*if\s*\(\s*_seen\.Add\s*\(\s*view\.gameObject\s*\)\s*\)\s*fills\.Add\s*\(\s*view\.gameObject\s*\)\s*;\s*continue\s*;\s*\}").Index;
        int block = Regex.Match(survey, @"if\s*\(\s*vehicle\s*\|\|\s*moving\s*\|\|\s*view\s*==\s*null").Index;
        Assert.True(item > 0 && fill > item && block > fill, "Earth blocks are sorted out after items and before blockers and the clearing lists.");
    }

    private static string SourceText(string relative) => Sources().Single(s => s.Path == relative).Text;

    /// <summary>The adapter properties a game-free test cannot see, pinned in the text that sets them. Removing or
    /// flipping any of them fails here.</summary>
    [Theory]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raise\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_square\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raiseDelta\s*=\s*delta\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raiseRadius\s*=\s*0f\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"if\s*\(\s*op\.GetRadius\s*\(\s*\)\s*!=\s*0f\s*\)\s*throw")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"_lower\s*=\s*Make\s*\(\s*container\.transform\s*,\s*LowerPrefabName\s*,\s*-Step\s*\)\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"_raise\s*=\s*Make\s*\(\s*container\.transform\s*,\s*RaisePrefabName\s*,\s*Step\s*\)\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"kind\s*==\s*DigFlightKind\.Dig\s*\?\s*target\s*\+\s*Step\s*:\s*target\s*-\s*Step")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_level\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_smooth\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_paintCleared\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"op\.m_spawnOnPlaced\s*=\s*null\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"CheckAccess\s*\([^;]*AccessCheckRadius\s*\(\s*footprint\s*\)\s*,\s*flash\s*:\s*true\s*,\s*wardCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"IsInside\s*\(\s*centre\s*,\s*radius\s*,\s*buildCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"float\s+radius\s*=\s*AccessCheckRadius\s*\(\s*footprint\s*\)\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"GetComponentInParent<Vagon>\(\)\s*!=\s*null\s*\|\|\s*hit\.GetComponentInParent<Ship>\(\)\s*!=\s*null")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"if\s*\(\s*!vehicle\s*&&\s*hit\.GetComponentInParent<WearNTear>\(\)\s*!=\s*null\s*\)")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*!ExcavationTerrainRules\.LatticeSupported\s*\(\s*tile\.m_width\s*,\s*tile\.m_scale\s*,\s*origin\.x\s*,\s*origin\.z\s*\)\s*\)\s*return\s+ExcavationReadFault\.UnsupportedLattice\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*tile\.m_doLateUpdate\s*!=\s*0\s*\)\s*return\s+ExcavationReadFault\.Rebuilding\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"WriteTo\s*\(\s*samples\s*,\s*x\s*,\s*z\s*,\s*ExcavationTerrainRules\.ExpectedCopies\s*\(\s*x\s*,\s*z\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"if\s*\(\s*op\s*==\s*null\s*\|\|\s*!_inFlight\.TryStart\s*\(\s*kind\s*,\s*record\s*,\s*Time\.time\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"DigPlanner\.Progress\s*\(\s*record\s*,\s*Read\s*\(\s*_check\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"DigUndo\.Progress\s*\(\s*record\s*,\s*Read\s*\(\s*_check\s*\)\s*\)")]
    public void TheAdapterPropertiesAreWhereTheyMustBe(string relative, string pattern)
    {
        Assert.Equal(1, Count(SourceText(relative), pattern));
    }

    [Fact]
    public void TheTerrainToolCallComesAfterTheSlotIsTaken()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        string place = MethodBody(runtime, PlaceSignature);
        int slot = place.IndexOf("_inFlight.TryStart(kind, record, Time.time)", StringComparison.Ordinal);
        int spawn = Regex.Match(place, @"\bSpawnOnHitTerrain\s*\(").Index;
        Assert.True(slot > 0 && spawn > slot, "The single SpawnOnHitTerrain call must be in the batch, after the in-flight slot.");
        // The batch is reached exactly twice: by a dig's commit and by an undo.
        Assert.Equal(2, Count(runtime, @"\bPlace\s*\(\s*DigFlightKind\.(Dig|Undo)\s*,\s*record\s*,"));
        Assert.Equal(1, Count(MethodBody(runtime, CommitSignature), @"\bPlace\s*\(\s*DigFlightKind\.Dig\b"));
        Assert.Equal(1, Count(MethodBody(runtime, UndoSignature), @"\bPlace\s*\(\s*DigFlightKind\.Undo\b"));
    }

    // ---- #487 slice 3: the Iron Shovel ----

    private static List<(string Path, int N)> Hits(string pattern) =>
        Sources().Select(s => (s.Path, N: Count(s.Text, pattern))).Where(h => h.N > 0).ToList();

    private static bool ExcavationCode(string path) =>
        path.StartsWith("Excavation/", StringComparison.Ordinal) || path.StartsWith("Domain/Excavation/", StringComparison.Ordinal) ||
        path.StartsWith("Items/", StringComparison.Ordinal);

    /// <summary>A durability write in any spelling: assignment, compound assignment, increment, or a ref.</summary>
    internal const string DurabilityWrite = @"\.m_durability\s*(=[^=]|\+=|-=|\*=|/=|\+\+|--)|\bref\s+[\w\.]*m_durability\b";

    /// <summary>A call of Charge (its definition, preceded by its return type, is not a call).</summary>
    internal const string ChargeCall = @"(?<!\bvoid\s+)\bCharge\s*\(";

    /// <summary>The cost is taken in one place: one durability write in the whole product and one UseStamina in the
    /// excavation code (the ladder's own climbing charge is the other product use), both in the runtime's Charge, and
    /// Charge is called once, after the spawn and after its refusal branch has returned.</summary>
    [Fact]
    public void TheChargeIsTakenOnceAfterTheSpawnAndNowhereElse()
    {
        foreach (string pattern in new[] { @"\bUseStamina\s*\(", DurabilityWrite, ChargeCall })
        {
            var hits = Hits(pattern).Where(h => pattern != @"\bUseStamina\s*\(" || ExcavationCode(h.Path)).ToList();
            Assert.True(hits.Count == 1 && hits[0].Path == "Excavation/ExcavationRuntime.cs" && hits[0].N == 1,
                pattern + " must appear exactly once, in Excavation/ExcavationRuntime.cs: " + string.Join(", ", hits));
        }
        // Stamina is never taken any other way in the excavation code.
        foreach (string pattern in new[] { @"\bAddStamina\s*\(", @"\bm_stamina\s*(=[^=]|\+=|-=|\*=|/=|\+\+|--)", @"\bRPC_UseStamina\b" })
        {
            Assert.Empty(Hits(pattern).Where(h => ExcavationCode(h.Path)));
        }
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        int spawn = Regex.Match(runtime, @"\bSpawnOnHitTerrain\s*\(").Index;
        // The end of the refusal branch: its return statement and the brace that closes it.
        Match branch = Regex.Match(runtime, @"refused\s*=\s*ExcavationCommitRefusal\.SpawnRefused\s*;\s*return\s+null\s*;\s*\}");
        int charge = Regex.Match(runtime, ChargeCall).Index;
        int stamina = Regex.Match(runtime, @"\bUseStamina\s*\(").Index;
        int durability = Regex.Match(runtime, DurabilityWrite).Index;
        int chargeBody = Regex.Match(runtime, @"private\s+static\s+void\s+Charge\s*\(").Index;
        Assert.True(spawn > 0 && branch.Success && branch.Index > spawn && charge >= branch.Index + branch.Length,
            "Charge must follow the spawn and the end of its refusal branch.");
        Assert.True(chargeBody > charge && stamina > chargeBody && durability > stamina, "Stamina, then durability, inside Charge.");
        // The charge is a dig's: inside Commit, after its batch; an undo charges nothing and refunds nothing.
        string commit = MethodBody(runtime, CommitSignature);
        int placed = Regex.Match(commit, @"\bPlace\s*\(").Index;
        Match charged = Regex.Match(commit, ChargeCall);
        Assert.True(charged.Success && charged.Index > placed, "Commit charges after its batch.");
        string undo = MethodBody(runtime, UndoSignature);
        foreach (string pattern in new[] { ChargeCall, @"\bUseStamina\s*\(", DurabilityWrite, @"\bAddStamina\s*\(" }) Assert.Equal(0, Count(undo, pattern));
    }

    /// <summary>No animation is played and no stored attack is reached: a swing clip's attack-trigger event runs whatever
    /// attack the player last made (independent review of slice 3).</summary>
    [Theory]
    [InlineData(@"\bSetTrigger\s*\(")]
    [InlineData(@"\bZSyncAnimation\b")]
    [InlineData(@"\bOnAttackTrigger\s*\(")]
    [InlineData(@"\bStartAttack\s*\(")]
    [InlineData(@"\bm_currentAttack\b")]
    public void TheShovelNeverAnimatesOrReachesAnAttack(string pattern)
    {
        Assert.Empty(Hits(pattern).Where(h => ExcavationCode(h.Path)));
    }

    /// <summary>The shovel's press is the only caller of the commit gate; the console cannot dig.</summary>
    [Fact]
    public void OnlyTheShovelPressReachesTheCommitGate()
    {
        // The runtime's presses (its own session's are its business) are reached from the input prefix alone.
        foreach (string press in new[] { @"(?<!_session)\.Primary\s*\(", @"(?<!_session)\.Secondary\s*\(" })
        {
            var calls = Hits(press);
            Assert.True(calls.Count == 1 && calls[0].Path == "Excavation/ExcavationShovelInput.cs" && calls[0].N == 1, press + ": " + string.Join(", ", calls));
        }

        // The dig and undo gates are reached only from the presses' own session actions, inside the runtime.
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        Assert.Equal(1, Count(runtime, @"(?<![\w.])DigWithShovel\s*\((?!\s*Player\b)"));
        Assert.Equal(1, Count(MethodBody(runtime, PrimarySignature), @"DigAction\.Dig\s*=>\s*DigWithShovel\s*\(\s*player\s*,\s*shovel\s*\)"));
        Assert.Equal(1, Count(runtime, @"(?<![\w.])UndoWithShovel\s*\((?!\s*Player\b)"));
        Assert.Equal(1, Count(MethodBody(runtime, SecondarySignature), @"case\s+DigAction\.Undo\s*:\s*return\s+UndoWithShovel\s*\(\s*player\s*,\s*shovel\s*\)"));
        Assert.Empty(Hits(@"\.(DigWithShovel|UndoWithShovel)\s*\("));
        string command = SourceText("Excavation/ExcavationCommand.cs");
        Assert.DoesNotMatch(@"\bDigWithShovel\b|\bUndoWithShovel\b|\bPrimary\b|\bSecondary\b|\bCommit\s*\(|\bChangeSize\b|\bRefreshPreview\b|\bPlace\s*\(", command);
        Assert.Equal(1, Count(command, @"(?<!\w)runtime\.PlanAtAim\s*\("));
        // Slice 4 (slice 2's follow-up): the command never keeps a runtime of its own, so one disposed by an install
        // fault cannot be reached through it; it asks the plugin for the current one on every use.
        Assert.DoesNotMatch(@"\breadonly\s+ExcavationRuntime\b|\bExcavationRuntime\s+_\w+\s*[;=]", command);
        Assert.Equal(1, Count(command, @"ExcavationRuntime\?\s+runtime\s*=\s*_current\s*\(\s*\)\s*;\s*if\s*\(\s*runtime\s*==\s*null\s*\)\s*return\b"));
        Assert.Equal(1, Count(SourceText("Plugin.cs"), @"new\s+ExcavationCommand\s*\(\s*\(\s*\)\s*=>\s*_excavation\s*\)"));
    }

    /// <summary>Whether the shovel and the player can pay is asked before any world check, so a refused payment never
    /// flashes a ward; and the input prefix only observes (a void prefix never skips the game's own input).</summary>
    [Theory]
    [InlineData("Excavation/ExcavationRuntime.cs", @"refusal\s*=\s*IronShovel\.CanPay\s*\(\s*Equipped\s*\(\s*player\s*,\s*shovel\s*\)\s*,\s*Ready\s*\(\s*player\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"ReferenceEquals\s*\(\s*player\.GetCurrentWeapon\s*\(\s*\)\s*,\s*shovel\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"player\.HaveStamina\s*\(\s*DigCost\.StaminaNeeded\s*\(\s*plan\.LoweredVolume\s*,\s*Game\.m_staminaRate\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"DigCost\.DurabilityCost\s*\(\s*plan\.LoweredVolume\s*,\s*Game\.m_durabilityRate\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"!player\.InAttack\(\)\s*&&\s*!player\.InDodge\(\)\s*&&\s*player\.CanMove\(\)\s*&&\s*!player\.IsStaggering\(\)\s*&&\s*!player\.IsKnockedBack\(\)\s*&&\s*!player\.InMinorAction\(\)\s*&&\s*!player\.IsTeleporting\(\)\s*;")]
    [InlineData("Plugin.cs", @"new\s+IronShovelItem\s*\([^;]*craftable\s*:\s*_shovelInput\s*!=\s*null\s*&&\s*_excavation\.Available\s*\)")]
    [InlineData("Items/IronShovelItem.cs", @"Enabled\s*=\s*_craftable\s*,")]
    [InlineData("Excavation/ExcavationShovelInput.cs", @"private\s+static\s+void\s+BeforeAttackInput\s*\(\s*Player\s+__instance\s*\)")]
    [InlineData("Excavation/ExcavationShovelInput.cs", @"ExcavationPreviewRules\.PressFor\s*\(\s*attack\s*\(\s*__instance\s*\)\s*,\s*secondary\s*\(\s*__instance\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationShovelInput.cs", @"ReferenceEquals\s*\(\s*__instance\s*,\s*Player\.m_localPlayer\s*\)")]
    [InlineData("Items/IronShovelItem.cs", @"shared\.m_attack\.m_attackAnimation\s*=\s*""""\s*;")]
    [InlineData("Items/IronShovelItem.cs", @"shared\.m_secondaryAttack\.m_attackAnimation\s*=\s*""""\s*;")]
    [InlineData("Items/IronShovelItem.cs", @"shared\.m_spawnOnHitTerrain\s*=\s*null\s*;")]
    [InlineData("Items/IronShovelItem.cs", @"shared\.m_maxQuality\s*=\s*1\s*;")]
    [InlineData("Items/IronShovelItem.cs", @"shared\.m_damages\s*=\s*default\s*\(\s*HitData\.DamageTypes\s*\)\s*;")]
    [InlineData("Items/IronShovelItem.cs", @"if\s*\(\s*item\.HavePrimaryAttack\s*\(\s*\)\s*\|\|\s*item\.HaveSecondaryAttack\s*\(\s*\)\s*\)\s*throw")]
    [InlineData("Items/IronShovelItem.cs", @"new\s+RequirementConfig\s*\(\s*IronShovel\.IronItem\s*,")]
    [InlineData("Items/IronShovelItem.cs", @"new\s+CustomItem\s*\(\s*IronShovel\.PrefabName\s*,\s*IronShovel\.DonorPrefab\s*,\s*config\s*\)")]
    public void TheShovelPropertiesAreWhereTheyMustBe(string relative, string pattern)
    {
        Assert.Equal(1, Count(SourceText(relative), pattern));
    }

    /// <summary>The attack animations are written exactly twice (both cleared), and both writes come before the check that
    /// refuses a clone with an attack left, so nothing can restore one after it.</summary>
    [Fact]
    public void TheClearedAttackCannotBeRestoredAfterTheCheck()
    {
        string item = SourceText("Items/IronShovelItem.cs");
        Assert.Equal(2, Count(item, @"\bm_attackAnimation\s*(=[^=]|\+=)"));
        int check = Regex.Match(item, @"item\.HavePrimaryAttack\s*\(").Index;
        Assert.True(check > 0);
        Assert.All(Regex.Matches(item, @"\bm_attackAnimation\s*=[^=]").Cast<Match>(), m => Assert.True(m.Index < check));
        Assert.Equal(0, Count(item.Substring(check), @"\bm_(secondaryA|a)ttack\b\s*(=[^=]|\.)"));
    }

    [Fact]
    public void TheCostIsAskedBeforeTheWorldChecks()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        int planner = Regex.Match(runtime, @"DigGate\.BeforeWorldChecks\s*\(").Index;
        int pay = Regex.Match(runtime, @"IronShovel\.CanPay\s*\(").Index;
        int ward = Regex.Match(runtime, @"ExcavationWorldChecks\.WardAllows\s*\(").Index;
        Assert.True(planner > 0 && pay > planner && ward > pay, "CanPay must sit between the planner gate and the ward check.");
    }

    /// <summary>With the switch off nothing is added: the shovel, its input and the console diagnostic are constructed only
    /// inside the switch's branch of the plugin, and nowhere else.</summary>
    [Fact]
    public void TheShovelExistsOnlyBehindTheSwitch()
    {
        foreach (string pattern in new[] { @"new\s+IronShovelItem\s*\(", @"new\s+ExcavationShovelInput\s*\(", @"new\s+ExcavationCommand\s*\(", @"new\s+ExcavationPreview\s*\(" })
        {
            var hits = Hits(pattern);
            Assert.True(hits.Count == 1 && hits[0].Path == "Plugin.cs" && hits[0].N == 1, pattern + ": " + string.Join(", ", hits));
        }
        string plugin = SourceText("Plugin.cs");
        Match branch = Regex.Match(plugin, @"if\s*\(\s*enabled\.Value\s*\)\s*\{(?<body>[^{}]*)\}");
        Assert.True(branch.Success, "The switch's branch was not found.");
        string body = branch.Groups["body"].Value;
        Assert.Matches(@"new\s+IronShovelItem\s*\(", body);
        Assert.Matches(@"new\s+ExcavationShovelInput\s*\(", body);
        Assert.Matches(@"new\s+ExcavationCommand\s*\(", body);
        Assert.Matches(@"new\s+ExcavationPreview\s*\(", body);
        Assert.Matches(@"Config\.Bind\s*\(\s*""Excavation""\s*,\s*""Enabled""\s*,\s*false\s*,", plugin);
    }

    /// <summary>The item folder clones and configures an item; it never changes terrain or the player.</summary>
    [Theory]
    [InlineData(@"\bSpawnOnHitTerrain\s*\(")]
    [InlineData(@"\bUseStamina\s*\(")]
    [InlineData(@"\bInvokeRPC\s*\(")]
    [InlineData(@"\bInstantiate\s*\(")]
    [InlineData(@"\bGetZDO\s*\(")]
    [InlineData(@"\.Damage\s*\(")]
    [InlineData(@"\bDestroy(Immediate)?\s*\(")]
    public void TheItemRegistrationChangesNothingElse(string pattern)
    {
        foreach ((string path, string text) in Sources("Items"))
        {
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
    }

    // ---- #487 slice 4: the preview, the confirmation, and slice 3's follow-ups ----

    /// <summary>The body of the first method whose signature matches, braces counted (comments are already stripped).</summary>
    internal static string MethodBody(string text, string signature)
    {
        Match head = Regex.Match(text, signature);
        Assert.True(head.Success, "Signature not found: " + signature);
        int open = text.IndexOf('{', head.Index + head.Length);
        Assert.True(open > 0, "No body after " + signature);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("Unbalanced body after " + signature);
    }

    internal const string RefreshSignature = @"internal\s+bool\s+RefreshPreview\s*\(";
    internal const string EvaluateSignature = @"private\s+ExcavationCommitRefusal\s+Evaluate\s*\(";
    internal const string DigSignature = @"internal\s+string\s+DigWithShovel\s*\(";
    internal const string UndoSignature = @"internal\s+string\s+UndoWithShovel\s*\(";
    internal const string CommitSignature = @"private\s+string\s+Commit\s*\(";
    internal const string PlaceSignature = @"private\s+DigRecord\?\s+Place\s*\(";
    internal const string PrimarySignature = @"internal\s+string\s+Primary\s*\(";
    internal const string SecondarySignature = @"internal\s+string\s+Secondary\s*\(";

    /// <summary>Aiming, switching size or moving never changes the world: the preview's refresh and the shared evaluation it
    /// uses contain no commit, charge, spawn or durability write, and the preview adapter none either.</summary>
    [Theory]
    [InlineData(@"\bCommit\s*\(")]
    [InlineData(ChargeCall)]
    [InlineData(@"\bSpawnOnHitTerrain\b")]
    [InlineData(@"\bUseStamina\s*\(")]
    [InlineData(@"\bTryStart\s*\(")]
    [InlineData(@"\bDigWithShovel\b")]
    [InlineData(@"\bUndoWithShovel\b")]
    [InlineData(@"\bPlace\s*\(")]
    [InlineData(DurabilityWrite)]
    public void ThePreviewPathNeverCommits(string pattern)
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        Assert.Equal(0, Count(MethodBody(runtime, RefreshSignature), pattern));
        Assert.Equal(0, Count(MethodBody(runtime, EvaluateSignature), pattern));
        Assert.Equal(0, Count(MethodBody(runtime, @"internal\s+void\s+ChangeSize\s*\("), pattern));
        Assert.Equal(0, Count(SourceText("Excavation/ExcavationPreview.cs"), pattern));
    }

    [Fact]
    public void ThePreviewAsksQuietlyAndOnlyThePressFlashes()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        string refresh = MethodBody(runtime, RefreshSignature);
        string evaluate = MethodBody(runtime, EvaluateSignature);
        string dig = MethodBody(runtime, DigSignature);
        Assert.Equal(1, Count(refresh, @"\bEvaluate\s*\([^;]*committing\s*:\s*false\b"));
        Assert.Equal(1, Count(refresh, @"\bEvaluate\s*\("));
        Assert.Equal(1, Count(dig, @"\bEvaluate\s*\([^;]*committing\s*:\s*true\b"));
        Assert.Equal(1, Count(dig, @"\bEvaluate\s*\("));
        Assert.Equal(1, Count(refresh, @"\bEvaluate\s*\(\s*footprint\s*,"));
        Assert.Equal(1, Count(dig, @"\bEvaluate\s*\(\s*_shown\.Footprint\s*,"));
        Assert.Equal(2, Count(runtime, @"\bEvaluate\s*\((?!\s*DigFootprint)"));
        // The flashing ward check and the stamina flash happen only when committing.
        Assert.Equal(1, Count(evaluate, @"committing\s*\?\s*ExcavationWorldChecks\.WardAllows\s*\(\s*footprint\s*,\s*plan\.Datum\s*\)\s*:\s*ExcavationWorldChecks\.WardAllowsQuietly\s*\(\s*footprint\s*,\s*plan\.Datum\s*\)"));
        // The ward flashes for a press only: the dig's evaluation when committing, and the undo press.
        Assert.Equal(2, Count(runtime, @"ExcavationWorldChecks\.WardAllows\s*\("));
        Assert.Equal(1, Count(MethodBody(runtime, UndoSignature), @"ExcavationWorldChecks\.WardAllows\s*\("));
        Assert.Equal(1, Count(runtime, @"\bFlashStamina\s*\(\s*\)\s*;"));
        Assert.Equal(1, Count(evaluate, @"if\s*\(\s*committing\s*&&\s*refusal\s*==\s*ExcavationCommitRefusal\.NotEnoughStamina\s*\)\s*FlashStamina\s*\(\s*\)\s*;"));
        string checks = SourceText("Excavation/ExcavationWorldChecks.cs");
        Assert.Equal(1, Count(checks, @"WardAllowsQuietly\s*\([^)]*\)\s*=>\s*PrivateArea\.CheckAccess\s*\([^;]*AccessCheckRadius\s*\(\s*footprint\s*\)\s*,\s*flash\s*:\s*false\s*,\s*wardCheck\s*:\s*true\s*\)"));
        Assert.Equal(2, Hits(@"\bCheckAccess\s*\(").Where(h => ExcavationCode(h.Path)).Sum(h => h.N));
        Assert.Empty(Hits(@"\bFlashShield\s*\(").Where(h => ExcavationCode(h.Path)));
    }

    /// <summary>A press digs only what the preview shows: the confirmation comes before any terrain read, the fresh plan is
    /// compared with the shown one before the commit, and the commit is reached once, from the press.</summary>
    [Fact]
    public void ThePressConfirmsThePreviewBeforeItCommits()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        string dig = MethodBody(runtime, DigSignature);
        int ladder = Regex.Match(dig, @"if\s*\(\s*OnLadder\s*\(\s*\)\s*\)\s*return").Index;
        int confirm = Regex.Match(dig, @"refusal\s*=\s*_shown\.Confirm\s*\(\s*Time\.frameCount\s*,").Index;
        int confirmed = Regex.Match(dig, @"if\s*\(\s*refusal\s*!=\s*ExcavationCommitRefusal\.None\s*\)\s*return").Index;
        int evaluate = Regex.Match(dig, @"\bEvaluate\s*\(").Index;
        int matches = Regex.Match(dig, @"if\s*\(\s*!_shown\.Matches\s*\(\s*plan\s*\)\s*\)").Index;
        int commit = Regex.Match(dig, @"return\s+Commit\s*\(\s*plan\b").Index;
        Assert.True(ladder > 0 && confirm > ladder && confirmed > confirm && evaluate > confirmed && matches > evaluate && commit > matches,
            "DigWithShovel must refuse on a ladder, then confirm the preview, then evaluate, then match, then commit.");
        // The rectangle comes from the preview, never from a fresh aim.
        Assert.Matches(@"Evaluate\s*\(\s*_shown\.Footprint\s*,", dig);
        Assert.DoesNotMatch(@"(?<![.\w])Aim\s*\(", dig);
        Assert.Equal(1, Count(runtime, @"\bCommit\s*\(\s*plan\b"));
        // The size change (#574) only changes the session's size (and asks for a fresh read).
        string depth = MethodBody(runtime, @"internal\s+void\s+ChangeSize\s*\(");
        Assert.Matches(@"^\{\s*if\s*\(\s*_session\.ChangeSize\s*\(\s*steps\s*\)\s*\)\s*_lastPreviewRead\s*=\s*float\.NaN\s*;\s*\}$", depth);
        var depths = Hits(@"\.ChangeSize\s*\(");
        Assert.True(depths.Sum(h => h.N) == 2 && depths.All(h => h.Path == "Excavation/ExcavationRuntime.cs" || h.Path == "Excavation/ExcavationPreview.cs"), string.Join(", ", depths));
        string input = SourceText("Excavation/ExcavationShovelInput.cs");
        Assert.Matches(@"if\s*\(\s*press\s*==\s*ExcavationPress\.None\s*\|\|\s*ExcavationPreview\.UiOpen\s*\(\s*\)\s*\)\s*return\s*;", input);
        Assert.Matches(@"string\s+line\s*=\s*press\s*==\s*ExcavationPress\.Primary\s*\?\s*runtime\.Primary\s*\(\s*__instance\s*,\s*weapon\s*\)\s*:\s*runtime\.Secondary\s*\(\s*__instance\s*,\s*weapon\s*\)\s*;", input);
        // Only the preview's own refresh (driven by the plugin's frame) reads for the preview.
        var refreshes = Hits(@"\.RefreshPreview\s*\(");
        Assert.True(refreshes.Count == 1 && refreshes[0].Path == "Excavation/ExcavationPreview.cs" && refreshes[0].N == 1, string.Join(", ", refreshes));
    }

    /// <summary>The targeting rule is the game's build-placement ray: the first thing hit must be terrain (or, since #579, an
    /// earth block, pinned in <see cref="AnEarthBlockIsTheOnlyNonTerrainAim"/> with the reach rule), triggers ignored; and the
    /// shovel does nothing from a ladder.</summary>
    [Theory]
    [InlineData(@"_aimMask\s*=\s*LayerMask\.GetMask\s*\(\s*""Default""\s*,\s*""static_solid""\s*,\s*""Default_small""\s*,\s*""piece""\s*,\s*""piece_nonsolid""\s*,\s*""terrain""\s*,\s*""vehicle""\s*\)\s*;")]
    [InlineData(@"Physics\.Raycast\s*\(\s*eye\.position\s*,\s*eye\.forward\s*,\s*out\s+RaycastHit\s+hit\s*,\s*ExcavationLimits\.AimRayLength\s*,\s*_aimMask\s*,\s*QueryTriggerInteraction\.Ignore\s*\)")]
    [InlineData(@"if\s*\(\s*collider\s*==\s*null\s*\|\|\s*collider\.GetComponent<Heightmap>\s*\(\s*\)\s*==\s*null\s*\)")]
    [InlineData(@"private\s+static\s+bool\s+OnLadder\s*\(\s*\)\s*=>\s*LadderRuntime\.Current\?\.IsClimbing\s*==\s*true\s*;")]
    public void TheTargetingRuleIsWhereItMustBe(string pattern)
    {
        Assert.Equal(1, Count(SourceText("Excavation/ExcavationRuntime.cs"), pattern));
    }

    /// <summary>#579: aiming at an earth block digs into it. Only an earth block is let through; anything else that is not
    /// terrain still blocks; the reach rule applies to it the same; and the aim is the block's own cell.</summary>
    [Fact]
    public void AnEarthBlockIsTheOnlyNonTerrainAim()
    {
        string aim = MethodBody(SourceText("Excavation/ExcavationRuntime.cs"), @"private\s+ExcavationAim\s+Aim\s*\(");
        Assert.Matches(@"if\s*\(\s*view\s*==\s*null\s*\|\|\s*!EarthFillPlacement\.IsFill\s*\(\s*view\.gameObject\s*\)\s*\)\s*\{\s*blocker\s*=\s*collider\s*;\s*return\s+ExcavationAim\.Blocked\s*;\s*\}", aim);
        Assert.Equal(2, Count(aim, @"if\s*\(\s*Vector3\.Distance\s*\(\s*player\.m_eye\.position\s*,\s*hit\.point\s*\)\s*>\s*ExcavationLimits\.Reach\s*\)\s*return\s+ExcavationAim\.TooFar\s*;"));
        Assert.Matches(@"\(\s*x\s*,\s*z\s*\)\s*=\s*EarthFill\.AimVertex\s*\(\s*block\s*\)\s*;", aim);
        Assert.Equal(1, Count(aim, @"return\s+ExcavationAim\.Blocked\s*;"));
    }

    /// <summary>The preview is a bounded, pooled, local visual: one root, a fixed number of line renderers created only
    /// when there is no root yet, one material, one HUD text; no network view, no ZDO, nothing instantiated from a prefab,
    /// nothing destroyed, no collider.</summary>
    [Fact]
    public void ThePreviewIsABoundedLocalVisual()
    {
        string preview = SourceText("Excavation/ExcavationPreview.cs");
        foreach (string forbidden in new[]
                 {
                     @"\bZNetView\b", @"\bZDO\b", @"\bZNetScene\b", @"\bZRoutedRpc\b", @"\bInstantiate\s*\(", @"\bDontDestroyOnLoad\s*\(",
                     @"\bDestroy(Immediate)?\s*\(", @"\bHideFlags\b", @"\bAddComponent<(?!LineRenderer>|TextMeshProUGUI>)", @"\bAddComponent\s*\(",
                     @"\bEvaluate\s*\(", @"\bSwitchMode\s*\(", @"\bShowMessage\s*\(", @"\bPhysics\.", @"\bCollider\b",
                 })
        {
            Assert.True(Count(preview, forbidden) == 0, "ExcavationPreview.cs uses " + forbidden + ".");
        }
        Assert.Equal(3, Count(preview, @"\bnew\s+GameObject\s*\("));
        Assert.Equal(1, Count(preview, @"\bAddComponent<LineRenderer>\s*\("));
        Assert.Equal(1, Count(preview, @"\bAddComponent<TextMeshProUGUI>\s*\("));
        Assert.Equal(1, Count(preview, @"\bnew\s+Material\s*\("));
        Assert.Equal(1, Count(preview, @"new\s+LineRenderer\?\[\s*DigPreviewShape\.LineCount\s*\]"));
        string ensure = MethodBody(preview, @"private\s+bool\s+EnsureLines\s*\(");
        Assert.Matches(@"^\{\s*if\s*\(\s*_root\s*!=\s*null\s*\)\s*return\s+false\s*;", ensure);
        Assert.Matches(@"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<\s*_lines\.Length\s*;\s*i\+\+\s*\)", ensure);
        Assert.Equal(2, Count(ensure, @"\bnew\s+GameObject\s*\("));
        string label = MethodBody(preview, @"private\s+TextMeshProUGUI\?\s+Label\s*\(");
        Assert.Matches(@"^\{\s*if\s*\(\s*_label\s*!=\s*null\s*\)\s*return\s+_label\s*;", label);
        string material = MethodBody(preview, @"private\s+Material\?\s+Material\s*\(");
        Assert.Matches(@"^\{\s*if\s*\(\s*_material\s*!=\s*null\s*\)\s*return\s+_material\s*;\s*if\s*\(\s*_shaderMissing\s*\)\s*return\s+null\s*;", material);
        Assert.Equal(1, Count(material, @"\bnew\s+Material\s*\("));
    }

    /// <summary>Any write to a recipe's enabled flag other than turning it off: a member write or an object initialiser.</summary>
    internal const string EnableWrite = @"\bm_enabled\s*=(?!=)(?!\s*false\s*;)";

    /// <summary>Slice 3's follow-up: a fault later in the session turns the recipe off, and nothing in the product turns a
    /// recipe on.</summary>
    [Fact]
    public void ALaterFaultDisablesTheRecipe()
    {
        Assert.Equal(1, Count(SourceText("Plugin.cs"), @"_excavation\.Faulted\s*=\s*\(\s*\)\s*=>\s*_shovel\?\.DisableRecipe\s*\(\s*\)\s*;"));
        string item = SourceText("Items/IronShovelItem.cs");
        Assert.Equal(1, Count(item, @"internal\s+void\s+DisableRecipe\s*\(\s*\)"));
        Assert.Equal(1, Count(item, @"\brecipe\.m_enabled\s*=\s*false\s*;"));
        Assert.Empty(Hits(EnableWrite).Where(h => ExcavationCode(h.Path)));
        Assert.Equal(1, Count("recipe.m_enabled = true;", EnableWrite));
        Assert.Equal(1, Count("recipe.m_enabled = _craftable;", EnableWrite));
        Assert.Equal(1, Count("var r = new Recipe { m_enabled = true };", EnableWrite));
        Assert.Equal(0, Count("if (recipe.m_enabled == false) return;", EnableWrite));
        Assert.Equal(0, Count("recipe.m_enabled = false;", EnableWrite));
        // The item config's one Enabled is the start-up decision, and nothing else in the item folder sets one.
        Assert.Equal(1, Hits(@"\bEnabled\s*=(?!=)").Where(h => h.Path.StartsWith("Items/", StringComparison.Ordinal)).Sum(h => h.N));
        Assert.Equal(1, Count(item, @"\bEnabled\s*=\s*_craftable\s*,"));
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        string fault = MethodBody(runtime, @"private\s+string\s+Fault\s*\(");
        Assert.Matches(@"_faulted\s*=\s*true\s*;", fault);
        Assert.Matches(@"if\s*\(\s*first\s*\)\s*\{\s*try\s*\{\s*Faulted\?\.Invoke\s*\(\s*\)\s*;", fault);
        // A preview fault stops excavation too, because a press needs the preview.
        Assert.Matches(@"_runtime\.StopAfterFault\s*\(\s*exception\s*\)", SourceText("Excavation/ExcavationPreview.cs"));
        // A disposed runtime is unavailable.
        Assert.Matches(@"internal\s+bool\s+Available\s*=>[^;]*!_faulted\s*&&\s*!_disposed\s*;", runtime);
    }

    /// <summary>Independent review of slice 4, S2: what makes the edge after a closed inventory harmless lives in the
    /// adapter, so it is pinned there. The preview hides (and tells the runtime, which restarts the dwell) whenever any UI
    /// is open; a free cursor counts as UI; the refresh and the confirmation use the same two clocks.</summary>
    [Fact]
    public void ThePreviewHidesWhileAnyUiIsOpenAndBothSidesUseOneClock()
    {
        string preview = SourceText("Excavation/ExcavationPreview.cs");
        string update = MethodBody(preview, @"internal\s+void\s+Update\s*\(");
        Assert.Matches(@"bool\s+visible\s*=[^;]*IronShovel\.IsShovelName\s*\(\s*shovel\.m_shared\.m_name\s*\)\s*&&\s*!UiOpen\s*\(\s*\)\s*;", update);
        Assert.Matches(@"if\s*\(\s*!visible\s*\)\s*\{\s*_runtime\.HidePreview\s*\(\s*\)\s*;", update);
        Assert.Matches(@"_runtime\.RefreshPreview\s*\(\s*player!\s*,\s*shovel!\s*,\s*Time\.frameCount\s*,\s*Time\.realtimeSinceStartup\s*\)", update);
        string ui = MethodBody(preview, @"internal\s+static\s+bool\s+UiOpen\s*\(");
        foreach (string screen in new[]
                 {
                     @"InventoryGui\.IsVisible\s*\(\s*\)", @"Menu\.IsVisible\s*\(\s*\)", @"Minimap\.IsOpen\s*\(\s*\)", @"Console\.IsVisible\s*\(\s*\)",
                     @"TextInput\.IsVisible\s*\(\s*\)", @"StoreGui\.IsVisible\s*\(\s*\)", @"Hud\.IsPieceSelectionVisible\s*\(\s*\)", @"Hud\.InRadial\s*\(\s*\)",
                     @"GameCamera\.InFreeFly\s*\(\s*\)", @"chat\.HasFocus\s*\(\s*\)", @"ZCursor\.LockState\s*!=\s*CursorLockMode\.Locked",
                 })
        {
            Assert.Matches(screen, ui);
        }
        // A pure "any of these" test: no condition can narrow another away.
        Assert.DoesNotContain("&&", ui.Replace("(chat != null && chat.HasFocus())", ""));
        string dig = MethodBody(SourceText("Excavation/ExcavationRuntime.cs"), DigSignature);
        Assert.Matches(@"_shown\.Confirm\s*\(\s*Time\.frameCount\s*,\s*Time\.realtimeSinceStartup\s*\)", dig);
        // A stopped session says so before it asks for the outline (review N5).
        int available = Regex.Match(dig, @"if\s*\(\s*!Available\s*\)\s*return\s+DigText\.Refusal\s*\(\s*ExcavationCommitRefusal\.Unavailable").Index;
        int confirm = Regex.Match(dig, @"_shown\.Confirm\s*\(").Index;
        Assert.True(available > 0 && confirm > available, "DigWithShovel must refuse Unavailable before the confirmation.");
        // No preview, and so no promise of a dig, when the press could not be installed (review N2).
        Assert.Equal(1, Count(SourceText("Plugin.cs"), @"_preview\s*=\s*_shovelInput\s*!=\s*null\s*\?\s*new\s+ExcavationPreview\s*\("));
    }

    /// <summary>#551: an undo restores only the ground the dig left, refusing first (in this order) when the ground has
    /// changed since, a ward or no-build area covers it, or anything - a building piece included - stands in the hole.</summary>
    [Fact]
    public void TheUndoChecksEverythingBeforeItsBatch()
    {
        string undo = MethodBody(SourceText("Excavation/ExcavationRuntime.cs"), UndoSignature);
        int check = Regex.Match(undo, @"DigUndo\.Check\s*\(\s*record\s*,\s*Read\s*\(\s*_check\s*\)\s*\)").Index;
        int ward = Regex.Match(undo, @"if\s*\(\s*!ExcavationWorldChecks\.WardAllows\s*\(").Index;
        int noBuild = Regex.Match(undo, @"if\s*\(\s*_world\.InNoBuildArea\s*\(").Index;
        Match inside = Regex.Match(undo, @"bool\s+occupied\s*=\s*overflow\s*\|\|\s*obstructions\s*>\s*0\s*\|\|\s*pieces\s*>\s*0\s*\|\|\s*_pick\.Count\s*>\s*0\s*\|\|\s*_remove\.Count\s*>\s*0\s*\|\|\s*FillInside\s*\(\s*record\.Footprint\s*\)\s*;[^}]*?if\s*\(\s*occupied\s*\)\s*return\s+DigText\.Undo\s*\(\s*UndoRefusal\.PieceInside\s*\)\s*;");
        int place = Regex.Match(undo, @"\bPlace\s*\(").Index;
        Assert.True(check > 0 && ward > check && noBuild > ward && inside.Success && inside.Index > noBuild && place > inside.Index,
            "UndoWithShovel must check the record, the ward, no-build areas and what stands in the hole, in that order, before its batch.");
        Assert.Matches(@"DigRecord\?\s+record\s*=\s*_history\.Last\s*;", undo);
        // Only a completed dig is offered for undo, and only a completed undo takes it off the list.
        string update = MethodBody(SourceText("Excavation/ExcavationRuntime.cs"), @"internal\s+void\s+Update\s*\(");
        Assert.Matches(@"if\s*\(\s*outcome\s*==\s*ExcavationOutcome\.Complete\s*\)\s*\{\s*if\s*\(\s*kind\s*==\s*DigFlightKind\.Dig\s*\)\s*_history\.Push\s*\(\s*record\s*\)\s*;\s*else\s+_history\.Remove\s*\(\s*record\s*\)\s*;\s*\}", update);
        Assert.Equal(1, Count(SourceText("Excavation/ExcavationRuntime.cs"), @"_history\.Push\s*\("));
    }

    /// <summary>#551: the shovel borrows the wheel by holding the camera's own distance across its update, and writes
    /// nothing else of the camera's. #580: the held distance is also put back right before the camera is placed from it,
    /// only while a hold is on, and the hold ends with the update.</summary>
    [Fact]
    public void TheCameraHoldRestoresOnlyTheCamerasOwnDistance()
    {
        string input = SourceText("Excavation/ExcavationShovelInput.cs");
        Assert.Equal(1, Count(input, @"AccessTools\.FieldRefAccess<GameCamera,\s*float>\s*\(\s*""m_distance""\s*\)"));
        Assert.Equal(1, Count(input, @"distance\s*\(\s*__instance\s*\)\s*=\s*__state\s*;"));
        Assert.Equal(1, Count(input, @"AccessTools\.Method\s*\(\s*typeof\s*\(\s*GameCamera\s*\)\s*,\s*""GetCameraPosition"""));
        string position = MethodBody(input, @"private\s+static\s+void\s+BeforeCameraPosition\s*\(");
        Assert.Matches(@"if\s*\(\s*distance\s*!=\s*null\s*&&\s*!float\.IsNaN\s*\(\s*_held\s*\)\s*\)\s*distance\s*\(\s*__instance\s*\)\s*=\s*_held\s*;", position);
        Assert.Equal(1, Count(input, @"distance\s*\(\s*__instance\s*\)\s*=\s*_held\s*;"));
        Assert.Matches(@"_held\s*=\s*__state\s*;\s*\}\s*$", MethodBody(input, @"private\s+static\s+void\s+BeforeCameraUpdate\s*\("));
        Assert.Matches(@"_held\s*=\s*float\.NaN\s*;\s*\}\s*$", MethodBody(input, @"private\s+static\s+void\s+AfterCameraUpdate\s*\("));
        Assert.Equal(1, Count(input, @"if\s*\(\s*distance\s*!=\s*null\s*&&\s*HoldCamera\?\.Invoke\s*\(\s*\)\s*==\s*true\s*\)\s*__state\s*=\s*distance\s*\(\s*__instance\s*\)\s*;"));
        Assert.DoesNotMatch(@"GameCamera\s*\.\s*instance\s*\.\s*m_\w+\s*=", input);
        Assert.Equal(1, Count(SourceText("Plugin.cs"), @"ExcavationShovelInput\.HoldCamera\s*=\s*\(\s*\)\s*=>\s*_preview\?\.UsingWheel\s*==\s*true\s*;"));
    }

    [Fact]
    public void TheAuditSeesWhatItLooksFor()
    {
        // The audit must fail when the protected property is removed: a planted second spawn and a planted RPC are seen.
        string planted = StripComments("x.SpawnOnHitTerrain(a); // SpawnOnHitTerrain( in a comment\n view.InvokeRPC(\"x\");");
        Assert.Equal(1, Count(planted, @"\bSpawnOnHitTerrain\s*\("));
        Assert.Equal(1, Count(planted, @"\bInvokeRPC\s*\("));
        Assert.Equal(1, Count("levels[index] = 0f;", @"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)"));
        Assert.Equal(0, Count("float level = levels[index];", @"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)"));
        // Slice 3: a second durability write in any spelling is seen, a comparison is not.
        Assert.Equal(1, Count("item.m_durability -= 4f;", DurabilityWrite));
        Assert.Equal(1, Count("item.m_durability = 0f;", DurabilityWrite));
        Assert.Equal(1, Count("item.m_durability--;", DurabilityWrite));
        Assert.Equal(1, Count("item.m_durability *= 0.5f;", DurabilityWrite));
        Assert.Equal(1, Count("Wear(ref item.m_durability);", DurabilityWrite));
        Assert.Equal(0, Count("if (item.m_durability == 0f) return;", DurabilityWrite));
        Assert.Equal(0, Count("float max = item.m_shared.m_maxDurability;", DurabilityWrite));
        // A Charge call under any argument name is seen; its definition is not.
        Assert.Equal(1, Count("Charge(__instance, weapon, 1f, 1f);", ChargeCall));
        Assert.Equal(0, Count("private static void Charge(Player player, ItemDrop.ItemData shovel)", ChargeCall));
    }
}
