using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What one press of the held Iron Shovel asks for (#487 slice 4).</summary>
internal enum ExcavationPress
{
    /// <summary>Nothing: neither button, or both in the same input step (ambiguous).</summary>
    None = 0,

    /// <summary>The game's Attack binding: dig the size the preview shows, where it shows it.</summary>
    Dig = 1,

    /// <summary>The game's Secondary Attack binding: switch the preview between the two sizes. Never digs.</summary>
    SwitchSize = 2,
}

/// <summary>Why the aim found no target (#487 slice 4). The targeting rule is the game's own build-placement rule: the
/// camera's ray against the placement layers, the first thing it hits must be terrain, and that point must be within
/// <see cref="ExcavationLimits.Reach"/> of the player's eyes.</summary>
internal enum ExcavationAim
{
    Ok = 0,

    /// <summary>No local player or camera.</summary>
    NoPlayer = 1,

    /// <summary>The ray met nothing at all within its length.</summary>
    NoGround = 2,

    /// <summary>The first thing the ray met is not terrain (a wall, a floor, a rock): the shovel never digs through
    /// something it cannot reach.</summary>
    Blocked = 3,

    /// <summary>The ground looked at is farther than <see cref="ExcavationLimits.Reach"/> from the player's eyes.</summary>
    TooFar = 4,
}

/// <summary>The local, in-memory size the held shovel previews and digs. It starts small every session and is never
/// saved or sent (a lead default pending owner confirmation, EXCAVATION.md section 8.6).</summary>
internal sealed class ExcavationSelection
{
    internal ExcavationMode Selected { get; private set; } = ExcavationMode.Small;

    internal void Switch() => Selected = Selected == ExcavationMode.Small ? ExcavationMode.Large : ExcavationMode.Small;

    /// <summary>The size the next switch would choose, for the key hint.</summary>
    internal ExcavationMode Other => Selected == ExcavationMode.Small ? ExcavationMode.Large : ExcavationMode.Small;
}

/// <summary>The rules the preview follows (#487 slice 4), game-free and tested.</summary>
internal static class ExcavationPreviewRules
{
    /// <summary>Re-reads the terrain at least this often while the target stays put, so a change in stamina, terrain or
    /// objects reaches the preview.</summary>
    internal const float SteadyRefreshSeconds = 0.25f;

    /// <summary>Re-reads the terrain no more often than this, however fast the aim moves: at most 20 reads of one square
    /// (49 vertices at most) per second, whatever the frame rate.</summary>
    internal const float MinimumRefreshSeconds = 0.05f;

    /// <summary>How long the preview must have been continuously visible before a press may dig. Closing the inventory, a
    /// menu or the map while the Attack button is held, or equipping the shovel and pressing at once, gives the game's
    /// attack edge in the first input step; the preview was hidden then, so that press digs nothing.</summary>
    internal const float ConfirmDwellSeconds = 0.2f;

    /// <summary>The preview must have been drawn in one of this many latest frames for a press to use it.</summary>
    internal const int MaximumFrameAge = 2;

    /// <summary>How far above the terrain the outlines are drawn, metres.</summary>
    internal const float LineLift = 0.05f;

    /// <summary>One press of the game's own edges (true for one input step per press) asks for one thing. Both at once is
    /// ambiguous and asks for nothing.</summary>
    internal static ExcavationPress PressFor(bool primaryPressed, bool secondaryPressed)
    {
        if (primaryPressed == secondaryPressed) return ExcavationPress.None;
        return primaryPressed ? ExcavationPress.Dig : ExcavationPress.SwitchSize;
    }

    /// <summary>Whether the preview re-reads the terrain this frame. A target that moved waits out the minimum interval; a
    /// steady one is re-read on the steady interval. An unknown interval (no read yet, or a clock that is not finite)
    /// reads.</summary>
    internal static bool ShouldRefresh(bool targetChanged, float secondsSinceLastRead)
    {
        if (!LadderRules.Finite(secondsSinceLastRead) || secondsSinceLastRead < 0f) return true;
        return secondsSinceLastRead >= (targetChanged ? MinimumRefreshSeconds : SteadyRefreshSeconds);
    }
}

/// <summary>What the preview last showed (#487 slice 4), and the confirmation rule a press must pass. A press digs only
/// the size and the square the preview shows, only after the preview has been visible long enough and was drawn in the
/// latest frames, and only when a fresh read of the terrain still gives the plan that was shown. Nothing here reads or
/// changes the world.</summary>
internal sealed class ExcavationPreviewState
{
    private ExcavationPlan _plan;

    /// <summary>The preview is showing (the shovel is held, no menu or inventory is open).</summary>
    internal bool Visible { get; private set; }

    /// <summary>Real time at which the preview last became visible.</summary>
    internal float VisibleSince { get; private set; } = float.NaN;

    /// <summary>Real time since which what is shown has not changed: the same size, the same aim result, the same square,
    /// the same gate answer and the same floor (independent review of slice 4, S1). The press's dwell counts from here, so
    /// a square swept onto, a size just switched, a red outline just turned green or a floor just moved is never dug in the
    /// step after it first appears.</summary>
    internal float SteadySince { get; private set; } = float.NaN;

    /// <summary>The last frame in which the preview was drawn.</summary>
    internal int Frame { get; private set; } = int.MinValue;

    /// <summary>Changes whenever what is shown changes, so the HUD text is rebuilt only then.</summary>
    internal int Version { get; private set; }

    internal ExcavationMode Mode { get; private set; }

    internal ExcavationAim Aim { get; private set; } = ExcavationAim.NoPlayer;

    /// <summary>What the aim ray hit first when it was not terrain.</summary>
    internal string? Blocker { get; private set; }

    internal int CentreX { get; private set; }

    internal int CentreZ { get; private set; }

    /// <summary>The gate's answer for the shown square, without committing anything. None means the press would dig.</summary>
    internal ExcavationCommitRefusal Refusal { get; private set; } = ExcavationCommitRefusal.NoPreview;

    internal ExcavationReadFault Fault { get; private set; }

    internal string? Obstruction { get; private set; }

    /// <summary>How many building-piece colliders stand on ground the shown plan would change.</summary>
    internal int Pieces { get; private set; }

    internal ExcavationPlan Plan => _plan;

    /// <summary>The aim found a square (whatever the gate then says about it).</summary>
    internal bool HasTarget => Visible && Aim == ExcavationAim.Ok;

    /// <summary>The preview is no longer shown: the shovel was put away, a menu opened, or the player died. The next time it
    /// shows, the dwell starts again.</summary>
    internal void Hide()
    {
        if (!Visible) return;
        Visible = false;
        VisibleSince = float.NaN;
        SteadySince = float.NaN;
        Version++;
    }

    /// <summary>The aim found no square.</summary>
    internal void ShowNoTarget(ExcavationMode mode, ExcavationAim aim, string? blocker, int frame, float now)
    {
        ExcavationAim shownAim = aim == ExcavationAim.Ok ? ExcavationAim.NoGround : aim;
        bool same = Visible && Mode == mode && Aim == shownAim && Refusal == ExcavationCommitRefusal.NotAimed;
        Begin(frame, now, same);
        Mode = mode;
        Aim = shownAim;
        Blocker = blocker;
        Refusal = ExcavationCommitRefusal.NotAimed;
        Fault = ExcavationReadFault.None;
        Obstruction = null;
        Pieces = 0;
        _plan = default;
        Version++;
    }

    /// <summary>A square was evaluated: the gate's answer, the plan and what the scan found.</summary>
    internal void ShowTarget(ExcavationMode mode, int centreX, int centreZ, ExcavationCommitRefusal refusal, ExcavationReadFault fault,
        in ExcavationPlan plan, string? obstruction, int pieces, int frame, float now)
    {
        bool same = Visible && Mode == mode && Aim == ExcavationAim.Ok && CentreX == centreX && CentreZ == centreZ && Refusal == refusal &&
                    plan.Status == _plan.Status && SameFloor(plan.Bottom, _plan.Bottom);
        Begin(frame, now, same);
        Mode = mode;
        Aim = ExcavationAim.Ok;
        Blocker = null;
        CentreX = centreX;
        CentreZ = centreZ;
        Refusal = refusal;
        Fault = fault;
        Obstruction = obstruction;
        Pieces = pieces;
        _plan = plan;
        Version++;
    }

    /// <summary>Drawn again this frame without a new read.</summary>
    internal void Touch(int frame) => Frame = frame;

    /// <summary>Whether a press may use what is shown: shown unchanged for the whole dwell, drawn in the latest frames, and of
    /// the size that is selected now. Returns <see cref="ExcavationCommitRefusal.NoPreview"/> otherwise.</summary>
    internal ExcavationCommitRefusal Confirm(ExcavationMode selected, int frame, float now)
    {
        if (!Visible || !LadderRules.Finite(now) || !LadderRules.Finite(SteadySince)) return ExcavationCommitRefusal.NoPreview;
        if (frame - Frame > ExcavationPreviewRules.MaximumFrameAge || frame < Frame) return ExcavationCommitRefusal.NoPreview;
        if (now - SteadySince < ExcavationPreviewRules.ConfirmDwellSeconds) return ExcavationCommitRefusal.NoPreview;
        if (Mode != selected) return ExcavationCommitRefusal.NoPreview;
        return ExcavationCommitRefusal.None;
    }

    /// <summary>A fresh plan, read at the press, is the one that was shown as ready: the same size and square, the shown
    /// gate answer was None, and the floor is at the same height. Anything else is a change the player has not seen.</summary>
    internal bool Matches(in ExcavationPlan fresh)
    {
        if (!HasTarget || Refusal != ExcavationCommitRefusal.None) return false;
        if (fresh.Status != ExcavationStatus.Ready || _plan.Status != ExcavationStatus.Ready) return false;
        if (fresh.Mode != Mode || fresh.CentreX != CentreX || fresh.CentreZ != CentreZ) return false;
        if (!LadderRules.Finite(fresh.Bottom) || !LadderRules.Finite(_plan.Bottom)) return false;
        return SameFloor(fresh.Bottom, _plan.Bottom);
    }

    /// <summary>Two floors within the planner's level tolerance, or both without a floor.</summary>
    private static bool SameFloor(float a, float b)
    {
        bool finiteA = LadderRules.Finite(a);
        bool finiteB = LadderRules.Finite(b);
        if (!finiteA || !finiteB) return finiteA == finiteB;
        return Math.Abs(a - b) <= ExcavationPlanner.LevelTolerance;
    }

    /// <summary>After a refused match the shown answer is no longer trusted; the next frame shows a fresh one.</summary>
    internal void Invalidate()
    {
        if (Refusal == ExcavationCommitRefusal.None) Refusal = ExcavationCommitRefusal.PreviewChanged;
        SteadySince = float.NaN;
        Version++;
    }

    private void Begin(int frame, float now, bool unchanged)
    {
        if (!Visible)
        {
            Visible = true;
            VisibleSince = now;
        }
        if (!unchanged || !LadderRules.Finite(SteadySince)) SteadySince = now;
        Frame = frame;
    }
}

/// <summary>How a preview line is coloured.</summary>
internal enum ExcavationPreviewTone
{
    /// <summary>A press would dig.</summary>
    Ready = 0,

    /// <summary>Nothing to dig, or waiting on the last dig: a press changes and charges nothing.</summary>
    Idle = 1,

    /// <summary>A press would be refused.</summary>
    Refused = 2,

    /// <summary>The water surface the floor would sit below.</summary>
    Water = 3,
}

/// <summary>The outlines the preview draws for one shown plan (#487 slice 4), in world metres, game-free. A fixed number of
/// lines with fixed capacities, filled in place: the preview never allocates per frame and never grows.
/// <list type="bullet">
/// <item><b>Border</b>: the untouched ring of vertices one cell outside the floor, through each vertex's sampled height.
/// The ring is never changed and the lattice line between two ring vertices is a triangle edge, so this line lies on the
/// terrain surface (and collider) exactly where the change stops: everything inside it may change, nothing on or
/// outside it does.</item>
/// <item><b>Floor</b>: the usable interior, a square at the one planned floor height.</item>
/// <item><b>Corners</b>: at each floor corner, a post from the datum down to the floor (the depth), then the sloping
/// transition out to the ring corner on the surface.</item>
/// <item><b>Water</b>: the border square at the water surface, only when the floor would sit below it.</item>
/// </list>
/// A refused plan with no floor (unloaded or unreadable terrain) draws nothing; the HUD says why.</summary>
internal sealed class ExcavationPreviewShape
{
    internal const int Border = 0;
    internal const int Floor = 1;
    internal const int FirstCorner = 2;
    internal const int Water = 6;

    /// <summary>Every line the preview can draw. The adapter creates exactly this many line renderers, once.</summary>
    internal const int LineCount = 7;

    /// <summary>The largest border: the large mode's ring, 8 x 3 vertices.</summary>
    internal const int MaximumPoints = 8 * 3;

    private readonly float[][] _points = new float[LineCount][];
    private readonly int[] _counts = new int[LineCount];
    private readonly bool[] _loops = new bool[LineCount];

    internal ExcavationPreviewShape()
    {
        for (int i = 0; i < LineCount; i++) _points[i] = new float[MaximumPoints * 3];
    }

    /// <summary>How many points line <paramref name="line"/> has now; zero means it is not drawn.</summary>
    internal int Count(int line) => _counts[line];

    /// <summary>Whether the line closes on itself.</summary>
    internal bool Loop(int line) => _loops[line];

    internal float X(int line, int point) => _points[line][point * 3];

    internal float Y(int line, int point) => _points[line][point * 3 + 1];

    internal float Z(int line, int point) => _points[line][point * 3 + 2];

    /// <summary>The colour the outline takes for a gate answer.</summary>
    internal static ExcavationPreviewTone ToneFor(ExcavationCommitRefusal refusal) => refusal switch
    {
        ExcavationCommitRefusal.None => ExcavationPreviewTone.Ready,
        ExcavationCommitRefusal.NothingToDig => ExcavationPreviewTone.Idle,
        ExcavationCommitRefusal.Busy => ExcavationPreviewTone.Idle,
        _ => ExcavationPreviewTone.Refused,
    };

    internal void Clear() => Array.Clear(_counts, 0, _counts.Length);

    /// <summary>Fills the lines for <paramref name="plan"/> over <paramref name="samples"/> (the terrain it was planned
    /// from). Draws nothing when the plan has no finite floor or a ring vertex is unreadable.</summary>
    internal void Build(in ExcavationPlan plan, ExcavationSamples samples, float? waterLevel)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        Clear();
        if (samples.Geometry.Mode != plan.Mode) return;
        float datum = plan.Datum;
        float bottom = plan.Bottom;
        if (!LadderRules.Finite(datum) || !LadderRules.Finite(bottom)) return;
        int ring = plan.Geometry.RingDistance;
        int half = plan.Geometry.HalfExtent;
        float spacing = ExcavationGeometry.VertexSpacing;
        float lift = ExcavationPreviewRules.LineLift;

        // The ring, walked once around: from the (-ring, -ring) corner along +x, then +z, then -x, then -z.
        int border = 0;
        for (int side = 0; side < 4; side++)
        {
            for (int step = 0; step < 2 * ring; step++)
            {
                RingStep(side, step, ring, out int dx, out int dz);
                if (!samples.IsLoaded(dx, dz) || !LadderRules.Finite(samples.Height(dx, dz))) { Clear(); return; }
                Put(Border, border++, (plan.CentreX + dx) * spacing, samples.Height(dx, dz) + lift, (plan.CentreZ + dz) * spacing);
            }
        }
        _counts[Border] = border;
        _loops[Border] = true;

        // The floor square and the four corners, in the same order as the ring's corners.
        for (int corner = 0; corner < 4; corner++)
        {
            CornerSigns(corner, out int sx, out int sz);
            float fx = (plan.CentreX + sx * half) * spacing;
            float fz = (plan.CentreZ + sz * half) * spacing;
            Put(Floor, corner, fx, bottom + lift, fz);
            int line = FirstCorner + corner;
            Put(line, 0, fx, datum + lift, fz);
            Put(line, 1, fx, bottom + lift, fz);
            Put(line, 2, (plan.CentreX + sx * ring) * spacing, samples.Height(sx * ring, sz * ring) + lift, (plan.CentreZ + sz * ring) * spacing);
            _counts[line] = 3;
            _loops[line] = false;
        }
        _counts[Floor] = 4;
        _loops[Floor] = true;

        if (waterLevel.HasValue && LadderRules.Finite(waterLevel.Value) && waterLevel.Value > bottom)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                CornerSigns(corner, out int sx, out int sz);
                Put(Water, corner, (plan.CentreX + sx * ring) * spacing, waterLevel.Value + lift, (plan.CentreZ + sz * ring) * spacing);
            }
            _counts[Water] = 4;
            _loops[Water] = true;
        }
    }

    /// <summary>The ring vertex at <paramref name="step"/> along <paramref name="side"/> (each side has 2 x ring steps and
    /// starts at a corner), relative to the centre.</summary>
    internal static void RingStep(int side, int step, int ring, out int dx, out int dz)
    {
        switch (side)
        {
            case 0: dx = -ring + step; dz = -ring; break;
            case 1: dx = ring; dz = -ring + step; break;
            case 2: dx = ring - step; dz = ring; break;
            default: dx = -ring; dz = ring - step; break;
        }
    }

    /// <summary>Corner order (-,-), (+,-), (+,+), (-,+): the order the ring walk meets them.</summary>
    private static void CornerSigns(int corner, out int sx, out int sz)
    {
        sx = corner == 1 || corner == 2 ? 1 : -1;
        sz = corner >= 2 ? 1 : -1;
    }

    private void Put(int line, int point, float x, float y, float z)
    {
        float[] points = _points[line];
        points[point * 3] = x;
        points[point * 3 + 1] = y;
        points[point * 3 + 2] = z;
    }
}
