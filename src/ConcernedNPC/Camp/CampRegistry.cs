using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>The cached camp: what pieces are known, and the last boundary
/// computed from them.
///
/// <b>How it stays cheap, which is part of the contract rather than a nicety.</b>
/// Four things, and each one is measured by <see cref="CampSurveyCost"/> rather
/// than asserted here:
///
/// <i>It never scans the world.</i> Nothing in this file asks anybody for
/// pieces. A role tells it what was built and what was destroyed, as those
/// things happen, and the registry is the accumulation of that. The only reads
/// are of its own dictionary.
///
/// <i>It never recomputes a hull per frame.</i> <see cref="Survey"/> returns
/// the cached snapshot untouched unless a piece changed or the anchor moved, so
/// a role may call it on a timer - or every frame, if it insists - and pay for
/// it only when camp actually changed.
///
/// <i>A survey visits camp, not the world.</i> Pieces are kept in a grid whose
/// cell is the link distance, growth opens only the cells it can reach, and a
/// piece in a cell nothing reaches is never taken out of it. A camp of forty
/// pieces costs the same whether the world holds forty pieces or forty
/// thousand.
///
/// <i>Non-members cost nothing, ever.</i> A terrain edit is refused at the
/// door rather than stored and skipped, so a player who has levelled half a
/// meadow has not made every later survey slower.
///
/// <b>The stray piece.</b> Membership is connectivity from a seed near the
/// anchor, not a radius: one torch on a distant hill is in no cell any member
/// reaches, so it is never visited, never a member, and cannot move the
/// boundary by a metre. That is the property <c>CampBoundaryTests</c> exists to
/// prove, and it is a consequence of the growth rule rather than a check
/// bolted on afterwards.</summary>
internal sealed class CampRegistry
{
    private readonly Dictionary<string, CampPiece> _pieces = new Dictionary<string, CampPiece>(StringComparer.Ordinal);
    private readonly Dictionary<CellKey, List<string>> _cells = new Dictionary<CellKey, List<string>>();
    private readonly CampSurveyOptions _options;

    private CampSnapshot _snapshot;
    private CampAnchor _surveyedAnchor;
    private bool _dirty;
    private int _revision;
    private int _recomputations;

    internal CampRegistry(CampSurveyOptions? options)
    {
        _options = options ?? CampSurveyOptions.Default;
        _snapshot = CampSnapshot.Nowhere(NpcWorldEpoch.Unknown, 0);
    }

    /// <summary>The world load the known pieces are named in. Unknown until a
    /// world is loaded, and every piece is refused until then.</summary>
    internal NpcWorldEpoch Epoch { get; private set; }

    internal CampSurveyOptions Options => _options;

    /// <summary>How many member pieces are known. Not how many are in camp -
    /// a known piece may be connected to nothing.</summary>
    internal int Known => _pieces.Count;

    /// <summary>True when the next survey will recompute rather than answer
    /// from cache.</summary>
    internal bool IsStale => _dirty;

    /// <summary>The last surveyed camp. Never null.</summary>
    internal CampSnapshot Current => _snapshot;

    /// <summary>How many times a survey has actually done the work, as opposed
    /// to handing back the snapshot it handed back last time.
    ///
    /// <b>This is how "no per-frame hull recomputation" is measured.</b> A
    /// cached survey returns the same snapshot object it returned before -
    /// including its cost, which is the cost of the survey that produced it -
    /// so the snapshot cannot report its own cachedness, and a counter here is
    /// the honest place for it.</summary>
    internal int Recomputations => _recomputations;

    /// <summary>A world has been loaded. <b>Everything known is forgotten</b>,
    /// because every name it was keyed by has been reassigned to some other
    /// object. Returns how many pieces were dropped.
    ///
    /// The same epoch twice is a no-op, so several parts of a role may
    /// report the load without any of them needing to know whether another got
    /// there first - the same shape the registry's own world-load event
    /// has.</summary>
    internal int BeginWorldLoad(NpcWorldEpoch epoch)
    {
        if (!epoch.IsUnknown && epoch.Equals(Epoch))
        {
            return 0;
        }

        int dropped = _pieces.Count;
        _pieces.Clear();
        _cells.Clear();
        Epoch = epoch;
        _surveyedAnchor = CampAnchor.None;
        _dirty = true;
        _revision++;
        _snapshot = CampSnapshot.Nowhere(epoch, _revision);
        return dropped;
    }

    /// <summary>A piece exists: newly built, or noticed by a role's periodic
    /// look around. Idempotent by key, which is what lets a role re-report an
    /// area without keeping a record of what it has already said.</summary>
    internal CampPieceOutcome Note(CampPiece piece)
    {
        if (!piece.IsWellFormed)
        {
            return CampPieceOutcome.Malformed;
        }

        if (!piece.Epoch.Matches(Epoch))
        {
            return CampPieceOutcome.StaleEpoch;
        }

        if (!piece.IsMember)
        {
            // Terrain edits and natural objects are not stored at all, so the
            // exclusion is free every time after this one - but a piece this
            // registry already holds has to be let go of when a role changes
            // its mind about it. Answering "excluded" while quietly keeping it
            // in camp is the same shape of defect as a ledger comparing a
            // payload and ignoring what became of it.
            Forget(piece.Key);
            return CampPieceOutcome.Excluded;
        }

        if (_pieces.TryGetValue(piece.Key, out CampPiece known))
        {
            if (known.Position.Equals(piece.Position))
            {
                return CampPieceOutcome.AlreadyKnown;
            }

            Unfile(known);
            _pieces[piece.Key] = piece;
            File(piece);
            _dirty = true;
            return CampPieceOutcome.Moved;
        }

        _pieces.Add(piece.Key, piece);
        File(piece);
        _dirty = true;
        return CampPieceOutcome.Admitted;
    }

    /// <summary>A piece is gone: destroyed, or no longer there. Returns false
    /// when it was not known, which is an ordinary answer - a role may report
    /// the destruction of a terrain edit it never told us about.</summary>
    internal bool Forget(string? key)
    {
        if (string.IsNullOrEmpty(key) || !_pieces.TryGetValue(key!, out CampPiece known))
        {
            return false;
        }

        Unfile(known);
        _pieces.Remove(key!);
        _dirty = true;
        return true;
    }

    /// <summary>Camp, as of now.
    ///
    /// Recomputes only when a piece changed or the anchor moved more than a
    /// metre; otherwise returns the snapshot it returned last time, with a cost
    /// that says so.</summary>
    internal CampSnapshot Survey(CampAnchor anchor)
    {
        if (!_dirty && anchor.SamePlaceAs(_surveyedAnchor))
        {
            return _snapshot;
        }

        _surveyedAnchor = anchor;
        _dirty = false;
        _revision++;
        _recomputations++;

        if (!anchor.HasAnchor || Epoch.IsUnknown)
        {
            _snapshot = CampSnapshot.Nowhere(Epoch, _revision);
            return _snapshot;
        }

        var cost = new Counters();
        List<NpcPoint> members = Grow(anchor, cost);

        NpcPoint centre = CampBoundary.Centre(members, anchor.Position);
        float extent = CampBoundary.Extent(members, centre, _options.PerimeterMarginMetres);
        IReadOnlyList<NpcPoint> perimeter =
            CampBoundary.Perimeter(members, centre, _options.PerimeterMarginMetres);

        _snapshot = new CampSnapshot(
            anchor,
            centre,
            extent,
            perimeter,
            members.Count,
            _revision,
            Epoch,
            new CampSurveyCost(
                true, cost.Cells, cost.Considered, cost.Checks, members.Count, perimeter.Count, cost.Truncated));

        return _snapshot;
    }

    /// <summary>Seeds from the cells around the anchor, then grows outwards
    /// through pieces within the link distance of a piece already in.
    ///
    /// Breadth-first rather than depth-first so that truncation, when it
    /// happens, keeps the pieces nearest the anchor rather than one long
    /// tendril of whichever direction was explored first.</summary>
    private List<NpcPoint> Grow(CampAnchor anchor, Counters cost)
    {
        var members = new List<NpcPoint>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<CampPiece>();

        foreach (CampPiece seed in Around(anchor.Position, _options.SeedRadiusMetres, cost))
        {
            cost.Checks++;
            if (seed.Position.HorizontalDistanceTo(anchor.Position) > _options.SeedRadiusMetres)
            {
                continue;
            }

            if (members.Count >= _options.MaxMembers)
            {
                cost.Truncated = true;
                break;
            }

            if (taken.Add(seed.Key))
            {
                members.Add(seed.Position);
                frontier.Enqueue(seed);
            }
        }

        while (frontier.Count > 0)
        {
            if (members.Count >= _options.MaxMembers)
            {
                cost.Truncated = true;
                break;
            }

            CampPiece from = frontier.Dequeue();
            foreach (CampPiece candidate in Around(from.Position, _options.LinkDistanceMetres, cost))
            {
                if (taken.Contains(candidate.Key))
                {
                    continue;
                }

                cost.Checks++;
                if (from.Position.HorizontalDistanceTo(candidate.Position) > _options.LinkDistanceMetres)
                {
                    continue;
                }

                if (members.Count >= _options.MaxMembers)
                {
                    cost.Truncated = true;
                    break;
                }

                taken.Add(candidate.Key);
                members.Add(candidate.Position);
                frontier.Enqueue(candidate);
            }
        }

        return members;
    }

    /// <summary>Every known piece filed in a cell that a circle of
    /// <paramref name="radius"/> around <paramref name="centre"/> touches.
    ///
    /// This is the whole performance argument: cells nothing reaches are never
    /// opened, and pieces in them are never looked at.</summary>
    private IEnumerable<CampPiece> Around(NpcPoint centre, float radius, Counters cost)
    {
        float cell = CellSize;
        int minX = (int)Math.Floor((centre.X - radius) / cell);
        int maxX = (int)Math.Floor((centre.X + radius) / cell);
        int minZ = (int)Math.Floor((centre.Z - radius) / cell);
        int maxZ = (int)Math.Floor((centre.Z + radius) / cell);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!_cells.TryGetValue(new CellKey(x, z), out List<string>? keys))
                {
                    continue;
                }

                cost.Cells++;
                foreach (string key in keys!)
                {
                    cost.Considered++;
                    yield return _pieces[key];
                }
            }
        }
    }

    private float CellSize => _options.LinkDistanceMetres;

    private void File(CampPiece piece)
    {
        CellKey key = CellOf(piece.Position);
        if (!_cells.TryGetValue(key, out List<string>? keys))
        {
            keys = new List<string>(4);
            _cells.Add(key, keys);
        }

        keys!.Add(piece.Key);
    }

    private void Unfile(CampPiece piece)
    {
        CellKey key = CellOf(piece.Position);
        if (!_cells.TryGetValue(key, out List<string>? keys))
        {
            return;
        }

        keys!.Remove(piece.Key);
        if (keys.Count == 0)
        {
            // An empty cell is removed rather than kept, so a camp that moves
            // does not leave a trail of cells for every later survey to open.
            _cells.Remove(key);
        }
    }

    private CellKey CellOf(NpcPoint position) =>
        new CellKey((int)Math.Floor(position.X / CellSize), (int)Math.Floor(position.Z / CellSize));

    private sealed class Counters
    {
        internal int Cells;
        internal int Considered;
        internal int Checks;
        internal bool Truncated;
    }

    private readonly struct CellKey : IEquatable<CellKey>
    {
        private readonly int _x;
        private readonly int _z;

        internal CellKey(int x, int z)
        {
            _x = x;
            _z = z;
        }

        public bool Equals(CellKey other) => _x == other._x && _z == other._z;

        public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

        public override int GetHashCode() => unchecked((_x * 397) ^ _z);
    }
}
