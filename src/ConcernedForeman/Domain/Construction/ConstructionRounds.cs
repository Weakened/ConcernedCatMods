using System;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>How one round of building ended.
///
/// <b>Seven answers, and the difference between them is what the next round
/// does.</b> A round that ran out of trips and a round that ran out of material
/// are both "not finished", and one of them is fixed by going round again while
/// the other is fixed by the player. Collapsing them gives an NPC that either
/// gives up on a big cottage or walks in circles beside an empty chest.
/// </summary>
internal enum RoundVerdict
{
    /// <summary>No round was run. The default, and never a result.</summary>
    Unspecified = 0,

    /// <summary>A plan was made and carried out, in whole or in part.</summary>
    Planned = 1,

    /// <summary>There was nothing left to do.</summary>
    NothingToDo = 2,

    /// <summary>The material for what is left is not in any chest he may use.
    /// Carries the list.</summary>
    ShortOfMaterial = 3,

    /// <summary>A chest the order depends on could not be opened - somebody is
    /// in it, it is out of the loaded world, a ward refuses it. <b>The order
    /// waits</b>; it does not fail and it does not substitute another
    /// chest.</summary>
    ContainerUnavailable = 4,

    /// <summary>The work area itself could not be read or no longer
    /// exists.</summary>
    AreaInvalid = 5,

    /// <summary>The looking was not finished, so nothing about this round means
    /// anything yet. Ask again.</summary>
    NotFinishedLooking = 6,

    /// <summary>The round was refused for a reason that will not change by
    /// asking again.</summary>
    Refused = 7,
}

/// <summary>What one round of building did.
///
/// <b><see cref="LeftForAnotherRound"/> is the number this type exists to
/// carry.</b> The shared planner caps how many trips one plan writes out and how
/// many chests one provisioning phase opens, and it reports honestly how many
/// targets those caps left behind. Until something reads that number, a cottage
/// bigger than one plan stops after one plan and looks, from the player's side,
/// exactly like a bug. This is the reader.</summary>
internal readonly struct BuildRoundOutcome
{
    internal BuildRoundOutcome(
        RoundVerdict verdict,
        int serviced,
        int leftForAnotherRound,
        int deferred,
        MaterialTally? missing,
        MaterialTally? stillCarried,
        string? reason)
    {
        Verdict = verdict;
        Serviced = serviced < 0 ? 0 : serviced;
        LeftForAnotherRound = leftForAnotherRound < 0 ? 0 : leftForAnotherRound;
        Deferred = deferred < 0 ? 0 : deferred;
        Missing = missing ?? new MaterialTally();
        StillCarried = stillCarried ?? new MaterialTally();
        Reason = reason ?? string.Empty;
    }

    /// <summary>How it ended.</summary>
    internal RoundVerdict Verdict { get; }

    /// <summary>How many pieces were actually built.</summary>
    internal int Serviced { get; }

    /// <summary>How many pieces of the job this round's plan never reached,
    /// because the job needs more trips than one plan writes out or more chests
    /// than one provisioning phase opens.</summary>
    internal int LeftForAnotherRound { get; }

    /// <summary>How many stops were put off rather than done or abandoned - a
    /// piece whose phase is not its turn yet, a piece on ground that had not
    /// loaded.</summary>
    internal int Deferred { get; }

    /// <summary>What could not be found, when the verdict is
    /// <see cref="RoundVerdict.ShortOfMaterial"/>.</summary>
    internal MaterialTally Missing { get; }

    /// <summary>What he is still carrying after the round reconciled. Material
    /// fetched for a piece that turned out to be done already stays with him
    /// rather than being walked back, and the next round plans against it.
    /// </summary>
    internal MaterialTally StillCarried { get; }

    /// <summary>The sentence the runtime should say, when there is one.
    /// </summary>
    internal string Reason { get; }

    /// <summary>Whether this round left work its own plan did not cover. The
    /// planner's own question, asked in the role's words.</summary>
    internal bool NeedsAnotherRound => LeftForAnotherRound > 0 || Deferred > 0;

    /// <summary>Whether anything at all happened.</summary>
    internal bool MadeProgress => Serviced > 0;

    public override string ToString() =>
        Verdict + ": built " + Serviced + ", " + LeftForAnotherRound + " left, " +
        Deferred + " deferred";
}

/// <summary>What to do after a round.</summary>
internal enum RoundDecision
{
    /// <summary>Plan and walk another round now.</summary>
    PlanAnother = 0,

    /// <summary>Hold the order open and try again when the world changes. The
    /// order is not finished and it is not failed.</summary>
    Wait = 1,

    /// <summary>The shelter is finished.</summary>
    Finished = 2,

    /// <summary>Stop, and say why. Reservations are released and custody is
    /// reconciled by the caller; this only decides that there is no point
    /// continuing.</summary>
    Stop = 3,
}

/// <summary>The round loop: whether to go round again, wait, or stop.
///
/// <b>Why this is a type and not an <c>if</c>.</b> Three of the four ways a
/// build can fail to finish are indistinguishable from a single round -
/// a plan capped by its own trip limit, a phase that is not its turn yet, and a
/// world that would answer differently in ten seconds all produce "not
/// finished". Deciding between them needs the previous rounds, and a decision
/// that needs history either gets a type or gets scattered across a runtime as
/// four fields nobody can test.
///
/// <b>The anti-spin rule, stated once.</b> The world can hold "every stop is
/// deferred" true indefinitely - a chest the player is standing in, ground that
/// never loads - and an NPC that answered by planning again would think for as
/// long as anyone watched. So a round that builds nothing counts against a small
/// budget, and a round that builds something clears it. A build that is really
/// progressing never runs the budget down; one that is not stops with a sentence
/// rather than spinning.</summary>
internal sealed class ConstructionRounds
{
    /// <summary>How many rounds in a row may build nothing before the order
    /// stops asking. Three rather than one, because a legitimate reason to
    /// build nothing exists - the first round of a phase whose material is in a
    /// chest across camp - and because two of the runtime's own caps can chain.
    ///
    /// <b>Never written to disk.</b> Changing it must stay free.</summary>
    internal const int FruitlessRoundsAllowed = 3;

    /// <summary>A ceiling on rounds per order, so that a shelter whose every
    /// round makes one piece of progress against a pathological world still
    /// ends. Seventeen pieces need at most seventeen rounds if each round builds
    /// exactly one, and this leaves generous room above that.</summary>
    internal const int RoundsAllowed = 64;

    private int _rounds;
    private int _fruitless;

    /// <summary>How many rounds have been run for this order.</summary>
    internal int Rounds => _rounds;

    /// <summary>How many of the most recent rounds built nothing.</summary>
    internal int FruitlessRounds => _fruitless;

    /// <summary>The sentence for the last decision, or empty.</summary>
    internal string Reason { get; private set; } = string.Empty;

    /// <summary>Records a round and says what to do next.</summary>
    /// <param name="outcome">What the round did.</param>
    /// <param name="progress">What the world looks like after it. The authority
    /// on "finished" - a round can report everything serviced and still be
    /// looking at a cottage with a hole in it, which is the failure the shared
    /// runtime's own reviewers opened with.</param>
    internal RoundDecision Next(in BuildRoundOutcome outcome, ConstructionProgress? progress)
    {
        _rounds++;
        _fruitless = outcome.MadeProgress ? 0 : _fruitless + 1;

        // The world decides whether it is finished, not the plan. A plan that
        // covered eight pieces of twelve can report every step done.
        if (progress != null && progress.IsComplete)
        {
            Reason = "the shelter is finished.";
            return RoundDecision.Finished;
        }

        switch (outcome.Verdict)
        {
            case RoundVerdict.NothingToDo:
                // Nothing to do, and the world does not agree it is finished.
                // That is only honest if the looking finished; otherwise it is
                // an unloaded zone talking.
                if (progress != null && !progress.IsConclusive)
                {
                    Reason = "not all of the site could be seen, so nothing has been concluded.";
                    return Budgeted(RoundDecision.Wait);
                }

                Reason = BlockedSentence(progress) ??
                    "there is nothing left that can be built.";
                return RoundDecision.Stop;

            case RoundVerdict.ShortOfMaterial:
                Reason = ConstructionSentences.ShortOfMaterial(outcome.Missing);
                return RoundDecision.Wait;

            case RoundVerdict.ContainerUnavailable:
                Reason = ConstructionSentences.ContainerUnavailable(outcome.Reason);
                return RoundDecision.Wait;

            case RoundVerdict.AreaInvalid:
                Reason = outcome.Reason.Length != 0
                    ? outcome.Reason
                    : "the build site could not be read, so the order is on hold.";
                return RoundDecision.Stop;

            case RoundVerdict.Refused:
                Reason = outcome.Reason.Length != 0
                    ? outcome.Reason
                    : "the order was refused.";
                return RoundDecision.Stop;

            case RoundVerdict.NotFinishedLooking:
                Reason = "the site has not been fully seen yet.";
                return Budgeted(RoundDecision.Wait);

            case RoundVerdict.Planned:
                if (outcome.NeedsAnotherRound || (progress != null && !progress.IsComplete))
                {
                    Reason = Carrying(outcome);
                    return Budgeted(RoundDecision.PlanAnother);
                }

                Reason = "the round finished.";
                return RoundDecision.Finished;

            default:
                Reason = "the round did not say how it ended, so the order stops rather than guessing.";
                return RoundDecision.Stop;
        }
    }

    private RoundDecision Budgeted(RoundDecision wanted)
    {
        if (_rounds >= RoundsAllowed)
        {
            Reason = "the order has run " + _rounds +
                " rounds without finishing, so it stops rather than going round again. " + Reason;
            return RoundDecision.Stop;
        }

        if (_fruitless >= FruitlessRoundsAllowed)
        {
            Reason = "nothing has been built for " + _fruitless +
                " rounds, so the order waits rather than walking in circles. " + Reason;
            return RoundDecision.Wait;
        }

        return wanted;
    }

    private static string Carrying(in BuildRoundOutcome outcome)
    {
        string built = "built " + outcome.Serviced +
            (outcome.Serviced == 1 ? " piece" : " pieces");
        string left = outcome.LeftForAnotherRound > 0
            ? ", " + outcome.LeftForAnotherRound + " more than this plan covered"
            : string.Empty;
        string deferred = outcome.Deferred > 0
            ? ", " + outcome.Deferred + " left for the next round"
            : string.Empty;
        string carried = outcome.StillCarried.IsEmpty
            ? string.Empty
            : ", still carrying " + outcome.StillCarried.Describe();
        return built + left + deferred + carried + ".";
    }

    private static string? BlockedSentence(ConstructionProgress? progress)
    {
        if (progress == null || progress.Blocked.Count == 0)
        {
            return null;
        }

        var first = progress.Blocked[0];
        return "something is in the way of " + first.Placement.Piece.Prefab + " at " +
            first.Placement.At + (progress.Blocked.Count > 1
                ? " and " + (progress.Blocked.Count - 1) + " other places"
                : string.Empty) +
            ". Clear it and the order goes on.";
    }
}
