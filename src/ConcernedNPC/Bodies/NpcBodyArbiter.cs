using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Roles;

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
/// it, with the holder that claimed it. A claim of the other kind is refused
/// while a body exists; a claim by a second holder is refused while a first has
/// it or while a job holds the identity's mode; a presentation claim is refused
/// unless the identity is resting, because presentation is what an NPC is when
/// nobody has ordered it to do anything. Nothing here tears down somebody else's
/// body to make room: the holder of the existing body releases it, or the claim
/// stays refused.
///
/// <b>Where it is reached from.</b> Only through <see cref="NpcRoleRegistry"/>,
/// which is the only thing that creates one and the only thing that creates an
/// <see cref="ActorModeOwner"/>. That is deliberate. The failure this prevents
/// is two arbiters for one identity, each correct and each unaware of the other,
/// and the surest way to prevent it is that a caller has nowhere to construct a
/// second one from.
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
    /// role, ever.</summary>
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

    /// <summary>Which kind of body exists for this identity right now, or
    /// <see cref="NpcBodyKind.Unspecified"/> for none - including for an
    /// identity nobody registered, because an untracked identity has no body
    /// this process knows of and saying so is not the same as saying it has
    /// one.</summary>
    internal NpcBodyKind CurrentBodyKind(NpcIdentity identity) =>
        TryGet(identity, out Record record) ? record.BodyKind : NpcBodyKind.Unspecified;

    /// <summary>The identity's one mode owner, or null if it is not tracked.
    /// Returned rather than recreated: there is exactly one per identity for the
    /// life of the process.</summary>
    internal ActorModeOwner? ModeOf(NpcIdentity identity) =>
        TryGet(identity, out Record record) ? record.Mode : null;

    /// <summary>May <paramref name="holder"/> construct a body of
    /// <paramref name="kind"/> for <paramref name="identity"/> now?
    ///
    /// Idempotent for a holder that already has this body, so a runtime that
    /// re-asks every tick, or after a reload, is not punished for asking twice.
    /// </summary>
    internal BodyClaim TryClaim(NpcIdentity identity, NpcBodyKind kind, string? holder)
    {
        if (!TryGet(identity, out Record record))
        {
            return Refuse(BodyClaimStatus.RefusedNotRegistered, identity, kind, holder,
                "no role is registered for this identity, so nothing declared how its body would be found again");
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

        // The never-coexist rule is checked FIRST, before whether this role
        // contracted for the kind it is asking for. Both would refuse, and
        // which refusal a caller is told matters: "a presentation body already
        // exists" is the rule, while "you did not contract for this" is an
        // accident of a role having exactly one contracted kind today. A rule
        // that holds only because of an unrelated restriction is not a rule,
        // and the day an identity is allowed two contracts this is the line
        // that has to already be right.
        if (record.BodyKind != NpcBodyKind.Unspecified)
        {
            if (record.BodyKind != kind)
            {
                return Refuse(BodyClaimStatus.RefusedOtherKindExists, identity, kind, holder,
                    "a " + record.BodyKind + " body already exists for this identity, held by '" +
                    record.BodyHolder + "'; it is released, never replaced");
            }

            if (record.IsBodyHeldBy(holder))
            {
                return new BodyClaim(BodyClaimStatus.AlreadyHeld, identity, kind, holder!, string.Empty);
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

        record.HoldBody(kind, holder!);
        return new BodyClaim(BodyClaimStatus.Claimed, identity, kind, holder!, string.Empty);
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
    internal BodyClaim Release(NpcIdentity identity, NpcBodyKind kind, string? holder)
    {
        if (!TryGet(identity, out Record record))
        {
            return Refuse(BodyClaimStatus.RefusedNotRegistered, identity, kind, holder,
                "no role is registered for this identity");
        }

        if (string.IsNullOrEmpty(holder) || record.BodyKind != kind || !record.IsBodyHeldBy(holder))
        {
            return Refuse(BodyClaimStatus.NotHeld, identity, kind, holder,
                record.BodyKind == NpcBodyKind.Unspecified
                    ? "this identity has no body"
                    : "this identity's " + record.BodyKind + " body is held by '" + record.BodyHolder + "'");
        }

        record.ReleaseBody();
        record.Mode.Release(holder!);
        return new BodyClaim(BodyClaimStatus.Released, identity, kind, holder!, string.Empty);
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
        new BodyClaim(status, identity, kind, holder ?? string.Empty, reason);

    /// <summary>Everything this arbiter knows about one identity: its single
    /// mode owner, the kind of body it was registered to have, and the body that
    /// exists right now with who holds it.</summary>
    private sealed class Record
    {
        internal Record(NpcIdentity identity, NpcBodyKind contractedKind)
        {
            ContractedKind = contractedKind;
            Mode = new ActorModeOwner(identity);
            BodyHolder = string.Empty;
        }

        internal NpcBodyKind ContractedKind { get; }

        internal ActorModeOwner Mode { get; }

        internal NpcBodyKind BodyKind { get; private set; }

        internal string BodyHolder { get; private set; }

        internal bool IsBodyHeldBy(string? holder) =>
            BodyHolder.Length != 0 && holder != null &&
            string.Equals(BodyHolder, holder, StringComparison.Ordinal);

        internal void HoldBody(NpcBodyKind kind, string holder)
        {
            BodyKind = kind;
            BodyHolder = holder;
        }

        internal void ReleaseBody()
        {
            BodyKind = NpcBodyKind.Unspecified;
            BodyHolder = string.Empty;
        }
    }
}
