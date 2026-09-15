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

            candidate = new Designation(
                DesignationKind.SupplyContainer, request.Centre, 0f, request.ContainerKey);
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

            return DesignationResult.Refused(
                DesignationRefusal.AlreadyDesignatedDifferently, existing);
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
    /// checks.
    ///
    /// Loading is not designating. The checks that ran when the player marked
    /// it ran against the world as it was then, and re-running them at load
    /// would mean a ward built next door, or a world not finished loading,
    /// quietly deleted a settlement the player still has. A record is read back
    /// as it was written.</summary>
    internal void Restore(Designation designation)
    {
        _designations[designation.Kind] = designation;
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

        return _designations.TryGetValue(DesignationKind.SupplyContainer, out Designation? supply)
            && string.Equals(supply!.ContainerKey, containerKey, System.StringComparison.Ordinal);
    }
}
