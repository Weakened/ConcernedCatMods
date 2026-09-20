using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>How one role writes a plan down, and the only thing this library
/// knows about that writing: that lines go out and lines come back.
///
/// <b>Why the format is on this side of the seam.</b> The zero-migration promise
/// is that a pre-refactor data directory, dropped in unchanged, keeps working
/// with no migration code having run - and that holds only while every durable
/// row tag, field order, schema number and file name stays with the role that
/// already writes it. A shared runtime that named any of them would have to
/// change one to change anything, and the cost would be paid by everyone who has
/// ever played. So the library owns the <i>mechanism</i> - when to write, in what
/// order relative to the world moving, what a torn write may leave behind, and
/// what a reconstructed plan is allowed to believe - and a role owns the bytes.
///
/// <b>What that costs the library, stated plainly.</b> It cannot check a plan
/// file. It cannot upgrade one. It cannot tell a role's own rows from somebody
/// else's, and it never looks. A codec that returns nonsense produces a plan that
/// fails its own revalidation, which is the correct outcome for a role that broke
/// its own format and is not something this library can do better.
///
/// <b>Never throws is not assumed.</b> Both methods are called inside a guard,
/// because a role's codec throwing during recovery would be a failure inside
/// failure handling - and because the caller at that moment is a world load.
/// </summary>
internal interface INpcPlanCodec
{
    /// <summary>Turns a plan into the lines this role's own format writes.
    /// Returns null to refuse, which stops the write rather than producing an
    /// empty file where a plan used to be.</summary>
    IReadOnlyList<string>? Encode(NpcPlanState state);

    /// <summary>Reads a plan back from this role's own lines.
    ///
    /// Returns false for anything it cannot read, with a reason a person can
    /// act on. <b>False is never "there was no plan"</b> - that answer comes from
    /// the absence of a file, one level up - so a role that cannot read its own
    /// rows leaves them exactly where they are rather than having them written
    /// over.</summary>
    bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason);
}
