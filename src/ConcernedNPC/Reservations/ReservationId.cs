using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Reservations;

/// <summary>The name of one reservation: a job and a step within it.
///
/// <b>What it guarantees: the same step of the same job produces the same id
/// after a restart.</b> That is the whole design, and it is why the id is
/// <i>derived</i> rather than allocated. A counter, a <c>Guid</c> or anything
/// else minted at the moment of reserving is a different id on the second run,
/// so a job resumed after a reload cannot tell whether the reservation it is
/// about to take out is the one it already has - and takes it out twice, which
/// is either a double withdrawal or a permanent hold on material nobody will
/// ever release.
///
/// There is exactly one precedent in this repository and it works: the
/// settlement's request id is computed from the order and the step number, with
/// a comment saying the same thing this one does. This is that idea, in the one
/// place all four NPCs can reach it.
///
/// <b>What follows for step numbering.</b> A plan's step indices are part of its
/// reservations' names. Renumbering steps after a re-plan renames every
/// reservation the old plan held, so the interruption leaf either keeps indices
/// stable across a re-plan or releases first and re-reserves under the new
/// names - deliberately, with a test. It is not a detail that can be left to
/// whoever writes the loop.
///
/// <b>What it is not: an object id.</b> It names a reservation, not the thing
/// reserved. What is reserved is the book's subject, keyed however that subject
/// is keyed - and a world object's own id is never durable, because a world load
/// renumbers every one of them.</summary>
internal readonly struct ReservationId : IEquatable<ReservationId>
{
    private const char Separator = '#';

    private ReservationId(string value)
    {
        Value = value;
    }

    /// <summary><c>job#step</c>. Empty for <c>default</c>, which is never a
    /// valid id and never matches one.</summary>
    internal string Value { get; }

    internal bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>The id for one step of one job. The same arguments always give
    /// the same id, in this session and in every later one.</summary>
    /// <param name="jobId">The job's own stable name. Must contain no
    /// <c>#</c>, so the two halves can always be told apart again.</param>
    /// <param name="step">The step's index in its plan, from zero.</param>
    internal static ReservationId For(string? jobId, int step)
    {
        if (string.IsNullOrEmpty(jobId) || jobId!.IndexOf(Separator) >= 0 || step < 0)
        {
            return default;
        }

        return new ReservationId(jobId + Separator + step.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads an id back. Returns false rather than throwing for
    /// anything that is not one.</summary>
    internal static bool TryParse(string? text, out ReservationId id)
    {
        id = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int separator = text!.IndexOf(Separator);
        if (separator <= 0 || separator != text.LastIndexOf(Separator) || separator == text.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(
                text.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int step))
        {
            return false;
        }

        id = For(text.Substring(0, separator), step);
        return !id.IsEmpty;
    }

    public bool Equals(ReservationId other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ReservationId other && Equals(other);

    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => IsEmpty ? "<empty>" : Value;
}
