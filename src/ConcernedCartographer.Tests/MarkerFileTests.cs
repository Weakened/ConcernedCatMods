using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>#304: a mod-manager configuration editor listed `author-id.txt`
/// among the files a player may edit. It is a generated GUID the mod writes for
/// itself, so both markers move into a `state` subfolder — and the migration has
/// to carry the identity across without ever being able to lose it.</summary>
public sealed class MarkerFileTests : IDisposable
{
    private const string Identity = "9f2c1b7e4a6d40f8b1c3e5a7d9f0b2c4";
    private const string OtherIdentity = "11112222333344445555666677778888";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cc-marker-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

    /// <summary>Every place the author marker has lived, newest first — the same
    /// order and the same literals the product passes.</summary>
    private static readonly string[] PriorLocations = { "author-id.dat", "author-id.txt" };

    public MarkerFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not a test failure, and the exception
            // a locked or read-only file throws is not only IOException.
        }
    }

    private string Legacy(string name) => Path.Combine(_directory, name);

    private string Marker(string name) => Path.Combine(_directory, MarkerFile.FolderName, name);

    private MarkerFile.MarkerSearch Resolve(out string? contents, Func<string, bool>? usable = null) =>
        MarkerFile.Resolve(
            _directory, "author-id.dat", PriorLocations, usable ?? IsGuid, _log.Add,
            out _, out contents);

    private string? Resolved(Func<string, bool>? usable = null)
    {
        Resolve(out string? contents, usable);
        return contents;
    }

    private void WriteMarker(string contents)
    {
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), contents);
    }

    private static bool IsGuid(string text) => Guid.TryParseExact(text.Trim(), "N", out _);

    // ---- the regression this shape exists to avoid --------------------------

    [Fact]
    public void AMarkerIsInvisibleToTheFreshInstallProbe()
    {
        // CartographerLegacyProbe lists the product directory with
        // Directory.GetFiles and asks whether everything in it is a name this
        // build writes for itself. GetFiles does not return subdirectories, so a
        // marker under "state" is never in that listing.
        WriteMarker(Identity);
        File.WriteAllText(Path.Combine(_directory, "cartographer-strings-template.tsv"), "x");

        var listed = new List<string>();
        foreach (string path in Directory.GetFiles(_directory))
        {
            listed.Add(Path.GetFileName(path));
        }

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
        // product directory. Only this mod ever writes that name, so it must not
        // read as "somebody configured this" — the fail-open reading that made
        // #343 suppress the introduction forever. An earlier version of this test
        // asserted the opposite and locked the defect in.
        var listed = new List<string> { "author-id.dat", "onboarding-shown.dat" };

        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(listed));
    }

    [Fact]
    public void AQuarantinedCompanionSidecarIsAlsoOursRatherThanAPlayersDoing()
    {
        // CompanionSidecarStore quarantines an unreadable sidecar to
        // `<name>.companions.tsv.corrupt` in this same directory, and the sidecar
        // itself is written with no player involvement. Left unrecognised, a
        // brand-new profile whose first sidecar write was torn is classified as a
        // returning player and never sees the #264 introduction — #343 again,
        // through a different file.
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("thor.companions.tsv.corrupt"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("thor.companions.tsv.corrupt.7"));
        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(
            new List<string> { "thor.companions.tsv", "thor.companions.tsv.corrupt" }));
    }

    [Fact]
    public void TheMarkerIsNotOneOfTheExtensionsAConfigEditorOpens()
    {
        // Restored. Without it, setting Extension to ".cfg" re-exposes a
        // generated GUID to the editor #304 is about with the whole suite green.
        Assert.Equal(".dat", MarkerFile.Extension);
        Assert.DoesNotContain(
            MarkerFile.Extension,
            new[] { ".txt", ".cfg", ".json", ".ini", ".yml", ".yaml", ".xml" });
    }

    // ---- an identity is never lost ------------------------------------------

    [Fact]
    public void TheOldFilesValueBecomesTheMarker()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        Assert.Equal(MarkerFile.MarkerSearch.Found, Resolve(out string? contents));
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
        // Commit 6903a65 wrote `author-id.dat` into the product directory.
        // Knowing only the `.txt` name, resolution found nothing, the caller
        // minted a fresh GUID, and the real identity was orphaned.
        File.WriteAllText(Legacy("author-id.dat"), Identity);

        Assert.Equal(MarkerFile.MarkerSearch.Found, Resolve(out string? contents));
        Assert.Equal(Identity, contents);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void TheNewestPriorLocationWinsAndEveryOtherCopyIsRemoved()
    {
        // The later build's file is the value the atlas was most recently keyed
        // on. Taking the oldest picked a value nothing was using AND left the
        // other raw GUID sitting in the config editor for the life of the
        // profile — the literal #304 report, unfixed.
        File.WriteAllText(Legacy("author-id.txt"), OtherIdentity);
        File.WriteAllText(Legacy("author-id.dat"), Identity);

        Assert.Equal(Identity, Resolved());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AMintedMarkerDoesNotHideAPriorFileThatIsStillThere()
    {
        // The defect that made the previous fix ineffective. A transient failure
        // makes the caller mint a GUID and write it as the marker; from then on
        // the marker PARSES, so validating its contents was not enough — nothing
        // looked at the older file again and the real identity was orphaned for
        // good. A prior file that still exists proves adoption never finished.
        WriteMarker(OtherIdentity);
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        Assert.Equal(MarkerFile.MarkerSearch.Found, Resolve(out string? contents));
        Assert.Equal(Identity, contents);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AnUnusableMarkerDoesNotEndTheSearch()
    {
        WriteMarker(string.Empty);
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        Assert.Equal(Identity, Resolved());
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
    }

    [Fact]
    public void WithNoPriorFileAGoodMarkerIsTheAnswer()
    {
        WriteMarker(Identity);

        Assert.Equal(MarkerFile.MarkerSearch.Found, Resolve(out string? contents));
        Assert.Equal(Identity, contents);
    }

    [Fact]
    public void GarbageInAPriorFileIsNeverAdoptedAndItIsSaidOutLoud()
    {
        // Restored acceptance criterion: the report, not only the refusal.
        File.WriteAllText(Legacy("author-id.txt"), "not a guid");

        Assert.Equal(MarkerFile.MarkerSearch.PriorFileUnread, Resolve(out string? contents));
        Assert.Null(contents);
        Assert.False(File.Exists(Marker("author-id.dat")));
        Assert.True(File.Exists(Legacy("author-id.txt")));
        Assert.Contains(_log, line => line.Contains("this build can use"));
    }

    [Fact]
    public void AnUnreadablePriorFileMeansTheCallerMustNotStartASecondIdentity()
    {
        // The real scenario: a mod-manager config editor, an antivirus on-access
        // scan or a sync client holding the file for a moment during startup.
        // The old code swallowed the read failure silently and the caller minted
        // a second identity that then hid this file for ever.
        File.WriteAllText(Legacy("author-id.txt"), Identity);
        using (new FileStream(
            Legacy("author-id.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(MarkerFile.MarkerSearch.PriorFileUnread, Resolve(out string? contents));
            Assert.Null(contents);
            Assert.NotEmpty(_log);
        }

        // And once the obstruction clears, the identity is still there to find.
        _log.Clear();
        Assert.Equal(Identity, Resolved());
    }

    [Fact]
    public void NothingAnywhereIsTheOnlyAnswerThatPermitsANewIdentity()
    {
        Assert.Equal(MarkerFile.MarkerSearch.NothingAnywhere, Resolve(out string? contents));
        Assert.Null(contents);
        Assert.Empty(_log);
    }

    [Fact]
    public void AFailureLeavesTheOldFileReadableAndSaysWhy()
    {
        // Restored. Without it the `false` arm of `if (TryWrite(...)) { remove }`
        // has no coverage, and a later refactor hoisting that removal out of the
        // branch destroys a player's only copy with the suite green.
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        // A file where the subfolder needs to be: the directory cannot be
        // created, so the copy cannot happen.
        File.WriteAllText(Path.Combine(_directory, MarkerFile.FolderName), "in the way");

        Assert.Equal(Identity, Resolved());
        Assert.Equal(Identity, File.ReadAllText(Legacy("author-id.txt")));
        Assert.NotEmpty(_log);
    }

    [Fact]
    public void ResolvingTwiceIsTheSameAsResolvingOnce()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);
        Resolve(out _);
        Resolve(out string? second);

        Assert.Equal(Identity, second);
        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));

        // Both directories. Asserting only on "state" would let a broken delete
        // leave author-id.txt in the settings folder — the literal bug report —
        // with every test still green.
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
    public void AWriteReplacesWhatWasThereAndIsVerifiedAtTheDestination()
    {
        MarkerFile.TryWrite(Marker("author-id.dat"), OtherIdentity, _log.Add);
        Assert.True(MarkerFile.TryWrite(Marker("author-id.dat"), Identity, _log.Add));

        Assert.Equal(Identity, File.ReadAllText(Marker("author-id.dat")));
    }

    [Fact]
    public void AWriteThatCannotHappenSaysSoAndReportsFalse()
    {
        File.WriteAllText(Path.Combine(_directory, MarkerFile.FolderName), "in the way");

        Assert.False(MarkerFile.TryWrite(Marker("author-id.dat"), Identity, _log.Add));
        Assert.NotEmpty(_log);
    }

    // ---- refusing to run at all ---------------------------------------------

    [Fact]
    public void AnEmptyDirectoryIsARefusalRatherThanAGuess()
    {
        Assert.Throws<ArgumentException>(() =>
            MarkerFile.Resolve(string.Empty, "author-id.dat", PriorLocations, IsGuid, _log.Add,
                out _, out _));
    }

    [Fact]
    public void TheCheckIsRequiredRatherThanDefaultingToAnythingWillDo()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MarkerFile.Resolve(_directory, "author-id.dat", PriorLocations, null!, _log.Add,
                out _, out _));
    }

    [Fact]
    public void APathIsAlwaysReturnedEvenWhenNothingIsFound()
    {
        MarkerFile.Resolve(_directory, "author-id.dat", PriorLocations, IsGuid, _log.Add,
            out string path, out _);

        // Under "state", always. An earlier fallback handed back a product-root
        // path, which is the location that caused #343.
        Assert.Equal(Marker("author-id.dat"), path);
    }
}
