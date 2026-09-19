using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>What a world holds of one identity's bodies, and what that permits.
///
/// <b>One census where there were three, and the merge is not cosmetic.</b>
/// Concerned Foreman counted saved objects to completion in one call and
/// reported a group plus a count of bodies carrying no identity at all.
/// Concerned Teamster walked the same index incrementally across frames and
/// turned the counts into a status ladder that refuses to spawn while the walk
/// is unfinished. The Steward had neither. Each half fixes a real failure the
/// other would still have: without the incremental walk a large world stalls a
/// frame, and without the ladder a half-finished walk reads as "nobody here"
/// and builds a second body for an identity that already has one.
///
/// So the counting and the reporting are Foreman's - saved bodies grouped by
/// identity, bodies with no identity counted and never adopted, nothing ever
/// destroyed to tidy a duplicate - and the ladder and its refusal are
/// Teamster's. This type is the ladder; <c>NpcWorldBodies</c> is the walk.
///
/// <b>The one ordering that looks wrong and is not.</b> A loaded body is
/// reported <see cref="NpcBodyPresence.Present"/> even while the scan is
/// unfinished, because a body standing in front of the player is not in doubt
/// and waiting would leave it unbound for no reason. The scan may later find a
/// second saved body and flip the answer to
/// <see cref="NpcBodyPresence.Duplicated"/>; that is a change of report, and it
/// costs nothing, because the only irreversible act - building a new body -
/// needs <see cref="NpcBodyPresence.Missing"/>, which an unfinished scan can
/// never produce.</summary>
public readonly struct NpcBodyTally
{
    private NpcBodyTally(
        NpcIdentity identity,
        bool scanComplete,
        int savedBodies,
        int loadedBodies,
        int unidentified,
        bool loadedIsFaulted)
    {
        Identity = identity;
        ScanComplete = scanComplete;
        SavedBodies = savedBodies < 0 ? 0 : savedBodies;
        LoadedBodies = loadedBodies < 0 ? 0 : loadedBodies;
        Unidentified = unidentified < 0 ? 0 : unidentified;
        LoadedIsFaulted = loadedIsFaulted;
    }

    /// <summary>Whose bodies were counted.</summary>
    public NpcIdentity Identity { get; }

    /// <summary>The walk over the world's saved objects finished.</summary>
    public bool ScanComplete { get; }

    /// <summary>Distinct saved objects carrying this identity, loaded or not.
    /// </summary>
    public int SavedBodies { get; }

    /// <summary>Of those, the ones loaded in this scene now.</summary>
    public int LoadedBodies { get; }

    /// <summary>Bodies of this role's prefab carrying no identity at all -
    /// built by a version that did not stamp one, or stamped and then failed.
    /// Reported so a player can be told; never adopted, never destroyed, and
    /// never counted as this identity's.</summary>
    public int Unidentified { get; }

    /// <summary>The single loaded body's mind has latched a fault.</summary>
    public bool LoadedIsFaulted { get; }

    public NpcBodyPresence Presence
    {
        get
        {
            if (SavedBodies >= 2 || LoadedBodies >= 2)
            {
                return NpcBodyPresence.Duplicated;
            }

            if (LoadedBodies == 1)
            {
                return LoadedIsFaulted ? NpcBodyPresence.Faulted : NpcBodyPresence.Present;
            }

            if (!ScanComplete)
            {
                return NpcBodyPresence.Searching;
            }

            return SavedBodies == 1 ? NpcBodyPresence.NotLoaded : NpcBodyPresence.Missing;
        }
    }

    /// <summary>A body may be built only when the scan finished and found none.
    /// Every other answer, including the unfinished one, is a refusal.</summary>
    public bool MaySpawn => Presence == NpcBodyPresence.Missing;

    /// <summary>More than one body carries this identity. Stated separately
    /// from <see cref="Presence"/> because a runtime has to stop working with a
    /// duplicated identity whatever else is true of it.</summary>
    public bool IsDuplicated => Presence == NpcBodyPresence.Duplicated;

    /// <summary><b>Both ways of making one are internal, and that is the point
    /// of making the type public.</b> A role has to be able to read a tally -
    /// it decides what the player is told, and it is what the factory takes -
    /// but a role that could <i>construct</i> one could hand the factory a
    /// tally saying <see cref="NpcBodyPresence.Missing"/> and build a second
    /// body for an identity that already has one. The only way a consumer gets
    /// a tally is <c>NpcWorldBodies.TallyFor</c>, which has counted. A defaulted
    /// value reports <see cref="NpcBodyPresence.Searching"/>, which permits
    /// nothing, so even the one construction the language always allows fails
    /// closed.</summary>
    internal static NpcBodyTally Of(
        NpcIdentity identity,
        bool scanComplete,
        int savedBodies,
        int loadedBodies,
        int unidentified,
        bool loadedIsFaulted) =>
        new NpcBodyTally(identity, scanComplete, savedBodies, loadedBodies, unidentified, loadedIsFaulted);

    /// <summary>The census before it has read anything. Reports
    /// <see cref="NpcBodyPresence.Searching"/>, which permits nothing - so a
    /// runtime that forgets to run the walk refuses to build rather than
    /// building a duplicate.</summary>
    internal static NpcBodyTally NotYetRun(NpcIdentity identity) =>
        new NpcBodyTally(identity, false, 0, 0, 0, false);

    public override string ToString() =>
        Identity + " " + Presence + " (saved " + SavedBodies + ", loaded " + LoadedBodies
        + ", unidentified " + Unidentified + ")";
}
