using System.Globalization;

namespace TheConcernedCat.Settlement.Designations;

/// <summary>What happened to a marking request.
///
/// <see cref="Refused"/> is zero, so a result that was never filled in is a
/// refusal. Every other arrangement means a dropped code path grants
/// something.</summary>
internal enum DesignationOutcome
{
    /// <summary>Nothing was marked, and <see cref="DesignationResult.Refusal"/>
    /// says why.</summary>
    Refused = 0,

    /// <summary>Newly marked.</summary>
    Designated = 1,

    /// <summary>Exactly this designation already existed. Nothing changed and
    /// nothing is wrong — this is what marking the same thing twice looks
    /// like.</summary>
    AlreadyDesignated = 2,
}

/// <summary>Why a marking request was refused.
///
/// Every value is a sentence a player can be shown, for the same reason
/// <c>WorkerDeferralReason</c> is: a refusal without a reason is
/// indistinguishable from a bug.</summary>
internal enum DesignationRefusal
{
    /// <summary>Refused with no reason recorded. This is itself a defect — it
    /// means a code path refused without saying why — and it is named so that
    /// it shows up as one instead of hiding behind a plausible reason.</summary>
    Unspecified = 0,

    /// <summary>The runtime may not act here at all: switched off, not the
    /// host, or no world.</summary>
    NotAuthorised = 1,

    /// <summary>The ward check could not be made. Refusing is the point:
    /// CF-SET-003's rule is that an unmakeable check refuses rather than
    /// assuming, and marking ground is the first place it bites.</summary>
    WardCheckUnavailable = 2,

    /// <summary>Somebody else's ward covers this ground. Refused now, at
    /// designation time, rather than at build time when a worker has already
    /// walked there.</summary>
    WardDenied = 3,

    /// <summary>Smaller or larger than a first-proof settlement is allowed to
    /// be.</summary>
    RadiusOutOfRange = 4,

    /// <summary>A different designation of this kind already exists. Not
    /// replaced: replacing it silently would be a designation the player did
    /// not make.</summary>
    AlreadyDesignatedDifferently = 5,

    /// <summary>There is no settlement area yet, and a harvest area or a
    /// supply container has to belong to one.</summary>
    NoSettlementArea = 6,

    /// <summary>The chest is outside the settlement area. The supply container
    /// is the settlement's own store, so it lives in the settlement.</summary>
    ContainerOutsideSettlement = 7,

    /// <summary>The adapter could not identify the container. Without a stable
    /// identity a designation would resolve by position, and follow whatever
    /// ends up standing there.</summary>
    ContainerNotIdentified = 8,

    /// <summary>The settlement's record could not be fully read, so nothing new
    /// is written over it.</summary>
    RecordReadOnly = 9,

    /// <summary>The request was not a valid kind — a default-constructed
    /// request reaching the book.</summary>
    InvalidRequest = 10,
}

/// <summary>The answer to one marking request, and why.</summary>
internal readonly struct DesignationResult
{
    private DesignationResult(
        DesignationOutcome outcome, DesignationRefusal refusal, Designation? designation)
    {
        Outcome = outcome;
        Refusal = refusal;
        Designation = designation;
    }

    public DesignationOutcome Outcome { get; }

    /// <summary><see cref="DesignationRefusal.Unspecified"/> unless
    /// <see cref="Outcome"/> is <see cref="DesignationOutcome.Refused"/>.</summary>
    public DesignationRefusal Refusal { get; }

    /// <summary>The designation that now exists, or the existing one that
    /// caused the refusal. Null when neither applies.</summary>
    public Designation? Designation { get; }

    public bool IsDesignated =>
        Outcome == DesignationOutcome.Designated || Outcome == DesignationOutcome.AlreadyDesignated;

    public static DesignationResult Designated(Designation designation)
    {
        return new DesignationResult(DesignationOutcome.Designated, DesignationRefusal.Unspecified, designation);
    }

    public static DesignationResult Already(Designation designation)
    {
        return new DesignationResult(
            DesignationOutcome.AlreadyDesignated, DesignationRefusal.Unspecified, designation);
    }

    public static DesignationResult Refused(DesignationRefusal refusal, Designation? existing = null)
    {
        return new DesignationResult(DesignationOutcome.Refused, refusal, existing);
    }

    /// <summary>One sentence for a player.</summary>
    public string Describe()
    {
        switch (Outcome)
        {
            case DesignationOutcome.Designated:
                return "Marked: " + Designation + ".";

            case DesignationOutcome.AlreadyDesignated:
                return "Already marked, exactly like that: " + Designation + ". Nothing changed.";

            default:
                return "Refused: " + DescribeRefusal() + ".";
        }
    }

    private string DescribeRefusal()
    {
        switch (Refusal)
        {
            case DesignationRefusal.NotAuthorised:
                return "the settlement runtime is not allowed to act here";

            case DesignationRefusal.WardCheckUnavailable:
                return "whether that ground belongs to somebody could not be checked, so it " +
                    "was not marked. All of it has to be loaded for the check to mean anything";

            case DesignationRefusal.WardDenied:
                return "a ward you do not have access to covers that ground";

            case DesignationRefusal.RadiusOutOfRange:
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "a first settlement is between {0:0.#} m and {1:0.#} m across from the middle",
                    DesignationBook.MinRadius, DesignationBook.MaxRadius);

            case DesignationRefusal.AlreadyDesignatedDifferently:
                return "there is already a different one — " + Designation +
                    ". Clear that first, so moving it is something you did on purpose";

            case DesignationRefusal.NoSettlementArea:
                return "there is no settlement area yet, and this belongs to one";

            case DesignationRefusal.ContainerOutsideSettlement:
                return "that chest is outside the settlement area";

            case DesignationRefusal.ContainerNotIdentified:
                return "that container could not be identified, and a designation that " +
                    "resolved by position would follow whatever ends up standing there";

            case DesignationRefusal.RecordReadOnly:
                return "this settlement's record could not be fully read, so nothing new is " +
                    "being written over it";

            case DesignationRefusal.InvalidRequest:
                return "that was not a designation this build understands";

            default:
                return "no reason was recorded, which is a bug — please report it";
        }
    }

    public override string ToString() => Describe();
}
