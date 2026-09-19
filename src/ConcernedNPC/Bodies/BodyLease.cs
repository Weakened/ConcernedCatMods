using System;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Bodies;

/// <summary>Permission to have a body, in a form that can be required.
///
/// <b>Why this exists: so the never-coexist rule is structural rather than
/// advisory.</b> An outcome struct is advice. A role can read
/// <see cref="BodyClaim.IsGranted"/>, ignore it, and build a body anyway;
/// nothing in the type system notices, and the rule is enforced only for
/// callers who choose to be bound by it - which is what it was when it was
/// prose. A lease is different: it is handed out <b>only</b> on a grant, it
/// cannot be constructed from outside this assembly, and the body factory that
/// arrives in the lifecycle leaf <b>takes one as a parameter</b>. From then on a
/// role that was refused has nothing to pass, and building a body it was refused
/// stops being a discipline and becomes a compile error.
///
/// <b>What it guarantees.</b>
///
/// <i>It cannot be forged.</i> The constructor is internal and there is no
/// <c>InternalsVisibleTo</c> anywhere in this package - a rule with its own
/// audit, because one such attribute would undo this and the registry's
/// single-arbiter guarantee together.
///
/// <i>It knows which world it belongs to.</i> A body exists in one world load. A
/// lease from a previous load is not active, whatever anybody kept a reference
/// to, because the body it named was destroyed with that world's scene.
///
/// <i>Giving it up is idempotent and safe from anywhere.</i>
/// <see cref="Dispose"/> may be called twice, after a world unload, or by a
/// <c>finally</c> that does not know whether the claim succeeded. It releases
/// what this lease holds and never touches anybody else's.
///
/// <b>What it is not.</b> Not the body, and not a handle to one. It carries no
/// game object, no prefab and no network id. It says an identity is allowed one
/// body of one kind right now, held by one holder, in one world.</summary>
public sealed class BodyLease : IDisposable
{
    private readonly NpcBodyArbiter _arbiter;

    internal BodyLease(NpcBodyArbiter arbiter, NpcIdentity identity, NpcBodyKind kind, string holder, NpcWorldEpoch world)
    {
        _arbiter = arbiter;
        Identity = identity;
        Kind = kind;
        Holder = holder;
        World = world;
    }

    /// <summary>Whose body this permits.</summary>
    public NpcIdentity Identity { get; }

    /// <summary>Which kind of body it permits.</summary>
    public NpcBodyKind Kind { get; }

    /// <summary>Who holds it, and who must give it up.</summary>
    public string Holder { get; }

    /// <summary>The world load this lease belongs to. It is worth nothing in any
    /// other.</summary>
    internal NpcWorldEpoch World { get; }

    /// <summary>Whether this lease still permits a body: it has not been
    /// disposed, the arbiter still records this holder holding this kind, and
    /// the world it was taken in is still loaded.
    ///
    /// Asked by the body factory before it builds anything, not once at the
    /// start.</summary>
    public bool IsActive => _arbiter.IsHeldBy(this);

    /// <summary>Gives the body up. Idempotent, and a no-op once the world it
    /// belonged to has gone.</summary>
    public void Dispose()
    {
        _arbiter.ReleaseLease(this);
    }

    public override string ToString() =>
        Identity + " " + Kind + " held by '" + Holder + "'" + (IsActive ? string.Empty : " (expired)");
}
