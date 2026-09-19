using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>What a caller thinks of a point the ground already accepted.
///
/// <b>Why standing room is not the whole question.</b> The probe answers "may an
/// NPC stand here"; whether there is anything here worth walking to is the
/// caller's business - a tree, a pile, a spot to put a wall. Keeping the two
/// apart is what lets one sweep serve a navigation search and a resource search
/// without this package learning what a tree is.</summary>
internal enum NpcTargetVerdict
{
    /// <summary>Could not tell. Counted as ground that was not looked at, so a
    /// filter that cannot answer can never make a sweep conclusive. Never a
    /// yes, and never evidence of emptiness.</summary>
    Unknown = 0,

    /// <summary>Worth walking to.</summary>
    Usable = 1,

    /// <summary>The right thing, already used up - picked, felled, awaiting
    /// respawn, taken by somebody else. Reported apart from
    /// <see cref="Unsuitable"/> because it tells a player to wait rather than to
    /// go somewhere else.</summary>
    Exhausted = 2,

    /// <summary>Not what was being looked for.</summary>
    Unsuitable = 3,
}

/// <summary>What the caller is looking for, asked about one standable point.
/// Never throws for a world reason: a filter that cannot answer returns
/// <see cref="NpcTargetVerdict.Unknown"/>.</summary>
internal interface INpcTargetFilter
{
    NpcTargetVerdict Judge(NpcPoint ground);
}

/// <summary>One pass of a sweep: what it found, and what it actually did.</summary>
internal readonly struct NpcAreaSweepPass
{
    private readonly IReadOnlyList<NpcPoint>? _targets;

    internal NpcAreaSweepPass(AreaScanReport report, IReadOnlyList<NpcPoint>? targets)
    {
        Report = report;
        _targets = targets;
    }

    /// <summary>The evidence, and the one answer derived from it.</summary>
    internal AreaScanReport Report { get; }

    /// <summary>Points an NPC can stand on that the caller wants, in the order
    /// they were found - which is the order they were proposed, which is
    /// deterministic. Empty for every outcome but
    /// <see cref="AreaScanOutcome.Found"/>.</summary>
    internal IReadOnlyList<NpcPoint> Targets => _targets ?? Array.Empty<NpcPoint>();
}

/// <summary>Looking over a work area for somewhere usable, a bounded number of
/// questions at a time.
///
/// <b>Propose in order, let the world say no, fall through.</b> That is the
/// idiom the probe seam describes and the only one the world can honestly
/// answer: there is no sweep of an area for standable ground in this game, only
/// one point at a time. So candidates come from the area's bounding circle in a
/// fixed order - the centre, then rings outward - every one of them is offered
/// to <see cref="INpcWorkArea.Contains"/> before anything is asked about it, and
/// the probe refuses what it likes.
///
/// <b>Deterministic, and that is load bearing.</b> The same area gives the same
/// candidates in the same order in every session, so a sweep interrupted by a
/// reload resumes over the same ground rather than a fresh random walk, and a
/// failing case can be reproduced from the area alone.
///
/// <b>Bounded and resumable.</b> A pass spends at most the probe calls it was
/// given and remembers where it stopped. Running out of budget is
/// <see cref="AreaScanOutcome.Incomplete"/>, never
/// <see cref="AreaScanOutcome.Empty"/>: a spent budget is not a finished job,
/// and an NPC that reports his settlement out of wood because a tick ran long is
/// an NPC nobody believes again.
///
/// <b>Every honest refusal is kept apart.</b> Ground that is not loaded, a point
/// outside the area, a filter that could not answer and a thing already used up
/// are four different facts, and only one arrangement of them -
/// everything looked at, nothing unknown, nothing truncated - is allowed to mean
/// the job can end.</summary>
internal sealed class NpcAreaSweep
{
    /// <summary>The golden angle in radians. Offsetting each ring by it keeps
    /// successive rings' spokes from lining up into spokes of empty ground,
    /// without making the order depend on anything but the ring number.</summary>
    private const double GoldenAngle = 2.39996322972865332d;

    private readonly INpcWorkArea? _area;
    private readonly INpcAreaProbe? _probe;
    private readonly INpcTargetFilter? _filter;
    private readonly int _rings;
    private readonly int _spokes;

    private int _next;
    private bool _areaFailed;
    private int _examined;
    private int _notLoaded;
    private int _rejected;
    private int _exhausted;

    internal NpcAreaSweep(
        INpcWorkArea? area, INpcAreaProbe? probe, INpcTargetFilter? filter = null, int rings = 6, int spokes = 12)
    {
        _area = area;
        _probe = probe;
        _filter = filter;
        _rings = Math.Max(1, rings);
        _spokes = Math.Max(1, spokes);
    }

    /// <summary>How many candidates this sweep will ever propose: the centre and
    /// every ring position.</summary>
    internal int CandidateCount => 1 + (_rings * _spokes);

    /// <summary>Whether every candidate has been proposed.</summary>
    internal bool IsFinished => _areaFailed || _next >= CandidateCount;

    /// <summary>Starts over. Called when the area's revision changes, because
    /// candidates computed from the old geometry mean nothing against the
    /// new.</summary>
    internal void Restart()
    {
        _next = 0;
        _areaFailed = false;
        _examined = 0;
        _notLoaded = 0;
        _rejected = 0;
        _exhausted = 0;
    }

    /// <summary>Proposes candidates until the area is finished or
    /// <paramref name="probeBudget"/> probe calls have been spent, whichever
    /// comes first.
    ///
    /// Candidates outside the area cost no probe call - they are refused by the
    /// area itself - but are still counted and reported as
    /// <see cref="AreaRejection.OutsideArea"/>, so a sweep whose bounding circle
    /// is mostly outside its own area is visible rather than merely slow.
    /// </summary>
    internal NpcAreaSweepPass Next(int probeBudget)
    {
        if (!IsAreaUsable())
        {
            _areaFailed = true;
            return new NpcAreaSweepPass(Report(AreaScanOutcome.AreaInvalid), null);
        }

        int budget = Math.Max(0, probeBudget);
        var targets = new List<NpcPoint>();
        int spent = 0;

        while (_next < CandidateCount)
        {
            if (spent >= budget)
            {
                break;
            }

            NpcPoint candidate = CandidateAt(_next);
            _next++;
            _examined++;

            bool inside;
            try
            {
                inside = _area!.Contains(candidate);
            }
            catch (Exception)
            {
                // An area that cannot answer its own containment question is not
                // an area. Fail closed and stop, rather than sweeping a bounding
                // circle nobody agreed to.
                _areaFailed = true;
                return new NpcAreaSweepPass(Report(AreaScanOutcome.AreaInvalid), null);
            }

            if (!inside)
            {
                _rejected++;
                continue;
            }

            spent++;
            AreaSample sample;
            try
            {
                sample = _probe!.Probe(candidate);
            }
            catch (Exception)
            {
                // The seam says a probe never throws for a world reason. One
                // that does has not proved the point is bad, so it counts as
                // ground nobody looked at.
                _notLoaded++;
                continue;
            }

            switch (sample.Verdict)
            {
                case AreaSampleVerdict.Standable:
                    Classify(sample.Ground, targets);
                    break;

                case AreaSampleVerdict.NotLoaded:
                case AreaSampleVerdict.Unreadable:
                    // Deliberately together: "I could not tell" and "I have not
                    // looked yet" are both unknown, and unknown may never add up
                    // to empty. The report has three counters and the probe has
                    // four verdicts, so this is where the two compress - always
                    // towards the answer that keeps a job open.
                    _notLoaded++;
                    break;

                default:
                    _rejected++;
                    break;
            }
        }

        AreaScanOutcome outcome = Outcome(targets.Count);
        return new NpcAreaSweepPass(Report(outcome), outcome == AreaScanOutcome.Found ? targets : null);
    }

    /// <summary>The evidence for the sweep so far, not for this pass alone.
    ///
    /// <b>Cumulative on purpose.</b> The one claim these counts support is "the
    /// job can end", and that claim is about the whole area rather than about
    /// the last twelve probes. A per-pass report makes a sweep that has finished
    /// answer with nothing examined, which is exactly the shape of a conclusive
    /// "there is nothing here" backed by no evidence at all.
    ///
    /// <see cref="AreaScanReport.TruncatedByBudget"/> is therefore "this sweep
    /// has not reached the end of its candidates", whatever the reason: a spent
    /// budget or an early return. It is what keeps
    /// <see cref="AreaScanReport.IsConclusive"/> false until there is genuinely
    /// nothing left to look at.</summary>
    private AreaScanReport Report(AreaScanOutcome outcome) =>
        new AreaScanReport(outcome, _examined, _notLoaded, _rejected, _exhausted, !IsFinished);

    /// <summary>A short, deterministic list of points inside an area, for the
    /// one question a role's adapter has to answer before a checkpoint can:
    /// is any of this ground loaded.
    ///
    /// <b>The first nine are the shipped ones.</b> The centre, then eight points
    /// on a ring at 70 % of the radius - the exact set the shipped scope check
    /// uses - so for a circle this is the same question asked the same way, and
    /// a role adopting it sees no change. The difference is that every point is
    /// offered to <see cref="INpcWorkArea.Contains"/> first, and for a shape
    /// where none of the nine is inside, the sweep's own candidate order
    /// continues until enough points are. A polygon whose bounding circle
    /// centre sits outside it is not an unloaded area, and would have looked
    /// like one.</summary>
    internal static IReadOnlyList<NpcPoint> LoadCheckPoints(INpcWorkArea? area, int count)
    {
        var points = new List<NpcPoint>();
        if (area == null || count < 1)
        {
            return points;
        }

        NpcPoint centre;
        float radius;
        try
        {
            centre = area.BoundingCentre;
            radius = area.BoundingRadiusMetres;
        }
        catch (Exception)
        {
            return points;
        }

        if (!centre.IsFinite || float.IsNaN(radius) || float.IsInfinity(radius) || !(radius > 0f))
        {
            return points;
        }

        Consider(area, centre, points, count);

        float ring = radius * 0.7f;
        for (int step = 0; step < 8 && points.Count < count; step++)
        {
            double angle = step * (Math.PI / 4d);
            Consider(
                area,
                new NpcPoint(
                    centre.X + (float)(Math.Cos(angle) * ring), centre.Y, centre.Z + (float)(Math.Sin(angle) * ring)),
                points,
                count);
        }

        if (points.Count >= count)
        {
            return points;
        }

        var sweep = new NpcAreaSweep(area, null);
        for (int index = 0; index < sweep.CandidateCount && points.Count < count; index++)
        {
            Consider(area, sweep.CandidateAt(index), points, count);
        }

        return points;
    }

    private static void Consider(INpcWorkArea area, NpcPoint point, List<NpcPoint> into, int count)
    {
        if (into.Count >= count)
        {
            return;
        }

        try
        {
            if (area.Contains(point))
            {
                into.Add(point);
            }
        }
        catch (Exception)
        {
            // An area that cannot answer contributes no points, which reads as
            // "none of it is loaded" - a wait, not a stop. The checkpoint turns
            // an area that has actually gone into Invalid on its own evidence.
        }
    }

    private void Classify(NpcPoint ground, List<NpcPoint> targets)
    {
        bool inside;
        try
        {
            inside = _area!.Contains(ground);
        }
        catch (Exception)
        {
            _rejected++;
            return;
        }

        if (!inside)
        {
            // The probe searches a band and answers with where the ground
            // actually is, which is rarely the point asked about. Ground that
            // slid outside the area is outside the area.
            _rejected++;
            return;
        }

        NpcTargetVerdict verdict;
        try
        {
            verdict = _filter == null ? NpcTargetVerdict.Usable : _filter.Judge(ground);
        }
        catch (Exception)
        {
            _notLoaded++;
            return;
        }

        switch (verdict)
        {
            case NpcTargetVerdict.Usable:
                targets.Add(ground);
                break;

            case NpcTargetVerdict.Exhausted:
                _exhausted++;
                break;

            case NpcTargetVerdict.Unsuitable:
                _rejected++;
                break;

            default:
                _notLoaded++;
                break;
        }
    }

    private AreaScanOutcome Outcome(int found)
    {
        if (found > 0)
        {
            return AreaScanOutcome.Found;
        }

        if (!IsFinished)
        {
            return AreaScanOutcome.Incomplete;
        }

        if (_notLoaded > 0)
        {
            return AreaScanOutcome.NotLoaded;
        }

        return _exhausted > 0 ? AreaScanOutcome.Exhausted : AreaScanOutcome.Empty;
    }

    private bool IsAreaUsable()
    {
        if (_areaFailed || _area == null || _probe == null)
        {
            return false;
        }

        try
        {
            return _area.BoundingCentre.IsFinite
                && !float.IsNaN(_area.BoundingRadiusMetres)
                && !float.IsInfinity(_area.BoundingRadiusMetres)
                && _area.BoundingRadiusMetres > 0f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The candidate at one index: 0 is the centre, then ring by ring
    /// outward. Pure arithmetic on the bounding circle, so it is the same list
    /// every session.</summary>
    private NpcPoint CandidateAt(int index)
    {
        NpcPoint centre = _area!.BoundingCentre;
        if (index <= 0)
        {
            return centre;
        }

        int position = index - 1;
        int ring = (position / _spokes) + 1;
        int spoke = position % _spokes;
        float radius = _area.BoundingRadiusMetres * ring / _rings;
        double angle = (spoke * (2d * Math.PI / _spokes)) + (ring * GoldenAngle);
        return new NpcPoint(
            centre.X + (float)(Math.Cos(angle) * radius), centre.Y, centre.Z + (float)(Math.Sin(angle) * radius));
    }
}
