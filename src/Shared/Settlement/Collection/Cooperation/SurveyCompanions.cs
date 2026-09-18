using System;
using TheConcernedCat.Interop.Presence;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Whether Hulgi is in the survey, asked no more often than it can
/// change usefully (#317, SPEC GATHER-03).
///
/// <b>Why this is polled rather than remembered.</b> Presence is exactly the
/// thing that changes while nobody is looking: he gets up, he walks off, a
/// player hides him. A remembered "he was here when the survey started" is how
/// somebody gets credited for work they walked away from, which is the one
/// thing GATHER-03 names. So the answer has a short life and is re-asked, and
/// between asks the last answer stands rather than being optimistically
/// assumed — the last answer came from the provider, an assumption would not.
///
/// <b>Why it is only asked while surveying.</b> He helps look over an area.
/// He does not carry, pick, haul or deliver, so asking about him at any other
/// time would produce a fact nothing may act on, and a panel line implying he
/// is involved in something he is not.</summary>
internal sealed class SurveyCompanions
{
    /// <summary>Half a second. Fast enough that walking away shows up within a
    /// survey pass, slow enough that a cross-assembly call is not a per-frame
    /// cost. The provider's answer is three booleans read off state it already
    /// holds, so this is cheap either way; the interval is about not making a
    /// habit of it.</summary>
    private const float PollIntervalSeconds = 0.5f;

    private readonly CompanionPresenceClient _client;
    private readonly Func<float> _now;

    private float _lastAsked = float.NegativeInfinity;
    private SurveyParticipation _participation = SurveyParticipation.NotAsked;

    /// <param name="discovery">The consumer adapter's current discovery. A
    /// function rather than a value because a world reload re-probes, and a
    /// captured discovery would keep calling an endpoint from the last
    /// session.</param>
    internal SurveyCompanions(Func<PresenceDiscovery> discovery, string consumerVersion, Func<float> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _client = new CompanionPresenceClient(discovery, consumerVersion);
    }

    /// <summary>The last answer. <see cref="SurveyParty.Solo"/> until somebody
    /// asks, which is the safe direction.</summary>
    internal SurveyParticipation Participation => _participation;

    /// <summary>True only while an order is actually surveying and Hulgi's own
    /// product says he is here and free.</summary>
    internal bool IsHelping(CollectionOrderState state)
    {
        if (state != CollectionOrderState.Surveying)
        {
            _participation = SurveyParticipation.NotAsked;
            return false;
        }

        float now = _now();
        if (now - _lastAsked >= PollIntervalSeconds)
        {
            _lastAsked = now;
            _participation = SurveyParticipation.Decide(
                PresenceContract.Companions.Hulgi,
                _client.Describe(PresenceContract.Companions.Hulgi));
        }

        return _participation.IsJoint;
    }

    /// <summary>The world went away: forget the answer as well as the probe, so
    /// the next world session does not start out believing somebody from the
    /// last one is standing around.</summary>
    internal void Forget()
    {
        _lastAsked = float.NegativeInfinity;
        _participation = SurveyParticipation.NotAsked;
    }
}
