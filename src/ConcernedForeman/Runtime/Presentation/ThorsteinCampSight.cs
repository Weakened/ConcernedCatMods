using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>What Thorstein knows of camp and of the doors near home (#295 S5):
/// one Concerned NPC <see cref="NpcCamp"/> for this world, fed from the pieces
/// loaded around home, and the doorways among them.
///
/// <b>It reads and changes nothing.</b> One <c>Piece.GetAllPiecesInRadius</c>
/// over the game's own list of loaded pieces, each time he looks for a spot (at
/// most every five seconds, normally every thirty), and reads of the pieces it
/// returns. No door is used, opened or closed, no piece is claimed, and nothing
/// is written anywhere.
///
/// <b>Fails closed.</b> A look that could not be completed answers false, and
/// the caller seats him nowhere new on it, because a door list that might be
/// missing a door cannot be trusted to keep him on the right side of one.
/// </summary>
internal sealed class ThorsteinCampSight
{
    /// <summary>Camp's seed radius plus one link distance: every piece that
    /// could be part of camp around home.</summary>
    private const float LookMetres = 32f;

    /// <summary>Doors farther from home than this cannot stand between home
    /// and a spot in his band (6 m out), whatever their width.</summary>
    private const float DoorMetres = 9f;

    /// <summary>A door's origin sits at the middle of its opening, about a
    /// metre above the floor (wood_door's snap points are at plus and minus 1 m).
    /// </summary>
    private const float DoorOriginAboveFloorMetres = 1f;

    private const float DoorHalfWidthMetres = 1f;

    private readonly NpcCamp _camp = new NpcCamp();
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly HashSet<string> _noted = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _gone = new List<string>();
    private readonly List<DoorPortal> _doors = new List<DoorPortal>();
    private readonly Action<string> _log;
    private bool _faultSaid;

    internal ThorsteinCampSight(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The doorways near home as of the last look. Every one counts as
    /// a door companions may not use: Foreman keeps no list of allowed doors.
    /// </summary>
    internal IReadOnlyList<DoorPortal> Doors => _doors;

    /// <summary>How many pieces camp is built from, for status.</summary>
    internal int Known => _camp.Known;

    /// <summary>Looks around <paramref name="home"/> and surveys camp from it.
    /// False when the look failed; <paramref name="camp"/> is then still the
    /// last survey, but the door list is empty and must not be trusted.</summary>
    internal bool TryLook(Vector3 home, NpcCampHome kind, NpcWorldEpoch epoch, out NpcCampView camp)
    {
        if (!_camp.Epoch.Equals(epoch))
        {
            // A new world: every name known was a name in the old one.
            _camp.BeginWorld(epoch);
            _noted.Clear();
        }

        _doors.Clear();
        _seen.Clear();
        bool read = true;
        try
        {
            _pieces.Clear();
            Piece.GetAllPiecesInRadius(home, LookMetres, _pieces);
            for (int index = 0; index < _pieces.Count; index++)
            {
                Piece piece = _pieces[index];
                if (piece == null)
                {
                    continue;
                }

                Note(piece, home, epoch);
            }

            foreach (string key in _noted)
            {
                if (!_seen.Contains(key))
                {
                    _gone.Add(key);
                }
            }

            for (int index = 0; index < _gone.Count; index++)
            {
                _camp.ForgetPiece(_gone[index]);
                _noted.Remove(_gone[index]);
            }
        }
        catch (Exception exception)
        {
            read = false;
            _doors.Clear();
            if (!_faultSaid)
            {
                _faultSaid = true;
                _log("Thorstein could not look around home for camp and doors; he is seated nowhere new until " +
                    "he can: " + SafeFailure.Brief(exception));
            }
        }
        finally
        {
            _gone.Clear();
            _pieces.Clear();
        }

        camp = _camp.Survey(kind, new NpcPoint(home.x, home.y, home.z));
        return read;
    }

    /// <summary>Forgets this world's camp.</summary>
    internal void Forget()
    {
        _camp.BeginWorld(NpcWorldEpoch.Unknown);
        _noted.Clear();
        _seen.Clear();
        _doors.Clear();
    }

    private void Note(Piece piece, Vector3 home, NpcWorldEpoch epoch)
    {
        Vector3 at = piece.transform.position;
        Door? door = piece.GetComponentInChildren<Door>();
        if (door != null && Vector3.Distance(door.transform.position, home) <= DoorMetres)
        {
            _doors.Add(PortalOf(door));
        }

        string? key = KeyOf(piece);
        if (key == null)
        {
            return;
        }

        if (_camp.NotePiece(key, new NpcPoint(at.x, at.y, at.z), IsStructure(piece), epoch))
        {
            _noted.Add(key);
            _seen.Add(key);
        }
        else
        {
            _noted.Remove(key);
        }
    }

    /// <summary>A player-built structural piece: placed by a player, and
    /// neither a terrain tool's piece nor a crop. Everything else is refused at
    /// camp's door.</summary>
    private static bool IsStructure(Piece piece) =>
        piece.IsPlacedByPlayer()
        && piece.GetComponent<TerrainOp>() == null
        && piece.GetComponent<Plant>() == null;

    /// <summary>The piece's network id for this world load, or null when it
    /// has none. Camp keys are epoch-scoped, so a renumbering at the next load
    /// is expected rather than a problem.</summary>
    private static string? KeyOf(Piece piece)
    {
        ZNetView? view = piece.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        return zdo == null ? null : zdo.m_uid.ToString();
    }

    private static DoorPortal PortalOf(Door door)
    {
        Vector3 centre = door.transform.position;
        Vector3 across = door.transform.forward;
        across.y = 0f;
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
