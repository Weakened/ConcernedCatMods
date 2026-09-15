namespace TheConcernedCat.Companions.Placement;

/// <summary>A seat a probe found, and everything the presentation layer needs
/// to use it.
///
/// Using a seat means putting the companion at the seat's own attachment point
/// in the seat's own sitting pose, and nothing else. No seat is claimed, no
/// attachment message is sent, and the game's own occupancy test is never told
/// anything — which is what keeps a companion from stealing a chair out from
/// under the player who built it.
///
/// The pose is carried as a position, a heading and the name of the sitting
/// animation the seat itself asks for, because those are the three things the
/// game uses and all three are per-seat data rather than constants.</summary>
internal readonly struct SeatOffer
{
    private SeatOffer(
        SeatAvailability availability, WorldPoint position, float yawDegrees, string? attachAnimation)
    {
        Availability = availability;
        Position = position;
        YawDegrees = yawDegrees;
        AttachAnimation = attachAnimation;
    }

    public SeatAvailability Availability { get; }

    /// <summary>The seat's attachment point. Only meaningful when <see
    /// cref="IsUsable"/>.</summary>
    public WorldPoint Position { get; }

    /// <summary>The heading the seat faces, in degrees.</summary>
    public float YawDegrees { get; }

    /// <summary>The sitting animation this seat asks for, e.g.
    /// <c>attach_chair</c>. Per-seat data, never assumed: a seat that does not
    /// name one leaves this null and the caller uses its own fallback.</summary>
    public string? AttachAnimation { get; }

    /// <summary>True only for a seat that is present, free, and gave us a pose
    /// to use. Anything less is the ground.</summary>
    public bool IsUsable => Availability == SeatAvailability.Free;

    public static SeatOffer None => new SeatOffer(SeatAvailability.None, default, 0f, null);

    /// <summary>A seat with a real occupant. The companion yields.</summary>
    public static SeatOffer Occupied =>
        new SeatOffer(SeatAvailability.Occupied, default, 0f, null);

    /// <summary>Seating is there, but this build could not establish a pose for
    /// it. Treated exactly like no seat - a documented gap beats a figure
    /// floating over a bench.</summary>
    public static SeatOffer Unverified =>
        new SeatOffer(SeatAvailability.Unverified, default, 0f, null);

    public static SeatOffer Free(WorldPoint position, float yawDegrees, string? attachAnimation)
    {
        return new SeatOffer(SeatAvailability.Free, position, yawDegrees, attachAnimation);
    }

    public override string ToString()
    {
        return IsUsable
            ? $"{Availability} at {Position} facing {YawDegrees:0}° ({AttachAnimation ?? "default pose"})"
            : Availability.ToString();
    }
}
