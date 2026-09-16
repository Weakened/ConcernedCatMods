using TheConcernedCat.ConcernedCartographer.Companions;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>#305. Unity skins a mesh by bone INDEX, so the choice made here is
/// the difference between hair on a head and hair a metre above it.</summary>
public sealed class SkinBindingTests
{
    [Fact]
    public void AMeshWithTheBodysBoneCountTakesTheBodysArray()
    {
        // The vanilla case, and the one that was wrong. Long Braid and
        // Handlebar both report 53 bindposes against a 53-bone body; both are
        // authored against the player skeleton's array layout, so the body's
        // array is the answer for both. Name-matching produced a complete,
        // plausible, wrong binding for one of them.
        Assert.Equal(SkinBindingMode.AdoptBodySkeleton, SkinBinding.Decide(53, 53));
        Assert.Equal(SkinBindingMode.AdoptBodySkeleton, SkinBinding.Decide(1, 1));
    }

    [Fact]
    public void AMeshWithADifferentBoneCountCannotTakeItAndIsMatchedByName()
    {
        // Unity requires bones.Length == bindposes.Length, so the body's array
        // is not even assignable here. No vanilla customization item takes
        // this path; a modded one might.
        Assert.Equal(SkinBindingMode.MatchByName, SkinBinding.Decide(12, 53));
        Assert.Equal(SkinBindingMode.MatchByName, SkinBinding.Decide(54, 53));
    }

    [Fact]
    public void AMeshWithNoBindposesOrABodyWithNoBonesIsRefused()
    {
        // Half-binding a mesh looks like a different bug entirely, so neither
        // missing count is guessed around.
        Assert.Equal(SkinBindingMode.Refuse, SkinBinding.Decide(0, 53));
        Assert.Equal(SkinBindingMode.Refuse, SkinBinding.Decide(-1, 53));
        Assert.Equal(SkinBindingMode.Refuse, SkinBinding.Decide(53, 0));
    }
}
