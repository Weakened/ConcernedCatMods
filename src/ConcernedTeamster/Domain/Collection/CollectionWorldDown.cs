using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Whether Gunnar's body's own inventory record (`tcc.worker.inventory`,
/// written by <c>TeamsterWorkerRecord</c>) was last seen holding what he
/// carries. Zero is never trust.</summary>
internal enum BodyRecordTrust
{
    /// <summary>No body was seen while he held anything this world, so nobody
    /// can say. Read as <see cref="Untrusted"/>.</summary>
    Unknown = 0,

    /// <summary>A bound body whose record had loaded and whose last change
    /// persisted.</summary>
    Trusted = 1,

    /// <summary>A body whose record never loaded, or whose last change did not
    /// reach its network object: what the live inventory holds is not what the
    /// world will save.</summary>
    Untrusted = 2,
}

/// <summary>Where one unsettled holding of his collection account stands when
/// its world goes down. Zero means nobody said.</summary>
internal enum WorldDownStanding
{
    Unspecified = 0,

    /// <summary>On his back, and his body's own record was last seen holding
    /// it. The record saves with the world, so it is already durable evidence
    /// and nothing else is written.</summary>
    InHisBodyRecord = 1,

    /// <summary>In the assigned cart. A vanilla container that saves with the
    /// world; nothing else is written.</summary>
    InTheCart = 2,

    /// <summary>On his back, and nothing shows his body's record holds it. The
    /// account was the only evidence, so it is written to the world-down
    /// record before the account is dropped, or said in the log where that
    /// write fails.</summary>
    Unresolved = 3,
}

/// <summary>One unsettled holding at world-down.</summary>
internal readonly struct WorldDownHolding
{
    public WorldDownHolding(string item, int units, CargoPlace place, WorldDownStanding standing)
    {
        Item = item ?? string.Empty;
        Units = units;
        Place = place;
        Standing = standing;
    }

    public string Item { get; }

    public int Units { get; }

    public CargoPlace Place { get; }

    public WorldDownStanding Standing { get; }
}

/// <summary>The lead default for what an unsettled collection account becomes
/// when its world goes down (#376, third slice), <b>pending owner
/// confirmation</b> (docs/mods/concerned-npc/ARCHITECTURE.md section 10, lead
/// default 8). The one place to change it.
///
/// <b>The account ends with its world.</b> The next world starts a fresh one,
/// so nothing he took in one world is ever accounted as his in another. What
/// it still held is classified here, failing closed: carried material counts
/// as kept only when his body's own record was <i>seen</i> holding it; unknown
/// trust is no trust. Nothing is ever written off, re-acquired, moved or
/// created - this decides what is written down, never what happens in a
/// world.</summary>
internal static class WorldDownPolicy
{
    public static WorldDownStanding ForCarried(BodyRecordTrust trust) =>
        trust == BodyRecordTrust.Trusted ? WorldDownStanding.InHisBodyRecord : WorldDownStanding.Unresolved;

    public static WorldDownStanding ForCart() => WorldDownStanding.InTheCart;
}

/// <summary>What his collection account still held when its world went down,
/// taken in the same call that replaced it with a fresh one.</summary>
internal sealed class WorldDownCustody
{
    private readonly WorldDownHolding[] _holdings;

    public WorldDownCustody(
        IReadOnlyList<WorldDownHolding>? holdings, int acquired, bool conserved, BodyRecordTrust trust)
    {
        var copy = new List<WorldDownHolding>();
        if (holdings != null)
        {
            for (int index = 0; index < holdings.Count; index++)
            {
                if (holdings[index].Units > 0)
                {
                    copy.Add(holdings[index]);
                }
            }
        }

        _holdings = copy.ToArray();
        Acquired = acquired;
        Conserved = conserved;
        Trust = trust;
    }

    public IReadOnlyList<WorldDownHolding> Holdings => _holdings;

    /// <summary>Everything the closed account ever acquired in its world.
    /// </summary>
    public int Acquired { get; }

    /// <summary>Whether the closed account balanced when it closed.</summary>
    public bool Conserved { get; }

    public BodyRecordTrust Trust { get; }

    /// <summary>Nothing was on his back or in the cart: there is nothing to
    /// keep and nothing is written.</summary>
    public bool IsSettled => _holdings.Length == 0;

    public int UnitsStanding(WorldDownStanding standing)
    {
        int total = 0;
        for (int index = 0; index < _holdings.Length; index++)
        {
            if (_holdings[index].Standing == standing)
            {
                total += _holdings[index].Units;
            }
        }

        return total;
    }

    /// <summary>The units only the closed account could vouch for - what the
    /// durable world-down record is for.</summary>
    public int Unresolved => UnitsStanding(WorldDownStanding.Unresolved);

    /// <summary>One sentence a player can read.</summary>
    public string Describe()
    {
        if (IsSettled)
        {
            return "his collection account held nothing unsettled";
        }

        return "his collection account held " +
            UnitsStanding(WorldDownStanding.InHisBodyRecord).ToString(CultureInfo.InvariantCulture) +
            " on his back that his body's own record holds, " +
            UnitsStanding(WorldDownStanding.InTheCart).ToString(CultureInfo.InvariantCulture) +
            " in the assigned cart, and " + Unresolved.ToString(CultureInfo.InvariantCulture) +
            " on his back that nothing shows his body's record holds (" + DescribeTrust(Trust) + ")" +
            (Conserved ? "" : "; THE ACCOUNT DID NOT BALANCE when it closed");
    }

    private static string DescribeTrust(BodyRecordTrust trust)
    {
        switch (trust)
        {
            case BodyRecordTrust.Trusted: return "his record was last seen current";
            case BodyRecordTrust.Untrusted: return "his record was last seen unread or behind his inventory";
            default: return "no body of his was bound when it was last looked at";
        }
    }
}

/// <summary>The world-down record's rows: one append-only file per world
/// beside the death record (`DECISIONS.md` D16's convention for evidence a
/// player may only look for tomorrow). Only <see cref="WorldDownStanding.Unresolved"/>
/// holdings are written; the rest are kept by the world itself.</summary>
internal static class WorldDownRows
{
    public const string Header =
        "# Concerned Teamster: what Gunnar's collection account still held when its world went down (#376), " +
        "and nothing shows his body's own record holds. Append-only.\n" +
        "# kind\twhen_utc\tworker\ttrust\titem\tunits\tplace\tdetail\n";

    /// <summary>The rows for one world-down, or empty when nothing is
    /// unresolved.</summary>
    public static string Serialize(DateTime whenUtc, string worker, WorldDownCustody custody, string why)
    {
        if (custody == null)
        {
            throw new ArgumentNullException(nameof(custody));
        }

        if (custody.Unresolved == 0)
        {
            return string.Empty;
        }

        string when = whenUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string head = "\t" + when + "\t" + Clean(worker) + "\t" + custody.Trust + "\t";
        var text = new StringBuilder();
        text.Append("world-down").Append(head)
            .Append('*').Append('\t')
            .Append(custody.Unresolved.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append('*').Append('\t')
            .Append(Clean(why + "; " + custody.Describe()))
            .Append('\n');

        IReadOnlyList<WorldDownHolding> holdings = custody.Holdings;
        for (int index = 0; index < holdings.Count; index++)
        {
            WorldDownHolding holding = holdings[index];
            if (holding.Standing != WorldDownStanding.Unresolved)
            {
                continue;
            }

            text.Append("holding").Append(head)
                .Append(Clean(holding.Item)).Append('\t')
                .Append(holding.Units.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(holding.Place).Append('\t')
                .Append("unresolved")
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

/// <summary>What the runtime has to remember about one world so that closing
/// its account at the down edge does not depend on anything the down edge can
/// still read (#376, third slice).
///
/// <b>Why it is latched while the world is up.</b> By the frame the runtime
/// sees a world has gone, the world id and his body are gone with it, and the
/// body-record flags answer "no body", which is not "a current record".
/// Reading them then would be failing open. So the world id is taken the first
/// time it can be read in a world, and his body record's trust is taken from
/// the last world-up frame in which he held anything.
///
/// <b>Trust is earned every frame and lost for good.</b> A record seen unread
/// or behind is <see cref="BodyRecordTrust.Untrusted"/> until the account
/// closes, whatever is seen afterwards: a zone reload re-creates the record
/// from the last package that was written, so a record that reads current again
/// may still lack what the account says he took. A frame with no body of his
/// bound - unloaded with its zone, or removed by a retire - is
/// <see cref="BodyRecordTrust.Unknown"/>, because nothing then shows that his
/// record holds it. Only a bound, current body in the latest frame, with
/// nothing untrusted seen before it, is <see cref="BodyRecordTrust.Trusted"/>.
/// Unknown is read as untrusted by <see cref="WorldDownPolicy"/>.</summary>
internal sealed class CollectionWorldWatch
{
    private long? _worldUid;
    private BodyRecordTrust _trust;

    /// <summary>The world the open account belongs to, when one could be
    /// read.</summary>
    public long? WorldUid => _worldUid;

    public BodyRecordTrust Trust => _trust;

    /// <summary>A frame with the world up. The first id it can read is the
    /// account's world; a later one never replaces it, because only a down
    /// edge ends the account.</summary>
    public void ObserveWorld(long? worldUid)
    {
        if (_worldUid == null && worldUid != null && worldUid.Value != 0L)
        {
            _worldUid = worldUid;
        }
    }

    /// <summary>A frame with the world up and something on his back. Untrusted
    /// is kept until <see cref="Close"/>; otherwise a record that is unread or
    /// behind is untrusted, a bound current body is trusted, and no body is
    /// unknown.</summary>
    public void ObserveBody(bool bodyBound, bool recordUnwritable, bool recordUnreadable)
    {
        if (_trust == BodyRecordTrust.Untrusted || recordUnwritable || recordUnreadable)
        {
            _trust = BodyRecordTrust.Untrusted;
        }
        else
        {
            _trust = bodyBound ? BodyRecordTrust.Trusted : BodyRecordTrust.Unknown;
        }
    }

    /// <summary>The down edge (or a teardown): closes the account with what
    /// was latched, hands back the world it belonged to, and forgets both so
    /// the next world starts with nothing of this one's.</summary>
    public WorldDownCustody Close(CollectionAccount account, out long? worldUid)
    {
        if (account == null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        worldUid = _worldUid;
        WorldDownCustody closed = account.CloseForWorld(_trust);
        _worldUid = null;
        _trust = BodyRecordTrust.Unknown;
        return closed;
    }
}
