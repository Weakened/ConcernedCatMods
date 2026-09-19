namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>What happened when an area was asked to come back.
///
/// <b>Four answers, and three of them are refusals with different fixes.</b>
/// Collapsing them costs a player the one sentence that would tell them what to
/// do: install the map mod again, re-mark the area, or nothing at all because
/// the area was deliberately removed.</summary>
public enum WorkAreaResolution
{
    /// <summary>Nobody asked. Never an area.</summary>
    Unspecified = 0,

    /// <summary>Rebuilt. The area is there.</summary>
    Resolved = 1,

    /// <summary>Nothing is registered under that provider id. The ordinary case
    /// for an area drawn by a mod that is no longer installed.
    ///
    /// <b>This is the value the whole registry exists to be able to return.</b>
    /// The tempting alternative - fall back to a circle around the same
    /// coordinates - looks like it worked, and is an NPC working ground the
    /// player never marked, for as long as nobody notices.</summary>
    ProviderMissing = 2,

    /// <summary>The provider is there and the shape is not one it can read: too
    /// few numbers, a radius of zero, a polygon with two corners. Refused, never
    /// repaired.</summary>
    ShapeUnreadable = 3,

    /// <summary>The provider was asked and said no - or failed while being
    /// asked. A provider that throws is a refusal, not an exception out of a
    /// scan loop: the driver above this does not catch one.</summary>
    Refused = 4,
}

/// <summary>An area rebuilt, or the reason it was not.
///
/// <b>What it guarantees: that a refusal carries no area.</b>
/// <see cref="Area"/> is non-null only for <see cref="WorkAreaResolution.Resolved"/>,
/// and the constructor is the only place that pairing is decided, so no caller
/// can be handed a plausible-looking area beside a refusal and use the wrong
/// half.</summary>
public readonly struct NpcWorkAreaResult
{
    private NpcWorkAreaResult(WorkAreaResolution resolution, INpcWorkArea? area, string reason)
    {
        Resolution = resolution;
        Area = area;
        Reason = reason;
    }

    internal WorkAreaResolution Resolution { get; }

    /// <summary>The rebuilt area, or null. Null for every refusal.</summary>
    internal INpcWorkArea? Area { get; }

    /// <summary>What to tell a person, in the register of evidence rather than
    /// of dialogue: "no provider is registered for cartographer/freehand". A
    /// role renders its own sentence; this is what it renders from.</summary>
    internal string Reason { get; }

    /// <summary>The one question a caller asks. True only with an area.</summary>
    internal bool IsResolved => Resolution == WorkAreaResolution.Resolved && Area != null;

    internal static NpcWorkAreaResult Resolved(INpcWorkArea area) =>
        area == null
            ? Refused("a provider answered with no area")
            : new NpcWorkAreaResult(WorkAreaResolution.Resolved, area, string.Empty);

    internal static NpcWorkAreaResult ProviderMissing(string reason) =>
        new NpcWorkAreaResult(WorkAreaResolution.ProviderMissing, null, reason ?? string.Empty);

    internal static NpcWorkAreaResult ShapeUnreadable(string reason) =>
        new NpcWorkAreaResult(WorkAreaResolution.ShapeUnreadable, null, reason ?? string.Empty);

    internal static NpcWorkAreaResult Refused(string reason) =>
        new NpcWorkAreaResult(WorkAreaResolution.Refused, null, reason ?? string.Empty);
}

/// <summary>Something that knows how to turn a saved shape back into an area.
///
/// <b>Why this exists at all.</b> One shape ships in this package: a bounded
/// circle, because that is what every work area in this repository is today. A
/// richer one - freehand, a polygon, a region drawn on a map - belongs to the
/// mod that can draw it, and this package must not reference that mod. So the
/// registry knows providers by an id and nothing else, and the built-in circle
/// is registered through the same door as any other: there is no privileged
/// path, and therefore no path that could quietly become a fallback.
///
/// <b>What an implementation must never do.</b> Widen. A provider handed a
/// shape it cannot read refuses; it does not clamp, repair, pad with defaults,
/// or substitute a circle of some plausible radius. The failure this contract
/// exists to prevent is not a crash - it is an area that comes back looking
/// fine and covering ground nobody marked.
///
/// <b>What an implementation may assume.</b> That the descriptor is well
/// formed - named, with a shape, with no NaN - because the registry checks that
/// once before asking anybody. Everything beyond that is the provider's to
/// judge.</summary>
public interface INpcWorkAreaProvider
{
    /// <summary>The id this provider answers to, matched ordinally against
    /// <see cref="NpcWorkAreaId.ProviderId"/>. Supplied by whoever constructs
    /// the provider, never by this library: it is written into whatever a role
    /// persists, and a durable name belongs to the role that will still be
    /// reading it in two years.</summary>
    string ProviderId { get; }

    /// <summary>Rebuilds one area from its saved shape. Never throws for a
    /// shape reason - an unreadable shape is
    /// <see cref="WorkAreaResolution.ShapeUnreadable"/> - and never returns an
    /// area for a descriptor it did not fully understand.</summary>
    NpcWorkAreaResult Rebuild(NpcWorkAreaDescriptor descriptor);
}
