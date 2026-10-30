using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Whether a dying Gunnar's carried inventory goes on the ground
/// (`DECISIONS.md` D16). Zero is never a grant.</summary>
internal enum DeathDropVerdict
{
    /// <summary>Nobody decided. Never a drop.</summary>
    Unspecified = 0,

    /// <summary>Every carried stack goes through vanilla's own drop, one at a
    /// time, measured.</summary>
    Drop = 1,

    /// <summary>The call did not come from the body's own genuine death. D16
    /// covers exactly one lifecycle event; a retirement, a cancellation, a
    /// despawn, a zone unload, a logout, a world change and a plugin teardown
    /// are each not it, and none of them may reach the drop.</summary>
    NotADeath = 2,

    /// <summary>A cart's joint still holds the body after D4's own teardown ran.
    /// D16 orders the detach <b>first</b> and the drop only after it, so the
    /// drop does not run: an interrupted death drop fails closed and keeps its
    /// evidence.</summary>
    CartStillHeld = 3,

    /// <summary>Teamster's master switch or the collection switch is off (or
    /// unreadable). The drop belongs to the opted-in collection runtime, the
    /// only thing that can have put anything in him.</summary>
    NotOptedIn = 4,

    /// <summary>The shared work-authority rule did not grant: not the host, a
    /// dedicated server, or somebody else connected (`DECISIONS.md` D3).
    /// </summary>
    NoAuthority = 5,

    /// <summary>The body never read its own stored inventory, so what is live in
    /// it is not what it holds. Dropping it would be dropping an amount nobody
    /// established; what the stored package held is recorded as unresolved.
    /// </summary>
    InventoryUnreadable = 6,

    /// <summary>The start-up probe could not verify every game member the drop
    /// binds, so none of them is called.</summary>
    SeamUnavailable = 7,
}

/// <summary>What the runtime established at the moment of a death, before
/// anything was dropped. A defaulted instance refuses on its first clause.
/// </summary>
internal readonly struct DeathDropFacts
{
    public DeathDropFacts(
        bool genuineDeath,
        bool cartReleased,
        bool optedIn,
        WorkAuthorityVerdict authority,
        bool capabilityVerified,
        bool inventoryLoaded)
    {
        CapabilityVerified = capabilityVerified;
        GenuineDeath = genuineDeath;
        CartReleased = cartReleased;
        OptedIn = optedIn;
        Authority = authority;
        InventoryLoaded = inventoryLoaded;
    }

    /// <summary>Vanilla's own death path is running on this body and the body
    /// itself answers that it is dead. Both, because the first is where the call
    /// comes from and the second is the body's own word for it.</summary>
    public bool GenuineDeath { get; }

    /// <summary>No cart's joint holds the body any more, after D4's teardown was
    /// given the chance to run.</summary>
    public bool CartReleased { get; }

    public bool OptedIn { get; }

    public WorkAuthorityVerdict Authority { get; }

    /// <summary>The start-up probe verified every member the drop binds.</summary>
    public bool CapabilityVerified { get; }

    /// <summary>The body's inventory record read its stored package. False for
    /// an inert body.</summary>
    public bool InventoryLoaded { get; }
}

/// <summary>D16, decided over values so every clause can be driven without the
/// game. The adapter that asks binds Unity and no test here can load it, which is
/// the same reason the pick's and the deposit's gates live in this layer.
/// </summary>
internal static class WorkerDeathDrop
{
    /// <summary>The first failure is the verdict. <b>Not a death is asked
    /// first</b>, because it is the clause the whole decision rests on: nothing
    /// that is not the body's own death reaches the drop, whatever else is true.
    /// The detach is asked second because D16 orders it before the drop.
    /// </summary>
    public static DeathDropVerdict Decide(in DeathDropFacts facts)
    {
        if (!facts.GenuineDeath)
        {
            return DeathDropVerdict.NotADeath;
        }

        if (!facts.CartReleased)
        {
            return DeathDropVerdict.CartStillHeld;
        }

        if (!facts.OptedIn)
        {
            return DeathDropVerdict.NotOptedIn;
        }

        if (facts.Authority != WorkAuthorityVerdict.Granted)
        {
            return DeathDropVerdict.NoAuthority;
        }

        if (!facts.CapabilityVerified)
        {
            return DeathDropVerdict.SeamUnavailable;
        }

        return facts.InventoryLoaded ? DeathDropVerdict.Drop : DeathDropVerdict.InventoryUnreadable;
    }

    /// <summary>What the verdict means, for the log and the durable record.
    /// Every value has a sentence of its own.</summary>
    public static string Describe(DeathDropVerdict verdict)
    {
        switch (verdict)
        {
            case DeathDropVerdict.Drop:
                return "what he carried went on the ground through the game's own drop";
            case DeathDropVerdict.NotADeath:
                return "this was not his own death, so nothing was dropped";
            case DeathDropVerdict.CartStillHeld:
                return "a cart was still hitched to him after it was told to let go, and the drop only " +
                    "runs once the cart is free, so nothing was dropped and what he carried is unresolved";
            case DeathDropVerdict.NotOptedIn:
                return "Gunnar's collection is off, so nothing was dropped and what he carried is unresolved";
            case DeathDropVerdict.NoAuthority:
                return "this game is not one Gunnar may work in (not the host, a dedicated server, or " +
                    "somebody else connected), so nothing was dropped and what he carried is unresolved";
            case DeathDropVerdict.SeamUnavailable:
                return "this build of the game does not offer everything the drop needs, so nothing was " +
                    "dropped and what he carried is unresolved";
            case DeathDropVerdict.InventoryUnreadable:
                return "his body had never read what it was carrying, so nothing was dropped and what " +
                    "his saved record held is unresolved";
            default:
                return "nothing decided what happens to what he carried, so nothing was dropped";
        }
    }
}

/// <summary>What became of one carried stack when he died.</summary>
internal readonly struct DeathDropLeg
{
    private DeathDropLeg(string item, int held, int dropped, int kept, int uncertain, string detail)
    {
        Item = item ?? string.Empty;
        Held = held;
        Dropped = dropped;
        Kept = kept;
        Uncertain = uncertain;
        Detail = detail ?? string.Empty;
    }

    /// <summary>The item, by its prefab name - the name the collection ledger
    /// keys on.</summary>
    public string Item { get; }

    /// <summary>The stack's own count when the drop was asked for.</summary>
    public int Held { get; }

    /// <summary>Measured as having left him, with vanilla answering that it
    /// dropped them.</summary>
    public int Dropped { get; }

    /// <summary>Still in him when the drop finished, so destroyed with the body
    /// a moment later. Held-and-unresolved.</summary>
    public int Kept { get; }

    /// <summary>Left him without vanilla confirming a drop, or by an amount the
    /// measurement cannot reconcile. Never credited as on the ground.</summary>
    public int Uncertain { get; }

    /// <summary>Kept plus uncertain: everything not proven to be on the ground.
    /// </summary>
    public int Unresolved => Kept + Uncertain;

    public string Detail { get; }

    /// <summary>A stack the verdict did not let drop. All of it is kept, which is
    /// unresolved: it goes with the body.</summary>
    public static DeathDropLeg NotAttempted(string item, int held, string detail) =>
        new DeathDropLeg(item, Positive(held), 0, Positive(held), 0, detail);

    /// <summary>Classifies one attempted drop <b>from the measurement</b>.
    /// Vanilla's own answer is necessary for a credit and never sufficient: a
    /// stack is "dropped" only when vanilla said so, nothing threw, and exactly
    /// the asked count left his inventory.</summary>
    /// <param name="asked">The stack's count, which is what was asked for.</param>
    /// <param name="before">How many of this item he held just before.</param>
    /// <param name="after">How many he held just after.</param>
    /// <param name="returned">What vanilla's drop answered.</param>
    /// <param name="threw">Whether the drop threw.</param>
    public static DeathDropLeg Classify(string item, int asked, int before, int after, bool returned, bool threw)
    {
        asked = Positive(asked);
        int removed = before - after;
        if (before < 0 || after < 0 || removed < 0)
        {
            // A count that went up during a drop, or a count nobody could read,
            // is a disagreement about reality. Nothing is credited to the ground,
            // and the whole stack is unresolved rather than guessed at.
            return new DeathDropLeg(item, asked, 0, 0, asked,
                "his count of it could not be reconciled (" + Number(before) + " before, " + Number(after) + " after)");
        }

        if (removed == 0)
        {
            return new DeathDropLeg(item, asked, 0, asked, 0,
                threw ? "the drop threw before anything left him" : "the game declined to drop it");
        }

        if (returned && !threw && removed == asked)
        {
            return new DeathDropLeg(item, asked, asked, 0, 0, string.Empty);
        }

        // Something left him and the drop is not confirmed for exactly that
        // amount. Whatever left is uncertain - it may be on the ground, it may be
        // nowhere - and whatever of the stack is still in him is kept. If more
        // left than was asked for, the excess is uncertain too and nothing is
        // invented to balance it.
        int uncertain = removed;
        int kept = removed < asked ? asked - removed : 0;
        return new DeathDropLeg(item, asked, 0, kept, uncertain,
            threw
                ? "the drop threw after " + removed + " had left him"
                : !returned
                    ? "the game said it did not drop it, but " + removed + " left him"
                    : removed + " left him where " + asked + " was asked for");
    }

    private static int Positive(int value) => value > 0 ? value : 0;

    private static string Number(int value) =>
        value < 0 ? "unreadable" : value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Everything that happened to what he carried when he died.
///
/// <b>Every unit is either on the ground or unresolved.</b> That is D16's
/// accounting rule, and <see cref="Conserves"/> is the one comparison that says
/// whether this report keeps it.</summary>
internal sealed class DeathDropReport
{
    private readonly List<DeathDropLeg> _legs = new List<DeathDropLeg>();

    public DeathDropReport(DeathDropVerdict verdict)
    {
        Verdict = verdict;
        Interrupted = string.Empty;
    }

    public DeathDropVerdict Verdict { get; }

    public IReadOnlyList<DeathDropLeg> Legs => _legs;

    /// <summary>Why the walk over his inventory stopped early, or empty. A report
    /// that says this is incomplete, and says so in the durable record.</summary>
    public string Interrupted { get; private set; }

    public void Add(DeathDropLeg leg) => _legs.Add(leg);

    public void MarkInterrupted(string why) =>
        Interrupted = string.IsNullOrEmpty(why) ? "the drop stopped for a reason nobody recorded" : why;

    public int Held => Sum(leg => leg.Held);

    public int Dropped => Sum(leg => leg.Dropped);

    public int Unresolved => Sum(leg => leg.Unresolved);

    /// <summary>Held equals dropped plus unresolved, in total and per leg. A leg
    /// whose measurement found MORE leaving than was asked for breaks this on
    /// purpose: the excess is unresolved, never balanced away.</summary>
    public bool Conserves
    {
        get
        {
            for (int index = 0; index < _legs.Count; index++)
            {
                DeathDropLeg leg = _legs[index];
                if (leg.Held != leg.Dropped + leg.Unresolved)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Per item, in a deterministic order: what went on the ground and
    /// what is unresolved. What the ledger is told.</summary>
    public IReadOnlyList<DeathDropTotal> Totals()
    {
        var byItem = new Dictionary<string, DeathDropTotal>(StringComparer.Ordinal);
        for (int index = 0; index < _legs.Count; index++)
        {
            DeathDropLeg leg = _legs[index];
            byItem.TryGetValue(leg.Item, out DeathDropTotal total);
            byItem[leg.Item] = new DeathDropTotal(
                leg.Item, total.Held + leg.Held, total.Dropped + leg.Dropped, total.Unresolved + leg.Unresolved);
        }

        var totals = new List<DeathDropTotal>(byItem.Values);
        totals.Sort((left, right) => string.CompareOrdinal(left.Item, right.Item));
        return totals;
    }

    private int Sum(Func<DeathDropLeg, int> part)
    {
        int total = 0;
        for (int index = 0; index < _legs.Count; index++)
        {
            total += part(_legs[index]);
        }

        return total;
    }
}

/// <summary>One item's totals in a death report.</summary>
internal readonly struct DeathDropTotal
{
    public DeathDropTotal(string item, int held, int dropped, int unresolved)
    {
        Item = item ?? string.Empty;
        Held = held;
        Dropped = dropped;
        Unresolved = unresolved;
    }

    public string Item { get; }

    public int Held { get; }

    public int Dropped { get; }

    public int Unresolved { get; }
}

/// <summary>The durable record of a death, as text: one tab-separated row per
/// item plus one row for the death itself, appended to a per-world file beside
/// Teamster's other sidecars. D16 asks that a drop that failed is "recorded and
/// left as durable reconciliation evidence"; the BepInEx log is overwritten on
/// the next launch, so it is not that.
///
/// <b>Append-only by construction.</b> <see cref="Append"/> keeps every byte of
/// what was there and adds rows after it; nothing in this product edits or
/// removes a row.</summary>
internal static class WorkerDeathRows
{
    public const string Header =
        "# Concerned Teamster: what Gunnar was carrying when he died (DECISIONS.md D16). Append-only.\n" +
        "# kind\twhen_utc\tworker\tx\ty\tz\tverdict\titem\theld\tdropped\tunresolved\tdetail\n";

    /// <summary>The rows for one death.</summary>
    public static string Serialize(
        DateTime whenUtc, string worker, float x, float y, float z, DeathDropReport report)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        string when = whenUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string where = Number(x) + "\t" + Number(y) + "\t" + Number(z);
        string head = "\t" + when + "\t" + Clean(worker) + "\t" + where + "\t" + report.Verdict + "\t";

        var text = new StringBuilder();
        text.Append("death").Append(head)
            .Append('*').Append('\t')
            .Append(report.Held.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(report.Dropped.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(report.Unresolved.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(Clean(Summary(report)))
            .Append('\n');

        for (int index = 0; index < report.Legs.Count; index++)
        {
            DeathDropLeg leg = report.Legs[index];
            text.Append("stack").Append(head)
                .Append(Clean(leg.Item)).Append('\t')
                .Append(leg.Held.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(leg.Dropped.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(leg.Unresolved.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(Clean(leg.Detail))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Existing content plus the new rows. Never shortens what was
    /// there; a file that did not exist gets the header first.</summary>
    public static string Append(string? existing, string rows)
    {
        if (string.IsNullOrEmpty(existing))
        {
            return Header + (rows ?? string.Empty);
        }

        string kept = existing!;
        if (!kept.EndsWith("\n", StringComparison.Ordinal))
        {
            kept += "\n";
        }

        return kept + (rows ?? string.Empty);
    }

    /// <summary>One sentence for the death row.</summary>
    public static string Summary(DeathDropReport report)
    {
        string said = WorkerDeathDrop.Describe(report.Verdict);
        if (report.Interrupted.Length != 0)
        {
            said += "; the drop was interrupted: " + report.Interrupted;
        }

        if (!report.Conserves)
        {
            said += "; the counts do not reconcile, and nothing was invented to make them";
        }

        return said;
    }

    private static string Number(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? "?" : value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>No tab, newline or carriage return can reach a field, so one
    /// row is always one line and one field is always one column.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "-";
        }

        var clean = new StringBuilder(text!.Length);
        foreach (char character in text)
        {
            clean.Append(character == '\t' || character == '\n' || character == '\r' ? ' ' : character);
        }

        return clean.ToString();
    }
}
