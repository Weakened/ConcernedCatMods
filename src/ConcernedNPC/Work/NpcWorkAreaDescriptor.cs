using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Everything needed to rebuild one work area, and nothing else.
///
/// <b>What it guarantees: that an area survives a reload without this library
/// owning a file format.</b> A role persists these three things however it
/// already persists things - its own rows, its own tags, its own schema - and
/// hands them back after a load. There is no codec here, no header, no row tag
/// and no file name, because the moment this package owned one it would own a
/// durable name, and a shared runtime that can drop a name into a role's data
/// directory is the first-run-detection bug this repository has already shipped
/// twice.
///
/// <b>Why the shape is numbers and not text.</b> Text needs a separator, an
/// escape rule, a culture and a version - four durable decisions - and the first
/// of them made here would be made for every role at once. Numbers need none:
/// a circle is four of them, a polygon is three per corner, and a role writes
/// them with whatever formatting its own file already uses. What the numbers
/// mean is the provider's business and is never interpreted here.
///
/// <b>Why the label travels with it.</b> Because the alternative is this
/// library inventing one, and "the work area" is the sort of sentence a player
/// should recognise from where they marked it, not from a type name.</summary>
internal readonly struct NpcWorkAreaDescriptor
{
    private readonly IReadOnlyList<float>? _shape;

    internal NpcWorkAreaDescriptor(NpcWorkAreaId id, IReadOnlyList<float>? shape, string? describe)
    {
        Id = id;
        _shape = shape;
        Describe = describe ?? string.Empty;
    }

    /// <summary>Who rebuilds it, and which one it is.</summary>
    internal NpcWorkAreaId Id { get; }

    /// <summary>The geometry, in whatever order the provider named by
    /// <see cref="Id"/> writes and reads. Never empty for a real area, and never
    /// read by anything in this library but the provider it belongs to.</summary>
    internal IReadOnlyList<float> Shape => _shape ?? Array.Empty<float>();

    /// <summary>What to call it in a sentence shown to a player.</summary>
    internal string Describe { get; }

    /// <summary>Whether this is worth handing to a provider at all: named, and
    /// carrying a shape whose every number is a number. A NaN that reached a
    /// containment test would make an area that answers false everywhere while
    /// looking perfectly valid - the failure that looks like it worked - so it
    /// is refused here, once, rather than at every provider.</summary>
    internal bool IsWellFormed
    {
        get
        {
            if (!Id.IsNamed)
            {
                return false;
            }

            IReadOnlyList<float> shape = Shape;
            if (shape.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < shape.Count; index++)
            {
                float value = shape[index];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public override string ToString() => Id.ToString() + " (" + Shape.Count.ToString(CultureInfo.InvariantCulture) + " numbers)";
}
