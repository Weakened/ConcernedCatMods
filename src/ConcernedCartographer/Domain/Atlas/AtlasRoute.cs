using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>How a route was authored, which controls how it is edited.</summary>
internal enum RouteKind
{
    Freehand = 1,
    Waypoint = 2,
}

/// <summary>Rendering style of a route line.</summary>
internal enum RouteStyle
{
    Solid = 1,
    Dashed = 2,
    Dotted = 3,
}

/// <summary>Planning status of a route.</summary>
internal enum RouteStatus
{
    Planned = 1,
    Active = 2,
    Done = 3,
}

/// <summary>A managed route: durable identity, monotonic revision, polyline
/// points, style/status metadata, lock/archive flags, and a durable
/// deletion tombstone — the same lifecycle contract as pins.</summary>
internal sealed class AtlasRoute
{
    public AtlasRoute(AtlasId id)
    {
        Id = id;
    }

    public AtlasId Id { get; }
    public long Revision { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public string Name { get; set; } = "";
    public RouteKind Kind { get; set; } = RouteKind.Freehand;
    public RouteStyle Style { get; set; } = RouteStyle.Solid;
    public RouteStatus Status { get; set; } = RouteStatus.Planned;
    public int? ColorArgb { get; set; }
    public string Notes { get; set; } = "";
    public AtlasScope Scope { get; set; } = AtlasScope.Private;

    /// <summary>A locked route rejects every geometry edit until unlocked.</summary>
    public bool Locked { get; set; }

    public bool Archived { get; set; }
    public bool Deleted { get; set; }
    public DateTime? DeletedUtc { get; set; }

    /// <summary>Audit metadata mirroring pins: creator and last editor
    /// identities; empty for pre-audit data.</summary>
    public string OwnerAuthor { get; set; } = "";

    public string LastAuthor { get; set; } = "";

    public List<RoadPoint> Points { get; } = new();

    public AtlasRoute Clone()
    {
        var clone = new AtlasRoute(Id)
        {
            Revision = Revision,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Name = Name,
            Kind = Kind,
            Style = Style,
            Status = Status,
            ColorArgb = ColorArgb,
            Notes = Notes,
            Scope = Scope,
            Locked = Locked,
            Archived = Archived,
            Deleted = Deleted,
            DeletedUtc = DeletedUtc,
            OwnerAuthor = OwnerAuthor,
            LastAuthor = LastAuthor,
        };
        clone.Points.AddRange(Points);
        return clone;
    }

    public void CopyFrom(AtlasRoute other)
    {
        Revision = other.Revision;
        CreatedUtc = other.CreatedUtc;
        ModifiedUtc = other.ModifiedUtc;
        Name = other.Name;
        Kind = other.Kind;
        Style = other.Style;
        Status = other.Status;
        ColorArgb = other.ColorArgb;
        Notes = other.Notes;
        Scope = other.Scope;
        Locked = other.Locked;
        Archived = other.Archived;
        Deleted = other.Deleted;
        DeletedUtc = other.DeletedUtc;
        OwnerAuthor = other.OwnerAuthor;
        LastAuthor = other.LastAuthor;
        Points.Clear();
        Points.AddRange(other.Points);
    }
}
