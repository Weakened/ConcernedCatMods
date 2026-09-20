using System.IO;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>#304: a mod manager offered Quandru <c>author-id.txt</c> among the
/// files to edit. It is a generated GUID the atlas keys ownership on.
///
/// <b>The cause is the extension, not the folder.</b> Read from the installed
/// Thunderstore Mod Manager bundle (1.124.2, <c>APP_NAME="r2modman"</c>, core
/// 3.2.18): the configuration editor is rooted at the whole profile, excluding
/// only <c>dotnet</c>, <c>_state</c> and a plugin's <c>manifest.json</c>, and
/// then filters by <c>SUPPORTED_CONFIG_FILE_EXTENSIONS</c>. It descends into
/// every subfolder. So moving a file cannot hide it and renaming one can, which
/// is the opposite of what this product's comments said for two releases.
///
/// These tests pin that measurement in the two places it decides something:
/// what the list is, and which of this product's files are on the wrong side of
/// it. The rule itself — no name written into the product's directory has an
/// editor-opened extension unless a player really does edit it — is enforced
/// for every call site by <c>validate_repo.py</c>, because only the validator
/// can see the call sites.
///
/// <b>Gale has not been assessed.</b> It is an independent Rust implementation,
/// it is not installed here, and nothing below was measured from it.</summary>
public sealed class ConfigEditorVisibilityTests
{
    [Fact]
    public void TheListIsTheOneThatWasReadFromTheBundle()
    {
        // Six, exactly, and in the bundle's own order. A seventh added on a
        // hunch would start hiding files that are fine; one dropped would stop
        // the rule catching the next author-id.txt.
        Assert.Equal(
            new[] { ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini" },
            CartographerConfigFiles.ExtensionsAnEditorOpens);
    }

    [Fact]
    public void TheSupportReportIsNoLongerOfferedAsASetting()
    {
        // The last file of ours on the wrong side of that list. It is a
        // generated, sanitized diagnostic - nothing in it is a setting - and it
        // was sitting in the editor beside the real ones.
        Assert.True(CartographerConfigFiles.AnEditorWouldOfferThis("support-report.txt"));
        Assert.False(CartographerConfigFiles.AnEditorWouldOfferThis("support-report.log"));
    }

    [Fact]
    public void TheSupportReportIsNotHiddenTheWayAMarkerIs()
    {
        // The report is `.log`, not `.dat`: it is the one file we ask people to
        // find and send us, so hiding it from a human to hide it from an editor
        // would trade one problem for a worse one. This says the two are not
        // allowed to converge — setting MarkerFile.Extension to ".log" would
        // make every marker look like something to open and send.
        //
        // An earlier version of this test asserted EndsWith(".log",
        // "support-report.log"), which is a literal against a literal and could
        // not fail. The product's own name stays a literal at its
        // CartographerPaths.InRoot call site deliberately: that literal is what
        // the validator's rule reads, and hoisting it into a shared constant
        // would hide the call from the rule that keeps #304 from returning.
        Assert.NotEqual(".log", MarkerFile.Extension);
        Assert.False(CartographerConfigFiles.AnEditorWouldOfferThis("x" + MarkerFile.Extension));
    }

    [Fact]
    public void TheMarkerExtensionIsWhatKeepsAMarkerOutOfTheEditor()
    {
        Assert.True(CartographerConfigFiles.AnEditorWouldOfferThis("author-id.txt"));
        Assert.False(CartographerConfigFiles.AnEditorWouldOfferThis("author-id" + MarkerFile.Extension));
        Assert.False(
            CartographerConfigFiles.AnEditorWouldOfferThis("onboarding-shown" + MarkerFile.Extension));
    }

    [Fact]
    public void PuttingAFileInASubfolderDoesNotHideItFromTheEditor()
    {
        // The claim this product shipped twice, and it is false. The editor
        // walks the whole profile, so `state/` is no more hidden from it than
        // the folder above. Had the markers only moved and kept `.txt`, #304
        // would have been reported again against the same two files.
        Assert.True(CartographerConfigFiles.AnEditorWouldOfferThis(
            Path.Combine(MarkerFile.FolderName, "author-id.txt")));
    }

    [Fact]
    public void TheAtlasWasNeverInTheEditorAtAll()
    {
        // Why relocating the atlas out of `BepInEx/config` would have cost the
        // player their profile export and bought nothing: none of this was ever
        // listed. `.tsv` is on no editor list, and `BepInEx/config` is the one
        // folder a profile export copies wholesale and unfiltered.
        foreach (string name in new[]
                 {
                     "1234567890.pins.tsv",
                     "1234567890.roads.tsv",
                     "1234567890.routes-atlas.tsv",
                     "1234567890.survey-rejected.tsv",
                     "1234567890.terrain-intent.tsv",
                     "views.tsv",
                     "thor.companions.tsv",
                     "doors-1234567890.tsv",
                     "1234567890.roads.tsv.v1.bak",
                 })
        {
            Assert.False(
                CartographerConfigFiles.AnEditorWouldOfferThis(name),
                name + " is not something a configuration editor has ever listed");
        }
    }

    [Fact]
    public void TheFilesAPlayerReallyDoesEditAreTheOnesOnTheList()
    {
        Assert.True(CartographerConfigFiles.IsConfiguration("survey-rules.tsv"));
        Assert.True(CartographerConfigFiles.IsConfiguration("cartographer-strings.tsv"));
        Assert.True(CartographerConfigFiles.IsConfiguration("cartographer-strings-template.tsv"));

        // Generated by the mod, every one of them. If any ever acquires an
        // editor-opened extension, the validator refuses the build rather than
        // waiting for a second Quandru.
        foreach (string name in new[]
                 {
                     "author-id.txt",
                     "author-id.dat",
                     "onboarding-shown.dat",
                     "support-report.txt",
                     "support-report.log",
                     "views.tsv",
                     "1234567890.pins.tsv",
                 })
        {
            Assert.False(CartographerConfigFiles.IsConfiguration(name), name);
        }
    }
}
