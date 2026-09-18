using System;
using TheConcernedCat.Interop.Presence;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Who is surveying, and why the other one is not.
///
/// SPEC GATHER-03 asks for three things and this type is all three: with
/// Cartographer, a cooperative survey where <i>both are shown</i>; without it,
/// a <i>clearly labelled</i> solo survey; and <i>no false credit</i> — a busy
/// or absent Hulgi is never credited.
///
/// The last one is why this is a decision type rather than a boolean on the
/// loop. "Absent" and "here but busy" and "here, free, and helping" have to be
/// three different answers, because the sentence a player reads has to be able
/// to say which — and because a single flag would sooner or later be set from
/// something cheaper to ask than the provider.</summary>
internal enum SurveyParty
{
    /// <summary>Thorstein alone. The ordinary case, and never a failure.
    /// </summary>
    Solo = 0,

    /// <summary>Thorstein and Hulgi, because Hulgi's own product says he is
    /// here and free.</summary>
    Joint = 1,
}

/// <summary>Why a survey is solo. Only meaningful when
/// <see cref="SurveyParticipation.Party"/> is <see cref="SurveyParty.Solo"/>.
/// </summary>
internal enum SoloReason
{
    /// <summary>It is not solo.</summary>
    None = 0,

    /// <summary>No presence provider answered: Cartographer is not installed,
    /// is too old, or its capability did not verify.</summary>
    NoProvider = 1,

    /// <summary>The provider answered, and this player has not met him.
    /// </summary>
    NotKnown = 2,

    /// <summary>Known, but there is no body in the world right now — hidden,
    /// or the world has not placed him yet.</summary>
    NotHere = 3,

    /// <summary>Here, but in the middle of something.</summary>
    Busy = 4,
}

/// <summary>The answer, and the sentence for it.</summary>
internal readonly struct SurveyParticipation
{
    private SurveyParticipation(SurveyParty party, SoloReason reason, string companionId)
    {
        Party = party;
        Reason = reason;
        CompanionId = companionId ?? string.Empty;
    }

    public SurveyParty Party { get; }

    public SoloReason Reason { get; }

    /// <summary>Which companion this was decided about — the stable id, not a
    /// display name.</summary>
    public string CompanionId { get; }

    public bool IsJoint => Party == SurveyParty.Joint;

    /// <summary>Decides from what the companion's own product said.
    ///
    /// The order of the checks is the order a person would ask the questions
    /// in, so the reason names the first thing that is actually true rather
    /// than the last check to run. Nothing here infers availability: if the
    /// provider says available, he is available, and if it does not, he is not.
    /// </summary>
    public static SurveyParticipation Decide(string companionId, CompanionPresence presence)
    {
        string id = string.IsNullOrEmpty(companionId) ? PresenceContract.Companions.Hulgi : companionId;

        if (!presence.Answered)
        {
            return new SurveyParticipation(SurveyParty.Solo, SoloReason.NoProvider, id);
        }

        if (!presence.Known)
        {
            return new SurveyParticipation(SurveyParty.Solo, SoloReason.NotKnown, id);
        }

        if (!presence.Present || !presence.Visible)
        {
            return new SurveyParticipation(SurveyParty.Solo, SoloReason.NotHere, id);
        }

        if (!presence.Available)
        {
            return new SurveyParticipation(SurveyParty.Solo, SoloReason.Busy, id);
        }

        return new SurveyParticipation(SurveyParty.Joint, SoloReason.None, id);
    }

    /// <summary>Solo, because nobody was asked. The state of an order that has
    /// not reached its survey yet.</summary>
    public static SurveyParticipation NotAsked =>
        new(SurveyParty.Solo, SoloReason.NoProvider, PresenceContract.Companions.Hulgi);

    /// <summary>What the order panel and <c>cf_collect</c> show while
    /// surveying. The display name is passed in because it is the other
    /// product's to localize and the player's to edit; this layer only knows
    /// the id.</summary>
    public string Describe(string? companionDisplayName = null)
    {
        string who = string.IsNullOrEmpty(companionDisplayName) ? "Hulgi" : companionDisplayName!;
        switch (Party)
        {
            case SurveyParty.Joint:
                return "surveying together with " + who;
            case SurveyParty.Solo:
                switch (Reason)
                {
                    case SoloReason.NotKnown:
                        return "surveying (Thorstein alone)";
                    case SoloReason.NotHere:
                        return "surveying (Thorstein alone; " + who + " is not here)";
                    case SoloReason.Busy:
                        return "surveying (Thorstein alone; " + who + " is busy)";
                    default:
                        return "surveying (Thorstein alone)";
                }

            default:
                throw new InvalidOperationException("A survey party this build does not know; that is a bug.");
        }
    }
}
