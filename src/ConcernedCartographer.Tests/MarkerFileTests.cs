using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>#304: a mod-manager configuration editor listed `author-id.txt`
/// among the files a player may edit. It is a generated GUID the mod writes for
/// itself, so both markers move into a `state` subfolder — and the migration
/// has to carry the identity across without ever being able to lose it.</summary>
public sealed class MarkerFileTests : IDisposable
{
    private const string Identity = "9f2c1b7e4a6d40f8b1c3e5a7d9f0b2c4";
    private const string OtherIdentity = "11112222333344445555666677778888";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cc-marker-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

    /// <summary>Every place the author marker has lived, oldest first: the
    /// original `.txt`, then the build that changed the extension without
    /// moving the file (commit 6903a65).</summary>
    private static readonly string[] PriorLocations = { "author-id.txt", "author-id.dat" };

    public MarkerFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not a test failure, and the
            // exception a locked or read-only file throws is not only IOException.
        }
    }

    private string Legacy(string name) => Path.Combine(_directory, name);

    private string Marker(string name) => Path.Combine(_directory, MarkerFile.FolderName, name);

    private bool Resolve(out string? contents, Func<string, bool>? usable = null) =>
        MarkerFile.TryResolve(
            _directory, "author-id.dat", PriorLocations, usable ?? IsGuid, _log.Add,
            out _, out contents);

    private string? Resolved(Func<string, bool>? usable = null)
    {
        Resolve(out string? contents, usable);
        return contents;
    }

    private static bool IsGuid(string text) => Guid.TryParseExact(text.Trim(), "N", out _);

    // ---- the regression this shape exists to avoid --------------------------

    [Fact]
    public void AMarkerIsInvisibleToTheFreshInstallProbe()
    {
        // CartographerLegacyProbe lists the product directory with
        // Directory.GetFiles and asks whether everything in it is a name this
        // build writes for itself. GetFiles does not return subdirectories, so
        // a marker under "state" is never in that listing.
        //
        // The first attempt at #304 renamed the markers in place instead, and
        // classified every brand-new player as a returning one — so the #264
        // introduction would never have run for anybody again (#343).
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), Identity);
        File.WriteAllText(Path.Combine(_directory, "cartographer-strings-template.tsv"), "x");

        var listed = new List<string>();
        foreach (string path in Directory.GetFiles(_directory))
        {
            listed.Add(Path.GetFileName(path));
        }

        // The invariant that actually fixes #304 and #343: the marker is not
        // in the product directory's listing at all.
        Assert.DoesNotContain("author-id.dat", listed);
        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(listed));
        Assert.Equal(
            LegacyEvidence.None,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: false,
                configuredBeforeThisRelease: !CartographerFirstRunFiles.IsOnlySelfWritten(listed),
                probeFailed: false)));
    }

    [Fact]
    public void AStrayMarkerInTheProductDirectoryIsNotEvidenceAPlayerWasHere()
    {
        // A profile that ran the intermediate build has `author-id.dat` in the
        // product directory. Only this mod ever writes that name, so it must
        // not read as "somebody configured this" — that is precisely the
        // fail-open reading that made #343 suppress the introduction forever.
        //
        // An earlier version of this test asserted the opposite and locked the
        // defect in.
        var listed = new List<string> { "author-id.dat", "onboarding-shown.dat" };

        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(listed));
        Assert.Equal(
            LegacyEvidence.None,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: false,
                configuredBeforeThisRelease: !CartographerFirstRunFiles.IsOnlySelfWritten(listed),
                probeFailed: false)));
    }

    // ---- an identity is never lost ------------------------------------------

    [Fact]
    public void TheOldFilesValueBecomesTheMarker()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        Assert.True(Resolve(out string? contents));
        Assert.Equal(Identity, contents);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
    }

    [Fact]
    public void TheOldFileIsGoneAfterwardsSoAConfigEditorStopsShowingIt()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);
        Resolve(out _);

        Assert.False(File.Exists(Legacy("author-id.txt")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AMarkerLeftInTheProductDirectoryByTheBuildBetweenIsAdoptedToo()
    {
        // The regression this closes: commit 6903a65 wrote `author-id.dat`
        // into the product directory. Knowing only the `.txt` name, resolution
        // found nothing, the caller minted a fresh GUID, and the profile's real
        // identity was orphaned — on exactly the machines this migration is for.
        File.WriteAllText(Legacy("author-id.dat"), Identity);

        Assert.True(Resolve(out string? contents));
        Assert.Equal(Identity, contents);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void TheOldestLocationWinsWhenTwoAreThere()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);
        File.WriteAllText(Legacy("author-id.dat"), OtherIdentity);

        Assert.Equal(Identity, Resolved());
    }

    [Fact]
    public void AnUnusableMarkerDoesNotEndTheSearch()
    {
        // The defect that cost an identity: resolution used to return as soon
        // as the marker file existed, without reading it. A marker minted
        // during one transient failure — or truncated by a torn write — then
        // hid the real identity sitting beside it, permanently, because no
        // later start ever looked at the old file again.
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), string.Empty);
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        Assert.True(Resolve(out string? contents));
        Assert.Equal(Identity, contents);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
    }

    [Fact]
    public void AGoodMarkerIsUsedAndTheOldFileIsLeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), Identity);
        File.WriteAllText(Legacy("author-id.txt"), OtherIdentity);

        Assert.Equal(Identity, Resolved());

        // Not deleted: this build did not read it, and removing somebody's
        // data because we happen to have our own copy is not ours to do.
        Assert.True(File.Exists(Legacy("author-id.txt")));
    }

    [Fact]
    public void GarbageIsNeverAdoptedAndTheOldFileSurvives()
    {
        File.WriteAllText(Legacy("author-id.txt"), "not a guid");

        Assert.False(Resolve(out string? contents));
        Assert.Null(contents);
        Assert.False(File.Exists(Marker("author-id.dat")));
        Assert.True(File.Exists(Legacy("author-id.txt")));
    }

    [Fact]
    public void NothingToAdoptIsNotAFailure()
    {
        Assert.False(Resolve(out string? contents));
        Assert.Null(contents);
        Assert.Empty(_log);
    }

    [Fact]
    public void ResolvingTwiceIsTheSameAsResolvingOnce()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);
        Resolve(out _);
        Resolve(out string? second);

        Assert.Equal(Identity, second);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));

        // Both directories, not just one. Asserting only on "state" would let
        // a broken delete leave author-id.txt sitting in the settings folder —
        // the literal bug report — with every test still green.
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, MarkerFile.FolderName)));
    }

    // ---- writing ------------------------------------------------------------

    [Fact]
    public void AWriteLeavesNoTemporaryFileBehind()
    {
        Assert.True(MarkerFile.TryWrite(Marker("author-id.dat"), Identity, _log.Add));

        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, MarkerFile.FolderName)));
        Assert.Empty(_log);
    }

    [Fact]
    public void AWriteReplacesWhatWasThere()
    {
        MarkerFile.TryWrite(Marker("author-id.dat"), OtherIdentity, _log.Add);
        Assert.True(MarkerFile.TryWrite(Marker("author-id.dat"), Identity, _log.Add));

        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
    }

    // ---- refusing to run at all ---------------------------------------------

    [Fact]
    public void AnEmptyDirectoryIsARefusalRatherThanAGuess()
    {
        Assert.Throws<ArgumentException>(() =>
            MarkerFile.TryResolve(string.Empty, "author-id.dat", PriorLocations, IsGuid, _log.Add,
                out _, out _));
    }

    [Fact]
    public void TheCheckIsRequiredRatherThanDefaultingToAnythingWillDo()
    {
        // Both safety arguments used to default to their unsafe value, so the
        // old three-argument call shape still compiled and adopted whatever it
        // found, unvalidated and unlogged.
        Assert.Throws<ArgumentNullException>(() =>
            MarkerFile.TryResolve(_directory, "author-id.dat", PriorLocations, null!, _log.Add,
                out _, out _));
    }

    [Fact]
    public void APathIsAlwaysReturnedEvenWhenNothingIsFound()
    {
        MarkerFile.TryResolve(_directory, "author-id.dat", PriorLocations, IsGuid, _log.Add,
            out string path, out _);

        // Under "state", always. The old fallback handed back a product-root
        // path, which is the location that caused #343.
        Assert.Equal(Marker("author-id.dat"), path);
    }
}
