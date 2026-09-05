using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-028: cooperative effort must classify each observed player as
/// helping / hindering / idle / unclear from read-only motion facts, tally
/// and summarize with only already-visible names, and combine with the
/// physical stuck verdict without ever changing it (no force is granted).</summary>
public class CooperativeEffortClassifierTests
{
    private static CoopParticipant P(
        string name, bool attached, bool contact, float alignment, bool local = false)
    {
        return new CoopParticipant(name, local, attached, contact, alignment);
    }

    // -- single-participant classification matrix --

    [Fact]
    public void AttachedPuller_IsHelping()
    {
        // Pulling the handle is help by definition — regardless of contact
        // flag or motion reading, the attached check short-circuits first.
        var puller = P("Ana", attached: true, contact: false, alignment: float.NaN);
        Assert.Equal(CoopEffort.Helping, CooperativeEffortClassifier.Classify(puller));
    }

    [Fact]
    public void PushingAlong_IsHelping()
    {
        var pusher = P("Bo", attached: false, contact: true, alignment: 0.8f);
        Assert.Equal(CoopEffort.Helping, CooperativeEffortClassifier.Classify(pusher));
    }

    [Fact]
    public void PushingAgainst_IsHindering()
    {
        var blocker = P("Cy", attached: false, contact: true, alignment: -0.8f);
        Assert.Equal(CoopEffort.Hindering, CooperativeEffortClassifier.Classify(blocker));
    }

    [Fact]
    public void ContactButNoMeaningfulMotion_IsIdle()
    {
        var leaning = P("Di", attached: false, contact: true, alignment: 0.05f);
        Assert.Equal(CoopEffort.Idle, CooperativeEffortClassifier.Classify(leaning));
    }

    [Fact]
    public void NotAttachedNotInContact_IsIdle()
    {
        var away = P("Ed", attached: false, contact: false, alignment: 0.9f);
        Assert.Equal(CoopEffort.Idle, CooperativeEffortClassifier.Classify(away));
    }

    [Fact]
    public void InContactUnknownMotion_IsUnclear()
    {
        var unknown = P("Fi", attached: false, contact: true, alignment: float.NaN);
        Assert.Equal(CoopEffort.Unclear, CooperativeEffortClassifier.Classify(unknown));
    }

    [Theory]
    [InlineData(0.16f, CoopEffort.Helping)]   // just over the threshold
    [InlineData(0.15f, CoopEffort.Idle)]      // exactly the threshold is not meaningful
    [InlineData(-0.16f, CoopEffort.Hindering)]
    [InlineData(-0.15f, CoopEffort.Idle)]
    public void AlignmentThreshold_IsHalfOpen(float alignment, CoopEffort expected)
    {
        var p = P("G", attached: false, contact: true, alignment: alignment);
        Assert.Equal(expected, CooperativeEffortClassifier.Classify(p));
    }

    // -- tally over a multi-actor trace --

    [Fact]
    public void Tally_CountsEveryRole()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ana", attached: true, contact: true, alignment: 0f),   // helping (puller)
            P("Bo", attached: false, contact: true, alignment: 0.7f), // helping (push)
            P("Cy", attached: false, contact: true, alignment: -0.7f),// hindering
            P("Di", attached: false, contact: true, alignment: 0f),   // idle (touch)
            P("Ed", attached: false, contact: false, alignment: 0f),  // idle (away)
            P("Fi", attached: false, contact: true, alignment: float.NaN), // unclear
        };

        CooperativeEffortClassifier.EffortTally tally =
            CooperativeEffortClassifier.Tally(crew);

        Assert.Equal(2, tally.Helping);
        Assert.Equal(1, tally.Hindering);
        Assert.Equal(2, tally.Idle);
        Assert.Equal(1, tally.Unclear);
        Assert.Equal(3, tally.Contributors);
    }

    // -- summary uses only visible names, capped --

    [Fact]
    public void Summarize_NamesHelpersAndHinderers()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ana", attached: true, contact: true, alignment: 0f),
            P("Cy", attached: false, contact: true, alignment: -0.9f),
        };

        string summary = CooperativeEffortClassifier.Summarize(crew);

        Assert.Contains("1 helping (Ana)", summary);
        Assert.Contains("1 hindering (Cy)", summary);
    }

    [Fact]
    public void Summarize_EmptyWhenNobodyContributesOrIsUnclear()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ed", attached: false, contact: false, alignment: 0f),
            P("Di", attached: false, contact: true, alignment: 0f),
        };

        Assert.Equal(string.Empty, CooperativeEffortClassifier.Summarize(crew));
    }

    [Fact]
    public void Summarize_FallsBackToVisiblePlaceholdersWhenNameMissing()
    {
        var crew = new List<CoopParticipant>
        {
            P("", attached: true, contact: true, alignment: 0f, local: true),
            P("", attached: false, contact: true, alignment: 0.9f),
        };

        string summary = CooperativeEffortClassifier.Summarize(crew);

        Assert.Contains("you", summary);
        Assert.Contains("a teammate", summary);
    }

    // -- combined-effort explanation never overrides the physical verdict --

    [Fact]
    public void ExplainStuck_EvenWithHelp_KeepsImpossibleLoadVerdict()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ana", attached: true, contact: true, alignment: 0f),
            P("Bo", attached: false, contact: true, alignment: 0.8f),
        };
        var diagnosis = new CartDiagnostic(
            CartDiagnosis.ImpossibleLoad, "load 900 exceeds the proven limit", "unload before this grade.");

        string line = CooperativeEffortClassifier.ExplainStuck(crew, diagnosis);

        Assert.Contains("Crew: 2 helping", line);
        Assert.Contains("Even with help", line);
        Assert.Contains("load 900 exceeds the proven limit", line);
        Assert.Contains("overloaded for this grade", line);
    }

    [Fact]
    public void ExplainStuck_NoCrew_IsJustThePhysicalLine()
    {
        var crew = new List<CoopParticipant>();
        var diagnosis = new CartDiagnostic(
            CartDiagnosis.Obstruction, "near-level ground", "check the wheels.");

        string line = CooperativeEffortClassifier.ExplainStuck(crew, diagnosis);

        Assert.Equal(diagnosis.ComposeLine(), line);
        Assert.DoesNotContain("Crew:", line);
    }

    [Fact]
    public void ExplainStuck_OnlyHinderersNoHelpers_CallsItOut()
    {
        var crew = new List<CoopParticipant>
        {
            P("Cy", attached: false, contact: true, alignment: -0.9f),
        };

        string line = CooperativeEffortClassifier.ExplainStuck(crew, CartDiagnostic.None);

        Assert.Contains("1 hindering", line);
        Assert.Contains("Nobody is helping", line);
    }

    // -- recovery guidance integration (coop context surfaced, never overrides) --

    private static CartTelemetry StuckTelemetry(float grade)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 900f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(
            snapshot, true, 0.1f, 0f, gradeAvailable: true, grade, grade,
            TheConcernedCat.ConcernedTeamster.Domain.Terrain.GradeDirection.Climbing,
            TheConcernedCat.ConcernedTeamster.Domain.Terrain.TerrainSurfaceKind.Untouched, 10.0);
    }

    [Fact]
    public void Guidance_WithCrew_PrependsCoopLineAndKeepsPhysicalSteps()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ana", attached: true, contact: true, alignment: 0f, local: true),
            P("Bo", attached: false, contact: true, alignment: 0.8f),
        };
        RecoveryGuidanceViewModel vm = RecoveryGuidancePresenter.Present(
            new CartDiagnostic(CartDiagnosis.ImpossibleLoad, "load exceeds proven limit", "unload."),
            StuckTelemetry(18f), loadModel: null, brakeFeatureEnabled: false, participants: crew);

        Assert.True(vm.HasGuidance);
        Assert.StartsWith("Crew right now:", vm.Steps[0]);
        Assert.Contains("2 helping", vm.Steps[0]);
        Assert.Contains(vm.Steps, s => s.Contains("Extra hands will not beat this"));
        // The physical unload advice is still present — coop never replaces it.
        Assert.Contains(vm.Steps, s => s.Contains("unload") || s.Contains("Unload") || s.Contains("proven"));
    }

    [Fact]
    public void Guidance_WithoutCrew_IsUnchanged()
    {
        RecoveryGuidanceViewModel withNull = RecoveryGuidancePresenter.Present(
            new CartDiagnostic(CartDiagnosis.Obstruction, "near-level", "check wheels."),
            StuckTelemetry(1f), loadModel: null, brakeFeatureEnabled: false);
        RecoveryGuidanceViewModel withEmpty = RecoveryGuidancePresenter.Present(
            new CartDiagnostic(CartDiagnosis.Obstruction, "near-level", "check wheels."),
            StuckTelemetry(1f), loadModel: null, brakeFeatureEnabled: false,
            participants: new List<CoopParticipant>());

        Assert.DoesNotContain(withNull.Steps, s => s.StartsWith("Crew", System.StringComparison.Ordinal));
        Assert.DoesNotContain(withEmpty.Steps, s => s.StartsWith("Crew", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Guidance_CrewAllIdle_AddsNoCoopLine()
    {
        var crew = new List<CoopParticipant>
        {
            P("Ed", attached: false, contact: false, alignment: 0f),
        };
        RecoveryGuidanceViewModel vm = RecoveryGuidancePresenter.Present(
            new CartDiagnostic(CartDiagnosis.Obstruction, "near-level", "check wheels."),
            StuckTelemetry(1f), loadModel: null, brakeFeatureEnabled: false, participants: crew);

        Assert.DoesNotContain(vm.Steps, s => s.StartsWith("Crew", System.StringComparison.Ordinal));
    }
}
