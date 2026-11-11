using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The preview's colour: a press would dig, there is nothing to dig, or it is refused (#551).</summary>
internal enum DigTone
{
    Ready = 0,
    Idle = 1,
    Refused = 2,
    Water = 3,
}

/// <summary>What the shovel's preview shows now (#551), and the rule a dig press must pass to use it: the outline has been
/// visible for <see cref="ExcavationPreviewRules.ConfirmDwellSeconds"/>, was drawn in the latest frames, and a fresh read
/// at the press still gives the plan that was shown as ready. Game-free so the rule is tested.</summary>
internal sealed class DigPreviewState
{
    internal bool Visible { get; private set; }

    /// <summary>True when the aim found ground and a footprint was evaluated.</summary>
    internal bool HasTarget { get; private set; }

    /// <summary>The size chosen when this was shown (#574).</summary>
    internal DigSize Size { get; private set; }

    internal DigFootprint Footprint { get; private set; }

    internal DigPlan Plan { get; private set; }

    /// <summary>True when <see cref="Plan"/> was planned (the terrain was read without fault).</summary>
    internal bool Planned { get; private set; }

    internal ExcavationCommitRefusal Refusal { get; private set; }

    internal ExcavationAim Aim { get; private set; }

    /// <summary>The one line the HUD shows.</summary>
    internal string Line { get; private set; } = string.Empty;

    /// <summary>Changes whenever what is shown changes, so the HUD redraws only then.</summary>
    internal int Version { get; private set; }

    internal float ShownSince { get; private set; } = float.NaN;

    internal int LastFrame { get; private set; } = int.MinValue;

    internal void Show(DigSize size, DigFootprint footprint, bool planned, in DigPlan plan, ExcavationCommitRefusal refusal,
        string line, int frame, float now)
    {
        bool same = Visible && HasTarget && Size.Equals(size) && Footprint.Equals(footprint) && Planned == planned &&
                    Refusal == refusal && SamePlan(Plan, plan) && Line == line;
        Visible = true;
        HasTarget = true;
        Aim = ExcavationAim.Ok;
        LastFrame = frame;
        if (same) return;
        Size = size;
        Footprint = footprint;
        Planned = planned;
        Plan = plan;
        Refusal = refusal;
        Line = line;
        ShownSince = now;
        Version++;
    }

    internal void ShowNoTarget(DigSize size, ExcavationAim aim, string line, int frame, float now)
    {
        bool same = Visible && !HasTarget && Size.Equals(size) && Aim == aim && Line == line;
        Visible = true;
        LastFrame = frame;
        if (same) return;
        HasTarget = false;
        Planned = false;
        Plan = default;
        Size = size;
        Aim = aim;
        Refusal = ExcavationCommitRefusal.NotAimed;
        Line = line;
        ShownSince = now;
        Version++;
    }

    /// <summary>Shows a line without changing the outline (a press's result, an armed undo).</summary>
    internal void Say(string line)
    {
        if (Line == line) return;
        Line = line;
        Version++;
    }

    /// <summary>Drawn again this frame, unchanged.</summary>
    internal void Touch(int frame) => LastFrame = frame;

    internal void Hide()
    {
        if (!Visible) return;
        Visible = false;
        HasTarget = false;
        ShownSince = float.NaN;
        Version++;
    }

    /// <summary>What was shown is spent (a dig went ahead, or the plan moved): the next refresh starts the dwell again.</summary>
    internal void Invalidate()
    {
        ShownSince = float.NaN;
        Version++;
    }

    /// <summary>Whether a dig press may use what is shown.</summary>
    internal ExcavationCommitRefusal Confirm(int frame, float now)
    {
        if (!Visible || !HasTarget || !Planned) return ExcavationCommitRefusal.NoPreview;
        if (frame - LastFrame > ExcavationPreviewRules.MaximumFrameAge || frame < LastFrame) return ExcavationCommitRefusal.NoPreview;
        if (!LadderRules.Finite(ShownSince) || !(now - ShownSince >= ExcavationPreviewRules.ConfirmDwellSeconds)) return ExcavationCommitRefusal.NoPreview;
        return ExcavationCommitRefusal.None;
    }

    /// <summary>A fresh plan at the press is the one shown as ready: the same rectangle and depth, the same floor, the
    /// same vertices to lower.</summary>
    internal bool Matches(in DigPlan fresh) =>
        Planned && Plan.Status == ExcavationStatus.Ready && fresh.Status == ExcavationStatus.Ready && SamePlan(Plan, fresh);

    private static bool SamePlan(in DigPlan a, in DigPlan b) =>
        a.Footprint.Equals(b.Footprint) && a.Status == b.Status && a.Refusal == b.Refusal && a.LoweredVertices == b.LoweredVertices &&
        (a.Status == ExcavationStatus.Refused || Math.Abs(a.Bottom - b.Bottom) <= ExcavationPlanner.LevelTolerance);
}

/// <summary>The preview's outlines (#551), flat rather than draped over the ground (owner playtest: the old wireframe bent
/// over uneven ground and was hard to read), drawn as a perfect box since #567 (owner: "show them as a perfect cube"): the
/// hole's footprint at the datum, the floor at its own height, four vertical edges between them, and the water surface
/// when the floor would flood. The ground slopes over one more cell outside the box (the engine's limit); a basement wall
/// piece covers it. Before the terrain has been read only the footprint is drawn, at the
/// aimed height. Fixed buffers, so it is refilled every refresh without allocating.</summary>
internal sealed class DigPreviewShape
{
    internal const int Top = 0;
    internal const int Floor = 1;
    internal const int FirstWall = 2;
    internal const int Water = 6;
    internal const int LineCount = 7;

    /// <summary>The most points in a line: a closed rectangle's four corners (drawn as a loop).</summary>
    internal const int MaximumPoints = 4;

    private readonly float[,] _x = new float[LineCount, MaximumPoints];
    private readonly float[,] _y = new float[LineCount, MaximumPoints];
    private readonly float[,] _z = new float[LineCount, MaximumPoints];
    private readonly int[] _count = new int[LineCount];

    internal int Count(int line) => _count[line];

    internal float X(int line, int point) => _x[line, point];

    internal float Y(int line, int point) => _y[line, point];

    internal float Z(int line, int point) => _z[line, point];

    internal bool Loop(int line) => line == Top || line == Floor || line == Water;

    internal void Clear() => Array.Clear(_count, 0, _count.Length);

    /// <summary>The outlines of a plan with a floor (Ready, nothing to dig, or refused after the datum was read).</summary>
    internal void Build(in DigPlan plan, float? waterLevel)
    {
        Clear();
        DigFootprint footprint = plan.Footprint;
        float lift = ExcavationPreviewRules.LineLift;
        if (!LadderRules.Finite(plan.Datum))
        {
            return;
        }

        float top = plan.Datum + lift;
        float floor = plan.Bottom + lift;
        float s = ExcavationGeometry.VertexSpacing;
        Rectangle(Top, footprint.MinX * s, footprint.MinZ * s, footprint.MaxX * s, footprint.MaxZ * s, top);
        Rectangle(Floor, footprint.MinX * s, footprint.MinZ * s, footprint.MaxX * s, footprint.MaxZ * s, floor);
        for (int corner = 0; corner < 4; corner++)
        {
            bool east = corner == 1 || corner == 2, north = corner >= 2;
            float x = (east ? footprint.MaxX : footprint.MinX) * s, z = (north ? footprint.MaxZ : footprint.MinZ) * s;
            Segment(FirstWall + corner, x, top, z, x, floor, z);
        }

        if (plan.Floods && waterLevel.HasValue && LadderRules.Finite(waterLevel.Value))
        {
            Rectangle(Water, footprint.MinX * s, footprint.MinZ * s, footprint.MaxX * s, footprint.MaxZ * s, waterLevel.Value + lift);
        }
    }

    /// <summary>Only the footprint, flat at <paramref name="groundY"/>: before the terrain has been read, or when it could
    /// not be.</summary>
    internal void BuildFootprint(DigFootprint footprint, float groundY)
    {
        Clear();
        if (!LadderRules.Finite(groundY)) return;
        float s = ExcavationGeometry.VertexSpacing;
        Rectangle(Floor, footprint.MinX * s, footprint.MinZ * s, footprint.MaxX * s, footprint.MaxZ * s, groundY + ExcavationPreviewRules.LineLift);
    }

    internal static DigTone ToneFor(ExcavationCommitRefusal refusal) => refusal switch
    {
        ExcavationCommitRefusal.None => DigTone.Ready,
        ExcavationCommitRefusal.NothingToDig => DigTone.Idle,
        _ => DigTone.Refused,
    };

    private void Rectangle(int line, float minX, float minZ, float maxX, float maxZ, float y)
    {
        Set(line, 0, minX, y, minZ);
        Set(line, 1, maxX, y, minZ);
        Set(line, 2, maxX, y, maxZ);
        Set(line, 3, minX, y, maxZ);
        _count[line] = 4;
    }

    private void Segment(int line, float ax, float ay, float az, float bx, float by, float bz)
    {
        Set(line, 0, ax, ay, az);
        Set(line, 1, bx, by, bz);
        _count[line] = 2;
    }

    private void Set(int line, int point, float x, float y, float z)
    {
        _x[line, point] = x;
        _y[line, point] = y;
        _z[line, point] = z;
    }
}
