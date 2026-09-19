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
/// <c>views.tsv</c>, <c>cartographer-strings.tsv</c>, <c>support-report.txt</c>
/// and the pre-existing <c>survey-rules.tsv</c>. Hiding any of them turns a
/// returning player into a new one, which takes their toolbar away, because
/// <c>LegacyEvidence.None</c> does not unlock. The two directions are not
/// symmetric and this one is worse.
///
/// <c>doors-&lt;world&gt;.tsv</c> and the companion sidecars are also written
/// into <see cref="Root"/>, by the shared companion sources, which cannot call
/// this type — the shared layer stays BepInEx-free. They reach the probe through
/// <c>CartographerFirstRunFiles</c> instead, which is why the validator audits
/// those files for the name rule even though they cannot obey the owner rule.
///
/// <b>What the validator actually enforces.</b> Two things: that this file is the
/// only place composing <c>Paths.ConfigPath</c>, and — the one that matters —
/// that every name handed to <see cref="InRoot"/> is one the probe knows. The
/// first version of that check enforced only the token, so moving a marker back
/// into the probed root passed green: one token, and #343's exact shape.
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
