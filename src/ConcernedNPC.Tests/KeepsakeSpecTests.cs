using System;
using TheConcernedCat.ConcernedNPC.Keepsakes;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The game-free half of the shared companion keepsake (#295 S2): what
/// a role may ask for. The object itself is game-bound and excluded from this
/// project; its engine writes are pinned by the validator's NPC keepsake and
/// figure-parts audits.</summary>
public sealed class KeepsakeSpecTests
{
    [Fact]
    public void A_spec_keeps_what_the_role_asked_for()
    {
        var spec = new NpcKeepsakeSpec("  Worn harness buckle ", " Examine ", new[] { "Bronze", "Iron" }, 0.8f);

        Assert.Equal("Worn harness buckle", spec.HoverName);
        Assert.Equal("Examine", spec.HoverVerb);
        Assert.Equal(new[] { "Bronze", "Iron" }, spec.PrefabCandidates);
        Assert.Equal(0.8f, spec.Scale);
    }

    [Fact]
    public void A_spec_without_a_name_a_verb_or_a_look_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new NpcKeepsakeSpec(" ", "Examine", new[] { "Hammer" }, 1f));
        Assert.Throws<ArgumentException>(() => new NpcKeepsakeSpec("Square", "", new[] { "Hammer" }, 1f));
        Assert.Throws<ArgumentException>(() => new NpcKeepsakeSpec("Square", "Examine", Array.Empty<string>(), 1f));
        Assert.Throws<ArgumentException>(() => new NpcKeepsakeSpec("Square", "Examine", null!, 1f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(10.5f)]
    [InlineData(float.NaN)]
    public void A_spec_scale_outside_the_range_is_refused(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NpcKeepsakeSpec("Square", "Examine", new[] { "Hammer" }, scale));
    }
}
