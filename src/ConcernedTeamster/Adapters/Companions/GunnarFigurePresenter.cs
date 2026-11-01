using System;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Body.Dressing;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>All construction/posing/removal goes through the existing library.
/// No direct engine write, component surgery or body census is added here.</summary>
internal sealed class GunnarFigurePresenter
{
    private readonly GunnarBodyLeases _leases;
    private readonly Action<string> _log;
    private NpcFigureDresser? _dresser;
    public GunnarFigurePresenter(GunnarBodyLeases leases, Action<string> log) { _leases=leases; _log=log; }
    public string LastRefusal { get; private set; } = string.Empty;
    public bool IsShown => _dresser != null && _leases.HoldsPresentation;
    public void Refresh(in GunnarBodyFacts facts, GameObject? verifiedPlayerPrefab,
        WorldPoint spot, float yaw, NpcLookSpec look, string displayName, Action talk)
    {
        if (GunnarBodyPlan.Decide(facts) != GunnarBodyKind.Presentation)
        { Hide(); return; }
        if (!_leases.TakePresentation()) { Hide(); return; }
        if (_dresser == null)
        {
            NpcPresentationFigure figure = NpcPresentationBody.TryExtract(verifiedPlayerPrefab,
                "installed player rig", "CT_GunnarFigure", "CT_GunnarFigure_Source", string.Empty, _log);
            if (!figure.IsBuilt) { LastRefusal=figure.Refusal; Hide(); return; }
            _dresser = new NpcFigureDresser(figure, look, "CT_GunnarLook_Source", _log);
            try { _dresser.Apply(); _dresser.EnableTalk(displayName, "Talk", talk, _log); }
            catch { Hide(); throw; }
        }
        _dresser.StandAt(new Vector3(spot.X,spot.Y,spot.Z), Quaternion.Euler(0,yaw,0));
        _dresser.VerifyOnce();
    }
    public void Hide() => _leases.ReleasePresentation(RemoveFigure);
    public bool HandOverToWorker(Func<bool> claimExistingWorker) =>
        _leases.HandOverToExistingWorker(RemoveFigure, claimExistingWorker);
    private void RemoveFigure() { _dresser?.TakeDown(); _dresser=null; }
}
