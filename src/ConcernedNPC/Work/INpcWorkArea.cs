namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Where an NPC may work: the ground an ordered job is confined to.
///
/// <b>What it guarantees.</b> Two things, and the second is the one that
/// matters.
///
/// <i>Containment is authoritative.</i> <see cref="Contains"/> is the only
/// answer to "may he work here". Nothing in this library derives containment
/// from the bounding circle, widens it when the area is unreadable, or falls
/// back to "anywhere" when nothing is marked. An area that answers false
/// everywhere is an NPC who does nothing, which is the correct failure.
///
/// <i>The bound is a superset, never a substitute.</i>
/// <see cref="BoundingCentre"/> and <see cref="BoundingRadiusMetres"/> exist so
/// a scan has somewhere finite to look - laying a grid over a circle is how the
/// shipped survey already works - and every point <see cref="Contains"/> accepts
/// is inside them. A candidate from the bound is still offered to
/// <see cref="Contains"/> before anything acts on it, because the bound is
/// allowed to be generous and the area is not.
///
/// <b>Why an interface and not a circle struct.</b> Every work area shipped
/// today is a horizontal circle with a 4 m to 48 m clamp, and if that were the
/// whole story this would be a struct. It is not: the collection order's scope
/// enum already reserves a value for an area supplied by a map product, with no
/// provider anywhere - a seam left open, waiting for exactly this. A role hands
/// its own shape in, and no leaf in this library is allowed to assume a radius.
///
/// <b>What it deliberately does not answer.</b> Whether the ground at a point is
/// loaded, standable or safe - that is <see cref="INpcAreaProbe"/>, asked per
/// point, because an area is a decision about permission and the ground is a
/// fact about the world, and confusing the two is how an NPC ends up refusing to
/// work in a marked area because a zone had not streamed in yet.
///
/// <b>Revision, not equality.</b> A job in flight compares
/// <see cref="Revision"/> against the value it started with. A changed area
/// pauses that job for a person to look at; it never silently widens or narrows
/// work already under way.</summary>
public interface INpcWorkArea
{
    /// <summary>May an NPC work at this point? Height is part of the question
    /// only if this area says it is; the shipped areas ignore it.</summary>
    bool Contains(NpcPoint point);

    /// <summary>Centre of a circle that contains every point
    /// <see cref="Contains"/> accepts. A place to start looking, never a
    /// permission.</summary>
    NpcPoint BoundingCentre { get; }

    /// <summary>Radius of that circle, in metres. Strictly positive for any
    /// area that contains anything.</summary>
    float BoundingRadiusMetres { get; }

    /// <summary>Changes whenever the area's own geometry changes, so a job that
    /// started against an older one can tell. Derived from the geometry rather
    /// than counted, so the same area gives the same revision in a later
    /// session.</summary>
    int Revision { get; }

    /// <summary>What to call this area in a sentence shown to a player - "the
    /// harvest area", "your camp". Never an enum name, never a slug.</summary>
    string Describe { get; }
}
