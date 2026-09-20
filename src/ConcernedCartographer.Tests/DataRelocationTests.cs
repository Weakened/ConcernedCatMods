using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>#304: a mod manager presents everything under
/// <c>BepInEx/config</c> as a mod's settings, and Quandru was shown
/// <c>author-id.txt</c> there — a generated GUID the atlas keys ownership on.
/// The fix is that this product's data stops living where configuration lives.
///
/// <b>These tests are about an upgrade, not about a feature.</b> Cartographer
/// is in public beta, so every installation this code will ever run on already
/// has the whole of its history in the settings folder. Moving a file is a
/// migration, and the property that matters is not that the new layout is tidy
/// — it is that nobody loses anything on the way to it.</summary>
public sealed class DataRelocationTests : IDisposable
{
    private const string Identity = "9f2c1b7e4a6d40f8b1c3e5a7d9f0b2c4";
    private const string OnboardedAt = "2026-01-02T03:04:05.0000000Z";
    private const long WorldUid = 1234567890L;

    /// <summary>Rules a player edited. Nothing in the starter set looks like
    /// this, which is the point: #385 exists so an edited file is never
    /// trampled, and a moved file must not become the exception.</summary>
    private const string EditedRules = "Crypt\tMy own note about crypts\ntrader\tHaldor, finally\n";

    private static readonly string[] AuthorPriorNames = { "author-id.dat", "author-id.txt" };

    private static readonly string[] OnboardingPriorNames =
        { "onboarding-shown.dat", "onboarding-shown.txt" };

    private readonly string _profile =
        Path.Combine(Path.GetTempPath(), "cc-relocate-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

    private string Config => Path.Combine(_profile, "config", "ConcernedCatMods", "ConcernedCartographer");

    private string Data => Path.Combine(_profile, "data", "ConcernedCatMods", "ConcernedCartographer");

    /// <summary>The BepInEx settings file itself, a level up from the product
    /// folder. It is genuine configuration, it is what a mod manager is
    /// actually for, and nothing here may go near it.</summary>
    private string SettingsFile =>
        Path.Combine(_profile, "config", "TheConcernedCat.ConcernedCartographer.cfg");

    public DataRelocationTests() => Directory.CreateDirectory(_profile);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_profile, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not a test failure, and the exception
            // a locked or read-only file throws is not only IOException.
        }
    }

    // ---- the profile every beta user is sitting on right now ----------------

    /// <summary>What a 1.2.2 installation looks like: everything in the
    /// settings folder, markers already under <c>state</c>, an atlas, backups,
    /// a companion, and survey rules the player has edited.</summary>
    private void GiveThemABetaInstall()
    {
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Path.Combine(Config, "state"));
        Directory.CreateDirectory(Path.Combine(Config, "backups", "1234567890-20260101-000000-backup"));

        Write(SettingsFile, "[Companions]\nDoorAccess = AllDoors\n");

        // Configuration: stays.
        Write(Path.Combine(Config, "survey-rules.tsv"), EditedRules);
        Write(Path.Combine(Config, "cartographer-strings.tsv"), "cc_pin\tMitt pin\n");
        Write(Path.Combine(Config, "cartographer-strings-template.tsv"), "cc_pin\tPin\n");

        // Their own bookkeeping: moves, invisibly.
        Write(Path.Combine(Config, "state", "author-id.dat"), Identity);
        Write(Path.Combine(Config, "state", "onboarding-shown.dat"), OnboardedAt);

        // Their work: moves.
        Write(Path.Combine(Config, WorldUid + ".pins.tsv"), "pins for a year of playing\n");
        Write(Path.Combine(Config, WorldUid + ".roads.tsv"), "roads\n");
        Write(Path.Combine(Config, WorldUid + ".routes-atlas.tsv"), "routes\n");
        Write(Path.Combine(Config, "views.tsv"), "a saved view\n");
        Write(Path.Combine(Config, "thor.companions.tsv"), "hulgi\n");
        Write(Path.Combine(Config, "doors-" + WorldUid + ".tsv"), "a door he may use\n");
        Write(
            Path.Combine(Config, "backups", "1234567890-20260101-000000-backup", WorldUid + ".pins.tsv"),
            "pins for a year of playing\n");
    }

    private static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private DataRelocation.Outcome Relocate() =>
        DataRelocation.Run(Config, Data, CartographerConfigFiles.IsConfiguration, _log.Add);

    private string? ResolveIdentity() =>
        Resolve("author-id.dat", AuthorPriorNames, IsGuid);

    private string? ResolveOnboarding() =>
        Resolve("onboarding-shown.dat", OnboardingPriorNames, text => !string.IsNullOrWhiteSpace(text));

    /// <summary>Exactly what <c>AuthorIdentity</c> does: look in the data
    /// folder, with the settings folder named as the place this marker set used
    /// to live.</summary>
    private string? Resolve(string name, string[] priorNames, Func<string, bool> usable)
    {
        MarkerFile.Resolve(
            Data, name, priorNames, new[] { Config }, usable, _log.Add, out _, out string? contents);
        return contents;
    }

    private static bool IsGuid(string text) => Guid.TryParseExact(text.Trim(), "N", out _);

    private LegacyEvidence Unlock() =>
        LegacyEvidenceRule.Evaluate(
            CartographerEvidenceScan.Gather(new[] { Data, Config }, WorldUid, DateTime.UtcNow));

    // ---- A. an existing install loses nothing -------------------------------

    [Fact]
    public void AnInstallFromTheBetaKeepsItsIdentity()
    {
        GiveThemABetaInstall();
        Relocate();

        Assert.Equal(Identity, ResolveIdentity());
        Assert.Equal(Identity, File.ReadAllText(Path.Combine(Data, "state", "author-id.dat")));
        Assert.False(File.Exists(Path.Combine(Config, "state", "author-id.dat")));
    }

    [Fact]
    public void AnIdentityIsStillFoundWhenTheMoveCouldNotHappenAtAll()
    {
        // The case that decides whether this migration is safe to ship. A
        // locked file or a read-only profile means the marker is still in the
        // settings folder; looking only where it now belongs would find
        // nothing, mint a second GUID, and silently make the player somebody
        // else — their own pins would stop being theirs to delete.
        GiveThemABetaInstall();

        Assert.Equal(Identity, ResolveIdentity());
        Assert.Equal(Identity, File.ReadAllText(Path.Combine(Data, "state", "author-id.dat")));
    }

    [Fact]
    public void AnInstallFromTheBetaKeepsTheSurveyRulesItEdited()
    {
        GiveThemABetaInstall();
        Relocate();

        // Byte for byte, and still where the player left it. #385 landed a
        // whole mechanism to keep an edited rules file from being trampled;
        // moving it out from under that mechanism would undo it.
        Assert.Equal(EditedRules, File.ReadAllText(Path.Combine(Config, "survey-rules.tsv")));
        Assert.False(File.Exists(Path.Combine(Data, "survey-rules.tsv")));
    }

    [Fact]
    public void AnInstallFromTheBetaKeepsItsAtlas()
    {
        GiveThemABetaInstall();
        Relocate();

        foreach (string name in new[]
                 {
                     WorldUid + ".pins.tsv",
                     WorldUid + ".roads.tsv",
                     WorldUid + ".routes-atlas.tsv",
                     "views.tsv",
                     "thor.companions.tsv",
                     "doors-" + WorldUid + ".tsv",
                 })
        {
            Assert.True(File.Exists(Path.Combine(Data, name)), name + " did not arrive");
            Assert.False(File.Exists(Path.Combine(Config, name)), name + " was left behind");
        }

        Assert.Equal(
            "pins for a year of playing\n",
            File.ReadAllText(Path.Combine(Data, WorldUid + ".pins.tsv")));

        // Backups are the player's own safety net and are nested, so the move
        // has to descend rather than skim the top of the folder.
        Assert.Equal(
            "pins for a year of playing\n",
            File.ReadAllText(Path.Combine(
                Data, "backups", "1234567890-20260101-000000-backup", WorldUid + ".pins.tsv")));
    }

    [Fact]
    public void AnInstallFromTheBetaKeepsThatItHasAlreadyBeenIntroduced()
    {
        GiveThemABetaInstall();
        Relocate();

        Assert.Equal(OnboardedAt, ResolveOnboarding());
        Assert.False(File.Exists(Path.Combine(Config, "state", "onboarding-shown.dat")));
    }

    [Fact]
    public void AYearOldPlayerIsStillAYearOldPlayerBeforeAndAfterTheMove()
    {
        // The probe reads both directories. Reading only the new one would
        // classify everybody in the beta as a new player on the first start of
        // this build, and LegacyEvidence.None does not unlock - it takes the
        // toolbar away. That is #343's failure with a different cause.
        GiveThemABetaInstall();
        Assert.Equal(LegacyEvidence.Present, Unlock());

        Relocate();
        Assert.Equal(LegacyEvidence.Present, Unlock());
    }

    // ---- B. running it again changes nothing --------------------------------

    [Fact]
    public void TheSecondStartFindsNothingLeftToDo()
    {
        GiveThemABetaInstall();

        DataRelocation.Outcome first = Relocate();
        Assert.False(first.NothingToDo);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, first.Kept);

        Dictionary<string, string> after = Snapshot(Data);

        DataRelocation.Outcome second = Relocate();
        Assert.True(second.NothingToDo);

        DataRelocation.Outcome third = Relocate();
        Assert.True(third.NothingToDo);

        // Not "it did not crash": nothing moved, nothing was duplicated, and
        // nothing was rewritten.
        Assert.Equal(after, Snapshot(Data));
    }

    [Fact]
    public void AMoveThatWasInterruptedHalfwayIsFinishedRatherThanRedone()
    {
        // The start before this one copied the file and did not get to remove
        // the original: the same bytes are in both places. Deleting the
        // leftover is safe, and is the only way the settings folder ever comes
        // clean.
        GiveThemABetaInstall();
        Write(Path.Combine(Data, "views.tsv"), "a saved view\n");

        DataRelocation.Outcome outcome = Relocate();

        Assert.False(File.Exists(Path.Combine(Config, "views.tsv")));
        Assert.Equal("a saved view\n", File.ReadAllText(Path.Combine(Data, "views.tsv")));
        Assert.True(outcome.Finished >= 1);
    }

    [Fact]
    public void AHalfWrittenCopyFromAnInterruptedStartIsNotMistakenForTheFile()
    {
        GiveThemABetaInstall();
        Write(Path.Combine(Data, "views.tsv" + DataRelocation.StagingSuffix), "half a saved");

        Relocate();

        Assert.Equal("a saved view\n", File.ReadAllText(Path.Combine(Data, "views.tsv")));
        Assert.False(File.Exists(Path.Combine(Data, "views.tsv" + DataRelocation.StagingSuffix)));
    }

    [Fact]
    public void TwoCopiesThatDisagreeAreBothKept()
    {
        // Somebody's atlas. The reasoning that says the new one must be the
        // live one is probably right, and "probably" is not a reason to delete
        // the other one.
        GiveThemABetaInstall();
        Write(Path.Combine(Data, WorldUid + ".pins.tsv"), "a different set of pins\n");

        DataRelocation.Outcome outcome = Relocate();

        Assert.Equal(1, outcome.Kept);
        Assert.Equal(
            "pins for a year of playing\n",
            File.ReadAllText(Path.Combine(Config, WorldUid + ".pins.tsv")));
        Assert.Equal(
            "a different set of pins\n",
            File.ReadAllText(Path.Combine(Data, WorldUid + ".pins.tsv")));
        Assert.Contains(_log, line => line.Contains("differ"));
    }

    // ---- C. a fresh install only ever uses the new layout -------------------

    [Fact]
    public void AFreshInstallHasNothingToMoveAndNoSettingsFolderIsInvented()
    {
        DataRelocation.Outcome outcome = Relocate();

        Assert.True(outcome.NothingToDo);
        Assert.False(Directory.Exists(Config));
        Assert.False(Directory.Exists(Data));
        Assert.Empty(_log);
        Assert.Equal(LegacyEvidence.None, Unlock());
    }

    [Fact]
    public void NothingAFreshInstallWritesAsDataCountsAsSomethingAPlayerEdits()
    {
        // The settings folder holds what this list says and nothing else, so
        // this list is the whole of what a fresh install may put there. A data
        // name on it is #304 coming back one file at a time - and the validator
        // holds the list and the CartographerPaths.InConfig call sites to each
        // other, so the two cannot drift apart.
        foreach (string name in new[]
                 {
                     WorldUid + ".pins.tsv",
                     WorldUid + ".roads.tsv",
                     WorldUid + ".routes-atlas.tsv",
                     WorldUid + ".survey-rejected.tsv",
                     WorldUid + ".terrain-intent.tsv",
                     "views.tsv",
                     "support-report.txt",
                     "author-id.dat",
                     "author-id.txt",
                     "onboarding-shown.dat",
                     "thor.companions.tsv",
                     "doors-" + WorldUid + ".tsv",
                 })
        {
            Assert.False(
                CartographerConfigFiles.IsConfiguration(name),
                name + " is not something a player edits and must not sit in the settings folder");
        }

        Assert.True(CartographerConfigFiles.IsConfiguration("survey-rules.tsv"));
        Assert.True(CartographerConfigFiles.IsConfiguration("cartographer-strings.tsv"));
        Assert.True(CartographerConfigFiles.IsConfiguration("cartographer-strings-template.tsv"));
    }

    // ---- D. real configuration is not touched -------------------------------

    [Fact]
    public void EverythingAPlayerActuallyEditsIsExactlyWhereTheyLeftIt()
    {
        GiveThemABetaInstall();
        Dictionary<string, string> before = Snapshot(Config);

        Relocate();

        // The three files a person opens, unchanged and still in the settings
        // folder - and the settings folder itself still there to hold them.
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["survey-rules.tsv"] = EditedRules,
                ["cartographer-strings.tsv"] = "cc_pin\tMitt pin\n",
                ["cartographer-strings-template.tsv"] = "cc_pin\tPin\n",
            },
            Snapshot(Config));

        foreach (string name in new[]
                 {
                     "survey-rules.tsv", "cartographer-strings.tsv", "cartographer-strings-template.tsv",
                 })
        {
            Assert.Equal(before[name], File.ReadAllText(Path.Combine(Config, name)));
        }

        // BepInEx's own settings file is a level up, and a relocation that
        // wandered out of its own folder would be a far worse bug than the one
        // it is fixing.
        Assert.Equal("[Companions]\nDoorAccess = AllDoors\n", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void TheEmptiedFoldersGoButTheSettingsFolderStays()
    {
        GiveThemABetaInstall();
        Relocate();

        Assert.True(Directory.Exists(Config));
        Assert.False(Directory.Exists(Path.Combine(Config, "state")));
        Assert.False(Directory.Exists(Path.Combine(Config, "backups")));
    }

    // ---- refusing to run at all ---------------------------------------------

    [Fact]
    public void TheCheckAndTheLogAreRequiredRatherThanDefaultingToSomethingUnsafe()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DataRelocation.Run(Config, Data, null!, _log.Add));
        Assert.Throws<ArgumentNullException>(() =>
            DataRelocation.Run(Config, Data, CartographerConfigFiles.IsConfiguration, null!));
        Assert.Throws<ArgumentException>(() =>
            DataRelocation.Run(string.Empty, Data, CartographerConfigFiles.IsConfiguration, _log.Add));
        Assert.Throws<ArgumentException>(() =>
            DataRelocation.Run(Config, string.Empty, CartographerConfigFiles.IsConfiguration, _log.Add));
    }

    /// <summary>Every file under a directory, by relative path, with its
    /// contents. Contents, not timestamps: the question is whether anything
    /// changed, and a rewrite with the same bytes is still a rewrite this
    /// migration has no business doing.</summary>
    private static Dictionary<string, string> Snapshot(string directory)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return found;
        }

        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            found[path.Substring(directory.Length).TrimStart(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)] = File.ReadAllText(path);
        }

        return found;
    }
}
