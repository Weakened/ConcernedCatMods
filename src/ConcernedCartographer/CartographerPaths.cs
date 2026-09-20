using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedCartographer;

/// <summary>Every path this product writes to, composed in one place.
///
/// <b>Two directories, and the difference is the point (#304).</b> A mod
/// manager presents everything under <c>BepInEx/config</c> as the mod's
/// settings, because that is what that folder means. Quandru reported seeing
/// <c>author-id.txt</c> offered for editing there; it was never a setting, and
/// neither is an atlas, a saved view or a support report. So:
///
/// <list type="bullet">
/// <item><see cref="Config"/> holds <b>only</b> what a person is meant to open
/// and change: <c>survey-rules.tsv</c>, a translator's
/// <c>cartographer-strings.tsv</c> and the template it is copied from. The
/// names are listed once, in <c>CartographerConfigFiles</c>, and the validator
/// holds this file and that list to each other.</item>
/// <item><see cref="Data"/> holds everything else — the atlas sidecars, views,
/// backups, companion sidecars, the support report, and this build's own
/// bookkeeping under <see cref="State"/>. It is a sibling of the config folder
/// rather than a child of it, so a settings editor has nothing of ours to
/// offer.</item>
/// </list>
///
/// Both are under the BepInEx root, so both stay scoped to one mod-manager
/// profile exactly as before.
///
/// <b>Why one place.</b> <c>CartographerLegacyProbe</c> decides whether
/// somebody is a new player or a returning one by listing these directories
/// with <c>Directory.GetFiles</c> and asking whether everything in them is a
/// name this build writes for itself. The signal is therefore a property of
/// <i>which directory a file lands in</i> — and the directory was once composed
/// by eleven separate literal <c>Path.Combine(Paths.ConfigPath,
/// "ConcernedCatMods", "ConcernedCartographer")</c> expressions scattered across
/// the persistence and runtime layers, so nothing connected "I am adding a file
/// here" to "I am changing who counts as a returning player".
///
/// That disconnection has cost twice (#363). <c>survey-rules.tsv</c> was the
/// first: the plugin wrote its own starter rules at startup, the probe read them
/// as a deliberate past action, and every fresh installation looked like a
/// returning player. <c>author-id.dat</c> was the second (#343), and it reached
/// <c>main</c>: a rename in place made every brand-new player a returning one,
/// and because unlock is monotonic the #264 introduction could then never run
/// again on that profile.
///
/// <b>What <see cref="State"/> is for.</b> <c>Directory.GetFiles</c> does not
/// descend, so anything under <see cref="State"/> is invisible to that listing by
/// construction. That is where a marker belongs: something the mod writes for
/// itself that a player should never see or edit.
///
/// <b>The probe still reads both directories, and that is not optional.</b>
/// A profile whose relocation has not run, or could not finish, still has its
/// whole history in <see cref="Config"/>. Hiding it there would turn a player
/// who has been mapping for a year into a new one, which takes their toolbar
/// away, because <c>LegacyEvidence.None</c> does not unlock. The two directions
/// are not symmetric and this one is worse — so every name written into
/// <i>either</i> directory has to be one the probe knows.
///
/// <c>doors-&lt;world&gt;.tsv</c> and the companion sidecars are also written
/// into <see cref="Data"/>, by the shared companion sources, which cannot call
/// this type — the shared layer stays BepInEx-free. They reach the probe through
/// <c>CartographerFirstRunFiles</c> instead, which is why the validator audits
/// those files for the name rule even though they cannot obey the owner rule.
///
/// <b>What the validator actually enforces.</b> Three things: that this file is
/// the only place composing <c>Paths.ConfigPath</c> or
/// <c>Paths.BepInExRootPath</c>; that every name handed to <see cref="InData"/>
/// or <see cref="InConfig"/> is one the probe knows — the one that matters, and
/// the first version of that check enforced only the token, so moving a marker
/// back into the probed root passed green: one token, and #343's exact shape;
/// and that <see cref="InConfig"/> is used for exactly the names
/// <c>CartographerConfigFiles</c> calls configuration, so #304 cannot quietly
/// come back one file at a time.
/// </summary>
internal static class CartographerPaths
{
    private const string Vendor = "ConcernedCatMods";

    private const string Product = "ConcernedCartographer";

    /// <summary>The folder non-configuration mod data goes in, beside
    /// <c>config</c> and <c>plugins</c> rather than inside any of them.</summary>
    private const string DataFolderName = "data";

    /// <summary>The product's <b>settings</b> directory: what a mod manager
    /// shows a player, and therefore only files
    /// <c>CartographerConfigFiles</c> says a player is meant to edit.</summary>
    public static string Config => Path.Combine(Paths.ConfigPath, Vendor, Product);

    /// <summary>The product's <b>data</b> directory: everything the mod keeps
    /// or accumulates that is not a setting.</summary>
    public static string Data =>
        Path.Combine(Paths.BepInExRootPath, DataFolderName, Vendor, Product);

    /// <summary>The mod's own bookkeeping, in a subfolder the probe's
    /// <c>Directory.GetFiles</c> cannot see. Named for what is in it: state the
    /// mod keeps, not settings a person sets.</summary>
    public static string State => Path.Combine(Data, Storage.MarkerFile.FolderName);

    /// <summary>A file in <see cref="Config"/>. For the few things a player
    /// opens and edits, and nothing else.</summary>
    public static string InConfig(string name) => Path.Combine(Config, name);

    /// <summary>A file in <see cref="Data"/>. For anything a player caused but
    /// does not edit by hand, and anything this build writes for itself that
    /// still has to be findable.
    ///
    /// Every literal name passed here is checked against the probe's own lists by
    /// the validator, which is why a <b>directory</b> gets its own member instead
    /// of going through this: <c>Directory.GetFiles</c> does not list directories,
    /// so a folder in the root is invisible to the probe and is not a name it
    /// needs to know.</summary>
    public static string InData(string name) => Path.Combine(Data, name);

    /// <summary>The backup folder. A directory, and beside the sidecars rather
    /// than under <see cref="State"/> because a player is meant to be able to
    /// find their own backups.</summary>
    public static string Backups => Path.Combine(Data, "backups");

    /// <summary>A file in <see cref="State"/>. For anything the mod wrote for
    /// itself.</summary>
    public static string InState(string name) => Path.Combine(State, name);
}
