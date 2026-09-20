using System.IO;
using BepInEx;

namespace TheConcernedCat.ConcernedCartographer;

/// <summary>Every path this product writes to, composed in one place.
///
/// <b>Why one place.</b> <c>CartographerLegacyProbe</c> decides whether somebody
/// is a new player or a returning one by listing <see cref="Root"/> with
/// <c>Directory.GetFiles</c> and asking whether everything in it is a name this
/// build writes for itself. The signal is therefore a property of <i>which
/// directory a file lands in</i> — and the directory was composed by eleven
/// separate literal <c>Path.Combine(Paths.ConfigPath, "ConcernedCatMods",
/// "ConcernedCartographer")</c> expressions scattered across the persistence and
/// runtime layers, so nothing connected "I am adding a file here" to "I am
/// changing who counts as a returning player".
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
/// <b>It does not remove the list, and saying otherwise would be the same
/// mistake in prose.</b> Some files this build writes for itself have to stay
/// findable — <c>cartographer-strings-template.tsv</c> exists precisely so a
/// translator can copy it — so they sit in <see cref="Root"/> and the probe still
/// has to recognise them by name through <c>CartographerFirstRunFiles</c>. What
/// <see cref="State"/> buys is that the files with no reason to be visible cannot
/// be forgotten on that list, and the validator check below closes the rest.
///
/// <b>What must stay in <see cref="Root"/>.</b> Everything a player edited or
/// caused. The probe's own evidence lists are the authority — five world
/// sidecars (<c>.pins.tsv</c>, <c>.roads.tsv</c>, <c>.routes-atlas.tsv</c>,
/// <c>.survey-rejected.tsv</c>, <c>.terrain-intent.tsv</c>), plus
/// <c>views.tsv</c>, <c>cartographer-strings.tsv</c>, <c>support-report.log</c>
/// (and the <c>.txt</c> an older build wrote) and the pre-existing
/// <c>survey-rules.tsv</c>. Hiding any of them turns a
/// returning player into a new one, which takes their toolbar away, because
/// <c>LegacyEvidence.None</c> does not unlock. The two directions are not
/// symmetric and this one is worse.
///
/// <c>doors-&lt;world&gt;.tsv</c> and the companion sidecars are also written
/// into <see cref="Root"/>, by the shared companion sources, which cannot call
/// this type — the shared layer stays BepInEx-free. The companion sidecars
/// reach the probe through <c>CartographerFirstRunFiles</c>'s
/// <c>.companions.tsv</c> suffixes; <c>doors-&lt;world&gt;.tsv</c> reaches no
/// list at all and lands in the probe's listing as an unrecognised name, which
/// grants through the weak "somebody was here" signal. That is the harmless
/// direction and it is pre-existing, so it is left alone here rather than
/// changed in passing — but it is not what an earlier version of this comment
/// said, which was that it reaches the probe.
///
/// <b>Why everything stays under <c>BepInEx/config</c>.</b> Not habit, and not
/// because a data file belongs among settings. Read from the installed
/// Thunderstore Mod Manager bundle (1.124.2, <c>APP_NAME="r2modman"</c>, core
/// 3.2.18): a profile export archives <c>BepInEx/config</c> <b>wholesale and
/// unfiltered</b> through its own <c>addFolder("config", …)</c> special case,
/// and every other folder has to pass an extension whitelist
/// (<c>SUPPORTED_CONFIG_FILE_EXTENSIONS</c>: <c>.cfg .txt .json .yml .yaml
/// .ini</c>). <c>.tsv</c>, <c>.bak</c> and <c>.dat</c> are on neither list, so a
/// sibling folder such as <c>BepInEx/data</c> would silently drop this
/// product's entire atlas, its backups and its author identity from the
/// player's own backup — outbound only, with no warning, and import is
/// permissive enough that hand-testing a zip would not show it.
/// <c>BepInEx/data</c> is not a BepInEx convention either: the installed
/// 5.4.23.5 exposes no such path.
///
/// <b>And the config editor was never about the folder.</b> Same bundle: the
/// editor is rooted at the <i>whole profile</i>, excluding only <c>dotnet</c>,
/// <c>_state</c> and a plugin's <c>manifest.json</c>, and then filters by that
/// same extension list. It descends into anything. It has never listed a
/// <c>.tsv</c>. Quandru saw <c>author-id.txt</c> because <c>.txt</c> is on the
/// list — which is why the fix is extensions, enforced by the validator rule
/// below, and not geography. <b>Gale itself is an independent implementation
/// and has not been assessed at all.</b>
///
/// <b>What the validator actually enforces.</b> Three things: that this file is
/// the only place composing <c>Paths.ConfigPath</c>; that every name handed to
/// <see cref="InRoot"/> is one the probe knows — the first version of that
/// check enforced only the token, so moving a marker back into the probed root
/// passed green: one token, and #343's exact shape; and that no name handed to
/// <see cref="InRoot"/> has an extension a configuration editor opens unless
/// <c>CartographerConfigFiles</c> says a player edits it.
/// </summary>
internal static class CartographerPaths
{
    private const string Vendor = "ConcernedCatMods";

    private const string Product = "ConcernedCartographer";

    /// <summary>The product's data directory: the one the fresh-install probe
    /// lists, and therefore the one only player-evidence files belong in.
    /// </summary>
    public static string Root => Path.Combine(Paths.ConfigPath, Vendor, Product);

    /// <summary>The mod's own bookkeeping, in a subfolder the probe's
    /// <c>Directory.GetFiles</c> cannot see. Named for what is in it: state the
    /// mod keeps, not settings a person sets.</summary>
    public static string State => Path.Combine(Root, Storage.MarkerFile.FolderName);

    /// <summary>A file in <see cref="Root"/>. For anything a player edited or
    /// caused.
    ///
    /// Every literal name passed here is checked against the probe's own lists by
    /// the validator, which is why a <b>directory</b> gets its own member instead
    /// of going through this: <c>Directory.GetFiles</c> does not list directories,
    /// so a folder in the root is invisible to the probe and is not a name it
    /// needs to know.</summary>
    public static string InRoot(string name) => Path.Combine(Root, name);

    /// <summary>The backup folder. A directory, and beside <see cref="Root"/>
    /// rather than under <see cref="State"/> because a player is meant to be able
    /// to find their own backups.</summary>
    public static string Backups => Path.Combine(Root, "backups");

    /// <summary>A file in <see cref="State"/>. For anything the mod wrote for
    /// itself.</summary>
    public static string InState(string name) => Path.Combine(State, name);
}
