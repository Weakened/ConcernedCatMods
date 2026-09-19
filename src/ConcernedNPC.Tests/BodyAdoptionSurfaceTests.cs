using System;
using System.Collections.Generic;
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
        AssertPublic(typeof(NpcBody), "LiveFor");
        AssertPublic(typeof(NpcBody), "ForgetAll");
        AssertPublic(typeof(NpcBody), "TryPersist");
        AssertPublic(typeof(NpcBodyMind), "LiveFor");
        AssertPublic(typeof(NpcBodyMind), "TryDrive");
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

        // Every role's bodies in one list, handed across an assembly boundary
        // with a doc comment asking each consumer to filter it, was the review's
        // second major. LiveFor does the filtering.
        AssertNotPublic(
            typeof(NpcBody), "Live",
            "every role's bodies in one public list makes filtering by prefab advice a role may ignore");
        AssertNotPublic(
            typeof(NpcBodyMind), "Live",
            "same, and a mind is the half that can be driven");

        // The driving verbs. Public on the mind, they made holding a mind and
        // being allowed to walk it the same thing.
        AssertNotPublic(
            typeof(NpcBodyMind), "WalkTo",
            "a public driving verb lets any consumer holding a mind walk another product's worker");
        AssertNotPublic(typeof(NpcBodyMind), "SteerToward", "same");
        AssertNotPublic(typeof(NpcBodyMind), "Face", "same");
        AssertNotPublic(typeof(NpcBodyMind), "Halt", "same");
        AssertNotPublic(
            typeof(NpcBodyMind), "TryFindPath",
            "the expensive one, and a path search on somebody else's body is still a cost they pay");
    }

    /// <summary>The four process-wide callbacks are events, so the second
    /// product to load cannot silently unhook the first.
    ///
    /// <b>Why this is asserted by shape and not only by behaviour.</b>
    /// <c>+=</c> combines on a settable property too, so a test that subscribes
    /// twice and checks both fire passes against the broken version. What is
    /// broken is that <c>=</c> compiles - the obvious reading of a setter - and
    /// that a consumer has no way to detach. Only the declaration says
    /// that.</summary>
    [Fact]
    public void Every_shared_callback_is_an_event_rather_than_a_slot_one_product_can_take()
    {
        AssertIsEvent(typeof(NpcBody), "Loaded");
        AssertIsEvent(typeof(NpcBody), "Died");
        AssertIsEvent(typeof(NpcBody), "ErrorLog");
        AssertIsEvent(typeof(NpcBodyMind), "ErrorLog");
    }

    [Fact]
    public void A_second_product_subscribing_never_costs_the_first_its_event()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());

        var foreman = new List<NpcBody>();
        var steward = new List<NpcBody>();
        NpcBody.Loaded += foreman.Add;
        NpcBody.Loaded += steward.Add;

        BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);

        Assert.Single(foreman);
        Assert.Single(steward);
    }

    [Fact]
    public void A_subscriber_that_throws_costs_only_itself_and_not_the_body()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());

        var errors = new List<string>();
        var second = new List<NpcBody>();
        NpcBody.ErrorLog += errors.Add;
        NpcBody.Loaded += _ => throw new InvalidOperationException("one product's binding is broken");
        NpcBody.Loaded += second.Add;

        NpcBody body = BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);

        Assert.Single(second);
        Assert.NotEmpty(errors);

        // And the body is still loaded: a consumer's exception out of the event
        // used to come back through the body's own failure path and report it
        // inert.
        Assert.True(body.IsLoaded);
        Assert.Null(body.Fault);
    }

    [Fact]
    public void A_role_reaches_its_own_bodies_and_not_another_roles()
    {
        // Two roles, two prefabs, one shared key prefix and the same identity
        // text in both - the case the census exists for, now asked of the two
        // public ways a consumer reaches a live body.
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        BodyFixtures.Register(BodyFixtures.TeamsterContract(), NpcBodyKeeps.IdentityOnly);

        BodyFixtures.SavedBody(BodyFixtures.ForemanPrefab, Stamped);
        BodyFixtures.SavedBody(BodyFixtures.TeamsterPrefab, Stamped);
        NpcBodyMind foremanMind = LiveMind(BodyFixtures.ForemanPrefab);
        LiveMind(BodyFixtures.TeamsterPrefab);

        IReadOnlyList<NpcBody> bodies = NpcBody.LiveFor(BodyFixtures.ForemanContract());
        Assert.Single(bodies);
        Assert.Equal(BodyFixtures.ForemanPrefab, bodies[0].PrefabName);

        IReadOnlyList<NpcBodyMind> minds = NpcBodyMind.LiveFor(BodyFixtures.ForemanContract());
        Assert.Single(minds);
        Assert.Same(foremanMind, minds[0]);
    }

    [Fact]
    public void A_body_cannot_be_driven_without_the_lease_the_arbiter_granted()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBodyMind mind = LiveMind(BodyFixtures.ForemanPrefab);
        mind.m_nview!.GetZDO().Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");

        // There is no cast to a motor, which is why the mind does not implement
        // the interface: an explicit implementation would still be reachable.
        Assert.False(typeof(INpcBodyMotor).IsAssignableFrom(typeof(NpcBodyMind)));

        Assert.False(mind.TryDrive(null, out INpcBodyMotor? none, out string reason));
        Assert.Null(none);
        Assert.Contains("no lease", reason, StringComparison.Ordinal);

        // A lease for somebody else's identity is not permission for this body.
        var registry = new NpcRoleRegistry();
        registry.Register(new FakeRole(BodyFixtures.Gunnar, BodyFixtures.TeamsterContract()));
        registry.BeginWorldLoad(out _);
        BodyLease elsewhere = registry
            .TryClaimBody(BodyFixtures.Gunnar, NpcBodyKind.Worker, "teamster").Lease!;

        Assert.False(mind.TryDrive(elsewhere, out _, out reason));
        Assert.Contains("the lease is for", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_motor_stops_permitting_anything_the_moment_its_lease_ends()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        NpcBodyMind mind = LiveMind(BodyFixtures.ForemanPrefab);
        mind.m_nview!.GetZDO().Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");

        var registry = new NpcRoleRegistry();
        registry.Register(new FakeRole(BodyFixtures.Thorstein, BodyFixtures.ForemanContract()));
        registry.BeginWorldLoad(out _);
        BodyLease lease = registry
            .TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Worker, "settlement").Lease!;

        Assert.True(mind.TryDrive(lease, out INpcBodyMotor? motor, out string reason), reason);
        motor!.WalkTo(new Vector3(3f, 0f, 0f), 2f);
        Assert.Equal(1, mind.Moves);
        Assert.True(motor.MotorCommanded);

        // The case a check taken once when the motor was handed out cannot see.
        lease.Dispose();
        motor.WalkTo(new Vector3(9f, 0f, 0f), 2f);

        Assert.Equal(1, mind.Moves);
        Assert.False(motor.IsOwnedAndValid);

        // And the body was stopped rather than left walking: vanilla never
        // clears a direction by itself.
        Assert.False(mind.MotorCommanded);
        Assert.Equal(1, mind.Stops);

        Assert.False(mind.TryDrive(lease, out _, out reason));
        Assert.Contains("no longer held", reason, StringComparison.Ordinal);
    }

    private static void Stamped(ZDO zdo) => zdo.Set(BodyFixtures.WorkerKeyField, "foreman/thorstein");

    private static NpcBodyMind LiveMind(string prefabName)
    {
        var instance = new GameObject(prefabName + "(Clone)");
        ZNetView view = instance.Add(new ZNetView());
        Character character = instance.Add(new Character());
        NpcBodyMind mind = instance.Add(new NpcBodyMind());
        mind.m_nview = view;
        mind.m_character = character;
        mind.OnEnable();
        return mind;
    }

    private static void AssertIsEvent(Type type, string member)
    {
        Assert.True(
            type.GetEvent(member, BindingFlags.Public | BindingFlags.Static) != null,
            type.FullName + "." + member + " is not a public static event. It must be one: with several "
            + "products loading this library, a settable property means the second to load writes "
            + member + " = handler and silently unhooks the first, whose NPC then stands inert in a "
            + "world where its body demonstrably exists - and no consumer has any way to detach at "
            + "teardown.");

        Assert.True(
            type.GetProperty(member, BindingFlags.Public | BindingFlags.Static) == null
            && type.GetField(member, BindingFlags.Public | BindingFlags.Static) == null,
            type.FullName + "." + member + " is also reachable as a property or a field, so assignment "
            + "still compiles and the clobbering version is still writable.");
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

        // And the role drives it through the motor and nothing else - which it
        // gets by showing the same lease it built the body with, not by casting
        // the mind it was handed.
        Assert.True(mind.TryDrive(claim.Lease, out INpcBodyMotor? motor, out string refusal), refusal);
        motor!.Halt();
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
