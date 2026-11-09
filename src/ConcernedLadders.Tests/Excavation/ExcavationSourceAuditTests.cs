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
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
        foreach ((string path, string text) in Sources("Domain/Excavation"))
        {
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
    }

    [Fact]
    public void OnlyTheAdaptersOwnContainerIsEverDestroyed()
    {
        var hits = Sources("Excavation").Select(s => (s.Path, N: Count(s.Text, @"\bDestroy\s*\("))).Where(h => h.N > 0).ToList();
        Assert.Single(hits);
        Assert.Equal("Excavation/ExcavationTerrainOps.cs", hits[0].Path);
        Assert.Equal(1, hits[0].N);
        Assert.Matches(@"Object\.Destroy\s*\(\s*_container\s*\)", Sources("Excavation").Single(s => s.Path == hits[0].Path).Text);
    }

    private static string SourceText(string relative) => Sources().Single(s => s.Path == relative).Text;

    /// <summary>The adapter properties a game-free test cannot see, pinned in the text that sets them. Removing or
    /// flipping any of them fails here.</summary>
    [Theory]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raise\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_square\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raiseDelta\s*=\s*-geometry\.Depth\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_level\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_smooth\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_paintCleared\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"op\.m_spawnOnPlaced\s*=\s*null\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"CheckAccess\s*\([^;]*AccessCheckRadius\s*,\s*flash\s*:\s*true\s*,\s*wardCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"IsInside\s*\(\s*centre\s*,\s*radius\s*,\s*buildCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"float\s+radius\s*=\s*plan\.Geometry\.AccessCheckRadius\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"GetComponentInParent<Vagon>\(\)\s*!=\s*null\s*\|\|\s*hit\.GetComponentInParent<Ship>\(\)\s*!=\s*null")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"if\s*\(\s*!vehicle\s*&&\s*hit\.GetComponentInParent<WearNTear>\(\)\s*!=\s*null\s*\)")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*!ExcavationTerrainRules\.LatticeSupported\s*\(\s*tile\.m_width\s*,\s*tile\.m_scale\s*,\s*origin\.x\s*,\s*origin\.z\s*\)\s*\)\s*return\s+ExcavationReadFault\.UnsupportedLattice\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*tile\.m_doLateUpdate\s*!=\s*0\s*\)\s*return\s+ExcavationReadFault\.Rebuilding\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"WriteTo\s*\(\s*samples\s*,\s*dx\s*,\s*dz\s*,\s*ExcavationTerrainRules\.ExpectedCopies\s*\(")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"if\s*\(\s*op\s*==\s*null\s*\|\|\s*!_inFlight\.TryStart\s*\(\s*plan\s*,\s*Time\.time\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"ExcavationPlanner\.Check\s*\(\s*plan\s*,\s*after\s*\)")]
    public void TheAdapterPropertiesAreWhereTheyMustBe(string relative, string pattern)
    {
        Assert.Equal(1, Count(SourceText(relative), pattern));
    }

    [Fact]
    public void TheTerrainToolCallComesAfterTheSlotIsTaken()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        int slot = runtime.IndexOf("_inFlight.TryStart(plan, Time.time)", StringComparison.Ordinal);
        int spawn = Regex.Match(runtime, @"\bSpawnOnHitTerrain\s*\(").Index;
        Assert.True(slot > 0 && spawn > slot, "The single SpawnOnHitTerrain call must follow the in-flight slot.");
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
        Match branch = Regex.Match(runtime, @"return\s+ExcavationReport\.Commit\s*\(\s*ExcavationCommitRefusal\.SpawnRefused[^;]*;\s*\}");
        int charge = Regex.Match(runtime, ChargeCall).Index;
        int stamina = Regex.Match(runtime, @"\bUseStamina\s*\(").Index;
        int durability = Regex.Match(runtime, DurabilityWrite).Index;
        int chargeBody = Regex.Match(runtime, @"private\s+static\s+void\s+Charge\s*\(").Index;
        Assert.True(spawn > 0 && branch.Success && branch.Index > spawn && charge >= branch.Index + branch.Length,
            "Charge must follow the spawn and the end of its refusal branch.");
        Assert.True(chargeBody > charge && stamina > chargeBody && durability > stamina, "Stamina, then durability, inside Charge.");
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
        var calls = Hits(@"\.DigWithShovel\s*\(");
        Assert.Single(calls);
        Assert.Equal("Excavation/ExcavationShovelInput.cs", calls[0].Path);
        Assert.Equal(1, calls[0].N);
        string command = SourceText("Excavation/ExcavationCommand.cs");
        Assert.DoesNotMatch(@"\bDigWithShovel\b|\bDigAtAim\b|\bCommit\s*\(", command);
        Assert.Equal(1, Count(command, @"_runtime\.PlanAtAim\s*\("));
    }

    /// <summary>Whether the shovel and the player can pay is asked before any world check, so a refused payment never
    /// flashes a ward; and the input prefix only observes (a void prefix never skips the game's own input).</summary>
    [Theory]
    [InlineData("Excavation/ExcavationRuntime.cs", @"refusal\s*=\s*IronShovel\.CanPay\s*\(\s*Equipped\s*\(\s*player\s*,\s*shovel\s*\)\s*,\s*Ready\s*\(\s*player\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"ReferenceEquals\s*\(\s*player\.GetCurrentWeapon\s*\(\s*\)\s*,\s*shovel\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"player\.HaveStamina\s*\(\s*IronShovel\.StaminaNeeded\s*\(\s*mode\s*,\s*Game\.m_staminaRate\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"IronShovel\.DurabilityCost\s*\(\s*mode\s*,\s*Game\.m_durabilityRate\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"!player\.InAttack\(\)\s*&&\s*!player\.InDodge\(\)\s*&&\s*player\.CanMove\(\)\s*&&\s*!player\.IsStaggering\(\)\s*&&\s*!player\.IsKnockedBack\(\)\s*&&\s*!player\.InMinorAction\(\)\s*&&\s*!player\.IsTeleporting\(\)\s*;")]
    [InlineData("Plugin.cs", @"new\s+IronShovelItem\s*\([^;]*craftable\s*:\s*_shovelInput\s*!=\s*null\s*&&\s*_excavation\.Available\s*\)")]
    [InlineData("Items/IronShovelItem.cs", @"Enabled\s*=\s*_craftable\s*,")]
    [InlineData("Excavation/ExcavationShovelInput.cs", @"private\s+static\s+void\s+BeforeAttackInput\s*\(\s*Player\s+__instance\s*\)")]
    [InlineData("Excavation/ExcavationShovelInput.cs", @"IronShovel\.ModeFor\s*\(\s*attack\s*\(\s*__instance\s*\)\s*,\s*secondary\s*\(\s*__instance\s*\)")]
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
        int planner = Regex.Match(runtime, @"ExcavationGate\.BeforeWorldChecks\s*\(").Index;
        int pay = Regex.Match(runtime, @"IronShovel\.CanPay\s*\(").Index;
        int ward = Regex.Match(runtime, @"ExcavationWorldChecks\.WardAllows\s*\(").Index;
        Assert.True(planner > 0 && pay > planner && ward > pay, "CanPay must sit between the planner gate and the ward check.");
    }

    /// <summary>With the switch off nothing is added: the shovel, its input and the console diagnostic are constructed only
    /// inside the switch's branch of the plugin, and nowhere else.</summary>
    [Fact]
    public void TheShovelExistsOnlyBehindTheSwitch()
    {
        foreach (string pattern in new[] { @"new\s+IronShovelItem\s*\(", @"new\s+ExcavationShovelInput\s*\(", @"new\s+ExcavationCommand\s*\(" })
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
