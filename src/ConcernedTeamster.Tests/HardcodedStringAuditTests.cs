using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace ConcernedTeamster.Tests;

/// <summary>CT-032 hardcoded-string audit: keeps user-facing English out of
/// the Teamster presentation layer for good. Source-scans the shipped
/// presentation files and fails on any letter-bearing string literal that is
/// not a catalog key, a multi-segment code identifier, a numeric format
/// specifier, or inside a log call (log diagnostics stay English by design —
/// they are support material, not UI). Companion checks prove every catalog
/// key is live in shipped code, no value would be corrupted by the
/// translator-file round trip, and no externalized sentence has been
/// re-hardcoded anywhere in the shipped source.</summary>
public class HardcodedStringAuditTests
{
    // ---- audited presentation sources (directories recurse) ----------------

    private static readonly string[] AuditedDirectories =
    {
        Path.Combine("src", "ConcernedTeamster", "Domain", "Ui"),
        Path.Combine("src", "ConcernedTeamster", "Domain", "Warnings"),
        Path.Combine("src", "ConcernedTeamster", "Domain", "Diagnostics"),
        Path.Combine("src", "ConcernedTeamster", "Ui"),
    };

    private static readonly string[] AuditedFiles =
    {
        Path.Combine("src", "ConcernedTeamster", "Domain", "Load", "LoadModel.cs"),
        Path.Combine("src", "ConcernedTeamster", "Domain", "Load", "LoadText.cs"),
    };

    /// <summary>Shipped (non-test) Teamster source scanned by the liveness
    /// and re-hardcode checks.</summary>
    private static string ShippedRoot => Path.Combine(RepoRoot, "src", "ConcernedTeamster");

    private static string CatalogFile =>
        Path.Combine(ShippedRoot, "Domain", "Localization", "TeamsterStrings.cs");

    // ---- tests -------------------------------------------------------------

    [Fact]
    public void PresentationSources_ContainNoHardcodedUiStrings()
    {
        var violations = new List<string>();
        foreach (string file in AuditedSourceFiles())
        {
            string text = File.ReadAllText(file);
            IReadOnlyList<SourceLiteral> literals = ExtractLiterals(text);
            IReadOnlyList<(int Start, int End)> logSpans = FindLogCallSpans(text);

            foreach (SourceLiteral literal in literals)
            {
                if (IsExempt(literal.Text))
                {
                    continue;
                }

                if (logSpans.Any(span => literal.Start >= span.Start && literal.Start <= span.End))
                {
                    continue; // log diagnostics stay English by design
                }

                violations.Add(
                    Relative(file) + ":" + literal.Line + ": \"" + literal.Text + "\"");
            }
        }

        Assert.True(violations.Count == 0,
            "Hardcoded user-facing string(s) found — resolve them through TeamsterStrings:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void EveryCatalogKey_IsReferencedByShippedCode()
    {
        HashSet<string> referenced = ShippedLiterals(excludeCatalogFile: true);
        List<string> dead = TeamsterStrings.Defaults.Keys
            .Where(key => !referenced.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(dead.Count == 0,
            "Catalog key(s) not referenced by any shipped source (dead keys):\n" +
            string.Join("\n", dead));
    }

    [Fact]
    public void NoCatalogValue_HasLeadingOrTrailingWhitespace()
    {
        // ParseOverrides trims translator-file line ends, so edge whitespace
        // in a value could never round-trip — it must not exist.
        List<string> offenders = TeamsterStrings.Defaults
            .Where(entry => entry.Value != entry.Value.Trim())
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Catalog value(s) with leading/trailing whitespace:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ExternalizedSentences_AreNotReHardcodedAnywhereInShippedSource()
    {
        // Any catalog value long enough to be a sentence (not a generic word
        // like "unknown") must appear in shipped source only inside the
        // catalog itself. Catching a copy here means someone bypassed the
        // catalog with text players see.
        HashSet<string> shipped = ShippedLiterals(excludeCatalogFile: true);
        List<string> copies = TeamsterStrings.Defaults
            .Where(entry => entry.Value.Length >= 15 && shipped.Contains(entry.Value))
            .Select(entry => entry.Key + " => \"" + entry.Value + "\"")
            .ToList();

        Assert.True(copies.Count == 0,
            "Catalog value(s) re-hardcoded outside the catalog:\n" + string.Join("\n", copies));
    }

    // ---- exemption rules ---------------------------------------------------

    private static bool IsExempt(string literal)
    {
        if (!Regex.IsMatch(literal, "[A-Za-z]"))
        {
            return true; // symbols, separators, digit patterns
        }

        if (TeamsterStrings.Defaults.ContainsKey(literal))
        {
            return true; // the literal IS a catalog key
        }

        // Multi-segment lowercase identifiers ("cart-status", "status.trips")
        // are code contracts, not display text. Single bare words stay
        // violations so a word like "climbing" can never sneak back in.
        if (Regex.IsMatch(literal, "^[a-z][a-z0-9]*([.\\-][a-z0-9]+)+$"))
        {
            return true;
        }

        // Numeric format specifiers ("F0", "D2").
        if (Regex.IsMatch(literal, "^[A-Za-z][0-9]$"))
        {
            return true;
        }

        return false;
    }

    // ---- source scanning ---------------------------------------------------

    private readonly struct SourceLiteral
    {
        public SourceLiteral(string text, int start, int line)
        {
            Text = text;
            Start = start;
            Line = line;
        }

        public string Text { get; }

        public int Start { get; }

        public int Line { get; }
    }

    /// <summary>Extracts every string literal (regular, interpolated, and
    /// verbatim) with comments removed — a pragmatic single-pass scanner, not
    /// a full lexer; the audited sources use no constructs it misreads.</summary>
    private static IReadOnlyList<SourceLiteral> ExtractLiterals(string text)
    {
        var literals = new List<SourceLiteral>();
        int line = 1;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current == '\n')
            {
                line++;
                continue;
            }

            if (current == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    while (index < text.Length && text[index] != '\n')
                    {
                        index++;
                    }

                    line++;
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    {
                        if (text[index] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    index++;
                    continue;
                }
            }

            if (current == '\'')
            {
                index++;
                if (index < text.Length && text[index] == '\\')
                {
                    index++;
                }

                while (index < text.Length && text[index] != '\'')
                {
                    index++;
                }

                continue;
            }

            bool verbatim = false;
            int quoteIndex = index;
            if (current == '@' && index + 1 < text.Length && text[index + 1] == '"')
            {
                verbatim = true;
                quoteIndex = index + 1;
            }
            else if (current == '$' && index + 1 < text.Length && text[index + 1] == '"')
            {
                quoteIndex = index + 1;
            }
            else if (current == '$' && index + 2 < text.Length &&
                text[index + 1] == '@' && text[index + 2] == '"')
            {
                verbatim = true;
                quoteIndex = index + 2;
            }
            else if (current != '"')
            {
                continue;
            }

            int start = quoteIndex + 1;
            int cursor = start;
            while (cursor < text.Length)
            {
                if (!verbatim && text[cursor] == '\\')
                {
                    cursor += 2;
                    continue;
                }

                if (text[cursor] == '"')
                {
                    if (verbatim && cursor + 1 < text.Length && text[cursor + 1] == '"')
                    {
                        cursor += 2;
                        continue;
                    }

                    break;
                }

                if (text[cursor] == '\n')
                {
                    line++;
                }

                cursor++;
            }

            literals.Add(new SourceLiteral(text.Substring(start, cursor - start), quoteIndex, line));
            index = cursor;
        }

        return literals;
    }

    /// <summary>Character spans of Log*(...) call argument lists, so log
    /// diagnostics are structurally exempt. Paren matching skips string
    /// content via the literal list.</summary>
    private static IReadOnlyList<(int Start, int End)> FindLogCallSpans(string text)
    {
        var spans = new List<(int, int)>();
        IReadOnlyList<SourceLiteral> literals = ExtractLiterals(text);

        foreach (Match match in Regex.Matches(text, "\\bLog(Error|Warning|Info|Debug)\\s*\\("))
        {
            int depth = 0;
            for (int index = match.Index; index < text.Length; index++)
            {
                SourceLiteral covering = literals.FirstOrDefault(
                    l => index >= l.Start && index <= l.Start + l.Text.Length + 1);
                if (covering.Text is not null && index > covering.Start)
                {
                    index = covering.Start + covering.Text.Length + 1;
                }

                if (index >= text.Length)
                {
                    break;
                }

                if (text[index] == '(')
                {
                    depth++;
                }
                else if (text[index] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        spans.Add((match.Index, index));
                        break;
                    }
                }
            }
        }

        return spans;
    }

    // ---- file plumbing -----------------------------------------------------

    private static readonly Lazy<string> _repoRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above test output.");
    });

    private static string RepoRoot => _repoRoot.Value;

    private static IEnumerable<string> AuditedSourceFiles()
    {
        foreach (string directory in AuditedDirectories)
        {
            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(RepoRoot, directory), "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        foreach (string file in AuditedFiles)
        {
            yield return Path.Combine(RepoRoot, file);
        }
    }

    private static HashSet<string> ShippedLiterals(bool excludeCatalogFile)
    {
        var literals = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(ShippedRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            if (excludeCatalogFile &&
                string.Equals(file, CatalogFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (SourceLiteral literal in ExtractLiterals(File.ReadAllText(file)))
            {
                literals.Add(literal.Text);
            }
        }

        return literals;
    }

    private static string Relative(string file)
    {
        return file.StartsWith(RepoRoot, StringComparison.OrdinalIgnoreCase)
            ? file.Substring(RepoRoot.Length + 1)
            : file;
    }
}
