using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>The shipped compatibility registry (CT-036). Intentionally
/// empty: naming a specific mod's GUID and policy here requires researching
/// its actual, current, shipped metadata first — inventing one would
/// violate this repository's "research, don't invent" rule for third-party
/// mods. CT-037 (Better Carts) and CT-038 (the broader compatibility
/// research pass) populate this list after that research; the framework
/// itself (this leaf) is fully proven against fake registries in
/// <c>CompatibilityFrameworkTests</c>.</summary>
public static class CompatibilityKnownMods
{
    public static readonly IReadOnlyList<KnownModProbe> Registry = Array.Empty<KnownModProbe>();
}
