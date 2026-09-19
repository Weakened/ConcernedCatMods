using System;

namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>Which NPC, across products: the product whose runtime owns the
/// body, and the NPC's stable slug. <c>foreman/thorstein</c>,
/// <c>teamster/gunnar</c>, <c>cartographer/hulgi</c>.
///
/// <b>What it guarantees.</b> Two things, and only these two. First, that one
/// logical NPC has exactly one name in this process, so the arbiter can hold one
/// mode owner and one body kind for it. Second, that the name is stable across
/// sessions and reloads, unlike anything the game assigns: a ZDO id is
/// renumbered every time a world loads, so it can never be an NPC's identity.
///
/// <b>What it is not.</b> It is not a persistence format that this library owns.
/// This library writes nothing, and this type is never the source of a byte on
/// disk. <see cref="Value"/> is <c>product/role</c> because that is already the
/// wire form shipped products write into settlement journal rows and pass across
/// the capability boundary as <c>WorkerKey.Value</c> - so a role converts at the
/// boundary and its existing files keep parsing, unchanged, with no migration.
///
/// <b>Why this is not <c>WorkerKey</c> itself.</b> <c>WorkerKey</c> lives in
/// <c>src/Shared/Workers</c>, where every type is <c>internal</c> because each
/// product compiles its own copy into its own assembly. An <c>internal</c> type
/// cannot appear in a library's public surface, and if this library compiled
/// that source in as well there would be two distinct CLR types called
/// <c>TheConcernedCat.Workers.WorkerKey</c> - the product's and this one's - and
/// a role could not pass its own key to a method here. So the type is new and
/// the <i>text</i> is identical, checked by <see cref="NpcSlug"/> against the
/// shipped rules. Converting is one line in either direction:
/// <c>NpcIdentity.TryParse(key.Value, out var id)</c> and
/// <c>WorkerKey.TryParse(id.Value, out var key)</c>, and both are total.</summary>
public readonly struct NpcIdentity : IEquatable<NpcIdentity>
{
    /// <summary>Builds an identity, or throws if either half is not a valid
    /// slug. Roles declare their identity as a constant, so a bad one is a
    /// programming error found on the first run, not a runtime condition. Code
    /// handling text from outside uses <see cref="TryParse"/>, which never
    /// throws.</summary>
    public NpcIdentity(string product, string role)
    {
        Product = NpcSlug.Require(product, nameof(product));
        Role = NpcSlug.Require(role, nameof(role));
    }

    /// <summary>The product whose runtime owns this NPC's body. Left half of
    /// <see cref="Value"/>.</summary>
    public string Product { get; }

    /// <summary>This NPC's stable slug within that product. Right half of
    /// <see cref="Value"/>.</summary>
    public string Role { get; }

    /// <summary>True for <c>default(NpcIdentity)</c>, which is never a valid
    /// identity and is never registered. Both halves are checked: they are
    /// always either both set or both absent today, so checking one happens to
    /// work - and a rule that is only correct because of an invariant somewhere
    /// else is a rule that breaks silently when that invariant moves.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Product) || string.IsNullOrEmpty(Role);

    /// <summary><c>product/role</c>: the form shipped products already write and
    /// read. Empty for <see cref="IsEmpty"/>.</summary>
    public string Value => IsEmpty ? string.Empty : Product + "/" + Role;

    /// <summary>Parses <c>product/role</c>. Returns false rather than throwing
    /// for anything else, including a second slash, an empty half, or a half
    /// that is not a valid slug.</summary>
    public static bool TryParse(string? text, out NpcIdentity identity)
    {
        identity = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int slash = text!.IndexOf('/');
        if (slash <= 0 || slash != text.LastIndexOf('/') || slash == text.Length - 1)
        {
            return false;
        }

        string product = text.Substring(0, slash);
        string role = text.Substring(slash + 1);
        if (!NpcSlug.IsValid(product) || !NpcSlug.IsValid(role))
        {
            return false;
        }

        identity = new NpcIdentity(product, role);
        return true;
    }

    public bool Equals(NpcIdentity other) =>
        string.Equals(Product, other.Product, StringComparison.Ordinal) &&
        string.Equals(Role, other.Role, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is NpcIdentity other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => IsEmpty ? "<empty>" : Value;

    public static bool operator ==(NpcIdentity left, NpcIdentity right) => left.Equals(right);

    public static bool operator !=(NpcIdentity left, NpcIdentity right) => !left.Equals(right);
}
