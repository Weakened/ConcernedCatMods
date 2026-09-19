namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Whether the area a job started against still means what it meant.
/// Four answers, and collapsing any two of them costs a player something
/// specific.</summary>
internal enum WorkAreaState
{
    /// <summary>Nobody asked. Never treated as valid.</summary>
    Unspecified = 0,

    /// <summary>Same area, same geometry, and at least some of it is loaded.
    /// Work may go on.</summary>
    Valid = 1,

    /// <summary>The area still exists but its geometry moved under a job in
    /// flight. The job pauses for a person: silently adopting the new shape
    /// either abandons work already done outside it or extends an order into
    /// ground nobody agreed to.</summary>
    Changed = 2,

    /// <summary>The area is intact and none of it is loaded right now. <b>This
    /// is "ask again", not "no".</b> An NPC who treats an unloaded area as an
    /// empty one reports a job finished that has not started, so the two are
    /// never the same value.</summary>
    Unloaded = 3,

    /// <summary>The area is gone, or could not be read at all - the source was
    /// removed, the world it belonged to is not this one, or the question could
    /// not be asked. Fail closed: nothing widens to a default area, because a
    /// default area is an NPC working somewhere the player never marked.
    /// </summary>
    Invalid = 4,
}

/// <summary>What the world said about a work area at the moment of asking,
/// gathered by a role's adapter and judged here.
///
/// <b>What it guarantees.</b> That "could not tell" is carried as its own fact
/// rather than folded into "no". <see cref="SourcePresent"/> is false both when
/// the area was removed and when the question could not be asked, and the leaf
/// that judges these turns both into <see cref="WorkAreaState.Invalid"/> - fail
/// closed - while <see cref="AnyGroundLoaded"/> false with the source present is
/// <see cref="WorkAreaState.Unloaded"/>, which is a wait rather than a
/// stop.</summary>
internal readonly struct WorkAreaObservation
{
    internal WorkAreaObservation(
        bool sourcePresent, int currentRevision, bool anyGroundLoaded, NpcWorldEpoch world)
    {
        SourcePresent = sourcePresent;
        CurrentRevision = currentRevision;
        AnyGroundLoaded = anyGroundLoaded;
        World = world;
    }

    /// <summary>Whether the thing the area was derived from still exists.
    /// <b>False also means "could not be asked"</b>, deliberately: an adapter
    /// that cannot see the designation book has not proved the area is there.
    /// </summary>
    internal bool SourcePresent { get; }

    /// <summary>The area's revision now, to compare against the one the job
    /// started with.</summary>
    internal int CurrentRevision { get; }

    /// <summary>Whether any of the area is loaded. Sampled, not assumed: a few
    /// points across the area is what the shipped check does, and an area whose
    /// far edge is unloaded is still workable near the player.</summary>
    internal bool AnyGroundLoaded { get; }

    /// <summary>The world this observation was taken in.
    /// <see cref="WorkAreaState.Invalid"/> is documented to cover "the world it
    /// belonged to is not this one", and without this a leaf judging an
    /// observation has no way to reach that answer - it would have to assume
    /// the area it is being told about belongs to the world it is asking
    /// from.</summary>
    internal NpcWorldEpoch World { get; }
}
