using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.Diagnostics;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>What Gunnar's figure knows of camp and of the doors near home
/// (#461): one Concerned NPC <see cref="NpcCamp"/> for this world, fed from the
/// player-built pieces loaded around home, the doorways among them, and the two
/// physics questions the camp routine asks of a spot.
///
/// <b>It reads and changes nothing.</b> One <c>Piece.GetAllPiecesInRadius</c>
/// over the game's own list of loaded pieces, each time the presentation looks
/// (when home moves, and otherwise at most every
/// <see cref="GunnarPresentation"/> look interval), and reads of the pieces it
/// returns. A spot is asked about with one bounded ray cast and one capsule
/// check into preallocated buffers. No door is used, opened or closed, no piece
/// is claimed, nothing is networked, and nothing is written anywhere.
///
/// <b>Which pieces are camp</b> is <see cref="GunnarRestBounds.IsCampPiece"/>:
/// placed by a player, and neither a terrain tool's piece nor a crop.
///
/// <b>Fails closed.</b> A look that could not be completed answers false, and
/// the routine then shows him nowhere new, because a door list that might be
/// missing a door cannot be trusted to keep him on the right side of one. A
/// wall check that cannot be asked counts as a wall; a room check that cannot
/// be asked counts as no room.</summary>
internal sealed class GunnarCampSight : IGunnarRestSurroundings
{
    /// <summary>Camp's seed radius plus one link distance: every piece that
    /// could be part of camp around home.</summary>
    private const float LookMetres = 32f;

    /// <summary>His spot is at most 4 m from home and a doorway's opening is
    /// about 2 m wide, so a door whose middle is farther than this cannot stand
    /// between home and his spot. Generous on purpose.</summary>
    private const float DoorMetres = 9f;

    /// <summary>A door's origin sits at the middle of its opening, about a
    /// metre above the floor (wood_door's snap points are at plus and minus 1 m).
    /// </summary>
    private const float DoorOriginAboveFloorMetres = 1f;

    private const float DoorHalfWidthMetres = 1f;

    /// <summary>Above a table, a chest or a fire, below a wall's top: the
    /// height the wall check looks along.</summary>
    private const float SightHeight = 1.4f;

    private readonly GunnarKeepsakeEnvironment _environment;
    private readonly Action<string> _log;
    private readonly NpcCamp _camp = new NpcCamp();
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly HashSet<string> _noted = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _everyPiece = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _everyPieceNow = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _gone = new List<string>();
    private readonly List<DoorPortal> _doors = new List<DoorPortal>();
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private int _solidMask;
    private int _lastDoorCount = -1;
    private bool _faultSaid;

    internal GunnarCampSight(GunnarKeepsakeEnvironment environment, Action<string> log)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc/>
    public int Revision { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<DoorPortal> Doors => _doors;

    /// <summary>How many pieces camp is built from, for status.</summary>
    internal int Known => _camp.Known;

    /// <summary>Looks around <paramref name="home"/> and surveys camp from it.
    /// False when the look failed; <paramref name="camp"/> is then still the
    /// last survey, but the door list is empty and must not be trusted.</summary>
    internal bool TryLook(CompanionAnchor home, NpcWorldEpoch epoch, out NpcCampView camp)
    {
        if (!_camp.Epoch.Equals(epoch))
        {
            // A new world: every name known was a name in the old one.
            _camp.BeginWorld(epoch);
            _noted.Clear();
            _everyPiece.Clear();
            Revision++;
        }

        var at = new Vector3(home.Position.X, home.Position.Y, home.Position.Z);
        _doors.Clear();
        _seen.Clear();
        _everyPieceNow.Clear();
        bool read = true;
        try
        {
            _pieces.Clear();
            Piece.GetAllPiecesInRadius(at, LookMetres, _pieces);
            for (int index = 0; index < _pieces.Count; index++)
            {
                Piece piece = _pieces[index];
                if (piece == null) continue;
                Note(piece, at, epoch);
            }

            foreach (string key in _noted)
                if (!_seen.Contains(key)) _gone.Add(key);
            for (int index = 0; index < _gone.Count; index++)
            {
                _camp.ForgetPiece(_gone[index]);
                _noted.Remove(_gone[index]);
            }

            if (_doors.Count != _lastDoorCount || !_everyPieceNow.SetEquals(_everyPiece))
            {
                // Something was built or taken down near home: a spot chosen
                // before it is asked again.
                Revision++;
                _lastDoorCount = _doors.Count;
                _everyPiece.Clear();
                _everyPiece.UnionWith(_everyPieceNow);
            }
        }
        catch (Exception exception)
        {
            read = false;
            _doors.Clear();
            _lastDoorCount = -1;
            if (!_faultSaid)
            {
                _faultSaid = true;
                _log("Gunnar could not look around home for camp and doors; he is shown nowhere new until he can: " +
                    SafeFailure.Brief(exception));
            }
        }
        finally
        {
            _gone.Clear();
            _pieces.Clear();
            _everyPieceNow.Clear();
        }

        camp = _camp.Survey(home.Kind == AnchorKind.ClaimedBed ? NpcCampHome.PlayerBed : NpcCampHome.WorldSpawn,
            new NpcPoint(home.Position.X, home.Position.Y, home.Position.Z));
        return read;
    }

    /// <summary>Forgets this world's camp.</summary>
    internal void Forget()
    {
        _camp.BeginWorld(NpcWorldEpoch.Unknown);
        _faultSaid = false;
        _noted.Clear();
        _seen.Clear();
        _everyPiece.Clear();
        _everyPieceNow.Clear();
        _doors.Clear();
        _lastDoorCount = -1;
        Revision++;
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorldPoint>? OtherKeepsakes(CompanionAnchor anchor) => _environment.OtherMementos(anchor);

    /// <inheritdoc/>
    public bool IsWallBetween(WorldPoint home, WorldPoint spot)
    {
        try
        {
            if (_solidMask == 0) _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            if (_solidMask == 0) return true;
            Vector3 from = new Vector3(home.X, home.Y, home.Z) + (Vector3.up * SightHeight);
            Vector3 to = new Vector3(spot.X, spot.Y, spot.Z) + (Vector3.up * SightHeight);
            Vector3 along = to - from;
            float distance = along.magnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return true;
            if (distance < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(from, along / distance, _hits, distance, _solidMask,
                QueryTriggerInteraction.Ignore);
            // A full buffer may have dropped the wall that matters.
            if (count >= _hits.Length) return true;
            for (int index = 0; index < count; index++)
            {
                Collider hit = _hits[index].collider;
                // The home bed itself and loose physics objects do not count.
                if (hit == null || hit.attachedRigidbody != null || hit.GetComponentInParent<Bed>() != null) continue;
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <inheritdoc/>
    public bool HasRoomToStand(WorldPoint spot)
    {
        try
        {
            if (_solidMask == 0) _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            var at = new Vector3(spot.X, spot.Y, spot.Z);
            return _solidMask != 0 && !Physics.CheckCapsule(at + Vector3.up * 0.6f, at + Vector3.up * 1.7f, 0.3f,
                _solidMask, QueryTriggerInteraction.Ignore);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Note(Piece piece, Vector3 home, NpcWorldEpoch epoch)
    {
        Vector3 at = piece.transform.position;
        Door? door = piece.GetComponentInChildren<Door>();
        if (door != null && Vector3.Distance(door.transform.position, home) <= DoorMetres)
            _doors.Add(PortalOf(door));

        string? key = KeyOf(piece);
        if (key == null) return;
        _everyPieceNow.Add(key);
        bool campPiece = GunnarRestBounds.IsCampPiece(
            piece.IsPlacedByPlayer(), piece.GetComponent<TerrainOp>() != null, piece.GetComponent<Plant>() != null);
        if (_camp.NotePiece(key, new NpcPoint(at.x, at.y, at.z), campPiece, epoch))
        {
            _noted.Add(key);
            _seen.Add(key);
        }
        else
        {
            _noted.Remove(key);
        }
    }

    /// <summary>The piece's network id for this world load, or null when it
    /// has none. Camp keys are epoch-scoped, so a renumbering at the next load
    /// is expected rather than a problem.</summary>
    private static string? KeyOf(Piece piece)
    {
        ZNetView? view = piece.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        return zdo == null ? null : zdo.m_uid.ToString();
    }

    /// <summary>A doorway as the shared door rule reads it. Every one says it
    /// is not allowed: Teamster keeps no list of doors opened to companions.
    /// </summary>
    private static DoorPortal PortalOf(Door door)
    {
        Vector3 centre = door.transform.position;
        Vector3 across = door.transform.forward;
        float floor = centre.y - DoorOriginAboveFloorMetres;
        return new DoorPortal(
            new DoorPlace(new WorldPoint(centre.x, floor, centre.z), 0),
            new WorldPoint(centre.x, floor, centre.z),
            across.x,
            across.z,
            DoorHalfWidthMetres,
            isOpen: false,
            canOpen: false,
            allowed: false);
    }
}
