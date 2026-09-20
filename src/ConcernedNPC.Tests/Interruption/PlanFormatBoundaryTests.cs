using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Why durable plan state needs no migration, proved rather than
/// asserted.
///
/// <b>The zero-migration promise, applied to the one area of this library that
/// now causes a file to be written.</b> There are two ways to keep it. One is to
/// move a format and pin its bytes. The other is to own no format at all, and that
/// is what this area does: the plan journal is handed a path and a codec and
/// reasons about neither. The role's row tags, field order, schema number and file
/// name are exactly where they were.
///
/// <b>So this is the fixture.</b> A durable decision cannot drift into this area,
/// because there is nothing here that makes one; if that ever stops being true,
/// this fails, and whoever made it untrue has to say which durable file this
/// library now owns and what happens to the ones that already exist.
///
/// <b>Why it is not folded into the camp and custody version of this test.</b>
/// That one forbids the substring <c>File.</c> outright, which is right for an area
/// that touches no file at all. This area legitimately names the one pinned
/// plumbing type - <c>NpcSidecarFile</c> - so the check here is the shape the
/// repository validator uses: the member access, at a word boundary, rather than
/// the substring. Nothing else is relaxed.</summary>
public class PlanFormatBoundaryTests
{
    /// <summary>A file primitive reached directly. Word-bounded, so naming the
    /// pinned sidecar type is allowed and calling <c>File.WriteAllLines</c> is
    /// not.</summary>
    private static readonly Regex FilePrimitive =
        new Regex(@"\b(?:File|Directory)\.\w+|\bnew\s+Stream(?:Writer|Reader)\b|\bnew\s+File(?:Stream|Info)\b");

    /// <summary>Choosing where data lives, or naming what shape it is in.
    /// Refused outright: either one makes this library a second author of
    /// somebody else's save file.</summary>
    private static readonly Regex FormatOwnership =
        new Regex(@"\bPath\.\w+|\bSchemaVersion\b|\bSerializable\b|\bSerialize\b|\bBinaryFormatter\b");

    [Fact]
    public void Nothing_in_the_interruption_area_opens_a_file_or_chooses_where_one_lives()
    {
        var found = new List<string>();
        foreach (string path in MyFiles())
        {
            string code = CsharpSource.Read(File.ReadAllText(path)).Code;
            foreach (Regex rule in new[] { FilePrimitive, FormatOwnership })
            {
                foreach (Match match in rule.Matches(code))
                {
                    found.Add(LibrarySources.Relative(path) + " -> " + match.Value);
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "Durable plan state owns no format and no path, which is why a role can adopt it without a " +
            "migration. Something here now owns one:\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void No_literal_here_looks_like_a_row_tag_a_field_name_or_a_file_name()
    {
        // The second half of the same property. A library that never opened a file
        // could still hand a role a string the role wrote straight to disk, and
        // then the format would live here after all - in the one place nobody would
        // look for it.
        var found = new List<string>();
        foreach (string path in MyFiles())
        {
            foreach (string literal in CsharpSource.Read(File.ReadAllText(path)).Literals)
            {
                if (literal.IndexOf('\t') >= 0
                    || literal.EndsWith(".tsv", System.StringComparison.OrdinalIgnoreCase)
                    || literal.EndsWith(".tmp", System.StringComparison.OrdinalIgnoreCase)
                    || literal.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(LibrarySources.Relative(path) + " -> " + literal);
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "A literal here looks like something a role would write to disk:\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void The_only_persistence_this_area_knows_about_is_the_one_pinned_plumbing_type()
    {
        // Named types rather than a blanket ban, because the point is not that
        // nothing is written - something is - but that exactly one door exists and
        // it is the one the repository validator already pins.
        var doors = new HashSet<string>();
        foreach (string path in MyFiles())
        {
            string code = CsharpSource.Read(File.ReadAllText(path)).Code;
            foreach (Match match in Regex.Matches(code, @"\bNpc(?:SidecarFile|AtomicText)\b"))
            {
                doors.Add(match.Value);
            }
        }

        Assert.Equal(new[] { "NpcSidecarFile" }, doors.OrderBy(name => name, System.StringComparer.Ordinal));
    }

    [Fact]
    public void The_area_this_test_is_about_actually_exists()
    {
        // Every check above passes trivially over an empty list, and a renamed
        // folder is exactly how that would happen quietly.
        Assert.True(MyFiles().Count >= 7);
    }

    private static IReadOnlyList<string> MyFiles()
    {
        string folder = Path.Combine(LibrarySources.Library, "Interruption");
        if (!Directory.Exists(folder))
        {
            return new string[0];
        }

        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).ToList();
        files.Sort(System.StringComparer.Ordinal);
        return files;
    }
}
