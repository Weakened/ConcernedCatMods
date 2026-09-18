using System;
using System.IO;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace TheConcernedCat.ConcernedCartographer.Tests;

/// <summary>#304: a mod-manager config editor listed <c>author-id.txt</c> among
/// the files a player may edit. It is a generated GUID the mod writes for
/// itself, so it is a <c>.dat</c> marker now — and an older build's <c>.txt</c>
/// has to become that marker without the identity in it changing.</summary>
public sealed class MarkerFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cc-marker-" + Guid.NewGuid().ToString("N"));

    public MarkerFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    [Fact]
    public void TheMarkerIsNotOneOfTheExtensionsAConfigEditorOpens()
    {
        Assert.DoesNotContain(
            MarkerFile.Extension,
            new[] { ".txt", ".cfg", ".json", ".ini", ".yml", ".yaml", ".xml" });
    }

    [Fact]
    public void AnOlderBuildsFileBecomesTheMarkerWithItsContentsIntact()
    {
        File.WriteAllText(Path_("author-id.txt"), "9f2c1b7e4a6d40f8b1c3e5a7d9f0b2c4");

        string path = MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt");

        Assert.Equal(Path_("author-id.dat"), path);
        Assert.Equal("9f2c1b7e4a6d40f8b1c3e5a7d9f0b2c4", File.ReadAllText(path));
    }

    [Fact]
    public void TheOldFileIsGoneAfterwardsSoAConfigEditorStopsShowingIt()
    {
        File.WriteAllText(Path_("author-id.txt"), "abc");

        MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt");

        Assert.False(File.Exists(Path_("author-id.txt")));
    }

    [Fact]
    public void AMarkerThatIsAlreadyThereWins()
    {
        File.WriteAllText(Path_("author-id.dat"), "current");
        File.WriteAllText(Path_("author-id.txt"), "stale");

        string path = MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt");

        Assert.Equal("current", File.ReadAllText(path));
        Assert.False(File.Exists(Path_("author-id.txt")));
    }

    [Fact]
    public void WithNothingToAdoptItJustNamesTheMarkerAndCreatesNoFile()
    {
        string path = MarkerFile.Adopt(_directory, "onboarding-shown.dat", "onboarding-shown.txt");

        Assert.Equal(Path_("onboarding-shown.dat"), path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AdoptingTwiceIsTheSameAsAdoptingOnce()
    {
        File.WriteAllText(Path_("author-id.txt"), "once");

        string first = MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt");
        string second = MarkerFile.Adopt(_directory, "author-id.dat", "author-id.txt");

        Assert.Equal(first, second);
        Assert.Equal("once", File.ReadAllText(second));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AFolderThatIsNotThereIsNotAnError()
    {
        string missing = Path.Combine(_directory, "not-created-yet");

        string path = MarkerFile.Adopt(missing, "author-id.dat", "author-id.txt");

        Assert.Equal(Path.Combine(missing, "author-id.dat"), path);
        Assert.False(Directory.Exists(missing));
    }
}
