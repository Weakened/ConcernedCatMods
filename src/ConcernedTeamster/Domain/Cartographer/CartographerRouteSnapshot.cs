using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>An immutable copy of one Cartographer route (CT-021): stable
/// identity, display name, archived flag, and the polyline geometry. A
/// snapshot never aliases Cartographer's live objects — every value is
/// copied at read time, so nothing Teamster holds can observe or affect a
/// later Cartographer edit (read-only integration, no atlas mutation).</summary>
public sealed class CartographerRouteSnapshot
{
    public CartographerRouteSnapshot(
        Guid id, string name, bool archived, IReadOnlyList<CartographerRoutePoint> points)
    {
        Id = id;
        Name = name;
        Archived = archived;
        Points = points;
    }

    /// <summary>The route's durable Cartographer identity (AtlasId.Value);
    /// stable across edits, sessions, and sync.</summary>
    public Guid Id { get; }

    /// <summary>Display name; empty when the route is unnamed.</summary>
    public string Name { get; }

    /// <summary>True when the owner archived the route. Passed through so
    /// the selection UI (CT-022) can exclude archived routes.</summary>
    public bool Archived { get; }

    /// <summary>Polyline vertices in recorded order; may be empty for a
    /// just-created route.</summary>
    public IReadOnlyList<CartographerRoutePoint> Points { get; }
}
