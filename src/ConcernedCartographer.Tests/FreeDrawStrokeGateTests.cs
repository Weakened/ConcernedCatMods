using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC11 blocker 4 regressions: Free Draw may only create a route
/// for a REAL stroke, and pointer-over-UI frames (held=false) end strokes
/// without leaving fragments.</summary>
public class FreeDrawStrokeGateTests
{
    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    [Fact]
    public void ClickTwitch_NeverCreatesARoute()
    {
        var gate = new FreeDrawStrokeGate();

        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(true, P(0f, 0f)).Kind);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(true, P(0.3f, 0f)).Kind);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.DropBuffer, gate.Observe(false, P(0.3f, 0f)).Kind);
        Assert.False(gate.StrokeLive);
    }

    [Fact]
    public void RealStroke_StartsOnceTravelReachesSpacing_AndCarriesTheFirstPoint()
    {
        var gate = new FreeDrawStrokeGate();

        gate.Observe(true, P(0f, 0f));
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(true, P(1.9f, 0f)).Kind);

        FreeDrawStrokeGate.Decision start = gate.Observe(true, P(2.1f, 0f));
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.StartStroke, start.Kind);
        Assert.Equal(0f, start.StrokeStart.X);
        Assert.True(gate.StrokeLive);

        Assert.Equal(FreeDrawStrokeGate.DecisionKind.Append, gate.Observe(true, P(4f, 0f)).Kind);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.EndStroke, gate.Observe(false, P(4f, 0f)).Kind);
        Assert.False(gate.StrokeLive);
    }

    [Fact]
    public void PointerOverUi_MidStroke_EndsIt_AndReturningStartsFresh()
    {
        var gate = new FreeDrawStrokeGate();
        gate.Observe(true, P(0f, 0f));
        gate.Observe(true, P(3f, 0f));
        Assert.True(gate.StrokeLive);

        // The runtime feeds held=false while the pointer covers CC UI.
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.EndStroke, gate.Observe(false, P(3f, 0f)).Kind);

        // Back over the map: a fresh buffer, and a fresh gate — a twitch
        // here still creates nothing.
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(true, P(10f, 0f)).Kind);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.DropBuffer, gate.Observe(false, P(10f, 0f)).Kind);
    }

    [Fact]
    public void HoldWithoutMovement_NeverCreates_NoMatterHowLong()
    {
        var gate = new FreeDrawStrokeGate();
        for (int frame = 0; frame < 300; frame++)
        {
            Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(true, P(0.5f, 0.5f)).Kind);
        }

        Assert.Equal(FreeDrawStrokeGate.DecisionKind.DropBuffer, gate.Observe(false, P(0.5f, 0.5f)).Kind);
    }

    [Fact]
    public void Reset_ForgetsBufferAndStroke_Silently()
    {
        var gate = new FreeDrawStrokeGate();
        gate.Observe(true, P(0f, 0f));
        gate.Reset();
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(false, P(0f, 0f)).Kind);

        gate.Observe(true, P(0f, 0f));
        gate.Observe(true, P(5f, 0f));
        gate.Reset();
        Assert.False(gate.StrokeLive);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(false, P(5f, 0f)).Kind);
    }

    [Fact]
    public void IdleFrames_DoNothing()
    {
        var gate = new FreeDrawStrokeGate();
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(false, P(0f, 0f)).Kind);
        Assert.Equal(FreeDrawStrokeGate.DecisionKind.None, gate.Observe(false, P(1f, 0f)).Kind);
    }
}
