using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What is about to move, recorded durably <b>before</b> any engine
/// mutation.
///
/// <b>The id is the idempotence key, and it is derived rather than minted.</b>
/// It is a <see cref="ReservationId"/> - the same <c>job#step</c> name a
/// reservation carries - and that is the whole reason a job resumed after a
/// crash can ask "did step 4 already happen?" and get a true answer. A counter
/// or a fresh identifier would be a different name on the second run, and the
/// ledger would answer "no" to a transfer that had in fact happened.
///
/// <b>The same id with a different payload is a caller defect</b>, refused
/// rather than waved through: answering "already done" to a transfer that is
/// not the one recorded is how material goes missing with the record looking
/// healthy.</summary>
internal sealed class NpcTransferIntent
{
    internal NpcTransferIntent(
        ReservationId request,
        NpcCustodyLocation from,
        NpcCustodyLocation to,
        NpcMaterial material,
        int count,
        int expectedRevision)
    {
        Request = request;
        From = from;
        To = to;
        Material = material;
        Count = count;
        ExpectedRevision = expectedRevision;
    }

    /// <summary>The transfer's stable name. Its <see cref="ReservationId.JobId"/>
    /// half is the owning job, which is what custody is kept on behalf of.
    /// </summary>
    internal ReservationId Request { get; }

    /// <summary>The job this transfer belongs to.</summary>
    internal string JobId => Request.JobId;

    internal NpcCustodyLocation From { get; }

    internal NpcCustodyLocation To { get; }

    internal NpcMaterial Material { get; }

    internal int Count { get; }

    /// <summary>The custody revision the planner read. A transfer built on an
    /// older view of custody is stale and never applied - which is what stops
    /// two steps of the same job from both spending the same units.</summary>
    internal int ExpectedRevision { get; }

    /// <summary>Everything that has to be true for two intents to be the same
    /// transfer. Checked against the recorded one on every retry.</summary>
    internal bool SamePayloadAs(NpcTransferIntent? other) =>
        other != null
        && From.Equals(other.From)
        && To.Equals(other.To)
        && Material.Equals(other.Material)
        && Count == other.Count;

    /// <summary>A transfer that could be attempted at all: a real name, two
    /// different real places, a named material, at least one unit, and a source
    /// the job is allowed to take from. Checked before anything is written, so
    /// a malformed intent never reaches disk.</summary>
    internal bool IsWellFormed =>
        !Request.IsEmpty
        && From.IsSpecified && To.IsSpecified && !From.Equals(To)
        && Material.IsNamed
        && Count > 0
        && !From.Epoch.IsUnknown && From.Epoch.Equals(To.Epoch);

    public override string ToString() =>
        Count + " " + Material + " " + From + " -> " + To + " (" + Request + ")";
}
