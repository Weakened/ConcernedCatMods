using System;
using System.Text;
using TheConcernedCat.ConcernedLadders.Domain.Animation;
using Xunit.Abstractions;

namespace TheConcernedCat.ConcernedLadders.Tests.Animation;

/// <summary>In game 2026-10-27: after climbing up then reversing, the right foot never planted during the descent.</summary>
public sealed class LimbGaitDescentTests
{
    private const float Pitch = 1f / 3f;
    private const float Base = 10f + Pitch / 2f;
    private readonly ITestOutputHelper _out;

    public LimbGaitDescentTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryLimbPlantsAgainWithinACycleAfterAReversal()
    {
        var gait = new LimbGait();
        Assert.True(gait.Bind(new GaitShape(), Base, Pitch, 0, 200, 1.38f, 12f));
        float feet = 12f;
        for (int i = 0; i < 250; i++) { feet += 0.018f; Assert.True(gait.Update(feet, 0.02f, false)); } // 4.5 m up at 0.9 m/s.
        for (int i = 0; i < 200; i++) { Assert.True(gait.Update(feet, 0.02f, true)); } // A stop: the game holds between the two.
        var log = new StringBuilder[4];
        for (int limb = 0; limb < 4; limb++) { log[limb] = new StringBuilder(); }
        for (int i = 0; i < 90; i++) // 1.6 m down: more than a cycle.
        {
            feet -= 0.018f;
            Assert.True(gait.Update(feet, 0.02f, false));
            for (int limb = 0; limb < 4; limb++) { log[limb].Append(gait.Contact(limb).Planted ? 'P' : '.'); }
        }

        for (int limb = 0; limb < 4; limb++)
        {
            _out.WriteLine(limb + " " + log[limb]);
            Assert.Contains('P', log[limb].ToString().Substring(30));
        }
    }
}
