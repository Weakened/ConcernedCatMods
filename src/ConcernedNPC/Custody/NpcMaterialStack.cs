using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>A quantity of one material. <b>Deliberately a value</b>: nothing
/// can hold a reference to "the wood" and change it behind the ledger's back.
///
/// An empty stack is an absent stack. Representing nothing twice - as a missing
/// entry and as a zero - is how a total comes to be counted twice, so a stack
/// with no units in it is not a stack and <see cref="IsValid"/> says so.
/// </summary>
internal readonly struct NpcMaterialStack : IEquatable<NpcMaterialStack>
{
    internal NpcMaterialStack(NpcMaterial material, int count)
    {
        Material = material;
        Count = count;
    }

    internal NpcMaterial Material { get; }

    internal int Count { get; }

    internal bool IsValid => Material.IsNamed && Count > 0;

    public bool Equals(NpcMaterialStack other) => Material.Equals(other.Material) && Count == other.Count;

    public override bool Equals(object? obj) => obj is NpcMaterialStack other && Equals(other);

    public override int GetHashCode() => unchecked((Material.GetHashCode() * 397) ^ Count);

    public override string ToString() => Material + " x" + Count.ToString(CultureInfo.InvariantCulture);
}
