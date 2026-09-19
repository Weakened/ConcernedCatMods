using System.Text;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>One file of C# source, read the way the audits need to read it.
///
/// <b>Why a hand-written scanner and not a regex.</b> The first version of this
/// stripped comments by cutting each line at its first <c>//</c> and then
/// matched literals with a regex. Both are wrong in the same way: neither knows
/// what a string is. One URL in a literal silently deleted every literal after
/// it on that line - from both audits - and a violation placed after such a
/// literal was invisible. An audit that can be switched off by an unrelated
/// string is worse than no audit, because it reports green.
///
/// So this walks the file once, in code / line-comment / block-comment / string
/// / verbatim-string / char states, and reports what it found. Comments are what
/// this package uses to <i>explain</i> the names it must never own, so they are
/// removed from the code text; literals are kept, because nothing here has any
/// business naming one of those in a string either.</summary>
internal sealed class CsharpSource
{
    private CsharpSource(
        string code,
        IReadOnlyList<string> literals,
        IReadOnlyList<int> interpolatedLines,
        IReadOnlyList<int> verbatimLines)
    {
        Code = code;
        Literals = literals;
        InterpolatedLines = interpolatedLines;
        VerbatimLines = verbatimLines;
    }

    /// <summary>The file with comments and the insides of literals blanked out,
    /// newlines preserved, so an identifier can be searched for without a
    /// comment or a string standing in for one.</summary>
    internal string Code { get; }

    /// <summary>Every string literal's contents, with pairs joined by <c>+</c>
    /// already folded together - so <c>"CF" + "_SettlementWorker"</c> is one
    /// literal by the time a predicate sees it.</summary>
    internal IReadOnlyList<string> Literals { get; }

    /// <summary>Lines carrying an interpolated string. A name assembled at run
    /// time is a name no reader of literals can see.</summary>
    internal IReadOnlyList<int> InterpolatedLines { get; }

    /// <summary>Lines carrying a verbatim string, which can hide a name across
    /// a line break.</summary>
    internal IReadOnlyList<int> VerbatimLines { get; }

    internal static CsharpSource Read(string source)
    {
        var code = new StringBuilder(source.Length);
        var literals = new List<(string Text, int End)>();
        var interpolated = new List<int>();
        var verbatim = new List<int>();

        int index = 0;
        int line = 1;
        while (index < source.Length)
        {
            char current = source[index];

            if (current == '\n')
            {
                line++;
                code.Append('\n');
                index++;
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
                {
                    if (source[index] == '\n')
                    {
                        line++;
                        code.Append('\n');
                    }

                    index++;
                }

                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (current == '\'')
            {
                index++;
                while (index < source.Length && source[index] != '\'')
                {
                    index += source[index] == '\\' ? 2 : 1;
                }

                index = Math.Min(index + 1, source.Length);
                code.Append("''");
                continue;
            }

            bool isInterpolated = false;
            bool isVerbatim = false;
            int prefix = index;
            while (prefix < source.Length && (source[prefix] == '$' || source[prefix] == '@'))
            {
                isInterpolated |= source[prefix] == '$';
                isVerbatim |= source[prefix] == '@';
                prefix++;
            }

            if (prefix < source.Length && source[prefix] == '"' && prefix > index)
            {
                if (isInterpolated)
                {
                    interpolated.Add(line);
                }

                if (isVerbatim)
                {
                    verbatim.Add(line);
                }

                index = prefix;
            }

            if (source[index] == '"')
            {
                int startLine = line;
                index++;
                var text = new StringBuilder();
                if (isVerbatim)
                {
                    while (index < source.Length)
                    {
                        if (source[index] == '"')
                        {
                            if (index + 1 < source.Length && source[index + 1] == '"')
                            {
                                text.Append('"');
                                index += 2;
                                continue;
                            }

                            index++;
                            break;
                        }

                        if (source[index] == '\n')
                        {
                            line++;
                            code.Append('\n');
                        }

                        text.Append(source[index]);
                        index++;
                    }
                }
                else
                {
                    while (index < source.Length && source[index] != '"' && source[index] != '\n')
                    {
                        if (source[index] == '\\' && index + 1 < source.Length)
                        {
                            // Keep the escape's payload, so "tcc." is not
                            // a way past the prefix check by accident.
                            text.Append(source[index + 1]);
                            index += 2;
                            continue;
                        }

                        text.Append(source[index]);
                        index++;
                    }

                    if (index < source.Length && source[index] == '"')
                    {
                        index++;
                    }
                }

                _ = startLine;
                code.Append("\"\"");

                // Recorded AFTER the placeholder, so the number is where this
                // literal ends in the code text and the gap to the next one is
                // exactly what sits between them.
                literals.Add((text.ToString(), code.Length));
                continue;
            }

            code.Append(current);
            index++;
        }

        return new CsharpSource(code.ToString(), Fold(literals, code.ToString()), interpolated, verbatim);
    }

    /// <summary>Joins literals that the source joins: <c>"a" + "b"</c> becomes
    /// one literal <c>ab</c>. Splitting a forbidden name across a <c>+</c> is
    /// the first thing anybody tries, deliberately or not, and a scanner that
    /// reads literals one at a time never sees it.</summary>
    private static IReadOnlyList<string> Fold(IReadOnlyList<(string Text, int End)> literals, string code)
    {
        var folded = new List<string>();
        string? pending = null;
        int pendingEnd = -1;

        foreach ((string text, int end) in literals)
        {
            if (pending != null && OnlyJoinBetween(code, pendingEnd, end - 2))
            {
                pending += text;
                pendingEnd = end;
                continue;
            }

            if (pending != null)
            {
                folded.Add(pending);
            }

            pending = text;
            pendingEnd = end;
        }

        if (pending != null)
        {
            folded.Add(pending);
        }

        return folded;
    }

    private static bool OnlyJoinBetween(string code, int from, int to)
    {
        if (from < 0 || to > code.Length || to < from)
        {
            return false;
        }

        bool sawPlus = false;
        for (int index = from; index < to; index++)
        {
            char character = code[index];
            if (character == '+')
            {
                if (sawPlus)
                {
                    return false;
                }

                sawPlus = true;
                continue;
            }

            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return sawPlus;
    }
}

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

    /// <summary>The names every shipped world already has bodies saved under.
    /// Listed here so a failure can say which one was found, and nowhere in the
    /// library itself.</summary>
    internal static IReadOnlyList<string> ShippedPrefabNames { get; } = new[]
    {
        "CF_SettlementWorker", "CT_TeamsterWorker", "CS_Steward",
    };

    /// <summary>The one file exempt from the structural rules - no string
    /// constant, no interpolated string, no verbatim string - and the reason.
    ///
    /// <c>Plugin.cs</c> is the BepInEx entry point, and its three constants are
    /// this package's <i>own</i> identity: the plugin GUID a consumer declares a
    /// dependency on, the display name, and the version. Those are durable facts
    /// this package legitimately owns, they are cross-checked against
    /// <c>thunderstore.toml</c> in three places by <c>validate_repo.py</c>, and
    /// they have nothing to do with a role's prefab or keys. The literal rules
    /// still apply to it in full.
    ///
    /// Adding to this list is how the structural rules get weakened, so the list
    /// is asserted to be exactly this one entry.</summary>
    internal static IReadOnlyList<string> StructurallyExempt { get; } = new[] { "Plugin.cs" };

    internal static IReadOnlyList<string> Files() =>
        Directory.EnumerateFiles(Library, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    internal static string Relative(string path) =>
        path.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Every way one file breaks the rule that this package owns no
    /// name the game or the disk will ever see.
    ///
    /// <b>This is the audit.</b> It is a named method rather than four <c>if</c>
    /// blocks inside a test for one reason: the test that plants a violation
    /// calls it too. When the predicates lived inline, deleting all of them left
    /// the suite green and the plant still passing, because the plant only
    /// exercised the tokenizer. Now removing any one of them fails
    /// <c>Each_rule_is_load_bearing</c>.</summary>
    internal static IEnumerable<string> Violations(string fileName, string source)
    {
        CsharpSource scanned = CsharpSource.Read(source);
        bool structural = !StructurallyExempt.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        foreach (string literal in scanned.Literals)
        {
            string trimmed = literal.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (ShippedPrefabNames.Contains(trimmed, StringComparer.Ordinal))
            {
                yield return $"{fileName}: a shipped prefab name, \"{trimmed}\"";
                continue;
            }

            if (Regex.IsMatch(trimmed, "^[A-Za-z]{2,4}_[A-Za-z0-9_]+$"))
            {
                yield return $"{fileName}: a prefab-shaped name, \"{trimmed}\"";
                continue;
            }

            if (trimmed.IndexOf("tcc.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return $"{fileName}: a mod key prefix, \"{trimmed}\"";
            }
        }

        foreach (Match match in Regex.Matches(scanned.Code, @"\b[A-Za-z]{2,4}_[A-Za-z0-9_]+\b"))
        {
            // nameof(CF_SettlementWorker) is a prefab name in the assembly
            // whatever it is spelled as.
            yield return $"{fileName}: a prefab-shaped identifier, {match.Value}";
        }

        if (!structural)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(
                     scanned.Code, @"\b(?:const\s+string|static\s+readonly\s+string)\b"))
        {
            _ = match;
            yield return
                $"{fileName}: a string constant. This package owns no durable name, so it needs none: a " +
                "prefab name and a key prefix come from NpcBodyContract and a path from INpcDataPaths. " +
                "If this one is genuinely not a name the game or the disk sees, add the file to " +
                "LibrarySources.StructurallyExempt with the reason.";
        }

        foreach (int line in scanned.InterpolatedLines)
        {
            yield return
                $"{fileName}:{line}: an interpolated string. A name assembled at run time is one no audit of " +
                "literals can see; use concatenation of explanatory text, and get names from the role.";
        }

        foreach (int line in scanned.VerbatimLines)
        {
            yield return
                $"{fileName}:{line}: a verbatim string, which can carry a name across a line break where the " +
                "shape checks will not match it.";
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
    [Fact]
    public void Nothing_in_this_library_owns_a_prefab_name_or_a_key_prefix()
    {
        // The failure this prevents is not a bug report. The host destroys any
        // saved object whose prefab is not registered when a world's objects are
        // created - it logs "Destroyed invalid prefab ZDO" and moves on. A
        // prefab name owned here, or a key prefix owned here, silently deletes
        // or empties every existing worker body on the first load after the
        // change, with the player's tools and gathered material inside it,
        // before any migration code could run.
        var violations = new List<string>();
        foreach (string path in LibrarySources.Files())
        {
            violations.AddRange(
                LibrarySources.Violations(Path.GetFileName(path), File.ReadAllText(path))
                    .Select(violation => LibrarySources.Relative(path) + " -> " + violation));
        }

        Assert.True(
            violations.Count == 0,
            "ConcernedNPC must own no prefab name and no ZDO key prefix. Both are DATA a role supplies " +
            "through NpcBodyContract, because a shipped world's saved bodies are found by them and a name " +
            "chosen here deletes or empties every one of them on the next load. Found:\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void Each_rule_is_load_bearing()
    {
        // An audit nobody has watched fail is an audit that might be scanning
        // nothing. Every predicate gets a candidate that only it catches, so
        // deleting any one of them fails here - which the previous version of
        // this test did not do: it only proved the tokenizer returned strings,
        // so all three checks could be removed and the suite stayed green.
        AssertCaught("a shipped prefab name", "class X { string a = \"CF_SettlementWorker\"; }");
        AssertCaught("a prefab-shaped name", "class X { string a = \"NPC_WorkerBody\"; }");
        AssertCaught("a mod key prefix", "class X { string a = \"tcc.npc.\"; }");
        AssertCaught("a prefab-shaped identifier", "class X { string a = nameof(CF_SettlementWorker); }");
        AssertCaught("a string constant", "class X { const string Prefab = \"whatever\"; }");
        AssertCaught("an interpolated string", "class X { string a = $\"{Root}worker.\"; }");
        AssertCaught("a verbatim string", "class X { string a = @\"CF_\nSettlementWorker\"; }");
    }

    [Fact]
    public void Splitting_a_name_across_a_plus_does_not_hide_it()
    {
        // The first evasion anybody reaches for, deliberately or by wrapping a
        // long line.
        AssertCaught("a shipped prefab name", "class X { string a = \"CF\" + \"_SettlementWorker\"; }");
        AssertCaught("a mod key prefix", "class X { string a = \"tcc\" + \".worker.\"; }");
        AssertCaught(
            "a shipped prefab name",
            "class X { string a = \"CF\"\n    + \"_Settlement\"\n    + \"Worker\"; }");
    }

    [Fact]
    public void A_double_slash_inside_a_string_does_not_blind_the_scanner()
    {
        // The nastiest case because it is accidental rather than adversarial: a
        // URL in a literal used to truncate the line, deleting every literal
        // after it from both audits.
        AssertCaught(
            "a shipped prefab name",
            "class X { string a = \"https://example.invalid\"; string b = \"CF_SettlementWorker\"; }");

        // And the same for the product-name audit, which shares the scanner.
        string code = CsharpSource.Read(
            "class X { string a = \"https://example.invalid\"; }\nclass ConcernedTeamsterThing { }").Code;
        Assert.Contains("ConcernedTeamster", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_may_still_explain_the_names_the_library_must_not_own()
    {
        // The whole reason comments are stripped rather than scanned. These
        // files exist largely to say which names they must never write.
        const string explaining =
            "// today CF_SettlementWorker, and the prefix tcc.worker.\n" +
            "/* CS_Steward too, and ConcernedSteward */\n" +
            "class X { }";

        Assert.Empty(LibrarySources.Violations("X.cs", explaining));
    }

    [Fact]
    public void Nothing_in_this_library_names_a_product()
    {
        // The validator enforces this for csproj references and for using
        // directives (check_library_consumers). This covers the rest of the
        // source: a type name, an attribute, a string.
        var violations = new List<string>();
        foreach (string path in LibrarySources.Files())
        {
            string code = CsharpSource.Read(File.ReadAllText(path)).Code;
            foreach (string product in Products)
            {
                if (code.Contains(product, StringComparison.Ordinal))
                {
                    violations.Add($"{LibrarySources.Relative(path)}: {product}");
                }
            }
        }

        Assert.True(violations.Count == 0, "A library may not name a product:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void No_internals_are_made_visible_to_anybody()
    {
        // The single-arbiter guarantee and the unforgeable lease both rest on
        // this package having no friend assemblies: an InternalsVisibleTo would
        // let a consumer construct its own NpcRoleRegistry, its own
        // NpcBodyArbiter and its own BodyLease, and undo all three at once. A
        // role leaf that needs to reach something should make it public
        // deliberately, with the version bump that implies.
        foreach (string path in LibrarySources.Files())
        {
            string code = CsharpSource.Read(File.ReadAllText(path)).Code;
            Assert.False(
                code.Contains("InternalsVisibleTo", StringComparison.Ordinal),
                $"{LibrarySources.Relative(path)} makes this package's internals visible to another assembly. " +
                "That undoes the single-arbiter guarantee and lets a BodyLease be forged.");
        }
    }

    [Fact]
    public void The_structural_exemption_list_is_exactly_the_plugin_entry_point()
    {
        // Weakening the structural rules means adding a file here, so the list
        // itself is pinned. Plugin.cs is exempt because its constants are this
        // package's own BepInEx identity, validated against thunderstore.toml.
        Assert.Equal(new[] { "Plugin.cs" }, LibrarySources.StructurallyExempt);
    }

    [Fact]
    public void The_library_has_sources_to_audit()
    {
        // Every check above passes trivially over an empty list.
        Assert.True(LibrarySources.Files().Count >= 10);
    }

    private static readonly string[] Products =
    {
        "ConcernedCartographer", "ConcernedTeamster", "ConcernedForeman", "ConcernedSteward",
    };

    private static void AssertCaught(string expected, string source)
    {
        IReadOnlyList<string> violations = LibrarySources.Violations("Planted.cs", source).ToList();
        Assert.True(
            violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The audit did not catch '{expected}' in:\n{source}\nIt reported: " +
            (violations.Count == 0 ? "nothing at all" : string.Join("; ", violations)));
    }
}
