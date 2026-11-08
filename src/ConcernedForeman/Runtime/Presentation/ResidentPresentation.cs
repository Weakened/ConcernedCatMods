using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Ui;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Body.Dressing;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Housing;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Owns one LOCAL ordinary resident. No worker, job, inventory, RPC,
/// bed claim or world entity. The journal is separate from the runtime's
/// custody journal, so two independent in-memory writers cannot clobber it.</summary>
internal sealed class ResidentPresentation : IDisposable
{
    private sealed class Role : INpcRole
    {
        public NpcIdentity Identity => Id;
        public NpcBodyContract Body => NpcBodyContract.ForPresentation();
        public INpcDataPaths Paths { get; } = new ForemanDataPaths(Path.Combine(ForemanPaths.Root, "residents"));
    }
    private static readonly NpcIdentity Id = new NpcIdentity("foreman", "ordinary-resident");
    private const string Holder = "foreman/resident-presentation";
    private readonly Func<bool> _enabled;
    private readonly NpcRoleRegistry _registry;
    private readonly SettlementRuntime _settlement;
    private readonly Action<string> _log;
    private readonly ResidentPanel _panel;
    private readonly WorldHousing _housing;
    private readonly Role _role = new Role();
    private Guid _epoch;
    private JournalStore? _store;
    private ResidentAdmission? _resident;
    private long _world;
    private BodyLease? _lease;
    private NpcFigureDresser? _dresser;
    private ResidentPose? _pose;
    private float _sinceSurvey;
    private bool _registered, _faulted, _figureRefused, _away;
    private string _notice = "Reserve a usable bed within four metres, then confirm admission. No jobs are offered.";
    internal ResidentPresentation(Func<bool> enabled, NpcRoleRegistry registry, SettlementRuntime settlement, Action<string> log)
    {
        _enabled = enabled; _registry = registry; _settlement = settlement; _log = log;
        _panel = new ResidentPanel(Act, Fail);
        _housing = new WorldHousing(area => ZoneSystem.instance != null &&
            WorldDesignationSite.IsSurroundingsLoaded(ZoneSystem.instance, SitePoints.ToVector3(area.Centre), area.Radius), log);
    }
    private bool MayPresent => !_faulted && _enabled() && ZNet.instance != null && ZNet.instance.IsServer() &&
        !ZNet.instance.IsDedicated() && Player.m_localPlayer != null;
    internal void Tick(float deltaTime)
    {
        try
        {
            _panel.Tick(MayPresent);
            if (!MayPresent) { TakeDown(); return; }
            if (!Open()) { TakeDown(); return; }
            if (_lease != null && !_lease.IsActive) TakeDown();
            if (_resident != null && TryPoint(_resident.Slot, out var home) &&
                (ZoneSystem.instance == null || !ZoneSystem.instance.IsZoneLoaded(home) ||
                 (Player.m_localPlayer.transform.position - home).sqrMagnitude > 64f * 64f))
            {
                // Unload only the local visual, never the saved resident. Recorded
                // once on the way out, not every frame: Revalidate re-folds the
                // journal, which is an allocation per frame for as long as the
                // player is away, and nothing it reads changes until they return.
                TakeDown();
                if (!_away) { _away = true; RecordHousing(HousingRefusal.NotMeasured); }
                return;
            }
            _away = false;
            _dresser?.VerifyOnce();
            _pose?.Apply(ResidentRoutine.At(EnvMan.instance != null ? EnvMan.instance.GetDayFraction() : float.NaN,
                _resident != null && _resident.Phase == ResidentJournalPhase.Admitted,
                _resident != null && !_resident.HousingUnverified));
            _sinceSurvey += deltaTime;
            if (_sinceSurvey < 10f) return;
            _sinceSurvey = 0;
            if (_resident == null || _resident.Phase == ResidentJournalPhase.Unspecified) return;
            if (!TryArea(out var area)) { RecordHousing(HousingRefusal.NotMeasured); return; }
            if (TryPoint(_resident.Slot, out var reservedAt) && !area.Contains(SitePoints.ToSitePoint(reservedAt)))
            { RecordHousing(HousingRefusal.OutsideSettlement); return; }
            if (!_housing.TryResidentSlot(area, _resident.Slot!, out var slot, out bool exists))
            { RecordHousing(HousingRefusal.NotMeasured); return; }
            // Only a complete loaded survey can establish a removed bed.
            if (!RecordHousing(HousingRules.Judge(slot.Facts))) return;
            if (_resident.Phase != ResidentJournalPhase.Admitted || _resident.HousingUnverified) return;
            if (_dresser == null && !_figureRefused) Show(slot);
        }
        catch (Exception exception) { Fail(exception); }
    }
    private bool RecordHousing(HousingRefusal refusal)
    {
        if (_resident == null) return false;
        var result = _resident.Revalidate(refusal);
        if (refusal == HousingRefusal.None) return true;
        // A real refusal (homeless, outside the settlement) takes the figure
        // down: posed at a bed that is gone, it would stand frozen where the bed
        // was. "Cannot be checked" only pauses the routine and keeps it up.
        if (refusal != HousingRefusal.NotMeasured) TakeDown();
        _notice = refusal == HousingRefusal.NotMeasured
            ? "Housing cannot currently be checked. The resident keeps their reservation; the routine is paused."
            : result == ResidentAdmissionResult.NotRecorded
                ? "Housing changed but its record could not be saved. The resident stays; the routine is paused."
                : "The resident is homeless: " + HousingSentences.For(refusal) + ". Reserve a safe bed and confirm to rehome them.";
        return false;
    }
    private bool Open()
    {
        long world = ZNet.instance.GetWorldUID();
        if (world == 0 || _registry.CurrentWorld.IsUnknown) return false;
        if (_world != world) { Forget(); _world = world; _epoch = _registry.CurrentWorld.Value; }
        if (!_registered)
        {
            _registered = _registry.Register(_role).IsRegistered;
            if (!_registered) { _notice = "The resident identity could not be registered."; return false; }
        }
        if (_resident != null) return !_resident.IsBlocked;
        _store = new JournalStore(_role.Paths.Root);
        var loaded = _store.Load(new SettlementScope(world, new SettlementId("home")));
        _resident = new ResidentAdmission(loaded.Journal, journal => _store.Save(journal).Saved, _epoch);
        if (_resident.IsBlocked) { _notice = "The resident record needs review; it will not be overwritten."; return false; }
        return true;
    }
    private bool TryArea(out Designation area)
    {
        area = null!;
        if (!_settlement.TryReadDesignations(out var marks, out _)) return false;
        var marked = marks.Where(mark => mark.Kind == DesignationKind.SettlementArea).ToArray();
        if (marked.Length != 1) return false;
        area = marked[0]; return true;
    }
    private bool ReadSlots(out IReadOnlyList<ResidentHousingSlot> slots)
    {
        slots = Array.Empty<ResidentHousingSlot>();
        return TryArea(out var area) && _housing.TryResidentSlots(area, out slots);
    }
    private string Act(string action)
    {
        try
        {
            if (!MayPresent || !Open()) return "Enable Residents and the settlement runtime on the host of a loaded world. " + _notice;
            if (action == "status") return Status();
            if (!ReadSlots(out var slots)) return "The complete settlement housing survey is unavailable. Nothing was admitted.";
            Vector3 viewer = Player.m_localPlayer.transform.position;
            if (action == "reserve")
            {
                var candidate = slots.Where(s => s.IsHabitable && TryPoint(s.Key, out var p) && (p - viewer).sqrMagnitude <= 16f)
                    .OrderBy(s => { TryPoint(s.Key, out var p); return (p - viewer).sqrMagnitude; }).ThenBy(s => s.Key, StringComparer.Ordinal).FirstOrDefault();
                if (string.IsNullOrEmpty(candidate.Key)) return "Stand within four metres of a measured, unoccupied, roofed and warm bed in your settlement.";
                var request = _resident!.Phase == ResidentJournalPhase.Reserved ? _resident.Reservation : new RequestId("resident-" + Guid.NewGuid().ToString("N"));
                var result = _resident.Reserve(Id.Value, candidate, request);
                _notice = result == ResidentAdmissionResult.Reserved || result == ResidentAdmissionResult.AlreadyRecorded
                    ? "Bed reserved and saved. Confirm admission here; no resident is created until that save succeeds."
                    : "Reservation refused or not saved: " + result + ".";
                return _notice;
            }
            if (action == "admit")
            {
                var current = slots.FirstOrDefault(s => s.Key == _resident!.Slot);
                if (!TryPoint(current.Key, out var at) || (at - viewer).sqrMagnitude > 16f) return "Return to the reserved bed before confirming.";
                var result = _resident!.Admit(Id.Value, current, _resident.Reservation);
                _notice = result == ResidentAdmissionResult.Admitted || result == ResidentAdmissionResult.AlreadyRecorded
                    ? "Admission saved. One local resident is expected here. The local routine follows the game clock: morning/evening sitting, daytime standing and nighttime ground rest."
                    : "Admission refused or not saved: " + result + ". The reservation is retained.";
                if (result == ResidentAdmissionResult.Admitted) { TakeDown(); _figureRefused = false; _sinceSurvey = 10; }
                return _notice;
            }
            return Status();
        }
        catch (Exception exception) { Fail(exception); return _notice; }
    }
    private string Status() => _resident == null ? _notice :
        "Resident: " + _resident.Phase + "; routine: " + (_pose?.Stage.ToString() ?? "not shown") + ". " + (_resident.HasResident ? "One saved identity. " : "No resident admitted. ") + _notice;
    private void Show(ResidentHousingSlot slot)
    {
        if (!TryPoint(slot.Key, out var bed) || (Player.m_localPlayer.transform.position - bed).sqrMagnitude > 48f * 48f) return;
        if (!TryStand(bed, out var at)) { _notice = "Admission is saved, but no clear supported presentation spot was found beside the bed."; return; }
        var claim = _registry.TryClaimBody(Id, NpcBodyKind.Presentation, Holder);
        if (!claim.IsGranted || claim.Lease == null) { _notice = "The single-body lease is unavailable."; return; }
        _lease = claim.Lease;
        // Ordinary working leathers, no specialist's hair/name/reference.
        var look = new NpcLookSpec("Resident", NpcBodyModel.Male, null, null, ThorsteinLookPlan.Chest, ThorsteinLookPlan.Legs,
            null, 0, .5f, new NpcLookRgb(.3f, .2f, .1f));
        var source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
        var figure = NpcPresentationBody.TryExtract(source, "Player", "CF_OrdinaryResident", "CF_ResidentHarvest", string.Empty, _log);
        if (!figure.IsBuilt || figure.Root == null)
        {
            _notice = "Resident presentation unavailable: " + figure.Refusal;
            _figureRefused = true; TakeDown(); return;
        }
        _dresser = new NpcFigureDresser(figure, look, "CF_ResidentHarvest", _log);
        _dresser.StandAt(at, Quaternion.LookRotation(Vector3.forward, Vector3.up));
        _dresser.Apply();
        _pose = new ResidentPose(figure);
        _dresser.EnableTalk("Resident", "Speak", () => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, Status()), _log);
        _notice = "The resident is shown locally. The daily pose routine uses native blends; in-game fit and transitions still need observation.";
    }
    private static bool TryStand(Vector3 bed, out Vector3 at)
    {
        at = default;
        int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        if (mask == 0 || ZoneSystem.instance == null || !ZoneSystem.instance.IsZoneLoaded(bed)) return false;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4;
            var candidate = bed + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 1.3f;
            if (!Physics.Raycast(candidate + Vector3.up * .4f, Vector3.down, out var hit, 1f, mask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .75f || Mathf.Abs(hit.point.y - bed.y) > .5f) continue;
            candidate.y = hit.point.y;
            if (Physics.CheckCapsule(candidate + Vector3.up * .4f, candidate + Vector3.up * 1.6f, .3f, mask, QueryTriggerInteraction.Ignore)) continue;
            at = candidate; return true;
        }
        return false;
    }
    private static bool TryPoint(string? key, out Vector3 point)
    {
        point = default;
        if (key == null) return false;
        var fields = key.Split(';');
        if (fields.Length != 3 || !float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return false;
        point = new Vector3(x, y, z); return true;
    }
    private void TakeDown() { _pose = null; _dresser?.TakeDown(); _dresser = null; _lease?.Dispose(); _lease = null; }
    internal void Forget()
    {
        TakeDown(); _panel.Dispose(); _resident = null; _store = null; _world = 0; _sinceSurvey = 0; _figureRefused = false; _away = false;
        _notice = "Reserve a usable bed nearby, then confirm admission. No jobs are offered.";
    }
    public void Dispose() => Forget();
    private void Fail(Exception exception)
    {
        _faulted = true; TakeDown(); _panel.Dispose();
        _notice = "Resident presentation stopped after a fault. Its saved record is preserved.";
        _log(_notice + " " + SafeFailure.Brief(exception));
    }
}
