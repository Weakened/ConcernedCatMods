using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>Which pieces the climb is offered on (CF-LAD-004, LADDERS.md L2).
///
/// This is the object behind <c>LadderSurvey.Admits</c>. It reads three numbers
/// off the piece standing in the world — how tall it is, how wide, how deep —
/// and hands them to <see cref="LadderAdmission"/>, which has no Unity in it
/// and holds every rule. The list of pieces is data in that class, not branches
/// here, so adding one after an in-game audit is a string.
///
/// <b>Cost.</b> The survey calls this for every collider it finds near the
/// player, four times a second, so it must be nearly free. It is: the verdict
/// is worked out once per prefab and remembered by name, with an instance-id
/// map in front of it so a repeat call is one dictionary lookup and no
/// allocation at all. A tower of ladders is measured once and never again.
///
/// <b>It never throws.</b> The survey runs inside the climb's own try/catch, so
/// a throw here would end somebody's climb. An unknown name, a piece with no
/// art, a piece whose bounds are not finite: each one is an answer, not an
/// exception, and anything genuinely unexpected refuses that piece and says so
/// once. A refused ladder still teleports, which is vanilla, which is safe.</summary>
internal sealed class LadderPieces
{
    /// <summary>How many instances to remember before starting again. Far more
    /// than any base has ladders; it exists only so that a session which loads
    /// world after world cannot grow this map forever.</summary>
    private const int MostInstancesRemembered = 512;

    private readonly Action<string> _log;
    private readonly Dictionary<int, bool> _byInstance = new Dictionary<int, bool>();
    private readonly Dictionary<string, bool> _byName = new Dictionary<string, bool>(StringComparer.Ordinal);

    private bool _saidItFailed;

    internal LadderPieces(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>How many distinct prefabs have been judged. For the handoff and
    /// for a test of the wiring; nothing depends on it.</summary>
    internal int Judged => _byName.Count;

    /// <summary>The predicate <c>LadderSurvey.Admits</c> wants: is this piece
    /// one a player may climb.</summary>
    internal bool Admits(Ladder ladder)
    {
        try
        {
            if (ladder == null)
            {
                return false;
            }

            int id = ladder.GetInstanceID();
            if (_byInstance.TryGetValue(id, out bool remembered))
            {
                return remembered;
            }

            bool admitted = Judge(ladder);
            if (_byInstance.Count >= MostInstancesRemembered)
            {
                // The verdicts themselves live in the by-name map, so this
                // costs one re-lookup per piece and nothing else.
                _byInstance.Clear();
            }

            _byInstance[id] = admitted;
            return admitted;
        }
        catch (Exception exception)
        {
            if (!_saidItFailed)
            {
                _saidItFailed = true;
                _log(
                    "Ladders: a piece could not be judged (" + SafeFailure.Brief(exception) +
                    "), so it keeps vanilla behaviour. This is said once.");
            }

            return false;
        }
    }

    /// <summary>Forgets every verdict. Nothing calls this today — the verdicts
    /// are per prefab, and a prefab means the same thing in the next world —
    /// but a world unload is where it would belong if a reason ever appears.</summary>
    internal void Forget()
    {
        _byInstance.Clear();
        _byName.Clear();
    }

    private bool Judge(Ladder ladder)
    {
        string key = KeyOf(ladder);
        if (_byName.TryGetValue(key, out bool known))
        {
            return known;
        }

        Piece piece = ladder.GetComponentInParent<Piece>();
        bool buildable = piece != null;
        Bounds bounds = WorldBounds(ladder.gameObject, out bool measured);

        float height = measured ? bounds.size.y : float.NaN;
        float widest = measured ? Mathf.Max(bounds.size.x, bounds.size.z) : float.NaN;
        float thinnest = measured ? Mathf.Min(bounds.size.x, bounds.size.z) : float.NaN;

        // Both names are offered to the known list: on a buildable piece the
        // Ladder component may sit on the root or on a child, and only the
        // loaded game knows which.
        string ladderName = LadderAdmission.Clean(ladder.gameObject.name);
        string pieceName = piece != null ? LadderAdmission.Clean(piece.gameObject.name) : string.Empty;
        string named = LadderAdmission.IsKnown(pieceName) ? pieceName : ladderName;

        LadderVerdict verdict = LadderAdmission.Decide(named, buildable, height, widest, thinnest);
        bool admits = verdict.Admits();

        _log(
            "Ladders: " + key + " is " + (admits ? "climbable" : "left to vanilla") +
            " - " + LadderAdmission.Explain(verdict) +
            (measured
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    " (measured {0:0.##} m tall, {1:0.##} m wide, {2:0.##} m deep{3})",
                    height,
                    widest,
                    thinnest,
                    buildable ? ", buildable" : ", not buildable")
                : " (nothing to measure on it)") +
            ".");

        if (verdict.WorthRemembering())
        {
            _byName[key] = admits;
        }

        return admits;
    }

    /// <summary>What this piece is called, for the cache and for the log. The
    /// piece root and the object carrying the <c>Ladder</c> are both in it,
    /// because a ladder on a ship and a ladder in a wall can both be called
    /// "ladder" under two different parents.</summary>
    private static string KeyOf(Ladder ladder)
    {
        string own = LadderAdmission.Clean(ladder.gameObject.name);
        Transform? root = ladder.transform.root;
        string outer = root == null ? string.Empty : LadderAdmission.Clean(root.gameObject.name);
        if (outer.Length == 0 || string.Equals(outer, own, StringComparison.Ordinal))
        {
            return own.Length == 0 ? "an unnamed piece" : own;
        }

        return outer + "/" + own;
    }

    /// <summary>The piece's own world bounds, from its art.
    ///
    /// Mesh renderers only: a particle or trail renderer on a piece would
    /// report a volume that has nothing to do with the ladder. Colliders are
    /// the fallback for a piece whose art is not a mesh, and a piece with
    /// neither cannot be measured — which is an answer
    /// (<see cref="LadderVerdict.NotMeasured"/>), not a refusal, so it is asked
    /// again next time.</summary>
    private static Bounds WorldBounds(GameObject root, out bool measured)
    {
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        measured = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
            {
                continue;
            }

            Add(ref bounds, ref measured, renderer.bounds);
        }

        if (measured)
        {
            return bounds;
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>())
        {
            if (collider == null || collider.isTrigger)
            {
                continue;
            }

            Add(ref bounds, ref measured, collider.bounds);
        }

        return bounds;
    }

    private static void Add(ref Bounds bounds, ref bool measured, Bounds next)
    {
        if (!measured)
        {
            bounds = next;
            measured = true;
            return;
        }

        bounds.Encapsulate(next);
    }
}
