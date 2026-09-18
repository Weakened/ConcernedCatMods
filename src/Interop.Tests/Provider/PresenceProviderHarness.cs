using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Presence;

namespace Interop.Tests.Provider;

/// <summary>The presence provider as the test sees it: the same
/// <see cref="PresenceResponder"/> Concerned Cartographer's publisher calls,
/// compiled into THIS assembly, over facts a test sets.
///
/// The publisher itself cannot be here — it needs Unity and BepInEx — but it is
/// a thin shell around this responder precisely so that what crosses the
/// assembly boundary in the test is the shipped protocol and the shipped reply
/// shape rather than a second implementation of them.</summary>
public sealed class PresenceProviderHarness
{
    private readonly string _providerVersion;

    public PresenceProviderHarness(string providerVersion = "1.2.2")
    {
        _providerVersion = providerVersion;

        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint = Handle;
        Capabilities = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(PresenceContract.Id, PresenceContract.Major)] = endpoint,
        });
    }

    /// <summary>What the plugin's <c>ConcernedCatCapabilities</c> property
    /// returns.</summary>
    public IReadOnlyDictionary<string, object> Capabilities { get; }

    /// <summary>False while there is no runtime to ask.</summary>
    public bool Ready { get; set; } = true;

    public bool Known { get; set; } = true;

    public bool Present { get; set; } = true;

    public bool Visible { get; set; } = true;

    public bool Available { get; set; } = true;

    public bool HasPosition { get; set; } = true;

    public float X { get; set; } = 12.5f;

    public float Y { get; set; } = 30.25f;

    public float Z { get; set; } = -4f;

    /// <summary>Set to make the provider throw, so the consumer's handling of a
    /// provider bug is exercised rather than assumed.</summary>
    public bool Throw { get; set; }

    public int Calls { get; private set; }

    /// <summary>A capability map whose value is not a callable endpoint, for the
    /// consumer's mismatch path.</summary>
    public static IReadOnlyDictionary<string, object> MapWithWrongShape() =>
        new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(PresenceContract.Id, PresenceContract.Major)] = "not a delegate",
        });

    /// <summary>A capability map that carries some other contract entirely.
    /// </summary>
    public static IReadOnlyDictionary<string, object> MapWithoutPresence() =>
        new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["concernedcat.somethingelse/1"] = new Func<
                IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>(_ =>
                    new Dictionary<string, string>()),
        });

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> request)
    {
        Calls++;
        if (Throw)
        {
            throw new InvalidOperationException("the provider is broken");
        }

        return PresenceResponder.Answer(
            request,
            _providerVersion,
            Ready,
            id => string.Equals(id, PresenceContract.Companions.Hulgi, StringComparison.Ordinal)
                ? new PresenceFacts(Known, Present, Visible, Available, HasPosition, X, Y, Z)
                : PresenceFacts.Nobody);
    }
}
