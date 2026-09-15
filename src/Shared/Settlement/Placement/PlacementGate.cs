using System;

namespace TheConcernedCat.Settlement.Placement;

/// <summary>The checks that stand between an order and a placed piece.
///
/// These are named for what they <i>mean</i>, not for vanilla's
/// <c>PlacementStatus</c> members, because the runtime reimplements them rather
/// than reading them — <c>Player.PlacePiece</c> performs none of them, and
/// <c>Player.TryPlacePiece</c>, which does, is bound to a <c>Player</c> and its
/// placement ghost and cannot be driven for somebody else.</summary>
internal enum PlacementCheck
{
    /// <summary>Somebody's ward. <c>PrivateArea.CheckAccess</c>.</summary>
    Ward = 0,

    /// <summary>A workbench or forge in range.
    /// <c>CraftingStation.HaveBuildStationInRange</c>.</summary>
    BuildStation = 1,

    /// <summary>A no-build zone.</summary>
    BuildZone = 2,

    /// <summary>Solid ground under the piece.</summary>
    Ground = 3,

    /// <summary>The biome the piece requires.</summary>
    Biome = 4,

    /// <summary>Room for it: nothing already there, no player in the way.</summary>
    Clearance = 5,

    /// <summary>A no-teleport area, for pieces that care.</summary>
    TeleportArea = 6,

    /// <summary>Inside or outside a dungeon, as the piece requires.</summary>
    DungeonRule = 7,

    /// <summary>What it must stand on: cultivated, dirt, snow, not-snow.</summary>
    GroundCover = 8,

    /// <summary>The real cost, held in reservation. Never taken from whatever
    /// happens to be in a nearby chest.</summary>
    Cost = 9,
}

/// <summary>What one check answered.
///
/// <see cref="Unavailable"/> is deliberately <b>zero</b>, so an uninitialised
/// or forgotten answer is "could not establish this" rather than "fine". The
/// whole leaf turns on that: #273's gate 4 requires every check that cannot be
/// faithfully reimplemented to become a refusal, and a default that meant
/// <see cref="Passed"/> would make the safe case the one you have to remember
/// to write.</summary>
internal enum CheckOutcome
{
    /// <summary>The runtime could not establish an answer. Refuses.</summary>
    Unavailable = 0,

    /// <summary>Allowed.</summary>
    Passed = 1,

    /// <summary>The game says no.</summary>
    Failed = 2,
}

/// <summary>Why a placement was refused.</summary>
internal enum PlacementRefusal
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>A check ran and said no. The ordinary case: a ward, a missing
    /// workbench, the wrong biome.</summary>
    Denied = 1,

    /// <summary>A check could not be made. This is the case the authority ADR
    /// is about — it is <b>not</b> the same as the check passing, and the
    /// difference is the whole of gate 4.</summary>
    CouldNotEstablish = 2,
}

/// <summary>The gate's answer.</summary>
internal readonly struct PlacementVerdict
{
    private PlacementVerdict(bool allowed, PlacementCheck check, PlacementRefusal refusal)
    {
        IsAllowed = allowed;
        Check = check;
        Refusal = refusal;
    }

    public bool IsAllowed { get; }

    /// <summary>The check that refused. Meaningless when
    /// <see cref="IsAllowed"/> is true.</summary>
    public PlacementCheck Check { get; }

    public PlacementRefusal Refusal { get; }

    public static PlacementVerdict Allow() =>
        new(allowed: true, default, PlacementRefusal.None);

    public static PlacementVerdict Refuse(PlacementCheck check, PlacementRefusal refusal) =>
        new(allowed: false, check, refusal);

    public override string ToString() =>
        IsAllowed ? "Allow" : $"Refuse({Check}, {Refusal})";
}

/// <summary>Collects one answer per check, then decides.
///
/// <b>The property this type exists to guarantee:</b> a placement is allowed
/// only when <i>every</i> check in <see cref="PlacementCheck"/> has been
/// answered <see cref="CheckOutcome.Passed"/>. There is no way to allow a
/// placement by not mentioning a check, because an unmentioned check is
/// <see cref="CheckOutcome.Unavailable"/> and refuses. Adding a new member to
/// <see cref="PlacementCheck"/> therefore makes every existing caller refuse
/// until it is taught to answer it — which is the correct direction for a
/// safety ladder to break in.
///
/// That is the difference between "we check these things" and "these things are
/// checked", and it is why the gate owns the required set rather than the
/// caller passing one in.</summary>
internal sealed class PlacementAnswers
{
    // The order refusals are reported in. Deliberately fixed and deliberately
    // not the enum's declaration order alone: cheap and categorical answers
    // come first, so a piece inside somebody's ward says "ward" rather than
    // "you also need a workbench", and the player is told the thing that
    // actually matters.
    private static readonly PlacementCheck[] ReportOrder =
    {
        PlacementCheck.Ward,
        PlacementCheck.BuildZone,
        PlacementCheck.DungeonRule,
        PlacementCheck.Biome,
        PlacementCheck.Ground,
        PlacementCheck.GroundCover,
        PlacementCheck.Clearance,
        PlacementCheck.TeleportArea,
        PlacementCheck.BuildStation,
        PlacementCheck.Cost,
    };

    private static readonly int CheckCount = Enum.GetValues(typeof(PlacementCheck)).Length;

    private readonly CheckOutcome[] _outcomes = new CheckOutcome[CheckCount];

    // Whether a check has been answered at all, kept separately from its
    // answer. Without it, "keep the worse of the two" would combine every first
    // answer with the Unavailable default and no check could ever be recorded
    // as passing -- which is safe, but uselessly so.
    private readonly bool[] _answered = new bool[CheckCount];

    /// <summary>Record one answer. The first answer for a check is taken as
    /// given; recording the same check <i>again</i> keeps the <b>worse</b> of
    /// the two, so a second opinion can only ever tighten a placement, never
    /// loosen one already refused.</summary>
    public PlacementAnswers Record(PlacementCheck check, CheckOutcome outcome)
    {
        int index = (int)check;
        if (index < 0 || index >= _outcomes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown placement check.");
        }

        _outcomes[index] = _answered[index] ? Worse(_outcomes[index], outcome) : outcome;
        _answered[index] = true;
        return this;
    }

    /// <summary>Convenience for a check whose answer is a plain boolean —
    /// something the runtime could ask and got a real answer to. A check that
    /// could not be asked must use <see cref="Record"/> with
    /// <see cref="CheckOutcome.Unavailable"/>, never <c>false</c>: "denied" and
    /// "unknown" are different refusals and a player deserves the real one.</summary>
    public PlacementAnswers Record(PlacementCheck check, bool passed)
    {
        return Record(check, passed ? CheckOutcome.Passed : CheckOutcome.Failed);
    }

    public CheckOutcome Outcome(PlacementCheck check) => _outcomes[(int)check];

    /// <summary>Allow only if every check passed; otherwise name the first
    /// refusal in report order.</summary>
    public PlacementVerdict Evaluate()
    {
        // Denials first, then unestablished checks. A placement blocked by a
        // ward AND missing a workbench should say "ward" — but a placement that
        // is definitely denied should never be reported as merely unverifiable,
        // which would read as a bug the player could retry past.
        foreach (PlacementCheck check in ReportOrder)
        {
            if (_outcomes[(int)check] == CheckOutcome.Failed)
            {
                return PlacementVerdict.Refuse(check, PlacementRefusal.Denied);
            }
        }

        foreach (PlacementCheck check in ReportOrder)
        {
            if (_outcomes[(int)check] != CheckOutcome.Passed)
            {
                return PlacementVerdict.Refuse(check, PlacementRefusal.CouldNotEstablish);
            }
        }

        return PlacementVerdict.Allow();
    }

    /// <summary>Ordering on how bad an answer is, so
    /// <see cref="Record(PlacementCheck, CheckOutcome)"/> can keep the worse of
    /// two. Failed beats Unavailable beats Passed: a check that definitely says
    /// no outranks one that could not be made, which outranks one that
    /// allowed.</summary>
    private static CheckOutcome Worse(CheckOutcome left, CheckOutcome right)
    {
        if (left == CheckOutcome.Failed || right == CheckOutcome.Failed)
        {
            return CheckOutcome.Failed;
        }

        if (left == CheckOutcome.Unavailable || right == CheckOutcome.Unavailable)
        {
            return CheckOutcome.Unavailable;
        }

        return CheckOutcome.Passed;
    }
}
