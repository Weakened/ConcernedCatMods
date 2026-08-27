using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>A managed map pin: durable identity, monotonic revision, full
/// metadata, and a durable-deletion tombstone flag. Edits mutate the entity
/// in place through <see cref="PinStore"/>, which owns revision bumps —
/// entities are never deleted and recreated to change a field.</summary>
internal sealed class AtlasPin
{
    public AtlasPin(AtlasId id)
    {
        Id = id;
    }

    public AtlasId Id { get; }
    public long Revision { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Namespaced icon identity from the icon registry, e.g.
    /// "vanilla:house". Unknown IDs render as the fallback icon but are
    /// preserved verbatim, so identities survive registry evolution.</summary>
    public string IconId { get; set; } = IconRegistry.DefaultIconId;

    public string Category { get; set; } = "";
    public int? ColorArgb { get; set; }
    public float SizeScale { get; set; } = 1f;
    public string Notes { get; set; } = "";
    public List<string> Tags { get; } = new();
    public AtlasPinStatus Status { get; set; }
    public bool Checked { get; set; }
    public AtlasScope Scope { get; set; } = AtlasScope.Private;
    public AtlasPinSource Source { get; set; } = AtlasPinSource.Managed;
    public bool Archived { get; set; }
    public bool Deleted { get; set; }
    public DateTime? DeletedUtc { get; set; }

    /// <summary>Audit metadata: the author identity that created the entity
    /// and the one that last modified it. Empty for pre-audit data. Used
    /// for sync labels and the non-owner-delete policy; never for local
    /// feature gating.</summary>
    public string OwnerAuthor { get; set; } = "";

    public string LastAuthor { get; set; } = "";

    public RoadPoint Position { get; set; }

    /// <summary>Deep copy used by undo snapshots and sync payloads.</summary>
    public AtlasPin Clone()
    {
        var clone = new AtlasPin(Id)
        {
            Revision = Revision,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Name = Name,
            IconId = IconId,
            Category = Category,
            ColorArgb = ColorArgb,
            SizeScale = SizeScale,
            Notes = Notes,
            Status = Status,
            Checked = Checked,
            Scope = Scope,
            Source = Source,
            Archived = Archived,
            Deleted = Deleted,
            DeletedUtc = DeletedUtc,
            OwnerAuthor = OwnerAuthor,
            LastAuthor = LastAuthor,
            Position = Position,
        };
        clone.Tags.AddRange(Tags);
        return clone;
    }

    public void CopyFrom(AtlasPin other)
    {
        Revision = other.Revision;
        CreatedUtc = other.CreatedUtc;
        ModifiedUtc = other.ModifiedUtc;
        Name = other.Name;
        IconId = other.IconId;
        Category = other.Category;
        ColorArgb = other.ColorArgb;
        SizeScale = other.SizeScale;
        Notes = other.Notes;
        Status = other.Status;
        Checked = other.Checked;
        Scope = other.Scope;
        Source = other.Source;
        Archived = other.Archived;
        Deleted = other.Deleted;
        DeletedUtc = other.DeletedUtc;
        OwnerAuthor = other.OwnerAuthor;
        LastAuthor = other.LastAuthor;
        Position = other.Position;
        Tags.Clear();
        Tags.AddRange(other.Tags);
    }
}
