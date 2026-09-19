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
/// construction. A file the mod writes for <i>itself</i> — nothing a player did,
/// nothing a player should edit — belongs there, and then the probe's question
/// answers itself correctly without any list to remember.
///
/// <b>What must stay in <see cref="Root"/>.</b> Everything a player edited or
/// caused: <c>survey-rules.tsv</c>, <c>cartographer-strings.tsv</c>,
/// <c>views.tsv</c>, the per-world pin, road, route and terrain sidecars,
/// <c>doors-&lt;world&gt;.tsv</c>, <c>support-report.txt</c>. Those files
/// <i>are</i> the evidence the probe is looking for, and hiding them would turn a
/// returning player into a new one — which takes their toolbar away, because
/// <c>LegacyEvidence.None</c> does not unlock. The two directions are not
/// symmetric and this one is worse.
///
/// A validator check enforces that this file is the only place in the product
/// that composes <c>Paths.ConfigPath</c>, so the invariant is structural rather
/// than remembered.</summary>
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
    /// caused.</summary>
    public static string InRoot(string name) => Path.Combine(Root, name);

    /// <summary>A file in <see cref="State"/>. For anything the mod wrote for
    /// itself.</summary>
    public static string InState(string name) => Path.Combine(State, name);
}
