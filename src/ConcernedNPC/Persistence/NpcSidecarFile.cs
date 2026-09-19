using System;
using System.Collections.Generic;
using System.IO;

namespace TheConcernedCat.ConcernedNPC.Persistence;

/// <summary>One role's sidecar file, read and written safely - and nothing
/// more.
///
/// <b>This type does not know what is in the file.</b> It reads lines and
/// writes lines. Row tags, field order, schema numbers and scope checks belong
/// to the role's own codec and stay exactly where they are, which is what makes
/// the whole move provably free of a migration: the bytes go in and come back
/// unchanged, and nothing here could reinterpret them if it wanted to.
///
/// <b>It does not know where the file is, either.</b> The absolute path arrives
/// in the constructor, from the role. This library composes no data root, joins
/// no path and invents no file name - a shipped product decides whether a
/// player is new or returning by looking at the files in its own root, so a
/// shared runtime that could drop a stray file there is a permanent, silent
/// wrong grant waiting for a caller. A path that is not absolute is refused
/// rather than resolved against whatever the working directory happens to be.
///
/// <b>The three rules that keep a bad file from becoming a bad
/// experience.</b> A write goes to a temporary file and is swapped in, so an
/// interrupted save never truncates the live one. An unreadable file is moved
/// aside rather than deleted, and if it cannot be moved aside it is left
/// exactly where it is and the caller is told - never overwritten. And a failed
/// commit keeps the complete temporary copy, named in the failure, because at
/// that moment it is the only intact copy there is.</summary>
internal sealed class NpcSidecarFile
{
    private readonly string _path;
    private readonly string _directory;

    private NpcSidecarFile(string path, string directory)
    {
        _path = path;
        _directory = directory;
    }

    /// <summary>The live file.</summary>
    internal string Path => _path;

    /// <summary>Where a half-written save lands. Beside the live file, because
    /// the swap has to happen within one directory to be atomic.</summary>
    internal string TemporaryPath => _path + ".tmp";

    /// <summary>Opens a sidecar at an absolute path the role composed, or says
    /// why it cannot.</summary>
    internal static bool TryAt(string? absolutePath, out NpcSidecarFile? file, out string reason)
    {
        file = null;

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            reason = "a role must supply the path of its own sidecar; this library composes none";
            return false;
        }

        string path = absolutePath!;
        string directory;
        try
        {
            if (!System.IO.Path.IsPathRooted(path))
            {
                reason = "the sidecar path must be absolute, so it cannot land wherever the process "
                    + "happens to be running from";
                return false;
            }

            directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch (ArgumentException exception)
        {
            reason = "that sidecar path is not usable (" + exception.GetType().Name + ")";
            return false;
        }

        if (directory.Length == 0)
        {
            reason = "the sidecar path names no directory";
            return false;
        }

        file = new NpcSidecarFile(path, directory);
        reason = string.Empty;
        return true;
    }

    /// <summary>Whether this exact file exists.
    ///
    /// Deliberately about one file and not about the directory. Counting files
    /// in a role's data root answers "has this player ever played anything",
    /// which is not the same question and has already shipped twice as a
    /// permanent wrong grant.</summary>
    internal bool Exists
    {
        get
        {
            try
            {
                return File.Exists(_path);
            }
            catch
            {
                // Unreadable is not the same as absent, and this method is not
                // the place to decide what to do about the difference.
                return false;
            }
        }
    }

    /// <summary>Reads the file's lines.</summary>
    internal NpcSidecarRead Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return NpcSidecarRead.Missing();
            }

            return NpcSidecarRead.Of(File.ReadAllLines(_path));
        }
        catch (Exception exception)
        {
            // An input failure is not evidence about anything the file holds,
            // so the answer is "could not read", never "empty". A caller that
            // treated it as empty would write an empty file over a full one.
            return NpcSidecarRead.Unreadable(exception.GetType().Name);
        }
    }

    /// <summary>Moves an unreadable file aside so a fresh one can be written,
    /// and says where it went. Null when it could not be moved - and a caller
    /// that gets null must not write, because the file it would overwrite is
    /// the one it just promised to keep.</summary>
    internal string? TryQuarantine() => NpcAtomicText.TryQuarantine(_path, ".corrupt");

    /// <summary>Writes the file, whole or not at all.</summary>
    internal NpcSidecarWrite Write(IEnumerable<string>? lines)
    {
        if (lines == null)
        {
            return NpcSidecarWrite.Refused("there were no lines to write", null);
        }

        string temporary = TemporaryPath;
        Exception? failure = NpcAtomicText.TryWriteTemporary(_directory, temporary, lines);
        if (failure != null)
        {
            return NpcSidecarWrite.Refused(failure.GetType().Name, null);
        }

        try
        {
            NpcAtomicText.Commit(temporary, _path);
            return NpcSidecarWrite.Saved();
        }
        catch (Exception exception)
        {
            // The temporary file is KEPT on purpose. The live file is whatever
            // it was, and this complete copy is the only place the new content
            // exists; deleting it here - which three of the five copies this
            // replaces did, in a finally - throws away the only intact copy at
            // the one moment it matters.
            return NpcSidecarWrite.Refused(exception.GetType().Name, temporary);
        }
    }
}

/// <summary>What one read produced.</summary>
internal readonly struct NpcSidecarRead
{
    private readonly IReadOnlyList<string>? _lines;

    private NpcSidecarRead(NpcSidecarOutcome outcome, IReadOnlyList<string>? lines, string failure)
    {
        Outcome = outcome;
        _lines = lines;
        Failure = failure ?? string.Empty;
    }

    internal NpcSidecarOutcome Outcome { get; }

    /// <summary>The file's lines, exactly as they were, or empty. Never null,
    /// so a caller cannot accidentally treat "could not read" as "no rows" by
    /// forgetting a null check - the outcome is the thing to ask.</summary>
    internal IReadOnlyList<string> Lines => _lines ?? new string[0];

    /// <summary>The exception type name, when the read failed. No path and no
    /// stack trace: this ends up in a sentence shown to a player.</summary>
    internal string Failure { get; }

    internal static NpcSidecarRead Missing() =>
        new NpcSidecarRead(NpcSidecarOutcome.Missing, null, string.Empty);

    internal static NpcSidecarRead Of(IReadOnlyList<string> lines) =>
        new NpcSidecarRead(NpcSidecarOutcome.Read, lines, string.Empty);

    internal static NpcSidecarRead Unreadable(string failure) =>
        new NpcSidecarRead(NpcSidecarOutcome.Unreadable, null, failure);

    public override string ToString() =>
        Outcome + (Failure.Length == 0 ? string.Empty : " (" + Failure + ")");
}

/// <summary>What one write produced.</summary>
internal readonly struct NpcSidecarWrite
{
    private NpcSidecarWrite(bool saved, string failure, string? completeCopy)
    {
        IsSaved = saved;
        Failure = failure ?? string.Empty;
        CompleteCopyPath = completeCopy;
    }

    internal bool IsSaved { get; }

    /// <summary>The exception type name, when it failed.</summary>
    internal string Failure { get; }

    /// <summary>Where a complete copy of what should have been saved was left,
    /// when the commit was the part that failed. Null when there is none - a
    /// write that never got as far as a complete temporary file has nothing to
    /// offer.</summary>
    internal string? CompleteCopyPath { get; }

    internal static NpcSidecarWrite Saved() => new NpcSidecarWrite(true, string.Empty, null);

    internal static NpcSidecarWrite Refused(string failure, string? completeCopy) =>
        new NpcSidecarWrite(false, failure, completeCopy);

    public override string ToString() => IsSaved ? "saved" : "not saved (" + Failure + ")";
}
