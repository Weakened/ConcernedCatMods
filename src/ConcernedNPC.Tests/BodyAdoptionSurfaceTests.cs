using System;
using System.Linq;
using System.Reflection;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>That a role can actually build a body, and that it still cannot do
/// the things the widening was careful not to allow.
///
/// <b>Why this test exists at all.</b> The wave-two review found that the
/// library exported twenty-one types and declared a hundred and sixty-nine
/// internal ones, so the adoption instruction written in this leaf's own
/// handoff - call the prefab factory at plugin start - named a type no product
/// could see. The failure would have been discovered by a role agent in its
/// first hour, and the fix at that point is a public-surface change per role.
///
/// <b>What this file can and cannot prove.</b> It cannot prove "a consumer can
/// compile this", because the test project <i>links</i> the library's sources
/// rather than referencing the assembly, so internals are visible here in a way
/// they never are to a product. What it can do, and does, is assert the
/// accessibility of every member on the adoption path by reflection - which
/// fails if any of them is narrowed - and walk the whole path end to end so the
/// sequence is executable rather than described. <c>PublicSurfaceTests</c> pins
/// the type list; this pins the members that make the type list useful.</summary>
public sealed class BodyAdoptionSurfaceTests : IDisposable
{
    public BodyAdoptionSurfaceTests() => BodyFixtures.ResetWorld();

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void A_role_can_reach_every_step_of_building_a_body()
    {
        // Exactly the sequence a role's plugin writes, and nothing on it may be
        // internal. Each entry is one call in that sequence.
        AssertPublic(typeof(NpcWorkerPrefabFactory), "TryFor");
        AssertPublic(typeof(NpcWorkerPrefabOptions), "Keeping");
        AssertPublic(typeof(NpcWorkerPrefabOptions), "Named");
        AssertPublic(typeof(NpcWorkerPrefabFactory), "Install");
        AssertPublic(typeof(NpcWorkerPrefabFactory), "TrySpawn");
        AssertPublic(typeof(NpcWorldBodies), "TryFor");
        AssertPublic(typeof(NpcWorldBodies), "Restart");
        AssertPublic(typeof(NpcWorldBodies), "Advance");
        AssertPublic(typeof(NpcWorldBodies), "TallyFor");
        AssertPublic(typeof(NpcWorldBodies), "Forget");
        AssertPublic(typeof(NpcBody), "FindLive");
        AssertPublic(typeof(NpcBody), "ForgetAll");
        AssertPublic(typeof(NpcBody), "TryPersist");
        AssertPublic(typeof(NpcPresentationBody), "TryExtract");
    }

    [Fact]
    public void And_still_cannot_reach_past_any_of_the_rules()
    {
        // Every one of these would let a caller around something the widening
        // was for. Widening one is a deliberate act with a consequence, so each
        // is named with its consequence.
        AssertNotPublic(
            typeof(NpcWorkerPrefabFactory), "Prefab",
            "handing out the built prefab ends the rule that bodies are created in one place");
        AssertNotPublic(
            typeof(NpcWorkerPrefabFactory), "TryCreate",
            "a caller that can force the build can build after a world's objects were created");
        AssertNotPublic(
            typeof(NpcBody), "TryStamp",
            "only the factory that just built a body may write an identity into it");
        AssertNotPublic(
            typeof(NpcBody), "View",
            "a role holding the network view can write into an object only the body may write");
        AssertNotPublic(
            typeof(NpcBodyMind), "RememberIdentity",
            "a mind answering with an identity its own object does not carry disagrees with the census");
        AssertNotPublic(
            typeof(NpcBodyTally), "Of",
            "a fabricated tally saying Missing builds a second body for an identity that has one");
        AssertNotPublic(
            typeof(NpcBodyTally), "NotYetRun",
            "same, from the other direction");
    }

    [Fact]
    public void A_tally_nobody_counted_permits_nothing()
    {
        // The one construction the language always allows on a public struct.
        NpcBodyTally never = default;

        Assert.Equal(NpcBodyPresence.Searching, never.Presence);
        Assert.False(never.MaySpawn);
    }

    [Fact]
    public void Options_nobody_filled_in_are_refused_rather_than_defaulted()
    {
        NpcWorkerPrefabOptions never = default;

        Assert.False(NpcWorkerPrefabFactory.TryFor(
            BodyFixtures.ForemanContract(), never, out _, out string reason));
        Assert.Contains("must say what its body stores", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_adoption_sequence_runs_end_to_end()
    {
        // The plugin-start half.
        ZDOMan.instance = new ZDOMan();
        GameObject creature = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(creature);

        Assert.True(NpcWorkerPrefabFactory.TryFor(
            BodyFixtures.ForemanContract(),
            NpcWorkerPrefabOptions.Keeping(NpcBodyKeeps.IdentityAndInventory).Named("Thorstein"),
            out NpcWorkerPrefabFactory? factory,
            out string reason), reason);

        factory!.Install("Dverger", _ => { });
        Jotunn.Managers.PrefabManager.RaiseVanillaPrefabsAvailable();
        Assert.True(factory.IsReady, factory.LastFailure);

        // The world-load half.
        Assert.True(NpcWorldBodies.TryFor(
            BodyFixtures.ForemanContract(),
            NpcBodyKeeps.IdentityAndInventory,
            out NpcWorldBodies? census,
            out reason), reason);

        census!.Restart();
        Assert.True(census.RunToCompletion());

        var registry = new NpcRoleRegistry();
        registry.Register(new FakeRole(BodyFixtures.Thorstein, factory.Contract));
        registry.BeginWorldLoad(out _);
        BodyClaim claim = registry.TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Worker, "settlement");
        Assert.True(claim.IsGranted, claim.Reason);

        NpcBodyTally tally = census.TallyFor(BodyFixtures.Thorstein, loadedBodies: 0, loadedIsFaulted: false);
        Assert.True(tally.MaySpawn);

        NpcBodyMind? mind = factory.TrySpawn(
            claim.Lease, tally, Vector3.zero, Quaternion.identity, out string failure);

        Assert.NotNull(mind);
        Assert.Equal(string.Empty, failure);
        Assert.Equal(BodyFixtures.Thorstein, mind!.Identity);

        // And the role drives it through the motor and nothing else.
        INpcBodyMotor motor = mind;
        motor.Halt();
        Assert.False(motor.MotorCommanded);
    }

    private static void AssertPublic(Type type, string member)
    {
        Assert.True(type.IsPublic, type.FullName + " is not public, so no product can name it.");

        MemberInfo[] found = type.GetMember(
            member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        Assert.True(
            found.Length > 0,
            type.FullName + "." + member + " is not publicly reachable. It is on the documented path a "
            + "role walks to build a body, so a product cannot adopt this library without it - which is "
            + "exactly the defect the wave-two review found.");
    }

    private static void AssertNotPublic(Type type, string member, string consequence)
    {
        MemberInfo[] found = type.GetMember(
            member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        Assert.True(
            found.Length == 0,
            type.FullName + "." + member + " became public. That is a decision, not a fix: "
            + consequence + ". If it is genuinely wanted, say so here and in the type's own comment, "
            + "and price the version bump.");
    }

    /// <summary>The body half of the surface is exactly the twelve types named
    /// here - the same list <c>PublicSurfaceTests</c> pins, asserted from the
    /// other direction so a thirteenth public type under <c>Body/</c> fails
    /// here too, with a message about what widening costs rather than about a
    /// list being out of date.</summary>
    [Fact]
    public void The_body_half_of_the_surface_is_twelve_types()
    {
        string[] expected =
        {
            nameof(INpcBodyMotor),
            nameof(NpcBody),
            nameof(NpcBodyKeeps),
            nameof(NpcBodyMind),
            nameof(NpcBodyPresence),
            nameof(NpcBodyTally),
            nameof(NpcDroppedItem),
            nameof(NpcPresentationBody),
            nameof(NpcPresentationFigure),
            nameof(NpcWorkerPrefabFactory),
            nameof(NpcWorkerPrefabOptions),
            nameof(NpcWorldBodies),
        };

        string[] actual = typeof(NpcWorkerPrefabFactory).Assembly
            .GetExportedTypes()
            .Where(type => string.Equals(
                type.Namespace, typeof(NpcWorkerPrefabFactory).Namespace, StringComparison.Ordinal))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "The body half of the public surface changed.\nExpected:\n  " + string.Join("\n  ", expected)
            + "\nFound:\n  " + string.Join("\n  ", actual)
            + "\n\nEvery type here is frozen under the major-version-plus-pin rule from the first "
            + "consumer onwards. Adding one means saying which role needs it.");
    }
}
