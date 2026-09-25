using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>One container a player has opened to NPCs, in the shape a role can
/// write down and read back (#374).
///
/// <b>Why this exists beside <see cref="NpcContainerPermission"/> instead of
/// replacing it.</b> That type carries an <see cref="NpcContainerPlace"/>, and a
/// place is a tolerance with a matching rule and a documented reason for every
/// number in it. Making it public would publish the matching rule as an API and
/// invite a role to build one for a container that moves - which the place type's
/// own documentation forbids and cannot enforce. This carries the four numbers
/// and the flag, nothing else, and the book turns them into places.
///
/// <b>It is still not a file.</b> No codec, no header, no row tag, no schema
/// number, no file name. A role writes these into whatever it already writes.
/// The reason is recorded on <see cref="NpcContainerPermission"/> and has not
/// changed: a shared runtime that can name a file in a role's data directory is
/// the first-run detection bug this repository has shipped twice.</summary>
public readonly struct NpcContainerDecision
{
    public NpcContainerDecision(float x, float y, float z, int prefab, NpcContainerUse allowed)
    {
        X = x;
        Y = y;
        Z = z;
        Prefab = prefab;
        Allowed = allowed;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    /// <summary>Which kind of thing it is, as the role names prefabs. Opaque
    /// here: two records match only if this agrees as well as the position, so a
    /// different piece rebuilt on the same spot is a different decision.
    /// </summary>
    public int Prefab { get; }

    /// <summary>What the player allowed. Never
    /// <see cref="NpcContainerUse.Off"/> in a record that came out of a desk -
    /// off is the absence of a record, which is what makes an empty store mean
    /// "nothing is enabled" rather than "nothing is known".</summary>
    public NpcContainerUse Allowed { get; }
}

/// <summary>Which containers a player has opened to NPCs, as a product sees it
/// (#374): the four states, the cycle a key press walks through, and the records
/// to persist.
///
/// <b>Why a facade and not eleven public types.</b> Everything under
/// <c>Storage</c> was internal, so the model was complete, tested, and reachable
/// by nothing: no product imported the namespace, no player could set a
/// permission, and "off by default" was a statement about dead code rather than
/// about behaviour. Publishing the namespace wholesale would have exported the
/// permit, the gate, the assignment and the transfer recorder as well - and
/// <c>NpcContainerPermit</c> is deliberately unforgeable from outside this
/// package, which <c>ContainerTests.NothingOutsideThisPackageCanForgeAPermit</c>
/// asserts. A role does not need to mint a permit; it needs to know what the
/// player allowed, to change it, and to write it down. That is this.
///
/// <b>Off is the default and off is the absence of a record.</b> Every way of
/// arriving at an answer without a deliberate choice - a lookup miss, a record
/// that failed to parse, a store that could not be read, an empty store - lands
/// on <see cref="NpcContainerUse.Off"/>. A player's chests are theirs until they
/// say otherwise, once, per chest.
///
/// <b>Local, and never written into the world.</b> Nothing about a container
/// changes in the save when it is marked. This is the door-permission shape the
/// owner approved for the same question about a different world object, and the
/// same rule applies: what one player lets their own NPCs do is nobody else's
/// business.</summary>
public sealed class NpcContainerDesk
{
    private readonly NpcContainerPermissionBook _book;

    public NpcContainerDesk()
        : this(new NpcContainerPermissionBook())
    {
    }

    private NpcContainerDesk(NpcContainerPermissionBook book)
    {
        _book = book;
    }

    /// <summary>The next state in the cycle a player walks through: off, take,
    /// deposit, both, off. One order, so every role's hover text and every
    /// role's key press agree about what comes next.</summary>
    public static NpcContainerUse NextState(NpcContainerUse current) =>
        NpcContainerPermissionBook.NextState(current);

    /// <summary>How many containers are enabled at all.</summary>
    public int Count => _book.Count;

    /// <summary>True once something changed since the last save.</summary>
    public bool IsDirty => _book.IsDirty;

    /// <summary>Everything the player has decided, in the order they decided it.
    /// What a role persists.</summary>
    public IReadOnlyList<NpcContainerDecision> Decisions
    {
        get
        {
            var records = new List<NpcContainerDecision>(_book.Count);
            foreach (NpcContainerPermission permission in _book.Allowed)
            {
                records.Add(new NpcContainerDecision(
                    permission.Place.Position.X,
                    permission.Place.Position.Y,
                    permission.Place.Position.Z,
                    permission.Place.Prefab,
                    permission.Allowed));
            }

            return records;
        }
    }

    /// <summary>Rebuilds a desk from what a role read back, reporting how many
    /// records were dropped so the role can tell the player some of their
    /// permissions did not come back rather than leaving them to find out at a
    /// chest. Every drop is a forgotten permission, which only ever keeps an NPC
    /// out.</summary>
    public static NpcContainerDesk Restore(
        IEnumerable<NpcContainerDecision>? records, out int dropped)
    {
        var permissions = new List<NpcContainerPermission>();
        if (records != null)
        {
            foreach (NpcContainerDecision record in records)
            {
                permissions.Add(new NpcContainerPermission(PlaceOf(record), record.Allowed));
            }
        }

        NpcContainerPermissionBook book =
            NpcContainerPermissionBook.Restore(permissions, out dropped);
        return new NpcContainerDesk(book);
    }

    /// <summary>What the player allows for the container standing here.
    /// <see cref="NpcContainerUse.Off"/> for one nobody has spoken about, which
    /// is most of them.</summary>
    public NpcContainerUse Allowance(NpcPoint position, int prefab) =>
        _book.Allowance(new NpcContainerPlace(position, prefab));

    /// <summary>Sets what NPCs may do with this container, returning whether
    /// anything changed. Setting <see cref="NpcContainerUse.Off"/> forgets it, so
    /// the desk only ever holds allowances.</summary>
    public bool SetAllowance(NpcPoint position, int prefab, NpcContainerUse allowed) =>
        _book.SetAllowance(new NpcContainerPlace(position, prefab), allowed);

    /// <summary>Moves this container to the next of the four states and returns
    /// what it is now. What a key press does.</summary>
    public NpcContainerUse Cycle(NpcPoint position, int prefab) =>
        _book.Cycle(new NpcContainerPlace(position, prefab));

    /// <summary>Forgets every container, returning how many there were. The verb
    /// an uninstall uses, and the one a player uses when they have lost track.
    /// </summary>
    public int Clear() => _book.Clear();

    public void MarkClean() => _book.MarkClean();

    /// <summary>Whether this container could be remembered at all. A position
    /// nobody could compute can never be found again, so a permission recorded
    /// for it would be one nothing could ever match.
    ///
    /// <b>A container that moves has no place</b> - one riding a cart - and this
    /// cannot tell. Whether a container is fixed is a fact only the role can
    /// see, so the role decides and this says so rather than guessing.</summary>
    public static bool CanBeRemembered(NpcPoint position) => position.IsFinite;

    private static NpcContainerPlace PlaceOf(NpcContainerDecision record) =>
        new NpcContainerPlace(new NpcPoint(record.X, record.Y, record.Z), record.Prefab);
}
