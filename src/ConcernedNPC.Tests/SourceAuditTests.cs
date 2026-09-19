using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Reading this library's own sources and refusing to ship some of
/// them.
///
/// These are checks that cannot be expressed as behaviour, because they are
/// about what the code <i>could</i> do rather than what it does. A test that
/// drives the registry can prove no prefab name was invented this time; only a
/// scan of the source can prove nothing in the package is able to.</summary>
internal static class LibrarySources
{
    /// <summary>The repository root, found by walking up to the solution. These
    /// tests run from an output folder several levels down, and hard-coding a
    /// relative depth breaks the first time the layout moves.</summary>
    internal static string Root { get; } = FindRoot();

    internal static string Library => Path.Combine(Root, "src", "ConcernedNPC");

    internal static IReadOnlyList<string> Files() =>
        Directory.EnumerateFiles(Library, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    internal static string Relative(string path) =>
        path.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>The file with its comments blanked out.
    ///
    /// Comments are where this package <i>explains</i> the names it must never
    /// own - "today <c>CF_SettlementWorker</c>", "the prefix <c>tcc.worker.</c>"
    /// - and that explanation is the most valuable thing in the file. String
    /// literals are NOT stripped: nothing here has any business naming those in
    /// one.</summary>
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

    /// <summary>Every ordinary string literal in the source, with comments
    /// already removed. Verbatim and interpolated literals are matched too;
    /// nothing in this package uses either, and a scan that silently ignored
    /// them would be a scan somebody could step around.</summary>
    internal static IEnumerable<string> StringLiterals(string source)
    {
        foreach (Match match in Regex.Matches(WithoutComments(source), "@?\\$?\"((?:[^\"\\\\\\n]|\\\\.)*)\""))
        {
            yield return match.Groups[1].Value;
        }
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

/// <summary>What this library must never own.</summary>
public class ContractLiteralTests
{
    /// <summary>The names every shipped world already has bodies saved under.
    /// Listed here so the failure message can say which one was found, and
    /// nowhere in the library itself.</summary>
    private static readonly string[] ShippedPrefabNames =
    {
        "CF_SettlementWorker", "CT_TeamsterWorker", "CS_Steward",
    };

    [Fact]
    public void Nothing_in_this_library_writes_a_prefab_name_or_a_key_prefix()
    {
        // The failure this prevents is not a bug report. The host destroys any
        // saved object whose prefab is not registered when a world's objects are
        // created - it logs "Destroyed invalid prefab ZDO" and moves on. A
        // prefab name owned here, or a key prefix owned here, silently deletes
        // or empties every existing Thorstein, Gunnar and Steward on the first
        // load after the change, with the player's tools and gathered material
        // inside them, before any migration code could run. Neither is
        // recoverable, so neither is left to a reviewer noticing.
        var violations = new List<string>();

        foreach (string path in LibrarySources.Files())
        {
            string source = File.ReadAllText(path);
            foreach (string literal in LibrarySources.StringLiterals(source))
            {
                string trimmed = literal.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (ShippedPrefabNames.Contains(trimmed, StringComparer.Ordinal))
                {
                    violations.Add($"{LibrarySources.Relative(path)}: a shipped prefab name, \"{trimmed}\"");
                    continue;
                }

                if (Regex.IsMatch(trimmed, "^[A-Z]{2}_[A-Za-z0-9_]+$"))
                {
                    violations.Add($"{LibrarySources.Relative(path)}: a prefab-shaped name, \"{trimmed}\"");
                    continue;
                }

                if (trimmed.IndexOf("tcc.", StringComparison.Ordinal) >= 0)
                {
                    violations.Add($"{LibrarySources.Relative(path)}: a mod key prefix, \"{trimmed}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ConcernedNPC must own no prefab name and no ZDO key prefix. Both are DATA a role supplies " +
            "through NpcBodyContract, because a shipped world's saved bodies are found by them and a name " +
            "chosen here deletes or empties every one of them on the next load. Found:\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void Nothing_in_this_library_names_a_product()
    {
        // The validator enforces this for csproj references and for using
        // directives (check_library_consumers). This covers the rest of the
        // source: a type, a comment's worth of coupling, a string.
        string[] products =
        {
            "ConcernedCartographer", "ConcernedTeamster", "ConcernedForeman", "ConcernedSteward",
        };
        var violations = new List<string>();

        foreach (string path in LibrarySources.Files())
        {
            string source = LibrarySources.WithoutComments(File.ReadAllText(path));
            foreach (string product in products)
            {
                if (source.Contains(product, StringComparison.Ordinal))
                {
                    violations.Add($"{LibrarySources.Relative(path)}: {product}");
                }
            }
        }

        Assert.True(violations.Count == 0, "A library may not name a product:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void The_audit_can_actually_see_a_violation()
    {
        // An audit nobody has watched fail is an audit that might be scanning
        // nothing. This proves the literal scanner reads what it claims to.
        const string planted =
            "internal static class Bad { internal const string Prefab = \"CF_SettlementWorker\";\n" +
            "// a comment naming CF_SettlementWorker and tcc.worker. is fine\n" +
            "internal const string Prefix = \"tcc.worker.\"; }";

        var literals = LibrarySources.StringLiterals(planted).ToList();

        Assert.Contains("CF_SettlementWorker", literals);
        Assert.Contains("tcc.worker.", literals);
        Assert.Equal(2, literals.Count);
    }

    [Fact]
    public void The_library_has_sources_to_audit()
    {
        // Every check above passes trivially over an empty list.
        Assert.True(LibrarySources.Files().Count >= 10);
    }
}
