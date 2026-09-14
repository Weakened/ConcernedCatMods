using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>One source-neutral survey sighting: some loaded-world surface
/// saw an object carrying this prefab-style name at this world position.
/// This is the contract between a loaded-world surface and the survey
/// engine, so a surface the scanner did not previously walk (issue #258:
/// Valheim world <c>Location</c> instances) can be added without touching
/// rule matching, duplicate suppression, or the Accept workflow.</summary>
/// <remarks><see cref="FootprintRadiusMeters"/> is the object's OWN
/// declared area: the scan range is measured from the object's boundary
/// rather than its centre, so a player standing anywhere inside a Burial
/// Chamber's or Troll Cave's own footprint counts as nearby.
/// <see cref="SurveySweep"/> clamps it to
/// <see cref="SurveySweep.MaxFootprintBonusMeters"/>, so a pathological
/// value can never unbound discovery.</remarks>
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
