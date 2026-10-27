using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Animation;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>The data exported from AnimationSources/LadderGait.blend (GeneratedGait) is well formed and, until the owner
/// accepts an authored change, equals the C# defaults it was seeded from: the LimbGait loops with the stance override
/// below, and DefaultTransitions. A deliberate edit in Blender fails <see cref="TheExportMatchesTheCodeDefaults"/> with
/// the list of channels that moved; see AnimationSources/README.md ("When a test fails after an edit").</summary>
public sealed class GeneratedGaitTests
{
    private const float Tolerance = 1e-4f;
    private static readonly string[] Limbs = { "LH", "RH", "LF", "RF" };

    /// <summary>The C# defaults the .blend was seeded from (GaitShape defaults plus the fitted hand stance).</summary>
    private static GaitShape Defaults() => new GaitShape { FootLowRel = 0f, HandMidFromShoulder = -0.02f };

    [Fact]
    public void TheExportIsValid()
    {
        GaitShape shape = GeneratedGait.Shape();
        Assert.True(shape.Valid);
        Assert.Equal("climb_up", shape.Up.Name);
        Assert.Equal("climb_down", shape.Down.Name);
        TransitionAction[] all = GeneratedGait.Transitions();
        foreach (TransitionKind kind in Enum.GetValues(typeof(TransitionKind)))
        {
            TransitionAction action = Assert.Single(all, a => a.Kind == kind);
            Assert.True(action.Valid, kind + " is malformed");
        }

        Assert.Equal(64, GeneratedGait.BlendSha256.Length);
        Assert.Equal(64, GeneratedGait.JsonSha256.Length);
    }

    [Fact]
    public void TheExportMatchesTheCodeDefaults()
    {
        var moved = new List<string>();
        GaitShape expected = Defaults(), actual = GeneratedGait.Shape();
        Loop(moved, "Up", expected.Up, actual.Up);
        Loop(moved, "Down", expected.Down, actual.Down);
        Near(moved, "FootLowRel", expected.FootLowRel, actual.FootLowRel);
        Near(moved, "HandMidFromShoulder", expected.HandMidFromShoulder, actual.HandMidFromShoulder);
        Near(moved, "HandSettleBelow", expected.HandSettleBelow, actual.HandSettleBelow);
        Near(moved, "HandSettleAbove", expected.HandSettleAbove, actual.HandSettleAbove);
        Near(moved, "FootSettleBelow", expected.FootSettleBelow, actual.FootSettleBelow);
        Near(moved, "FootSettleAbove", expected.FootSettleAbove, actual.FootSettleAbove);
        Near(moved, "SettleSeconds", expected.SettleSeconds, actual.SettleSeconds);
        Near(moved, "RejoinSeconds", expected.RejoinSeconds, actual.RejoinSeconds);
        Near(moved, "ReverseSeconds", expected.ReverseSeconds, actual.ReverseSeconds);

        TransitionAction[] defaults = DefaultTransitions.All(), generated = GeneratedGait.Transitions();
        Assert.Equal(defaults.Select(a => a.Kind), generated.Select(a => a.Kind));
        for (int i = 0; i < defaults.Length; i++) { Transition(moved, defaults[i], generated[i]); }

        // The posture and bend constants were these literals before the Blender source carried them.
        // Owner 2026-10-27 (climber too close, knees folded): pelvis up and back, chest leaning in.
        Near(moved, "PelvisDrop", 0f, GeneratedGait.PelvisDrop);
        Near(moved, "PelvisToward", 0.16f, GeneratedGait.PelvisToward);
        Near(moved, "Lean", 12f, GeneratedGait.Lean);
        Near(moved, "SlideLean", 6f, GeneratedGait.SlideLean);
        Near(moved, "SwayTilt", 3f, GeneratedGait.SwayTilt);
        Near(moved, "SwayShift", 0.03f, GeneratedGait.SwayShift);
        Near(moved, "ElbowOut", 0.18f, GeneratedGait.ElbowOut);
        Near(moved, "ElbowSide", 0.08f, GeneratedGait.ElbowSide);
        Near(moved, "ElbowDown", 0.32f, GeneratedGait.ElbowDown);
        Near(moved, "KneeToward", 0.22f, GeneratedGait.KneeToward);
        Near(moved, "KneeSide", 0.04f, GeneratedGait.KneeSide);
        Near(moved, "KneeDown", 0.15f, GeneratedGait.KneeDown);

        Assert.True(moved.Count == 0, "GeneratedGait differs from the C# defaults in " + moved.Count + " values:\n" + string.Join("\n", Summary(moved)));
    }

    [Fact]
    public void TheExportBindsToARealRun()
    {
        Assert.True(new LimbGait().Bind(GeneratedGait.Shape(), 0.1667f, 1f / 3f, 0, 100, 1.38f, 12f));
    }

    private static void Loop(List<string> moved, string name, ClimbLoop expected, ClimbLoop actual)
    {
        if (expected.Name != actual.Name) { moved.Add(name + ".Name " + expected.Name + " -> " + actual.Name); }
        if (expected.StrideRungs != actual.StrideRungs) { moved.Add(name + ".StrideRungs " + expected.StrideRungs + " -> " + actual.StrideRungs); }
        for (int limb = 0; limb < 4; limb++)
        {
            Near(moved, name + ".Start[" + Limbs[limb] + "]", expected.Start[limb], actual.Start[limb]);
            Near(moved, name + ".Length[" + Limbs[limb] + "]", expected.Length[limb], actual.Length[limb]);
            Table3(moved, name + ".Arc[" + Limbs[limb] + "]", expected.Arc[limb], actual.Arc[limb], "lat", "out", "up");
        }

        Table(moved, name + ".Swing", expected.Swing, actual.Swing);
        Table3(moved, name + ".Body", expected.Body, actual.Body, "up", "toward");
        Table(moved, name + ".Lean", expected.Lean, actual.Lean);
    }

    private static void Transition(List<string> moved, TransitionAction expected, TransitionAction actual)
    {
        string name = expected.Kind.ToString();
        Near(moved, name + ".Seconds", expected.Seconds, actual.Seconds);
        for (int limb = 0; limb < 4; limb++)
        {
            Table(moved, name + ".Weight[" + Limbs[limb] + "]", expected.Weight[limb], actual.Weight[limb]);
            Table(moved, name + ".Reach[" + Limbs[limb] + "]", expected.Reach[limb], actual.Reach[limb]);
            Table3(moved, name + ".Arc[" + Limbs[limb] + "]", expected.Arc[limb], actual.Arc[limb], "lat", "out", "up");
        }

        Table(moved, name + ".BodyWeight", expected.BodyWeight, actual.BodyWeight);
        Table(moved, name + ".BodyUp", expected.BodyUp, actual.BodyUp);
        Table(moved, name + ".BodyToward", expected.BodyToward, actual.BodyToward);
        Table(moved, name + ".Lean", expected.Lean, actual.Lean);
    }

    private static void Table(List<string> moved, string name, float[] expected, float[] actual)
    {
        if (expected.Length != actual.Length) { moved.Add(name + " has " + actual.Length + " samples, expected " + expected.Length); return; }
        for (int i = 0; i < expected.Length; i++) { Near(moved, name + "[" + i + "]", expected[i], actual[i]); }
    }

    private static void Table3(List<string> moved, string name, float[][] expected, float[][] actual, params string[] columns)
    {
        if (expected.Length != actual.Length) { moved.Add(name + " has " + actual.Length + " samples, expected " + expected.Length); return; }
        for (int i = 0; i < expected.Length; i++)
        {
            for (int c = 0; c < columns.Length; c++) { Near(moved, name + "[" + i + "]." + columns[c], expected[i][c], actual[i][c]); }
        }
    }

    private static void Near(List<string> moved, string name, float expected, float actual)
    {
        if (!(Math.Abs(expected - actual) <= Tolerance)) { moved.Add(name + " " + expected.ToString("0.######") + " -> " + actual.ToString("0.######")); }
    }

    /// <summary>One line per channel (a table without its sample index), with how many samples moved and the first
    /// of them, so an authored edit reads as the list of channels it touched.</summary>
    private static IEnumerable<string> Summary(List<string> moved) =>
        moved.GroupBy(Channel).Select(channel => "  " + channel.Key + ": " + channel.Count() + " value(s), first " + channel.First());

    /// <summary>"Up.Arc[LF][16].up 0.05 -> 0.075" belongs to channel "Up.Arc[LF].up".</summary>
    private static string Channel(string entry)
    {
        string path = entry.Split(' ')[0];
        int open = path.LastIndexOf('['), close = path.LastIndexOf(']');
        if (open < 0 || close < open || !int.TryParse(path.Substring(open + 1, close - open - 1), out _)) { return path; }
        return path.Substring(0, open) + path.Substring(close + 1);
    }
}
