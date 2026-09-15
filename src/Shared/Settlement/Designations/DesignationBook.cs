using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Designations;

/// <summary>What one settlement's player has marked, and the only way to change
/// it.
///
/// <b>Nothing here can be designated implicitly.</b> That is a property of the
/// type rather than a promise in a comment: there is no constructor, method or
/// field that takes a position and works out what was meant. The only way a
/// designation comes into existence is <see cref="Designate"/>, with an
/// explicit kind, explicit geometry and an explicit ward answer — and the only
/// way one leaves is <see cref="Undesignate"/>. A worker asking "may I fell
/// this tree" gets its answer by <i>reading</i> the book; nothing it does can
/// write to it.
///
/// Re-marking follows from the same rule. Marking the same thing the same way
/// twice is idempotent. Marking the same <i>kind</i> a <i>different</i> way is
/// refused and names the one that exists, because silently moving a player's
/// settlement is a designation the player did not make.
///
/// <b>What this book does not do:</b> it does not re-check old designations
/// against the world. A ward raised after the fact does not retroactively
/// un-mark ground — it makes the placement refuse, which is CF-SET-003's gate
/// and is re-checked at the moment a piece is placed. Marking is a record of a
/// player's intent; legality at the instant of acting is checked when acting.</summary>
internal sealed class DesignationBook
{
    /// <summary>The smallest settlement worth marking. Below this the area is
    /// barely larger than a single piece, and every placement inside it would
    /// be at the edge.</summary>
    public const float MinRadius = 4f;

    /// <summary>The largest area the first proof will mark.
    ///
    /// Chosen against the ward check rather than against taste: the check is
    /// only meaningful over ground the game has loaded, and past this size an
    /// area routinely reaches outside it — so a larger designation would spend
    /// most of its life being refused as unanswerable. Refusing it up front,
    /// by size, is the honest version of the same answer. #273 also asks for
    /// one <i>small</i> settlement, and this is comfortably that.</summary>
    public const float MaxRadius = 48f;

    private readonly Dictionary<DesignationKind, Designation> _designations =
        new Dictionary<DesignationKind, Designation>();

    /// <summary>Which run of the world we are in, as far as object identities
    /// are concerned. Null until an adapter supplies one, and null means no
    /// chest can be designated or resolved — fail-closed, because without it a
    /// key from a previous run could not be told from a current one.</summary>
    private string? _epoch;

    internal void UseIdentityEpoch(string? epoch)
    {
        _epoch = epoch;
    }

    /// <summary>Everything marked, in a stable order so that saving the book
    /// twice produces the same file.</summary>
    public IReadOnlyList<Designation> Designations
    {
        get
        {
            var ordered = new List<Designation>(_designations.Count);
            foreach (DesignationKind kind in OrderedKinds)
            {
                if (_designations.TryGetValue(kind, out Designation? designation))
                {
                    ordered.Add(designation!);
                }
            }

            return ordered;
        }
    }

    /// <summary>Settlement area first, because everything else belongs to it
    /// and a file that lists a child before its parent reads wrongly.</summary>
    private static readonly DesignationKind[] OrderedKinds =
    {
        DesignationKind.SettlementArea,
        DesignationKind.HarvestArea,
        DesignationKind.SupplyContainer,
    };

    public bool TryGet(DesignationKind kind, out Designation designation)
    {
        return _designations.TryGetValue(kind, out designation!);
    }

    public bool Has(DesignationKind kind) => _designations.ContainsKey(kind);

    public int Count => _designations.Count;

    /// <summary>Marks something, or refuses and says why.</summary>
    public DesignationResult Designate(DesignationRequest request, IDesignationSite site)
    {
        if (request.Kind == DesignationKind.None)
        {
            return DesignationResult.Refused(DesignationRefusal.InvalidRequest);
        }

        Designation candidate;
        if (request.Kind == DesignationKind.SupplyContainer)
        {
            if (string.IsNullOrEmpty(request.ContainerKey))
            {
                return DesignationResult.Refused(DesignationRefusal.ContainerNotIdentified);
            }

            if (string.IsNullOrEmpty(_epoch))
            {
                return DesignationResult.Refused(DesignationRefusal.ContainerIdentityUnavailable);
            }

            candidate = new Designation(
                DesignationKind.SupplyContainer, request.Centre, 0f, request.ContainerKey, _epoch);
        }
        else
        {
            if (!(request.Radius >= MinRadius) || !(request.Radius <= MaxRadius))
            {
                return DesignationResult.Refused(DesignationRefusal.RadiusOutOfRange);
            }

            candidate = new Designation(request.Kind, request.Centre, request.Radius, null);
        }

        // Idempotence is settled before anything else is asked, and that
        // ordering is deliberate. Re-marking exactly what is already marked
        // changes nothing, so it must not be able to fail — including on a ward
        // check that has started answering differently. The existing
        // designation would survive such a refusal anyway, so refusing would
        // only produce a confusing message about something that did not change.
        if (_designations.TryGetValue(request.Kind, out Designation? existing))
        {
            if (existing!.SameAs(candidate))
            {
                return DesignationResult.Already(existing);
            }

            // A chest whose key was written in a previous run of the world is
            // not a designation you have to clear first -- it is the memory of
            // one, and it already resolves to nothing. Re-marking is the
            // recovery path, so it replaces rather than being refused. Refusing
            // would leave a player with a row they can see, cannot use, and
            // cannot replace without first clearing something that is already
            // inert.
            if (!existing.IsStaleIdentity(_epoch))
            {
                return DesignationResult.Refused(
                    DesignationRefusal.AlreadyDesignatedDifferently, existing);
            }
        }

        if (request.Kind != DesignationKind.SettlementArea
            && !_designations.TryGetValue(DesignationKind.SettlementArea, out Designation? _))
        {
            return DesignationResult.Refused(DesignationRefusal.NoSettlementArea);
        }

        if (request.Kind == DesignationKind.SupplyContainer)
        {
            Designation settlement = _designations[DesignationKind.SettlementArea];
            if (!settlement.Contains(candidate.Centre))
            {
                return DesignationResult.Refused(
                    DesignationRefusal.ContainerOutsideSettlement, settlement);
            }
        }

        switch (site == null ? AreaAccess.Unavailable : site.CheckAccess(candidate.Centre, candidate.Radius))
        {
            case AreaAccess.Granted:
                break;

            case AreaAccess.Denied:
                return DesignationResult.Refused(DesignationRefusal.WardDenied);

            default:
                return DesignationResult.Refused(DesignationRefusal.WardCheckUnavailable);
        }

        _designations[candidate.Kind] = candidate;
        return DesignationResult.Designated(candidate);
    }

    /// <summary>Removes a designation and everything that belonged to it.
    ///
    /// Removing the settlement area removes the harvest area and the supply
    /// container too: they are described relative to a settlement, and a
    /// harvest area belonging to a settlement that no longer exists is state
    /// nobody asked for. The recruited worker is deliberately <b>not</b>
    /// dismissed — dismissing somebody is its own explicit act, and doing it as
    /// a side effect of clearing ground is the same class of surprise as
    /// designating something implicitly.</summary>
    public IReadOnlyList<Designation> Undesignate(DesignationKind kind)
    {
        var removed = new List<Designation>();
        if (kind == DesignationKind.None)
        {
            return removed;
        }

        if (kind == DesignationKind.SettlementArea)
        {
            // Children first, so a caller applying this list in order returns
            // the supply container's contents before the settlement it sat in
            // stops existing.
            foreach (DesignationKind child in new[]
            {
                DesignationKind.SupplyContainer, DesignationKind.HarvestArea,
            })
            {
                if (_designations.TryGetValue(child, out Designation? owned))
                {
                    removed.Add(owned!);
                    _designations.Remove(child);
                }
            }
        }

        if (_designations.TryGetValue(kind, out Designation? target))
        {
            removed.Add(target!);
            _designations.Remove(kind);
        }

        return removed;
    }

    /// <summary>Restores a designation read from disk, without re-running the
    /// world checks. Returns false when the row cannot be taken.
    ///
    /// Loading is not designating. The checks that ran when the player marked
    /// it ran against the world as it was then, and re-running them at load
    /// would mean a ward built next door, or a world not finished loading,
    /// quietly deleted a settlement the player still has. A record is read back
    /// as it was written.
    ///
    /// A <b>second</b> row of a kind that already has one is a different
    /// matter, and is refused rather than taken as last-wins. The file is
    /// supposed to hold at most one of each; two means it is damaged, and
    /// silently keeping whichever happened to be second would discard a
    /// designation and then rewrite the file without it.</summary>
    internal bool Restore(Designation designation)
    {
        if (_designations.ContainsKey(designation.Kind))
        {
            return false;
        }

        // A harvest area or a chest belongs to a settlement area. A file that
        // carries one without the other is damaged, and taking the orphan would
        // produce a settlement whose parts answer questions their parent never
        // authorised -- then write it back in that shape.
        if (designation.Kind != DesignationKind.SettlementArea
            && !_designations.ContainsKey(DesignationKind.SettlementArea))
        {
            return false;
        }

        _designations[designation.Kind] = designation;
        return true;
    }

    /// <summary>True when a point is inside the marked harvest area. False when
    /// there is no harvest area — "nothing is marked" means "nothing may be
    /// felled", never "anywhere".</summary>
    public bool IsInHarvestArea(SitePoint point)
    {
        return _designations.TryGetValue(DesignationKind.HarvestArea, out Designation? harvest)
            && harvest!.Contains(point);
    }

    /// <summary>True when this is the one container a worker may take from.
    /// False when nothing is designated, and false for every other
    /// container.</summary>
    public bool IsSupplyContainer(string? containerKey)
    {
        if (string.IsNullOrEmpty(containerKey))
        {
            return false;
        }

        if (!_designations.TryGetValue(DesignationKind.SupplyContainer, out Designation? supply))
        {
            return false;
        }

        // A key from a previous run of the world names nothing now, and the ids
        // it was drawn from are handed out densely from one on every load -- so
        // a stale key is not merely useless, it is LIKELY to match some other
        // chest. Answering false is the only safe answer available.
        if (supply!.IsStaleIdentity(_epoch))
        {
            return false;
        }

        return string.Equals(supply.ContainerKey, containerKey, System.StringComparison.Ordinal);
    }

    /// <summary>True when a chest is marked but its identity was written in a
    /// previous run of the world, so it currently resolves to nothing and the
    /// player needs to mark it again.</summary>
    public bool HasStaleSupplyIdentity
    {
        get
        {
            return _designations.TryGetValue(DesignationKind.SupplyContainer, out Designation? supply)
                && supply!.IsStaleIdentity(_epoch);
        }
    }
}
