using System;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Every numeric bound of a cooperative order in one place (COOP-02,
/// CONTRACTS.md §7), so waits, polls and retries are recorded and tested rather
/// than scattered.
///
/// <see cref="PollIntervalSeconds"/> and <see cref="RendezvousTimeoutSeconds"/>
/// mirror the cooperation section of Teamster's <c>HaulLimits</c>. Foreman
/// cannot compile Teamster's domain, so the values are repeated here and the
/// two-assembly interop tests pin them equal; a change on one side without the
/// other fails there.</summary>
internal sealed class CooperationLimits
{
    public static CooperationLimits Default => new CooperationLimits();

    // Protocol (mirrors HaulLimits)

    /// <summary>A haul is never polled faster than this.</summary>
    public float PollIntervalSeconds { get; set; } = 0.5f;

    /// <summary>The longest any cooperative wait may last before it ends in
    /// NeedsAttention: the cart reaching a rendezvous, Thorstein reaching the
    /// cart, a haul leg reaching the destination, a cancel settling.</summary>
    public float RendezvousTimeoutSeconds { get; set; } = 180f;

    // Transport

    /// <summary>Calls in a row with no trustworthy answer (thrown, missing or
    /// malformed reply, ProviderError) before the provider counts as lost.
    /// </summary>
    public int MaxConsecutiveNoAnswers { get; set; } = 3;

    public float NoAnswerBackoffSeconds { get; set; } = 1f;

    public float NoAnswerBackoffMaxSeconds { get; set; } = 8f;

    // Transfers (CONTRACTS.md §7: three attempts on Refused, with backoff)

    public int MaxTransferRefusals { get; set; } = 3;

    public float TransferRetrySeconds { get; set; } = 1f;

    public float TransferRetryMaxSeconds { get; set; } = 4f;

    // Geometry

    /// <summary>How close Thorstein must be to the cart before a transfer at
    /// it is attempted. The custody ports check reach again at the mutation
    /// itself; this only avoids asking for a hold he cannot use.</summary>
    public float CartReachMetres { get; set; } = 2.5f;

    /// <summary>How far beside the cart Thorstein aims to stand.</summary>
    public float CartStandOffMetres { get; set; } = 1.2f;

    public float WalkToleranceMetres { get; set; } = 1f;

    public float RendezvousArrivalRadiusMetres { get; set; } = 3f;

    public float DestinationArrivalRadiusMetres { get; set; } = 3f;

    /// <summary>Rendezvous points offered to Gunnar, in order, before the
    /// staging attempt gives up (a route refusal moves to the next one).
    /// </summary>
    public int MaxRendezvousCandidates { get; set; } = 4;

    /// <summary>Throws when a value is outside what the loop was designed for.
    /// </summary>
    public CooperationLimits Validate()
    {
        Require(PollIntervalSeconds >= 0.1f && PollIntervalSeconds <= 10f, nameof(PollIntervalSeconds));
        Require(RendezvousTimeoutSeconds >= 10f && RendezvousTimeoutSeconds <= 1800f, nameof(RendezvousTimeoutSeconds));
        Require(MaxConsecutiveNoAnswers >= 1 && MaxConsecutiveNoAnswers <= 20, nameof(MaxConsecutiveNoAnswers));
        Require(
            NoAnswerBackoffSeconds > 0f && NoAnswerBackoffMaxSeconds >= NoAnswerBackoffSeconds &&
            NoAnswerBackoffMaxSeconds <= 120f,
            nameof(NoAnswerBackoffSeconds));
        Require(MaxTransferRefusals >= 1 && MaxTransferRefusals <= 10, nameof(MaxTransferRefusals));
        Require(
            TransferRetrySeconds > 0f && TransferRetryMaxSeconds >= TransferRetrySeconds && TransferRetryMaxSeconds <= 60f,
            nameof(TransferRetrySeconds));
        Require(CartReachMetres >= 1f && CartReachMetres <= 5f, nameof(CartReachMetres));
        Require(CartStandOffMetres >= 0.5f && CartStandOffMetres < CartReachMetres, nameof(CartStandOffMetres));
        Require(WalkToleranceMetres >= 0.25f && WalkToleranceMetres <= 3f, nameof(WalkToleranceMetres));
        Require(
            RendezvousArrivalRadiusMetres >= 0.5f && RendezvousArrivalRadiusMetres <= 16f,
            nameof(RendezvousArrivalRadiusMetres));
        Require(
            DestinationArrivalRadiusMetres >= 0.5f && DestinationArrivalRadiusMetres <= 16f,
            nameof(DestinationArrivalRadiusMetres));
        Require(MaxRendezvousCandidates >= 1 && MaxRendezvousCandidates <= 8, nameof(MaxRendezvousCandidates));
        return this;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, "Cooperation limit out of its designed range.");
        }
    }
}
