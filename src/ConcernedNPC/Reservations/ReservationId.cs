using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Reservations;

/// <summary>The name of one reservation: a job, and a step within it.
///
/// <b>What it guarantees: the same step of the same job produces the same name
/// after a restart.</b> That is the whole design, and it is why the name is
/// <i>derived</i> rather than allocated. A counter, a <c>Guid</c> or anything
/// else minted at the moment of reserving is a different name on the second run,
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
/// <b>It names a reservation. It does not decide who conflicts with whom.</b>
/// That is <see cref="JobId"/>'s job, and the distinction is load-bearing. If a
/// whole id were the holder, a re-plan that moved the same subject from step 3
/// to step 2 would ask for something the job already holds under another name
/// and be refused <i>by itself</i> - and re-planning from where things actually
/// are is exactly what an interruption is supposed to do. So the book records
/// the full name, for the journal row and the evidence line, and decides
/// conflicts on the job half alone. The shipped book this seam is modelled on
/// holds a source for an <i>order</i> for the same reason.
///
/// <b>What it is not: an object id.</b> It names a reservation, not the thing
/// reserved. What is reserved is the book's subject, keyed however that subject
/// is keyed - and a world object's own id is never durable, because a world load
/// renumbers every one of them.</summary>
internal readonly struct ReservationId : IEquatable<ReservationId>
{
    private const char Separator = '#';

    private readonly string? _jobId;

    private ReservationId(string jobId, int step)
    {
        _jobId = jobId;
        Step = step;
    }

    /// <summary>The job this reservation belongs to, and <b>the unit of
    /// conflict</b>: two reservations with the same job id never conflict with
    /// one another, whatever their steps. Empty for <c>default</c>.</summary>
    internal string JobId => _jobId ?? string.Empty;

    /// <summary>Which step of that job's plan took it out, from zero. Carried so
    /// an evidence line can say which step, never compared when deciding
    /// whether two reservations conflict.</summary>
    internal int Step { get; }

    /// <summary><c>job#step</c>. Empty for <c>default</c>, which is never a
    /// valid name and never matches one.</summary>
    internal string Value =>
        _jobId == null ? string.Empty : _jobId + Separator + Step.ToString(CultureInfo.InvariantCulture);

    internal bool IsEmpty => _jobId == null;

    /// <summary>The name for one step of one job. The same arguments always give
    /// the same name, in this session and in every later one.</summary>
    /// <param name="jobId">The job's own stable name. Must contain no
    /// <c>#</c>, so the two halves can always be told apart again.</param>
    /// <param name="step">The step's index in its plan, from zero.</param>
    internal static ReservationId For(string? jobId, int step)
    {
        if (string.IsNullOrEmpty(jobId) || jobId!.IndexOf(Separator) >= 0 || step < 0)
        {
            return default;
        }

        return new ReservationId(jobId, step);
    }

    /// <summary>Reads a name back. Returns false rather than throwing for
    /// anything that is not one, <b>including a step written with a leading
    /// zero</b>: <c>job#007</c> would otherwise parse to a name whose
    /// <see cref="Value"/> is <c>job#7</c>, so text and name would disagree for
    /// anything keyed by text.</summary>
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

        string stepText = text.Substring(separator + 1);
        if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out int step))
        {
            return false;
        }

        id = For(text.Substring(0, separator), step);
        if (id.IsEmpty)
        {
            return false;
        }

        // Round-trip exactly or not at all.
        if (!string.Equals(id.Value, text, StringComparison.Ordinal))
        {
            id = default;
            return false;
        }

        return true;
    }

    /// <summary>Whether this reservation belongs to the same job as another.
    /// <b>The comparison a book uses to decide a conflict</b>, as opposed to
    /// <see cref="Equals(ReservationId)"/>, which is name equality.</summary>
    internal bool SameJobAs(ReservationId other) =>
        _jobId != null && other._jobId != null &&
        string.Equals(_jobId, other._jobId, StringComparison.Ordinal);

    public bool Equals(ReservationId other) =>
        string.Equals(JobId, other.JobId, StringComparison.Ordinal) && Step == other.Step;

    public override bool Equals(object? obj) => obj is ReservationId other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(JobId) * 397) ^ Step;
        }
    }

    public override string ToString() => IsEmpty ? "<empty>" : Value;
}
