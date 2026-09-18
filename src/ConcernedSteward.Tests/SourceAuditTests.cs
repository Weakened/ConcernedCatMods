using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Reading the shipped sources and refusing to ship some of them.
///
/// These are the checks that cannot be expressed as behaviour, because they are
/// about what the code <i>could</i> do rather than what it does. A test that
/// drives the loop can prove the Steward did not teleport this time; only a
/// scan of the source can prove nothing in the product is able to.</summary>
internal static class ProductSources
{
    /// <summary>The repository root, found by walking up to the solution. The
    /// test runs from an output folder several levels down, and hard-coding a
    /// relative depth breaks the first time the layout moves.</summary>
    internal static string Root { get; } = FindRoot();

    internal static string Product => Path.Combine(Root, "src", "ConcernedSteward");

    internal static string Tests => Path.Combine(Root, "src", "ConcernedSteward.Tests");

    internal static IReadOnlyList<string> Files(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    internal static string Relative(string path) =>
        path.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>The file with its comments blanked out, so an identifier a
    /// comment names in order to explain why it is forbidden is not itself a
    /// violation.
    ///
    /// <b>This is a deliberate softening of an absolute rule, and it is the
    /// right one here.</b> Concerned Teamster's equivalent scan includes
    /// comments so that it can stay simple and unarguable; this product's
    /// adapters exist largely to explain which vanilla members must never be
    /// called, and that explanation is worth more than the simplicity. String
    /// literals are NOT stripped: nothing here has any business naming those
    /// members in one either.</summary>
    internal static string WithoutComments(string source)
    {
        string withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var kept = new List<string>();
        foreach (string line in withoutBlocks.Split('\n'))
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            kept.Add(comment < 0 ? line : line.Substring(0, comment));
        }

        return string.Join("\n", kept);
    }

    private static string FindRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !File.Exists(Path.Combine(here.FullName, "ConcernedCatMods.sln")))
        {
            here = here.Parent;
        }

        if (here == null)
        {
            throw new InvalidOperationException(
                "The repository root could not be found from " + AppContext.BaseDirectory +
                "; these audits read the shipped sources and cannot run without it.");
        }

        return here.FullName;
    }
}

/// <summary>What this product must not be able to do to a player's world.
/// </summary>
public sealed class ForbiddenWriteTests
{
    /// <summary>Each entry is a vanilla member, and the sentence is why the
    /// Steward may never reach it. The sentence is in the failure message, so
    /// whoever trips one of these finds out why rather than just that.</summary>
    private static readonly (string token, string why)[] Forbidden =
    {
        ("AddFuel(",
            "Fireplace.AddFuel writes the fuel level with NO item consumed. It is resource " +
            "conjuring. The only legitimate path is UseItem, which takes a real item out of a " +
            "real inventory."),
        ("SetFuel(",
            "Fireplace.SetFuel writes the fuel level with no item consumed, same as AddFuel."),
        ("RPC_AddFuelAmount",
            "the RPC behind AddFuel. Invoking it directly is the same conjuring by another name."),
        ("RPC_SetFuelAmount",
            "the RPC behind SetFuel."),
        ("ClaimOwnership",
            "taking ownership of an object this session was not granted. The Steward acts only " +
            "on objects it already owns, and declines the rest."),
        ("SetOwner(",
            "the same seizure, one level down."),
        ("AddForce",
            "writing physics to a body. The Steward moves on the game's own motor and nothing " +
            "else."),
        ("AddTorque",
            "writing physics to a body."),
        (".velocity",
            "writing a velocity. Movement is MoveTo and StopMoving."),
        (".position =",
            "writing a transform. A worker that can be placed is a worker that can be placed " +
            "inside a wall, through a ward, or across the map."),
        (".rotation =",
            "writing a transform."),
        ("TeleportTo",
            "teleporting. #340 is explicit: normal motor and pathing only."),
        ("Fireplace.Interact",
            "Interact claims ownership when the object has none and toggles the fire off when " +
            "it can be turned off. UseItem does neither."),
    };

    [Fact]
    public void The_audits_are_actually_reading_the_product()
    {
        // Every audit in this file passes vacuously if the file walk finds
        // nothing, and a walk that finds nothing is exactly what a moved folder
        // or a renamed project produces. This is the guard on all of them.
        IReadOnlyList<string> product = ProductSources.Files(ProductSources.Product);

        Assert.True(product.Count >= 15, "only " + product.Count + " product sources were found");
        Assert.Contains(product, path => path.EndsWith("WorldFuelTargets.cs", StringComparison.Ordinal));
        Assert.Contains(product, path => path.EndsWith("UpkeepLoop.cs", StringComparison.Ordinal));
        Assert.Contains(product, path => path.EndsWith("Plugin.cs", StringComparison.Ordinal));

        // And that stripping comments does not strip code with them.
        string stripped = ProductSources.WithoutComments(
            string.Join("\n", "int a = 1; // AddFuel(", "/* AddFuel( */", "int b = 2;"));
        Assert.DoesNotContain("AddFuel(", stripped);
        Assert.Contains("int a = 1;", stripped);
        Assert.Contains("int b = 2;", stripped);
    }

    [Fact]
    public void Nothing_in_the_product_can_conjure_fuel_seize_ownership_or_write_a_transform()
    {
        var violations = new List<string>();

        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string[] lines = ProductSources.WithoutComments(
                File.ReadAllText(path)).Split('\n');
            for (int number = 0; number < lines.Length; number++)
            {
                foreach ((string token, string why) in Forbidden)
                {
                    if (lines[number].Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add(
                            ProductSources.Relative(path) + ":" + (number + 1) + " uses '" +
                            token + "' — " + why);
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_only_vanilla_call_that_changes_a_fire_is_UseItem()
    {
        string adapter = File.ReadAllText(
            Path.Combine(ProductSources.Product, "Runtime", "WorldFuelTargets.cs"));
        string code = ProductSources.WithoutComments(adapter);

        Assert.Contains("fireplace.UseItem(body, item);", code);

        // And exactly one call site, so there is one place to read to know what
        // the Steward can do to a fire.
        Assert.Single(Regex.Matches(code, @"\.UseItem\("));
    }

    [Fact]
    public void The_only_network_object_the_product_writes_to_is_the_Stewards_own()
    {
        var writes = new List<string>();
        var pattern = new Regex(@"\bzdo\.Set\(|\.GetZDO\(\)\.Set\(", RegexOptions.IgnoreCase);

        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));
            if (pattern.IsMatch(code))
            {
                writes.Add(ProductSources.Relative(path));
            }
        }

        // CLAUDE.md: mod data is never written into a vanilla object. The body
        // writes its identity and its pack into its OWN prefab's object, and
        // nothing else in the product writes to any object at all.
        string only = Assert.Single(writes);
        Assert.EndsWith("StewardBody.cs", only);

        string body = ProductSources.WithoutComments(
            File.ReadAllText(Path.Combine(ProductSources.Product, "Runtime", "StewardBody.cs")));
        foreach (Match match in Regex.Matches(body, @"zdo\.Set\((\w+),"))
        {
            Assert.Contains(
                match.Groups[1].Value,
                new[] { "KeyField", "InventoryField", "RevisionField" });
        }
    }

    [Fact]
    public void The_product_makes_no_network_calls_of_its_own()
    {
        var violations = new List<string>();
        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));

            // No custom RPC is registered or invoked. The one RPC that runs on
            // the Steward's behalf is vanilla's own, reached through
            // Fireplace.UseItem, which registers and invokes it itself.
            foreach (string token in new[]
            {
                "InvokeRPC(", "ZRoutedRpc", "HttpClient", "WebRequest", "UnityWebRequest", "Socket(",
            })
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add(ProductSources.Relative(path) + " uses '" + token + "'");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_product_patches_nothing()
    {
        var violations = new List<string>();
        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));
            foreach (string token in new[] { "HarmonyPatch", "HarmonyPrefix", "HarmonyPostfix", "new Harmony(" })
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add(ProductSources.Relative(path) + " uses '" + token + "'");
                }
            }
        }

        // With the runtime off this plugin is inert, and "inert" is only true
        // if nothing was patched at load.
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void No_fireplace_prefab_is_named_anywhere()
    {
        // Prefab names are asset-bundle data, not API. Naming one would be a
        // guess that compiles.
        var violations = new List<string>();
        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));
            foreach (string name in new[] { "\"fire_pit\"", "\"hearth\"", "\"bonfire\"", "\"Campfire\"" })
            {
                if (code.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(ProductSources.Relative(path) + " names " + name);
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_fuel_path_never_counts_with_a_predicate_vanilla_does_not_mutate_with()
    {
        // Inventory.CountItems and HaveItem also filter on m_worldLevel;
        // Fireplace.UseItem does not. Measuring with one and mutating with the
        // other is how a conservation argument develops a hole.
        var violations = new List<string>();
        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));
            foreach (string token in new[] { ".CountItems(", ".HaveItem(", ".FindFreeStackSpace(" })
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add(ProductSources.Relative(path) + " uses '" + token + "'");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }
}

/// <summary>That this product is its own product, and that the shared subset it
/// takes is one it may take.</summary>
public sealed class IndependenceTests
{
    private static readonly string[] Siblings =
    {
        "ConcernedCartographer", "ConcernedTeamster", "ConcernedForeman",
    };

    [Fact]
    public void Nothing_in_the_Steward_references_another_product_at_compile_time()
    {
        var violations = new List<string>();

        foreach (string folder in new[] { ProductSources.Product, ProductSources.Tests })
        {
            foreach (string path in ProductSources.Files(folder))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    foreach (string sibling in Siblings)
                    {
                        if (Regex.IsMatch(
                            line,
                            @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?TheConcernedCat\."
                            + sibling + @"\b"))
                        {
                            violations.Add(ProductSources.Relative(path) + " uses " + sibling);
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void Neither_project_file_references_another_product()
    {
        foreach (string name in new[]
        {
            Path.Combine(ProductSources.Product, "ConcernedSteward.csproj"),
            Path.Combine(ProductSources.Tests, "ConcernedSteward.Tests.csproj"),
        })
        {
            string project = File.ReadAllText(name);
            foreach (string sibling in Siblings)
            {
                // The word may appear in a comment explaining why the rule
                // exists; it may not appear in an item that would compile or
                // reference one.
                foreach (Match match in Regex.Matches(
                    project, @"<(?:Compile|ProjectReference|Reference|PackageReference)[^>]*>"))
                {
                    Assert.DoesNotContain(sibling, match.Value);
                }
            }
        }
    }

    [Fact]
    public void The_hauler_is_found_by_a_string_and_never_by_a_reference()
    {
        string runtime = File.ReadAllText(
            Path.Combine(ProductSources.Product, "Runtime", "StewardRuntime.cs"));

        // The whole cross-product surface: one GUID and one property name, both
        // string literals, with only BCL types crossing.
        Assert.Contains("HaulContract.ProviderGuid", runtime);
        Assert.Contains("CapabilityMap.PropertyName", runtime);
        Assert.DoesNotContain("TheConcernedCat.ConcernedTeamster", runtime);
    }
}

/// <summary>The partial adoption of the settlement area, held to the condition
/// it was approved on.
///
/// <b>Why this test is part of the slice and not a follow-up.</b> Concerned
/// Steward takes four named subfolders of <c>src/Shared/Settlement</c> rather
/// than the whole area, because the rest is Concerned Foreman's collection-order
/// and haul-cooperation machinery and would be roughly sixteen thousand unused
/// lines in this DLL. That is only safe while the four are a CLOSED set. Without
/// this test the arrangement is a trap: somebody edits Foreman, adds a perfectly
/// reasonable <c>using</c> inside <c>Designations</c>, and the Steward stops
/// compiling for a reason that has nothing to do with the Steward.</summary>
public sealed class SharedSubsetClosureTests
{
    private static readonly string[] Adopted = { "Identity", "Worker", "Designations", "Storage" };

    private static string SettlementRoot =>
        Path.Combine(ProductSources.Root, "src", "Shared", "Settlement");

    [Fact]
    public void The_adopted_subset_never_reaches_outside_itself()
    {
        var allowed = new HashSet<string>(
            Adopted.Select(folder => "TheConcernedCat.Settlement." + folder), StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (string folder in Adopted)
        {
            foreach (string path in ProductSources.Files(Path.Combine(SettlementRoot, folder)))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    Match match = Regex.Match(line, @"^\s*using\s+(TheConcernedCat\.[\w.]+)\s*;");
                    if (!match.Success)
                    {
                        continue;
                    }

                    string used = match.Groups[1].Value;
                    if (!allowed.Contains(used) && used != "TheConcernedCat.Workers")
                    {
                        violations.Add(
                            ProductSources.Relative(path) + " reaches outside the subset: " + used +
                            ". Concerned Steward adopts only " + string.Join(", ", Adopted) +
                            " from src/Shared/Settlement, so this breaks its build. Either keep " +
                            "the subset closed, or tell the Steward to adopt the whole area.");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_project_file_adopts_exactly_the_subset_this_test_checks()
    {
        // The two lists have to agree, or the test is checking a set nobody
        // compiles and the build is compiling a set nobody checks.
        string project = File.ReadAllText(
            Path.Combine(ProductSources.Product, "ConcernedSteward.csproj"));

        var adopted = Regex.Matches(project, @"Shared\\Settlement\\(\w+)\\")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Adopted.OrderBy(name => name, StringComparer.Ordinal).ToArray(), adopted);
    }

    [Fact]
    public void The_areas_the_Steward_takes_whole_are_the_ones_it_expects()
    {
        string project = File.ReadAllText(
            Path.Combine(ProductSources.Product, "ConcernedSteward.csproj"));

        Assert.Contains(@"..\Shared\Workers\**\*.cs", project);
        Assert.Contains(@"..\Shared\Interop\**\*.cs", project);

        // Not the companion layer and not the ladder layer: a shared area is
        // not a framework and is not mandatory.
        Assert.DoesNotContain(@"..\Shared\Companions", project);
        Assert.DoesNotContain(@"..\Shared\Ladders", project);
    }

    [Fact]
    public void Nothing_shared_carries_a_product_namespace_or_a_game_type()
    {
        var violations = new List<string>();
        var folders = Adopted
            .Select(folder => Path.Combine(SettlementRoot, folder))
            .Concat(new[]
            {
                Path.Combine(ProductSources.Root, "src", "Shared", "Workers"),
                Path.Combine(ProductSources.Root, "src", "Shared", "Interop"),
            });

        foreach (string folder in folders)
        {
            foreach (string path in ProductSources.Files(folder))
            {
                string code = ProductSources.WithoutComments(File.ReadAllText(path));
                foreach (string token in new[]
                {
                    "UnityEngine", "BepInEx", "Jotunn", "TheConcernedCat.ConcernedSteward",
                })
                {
                    if (code.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add(ProductSources.Relative(path) + " names " + token);
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }
}
