using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Net;

namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>Classifies each nearby player's effort on a shared cart and
/// composes a combined-effort explanation for a stuck verdict (CT-028).
/// Pure and deterministic: it reads reduced observations, never game state,
/// and never applies or requests force — the output is advice, not a push.
/// The classification order is fixed so identical traces always produce the
/// identical verdict.</summary>
public static class CooperativeEffortClassifier
{
    /// <summary>Motion projection magnitude below which a contact is treated
    /// as no contribution (idle touch), not help or hindrance.</summary>
    public const float MeaningfulAlignment = 0.15f;

    public static CoopEffort Classify(in CoopParticipant participant)
    {
        // Pulling the handle is help by definition — the puller is doing the
        // haul's work whether or not the cart is currently moving.
        if (participant.IsAttached)
        {
            return CoopEffort.Helping;
        }

        if (!participant.InContact)
        {
            return CoopEffort.Idle;
        }

        float alignment = participant.MotionAlignment;
        if (float.IsNaN(alignment))
        {
            return CoopEffort.Unclear;
        }

        if (alignment > MeaningfulAlignment)
        {
            return CoopEffort.Helping;
        }

        if (alignment < -MeaningfulAlignment)
        {
            return CoopEffort.Hindering;
        }

        return CoopEffort.Idle;
    }

    /// <summary>Counts of each effort across the participants.</summary>
    public readonly struct EffortTally
    {
        public EffortTally(int helping, int hindering, int idle, int unclear)
        {
            Helping = helping;
            Hindering = hindering;
            Idle = idle;
            Unclear = unclear;
        }

        public int Helping { get; }

        public int Hindering { get; }

        public int Idle { get; }

        public int Unclear { get; }

        public int Contributors => Helping + Hindering;
    }

    public static EffortTally Tally(IReadOnlyList<CoopParticipant> participants)
    {
        int helping = 0, hindering = 0, idle = 0, unclear = 0;
        for (int index = 0; index < participants.Count; index++)
        {
            switch (Classify(participants[index]))
            {
                case CoopEffort.Helping: helping++; break;
                case CoopEffort.Hindering: hindering++; break;
                case CoopEffort.Unclear: unclear++; break;
                default: idle++; break;
            }
        }

        return new EffortTally(helping, hindering, idle, unclear);
    }

    /// <summary>A one-line cooperative summary, or empty when nobody is
    /// helping or hindering (no cooperation to describe). Names the
    /// helpers/hinderers using only their already-visible in-game names,
    /// capped so the line stays readable.</summary>
    public static string Summarize(IReadOnlyList<CoopParticipant> participants)
    {
        EffortTally tally = Tally(participants);
        if (tally.Contributors == 0 && tally.Unclear == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(3);
        if (tally.Helping > 0)
        {
            parts.Add(TeamsterStrings.Format(
                    "coop.helpingCount", tally.Helping.ToString(CultureInfo.InvariantCulture)) +
                NameList(participants, CoopEffort.Helping));
        }

        if (tally.Hindering > 0)
        {
            parts.Add(TeamsterStrings.Format(
                    "coop.hinderingCount", tally.Hindering.ToString(CultureInfo.InvariantCulture)) +
                NameList(participants, CoopEffort.Hindering));
        }

        if (tally.Unclear > 0)
        {
            parts.Add(TeamsterStrings.Format(
                "coop.unclearCount", tally.Unclear.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Join(", ", parts);
    }

    /// <summary>Combines the cooperative context with the physical stuck
    /// verdict into one explanation: crews often stall because the load or
    /// grade beats them even with everyone helping, or because someone is
    /// pushing the wrong way. Effort never changes the physical diagnosis —
    /// it only explains it (no force is granted).</summary>
    public static string ExplainStuck(
        IReadOnlyList<CoopParticipant> participants, CartDiagnostic diagnosis)
    {
        string coop = Summarize(participants);
        string physical = diagnosis.ComposeLine();

        if (coop.Length == 0)
        {
            return physical;
        }

        EffortTally tally = Tally(participants);
        var builder = new StringBuilder();
        builder.Append(TeamsterStrings.Format("coop.crewLine", coop));

        if (physical.Length > 0)
        {
            // The physical cause stands regardless of how many push — say so,
            // so a crew does not keep heaving against an overload.
            builder.Append(' ');
            if (tally.Helping > 1 &&
                (diagnosis.Diagnosis == CartDiagnosis.ImpossibleLoad ||
                 diagnosis.Diagnosis == CartDiagnosis.SteepClimb))
            {
                builder.Append(TeamsterStrings.Format("coop.evenWithHelp", physical));
            }
            else
            {
                builder.Append(physical);
            }
        }
        else if (tally.Hindering > 0 && tally.Helping == 0)
        {
            builder.Append(' ').Append(TeamsterStrings.Get("coop.nobodyHelping"));
        }

        return builder.ToString();
    }

    private static string NameList(
        IReadOnlyList<CoopParticipant> participants, CoopEffort effort)
    {
        var names = new List<string>(2);
        for (int index = 0; index < participants.Count && names.Count < 2; index++)
        {
            CoopParticipant participant = participants[index];
            if (Classify(participant) != effort)
            {
                continue;
            }

            // CT-029: the name is network-derived, so length-cap and
            // control-strip it before it reaches a panel line.
            string safeName = NetworkInputGuard.Label(participant.DisplayName);
            names.Add(safeName.Length > 0
                ? safeName
                : TeamsterStrings.Get(participant.IsLocalPlayer ? "coop.you" : "coop.teammate"));
        }

        return names.Count > 0
            ? " " + TeamsterStrings.Format("coop.nameList", string.Join(", ", names))
            : string.Empty;
    }
}
