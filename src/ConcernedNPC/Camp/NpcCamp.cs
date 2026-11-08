using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>What a role says camp is measured from this pass. The values are
/// the library's own anchor kinds, in the owner's order: the player's bed, else
/// the settlement, else the world's start until there is a home.</summary>
public enum NpcCampHome
{
    /// <summary>No home is known. Camp is unknown and nothing is surveyed.
    /// </summary>
    Unknown = 0,

    /// <summary>The player's current respawn or home bed.</summary>
    PlayerBed = 1,

    /// <summary>The settlement's established anchor.</summary>
    Settlement = 2,

    /// <summary>The world's start, a fallback before a residence exists.
    /// </summary>
    WorldSpawn = 3,
}

/// <summary>The public door to one cached camp (#461, #295 S5): one per role,
/// per character, per world. A role tells it what was built and what is gone,
/// and asks where camp is.
///
/// <b>What it never does.</b> It never scans the world, names a prefab, reads
/// or writes a file, or touches a game object. Everything it knows a role told
/// it, and the role decides which world objects are player-built structures.
/// It only counts.
///
/// <b>What it costs.</b> A survey that finds nothing changed hands back the
/// same <see cref="NpcCampView"/> it handed back last time and allocates
/// nothing, so a role may ask on every pass. Only a changed piece or a moved
/// home recomputes, and that work is bounded by the registry's own survey
/// budget.
///
/// <b>Why a facade and not the registry.</b> The registry, the snapshot, the
/// piece and the anchor stay internal. Publishing them would publish the
/// membership rule, the cell grid and the anchor tolerance as an API. This
/// type takes plain values and gives back a read-only view.</summary>
public sealed class NpcCamp
{
    private readonly CampRegistry _registry = new CampRegistry(null);
    private CampSnapshot? _surveyed;
    private NpcCampView? _view;

    /// <summary>The world load the known pieces are named in. Unknown until
    /// <see cref="BeginWorld"/>, and every piece is refused until then.</summary>
    public NpcWorldEpoch Epoch => _registry.Epoch;

    /// <summary>How many player-built pieces are known. Not how many are in
    /// camp: a known piece may be connected to nothing.</summary>
    public int Known => _registry.Known;

    /// <summary>A world has been loaded. Everything known is forgotten, because
    /// every name it was keyed by now names something else. The same epoch
    /// twice is a no-op. Returns how many pieces were dropped.</summary>
    public int BeginWorld(NpcWorldEpoch epoch) => _registry.BeginWorldLoad(epoch);

    /// <summary>A world object exists, as the role sees it. Only a
    /// <paramref name="playerBuiltStructure"/> is camp. Anything else (a
    /// terrain edit, a tree, something nobody classified) is refused, and a
    /// piece already known under that key is let go of. A piece named in
    /// another world load, or with no key or no real position, is refused.
    /// Idempotent by key.</summary>
    /// <returns>True when the piece is now part of what camp is built from.
    /// </returns>
    public bool NotePiece(string key, NpcPoint position, bool playerBuiltStructure, NpcWorldEpoch epoch)
    {
        CampPieceOutcome outcome = _registry.Note(new CampPiece(
            key, position, playerBuiltStructure ? CampPieceClass.Built : CampPieceClass.Unknown, epoch));
        return outcome == CampPieceOutcome.Admitted
            || outcome == CampPieceOutcome.AlreadyKnown
            || outcome == CampPieceOutcome.Moved;
    }

    /// <summary>A piece is gone. False when it was not known, which is an
    /// ordinary answer.</summary>
    public bool ForgetPiece(string key) => _registry.Forget(key);

    /// <summary>Camp, as of now, measured from <paramref name="home"/> at
    /// <paramref name="position"/>. An unknown home, or a position that is not
    /// a real point, is no camp at all: the view is not established and
    /// contains nothing.
    ///
    /// The same view object comes back until camp changes, so a role can
    /// compare <see cref="NpcCampView.Revision"/> rather than re-plan.</summary>
    public NpcCampView Survey(NpcCampHome home, NpcPoint position)
    {
        CampAnchor anchor = new CampAnchor(KindOf(home), position);
        CampSnapshot current = _registry.Survey(anchor);
        if (_view == null || !ReferenceEquals(current, _surveyed))
        {
            _surveyed = current;
            _view = new NpcCampView(current);
        }

        return _view;
    }

    private static CampAnchorKind KindOf(NpcCampHome home) => home switch
    {
        NpcCampHome.PlayerBed => CampAnchorKind.PlayerBed,
        NpcCampHome.Settlement => CampAnchorKind.Settlement,
        NpcCampHome.WorldSpawn => CampAnchorKind.WorldSpawn,
        _ => CampAnchorKind.None,
    };
}

/// <summary>Where camp is, as of one survey. Immutable and read-only: a role
/// may hold one as long as it likes. There is no list of pieces on purpose, so
/// no role can re-decide membership for itself.</summary>
public sealed class NpcCampView
{
    private readonly CampSnapshot _snapshot;

    internal NpcCampView(CampSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    /// <summary>True when there is a home and at least one player-built piece:
    /// camp is a place, not just a point.</summary>
    public bool IsEstablished => _snapshot.IsEstablished;

    /// <summary>True when camp is measured from the world's start because no
    /// residence exists yet.</summary>
    public bool IsProvisional => _snapshot.Anchor.IsProvisional;

    /// <summary>What camp was measured from.</summary>
    public NpcCampHome Home => _snapshot.Anchor.Kind switch
    {
        CampAnchorKind.PlayerBed => NpcCampHome.PlayerBed,
        CampAnchorKind.Settlement => NpcCampHome.Settlement,
        CampAnchorKind.WorldSpawn => NpcCampHome.WorldSpawn,
        _ => NpcCampHome.Unknown,
    };

    /// <summary>Increases whenever camp changed. A plan made against another
    /// revision is out of date.</summary>
    public int Revision => _snapshot.Revision;

    /// <summary>How many player-built pieces this camp is made of.</summary>
    public int Members => _snapshot.Members;

    /// <summary>The world load this camp was surveyed in.</summary>
    public NpcWorldEpoch Epoch => _snapshot.Epoch;

    /// <summary>The mean of the member positions, or home when camp has no
    /// members yet.</summary>
    public NpcPoint Centre => _snapshot.Centre;

    /// <summary>The furthest member from <see cref="Centre"/>, plus the margin.
    /// </summary>
    public float ExtentMetres => _snapshot.ExtentMetres;

    /// <summary>Whether a point is inside camp. False when there is no home.
    /// </summary>
    public bool Contains(NpcPoint point) => _snapshot.Contains(point);

    /// <summary>The nearest point inside camp to <paramref name="from"/>, away
    /// from the edge. False when there is no camp at all.</summary>
    public bool TryNearestInteriorPoint(NpcPoint from, out NpcPoint interior) =>
        _snapshot.TryNearestInteriorPoint(from, out interior);

    public override string ToString() => _snapshot.ToString();
}
