using System;
using System.IO;
using BepInEx;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Register;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>A read-only view of the settlement's register and journal, for the
/// questions collection asks of them: is there a harvest designation, is
/// Thorstein recruited, which tools does he hold.
///
/// <b>Read-only, and why it reads the files.</b> The live records belong to the
/// settlement runtime (agent D). Collection never writes them, so rather than
/// hold a second writable copy it loads what is on disk, which every act saves
/// as it happens. Answers are reused for a few seconds so a checkpoint every
/// tick does not become a file read every tick. A record that cannot be read
/// answers "unavailable", and an unavailable assigned area never falls back to
/// the default circle. The lead may replace this with the runtime's live
/// records at integration.</summary>
internal sealed class SettlementDiskReader
{
    /// <summary>The first proof's one settlement, as <c>SettlementRecords</c>
    /// names it.</summary>
    private const string FirstSettlement = "home";

    private const float ReuseSeconds = 5f;

    private readonly SettlementRegisterStore _registers;
    private readonly JournalStore _journals;
    private float _readAt = float.NegativeInfinity;
    private long _readWorld;
    private SettlementRegister? _register;
    private ReplayResult? _replay;
    private string _failure = string.Empty;

    public SettlementDiskReader()
        : this(Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedForeman", "settlements"))
    {
    }

    public SettlementDiskReader(string root)
    {
        _registers = new SettlementRegisterStore(root);
        _journals = new JournalStore(root);
    }

    public void Forget()
    {
        _readAt = float.NegativeInfinity;
        _register = null;
        _replay = null;
    }

    public bool TryRead(out SettlementRegister register, out ReplayResult replay, out string failure)
    {
        register = null!;
        replay = null!;

        ZNet net = ZNet.instance;
        long world = 0L;
        try
        {
            world = net != null ? net.GetWorldUID() : 0L;
        }
        catch (Exception)
        {
            world = 0L;
        }

        if (world == 0L)
        {
            failure = "no world is loaded";
            return false;
        }

        float now = Time.time;
        if (_readWorld != world || now - _readAt >= ReuseSeconds || now < _readAt)
        {
            _readAt = now;
            _readWorld = world;
            try
            {
                var scope = new SettlementScope(world, new SettlementId(FirstSettlement));
                SettlementRegisterStore.LoadReport registerReport = _registers.Load(scope);
                JournalStore.LoadReport journalReport = _journals.Load(scope);
                _register = registerReport.Register;
                _replay = journalReport.Journal.Replay();
                _failure = string.Empty;
            }
            catch (Exception exception)
            {
                _register = null;
                _replay = null;
                _failure = "the settlement record could not be read (" + exception.GetType().Name + ")";
            }
        }

        if (_register == null || _replay == null)
        {
            failure = _failure.Length > 0 ? _failure : "the settlement record could not be read";
            return false;
        }

        register = _register;
        replay = _replay;
        failure = string.Empty;
        return true;
    }
}

/// <summary>Builds and re-observes an order's work scope (GATHER-02, D11).
///
/// <b>Assigned first.</b> A harvest designation is the assigned area and is
/// copied as it stands. With none, the default circle is centred on the latest
/// valid respawn anchor, shown to the player first. A record that cannot be
/// read is not "no designation": it refuses, because an assigned area that is
/// unavailable never falls back.
///
/// <b>Revisions.</b> A designation's revision is its own centre and radius; the
/// default circle's is its anchor's kind and point. A redrawn area or a newly
/// claimed bed therefore changes the revision, and the order pauses with
/// <c>ScopeChanged</c> at its next checkpoint instead of following.</summary>
internal sealed class WorkScopeBuilder
{
    private readonly SettlementDiskReader _reader;

    public WorkScopeBuilder(SettlementDiskReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>The harvest designation, when the record is readable. False
    /// with a failure when it is not; true with a null designation when none is
    /// marked.</summary>
    public bool TryGetHarvestArea(out Designation? harvest, out string failure)
    {
        harvest = null;
        if (!_reader.TryRead(out SettlementRegister register, out _, out failure))
        {
            return false;
        }

        if (register.TryGet(DesignationKind.HarvestArea, out Designation designation))
        {
            harvest = designation;
        }

        return true;
    }

    public static WorkScope FromHarvest(Designation harvest, Guid worldLoadEpoch) =>
        new WorkScope(
            WorkScopeSource.HarvestDesignation, harvest.Centre, harvest.Radius, "the harvest area",
            ScopeRevision.ForArea(harvest.Centre, harvest.Radius), worldLoadEpoch);

    public static WorkScope FromAnchor(RespawnAnchor anchor, float radius, Guid worldLoadEpoch) =>
        new WorkScope(
            WorkScopeSource.DefaultCampCircle, anchor.Point, radius, anchor.Describe(),
            ScopeRevision.ForAnchor(anchor.RevisionName, anchor.Point), worldLoadEpoch);

    /// <summary>What the world says now about where an accepted scope came
    /// from. Anything that cannot be asked answers "not present".</summary>
    public ScopeObservation Observe(WorkScope scope, Guid currentEpoch)
    {
        switch (scope.Source)
        {
            case WorkScopeSource.HarvestDesignation:
                if (!TryGetHarvestArea(out Designation? harvest, out _) || harvest == null)
                {
                    return new ScopeObservation(false, 0, currentEpoch);
                }

                return new ScopeObservation(true, ScopeRevision.ForArea(harvest.Centre, harvest.Radius), currentEpoch);

            case WorkScopeSource.DefaultCampCircle:
                if (!RespawnAnchorSource.TryGetLatest(out RespawnAnchor anchor, out _))
                {
                    return new ScopeObservation(false, 0, currentEpoch);
                }

                return new ScopeObservation(true, ScopeRevision.ForAnchor(anchor.RevisionName, anchor.Point), currentEpoch);

            default:
                // A Cartographer work area has no provider in this build.
                return new ScopeObservation(false, 0, currentEpoch);
        }
    }
}
