using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Persistence;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>One plan's durable state: read it, write it, and nothing else.
///
/// <b>Where the file is: the role's answer, never this type's.</b> An absolute
/// path arrives in <see cref="TryOpen"/> and is handed straight to the sidecar
/// that already exists for this. Nothing here composes a path, joins one, names
/// one or takes one apart - a shipped product decides whether a player is new or
/// returning by looking at the files in its own data root, so a shared runtime
/// able to drop a name in there is a permanent, silent wrong grant waiting for a
/// caller.
///
/// <b>What is in the file: the role's answer too.</b> The codec turns a plan
/// into lines and back. This type counts neither rows nor fields.
///
/// <b>What is this type's, and is the reason it exists.</b> Three rules a role
/// would have to get right four times otherwise.
///
/// A write is whole or not at all, because the sidecar's temporary-then-replace
/// is what makes a plan file that was being written when the process died read
/// back as either the whole previous plan or the whole new one - never a
/// half-plan, which is a plan whose phase and whose reservations disagree.
///
/// A file that exists and cannot be read is never written over. That is the one
/// rule where the convenient behaviour is catastrophic: the unreadable file is
/// the only copy of what the NPC is holding, and an empty one written over it is
/// a player's material with nothing left that says where it went.
///
/// And a plan that comes back from disk is never live. <see cref="Load"/> returns
/// it through <see cref="NpcPlanState.AsRecovered"/>, so its world is unknown,
/// its attempt count has risen and a pending movement has become an uncertain
/// one. A codec cannot forget to do that, because a codec is not what does it.
/// </summary>
internal sealed class NpcPlanJournal
{
    private readonly NpcSidecarFile _file;
    private readonly INpcPlanCodec _codec;

    private NpcPlanJournal(NpcSidecarFile file, INpcPlanCodec codec)
    {
        _file = file;
        _codec = codec;
    }

    /// <summary>Opens the journal for a plan file whose path and format the role
    /// owns, or says why it cannot.</summary>
    internal static bool TryOpen(
        string? absolutePath, INpcPlanCodec? codec, out NpcPlanJournal? journal, out string reason)
    {
        journal = null;

        if (codec == null)
        {
            reason = "a role must supply the codec that reads and writes its own plan rows; this library "
                + "owns no format";
            return false;
        }

        if (!NpcSidecarFile.TryAt(absolutePath, out NpcSidecarFile? file, out reason))
        {
            return false;
        }

        journal = new NpcPlanJournal(file!, codec);
        reason = string.Empty;
        return true;
    }

    /// <summary>Whether a plan file exists. About this one file and never about
    /// the directory it is in: counting files in a role's data root answers a
    /// different question and has already shipped twice as a permanent wrong
    /// grant.</summary>
    internal bool Exists => _file.Exists;

    /// <summary>Reads the plan back, already stale.</summary>
    internal NpcPlanLoad Load()
    {
        NpcSidecarRead read = _file.Read();
        switch (read.Outcome)
        {
            case NpcSidecarOutcome.Missing:
                return NpcPlanLoad.None();

            case NpcSidecarOutcome.Read:
                break;

            default:
                return NpcPlanLoad.Unreadable(read.Failure);
        }

        NpcPlanState? decoded;
        string reason;
        try
        {
            if (!_codec.TryDecode(read.Lines, out decoded, out reason))
            {
                return NpcPlanLoad.Unreadable(reason.Length == 0 ? "the role could not read its own rows" : reason);
            }
        }
        catch (Exception exception)
        {
            // A codec that throws during a world load must not take the load
            // out with it, and must not be treated as "there was no plan".
            return NpcPlanLoad.Unreadable(exception.GetType().Name);
        }

        if (decoded == null || !decoded.IsNamed)
        {
            return NpcPlanLoad.Unreadable("the rows named no plan");
        }

        return NpcPlanLoad.Of(decoded.AsRecovered());
    }

    /// <summary>Moves an unreadable plan file aside so a new one can be written,
    /// and says where it went. Null when it could not be moved - and a caller
    /// that gets null must not write, because the file it would overwrite is the
    /// one it just promised to keep.</summary>
    internal string? TryQuarantine() => _file.TryQuarantine();

    /// <summary>Writes the plan, whole or not at all.</summary>
    internal NpcPlanSave Save(NpcPlanState? state)
    {
        if (state == null || !state.IsNamed)
        {
            return NpcPlanSave.Refused("a plan with no identity and no job name cannot be resumed, so it is "
                + "not written");
        }

        if (state.Phase == NpcPlanPhase.Unspecified)
        {
            return NpcPlanSave.Refused("a plan whose phase nobody set says nothing about what to do next");
        }

        IReadOnlyList<string>? lines;
        try
        {
            lines = _codec.Encode(state);
        }
        catch (Exception exception)
        {
            return NpcPlanSave.Refused("the role's own writer failed (" + exception.GetType().Name + ")");
        }

        if (lines == null)
        {
            return NpcPlanSave.Refused("the role refused to write this plan");
        }

        NpcSidecarWrite written = _file.Write(lines);
        if (written.IsSaved)
        {
            return NpcPlanSave.Saved();
        }

        return NpcPlanSave.Refused(written.Failure, written.CompleteCopyPath);
    }
}

/// <summary>What one read produced. Three answers rather than a plan-or-null,
/// because "no plan" and "a plan this build could not read" lead to opposite
/// actions: the first may be written over freely and the second may never be.
/// </summary>
internal readonly struct NpcPlanLoad
{
    private NpcPlanLoad(NpcSidecarOutcome outcome, NpcPlanState? plan, string failure)
    {
        Outcome = outcome;
        Plan = plan;
        Failure = failure ?? string.Empty;
    }

    internal NpcSidecarOutcome Outcome { get; }

    /// <summary>The plan, already put through
    /// <see cref="NpcPlanState.AsRecovered"/>. Null unless
    /// <see cref="IsLoaded"/>.</summary>
    internal NpcPlanState? Plan { get; }

    /// <summary>Why it could not be read. Never a path and never a stack trace:
    /// this ends up in a sentence somebody reads.</summary>
    internal string Failure { get; }

    /// <summary>There was a plan and it was read.</summary>
    internal bool IsLoaded => Outcome == NpcSidecarOutcome.Read && Plan != null;

    /// <summary>There was no plan. A first run, or a job that has never been
    /// interrupted.</summary>
    internal bool IsAbsent => Outcome == NpcSidecarOutcome.Missing;

    /// <summary>There is a plan file and this build could not read it. Nothing
    /// may be written over it.</summary>
    internal bool IsUnreadable => Outcome == NpcSidecarOutcome.Unreadable;

    internal static NpcPlanLoad None() => new NpcPlanLoad(NpcSidecarOutcome.Missing, null, string.Empty);

    internal static NpcPlanLoad Of(NpcPlanState plan) =>
        new NpcPlanLoad(NpcSidecarOutcome.Read, plan, string.Empty);

    internal static NpcPlanLoad Unreadable(string failure) =>
        new NpcPlanLoad(NpcSidecarOutcome.Unreadable, null, failure);

    public override string ToString() =>
        Outcome + (Failure.Length == 0 ? string.Empty : " (" + Failure + ")");
}

/// <summary>What one write produced.</summary>
internal readonly struct NpcPlanSave
{
    private NpcPlanSave(bool saved, string failure, string? completeCopy)
    {
        IsSaved = saved;
        Failure = failure ?? string.Empty;
        CompleteCopyPath = completeCopy;
    }

    /// <summary>Whether the plan is on the disk. <b>The question a caller must
    /// ask before it acts</b>: a plan that could not be written down is a plan
    /// whose next action nothing would remember.</summary>
    internal bool IsSaved { get; }

    internal string Failure { get; }

    /// <summary>Where a complete copy of what should have been saved was left,
    /// when the commit was the part that failed. Null when there is none.
    /// </summary>
    internal string? CompleteCopyPath { get; }

    internal static NpcPlanSave Saved() => new NpcPlanSave(true, string.Empty, null);

    internal static NpcPlanSave Refused(string failure) => new NpcPlanSave(false, failure, null);

    internal static NpcPlanSave Refused(string failure, string? completeCopy) =>
        new NpcPlanSave(false, failure, completeCopy);

    public override string ToString() => IsSaved ? "saved" : "not saved (" + Failure + ")";
}
