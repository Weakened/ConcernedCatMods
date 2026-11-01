using TheConcernedCat.ConcernedNPC.Work;
namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>Additive public view of the existing cached Camp registry. A role
/// supplies verified structural-piece events and its verified respawn anchor;
/// this facade never scans the world, names a prefab or writes a file.</summary>
public sealed class NpcCamp
{
    private readonly CampRegistry _registry=new(null);
    private CampSnapshot? _last;
    private NpcCampView? _view;
    public void BeginWorld(NpcWorldEpoch epoch) { _registry.BeginWorldLoad(epoch); _last=null; _view=null; }
    public bool ObservePiece(string key,NpcPoint position,bool playerBuiltStructural,NpcWorldEpoch epoch)
    {
        CampPieceOutcome result=_registry.Note(new CampPiece(key,position,
            playerBuiltStructural ? CampPieceClass.Built : CampPieceClass.Unknown,epoch));
        return result==CampPieceOutcome.Admitted || result==CampPieceOutcome.AlreadyKnown || result==CampPieceOutcome.Moved;
    }
    public bool ForgetPiece(string key) => _registry.Forget(key);
    public NpcCampView Survey(NpcPoint verifiedRespawnAnchor,bool anchorKnown,bool claimedBed)
    {
        CampSnapshot current=_registry.Survey(anchorKnown
            ? new CampAnchor(claimedBed ? CampAnchorKind.PlayerBed : CampAnchorKind.WorldSpawn,verifiedRespawnAnchor)
            : CampAnchor.None);
        if (!ReferenceEquals(current,_last)) { _last=current; _view=new NpcCampView(current); }
        return _view!;
    }
}
public sealed class NpcCampView
{
    private readonly CampSnapshot _snapshot;
    internal NpcCampView(CampSnapshot snapshot) { _snapshot=snapshot; }
    public bool IsEstablished => _snapshot.IsEstablished;
    public int Revision => _snapshot.Revision;
    public int Members => _snapshot.Members;
    public NpcWorldEpoch Epoch => _snapshot.Epoch;
    public NpcPoint Centre => _snapshot.Centre;
    public bool Contains(NpcPoint point) => _snapshot.Contains(point);
    public bool TryNearestInteriorPoint(NpcPoint from,out NpcPoint interior) => _snapshot.TryNearestInteriorPoint(from,out interior);
}
