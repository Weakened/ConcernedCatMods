using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>One source-neutral survey sighting: some loaded-world surface
/// saw an object carrying this prefab-style name at this world position.
/// This is the only contract between a loaded-world surface and the
/// survey engine, so a surface the scanner did not previously walk (issue
/// #258: Valheim world <c>Location</c> instances) can be added without
/// touching rule matching, duplicate suppression, or the Accept
/// workflow.</summary>
/// <remarks><see cref="FootprintRadiusMeters"/> is the object's OWN
/// declared area, not a scan-range increase: a Burial Chamber or Troll
/// Cave reports its exterior radius so a player standing inside the
/// dungeon's own footprint counts as nearby. <see cref="SurveySweep"/>
/// clamps it, so a pathological value can never unbound discovery.</remarks>
internal readonly struct SurveySighting
{
    public SurveySighting(string name, RoadPoint position, float footprintRadiusMeters = 0f)
    {
        Name = name;
        Position = position;
        FootprintRadiusMeters = footprintRadiusMeters;
    }

    public string Name { get; }
    public RoadPoint Position { get; }
    public float FootprintRadiusMeters { get; }
}
