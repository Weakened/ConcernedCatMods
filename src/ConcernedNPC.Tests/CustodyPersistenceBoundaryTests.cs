namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Why the camp and custody areas need no golden fixture of their own,
/// proved rather than asserted.
///
/// <b>The zero-migration promise.</b> A pre-refactor data directory, dropped in
/// unchanged, must still work with no migration code having run. There are two
/// ways to keep that promise. One is to move a file format and pin its bytes;
/// the settlement journal is pinned exactly that way, by a literal fixture in
/// the settlement suite, because that format did not move. The other is to own
/// no format at all, and that is what these two areas do: the custody ledger
/// reasons about rows it is handed and hands rows back, and the role's own
/// writer - its schema, its row tags, its path, its checksum - is untouched.
///
/// <b>So this is the fixture.</b> A file format cannot drift here, because
/// there is nothing here that reads or writes one; if that ever stops being
/// true, this fails, and whoever made it untrue has to say which durable file
/// this library now owns and what happens to the ones that already exist.
/// </summary>
public class CustodyPersistenceBoundaryTests
{
    /// <summary>Anything that would mean this library had learned to read or
    /// write a file. Deliberately blunt: a false positive here is a comment
    /// away from being fixed, and a false negative is a shipped save format
    /// nobody decided on.</summary>
    private static readonly string[] Persistence =
    {
        "System.IO", "File.", "Directory.", "Path.Combine", "StreamReader", "StreamWriter",
        "FileStream", "Serializable", "Serialize", "BinaryFormatter", "AssemblyQualifiedName",
        "SchemaVersion",
    };

    [Fact]
    public void Nothing_in_camp_or_custody_reads_or_writes_a_file()
    {
        var found = new List<string>();
        foreach (string path in MyFiles())
        {
            string code = CsharpSource.Read(File.ReadAllText(path)).Code;
            foreach (string token in Persistence)
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    found.Add(LibrarySources.Relative(path) + " -> " + token);
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "The camp and custody areas own no durable format, which is why moving them needs no " +
            "migration. Something here now touches one:\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void No_literal_here_looks_like_a_row_tag_a_field_name_or_a_file()
    {
        // The second half of the same property. A library that never opened a
        // file could still hand a role a string the role wrote straight to
        // disk, and then the format would live here after all - in the one
        // place nobody would look for it.
        var found = new List<string>();
        foreach (string path in MyFiles())
        {
            foreach (string literal in CsharpSource.Read(File.ReadAllText(path)).Literals)
            {
                if (literal.IndexOf('\t') >= 0
                    || literal.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
                    || literal.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || literal.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
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
    public void The_areas_this_test_is_about_actually_exist()
    {
        // Every check above passes trivially over an empty list, and a renamed
        // folder is exactly how that would happen quietly.
        Assert.True(MyFiles().Count >= 20);
    }

    private static IReadOnlyList<string> MyFiles()
    {
        var files = new List<string>();
        foreach (string area in new[] { "Camp", "Custody" })
        {
            string folder = Path.Combine(LibrarySources.Library, area);
            if (Directory.Exists(folder))
            {
                files.AddRange(Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories));
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
