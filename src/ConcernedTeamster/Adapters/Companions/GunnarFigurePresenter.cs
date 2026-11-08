using System;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Body.Dressing;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Gunnar's presentation figure (#458): the only Teamster file that
/// builds, stands, dresses or takes down a figure, and it does each through
/// Concerned NPC. The validator's Teamster figure audit pins every one of those
/// calls here and refuses any direct engine write in this file - no
/// instantiation, destruction, re-parenting, component, collider, network view
/// or transform write of its own.
///
/// What the library builds is the game's own character prefab, extracted dark:
/// no network view, no character, no AI, no collider that blocks anybody,
/// nothing saved, every game script removed before it was ever enabled. Never a
/// live Player stripped afterwards. It changes nothing in the world and nobody
/// else sees it.
///
/// <b>Lease first, figure second; figure down, lease second.</b> A figure is
/// built only after its presentation lease is granted, and every path that
/// gives the lease up takes the figure down first
/// (<see cref="GunnarBodyLeases.ReleasePresentation"/>).</summary>
internal sealed class GunnarFigurePresenter
{
    private readonly GunnarBodyLeases _leases;
    private readonly Action<string> _log;
    private NpcFigureDresser? _dresser;
    private Vector3 _standing;

    public GunnarFigurePresenter(GunnarBodyLeases leases, Action<string> log)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Why the last build was refused, for the status line.</summary>
    public string LastRefusal { get; private set; } = string.Empty;

    /// <summary>A build was refused in this world; no second attempt is made
    /// until the next world, so a missing rig is one log line, not two a second.</summary>
    public bool BuildFailed { get; private set; }

    public bool IsShown => _dresser != null && _leases.HoldsPresentation;

    /// <summary>Where the figure stands, or null when none does.</summary>
    public Vector3? Standing => _dresser != null ? _standing : (Vector3?)null;

    public string Describe() => _dresser != null ? _dresser.Describe() : string.Empty;

    /// <summary>Shows him at <paramref name="spot"/>. The caller has already
    /// decided, from <see cref="GunnarBodyPlan"/>, that the figure is wanted.
    /// <paramref name="talk"/> is optional: with none, the figure is not
    /// interactive.</summary>
    public void Show(GameObject? source, Vector3 spot, Quaternion facing, NpcLookSpec look,
        string displayName, Action? talk)
    {
        if (!Finite(spot)) { Hide(); return; }

        // A lease the arbiter ended (a world change, a late callback) never
        // carries an old figure into a new claim.
        if (_dresser != null && !_leases.HoldsPresentation) RemoveFigure();
        if (!_leases.TakePresentation()) { Hide(); return; }

        if (_dresser != null)
        {
            if ((_standing - spot).sqrMagnitude > 0.0001f)
            {
                _dresser.StandAt(spot, facing);
                _standing = spot;
            }

            return;
        }

        if (BuildFailed) return;
        NpcPresentationFigure figure = NpcPresentationBody.TryExtract(
            source, "Player", "CT_GunnarFigure", "CT_GunnarFigure_Source", string.Empty, _log);
        if (!figure.IsBuilt)
        {
            BuildFailed = true;
            LastRefusal = figure.Refusal;
            _log("Gunnar could not be shown at home on this build: " + figure.Refusal +
                ". Every Teamster tool is unchanged.");
            Hide();
            return;
        }

        LastRefusal = string.Empty;
        _dresser = new NpcFigureDresser(figure, look, "CT_GunnarFigure_Look", _log);
        _dresser.StandAt(spot, facing);
        _standing = spot;
        _dresser.Apply();
        if (talk != null) _dresser.EnableTalk(displayName, "Talk", talk, _log);
    }

    /// <summary>Measures the dressed pieces once, after the figure was drawn.</summary>
    public void VerifyOnce() => _dresser?.VerifyOnce();

    /// <summary>Figure down, then its lease. Idempotent.</summary>
    public void Hide() => _leases.ReleasePresentation(RemoveFigure);

    /// <summary>The hand-over an order makes before his worker body is placed:
    /// figure down, presentation lease released, worker lease claimed.
    /// <paramref name="standing"/> is where the figure stood, so the body can
    /// appear where he was.</summary>
    public bool HandOverToWorker(out Vector3? standing, out string refusal)
    {
        standing = Standing;
        return _leases.TryTakeWorker(RemoveFigure, out refusal);
    }

    /// <summary>A new world: a rig that failed to build may be tried again.</summary>
    public void ResetForWorld()
    {
        BuildFailed = false;
        LastRefusal = string.Empty;
    }

    private void RemoveFigure()
    {
        // Taken down before it is forgotten: if the take-down throws, the
        // dresser is kept for the next attempt and the lease is not released.
        _dresser?.TakeDown();
        _dresser = null;
    }

    private static bool Finite(Vector3 p) =>
        !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) ||
          float.IsNaN(p.z) || float.IsInfinity(p.z));
}
