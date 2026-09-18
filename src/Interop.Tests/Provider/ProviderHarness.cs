using System;
using System.Collections.Generic;
using ConcernedTeamster.Tests;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace Interop.Tests.Provider;

/// <summary>The provider product as the test sees it: Teamster's real
/// <see cref="HaulProvider"/> over a fake haul service, published the way the
/// plugin publishes it. Everything public here is a BCL type, so the test
/// project can drive it without seeing any contract type compiled into this
/// assembly.</summary>
public sealed class ProviderHarness
{
    private readonly HaulingInteropFakeService _service;
    private readonly HaulProvider _provider;
    private int _epochCounter = 1;
    private bool _runtimeRunning = true;

    public ProviderHarness(string providerVersion = "1.0.5")
    {
        _service = new HaulingInteropFakeService(EpochFor(_epochCounter));
        _provider = new HaulProvider(() => _runtimeRunning ? _service : null, providerVersion);
        Capabilities = _provider.CreateCapabilityMap();
    }

    /// <summary>What the plugin's <c>ConcernedCatCapabilities</c> property
    /// returns.</summary>
    public IReadOnlyDictionary<string, object> Capabilities { get; }

    public string EpochText => HaulEpoch.Format(_service.WorldLoadEpoch);

    public string Phase => _service.Phase.ToString();

    public int Revision => _service.Revision;

    public string HaulId => _service.HaulId;

    public bool Attached => _service.Attached;

    public int LegCalls => _service.LegCalls;

    public int AcknowledgeCalls => _service.AcknowledgeCalls;

    public int CancelCalls => _service.CancelCalls;

    public int FaultCount => _provider.FaultCount;

    public string LastLegTarget => _service.LastLeg?.Target.Format() ?? string.Empty;

    public bool LastLegToDestination => _service.LastLeg?.ToDestination ?? false;

    public string LastLegOrderId => _service.LastLeg?.OrderId ?? string.Empty;

    /// <summary>Teamster's poll interval, for pinning the consumer's copy.
    /// </summary>
    public float PollIntervalSeconds => HaulLimits.Default.PollIntervalSeconds;

    public float RendezvousTimeoutSeconds => HaulLimits.Default.RendezvousTimeoutSeconds;

    /// <summary>Proof that this assembly compiled its own copy of the contract.
    /// </summary>
    public string ContractTypeIdentity => typeof(HaulReplyStatus).AssemblyQualifiedName ?? string.Empty;

    public string AssignCart(string leaseId, string cartSessionKey) =>
        _service.AssignCart(leaseId, cartSessionKey).ToString();

    public void ReleaseLease() => _service.ReleaseLease();

    public void CompleteLeg(float x, float y, float z) => _service.CompleteLeg(new WorkPoint(x, y, z));

    /// <summary>Provider-ended control, by <c>HaulAttentionReason</c> name.
    /// </summary>
    public void EndControl(string attentionReason) =>
        _service.EndControl((HaulAttentionReason)Enum.Parse(typeof(HaulAttentionReason), attentionReason));

    public void ReloadWorld()
    {
        _epochCounter++;
        _service.ReloadWorld(EpochFor(_epochCounter));
    }

    public void SetAuthority(string verdict) =>
        _service.Authority = (WorkAuthorityVerdict)Enum.Parse(typeof(WorkAuthorityVerdict), verdict);

    public void SetWorkerAvailable(bool available) => _service.WorkerAvailable = available;

    public void SetCartStill(bool still) => _service.CartStill = still;

    public void ThrowOnNextCommand() => _service.ThrowOnNextCommand = new InvalidOperationException("provider-side failure");

    /// <summary>Gunnar's runtime stops (disabled in the middle of a session).
    /// </summary>
    public void StopRuntime() => _runtimeRunning = false;

    public void BeginShutdown() => _provider.BeginShutdown();

    private static Guid EpochFor(int counter) => new Guid(counter, 0x317, 0x2, 0, 0, 0, 0, 0, 0, 0, 1);
}
