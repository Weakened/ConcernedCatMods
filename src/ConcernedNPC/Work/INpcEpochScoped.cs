namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Something named within one world load, and worth nothing outside it.
///
/// <b>What it guarantees: that a thing carrying an in-session name also carries
/// the world that name was minted in.</b> Anything that keys a world object -
/// a container, a resource, a place - is naming something whose id is reassigned
/// from a counter that restarts on every load. The name alone is therefore not
/// only useless after a reload, it is actively dangerous, because it now
/// overwhelmingly likely refers to a different object.
///
/// <b>Why it is a constraint rather than a convention.</b> A book that holds
/// reservations has to be able to refuse one taken in a previous world. If its
/// subject is an unconstrained type it cannot: there is nothing to ask. The one
/// shipped reservation book that gets this right reads the epoch straight off
/// its subject, and lifting that to a generic with an opaque subject would
/// quietly drop a safety property that ships today. So the subject is required
/// to answer, and the alternative - passing the epoch alongside the subject at
/// the call - is worse, because it lets a caller state an epoch the subject does
/// not have.</summary>
internal interface INpcEpochScoped
{
    /// <summary>The world load this thing's name was minted in. Comparing it
    /// with <see cref="NpcWorldEpoch.Matches"/> is how "this name points at
    /// nothing any more" becomes a check rather than a hope.</summary>
    NpcWorldEpoch Epoch { get; }
}
