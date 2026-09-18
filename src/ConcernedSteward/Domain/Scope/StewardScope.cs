using System;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Scope;

/// <summary>Whether the Steward has somewhere to work and something to work
/// from. <see cref="Unavailable"/> is zero: a verdict nobody computed is never
/// a grant.</summary>
internal enum ScopeVerdict
{
    /// <summary>Not established. Refuse.</summary>
    Unavailable = 0,

    /// <summary>A settlement area and a supply depot are both marked, and the
    /// depot's identity belongs to this run of the world.</summary>
    Ready = 1,

    /// <summary>Nothing is marked. <b>Nothing marked means nothing in
    /// scope</b>, never "anywhere".</summary>
    NoSettlementArea = 2,

    /// <summary>The settlement is marked but no chest is.</summary>
    NoSupplyDepot = 3,

    /// <summary>A chest is marked, but its identity was written in a previous
    /// run of the world and now names nothing. The player marks it again; that
    /// is one keystroke and it is honest.</summary>
    StaleDepotIdentity = 4,

    /// <summary>No world-load identity space, so no chest key can be told from
    /// a stale one. Refuses both marking and resolving.</summary>
    NoIdentityEpoch = 5,
}

/// <summary>One reading of the Steward's scope, with the revision it was read
/// at.
///
/// <b>Why a snapshot rather than live lookups.</b> A job spans a walk, and a
/// walk spans seconds during which a player can un-mark their settlement, move
/// their chest, or reload. Reading the scope fresh at each step would let a job
/// start against one settlement and finish against another, with the withdrawal
/// charged to the first. So the job carries the revision it started at and
/// every step checks it is still current — the stale-assignment rule, made
/// mechanical.</summary>
internal readonly struct ScopeSnapshot
{
    internal ScopeSnapshot(
        ScopeVerdict verdict,
        int revision,
        Designation? settlement,
        SitePoint depotPoint,
        string depotKey,
        string epoch)
    {
        Verdict = verdict;
        Revision = revision;
        Settlement = settlement;
        DepotPoint = depotPoint;
        DepotKey = depotKey ?? string.Empty;
        Epoch = epoch ?? string.Empty;
    }

    public ScopeVerdict Verdict { get; }

    /// <summary>The scope's revision when this was read. A job started at one
    /// revision refuses to act at another.</summary>
    public int Revision { get; }

    /// <summary>The marked settlement area, or null. The only ground the
    /// Steward acts on.</summary>
    public Designation? Settlement { get; }

    /// <summary>Where the depot stood when it was marked. Shown to the player
    /// and used to walk towards; <b>never</b> used to decide which chest is
    /// meant — that is <see cref="DepotKey"/>'s job, because a position would
    /// silently follow whatever ends up standing there.</summary>
    public SitePoint DepotPoint { get; }

    /// <summary>The depot's own identity, opaque to this layer.</summary>
    public string DepotKey { get; }

    /// <summary>Which run of the world the key means anything in.</summary>
    public string Epoch { get; }

    public bool IsReady => Verdict == ScopeVerdict.Ready;

    public SitePoint SettlementCentre => Settlement == null ? default : Settlement.Centre;

    public float SettlementRadius => Settlement == null ? 0f : Settlement.Radius;

    /// <summary>One sentence a player can act on. Every refusal names the thing
    /// to do next, because a Steward who stands still without saying why is
    /// indistinguishable from a broken one.</summary>
    public string Describe()
    {
        switch (Verdict)
        {
            case ScopeVerdict.Ready:
                return "Working inside the marked settlement, from the marked supply chest.";

            case ScopeVerdict.NoSettlementArea:
                return "No settlement area is marked. Stand where you want it and run " +
                    "\"cs_steward area <radius>\".";

            case ScopeVerdict.NoSupplyDepot:
                return "No supply chest is marked. Look at the chest the Steward may take " +
                    "wood from and run \"cs_steward depot\".";

            case ScopeVerdict.StaleDepotIdentity:
                return "The marked supply chest was marked in a previous session, so it no " +
                    "longer names anything. Look at it and run \"cs_steward depot\" again.";

            case ScopeVerdict.NoIdentityEpoch:
                return "No world is loaded, so the marked chest cannot be identified.";

            default:
                return "The Steward's working area could not be established, so he is not " +
                    "acting. This is a bug — please report it.";
        }
    }
}

/// <summary>The Steward's working area and supply depot, and the only way to
/// change either.
///
/// This is a thin, deliberate wrapper over the shared
/// <see cref="DesignationBook"/> rather than a second designation system. The
/// book already holds exactly the two rules this needs — a supply container is
/// one named container and not "the nearest chest", and a container key from a
/// previous run of the world resolves to <i>nothing</i> rather than to a guess
/// — and reimplementing them here would mean two places to get the stale-key
/// rule right.
///
/// What this adds is the <b>revision</b>. The book answers questions; it does
/// not tell a long-running job that the answers changed underneath it. A
/// counter that moves on every change is what lets a job that started before a
/// re-mark refuse to finish after one.
///
/// The Steward uses two of the book's three kinds and never the third: a
/// settlement area and a supply container. He does not fell trees, so a harvest
/// area is not his to mark.</summary>
internal sealed class StewardScope
{
    private readonly DesignationBook _book = new DesignationBook();
    private string? _epoch;

    /// <summary>Moves on every change to either designation, and on every
    /// change of world-load epoch.</summary>
    public int Revision { get; private set; }

    /// <summary>The underlying book, for the command layer that marks and
    /// un-marks. Deliberately not exposed to the upkeep loop, which only ever
    /// reads a snapshot.</summary>
    internal DesignationBook Book => _book;

    /// <summary>Tells the scope which run of the world it is in. Changing it
    /// invalidates every job in flight, which is the point: a reload is exactly
    /// when a chest key stops meaning what it meant.</summary>
    public void UseIdentityEpoch(string? epoch)
    {
        if (string.Equals(_epoch, epoch, StringComparison.Ordinal))
        {
            return;
        }

        _epoch = epoch;
        _book.UseIdentityEpoch(epoch);
        Revision++;
    }

    public string? Epoch => _epoch;

    /// <summary>Marks something. Every refusal comes back as a reason, never as
    /// a silent no-op.</summary>
    public DesignationResult Designate(DesignationRequest request, IDesignationSite site)
    {
        DesignationResult result = _book.Designate(request, site);
        if (result.Outcome == DesignationOutcome.Designated)
        {
            Revision++;
        }

        return result;
    }

    /// <summary>Replaces a chest whose key belongs to a previous run.
    ///
    /// The book refuses a plain re-mark over a stale row, because in Foreman's
    /// world that overwrite would orphan a reservation held against the old
    /// key. The Steward holds no such reservation against a depot — his
    /// reservations are on fires — so the recovery here is to drop the stale row
    /// explicitly and then mark, which is visible in this method rather than
    /// hidden inside the book.</summary>
    public DesignationResult ReplaceStaleDepot(DesignationRequest request, IDesignationSite site)
    {
        if (!_book.WouldReplaceStaleContainer(request))
        {
            return Designate(request, site);
        }

        // Ask first, and only remove the stale row if the replacement would be
        // accepted. Removing it for a replacement that is then refused would
        // leave the player with neither.
        DesignationResult probe = _book.ProbeOverStaleContainer(request, site);
        if (probe.Outcome != DesignationOutcome.Designated)
        {
            return probe;
        }

        _book.Undesignate(DesignationKind.SupplyContainer);
        Revision++;
        return Designate(request, site);
    }

    /// <summary>Un-marks something and everything that belonged to it.</summary>
    public void Undesignate(DesignationKind kind)
    {
        if (_book.Undesignate(kind).Count > 0)
        {
            Revision++;
        }
    }

    /// <summary>Restores a designation read from disk without re-running the
    /// world checks. Loading is not designating.</summary>
    internal bool Restore(Designation designation)
    {
        if (!_book.Restore(designation))
        {
            return false;
        }

        Revision++;
        return true;
    }

    /// <summary>Reads the scope now.
    ///
    /// The order of refusals is the order a player would fix them in: an area
    /// first, because a chest belongs to one; then a chest; then whether the
    /// chest still resolves.</summary>
    public ScopeSnapshot Resolve()
    {
        if (string.IsNullOrEmpty(_epoch))
        {
            return new ScopeSnapshot(
                ScopeVerdict.NoIdentityEpoch, Revision, null, default, string.Empty, string.Empty);
        }

        if (!_book.TryGet(DesignationKind.SettlementArea, out Designation settlement))
        {
            return new ScopeSnapshot(
                ScopeVerdict.NoSettlementArea, Revision, null, default, string.Empty, _epoch!);
        }

        if (!_book.TryGet(DesignationKind.SupplyContainer, out Designation depot))
        {
            return new ScopeSnapshot(
                ScopeVerdict.NoSupplyDepot, Revision, settlement, default, string.Empty, _epoch!);
        }

        if (_book.HasStaleSupplyIdentity || !_book.IsSupplyContainer(depot.ContainerKey))
        {
            return new ScopeSnapshot(
                ScopeVerdict.StaleDepotIdentity, Revision, settlement, depot.Centre,
                string.Empty, _epoch!);
        }

        return new ScopeSnapshot(
            ScopeVerdict.Ready, Revision, settlement, depot.Centre, depot.ContainerKey!, _epoch!);
    }

    /// <summary>True when a snapshot taken earlier still describes the scope.
    /// Checked before every mutation, not once when a job starts.</summary>
    public bool IsCurrent(in ScopeSnapshot snapshot) => snapshot.Revision == Revision;

    /// <summary>True when this is the one chest the Steward may take from.
    /// False for every other, and false when nothing is marked.</summary>
    public bool IsSupplyDepot(string? containerKey) => _book.IsSupplyContainer(containerKey);
}
