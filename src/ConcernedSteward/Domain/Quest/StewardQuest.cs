using System;

namespace TheConcernedCat.ConcernedSteward.Domain.Quest;

/// <summary>How far Sunniva's introduction has got.
///
/// <b>The numbers are durable.</b> They are written into the Steward's record as
/// integers and must never be renumbered; a new beat is appended. Zero is where
/// a record that was never written, or could not be read, starts.</summary>
internal enum StewardQuestStage
{
    /// <summary>Nothing has been found. The one stage from which the quest
    /// object can still be produced.</summary>
    Unstarted = 0,

    /// <summary><b>The flint and steel have been found, and that has happened
    /// for the last time.</b> Reaching this stage is the once-ever event this
    /// whole type exists to make once-ever.</summary>
    Found = 1,

    /// <summary>The runes have been read.</summary>
    Examined = 2,

    /// <summary>She has answered - who she was, and what she thinks a fire
    /// is.</summary>
    Answered = 3,

    /// <summary>After being taken on, she has moved in. The last beat, and the
    /// only one that follows recruitment rather than leading to it.</summary>
    Settled = 4,
}

/// <summary>Why a beat did not happen. Zero is "nobody recorded a reason", which
/// is a defect and is named so it shows up as one.</summary>
internal enum StewardQuestRefusal
{
    Unspecified = 0,

    /// <summary>The object has already been found, in this world, once. There is
    /// one of it.</summary>
    AlreadyFound = 1,

    /// <summary>The record could not be written, so nothing was granted. The
    /// single most important refusal here: a quest object granted in memory and
    /// not written down is granted again on the next load, and then there are
    /// two.</summary>
    NotRecorded = 2,

    /// <summary>The step asked for skips one, or goes backwards.</summary>
    OutOfOrder = 3,

    /// <summary>There is nobody to find anything. No local player, no loaded
    /// world.</summary>
    NobodyThere = 4,
}

internal enum StewardQuestOutcome
{
    /// <summary>Nothing changed and something was wrong. Zero.</summary>
    Refused = 0,

    Advanced = 1,

    /// <summary>Already at least this far. The normal answer to a repeated
    /// command, a second resin pickup, or a replayed step after a reload.
    /// </summary>
    AlreadyThere = 2,
}

/// <summary>What one attempt at a beat produced.</summary>
internal readonly struct StewardQuestResult
{
    private StewardQuestResult(
        StewardQuestOutcome outcome, StewardQuestRefusal refusal, StewardQuestStage stage)
    {
        Outcome = outcome;
        Refusal = refusal;
        Stage = stage;
    }

    public StewardQuestOutcome Outcome { get; }

    public StewardQuestRefusal Refusal { get; }

    /// <summary>Where the quest stands now, whatever happened.</summary>
    public StewardQuestStage Stage { get; }

    public bool Changed => Outcome == StewardQuestOutcome.Advanced;

    internal static StewardQuestResult Advanced(StewardQuestStage stage) =>
        new StewardQuestResult(StewardQuestOutcome.Advanced, StewardQuestRefusal.Unspecified, stage);

    internal static StewardQuestResult Already(StewardQuestStage stage) =>
        new StewardQuestResult(StewardQuestOutcome.AlreadyThere, StewardQuestRefusal.Unspecified, stage);

    internal static StewardQuestResult Refused(StewardQuestRefusal refusal, StewardQuestStage stage) =>
        new StewardQuestResult(StewardQuestOutcome.Refused, refusal, stage);

    /// <summary>What to say, or nothing. An unchanged quest says nothing at all:
    /// a player who picks up their hundredth resin is not told about a flint and
    /// steel they already have.</summary>
    public string? Announcement =>
        Changed ? StewardQuestSentences.ForStage(Stage) : null;
}

/// <summary>What the runtime could establish when a beat was asked for.
/// Constructed at the call site rather than read from anywhere, so every awkward
/// combination is one struct literal away in a test.</summary>
internal readonly struct StewardQuestFacts
{
    public StewardQuestFacts(bool somebodyIsThere, bool recordWritable)
    {
        SomebodyIsThere = somebodyIsThere;
        RecordWritable = recordWritable;
    }

    /// <summary>There is a local player in a loaded world to find something.
    /// </summary>
    public bool SomebodyIsThere { get; }

    /// <summary>The Steward's record can be written right now. A damaged or
    /// unwritable record refuses the grant rather than making it in memory.
    /// </summary>
    public bool RecordWritable { get; }
}

/// <summary>Writing a quest stage down, before it is believed.
///
/// <b>Why the quest holds a recorder instead of the runtime saving
/// afterwards.</b> "One quest object, ever" is a promise about a crash. Advance
/// first and save second, and a game that closes in between comes back at
/// <see cref="StewardQuestStage.Unstarted"/> with the object already announced,
/// and the next resin produces a second one. So the order is inverted: the stage
/// is offered to the record, and it only becomes true if the record took it.
/// <see cref="TryRecord"/> returning false means nothing happened at all.
/// </summary>
internal interface IStewardQuestRecorder
{
    /// <summary>Writes this stage as the quest's new stage, and its high-water
    /// mark if it is higher. True only if it reached the disk.</summary>
    bool TryRecord(StewardQuestStage stage, StewardQuestStage furthest);
}

/// <summary>Sunniva's introduction, and the one thing about it that must be
/// true: <b>there is one flint and steel and there will only ever be one.</b>
///
/// <b>How that is guaranteed, in three parts, because one is not enough.</b>
///
/// <list type="number">
/// <item><b>The high-water mark, not the stage, is what the grant is refused
/// on.</b> <see cref="FurthestReached"/> never falls - not on a reload, not on a
/// dismissal, not on a corrupt record repaired by clamping. A check against
/// <see cref="Stage"/> would be correct today and wrong the first time anything
/// moves the stage backwards.</item>
/// <item><b>The record is written before the object exists.</b> The stage is
/// offered to <see cref="IStewardQuestRecorder"/> and is only adopted if the
/// write landed, so the crash window between "she has been found" and "it is
/// written down" is closed rather than narrowed.</item>
/// <item><b>The find is a transition and not an event.</b> There is no method
/// that produces the object; there is a method that moves
/// <see cref="StewardQuestStage.Unstarted"/> to
/// <see cref="StewardQuestStage.Found"/>, once, and the announcement is derived
/// from the transition. Nothing can announce twice without moving the stage
/// twice, and the stage cannot move to Found twice.</item>
/// </list>
///
/// <b>And it is a discovery, not an item.</b> Nothing here adds anything to a
/// player's inventory and nothing here touches the world. The flint and steel is
/// a thing the player is told about and the mod remembers, exactly as the Broken
/// Compass's memento is in Concerned Cartographer - which is what keeps this
/// inside the local-only presentation rule and keeps material conserved: a mod
/// that minted a real item into a real inventory would be conjuring one.
///
/// <b>What this is not.</b> It is not recruitment. Recruitment is
/// <c>StewardIntroduction</c> and it fails closed, because it is authority over
/// a player's materials. This is presentation and it is monotonic, because it is
/// a story somebody has already been told. `CLAUDE.md` separates the two
/// deliberately and so does this product; the two run beside each other and the
/// quest never advances the introduction on its own.</summary>
internal sealed class StewardQuest
{
    private readonly IStewardQuestRecorder _recorder;

    private StewardQuestStage _stage;
    private StewardQuestStage _furthest;

    internal StewardQuest(IStewardQuestRecorder recorder)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public StewardQuestStage Stage => _stage;

    /// <summary>The furthest the quest has ever got. Never falls, and it is what
    /// the once-ever grant is refused on.</summary>
    public StewardQuestStage FurthestReached => _furthest;

    /// <summary>True once the flint and steel have been found. <b>Never becomes
    /// false again</b>, in this world, for any reason.</summary>
    public bool ObjectHasBeenFound => _furthest >= StewardQuestStage.Found;

    /// <summary>Moves on every change, so a caller can tell a read is stale.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>A player has just picked up resin. Produces the one flint and
    /// steel there is, or explains why it did not.
    ///
    /// Total and cheap to call: this runs off an inventory change, so the
    /// overwhelmingly common answer is <see cref="StewardQuestOutcome.AlreadyThere"/>
    /// and it costs one enum comparison.</summary>
    public StewardQuestResult NoticeResinPickedUp(in StewardQuestFacts facts)
    {
        if (ObjectHasBeenFound)
        {
            return StewardQuestResult.Already(_stage);
        }

        if (!facts.SomebodyIsThere)
        {
            return StewardQuestResult.Refused(StewardQuestRefusal.NobodyThere, _stage);
        }

        if (!facts.RecordWritable)
        {
            return StewardQuestResult.Refused(StewardQuestRefusal.NotRecorded, _stage);
        }

        return Advance(StewardQuestStage.Found, facts);
    }

    /// <summary>Takes the next beat, or explains why not. Strictly one step at a
    /// time: the quest is five beats long and skipping one is a caller's bug
    /// rather than a shortcut.</summary>
    public StewardQuestResult Advance(StewardQuestStage to, in StewardQuestFacts facts)
    {
        if (to == StewardQuestStage.Unstarted)
        {
            // There is no step back to not having found anything.
            return StewardQuestResult.Refused(StewardQuestRefusal.OutOfOrder, _stage);
        }

        if (to <= _stage)
        {
            return StewardQuestResult.Already(_stage);
        }

        if ((int)to != (int)_stage + 1)
        {
            return StewardQuestResult.Refused(StewardQuestRefusal.OutOfOrder, _stage);
        }

        if (!facts.SomebodyIsThere)
        {
            return StewardQuestResult.Refused(StewardQuestRefusal.NobodyThere, _stage);
        }

        if (!facts.RecordWritable)
        {
            return StewardQuestResult.Refused(StewardQuestRefusal.NotRecorded, _stage);
        }

        StewardQuestStage furthest = to > _furthest ? to : _furthest;
        if (!Record(to, furthest))
        {
            // The write is the transition. Nothing moved, so nothing is
            // announced and the next attempt starts from exactly here.
            return StewardQuestResult.Refused(StewardQuestRefusal.NotRecorded, _stage);
        }

        _stage = to;
        _furthest = furthest;
        Revision++;
        return StewardQuestResult.Advanced(_stage);
    }

    /// <summary>Restores what a record said, without re-running any check.
    ///
    /// Clamped against each other the same way the introduction's are, and for
    /// the same reason: a record claiming a stage beyond its own high-water mark
    /// is damaged, and the repair is to raise the mark, never to lower the
    /// stage. Lowering it is the one direction that could produce a second flint
    /// and steel.</summary>
    internal void Restore(StewardQuestStage stage, StewardQuestStage furthest)
    {
        _stage = Clamp(stage);
        _furthest = Clamp(furthest);
        if (_stage > _furthest)
        {
            _furthest = _stage;
        }

        Revision++;
    }

    private bool Record(StewardQuestStage stage, StewardQuestStage furthest)
    {
        try
        {
            return _recorder.TryRecord(stage, furthest);
        }
        catch (Exception)
        {
            // A recorder that throws is a recorder that did not write. Treated
            // as a refusal rather than allowed to escape: the alternative is an
            // exception out of an inventory-changed callback, which would take
            // the player's own inventory handler out with it.
            return false;
        }
    }

    private static StewardQuestStage Clamp(StewardQuestStage value)
    {
        if (value < StewardQuestStage.Unstarted)
        {
            return StewardQuestStage.Unstarted;
        }

        return value > StewardQuestStage.Settled ? StewardQuestStage.Settled : value;
    }

    public override string ToString() =>
        _stage + (_furthest > _stage ? " (has been " + _furthest + ")" : string.Empty);
}
