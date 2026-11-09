using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 slice 3, the lid runtime. The frame step and the first pose are game-free
/// (<see cref="HatchMotion.Step{TProbe}"/>, <see cref="HatchMotion.Settle{TProbe}"/>) and tested with a scripted
/// occupancy probe: the lid is only ever put at a pose the probe called clear, at most 5 degrees from the last one,
/// and it holds while anything is in the way. The Unity adapter (<c>Runtime/TrapdoorLid.cs</c>) is pinned by source:
/// it reads the native door state and writes nothing to the network, and its per-frame path does not allocate.</summary>
public sealed class TrapdoorLidTests
{
    private const float Frame = 1f / 60f;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClearFrameMovesExactlyTheProposalThroughCheckedPoses(bool open)
    {
        float start = open ? 0.2f : 0.8f;
        var probe = new ScriptedProbe(_ => false);
        float proposed = HatchMotion.Proposed(start, open, HatchMotion.MaximumStepSeconds, 0.1f);
        float reached = HatchMotion.Step(start, open, HatchMotion.MaximumStepSeconds, 0.1f, probe);

        Assert.Equal(proposed, reached);
        Assert.Equal(HatchMotion.SubSteps(start, proposed), probe.Asked.Count);
        Assert.True(probe.Asked.Count > 1, "A fast swing over a full step must be checked in more than one pose.");
        float previous = HatchMotion.Angle(start);
        foreach (float angle in probe.Asked)
        {
            Assert.True(open ? angle > previous : angle < previous, "Checked poses must run toward the target.");
            Assert.True(Math.Abs(angle - previous) <= HatchMotion.MaximumAngleStepDegrees + 1e-3f);
            previous = angle;
        }

        Assert.Equal(HatchMotion.Angle(reached), probe.Asked[^1], 4);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(10f)]
    [InlineData(37f)]
    [InlineData(80f)]
    public void OpeningStopsAtTheLastClearPoseAndAsksNothingAfterTheFirstOccupiedOne(float blockedFrom)
    {
        // Something stands where the lid would be from this angle up.
        var probe = new ScriptedProbe(angle => angle >= blockedFrom);
        float progress = 0f;
        for (int frame = 0; frame < 600; ++frame) { progress = HatchMotion.Step(progress, true, Frame, 0.5f, probe); }

        float held = HatchMotion.Angle(progress);
        Assert.True(held < blockedFrom, $"The lid reached {held} degrees into something from {blockedFrom}.");
        Assert.True(blockedFrom - held <= HatchMotion.MaximumAngleStepDegrees + 1e-3f, $"Held at {held}, too far short of {blockedFrom}.");
        Assert.True(progress == 0f || probe.ClearAnswers.Contains(held), "The lid was put at a pose nobody checked.");
        Assert.All(probe.Asked, angle => Assert.True(angle <= blockedFrom + HatchMotion.MaximumAngleStepDegrees + 1e-3f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnObstacleWiderThanTheCheckSpacingIsNeverSteppedOver(bool open)
    {
        // The guarantee is the check spacing: no two checked poses are more than 5 degrees apart, so anything that
        // spans just over that much of the sweep is always seen, wherever it lies and at the fastest swing and the
        // longest frame. (Something narrower can pass between two checked poses; HatchMotion's doc says so.) A
        // later clear pose must never carry the lid past it, within the same frame or any later one.
        const float width = HatchMotion.MaximumAngleStepDegrees + 0.05f;
        foreach (float swing in new[] { HatchMotion.MinimumSwingSeconds, 0.5f, 2f })
        {
            for (float from = 0.5f; from + width < HatchGeometry.OpenAngleDegrees - 0.5f; from += 0.75f)
            {
                float low = from, high = from + width;
                var probe = new ScriptedProbe(angle => angle >= low && angle <= high);
                float progress = open ? 0f : 1f;
                for (int frame = 0; frame < 400; ++frame) { progress = HatchMotion.Step(progress, open, HatchMotion.MaximumStepSeconds, swing, probe); }
                float held = HatchMotion.Angle(progress);
                Assert.True(open ? held < low : held > high, $"{swing}s: the lid passed [{low}, {high}] to {held} degrees.");
            }
        }
    }

    [Fact]
    public void ALidWithSomeoneOnItStaysShutUntilTheyLeaveThenOpensFully()
    {
        bool someoneOnIt = true;
        var probe = new ScriptedProbe(_ => someoneOnIt);
        float progress = 0f;
        for (int frame = 0; frame < 120; ++frame) { progress = HatchMotion.Step(progress, true, Frame, 1f, probe); }
        Assert.Equal(0f, progress);
        // While held, each frame asks once (the first sub-step) and gives up.
        Assert.Equal(120, probe.Asked.Count);

        someoneOnIt = false;
        int frames = 0;
        while (progress < 1f && frames < 600) { progress = HatchMotion.Step(progress, true, Frame, 1f, probe); frames++; }
        Assert.Equal(1f, progress);
        Assert.InRange(frames * Frame, 1f - Frame, 1f + 1.01f * Frame);
    }

    [Fact]
    public void ALidClosingOverAHeadWaitsAboveItAndShutsOnceItIsGone()
    {
        // A climber's head stands in the opening: every pose below 30 degrees overlaps it.
        bool headInTheWay = true;
        var probe = new ScriptedProbe(angle => headInTheWay && angle < 30f);
        float progress = 1f;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, false, Frame, 0.5f, probe); }
        float held = HatchMotion.Angle(progress);
        Assert.InRange(held, 30f, 30f + HatchMotion.MaximumAngleStepDegrees + 1e-3f);

        headInTheWay = false;
        for (int frame = 0; frame < 300; ++frame) { progress = HatchMotion.Step(progress, false, Frame, 0.5f, probe); }
        Assert.Equal(0f, progress);
    }

    [Fact]
    public void AReversalMidSwingIsCheckedLikeAnyOtherMove()
    {
        var probe = new ScriptedProbe(_ => false);
        float progress = 0f;
        for (int frame = 0; frame < 15; ++frame) { progress = HatchMotion.Step(progress, true, Frame, 1f, probe); }
        Assert.InRange(progress, 0.2f, 0.3f);
        probe.Asked.Clear();
        float back = HatchMotion.Step(progress, false, Frame, 1f, probe);
        Assert.True(back < progress);
        Assert.NotEmpty(probe.Asked);
        Assert.Equal(HatchMotion.Angle(back), probe.Asked[^1], 4);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(1f, true)]
    public void ALidAtRestAsksNothing(float progress, bool open)
    {
        var probe = new ScriptedProbe(_ => throw new InvalidOperationException("A resting lid must not query."));
        Assert.Equal(progress, HatchMotion.Step(progress, open, Frame, 0.5f, probe));
        Assert.Equal(progress, HatchMotion.Step(progress, !open, 0f, 0.5f, probe));
        Assert.Empty(probe.Asked);
    }

    [Theory]
    [InlineData(false, false, false, 0f)]
    [InlineData(true, false, false, 1f)]
    [InlineData(false, true, false, 1f)]
    [InlineData(true, true, false, 0f)]
    [InlineData(false, true, true, 0f)]
    [InlineData(true, true, true, 1f)]
    public void TheFirstPoseIsTheStoredStateUnlessThatWouldPutTheLidIntoSomeone(bool open, bool storedOccupied, bool otherOccupied, float expected)
    {
        float stored = open ? HatchGeometry.OpenAngleDegrees : 0f;
        var probe = new ScriptedProbe(angle => Math.Abs(angle - stored) < 1e-3f ? storedOccupied : otherOccupied);
        Assert.Equal(expected, HatchMotion.Settle(open, probe));
        int asked = !storedOccupied ? 1 : !otherOccupied ? 2 : 2 + (HatchMotion.SettleScanPoses - 1);
        Assert.Equal(asked, probe.Asked.Count);
        Assert.Equal(stored, probe.Asked[0], 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithBothEndsOccupiedTheLidSettlesAtTheClearPoseNearestItsStoredState(bool open)
    {
        // Both ends occupied; only the poses between 30 and 60 degrees are clear.
        var probe = new ScriptedProbe(angle => angle < 30f - 1e-3f || angle > 60f + 1e-3f);
        float settled = HatchMotion.Settle(open, probe);
        Assert.Equal(open ? 60f : 30f, HatchMotion.Angle(settled), 2);
        Assert.Contains(HatchMotion.Angle(settled), probe.ClearAnswers);
        // Nothing clear anywhere: the stored end, the pose the network agrees on.
        Assert.Equal(open ? 1f : 0f, HatchMotion.Settle(open, new ScriptedProbe(_ => true)));
    }

    [Fact]
    public void TheSettleScanCoversTheSwingAtTheCheckSpacing()
    {
        Assert.Equal(18, HatchMotion.SettleScanPoses);
        for (int pose = 0; pose <= HatchMotion.SettleScanPoses; ++pose)
        {
            float angle = HatchGeometry.OpenAngleDegrees * pose / HatchMotion.SettleScanPoses;
            Assert.Equal(angle, HatchMotion.Angle(HatchMotion.ProgressForAngle(angle)), 3);
        }
    }

    [Fact]
    public void TheRuntimeQueryHasASkinSoContactIsNotOccupancy()
    {
        // Something resting on a closed lid touches it. Without the skin a zone load would read the stored closed
        // pose as occupied and settle the lid open under it (independent review of d195449, blocker 1).
        Assert.InRange(HatchMotion.QuerySkinMetres, 0.005f, HatchGeometry.LidThickness / 4f);
        string lid = Code(Source("Runtime/TrapdoorLid.cs"));
        Assert.Matches(new Regex(@"float\s+skin\s*=\s*HatchMotion\s*\.\s*QuerySkinMetres\s*;"), lid);
        foreach (string axis in new[] { "x", "y", "z" })
        {
            Assert.Matches(new Regex(@"Mathf\s*\.\s*Max\s*\(\s*0\.005f\s*,\s*half\s*\.\s*" + axis + @"\s*-\s*skin\s*\)"), lid);
        }

        Assert.Matches(new Regex(@"OverlapBoxNonAlloc\s*\(\s*centre\s*,\s*_boxHalfExtents\s*,"), lid);
        Assert.Single(Regex.Matches(lid, @"_boxHalfExtents\s*=(?!=)"));
    }

    [Fact]
    public void ALidSettledAwayFromItsStoredStateReachesItOnceClear()
    {
        // Loaded closed with someone in the opening: it starts open and shuts only after they have gone.
        bool someoneInTheOpening = true;
        var probe = new ScriptedProbe(angle => someoneInTheOpening && angle < 60f);
        float progress = HatchMotion.Settle(false, probe);
        Assert.Equal(1f, progress);
        for (int frame = 0; frame < 120; ++frame) { progress = HatchMotion.Step(progress, false, Frame, 0.5f, probe); }
        Assert.True(HatchMotion.Angle(progress) >= 60f);
        someoneInTheOpening = false;
        for (int frame = 0; frame < 120; ++frame) { progress = HatchMotion.Step(progress, false, Frame, 0.5f, probe); }
        Assert.Equal(0f, progress);
    }

    [Fact]
    public void ThePassageAndLandingRulesHoldOnlyAtRestDuringARealSwing()
    {
        var probe = new ScriptedProbe(_ => false);
        float progress = 0f;
        while (progress < 1f)
        {
            Assert.False(HatchMotion.IsPassage(progress, true));
            Assert.False(HatchMotion.IsLanding(progress, true));
            progress = HatchMotion.Step(progress, true, Frame, 0.5f, probe);
        }

        Assert.True(HatchMotion.IsPassage(progress, true));
        // Asked to close: no longer a passage before it moves, and not a landing until it is shut.
        Assert.False(HatchMotion.IsPassage(progress, false));
        while (progress > 0f)
        {
            Assert.False(HatchMotion.IsLanding(progress, false));
            progress = HatchMotion.Step(progress, false, Frame, 0.5f, probe);
        }

        Assert.True(HatchMotion.IsLanding(progress, false));
    }

    // ---- The Unity adapter, by source ----

    [Fact]
    public void TheLidReadsTheNativeDoorStateAndWritesNothingToTheNetwork()
    {
        string lid = Code(Source("Runtime/TrapdoorLid.cs"));
        // Three reads of the one native value: the first pose (TrySettle), each frame (Update) and the ladder's
        // passage and landing questions (TryReadOpen, slice 4). Each after IsValid().
        Assert.Equal(3, Regex.Matches(lid, @"\.\s*GetZDO\s*\(\s*\)\s*\.\s*GetInt\s*\(\s*ZDOVars\s*\.\s*s_state\s*\)").Count);
        Assert.Equal(3, Regex.Matches(lid, @"view\s*==\s*null\s*\|\|\s*!\s*view\s*\.\s*IsValid\s*\(\s*\)").Count);
        foreach (string forbidden in new[]
                 {
                     @"\bInvokeRPC\b", @"\bRoutedRPC\b", @"\.\s*Register\s*<", @"\.\s*Register\s*\(", @"\.\s*Set\s*\(",
                     @"\bClaimOwnership\b", @"\bSetOwner\b", @"\bDestroy\w*\s*\(", @"\bInteract\s*\(", @"\bUseDoor\b",
                     @"\bMoveRotation\b", @"\bMovePosition\b", @"\bAddForce\w*\b", @"\bvelocity\b", @"\bangularVelocity\b",
                     @"\bSetInteger\b", @"\bisKinematic\s*=",
                 })
        {
            Assert.DoesNotMatch(new Regex(forbidden), lid);
        }

        // The only transform it turns is the hinge, about local x, by the model's angle.
        Assert.Single(Regex.Matches(lid, @"_hinge\s*!\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*Euler\s*\(\s*angle\s*,\s*0f\s*,\s*0f\s*\)\s*;"));
        Assert.Single(Regex.Matches(lid, @"\blocalRotation\s*=\s*Quaternion\s*\.\s*identity\s*;"));
        Assert.Equal(2, Regex.Matches(lid, @"\.\s*(localRotation|rotation|localPosition|position|localScale)\s*=(?!=)").Count);
    }

    [Fact]
    public void TheLidSteppedAndSettledThroughTheTestedModel()
    {
        string lid = Code(Source("Runtime/TrapdoorLid.cs"));
        Assert.Single(Regex.Matches(lid, @"HatchMotion\s*\.\s*Step\s*\(\s*_progress\s*,\s*open\s*,\s*Time\s*\.\s*deltaTime\s*,\s*SwingSeconds\s*,\s*this\s*\)"));
        Assert.Single(Regex.Matches(lid, @"HatchMotion\s*\.\s*Settle\s*\(\s*open\s*,\s*this\s*\)"));
        Assert.Equal(2, Regex.Matches(lid, @"\b_progress\s*=(?!=)").Count);
        // One bounded, non-allocating box overlap that ignores triggers; a full buffer is occupied.
        Assert.Single(Regex.Matches(lid, @"Physics\s*\.\s*OverlapBoxNonAlloc\s*\([^;]*QueryTriggerInteraction\s*\.\s*Ignore\s*\)"));
        Assert.Matches(new Regex(@"bool\s+occupied\s*=\s*found\s*>=\s*Overlaps\s*\.\s*Length\s*;"), lid);
        Assert.DoesNotMatch(new Regex(@"Physics\s*\.\s*(OverlapBox|OverlapSphere|OverlapCapsule|BoxCastAll|RaycastAll)\s*\("), lid);
        // Its own hatch never blocks it.
        Assert.Matches(new Regex(@"collider\s*\.\s*transform\s*\.\s*IsChildOf\s*\(\s*transform\s*\)"), lid);
    }

    [Fact]
    public void TheLidsPerFramePathDoesNotAllocate()
    {
        string lid = Code(Source("Runtime/TrapdoorLid.cs"));
        string update = Between(lid, @"private\s+void\s+Update\s*\(\s*\)", @"\bcatch\s*\(");
        string rest = Between(lid, @"private\s+void\s+Apply\s*\(\s*\)", null);
        foreach (string body in new[] { update, rest })
        {
            foreach (string allocating in new[]
                     {
                         @"\bnew\s+(?!Vector3\b|Quaternion\b)", @"\bGetComponent\w*\s*[<(]", @"\bFind\s*\(", @"\.\s*Select\s*\(", @"\.\s*Where\s*\(",
                         @"\.\s*ToArray\s*\(", @"\.\s*ToList\s*\(", @"""", @"\bstring\s*\.", @"=>\s*\w+\s*\(", @"\bLog\b",
                     })
            {
                Assert.DoesNotMatch(new Regex(allocating), body);
            }
        }
    }

    [Fact]
    public void EveryTrapdoorCloneCarriesTheLidWithItsDonorsSwingBeforeItIsVerified()
    {
        string adapter = Code(Source("Pieces/TrapdoorPieceRegistration.cs"));
        Assert.Single(Regex.Matches(adapter, @"AddComponent\s*<\s*TrapdoorLid\s*>"));
        int add = Index(adapter, @"prefab\s*\.\s*AddComponent\s*<\s*TrapdoorLid\s*>\s*\(\s*\)\s*\.\s*SwingSeconds\s*=\s*family\s*\.\s*SwingSeconds\s*;");
        int verify = Index(adapter, @"\bVerify\s*\(\s*prefab\s*,\s*wear\s*\)\s*;");
        int register = Index(adapter, @"PieceManager\s*\.\s*Instance\s*\.\s*AddPiece\s*\(");
        Assert.True(add < verify && verify < register, "The lid must be on the clone before it is verified and registered.");
        Assert.Matches(new Regex(@"GetComponentsInChildren\s*<\s*TrapdoorLid\s*>\s*\(\s*true\s*\)"), adapter);

        // Nothing else in the product adds a lid or reads or writes the door state.
        foreach (string file in ProductFiles())
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            string text = Code(File.ReadAllText(file));
            if (relative != "Pieces/TrapdoorPieceRegistration.cs") { Assert.DoesNotMatch(new Regex(@"AddComponent\s*<\s*TrapdoorLid\s*>"), text); }
            if (relative != "Runtime/TrapdoorLid.cs") { Assert.DoesNotMatch(new Regex(@"ZDOVars\s*\.\s*s_state"), text); }
        }
    }

    [Fact]
    public void EveryFamilySwingIsOneTheLidAccepts()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            Assert.InRange(family.SwingSeconds, HatchMotion.MinimumSwingSeconds, HatchMotion.MaximumSwingSeconds);
        }
    }

    private sealed class ScriptedProbe : IHatchOccupancy
    {
        private readonly Func<float, bool> _rule;

        internal ScriptedProbe(Func<float, bool> rule) => _rule = rule;

        internal List<float> Asked { get; } = new List<float>();

        internal HashSet<float> ClearAnswers { get; } = new HashSet<float>();

        public bool Occupied(float angleDegrees)
        {
            Asked.Add(angleDegrees);
            bool occupied = _rule(angleDegrees);
            if (!occupied) { ClearAnswers.Add(angleDegrees); }
            return occupied;
        }
    }

    /// <summary>The source without comments, so a pin is never satisfied or tripped by prose.</summary>
    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Between(string text, string startPattern, string? endPattern)
    {
        Match start = Regex.Match(text, startPattern);
        Assert.True(start.Success, "Missing: " + startPattern);
        if (endPattern == null) { return text.Substring(start.Index); }
        Match end = new Regex(endPattern).Match(text, start.Index);
        Assert.True(end.Success, "Missing: " + endPattern);
        return text.Substring(start.Index, end.Index - start.Index);
    }

    private static int Index(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        Assert.True(match.Success, "Missing: " + pattern);
        return match.Index;
    }

    private static IEnumerable<string> ProductFiles() =>
        Directory.EnumerateFiles(ProductRoot, "*.cs", SearchOption.AllDirectories).Where(file =>
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            return !relative.StartsWith("obj/", StringComparison.Ordinal) && !relative.StartsWith("bin/", StringComparison.Ordinal);
        });

    private static string Source(string relative) => File.ReadAllText(Path.Combine(ProductRoot, relative));

    private static string ProductRoot => Path.Combine(RepoRoot, "src", "ConcernedLadders");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
