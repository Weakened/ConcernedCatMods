using System;

namespace TheConcernedCat.ConcernedSteward.Domain.Recruitment;

/// <summary>How far the Steward's introduction has got.
///
/// Four stages, deliberately few. #340 asks for "a small introduction
/// appropriate to an established settlement" — somebody who turns up because
/// there is already something worth looking after, not a quest chain. A player
/// who has built a base and marked it should be able to get from meeting him to
/// employing him in under a minute.
///
/// <see cref="Unmet"/> is zero, so a record that was never written, or could
/// not be read, starts at the beginning rather than part way through.</summary>
internal enum IntroductionStage
{
    /// <summary>Nothing has happened. The default, and what an unreadable
    /// record reads as.</summary>
    Unmet = 0,

    /// <summary>He has taken note of the settlement: there is ground marked,
    /// and somebody is living on it.</summary>
    Noticed = 1,

    /// <summary>He has offered to keep it running. The player has not answered.
    /// </summary>
    Offered = 2,

    /// <summary>The player accepted. He is employed.</summary>
    Engaged = 3,
}

/// <summary>Why an introduction step was refused. Zero is "no reason recorded",
/// which is a defect and is named so it shows up as one.</summary>
internal enum IntroductionRefusal
{
    Unspecified = 0,

    /// <summary>The runtime is off, or this process may not act here.</summary>
    NotAuthorised = 1,

    /// <summary>There is no settlement to look after, and the whole premise of
    /// this character is that there is one.</summary>
    NoSettlementArea = 2,

    /// <summary>The record could not be written, so nothing was advanced.
    /// Recruitment fails closed (`CLAUDE.md`): an unwritable record refuses
    /// rather than advancing in memory and hoping.</summary>
    RecordNotWritable = 3,

    /// <summary>The step asked for skips one. The introduction is short enough
    /// that skipping is a caller bug rather than a shortcut.</summary>
    OutOfOrder = 4,
}

internal enum IntroductionOutcome
{
    /// <summary>Nothing changed and something was wrong. Zero.</summary>
    Refused = 0,

    Advanced = 1,

    /// <summary>Already at least this far. Idempotent, and the normal answer to
    /// a repeated command or a replayed step after a reload.</summary>
    AlreadyThere = 2,
}

/// <summary>The answer to one introduction step.</summary>
internal readonly struct IntroductionResult
{
    private IntroductionResult(
        IntroductionOutcome outcome, IntroductionRefusal refusal, IntroductionStage stage)
    {
        Outcome = outcome;
        Refusal = refusal;
        Stage = stage;
    }

    public IntroductionOutcome Outcome { get; }

    public IntroductionRefusal Refusal { get; }

    /// <summary>Where the introduction stands now, whatever happened.</summary>
    public IntroductionStage Stage { get; }

    public bool Changed => Outcome == IntroductionOutcome.Advanced;

    public static IntroductionResult Advanced(IntroductionStage stage) =>
        new IntroductionResult(IntroductionOutcome.Advanced, IntroductionRefusal.Unspecified, stage);

    public static IntroductionResult Already(IntroductionStage stage) =>
        new IntroductionResult(IntroductionOutcome.AlreadyThere, IntroductionRefusal.Unspecified, stage);

    public static IntroductionResult Refused(IntroductionRefusal refusal, IntroductionStage stage) =>
        new IntroductionResult(IntroductionOutcome.Refused, refusal, stage);

    public string Describe()
    {
        switch (Outcome)
        {
            case IntroductionOutcome.Advanced:
                return StewardSentences.ForStage(Stage);

            case IntroductionOutcome.AlreadyThere:
                return StewardSentences.AlreadyAtStage(Stage);

            default:
                return "Not yet: " + DescribeRefusal() + ".";
        }
    }

    private string DescribeRefusal()
    {
        switch (Refusal)
        {
            case IntroductionRefusal.NotAuthorised:
                return "the Steward is switched off, or this is not a game she can work in";

            case IntroductionRefusal.NoSettlementArea:
                return "there is nothing marked for her to look after. Stand in the middle of " +
                    "your settlement and run \"cs_steward area <radius>\"";

            case IntroductionRefusal.RecordNotWritable:
                return "her record could not be written, so nothing was agreed. Nothing is ever " +
                    "agreed in memory alone";

            case IntroductionRefusal.OutOfOrder:
                return "that is not the next thing that happens";

            default:
                return "no reason was recorded, which is a bug — please report it";
        }
    }

    public override string ToString() => Describe();
}

/// <summary>What the runtime could establish when an introduction step was
/// asked for.</summary>
internal readonly struct IntroductionFacts
{
    public IntroductionFacts(bool authorised, bool hasSettlementArea, bool recordWritable)
    {
        Authorised = authorised;
        HasSettlementArea = hasSettlementArea;
        RecordWritable = recordWritable;
    }

    public bool Authorised { get; }

    public bool HasSettlementArea { get; }

    public bool RecordWritable { get; }
}

/// <summary>The Steward's introduction, and the one property that makes it
/// resumable: <b>it never goes backwards on its own</b>.
///
/// <list type="bullet">
/// <item><b><see cref="Stage"/> is where things stand.</b> It moves forward one
/// step at a time and is lowered by exactly one thing: the player explicitly
/// dismissing him.</item>
/// <item><b><see cref="FurthestReached"/> never falls.</b> Not on dismissal,
/// not on a reload, not when his body is lost, not when the settlement is
/// un-marked. It is the record that this player and this character have already
/// met, and it is why re-hiring somebody you dismissed does not make you sit
/// through the introduction again.</item>
/// </list>
///
/// <b>The two halves of the monotonicity rule, kept apart.</b> `CLAUDE.md` says
/// feature access is monotonic and ambiguous evidence grants it — that is
/// <see cref="FurthestReached"/>, which is presentation and costs nothing if it
/// is wrong. It also says recruitment into a worker runtime <i>fails closed</i>
/// — that is <see cref="Stage"/> reaching <see cref="IntroductionStage.Engaged"/>,
/// which is authority over a player's materials, and every refusal below exists
/// to keep it that way. Conflating the two would either make a player repeat an
/// introduction after every reload or hand a worker authority nobody granted.
/// </summary>
internal sealed class StewardIntroduction
{
    private IntroductionStage _stage;
    private IntroductionStage _furthest;

    public IntroductionStage Stage => _stage;

    /// <summary>The furthest the introduction has ever got. Never falls.
    /// </summary>
    public IntroductionStage FurthestReached => _furthest;

    /// <summary>True when he is employed right now.</summary>
    public bool IsEngaged => _stage == IntroductionStage.Engaged;

    /// <summary>True when this player has employed him at some point, whether
    /// or not they still do. What the introduction prose is skipped on.
    /// </summary>
    public bool HasEverEngaged => _furthest == IntroductionStage.Engaged;

    /// <summary>Moves on every change, so a caller can tell a read is stale and
    /// a store knows there is something to write.</summary>
    public int Revision { get; private set; }

    /// <summary>Takes the next step, or explains why not.
    ///
    /// The checks are in the order a player would fix them, and
    /// <paramref name="facts"/> is passed in rather than read from anywhere so
    /// that every combination — including the ones that are hard to arrange in
    /// a live game — is one struct literal away in a test.</summary>
    public IntroductionResult Advance(IntroductionStage to, in IntroductionFacts facts)
    {
        if (to == IntroductionStage.Unmet)
        {
            // There is no step "back to not having met". Dismissal is its own
            // explicit act and does not travel through here.
            return IntroductionResult.Refused(IntroductionRefusal.OutOfOrder, _stage);
        }

        if (to <= _stage)
        {
            return IntroductionResult.Already(_stage);
        }

        if ((int)to != (int)_stage + 1)
        {
            return IntroductionResult.Refused(IntroductionRefusal.OutOfOrder, _stage);
        }

        if (!facts.Authorised)
        {
            return IntroductionResult.Refused(IntroductionRefusal.NotAuthorised, _stage);
        }

        if (!facts.HasSettlementArea)
        {
            return IntroductionResult.Refused(IntroductionRefusal.NoSettlementArea, _stage);
        }

        if (!facts.RecordWritable)
        {
            return IntroductionResult.Refused(IntroductionRefusal.RecordNotWritable, _stage);
        }

        _stage = to;
        if (to > _furthest)
        {
            _furthest = to;
        }

        Revision++;
        return IntroductionResult.Advanced(_stage);
    }

    /// <summary>Lets him go.
    ///
    /// <b>The only thing that lowers <see cref="Stage"/>, and it never touches
    /// <see cref="FurthestReached"/>.</b> He goes back to having offered, not to
    /// being a stranger, because the player has met him and pretending
    /// otherwise would be a worse lie than the small inconsistency of an
    /// unemployed man who is still on first-name terms.
    ///
    /// Dismissing somebody who is not employed is not an error; it answers
    /// false and changes nothing.</summary>
    public bool Dismiss()
    {
        if (_stage != IntroductionStage.Engaged)
        {
            return false;
        }

        _stage = IntroductionStage.Offered;
        Revision++;
        return true;
    }

    /// <summary>Restores what a record said, without re-running any check.
    ///
    /// <b>Loading is not advancing.</b> The checks that ran when the player took
    /// each step ran against the game as it was then; re-running them at load
    /// would mean a settlement un-marked for five minutes quietly un-hired
    /// somebody.
    ///
    /// The two values are clamped against each other rather than trusted: a
    /// record claiming a stage beyond its own high-water mark is damaged, and
    /// the repair is to raise the mark, never to lower the stage — lowering it
    /// is the one direction this type exists to prevent.</summary>
    internal void Restore(IntroductionStage stage, IntroductionStage furthest)
    {
        _stage = Clamp(stage);
        _furthest = Clamp(furthest);
        if (_stage > _furthest)
        {
            _furthest = _stage;
        }

        Revision++;
    }

    private static IntroductionStage Clamp(IntroductionStage value)
    {
        if (value < IntroductionStage.Unmet)
        {
            return IntroductionStage.Unmet;
        }

        return value > IntroductionStage.Engaged ? IntroductionStage.Engaged : value;
    }

    public override string ToString() =>
        _stage + (_furthest > _stage ? " (has been " + _furthest + ")" : string.Empty);
}

/// <summary>Everything the Steward says, in one place.
///
/// <b>Owner-editable, and deliberately separated from the state machine.</b>
/// #340 fixes the state machine and its recovery and leaves the prose and the
/// name open. Keeping every line here means renaming him or rewriting his
/// introduction is an edit to this file and nothing else — no state to migrate,
/// no test to rewrite, and no risk of changing behaviour by changing words.
///
/// The lines avoid a name on purpose: see <see cref="StewardRole"/>. They also
/// avoid implying he came from anywhere in particular, because that is a
/// setting decision the owner has not made.</summary>
internal static class StewardSentences
{
    public static string ForStage(IntroductionStage stage)
    {
        switch (stage)
        {
            case IntroductionStage.Noticed:
                return StewardRole.DisplayNameFallbackCapitalised + " has been watching your " +
                    "settlement. Somebody has been keeping the fires in, she says, and doing " +
                    "it badly.";

            case IntroductionStage.Offered:
                return "She offers to take that off your hands: mark the chest she may draw from, " +
                    "and she will see the fires stay lit.";

            case IntroductionStage.Engaged:
                return StewardRole.DisplayNameFallbackCapitalised +
                    " has taken the work. Turn on \"cs_steward tend on\" when you want her to start.";

            default:
                return "Nothing has happened yet.";
        }
    }

    public static string AlreadyAtStage(IntroductionStage stage)
    {
        switch (stage)
        {
            case IntroductionStage.Noticed:
                return "She has already noticed. She is waiting to be asked.";

            case IntroductionStage.Offered:
                return "She has already offered. Say yes with \"cs_steward recruit\".";

            case IntroductionStage.Engaged:
                return StewardRole.DisplayNameFallbackCapitalised +
                    " already works here. Nothing changed.";

            default:
                return "Nothing has happened yet.";
        }
    }

    /// <summary>The line shown when somebody re-hires a Steward they had let
    /// go. The introduction is not repeated: he has been here before.</summary>
    public static string WelcomeBack() =>
        StewardRole.DisplayNameFallbackCapitalised + " is back at work. No need for introductions.";

    public static string Dismissed() =>
        StewardRole.DisplayNameFallbackCapitalised + " has been let go. She keeps nothing; " +
        "anything she was carrying is accounted for in her record.";
}
