using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>#243: the invariant that keeps a synthetic rudder write inside
/// the <c>Player.SetControls</c> call that authorised it. The runtime passes
/// the live <c>ShipControlls</c> instance as the controller identity; these
/// stand-ins are the same reference-identity contract, testable without
/// Unity.</summary>
public class SailingSteeringAuthorisationTests
{
    private sealed class FakeControls
    {
        public FakeControls(string name) => Name = name;

        public string Name { get; }
    }

    [Fact]
    public void NothingIsAuthorisedByDefault()
    {
        var authorisation = new SailingSteeringAuthorisation();

        Assert.False(authorisation.IsArmed);
        Assert.False(authorisation.TryConsume(new FakeControls("a"), out float rudder));
        Assert.Equal(0f, rudder);
    }

    [Fact]
    public void ArmingAuthorisesExactlyOneWrite()
    {
        var controls = new FakeControls("helm");
        var authorisation = new SailingSteeringAuthorisation();
        authorisation.Arm(controls, 0.42f);

        Assert.True(authorisation.IsArmed);
        Assert.True(authorisation.TryConsume(controls, out float first));
        Assert.Equal(0.42f, first);

        // The second attempt in the same or a later call gets nothing.
        Assert.False(authorisation.IsArmed);
        Assert.False(authorisation.TryConsume(controls, out float second));
        Assert.Equal(0f, second);
    }

    [Fact]
    public void AWriteCannotReachADifferentShip()
    {
        var mine = new FakeControls("mine");
        var other = new FakeControls("other");
        var authorisation = new SailingSteeringAuthorisation();
        authorisation.Arm(mine, 0.5f);

        Assert.False(authorisation.TryConsume(other, out float rudder));
        Assert.Equal(0f, rudder);

        // A mismatched attempt still consumes, so it cannot be retried with
        // the right instance afterwards.
        Assert.False(authorisation.IsArmed);
        Assert.False(authorisation.TryConsume(mine, out _));
    }

    [Fact]
    public void ClearingDropsAnUnconsumedAuthorisation()
    {
        var controls = new FakeControls("helm");
        var authorisation = new SailingSteeringAuthorisation();
        authorisation.Arm(controls, 1f);

        // This is what the raw-input observer does at the start of every call.
        authorisation.Clear();

        Assert.False(authorisation.IsArmed);
        Assert.False(authorisation.TryConsume(controls, out _));
    }

    [Fact]
    public void ArmingWithNoControllerAuthorisesNothing()
    {
        var authorisation = new SailingSteeringAuthorisation();
        authorisation.Arm(null, 0.9f);

        Assert.False(authorisation.IsArmed);
        Assert.False(authorisation.TryConsume(null, out float rudder));
        Assert.Equal(0f, rudder);
    }

    [Fact]
    public void ReArmingReplacesThePreviousAuthorisation()
    {
        var first = new FakeControls("first");
        var second = new FakeControls("second");
        var authorisation = new SailingSteeringAuthorisation();

        authorisation.Arm(first, 0.1f);
        authorisation.Arm(second, -0.7f);

        Assert.False(authorisation.TryConsume(first, out _));

        authorisation.Arm(second, -0.7f);
        Assert.True(authorisation.TryConsume(second, out float rudder));
        Assert.Equal(-0.7f, rudder);
    }

    [Fact]
    public void ASteeringSeamReachedWithoutTheObserverWritesNothing()
    {
        // Another mod calling ShipControlls.ApplyControlls directly, or any
        // path where vanilla never ran Player.SetControls, finds no
        // authorisation at all.
        var controls = new FakeControls("helm");
        var authorisation = new SailingSteeringAuthorisation();

        Assert.False(authorisation.TryConsume(controls, out float rudder));
        Assert.Equal(0f, rudder);
    }
}
