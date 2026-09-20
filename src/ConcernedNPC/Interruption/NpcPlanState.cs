using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>Everything about one plan that has to survive the process dying.
///
/// <b>The field list is the acceptance criterion, not a convenience.</b> Plan
/// identity, current phase, reservations, carried inventory, target progress,
/// assigned vehicle or cart, source and destination, custody state: each of
/// those is here because a plan that came back without it would have to start
/// over, and starting over is how material gets fetched twice.
///
/// <b>Immutable, and copied rather than edited.</b> A plan's durable state is
/// written down before it is acted on, so the thing acted on and the thing
/// written must be the same thing. A settable field would let the two drift
/// between the write and the act, which is the exact window this leaf exists to
/// close. Every change produces a new value through a <c>With</c> method, and
/// the run's own writer is the only thing that adopts one.
///
/// <b>Keys are text this library never interprets.</b> The source, the
/// destination and the vehicle are named by whatever the role names them by. This
/// library does not parse them, does not compose them and owns none of them - and
/// because a world load renumbers every world object, a key read back from disk
/// is carried as evidence of what the plan was about rather than as something to
/// act on. <see cref="World"/> is what says which of the two it is, and a state
/// read back from disk always carries <see cref="NpcWorldEpoch.Unknown"/>, which
/// matches nothing.
///
/// <b>There is no schema here, and no field name.</b> This type is a runtime
/// value. What its fields are called on disk, in what order, under which row tag
/// and at which version is the role's, through
/// <see cref="INpcPlanCodec"/> - which is why moving a role onto this needs no
/// migration.</summary>
internal sealed class NpcPlanState
{
    private static readonly ReservationId[] NoReservations = new ReservationId[0];
    private static readonly NpcMaterialStack[] NoMaterial = new NpcMaterialStack[0];

    internal NpcPlanState(
        NpcIdentity identity,
        string? jobId,
        NpcPlanPhase phase,
        NpcPlanCustody custody,
        IReadOnlyList<ReservationId>? reservations,
        IReadOnlyList<NpcMaterialStack>? carried,
        int targetsDone,
        int targetsTotal,
        string? sourceKey,
        string? destinationKey,
        string? vehicleKey,
        NpcWorldEpoch world,
        int attempt,
        string? note)
    {
        Identity = identity;
        JobId = jobId ?? string.Empty;
        Phase = phase;
        Custody = custody;
        Reservations = Copy(reservations);
        Carried = Copy(carried);
        TargetsDone = targetsDone < 0 ? 0 : targetsDone;
        TargetsTotal = targetsTotal < 0 ? 0 : targetsTotal;
        SourceKey = sourceKey ?? string.Empty;
        DestinationKey = destinationKey ?? string.Empty;
        VehicleKey = vehicleKey ?? string.Empty;
        World = world;
        Attempt = attempt < 0 ? 0 : attempt;
        Note = note ?? string.Empty;
    }

    /// <summary>A plan at its beginning: named, in the world it was made in,
    /// holding nothing and having done nothing.
    ///
    /// <b>Nothing is defaulted that makes a claim.</b> The target total is
    /// required, because a total of zero says the plan covers no work and a plan
    /// that reported itself finished on a silent zero is the failure the planning
    /// leaf already paid for once.</summary>
    internal static NpcPlanState Opening(
        NpcIdentity identity, string? jobId, NpcWorldEpoch world, int targetsTotal) =>
        new NpcPlanState(
            identity,
            jobId,
            NpcPlanPhase.Observing,
            NpcPlanCustody.Clear,
            null,
            null,
            0,
            targetsTotal,
            null,
            null,
            null,
            world,
            0,
            null);

    /// <summary>Which NPC this plan belongs to. <b>Half of the plan's identity</b>
    /// - the half that makes a resumed plan re-attach to a body rather than
    /// build one.</summary>
    internal NpcIdentity Identity { get; }

    /// <summary>The job's own stable name. The other half of the plan's
    /// identity, and the half every reservation name is derived from.</summary>
    internal string JobId { get; }

    /// <summary>How far it got.</summary>
    internal NpcPlanPhase Phase { get; }

    /// <summary>Whether a movement of material is in flight.</summary>
    internal NpcPlanCustody Custody { get; }

    /// <summary>What this plan has set aside, by name. Derived names, so
    /// re-taking one after a reload is satisfied rather than doubled.</summary>
    internal IReadOnlyList<ReservationId> Reservations { get; }

    /// <summary>What the NPC is carrying on this plan's behalf. <b>Evidence,
    /// not truth</b>: the custody ledger is the truth and an inventory can
    /// vanish with a body. Carried here so that a resumed plan knows what it
    /// should find, and can say so when it does not.</summary>
    internal IReadOnlyList<NpcMaterialStack> Carried { get; }

    /// <summary>How many of the plan's targets are done.</summary>
    internal int TargetsDone { get; }

    /// <summary>How many the plan covers. Zero means the plan covers nothing,
    /// which is a fact a resumed plan needs and never a default.</summary>
    internal int TargetsTotal { get; }

    /// <summary>Where material is coming from, named by the role.</summary>
    internal string SourceKey { get; }

    /// <summary>Where it is going, named by the role.</summary>
    internal string DestinationKey { get; }

    /// <summary>The vehicle or cart assigned to this plan, named by the role.
    /// Empty when there is none - which is most roles, and is why this library
    /// knows the word "vehicle" and not the word for any particular one.
    /// </summary>
    internal string VehicleKey { get; }

    /// <summary>The world load this state's keys were minted in. Unknown for a
    /// state read back from disk, which is what makes every key in it stale
    /// rather than dangerous.</summary>
    internal NpcWorldEpoch World { get; }

    /// <summary>How many times this plan has been reconstructed. Carried as
    /// evidence for a person: a plan on its fifth reconstruction is telling
    /// somebody something a single attempt does not.</summary>
    internal int Attempt { get; }

    /// <summary>What happened, in words, for whoever reads this later. Never a
    /// format and never parsed back.</summary>
    internal string Note { get; }

    /// <summary>Whether this state names a plan at all. A state without both
    /// halves of its identity is not resumable and is not written.</summary>
    internal bool IsNamed => !Identity.IsEmpty && JobId.Length != 0;

    /// <summary>Whether this state's keys may be acted on: named, in a phase
    /// somebody set, and minted in the world that is loaded now.</summary>
    internal bool IsLiveIn(NpcWorldEpoch now) =>
        IsNamed && Phase != NpcPlanPhase.Unspecified && World.Matches(now);

    /// <summary>Whether this plan is holding anything at all - a reservation,
    /// material on the body, or a vehicle. A plan holding nothing can be
    /// abandoned where it stands; one that is not, cannot.</summary>
    internal bool HoldsAnything =>
        Reservations.Count > 0 || Carried.Count > 0 || VehicleKey.Length != 0;

    /// <summary>Whether anything about this plan's record cannot be resolved
    /// from it. Both the unset value and the uncertain one, because a custody
    /// field nobody wrote is not evidence that nothing was in flight.</summary>
    internal bool IsUncertain =>
        Custody == NpcPlanCustody.Uncertain || Custody == NpcPlanCustody.Unspecified;

    /// <summary>How many units of material this plan says are on the body. The
    /// conservation sum's carried term, totalled in one place so no caller adds
    /// it up differently.</summary>
    internal int CarriedUnits
    {
        get
        {
            int total = 0;
            for (int index = 0; index < Carried.Count; index++)
            {
                NpcMaterialStack stack = Carried[index];
                if (stack.IsValid)
                {
                    total += stack.Count;
                }
            }

            return total;
        }
    }

    /// <summary>Whether two states describe the same work in the same
    /// condition: identity, job, reservations, carried material, progress,
    /// source, destination, vehicle and custody.
    ///
    /// <b>This is the "an interruption preserves everything" check, as a
    /// question rather than a paragraph.</b> Phase, world, attempt and note are
    /// deliberately excluded: an interruption is allowed to change where a plan
    /// is and to say why, and is not allowed to change what the plan is about or
    /// what it is holding.</summary>
    internal bool CarriesTheSameWorkAs(NpcPlanState? other)
    {
        if (other == null)
        {
            return false;
        }

        return Identity.Equals(other.Identity)
            && string.Equals(JobId, other.JobId, StringComparison.Ordinal)
            && Custody == other.Custody
            && TargetsDone == other.TargetsDone
            && TargetsTotal == other.TargetsTotal
            && string.Equals(SourceKey, other.SourceKey, StringComparison.Ordinal)
            && string.Equals(DestinationKey, other.DestinationKey, StringComparison.Ordinal)
            && string.Equals(VehicleKey, other.VehicleKey, StringComparison.Ordinal)
            && Same(Reservations, other.Reservations)
            && Same(Carried, other.Carried);
    }

    /// <summary>This state as it comes back from disk.
    ///
    /// <b>Three things happen here and every one of them is load-bearing.</b>
    /// The world becomes unknown, because every in-session key the plan holds
    /// was minted in a world load that has ended and an unknown epoch matches
    /// nothing - so a resumed plan cannot act on a chest id that now names a
    /// tree. The attempt count rises, so a plan that keeps coming back says so.
    /// And a custody field that is pending or unset becomes uncertain, because
    /// an intent with no recorded outcome is the exact shape of an interruption
    /// and replaying it would either duplicate a player's material or delete it.
    ///
    /// <b>This is the library's, not the role's.</b> A codec that forgot to do
    /// it would produce a plan that looked live and resumable, and the failure
    /// would be a duplicated stack rather than a compile error. So the format is
    /// the role's and this is not.</summary>
    internal NpcPlanState AsRecovered() =>
        new NpcPlanState(
            Identity,
            JobId,
            Phase,
            Custody == NpcPlanCustody.Clear ? NpcPlanCustody.Clear : NpcPlanCustody.Uncertain,
            Reservations,
            Carried,
            TargetsDone,
            TargetsTotal,
            SourceKey,
            DestinationKey,
            VehicleKey,
            NpcWorldEpoch.Unknown,
            Attempt + 1,
            Note);

    /// <summary>The same plan, in another phase, with a note saying why.
    /// </summary>
    internal NpcPlanState WithPhase(NpcPlanPhase phase, string? note) =>
        new NpcPlanState(
            Identity, JobId, phase, Custody, Reservations, Carried, TargetsDone, TargetsTotal,
            SourceKey, DestinationKey, VehicleKey, World, Attempt, note ?? Note);

    /// <summary>The same plan with a different custody standing.</summary>
    internal NpcPlanState WithCustody(NpcPlanCustody custody, string? note) =>
        new NpcPlanState(
            Identity, JobId, Phase, custody, Reservations, Carried, TargetsDone, TargetsTotal,
            SourceKey, DestinationKey, VehicleKey, World, Attempt, note ?? Note);

    /// <summary>The same plan, re-attached to the world that is loaded now.
    /// Called only once a resumed plan's references have been established
    /// again; nothing here checks that, because nothing here can.</summary>
    internal NpcPlanState WithWorld(NpcWorldEpoch world) =>
        new NpcPlanState(
            Identity, JobId, Phase, Custody, Reservations, Carried, TargetsDone, TargetsTotal,
            SourceKey, DestinationKey, VehicleKey, world, Attempt, Note);

    /// <summary>The same plan holding what it now holds.</summary>
    internal NpcPlanState WithHoldings(
        IReadOnlyList<ReservationId>? reservations, IReadOnlyList<NpcMaterialStack>? carried) =>
        new NpcPlanState(
            Identity, JobId, Phase, Custody, reservations, carried, TargetsDone, TargetsTotal,
            SourceKey, DestinationKey, VehicleKey, World, Attempt, Note);

    /// <summary>The same plan with its ends named.</summary>
    internal NpcPlanState WithRoute(string? sourceKey, string? destinationKey, string? vehicleKey) =>
        new NpcPlanState(
            Identity, JobId, Phase, Custody, Reservations, Carried, TargetsDone, TargetsTotal,
            sourceKey, destinationKey, vehicleKey, World, Attempt, Note);

    /// <summary>The same plan, further on.</summary>
    internal NpcPlanState WithProgress(int targetsDone) =>
        new NpcPlanState(
            Identity, JobId, Phase, Custody, Reservations, Carried, targetsDone, TargetsTotal,
            SourceKey, DestinationKey, VehicleKey, World, Attempt, Note);

    public override string ToString() =>
        (IsNamed ? Identity.Value + " " + JobId : "<unnamed plan>")
        + " " + Phase + "/" + Custody
        + " " + TargetsDone.ToString(CultureInfo.InvariantCulture)
        + "/" + TargetsTotal.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<ReservationId> Copy(IReadOnlyList<ReservationId>? source)
    {
        if (source == null || source.Count == 0)
        {
            return NoReservations;
        }

        var copy = new ReservationId[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }

    private static IReadOnlyList<NpcMaterialStack> Copy(IReadOnlyList<NpcMaterialStack>? source)
    {
        if (source == null || source.Count == 0)
        {
            return NoMaterial;
        }

        var copy = new NpcMaterialStack[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }

    private static bool Same(IReadOnlyList<ReservationId> left, IReadOnlyList<ReservationId> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(IReadOnlyList<NpcMaterialStack> left, IReadOnlyList<NpcMaterialStack> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
