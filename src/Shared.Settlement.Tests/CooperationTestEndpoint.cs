using System;
using System.Collections.Generic;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>A scripted haul endpoint for consumer tests: records every request
/// and answers through a replaceable responder. The real provider is tested in
/// Teamster's suite and against this consumer in <c>src/Interop.Tests</c>.
/// </summary>
internal sealed class CooperationTestEndpoint : IHaulEndpointSource
{
    public CooperationTestEndpoint()
    {
        Discovery = HaulDiscovery.Available("1.0.5", Handle);
        Respond = _ => throw new InvalidOperationException("no responder");
    }

    public HaulDiscovery Discovery { get; set; }

    public List<IReadOnlyDictionary<string, string>> Requests { get; } = new List<IReadOnlyDictionary<string, string>>();

    public Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>?> Respond { get; set; }

    public static CooperationTestEndpoint Healthy(Guid epoch) =>
        new CooperationTestEndpoint { Respond = HealthyResponder(epoch) };

    /// <summary>Answers every op successfully with plausible fields.</summary>
    public static Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>?> HealthyResponder(Guid epoch) =>
        request =>
        {
            HaulReplyHeader accepted = HaulReplyHeader.Success(HaulReplyStatus.Accepted);
            HaulOps.TryParse(request.TryGetValue(HaulContract.Keys.Op, out string? op) ? op : null, out HaulOp parsed);
            switch (parsed)
            {
                case HaulOp.Hello:
                    return new HelloReply(accepted, 0, "1.0.5", epoch, WorkAuthorityVerdict.Granted, true, HaulWirePhase.Ready, "lease-1").ToWire();
                case HaulOp.DescribeLease:
                    return new DescribeLeaseReply(accepted, "lease-1", "5:42", new WorkPoint(1f, 2f, 3f), true, true, false, HaulWirePhase.Ready, 1).ToWire();
                case HaulOp.GetHaul:
                    return new GetHaulReply(accepted, HaulWirePhase.Waiting, HaulWireReason.Unspecified, null, 3, true, true, new WorkPoint(1f, 2f, 3f), null, true).ToWire();
                case HaulOp.RequestHaul:
                    return new RequestHaulReply(accepted, request[HaulContract.Keys.HaulId], HaulWirePhase.Approaching, 2).ToWire();
                default:
                    return new HaulPhaseReply(accepted, HaulWirePhase.Waiting, 4).ToWire();
            }
        };

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> request)
    {
        Requests.Add(request);
        return Respond(request)!;
    }
}
