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

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cc-marker-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

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

    private string Adopt(Func<string, bool>? usable = null) =>
        MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt", usable ?? IsGuid, _log.Add);

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
        // `author-id.dat` is not in CartographerFirstRunFiles.Names — so every
        // brand-new player would have been classified as a returning one and
        // the #264 introduction would never have run for anybody again.
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), Identity);
        File.WriteAllText(Path.Combine(_directory, "cartographer-strings-template.tsv"), "x");

        var listed = new List<string>();
        foreach (string path in Directory.GetFiles(_directory))
        {
            listed.Add(Path.GetFileName(path));
        }

        Assert.DoesNotContain("author-id.dat", listed);

        // And the same listing WITH the marker in the product directory - the
        // shape the first attempt shipped - is what it would have cost:
        var renamedInPlace = new List<string>(listed) { "author-id.dat" };
        Assert.False(CartographerFirstRunFiles.IsOnlySelfWritten(renamedInPlace));
        Assert.Equal(
            LegacyEvidence.Ambiguous,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: false,
                configuredBeforeThisRelease: !CartographerFirstRunFiles.IsOnlySelfWritten(renamedInPlace),
                probeFailed: false)));

        Assert.True(CartographerFirstRunFiles.IsOnlySelfWritten(listed));
        Assert.Equal(
            LegacyEvidence.None,
            LegacyEvidenceRule.Evaluate(new LegacyEvidenceFacts(
                thisWorldHasData: false, anyWorldHasData: false, profileWideDataExists: false,
                configuredBeforeThisRelease: !CartographerFirstRunFiles.IsOnlySelfWritten(listed),
                probeFailed: false)));
    }

    [Fact]
    public void ANameThisBuildUsedToWriteIsStillRecognisedAsOurs()
    {
        // A returning player may still have the old file: it is only removed
        // once the adoption round trip has succeeded, and never if it fails.
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("author-id.txt"));
        Assert.True(CartographerFirstRunFiles.IsSelfWritten("onboarding-shown.txt"));
    }

    // ---- carrying the identity across --------------------------------------

    [Fact]
    public void AnOlderBuildsIdentityBecomesTheMarkerAndStillParses()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        string path = Adopt();

        Assert.Equal(Marker("author-id.dat"), path);
        Assert.Equal(Identity, File.ReadAllText(path));
        Assert.True(Guid.TryParseExact(File.ReadAllText(path).Trim(), "N", out _));
        Assert.False(File.Exists(Legacy("author-id.txt")));
    }

    [Fact]
    public void AMarkerThatIsAlreadyThereWinsAndTheOldFileIsNotTouched()
    {
        Directory.CreateDirectory(Path.Combine(_directory, MarkerFile.FolderName));
        File.WriteAllText(Marker("author-id.dat"), Identity);
        File.WriteAllText(Legacy("author-id.txt"), "an older identity");

        string path = Adopt();

        Assert.Equal(Identity, File.ReadAllText(path));

        // Deliberately NOT deleted. The first version removed it here, so one
        // interrupted copy destroyed the identity on the following start.
        Assert.True(File.Exists(Legacy("author-id.txt")));
    }

    [Fact]
    public void ContentsThisBuildCannotUseAreLeftAloneAndReported()
    {
        File.WriteAllText(Legacy("author-id.txt"), "not a guid");

        string path = Adopt();

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Legacy("author-id.txt")));
        Assert.Contains(_log, line => line.Contains("this build can use"));
    }

    [Fact]
    public void WithNothingToAdoptItNamesTheMarkerAndCreatesNothing()
    {
        string path = MarkerFile.Adopt(
            _directory, "onboarding-shown.dat", "onboarding-shown.txt", _ => true, _log.Add);

        Assert.Equal(Marker("onboarding-shown.dat"), path);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(_directory, MarkerFile.FolderName)));
        Assert.Empty(_log);
    }

    [Fact]
    public void AdoptingTwiceIsTheSameAsAdoptingOnce()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        string first = Adopt();
        string second = Adopt();

        Assert.Equal(first, second);
        Assert.Equal(Identity, File.ReadAllText(second));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, MarkerFile.FolderName)));
    }

    [Fact]
    public void AFailureLeavesTheOldFileReadableAndSaysWhy()
    {
        File.WriteAllText(Legacy("author-id.txt"), Identity);

        // A file where the subfolder needs to be: creating the directory
        // fails, so the copy cannot happen.
        File.WriteAllText(Path.Combine(_directory, MarkerFile.FolderName), "in the way");

        string path = Adopt();

        Assert.False(File.Exists(path));
        Assert.Equal(Identity, File.ReadAllText(Legacy("author-id.txt")));
        Assert.NotEmpty(_log);
        Assert.Contains(_log, line => line.Contains("left where it is"));
    }

    [Fact]
    public void AFolderThatIsNotThereIsNotAnError()
    {
        string missing = Path.Combine(_directory, "not-created-yet");

        string path = MarkerFile.Adopt(missing, "author-id.dat", "author-id.txt", IsGuid, _log.Add);

        Assert.Equal(Path.Combine(missing, MarkerFile.FolderName, "author-id.dat"), path);
        Assert.False(Directory.Exists(missing));
        Assert.Empty(_log);
    }

    [Fact]
    public void AnEmptyDirectoryIsRefusedRatherThanResolvingToSomewhereElse()
    {
        Assert.Throws<ArgumentException>(
            () => MarkerFile.Adopt(string.Empty, "author-id.dat", "author-id.txt"));
    }

    [Fact]
    public void TheMarkerIsNotOneOfTheExtensionsAConfigEditorOpens()
    {
        foreach (string claimed in new[] { ".txt", ".cfg", ".json", ".ini", ".yml", ".yaml", ".xml" })
        {
            Assert.NotEqual(claimed, MarkerFile.Extension, StringComparer.OrdinalIgnoreCase);
        }
    }
}
