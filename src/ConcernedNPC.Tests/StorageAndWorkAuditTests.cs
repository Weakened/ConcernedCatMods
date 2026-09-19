using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Checks over the container and work-area sources that cannot be
/// expressed as behaviour, because they are about what the code <i>could</i>
/// do.
///
/// A test that drives the registry proves no fallback happened this time. Only a
/// scan of the source can prove there is no code in the package able to fall
/// back, pick a chest by distance, or move a player to mark an area - and those
/// three absences are the whole of what #374 and #375 ask for beyond the
/// behaviour above.
///
/// These reuse <see cref="CsharpSource"/> rather than matching raw text, for the
/// reason that type exists: one URL in a literal used to delete every literal
/// after it on the line, from both audits, and an audit that can be switched off
/// by an unrelated string reports green.</summary>
public class StorageAndWorkAuditTests
{
    private static IEnumerable<string> Sources(string folder) =>
        LibrarySources.Files()
            .Where(path => path.Contains(Path.DirectorySeparatorChar + folder + Path.DirectorySeparatorChar));

    [Fact]
    public void TheFoldersThisLeafOwnsExistAndAreScanned()
    {
        // Without this, every audit below passes over nothing at all.
        Assert.NotEmpty(Sources("Storage"));
        Assert.NotEmpty(Sources("Work"));
    }

    [Fact]
    public void NothingInTheseFoldersCanMoveOrEvenSeeAPlayer()
    {
        // The first of #375's two named failures: moving the player in order to
        // establish an area. An area here is built from coordinates handed in,
        // and nothing in the package is able to ask where anybody is standing or
        // to put them somewhere - so marking an area by walking to the middle of
        // it is a role's input method rather than the only one there is.
        // Case-insensitive on purpose: a rule a lower-case local can walk past
        // is a rule that reports green while the thing it forbids is in the
        // file.
        var forbidden = new Regex(
            @"\b(Player|LocalPlayer|Teleport\w*|SetPosition|MoveTo|WarpTo|CurrentPosition)\b",
            RegexOptions.IgnoreCase);

        var found = new List<string>();
        foreach (string path in Sources("Storage").Concat(Sources("Work")))
        {
            CsharpSource scanned = CsharpSource.Read(File.ReadAllText(path));
            foreach (Match match in forbidden.Matches(scanned.Code))
            {
                found.Add(LibrarySources.Relative(path) + ": " + match.Value);
            }
        }

        Assert.True(
            found.Count == 0,
            "An area is established from coordinates, never from where somebody is standing, and nothing here " +
            "may move anybody: " + string.Join("; ", found));
    }

    [Fact]
    public void NothingInTheContainerFolderPicksAContainerByDistance()
    {
        // #374's second forbidden behaviour is depositing into an arbitrary
        // nearby container, and the cheapest way to guarantee it never happens
        // is for the code that could do it not to exist. The only distance fact
        // the model has arrives pre-answered from the adapter, as
        // NpcContainerSighting.WithinReach.
        //
        // NpcContainerPlace is the one exemption, and it is a different
        // question: it compares a remembered position with a seen one to decide
        // whether these are the same chest. It never ranks two containers and
        // never chooses one, and the test below pins that it holds no
        // collection to choose from.
        // Narrow on purpose. Math.Min over two counts is arithmetic about how
        // many units fit; it is not a choice between containers. What is
        // forbidden is measuring how far away a container is, and ranking a set
        // of them.
        var arithmetic = new Regex(
            @"\b(HorizontalDistanceTo|VerticalDistanceTo|OrderBy|OrderByDescending|MinBy|MaxBy|\.Sort)\b",
            RegexOptions.IgnoreCase);
        var selection = new Regex(
            @"\b\w*(Nearest|Closest|BestContainer|AnyContainer|FindContainer)\w*\b", RegexOptions.IgnoreCase);

        var found = new List<string>();
        foreach (string path in Sources("Storage"))
        {
            string name = Path.GetFileName(path);
            CsharpSource scanned = CsharpSource.Read(File.ReadAllText(path));

            if (!string.Equals(name, "NpcContainerPlace.cs", StringComparison.Ordinal))
            {
                foreach (Match match in arithmetic.Matches(scanned.Code))
                {
                    found.Add(LibrarySources.Relative(path) + ": " + match.Value);
                }
            }

            foreach (Match match in selection.Matches(scanned.Code))
            {
                found.Add(LibrarySources.Relative(path) + ": " + match.Value);
            }
        }

        Assert.True(
            found.Count == 0,
            "A container an NPC may use is one the player enabled and the job named; nothing here may search " +
            "for one: " + string.Join("; ", found));
    }

    [Fact]
    public void TheOneExemptionToTheDistanceRuleHoldsNoCollectionToChooseFrom()
    {
        // The exemption above is only safe while the exempt type cannot see two
        // containers at once. A list inside it would turn "is this the same
        // chest" into "which of these is closest" with no other edit.
        Assert.DoesNotContain(
            typeof(TheConcernedCat.ConcernedNPC.Storage.NpcContainerPlace)
                .GetFields(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public),
            field => typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void NothingInTheWorkFolderNamesAMapProduct()
    {
        // #375: ConcernedNPC owns the generic work-area contract and must not
        // depend on the mod that draws a richer one. The validator already
        // forbids this package naming any product; this says it about the one
        // place a temptation exists, with the failure message that explains why.
        var products = new Regex(@"\b(Cartographer|Foreman|Teamster|Steward)\b");

        var found = new List<string>();
        foreach (string path in Sources("Work").Concat(Sources("Storage")))
        {
            CsharpSource scanned = CsharpSource.Read(File.ReadAllText(path));
            foreach (Match match in products.Matches(scanned.Code))
            {
                found.Add(LibrarySources.Relative(path) + ": " + match.Value);
            }
        }

        Assert.True(
            found.Count == 0,
            "A richer work area is registered from outside through INpcWorkAreaProvider; this package never " +
            "names the mod that draws one: " + string.Join("; ", found));
    }

    [Fact]
    public void TheseSourcesStillPassTheLibraryWideAudit()
    {
        // Belt and braces against a rule that stops scanning a new folder.
        var violations = new List<string>();
        foreach (string path in Sources("Storage").Concat(Sources("Work")))
        {
            violations.AddRange(LibrarySources.Violations(Path.GetFileName(path), File.ReadAllText(path)));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
