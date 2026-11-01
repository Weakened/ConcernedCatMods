using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedNPC.Body.Keepsakes;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Only ConcernedNPC builds/removes objects. The lead calls Refresh
/// from the presentation cadence, and Forget before scope teardown. No timer,
/// nag, world scan, worker operation or inventory operation lives here.</summary>
internal sealed class GunnarKeepsakePresenter
{
    private readonly GunnarCompanionAdapter _companion;
    private readonly Func<bool?> _workerExists;
    private readonly Action<string> _notice;
    private readonly Action _beginIntroduction;
    private NpcKeepsake? _keepsake;
    private CompanionScope _scope;
    private WorldPoint _spot;
    private bool _examining;
    public string LastRefusal { get; private set; } = string.Empty;

    public GunnarKeepsakePresenter(GunnarCompanionAdapter companion, Func<bool?> workerExists,
        Action<string> notice, Action beginIntroduction)
    {
        _companion = companion; _workerExists = workerExists;
        _notice = notice; _beginIntroduction = beginIntroduction;
    }

    public void Refresh(CompanionAnchor verifiedAnchor, IPlacementProbe probe,
        IReadOnlyList<WorldPoint>? completeMementoSnapshot)
    {
        GunnarCompanionRecord? record = _companion.Current();
        if (record == null || !GunnarKeepsake.Eligible(record, _companion.Enabled.Value, _workerExists()))
        { Forget(); return; }
        PlacementResult placement = GunnarKeepsake.Plan(verifiedAnchor, probe, completeMementoSnapshot);
        if (!placement.Found) { Forget(); return; }
        if (_keepsake != null && _keepsake.IsPlaced && _scope.Equals(record.Scope) &&
            _spot.Equals(placement.Position)) return;
        Forget();
        _scope = record.Scope; _spot = placement.Position;
        _keepsake = NpcKeepsake.TryPlace(GunnarKeepsake.Spec,
            new Vector3(_spot.X, _spot.Y + 0.05f, _spot.Z), Quaternion.identity,
            "CT_GunnarBuckle", "CT_GunnarBuckle_Source", Examine, _notice, out string refusal);
        LastRefusal = refusal;
    }

    private void Examine()
    {
        if (_examining || _keepsake == null || !_keepsake.IsPlaced) return;
        _examining = true;
        try
        {
            GunnarCompanionRecord? record = _companion.Current();
            if (record == null || !_scope.Equals(record.Scope)) { Forget(); return; }
            GunnarPickupResult result = GunnarKeepsake.Examine(record, _companion.Enabled.Value, _workerExists());
            if (result == GunnarPickupResult.NotRecorded)
            { _notice(record.Notice ?? "The buckle was not recorded. Please try again after fixing companion storage."); return; }
            if (result != GunnarPickupResult.Collected) return;
            Forget(); // only after Collected is on disk
            _notice(GunnarKeepsake.Inscription);
            _beginIntroduction();
        }
        finally { _examining = false; }
    }

    public void Forget()
    {
        _keepsake?.Remove(); _keepsake = null; _scope = default;
        LastRefusal = string.Empty;
    }
}
