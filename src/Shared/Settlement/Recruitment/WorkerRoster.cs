using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Recruitment;

/// <summary>The roles this build knows.
///
/// #273 is explicit that named specialists and ordinary labourers are different
/// roles, so a worker carries one from the start rather than having it bolted
/// on when the second kind appears. The first proof recruits exactly one
/// ordinary labourer.</summary>
internal static class WorkerRoles
{
    public const string Labourer = "labourer";

    public static bool IsKnown(string? role)
    {
        return string.Equals(role, Labourer, StringComparison.Ordinal);
    }
}

internal enum RecruitmentOutcome
{
    /// <summary>Nobody was recruited. Zero, so an unfilled result refuses.</summary>
    Refused = 0,

    Recruited = 1,

    /// <summary>This worker is already on the roster. Idempotent.</summary>
    AlreadyRecruited = 2,
}

internal enum RecruitmentRefusal
{
    /// <summary>Refused with no reason recorded — a defect, named so it is
    /// visible rather than disguised as a plausible reason.</summary>
    Unspecified = 0,

    NotAuthorised = 1,

    /// <summary>There is nowhere to work. A worker belongs to a settlement.</summary>
    NoSettlementArea = 2,

    /// <summary>The first proof employs one worker.</summary>
    RosterFull = 3,

    /// <summary>The settlement's record could not be fully read.</summary>
    RecordReadOnly = 4,

    InvalidIdentity = 5,

    /// <summary>A role this build does not employ.</summary>
    UnknownRole = 6,
}

/// <summary>One worker the player recruited.
///
/// <b>This record, not the creature, is what "recruited" means.</b> The body in
/// the world is presentation — it can be killed, despawned, or lost to a zone
/// unload — exactly as the worker's inventory is presentation and the custody
/// ledger is the truth. A roster that emptied itself when a creature vanished
/// would make "is this settlement staffed?" depend on which zones happen to be
/// loaded.
///
/// This is the half of the companion unlock contract that fits: access, once
/// granted, is monotonic and survives the actor not existing. The half that
/// does <b>not</b> fit is that policy's deliberate bias towards granting on
/// ambiguous evidence — settlement authority fails closed, so an unreadable
/// roster refuses new recruitment rather than assuming it.</summary>
internal sealed class WorkerRecord
{
    internal WorkerRecord(WorkerId id, string role)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A worker record needs an identity.", nameof(id));
        }

        if (!WorkerRoles.IsKnown(role))
        {
            throw new ArgumentException(
                "A worker record needs a role this build employs. Received: '" +
                (role ?? "<null>") + "'.",
                nameof(role));
        }

        Id = id;
        Role = role;
    }

    public WorkerId Id { get; }

    public string Role { get; }

    public override string ToString() => Id.Value + " (" + Role + ")";
}

/// <summary>The answer to one recruitment request.</summary>
internal readonly struct RecruitmentResult
{
    private RecruitmentResult(
        RecruitmentOutcome outcome, RecruitmentRefusal refusal, WorkerRecord? worker)
    {
        Outcome = outcome;
        Refusal = refusal;
        Worker = worker;
    }

    public RecruitmentOutcome Outcome { get; }
    public RecruitmentRefusal Refusal { get; }
    public WorkerRecord? Worker { get; }

    public bool IsRecruited =>
        Outcome == RecruitmentOutcome.Recruited || Outcome == RecruitmentOutcome.AlreadyRecruited;

    public static RecruitmentResult Recruited(WorkerRecord worker)
    {
        return new RecruitmentResult(
            RecruitmentOutcome.Recruited, RecruitmentRefusal.Unspecified, worker);
    }

    public static RecruitmentResult Already(WorkerRecord worker)
    {
        return new RecruitmentResult(
            RecruitmentOutcome.AlreadyRecruited, RecruitmentRefusal.Unspecified, worker);
    }

    public static RecruitmentResult Refused(RecruitmentRefusal refusal, WorkerRecord? existing = null)
    {
        return new RecruitmentResult(RecruitmentOutcome.Refused, refusal, existing);
    }

    public string Describe()
    {
        switch (Outcome)
        {
            case RecruitmentOutcome.Recruited:
                return "Recruited " + Worker + ".";

            case RecruitmentOutcome.AlreadyRecruited:
                return Worker + " is already on the roster. Nothing changed.";

            default:
                return "Refused: " + DescribeRefusal() + ".";
        }
    }

    private string DescribeRefusal()
    {
        switch (Refusal)
        {
            case RecruitmentRefusal.NotAuthorised:
                return "the settlement runtime is not allowed to act here";

            case RecruitmentRefusal.NoSettlementArea:
                return "there is no settlement area yet, and a worker belongs to one";

            case RecruitmentRefusal.RosterFull:
                return "this settlement already employs " + Worker +
                    ", and the first proof employs one worker at a time";

            case RecruitmentRefusal.RecordReadOnly:
                return "this settlement's record could not be fully read, so nothing new is " +
                    "being written over it";

            case RecruitmentRefusal.InvalidIdentity:
                return "that is not a usable worker name";

            case RecruitmentRefusal.UnknownRole:
                return "this build does not employ that role";

            default:
                return "no reason was recorded, which is a bug — please report it";
        }
    }

    public override string ToString() => Describe();
}

/// <summary>Who this settlement employs.
///
/// One worker, for the first proof, and that bound is written here rather than
/// left to a caller so that "how many workers can there be" has one answer.
/// Raising it is a visible change to this constant and to the tests that pin
/// it, not a caller quietly passing a bigger number.</summary>
internal sealed class WorkerRoster
{
    /// <summary>#273's first playable outcome is one cottage and one resident,
    /// built by one worker. A second worker is a scaling question with no
    /// measurement behind it yet.</summary>
    public const int MaxWorkers = 1;

    private readonly Dictionary<string, WorkerRecord> _workers =
        new Dictionary<string, WorkerRecord>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();

    public IReadOnlyList<WorkerRecord> Workers
    {
        get
        {
            var ordered = new List<WorkerRecord>(_order.Count);
            foreach (string key in _order)
            {
                ordered.Add(_workers[key]);
            }

            return ordered;
        }
    }

    public int Count => _order.Count;

    public bool IsEmpty => _order.Count == 0;

    public bool Contains(WorkerId id)
    {
        return !id.IsEmpty && _workers.ContainsKey(id.Value);
    }

    /// <summary>Adds a worker, or explains why not.
    ///
    /// <paramref name="hasSettlementArea"/> is passed in rather than read from
    /// a designation book so that this type stays about employment and nothing
    /// else — but it is required, because recruiting into a settlement that
    /// does not exist produces a worker with nowhere to work.</summary>
    public RecruitmentResult Recruit(WorkerId id, string role, bool hasSettlementArea)
    {
        if (id.IsEmpty)
        {
            return RecruitmentResult.Refused(RecruitmentRefusal.InvalidIdentity);
        }

        if (!WorkerRoles.IsKnown(role))
        {
            return RecruitmentResult.Refused(RecruitmentRefusal.UnknownRole);
        }

        if (_workers.TryGetValue(id.Value, out WorkerRecord? existing))
        {
            return RecruitmentResult.Already(existing!);
        }

        if (!hasSettlementArea)
        {
            return RecruitmentResult.Refused(RecruitmentRefusal.NoSettlementArea);
        }

        if (_order.Count >= MaxWorkers)
        {
            return RecruitmentResult.Refused(
                RecruitmentRefusal.RosterFull, _workers[_order[0]]);
        }

        var record = new WorkerRecord(id, role);
        _workers.Add(id.Value, record);
        _order.Add(id.Value);
        return RecruitmentResult.Recruited(record);
    }

    /// <summary>Removes a worker. Its own explicit act, never a side effect of
    /// clearing ground or of a creature dying. Returns true when the roster
    /// changed.</summary>
    public bool Dismiss(WorkerId id)
    {
        if (id.IsEmpty || !_workers.Remove(id.Value))
        {
            return false;
        }

        _order.Remove(id.Value);
        return true;
    }

    /// <summary>Restores a worker read from disk, without re-checking the
    /// settlement. Loading is not recruiting: a record written when the
    /// settlement existed is read back as it was written.</summary>
    internal void Restore(WorkerRecord record)
    {
        if (_workers.ContainsKey(record.Id.Value))
        {
            return;
        }

        _workers.Add(record.Id.Value, record);
        _order.Add(record.Id.Value);
    }
}

/// <summary>What happened to a request to dismiss somebody.
///
/// <see cref="Refused"/> is zero for the same reason every other outcome in
/// this layer is: an unfilled result must not read as "it worked".</summary>
internal enum DismissalOutcome
{
    /// <summary>Not allowed to act. Nobody was dismissed.</summary>
    Refused = 0,

    Dismissed = 1,

    /// <summary>Nobody by that name was employed. Idempotent: dismissing
    /// somebody twice is dismissing them once.</summary>
    NotOnRoster = 2,
}
