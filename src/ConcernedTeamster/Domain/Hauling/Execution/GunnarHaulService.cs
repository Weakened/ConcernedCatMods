using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>Gunnar's <see cref="IHaulService"/> (CONTRACTS.md §3.2): the one
/// stable object the <c>concernedcat.haul/1</c> provider and Teamster's own UI
/// hold, over an executor that is rebuilt every world load.
///
/// It applies each command it is given exactly once; request-id idempotence is
/// the provider's job. Between worlds (no executor) every command answers
/// Unavailable and the snapshot reads Unassigned. The revision never goes
/// backwards across world loads, so a consumer holding a revision from the
/// previous world is answered Stale rather than accidentally current.</summary>
internal sealed class GunnarHaulService : IHaulService
{
    private readonly IHaulAuthority _authority;
    private HaulExecutor? _executor;
    private int _retiredRevision;

    public GunnarHaulService(IHaulAuthority authority)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    /// <summary>The executor of the current world load, or null between worlds.
    /// </summary>
    public HaulExecutor? Executor => _executor;

    /// <summary>The revision a new world's executor starts after.</summary>
    public int NextStartingRevision => Math.Max(_retiredRevision, _executor?.Revision ?? 0) + 1;

    public HaulCommandDetail LastCommandDetail { get; private set; }

    public WorkAuthorityVerdict Authority => _authority.Evaluate();

    public bool WorkerAvailable => _executor != null && _executor.WorkerAvailable;

    public Guid WorldLoadEpoch => _executor?.WorldLoadEpoch ?? Guid.Empty;

    public CartLease? ActiveLease => _executor?.ActiveLease;

    public HaulSnapshot Snapshot =>
        _executor?.Snapshot ??
        new HaulSnapshot(string.Empty, HaulPhase.Unassigned, _retiredRevision, HaulAttentionReason.Unspecified, false, false, false, false, null, null);

    /// <summary>Binds the executor of a new world load.</summary>
    public void Bind(HaulExecutor executor)
    {
        if (executor == null)
        {
            throw new ArgumentNullException(nameof(executor));
        }

        if (_executor != null)
        {
            _retiredRevision = Math.Max(_retiredRevision, _executor.Revision);
        }

        _executor = executor;
    }

    /// <summary>Forgets the executor when its world goes away. The caller has
    /// already torn it down (joint released first).</summary>
    public void Unbind()
    {
        if (_executor != null)
        {
            _retiredRevision = Math.Max(_retiredRevision, _executor.Revision) + 1;
            _executor = null;
        }
    }

    public HaulCommandResult RequestLeg(HaulLegRequest request, int expectedRevision)
    {
        if (_executor == null)
        {
            return Unavailable();
        }

        HaulCommandResult result = _executor.RequestLeg(request, expectedRevision);
        LastCommandDetail = _executor.LastCommandDetail;
        return result;
    }

    public HaulCommandResult AcknowledgeWait(string haulId, int expectedRevision, bool transferring)
    {
        if (_executor == null)
        {
            return Unavailable();
        }

        HaulCommandResult result = _executor.AcknowledgeWait(haulId, expectedRevision, transferring);
        LastCommandDetail = _executor.LastCommandDetail;
        return result;
    }

    public HaulCommandResult Cancel(string haulId, bool detachAndPark)
    {
        if (_executor == null)
        {
            return Unavailable();
        }

        HaulCommandResult result = _executor.Cancel(haulId, detachAndPark);
        LastCommandDetail = _executor.LastCommandDetail;
        return result;
    }

    private HaulCommandResult Unavailable()
    {
        LastCommandDetail = HaulCommandDetail.WorkerUnavailable;
        return new HaulCommandResult(HaulCommandOutcome.Unavailable, HaulAttentionReason.WorkerBodyLost, _retiredRevision);
    }
}
