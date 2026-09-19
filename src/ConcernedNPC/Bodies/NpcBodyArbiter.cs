using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Bodies;

/// <summary>The process-wide answer to "may this role construct a body of this
/// kind, for this identity, right now".
///
/// <b>What it guarantees: that one identity never has two bodies.</b> A
/// presentation figure and a saved worker body standing in one world are two of
/// the same NPC, and every question the player then asks - who do I talk to, who
/// is holding my axe, which one do I feed - has two answers. Today that rule is
/// a sentence in three documents plus <c>MayRetireBody</c>, a property nothing
/// calls. This type is the smallest thing that makes it true instead of
/// intended, and the tests get at it by trying to break it rather than by
/// reading it back.
///
/// <b>How.</b> One record per registered identity, holding the identity's single
/// <see cref="ActorModeOwner"/> and the one body kind that currently exists for
/// it, with the holder that claimed it and the world it was claimed in. A claim
/// of the other kind is refused while a body exists; a claim by a second holder
/// is refused while a first has it or while a job holds the identity's mode; a
/// presentation claim is refused unless the identity is resting, because
/// presentation is what an NPC is when nobody has ordered it to do anything.
/// Nothing here tears down somebody else's body to make room: the holder of the
/// existing body releases it, or the claim stays refused.
///
/// <b>Registration is per process; a body is per world load.</b> Roles register
/// from their plugin's <c>Awake</c>, once, and the records outlive every world.
/// A body does not: quitting to the main menu destroys every saved object with
/// the scene. So every hold is stamped with the world it was taken in and a hold
/// from any other world counts as no hold at all - which closes two opposite
/// failures that a process-wide hold produces. A runtime with a stable holder id
/// would otherwise be told <c>AlreadyHeld</c>, a grant, for a body destroyed
/// with the previous world; a runtime with a per-session holder id would
/// otherwise be refused forever by a job that ended with that world, with no
/// holder string left in existence to release it. The second is the same
/// "permanently busy" failure this type already takes care to avoid one level
/// down, in <see cref="Release"/>, and reintroducing it one level up where
/// nothing can reach it would be worse.
///
/// <b>Where it is reached from.</b> Only through <see cref="NpcRoleRegistry"/>,
/// which is the only thing that creates one and the only thing that creates an
/// <see cref="ActorModeOwner"/>. That is deliberate. The failure this prevents
/// is two arbiters for one identity, each correct and each unaware of the other,
/// and the surest way to prevent it is that a caller has nowhere to construct a
/// second one from. A grant hands back a <see cref="BodyLease"/>, which the body
/// factory requires, so the rule is something a role cannot route around rather
/// than something it is asked to respect.
///
/// <b>What it is not.</b> Not a body factory, not a census, not a spawner. It
/// knows nothing about prefabs, ZDOs, Unity or the game; it answers a question
/// about permission and records the answer. Building the body, finding it again
/// after a reload and refusing to spawn while the census is still searching are
/// separate problems in a later leaf.
///
/// <b>Threading.</b> Not thread safe, and does not need to be: every caller is
/// on the game's main thread, like the rest of this repository's runtime.
/// </summary>
internal sealed class NpcBodyArbiter
{
    private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>(StringComparer.Ordinal);

    /// <summary>How many identities this arbiter is tracking. One per registered
    /// role, ever - unlike the bodies they hold, which come and go with each
    /// world.</summary>
    internal int Count => _records.Count;

    /// <summary>Starts tracking an identity and creates its single mode owner.
    /// Called once, by registration, and refuses a second call for the same
    /// identity - a second record would be a second owner, which is the
    /// condition this type exists to prevent.</summary>
    internal bool Track(NpcIdentity identity, NpcBodyKind contractedKind)
    {
        if (identity.IsEmpty || _records.ContainsKey(identity.Value))
        {
            return false;
        }

        _records.Add(identity.Value, new Record(identity, contractedKind));
        return true;
    }

    internal bool IsTracked(NpcIdentity identity) =>
        !identity.IsEmpty && _records.ContainsKey(identity.Value);

    /// <summary>Which kind of body exists for this identity in
    /// <paramref name="world"/> right now, or
    /// <see cref="NpcBodyKind.Unspecified"/> for none - including for an
    /// identity nobody registered, for a hold taken in a different world, and
    /// for an unknown world, because in none of those cases does a body this
    /// process knows of exist.</summary>
    internal NpcBodyKind CurrentBodyKind(NpcIdentity identity, NpcWorldEpoch world) =>
        TryGet(identity, out Record record) && record.HoldsBodyIn(world)
            ? record.BodyKind
            : NpcBodyKind.Unspecified;

    /// <summary>The identity's one mode owner, or null if it is not tracked.
    /// Returned rather than recreated: there is exactly one per identity for the
    /// life of the process.</summary>
    internal ActorModeOwner? ModeOf(NpcIdentity identity) =>
        TryGet(identity, out Record record) ? record.Mode : null;

    /// <summary>May <paramref name="holder"/> construct a body of
    /// <paramref name="kind"/> for <paramref name="identity"/> in
    /// <paramref name="world"/> now?
    ///
    /// Idempotent for a holder that already has this body in this world, so a
    /// runtime that re-asks every tick is not punished for it. A holder that had
    /// it in a <i>previous</i> world is not: that hold is dropped and the claim
    /// is judged afresh, because the body it named does not exist.</summary>
    internal BodyClaim TryClaim(NpcIdentity identity, NpcBodyKind kind, string? holder, NpcWorldEpoch world)
    {
        if (!TryGet(identity, out Record record))
        {
            return Refuse(BodyClaimStatus.RefusedNotRegistered, identity, kind, holder,
                "no role is registered for this identity, so nothing declared how its body would be found again");
        }

        if (world.IsUnknown)
        {
            return Refuse(BodyClaimStatus.RefusedNoWorld, identity, kind, holder,
                "no world is loaded, and a body exists in a world");
        }

        if (string.IsNullOrEmpty(holder))
        {
            return Refuse(BodyClaimStatus.RefusedNoHolder, identity, kind, holder,
                "a body needs a holder that can be required to release it");
        }

        if (kind == NpcBodyKind.Unspecified)
        {
            return Refuse(BodyClaimStatus.RefusedKindNotContracted, identity, kind, holder,
                "a claim must say which kind of body it wants");
        }

        // A hold from another world is not a hold. Dropped here rather than
        // refused, because the body it named was destroyed with that world's
        // scene: failing open on the BODY is the only answer that matches what
        // is actually standing in the world, and failing closed would leave the
        // identity refused forever by a holder string nothing still has.
        record.ForgetBodyUnless(world);

        if (record.BodyKind != NpcBodyKind.Unspecified)
        {
            if (record.BodyKind != kind)
            {
                // The never-coexist rule is checked FIRST, before whether this
                // role contracted for the kind it is asking for. Both would
                // refuse, and which refusal a caller is told matters: "a
                // presentation body already exists" is the rule, while "you did
                // not contract for this" is an accident of a role having
                // exactly one contracted kind today. A rule that holds only
                // because of an unrelated restriction is not a rule.
                return Refuse(BodyClaimStatus.RefusedOtherKindExists, identity, kind, holder,
                    "a " + record.BodyKind + " body already exists for this identity, held by '" +
                    record.BodyHolder + "'; it is released, never replaced");
            }

            if (record.IsBodyHeldBy(holder))
            {
                return new BodyClaim(BodyClaimStatus.AlreadyHeld, identity, kind, holder!, string.Empty, record.Lease);
            }

            return Refuse(BodyClaimStatus.RefusedHolderConflict, identity, kind, holder,
                "'" + record.BodyHolder + "' already holds this identity's " + kind + " body");
        }

        if (kind != record.ContractedKind)
        {
            return Refuse(BodyClaimStatus.RefusedKindNotContracted, identity, kind, holder,
                "this role contracted for " + record.ContractedKind + ", not " + kind);
        }

        if (record.Mode.JobId != null && !record.Mode.IsHeldBy(holder))
        {
            return Refuse(BodyClaimStatus.RefusedHolderConflict, identity, kind, holder,
                "job '" + record.Mode.JobId + "' holds this identity (" + record.Mode.Mode + ")");
        }

        if (kind == NpcBodyKind.Presentation && !record.Mode.MayRetireBody)
        {
            // Presentation is what an NPC is when nobody has ordered it to do
            // anything. Appearing at camp while a job is in flight would put the
            // figure wherever home is while the working body is wherever the
            // work is.
            return Refuse(BodyClaimStatus.RefusedHolderConflict, identity, kind, holder,
                "a presentation body may only be claimed while the identity is resting; it is " +
                record.Mode.Mode);
        }

        BodyLease lease = record.HoldBody(this, kind, holder!, world);
        return new BodyClaim(BodyClaimStatus.Claimed, identity, kind, holder!, string.Empty, lease);
    }

    /// <summary>Gives up a body. Releasing one this holder does not have is a
    /// no-op that says so, never an exception and never a way to clear somebody
    /// else's claim - so a runtime tidying up after a reload, a death or a
    /// failure can release unconditionally, which every reservation in this
    /// repository already relies on.
    ///
    /// If the holder also held the identity's mode, that is released with the
    /// body: the job is over, and leaving the identity held by a job whose body
    /// is gone is how an NPC becomes permanently busy.</summary>
    internal BodyClaim Release(NpcIdentity identity, NpcBodyKind kind, string? holder, NpcWorldEpoch world)
    {
        if (!TryGet(identity, out Record record))
        {
            return Refuse(BodyClaimStatus.RefusedNotRegistered, identity, kind, holder,
                "no role is registered for this identity");
        }

        if (world.IsUnknown)
        {
            // Nothing is held in a world that is not loaded, and a release is
            // never the event that ends a world - ForgetWorld is. Answering here
            // keeps a caller tidying up after an unload from evicting a job
            // through a verb that is not supposed to be able to.
            return Refuse(BodyClaimStatus.NotHeld, identity, kind, holder, "no world is loaded");
        }

        record.ForgetBodyUnless(world);

        if (string.IsNullOrEmpty(holder) || record.BodyKind != kind || !record.IsBodyHeldBy(holder))
        {
            return Refuse(BodyClaimStatus.NotHeld, identity, kind, holder,
                record.BodyKind == NpcBodyKind.Unspecified
                    ? "this identity has no body in this world"
                    : "this identity's " + record.BodyKind + " body is held by '" + record.BodyHolder + "'");
        }

        string held = record.BodyHolder;
        record.ReleaseBody();
        record.Mode.Release(held);
        return new BodyClaim(BodyClaimStatus.Released, identity, kind, holder!, string.Empty, null);
    }

    /// <summary>Whether this exact lease still permits a body: the arbiter
    /// records this holder holding this kind for this identity, in the world the
    /// lease was taken in.</summary>
    /// <summary>Whether this exact lease is the one currently holding.
    ///
    /// The identity check is the lease itself, not what it says. Claim, dispose
    /// and claim again under the same holder in the same world and the values
    /// match on every field, so a disposed lease would report itself live and
    /// could release the hold its successor is relying on.</summary>
    internal bool IsHeldBy(BodyLease lease) =>
        lease != null
        && TryGet(lease.Identity, out Record record)
        && ReferenceEquals(record.Lease, lease)
        && record.HoldsBodyIn(lease.World)
        && record.BodyKind == lease.Kind
        && record.IsBodyHeldBy(lease.Holder);

    /// <summary>Releases what one lease holds, if it still holds anything.
    /// Idempotent, and a no-op for a lease whose world has gone.</summary>
    internal void ReleaseLease(BodyLease lease)
    {
        if (IsHeldBy(lease))
        {
            Release(lease.Identity, lease.Kind, lease.Holder, lease.World);
        }
    }

    /// <summary>Forgets every body, because the world they stood in has gone.
    /// Returns how many were forgotten, so a caller can log that it happened.
    ///
    /// Identities and their mode owners survive - a role is registered once per
    /// process - but every hold and every job is ended, because both belonged to
    /// that world. This is the explicit event; the per-hold world stamp is the
    /// invariant that holds even if nobody says it.</summary>
    internal int ForgetWorld()
    {
        int forgotten = 0;
        foreach (Record record in _records.Values)
        {
            if (record.BodyKind != NpcBodyKind.Unspecified)
            {
                forgotten++;
            }

            record.ForgetBodyUnless(NpcWorldEpoch.Unknown);
        }

        return forgotten;
    }

    private bool TryGet(NpcIdentity identity, out Record record)
    {
        if (identity.IsEmpty)
        {
            record = null!;
            return false;
        }

        return _records.TryGetValue(identity.Value, out record!);
    }

    private static BodyClaim Refuse(
        BodyClaimStatus status, NpcIdentity identity, NpcBodyKind kind, string? holder, string reason) =>
        new BodyClaim(status, identity, kind, holder ?? string.Empty, reason, null);

    /// <summary>Everything this arbiter knows about one identity: its single
    /// mode owner, the kind of body it was registered to have, and the body that
    /// exists right now - with who holds it and which world it stands in.
    /// </summary>
    private sealed class Record
    {
        internal Record(NpcIdentity identity, NpcBodyKind contractedKind)
        {
            Identity = identity;
            ContractedKind = contractedKind;
            Mode = new ActorModeOwner(identity);
            BodyHolder = string.Empty;
        }

        internal NpcIdentity Identity { get; }

        internal NpcBodyKind ContractedKind { get; }

        internal ActorModeOwner Mode { get; }

        internal NpcBodyKind BodyKind { get; private set; }

        internal string BodyHolder { get; private set; }

        internal NpcWorldEpoch BodyWorld { get; private set; }

        internal BodyLease? Lease { get; private set; }

        internal bool HoldsBodyIn(NpcWorldEpoch world) =>
            BodyKind != NpcBodyKind.Unspecified && BodyWorld.Matches(world);

        internal bool IsBodyHeldBy(string? holder) =>
            BodyHolder.Length != 0 && holder != null &&
            string.Equals(BodyHolder, holder, StringComparison.Ordinal);

        internal BodyLease HoldBody(NpcBodyArbiter arbiter, NpcBodyKind kind, string holder, NpcWorldEpoch world)
        {
            BodyKind = kind;
            BodyHolder = holder;
            BodyWorld = world;
            Lease = new BodyLease(arbiter, Identity, kind, holder, world);
            return Lease;
        }

        internal void ReleaseBody()
        {
            BodyKind = NpcBodyKind.Unspecified;
            BodyHolder = string.Empty;
            BodyWorld = NpcWorldEpoch.Unknown;
            Lease = null;
        }

        /// <summary>Drops a hold that belongs to any world but this one, and
        /// with it the job that took it. Passing
        /// <see cref="NpcWorldEpoch.Unknown"/> drops every hold, because an
        /// unknown epoch matches nothing - which is how the explicit unload and
        /// the per-hold stamp end up being the same line of code.</summary>
        internal void ForgetBodyUnless(NpcWorldEpoch world)
        {
            if (BodyKind == NpcBodyKind.Unspecified)
            {
                if (!world.IsUnknown)
                {
                    return;
                }

                // An unload ends the identity's job even when no body was ever
                // built for it: the job belonged to that world too.
                Mode.AbandonForWorldUnload();
                return;
            }

            if (BodyWorld.Matches(world))
            {
                return;
            }

            ReleaseBody();
            Mode.AbandonForWorldUnload();
        }
    }
}
