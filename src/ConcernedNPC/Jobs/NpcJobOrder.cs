using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Jobs;

/// <summary>What one job is: who it is for, what it is called, where it may
/// happen, and the two numbers and two words that shape it.
///
/// <b>Public because a role cannot start a job without constructing one.</b>
/// This is the whole of what a role says about a job before the pipeline takes
/// over - everything else the driver works out. It is the analogue of
/// <c>NpcWorkerPrefabOptions</c> one tier up, and it is a struct with a public
/// constructor for the same reason: a caller has to be able to make one, and
/// there is nothing in it a caller could forge permission with.
///
/// <b>Defaults fail closed.</b> A <c>default(NpcJobOrder)</c> carries no
/// identity, no job name, no area and no vocabulary, and
/// <see cref="NpcJobDriver"/> refuses it rather than planning against
/// anywhere.</summary>
public readonly struct NpcJobOrder
{
    /// <summary>How many rounds one job may take before it stops and says so.
    ///
    /// <b>A pacing number, not a correctness one, and it is a guess.</b> Nobody
    /// can know the right value without a role standing in a real base, so it is
    /// chosen to be of the same order as the trips one plan writes out: a job
    /// that has re-planned eight times and still has work is a job a player
    /// should be told about rather than one an NPC should keep grinding at. It
    /// is never written anywhere durable, so changing it later costs
    /// nothing.</summary>
    public const int DefaultRounds = 8;

    /// <summary>Builds an order.</summary>
    /// <param name="identity">Which NPC the job is for.</param>
    /// <param name="jobId">What the job is called. Half of every reservation
    /// name the plan takes out, so it must be unique to this job and stable for
    /// its life - two drivers sharing one job name release each other's
    /// holds.</param>
    /// <param name="area">Where work is allowed. Null is refused, never widened
    /// to anywhere.</param>
    /// <param name="world">The world load, as
    /// <c>NpcRoleRegistry.BeginWorldLoad</c> handed it over. A role must not
    /// mint its own.</param>
    /// <param name="capacity">How much goes in one trip, in the role's own
    /// units.</param>
    /// <param name="actions">The role's two words for the two kinds of step the
    /// pipeline writes.</param>
    /// <param name="ceiling">The most this job may spend, in total. Empty means
    /// no ceiling; a job that then turns out to need more than the role said it
    /// was for is refused rather than quietly spending a player's
    /// material.</param>
    /// <param name="rounds">How many rounds the job may take. See
    /// <see cref="DefaultRounds"/>.</param>
    /// <param name="allowance">What one round may spend looking and planning.
    /// A bound rather than no limit, because "no limit" is how a pathological
    /// world becomes a frame the player feels.</param>
    public NpcJobOrder(
        NpcIdentity identity,
        string jobId,
        INpcWorkArea? area,
        NpcWorldEpoch world,
        NpcCarryCapacity capacity,
        JobStepActions actions,
        JobManifest ceiling = default,
        int rounds = DefaultRounds,
        int allowance = TourJobPlanner.DefaultAllowance)
    {
        Identity = identity;
        JobId = jobId ?? string.Empty;
        Area = area;
        World = world;
        Capacity = capacity;
        Actions = actions;
        Ceiling = ceiling;
        Rounds = rounds < 1 ? 1 : rounds;
        Allowance = allowance < 0 ? 0 : allowance;
    }

    /// <summary>Which NPC the job is for.</summary>
    public NpcIdentity Identity { get; }

    /// <summary>What the job is called.</summary>
    public string JobId { get; }

    /// <summary>Where work is allowed.</summary>
    public INpcWorkArea? Area { get; }

    /// <summary>The world load this job belongs to.</summary>
    public NpcWorldEpoch World { get; }

    /// <summary>How much goes in one trip.</summary>
    public NpcCarryCapacity Capacity { get; }

    /// <summary>The role's two words.</summary>
    public JobStepActions Actions { get; }

    /// <summary>The most this job may spend in total, or empty for no
    /// ceiling.</summary>
    public JobManifest Ceiling { get; }

    /// <summary>How many rounds the job may take.</summary>
    public int Rounds { get; }

    /// <summary>What one round may spend looking and planning.</summary>
    public int Allowance { get; }

    /// <summary>Whether this order can be worked at all: an identity, a name
    /// whose reservations can be spelled, an area, a world, and both
    /// words.</summary>
    public bool IsValid =>
        !Identity.IsEmpty && JobId.Length != 0 && Area != null && !World.IsUnknown && Actions.IsValid;
}
