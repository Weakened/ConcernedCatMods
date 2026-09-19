using System.Reflection;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>What this package promises not to break.
///
/// <b>Why a hard-coded list rather than a judgement.</b> This is a library, and
/// the conventions say a change to its public surface is a major version bump
/// plus a pin bump in every consumer, together, in one change - because from the
/// first consumer onwards, widening or narrowing this surface can break a mod
/// that is already installed. Nothing else in the repository notices such a
/// change: a leaf that reaches for <c>public</c> to fix a compile error in a
/// later issue would ship a breaking change with a completely green gate.
///
/// So the surface is written down. Editing this list is the deliberate act; the
/// failure message says what editing it costs.</summary>
public class PublicSurfaceTests
{
    /// <summary>Every type a consumer can see, sorted, and each is here because
    /// a role genuinely needs it. Three groups:
    ///
    /// <b>Registration.</b> The identity and the contract a role declares
    /// (<c>NpcIdentity</c>, <c>INpcRole</c>, <c>INpcDataPaths</c>,
    /// <c>NpcBodyContract</c>, <c>NpcBodyKind</c>); the registry it declares
    /// itself to and the outcome it gets back (<c>NpcRoleRegistry</c>,
    /// <c>RoleRegistration</c>, <c>RoleRegistrationStatus</c>); the world load
    /// it announces (<c>NpcWorldEpoch</c>); and the body claim with the lease a
    /// grant carries (<c>BodyClaim</c>, <c>BodyClaimStatus</c>,
    /// <c>BodyLease</c>).
    ///
    /// <b>Work areas.</b> The provider side a role registers and the resolution
    /// it gets back (<c>INpcWorkArea</c>, <c>INpcWorkAreaProvider</c>,
    /// <c>NpcWorkAreaDescriptor</c>, <c>NpcWorkAreaId</c>,
    /// <c>NpcWorkAreaRegistry</c>, <c>NpcWorkAreaResult</c>,
    /// <c>ProviderRegistration</c>, <c>WorkAreaResolution</c>, <c>NpcPoint</c>).
    ///
    /// <b>Building a body.</b> The entry point and its inputs and outputs, and
    /// nothing else: the factory (<c>NpcWorkerPrefabFactory</c>), what it is
    /// configured with (<c>NpcWorkerPrefabOptions</c>, <c>NpcBodyKeeps</c>),
    /// the census that produces the only honest spawn input
    /// (<c>NpcWorldBodies</c>, <c>NpcBodyTally</c>, <c>NpcBodyPresence</c>),
    /// what it returns and what a role then reads and drives
    /// (<c>NpcBodyMind</c>, <c>INpcBodyMotor</c>, <c>NpcBody</c>,
    /// <c>NpcDroppedItem</c>), and the other kind of body
    /// (<c>NpcPresentationBody</c>, <c>NpcPresentationFigure</c>).
    ///
    /// <b>Running a job.</b> The driver a role pumps and what it hands back
    /// (<c>NpcJobDriver</c>, <c>NpcJobOrder</c>, <c>INpcJobRole</c>,
    /// <c>NpcJobProgress</c>, <c>NpcJobAdvance</c>); what a role describes its
    /// work with (<c>JobTarget</c>, <c>JobManifest</c>, <c>JobManifestLine</c>,
    /// <c>SourceStock</c>, <c>StockLine</c>, <c>NpcCarryCapacity</c>,
    /// <c>JobStepActions</c>); what it carries out and branches on
    /// (<c>PlannedStep</c>, <c>JobStep</c>, <c>JobPlanVerdict</c>,
    /// <c>JobReconciliation</c>, <c>AreaScanReport</c>,
    /// <c>AreaScanOutcome</c>); the completion condition it answers
    /// (<c>IStopObserver</c>, <c>RouteStop</c>, <c>StopStatus</c>); the ground
    /// it reads (<c>INpcAreaProbe</c>, <c>AreaSample</c>,
    /// <c>AreaSampleVerdict</c>, <c>AreaRejection</c>); the chests it offers
    /// (<c>INpcContainer</c>, <c>NpcContainerAccess</c>,
    /// <c>NpcContainerUse</c>, <c>NpcContainerRefusal</c>,
    /// <c>INpcEpochScoped</c>); and what other jobs have set aside
    /// (<c>INpcSourceAvailability</c>).
    ///
    /// <b>What is still internal, and why that is not an oversight.</b> The
    /// arbiter, the mode owner, the slug rules, the build gate, the key
    /// composition, the prefab-to-contract table, the sidecar and the atomic
    /// write. Members too: the built prefab, the eager build, the identity
    /// stamp, the network view and the tally's own constructors, each because
    /// handing it over would let a caller reach past a rule the type exists to
    /// keep. The whole of <c>Custody/</c> and <c>Storage/</c>, which the job
    /// pipeline never touches. And the sequencing itself - the snapshot
    /// builder, the tour planner, the tour plan, the partitioner, the source
    /// selector, the manifest arithmetic, the budget, the commitments, the
    /// reservation books, the stop sequencer and the route execution - because
    /// <c>NpcJobDriver</c> is the door to all of it and a role that named them
    /// would be a role sequencing a job for itself, three times, differently.
    /// The rule for adding to this list has not changed - a leaf says which
    /// role needs it and accepts the version bump - and the reason the body
    /// group arrives in one edit rather than three is that three role leaves
    /// discovering it one at a time is three bumps.</summary>
    private static readonly string[] Expected =
    {
        "TheConcernedCat.ConcernedNPC.Bodies.BodyClaim",
        "TheConcernedCat.ConcernedNPC.Bodies.BodyClaimStatus",
        "TheConcernedCat.ConcernedNPC.Bodies.BodyLease",
        "TheConcernedCat.ConcernedNPC.Body.INpcBodyMotor",
        "TheConcernedCat.ConcernedNPC.Body.NpcBody",
        "TheConcernedCat.ConcernedNPC.Body.NpcBodyKeeps",
        "TheConcernedCat.ConcernedNPC.Body.NpcBodyMind",
        "TheConcernedCat.ConcernedNPC.Body.NpcBodyPresence",
        "TheConcernedCat.ConcernedNPC.Body.NpcBodyTally",
        "TheConcernedCat.ConcernedNPC.Body.NpcDroppedItem",
        "TheConcernedCat.ConcernedNPC.Body.NpcPresentationBody",
        "TheConcernedCat.ConcernedNPC.Body.NpcPresentationFigure",
        "TheConcernedCat.ConcernedNPC.Body.NpcWorkerPrefabFactory",
        "TheConcernedCat.ConcernedNPC.Body.NpcWorkerPrefabOptions",
        "TheConcernedCat.ConcernedNPC.Body.NpcWorldBodies",
        "TheConcernedCat.ConcernedNPC.Containers.INpcContainer",
        "TheConcernedCat.ConcernedNPC.Containers.NpcContainerAccess",
        "TheConcernedCat.ConcernedNPC.Containers.NpcContainerRefusal",
        "TheConcernedCat.ConcernedNPC.Containers.NpcContainerUse",
        "TheConcernedCat.ConcernedNPC.Jobs.INpcJobRole",
        "TheConcernedCat.ConcernedNPC.Jobs.NpcJobAdvance",
        "TheConcernedCat.ConcernedNPC.Jobs.NpcJobDriver",
        "TheConcernedCat.ConcernedNPC.Jobs.NpcJobOrder",
        "TheConcernedCat.ConcernedNPC.Jobs.NpcJobProgress",
        "TheConcernedCat.ConcernedNPC.Planning.INpcSourceAvailability",
        "TheConcernedCat.ConcernedNPC.Planning.JobManifest",
        "TheConcernedCat.ConcernedNPC.Planning.JobManifestLine",
        "TheConcernedCat.ConcernedNPC.Planning.JobPlanVerdict",
        "TheConcernedCat.ConcernedNPC.Planning.JobReconciliation",
        "TheConcernedCat.ConcernedNPC.Planning.JobStep",
        "TheConcernedCat.ConcernedNPC.Planning.JobStepActions",
        "TheConcernedCat.ConcernedNPC.Planning.JobTarget",
        "TheConcernedCat.ConcernedNPC.Planning.NpcCarryCapacity",
        "TheConcernedCat.ConcernedNPC.Planning.PlannedStep",
        "TheConcernedCat.ConcernedNPC.Planning.SourceStock",
        "TheConcernedCat.ConcernedNPC.Planning.StockLine",
        "TheConcernedCat.ConcernedNPC.Roles.INpcDataPaths",
        "TheConcernedCat.ConcernedNPC.Roles.INpcRole",
        "TheConcernedCat.ConcernedNPC.Roles.NpcBodyContract",
        "TheConcernedCat.ConcernedNPC.Roles.NpcBodyKind",
        "TheConcernedCat.ConcernedNPC.Roles.NpcIdentity",
        "TheConcernedCat.ConcernedNPC.Roles.NpcRoleRegistry",
        "TheConcernedCat.ConcernedNPC.Roles.RoleRegistration",
        "TheConcernedCat.ConcernedNPC.Roles.RoleRegistrationStatus",
        "TheConcernedCat.ConcernedNPC.Routing.IStopObserver",
        "TheConcernedCat.ConcernedNPC.Routing.RouteStop",
        "TheConcernedCat.ConcernedNPC.Routing.StopStatus",
        "TheConcernedCat.ConcernedNPC.Work.AreaRejection",
        "TheConcernedCat.ConcernedNPC.Work.AreaSample",
        "TheConcernedCat.ConcernedNPC.Work.AreaSampleVerdict",
        "TheConcernedCat.ConcernedNPC.Work.AreaScanOutcome",
        "TheConcernedCat.ConcernedNPC.Work.AreaScanReport",
        "TheConcernedCat.ConcernedNPC.Work.INpcAreaProbe",
        "TheConcernedCat.ConcernedNPC.Work.INpcEpochScoped",
        "TheConcernedCat.ConcernedNPC.Work.INpcWorkArea",
        "TheConcernedCat.ConcernedNPC.Work.INpcWorkAreaProvider",
        "TheConcernedCat.ConcernedNPC.Work.NpcPoint",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorkAreaDescriptor",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorkAreaId",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorkAreaRegistry",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorkAreaResult",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorldEpoch",
        "TheConcernedCat.ConcernedNPC.Work.ProviderRegistration",
        "TheConcernedCat.ConcernedNPC.Work.WorkAreaResolution",
    };

    [Fact]
    public void The_public_surface_is_exactly_what_is_written_down()
    {
        // Every exported type, not merely the ones under the expected prefix: a
        // public type in some other namespace is exactly the escape this test
        // exists to catch, and a prefix filter would hide it.
        //
        // Two namespaces are excluded, and only these two. This assembly links
        // the library's sources rather than referencing it, which is what makes
        // "public" here mean the same thing it will mean to a consumer - and it
        // also means the stand-ins that let the game-bound half compile without
        // the game are in the same assembly. They are not part of anybody's
        // surface. The list is written out so that adding a third stub
        // namespace is a deliberate edit to this test rather than a quiet
        // widening of what may escape it.
        // The stand-ins come in two shapes: the modding frameworks, which have
        // namespaces, and the game's own types, which have none at all.
        string[] stubNamespaces = { "Jotunn.", "UnityEngine." };

        // The namespace-less shape was excluded and not pinned, which left the
        // exact escape the widening was for: a library file that forgets its
        // namespace line and declares a public type lands in the global
        // namespace and was filtered out unexamined. So that category is
        // written down too, and asserted to be exactly the game stand-ins.
        string[] globalStubs =
        {
            "BaseAI", "Character", "Character+Faction", "CharacterAnimEvent", "CharacterDrop",
            "CharacterTimedDestruction", "EffectList", "Growup", "Humanoid", "Humanoid+ItemSet",
            "Humanoid+RandomItem", "Inventory", "ItemDrop", "ItemDrop+ItemData",
            "ItemDrop+ItemData+SharedData", "NpcTalk", "Pathfinding", "Pathfinding+AgentType", "Player",
            "Procreation", "Sadle", "StubOwnership", "Tameable", "VisEquipment", "ZDO", "ZDOID", "ZDOMan",
            "ZNetScene", "ZNetView", "ZPackage", "ZSyncAnimation", "ZoneSystem",
        };

        string[] global = typeof(NpcRoleRegistry).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == null && type.FullName != null)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            globalStubs.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(global, StringComparer.Ordinal),
            "The exported types in the global namespace changed.\nExpected:\n  "
            + string.Join("\n  ", globalStubs.OrderBy(name => name, StringComparer.Ordinal))
            + "\nFound:\n  " + string.Join("\n  ", global)
            + "\n\nThese are the game's own types, stood in for so the game-bound half compiles without "
            + "the game, and they are pinned because the alternative is that a library file which forgot "
            + "its namespace line could declare a public type and escape this test entirely.");

        string[] actual = typeof(NpcRoleRegistry).Assembly
            .GetExportedTypes()
            .Where(type => type.FullName != null
                && type.Namespace != null
                && !type.FullName.StartsWith("TheConcernedCat.ConcernedNPC.Tests.", StringComparison.Ordinal)
                && !stubNamespaces.Any(stub => type.FullName!.StartsWith(stub, StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            Expected.SequenceEqual(actual, StringComparer.Ordinal),
            "The public surface of ConcernedNPC changed.\n" +
            "Expected:\n  " + string.Join("\n  ", Expected) + "\n" +
            "Found:\n  " + string.Join("\n  ", actual) + "\n\n" +
            "This is not a test to edit on the way past. A library's public surface is what an " +
            "already-installed consumer compiled against, so changing it means a MAJOR version bump in " +
            "src/ConcernedNPC/ConcernedNPC.csproj and Package/thunderstore.toml AND a pin bump in every " +
            "consumer's thunderstore.toml, together, in one change - see the 'Library packages' section of " +
            "docs/NAMING_CONVENTIONS.md. If that is what you mean to do, update this list in the same commit.");
    }

    [Fact]
    public void Nothing_that_could_forge_a_grant_can_be_constructed_from_outside()
    {
        // A public constructor on any of these would let a consumer manufacture
        // permission it was refused: a second registry is a second arbiter, and
        // a forged claim or lease is a body built against the never-coexist
        // rule.
        AssertNoPublicConstructor(typeof(NpcRoleRegistry));
        AssertNoPublicConstructor(typeof(Bodies.BodyClaim));
        AssertNoPublicConstructor(typeof(Bodies.BodyLease));
        AssertNoPublicConstructor(typeof(RoleRegistration));

        // And the two named factories are the only public way to a contract, so
        // a presentation body cannot be given durable facts by composing one.
        Assert.Null(typeof(NpcBodyContract).GetMethod(
            "Compose", BindingFlags.Public | BindingFlags.Static));

        // The same rule one tier down, for the two types the body half made
        // public. A prefab factory built outside its own TryFor would never
        // have recorded its contract, so bodies of its prefab would wake with
        // no key names and write nothing; a census built outside its TryFor
        // would never have validated the contract, so it would read a key
        // nobody wrote, find no bodies, and permit a second one.
        AssertNoPublicConstructor(typeof(Body.NpcWorkerPrefabFactory));
        AssertNoPublicConstructor(typeof(Body.NpcWorldBodies));

        // And the same rule for the job surface. Each of these is something a
        // role OBTAINS from the pipeline rather than states, and each one a
        // role could mint would be a claim about work nobody did: a step the
        // plan never wrote, a stop the planner never ordered, a report about a
        // look nobody took, books for a round that never ran, or an
        // instruction the driver never gave. The driver itself is reached
        // through NpcJobDriver.For, which is what makes "an order that cannot
        // be worked comes back already stopped" true of every driver rather
        // than of the ones that happened to go through the factory.
        AssertNoPublicConstructor(typeof(Planning.JobStep));
        AssertNoPublicConstructor(typeof(Planning.PlannedStep));
        AssertNoPublicConstructor(typeof(Planning.JobReconciliation));
        AssertNoPublicConstructor(typeof(Routing.RouteStop));
        AssertNoPublicConstructor(typeof(Work.AreaScanReport));
        AssertNoPublicConstructor(typeof(TheConcernedCat.ConcernedNPC.Jobs.NpcJobAdvance));
        AssertNoPublicConstructor(typeof(TheConcernedCat.ConcernedNPC.Jobs.NpcJobDriver));
    }

    [Fact]
    public void The_process_wide_registry_exists_and_is_one()
    {
        // Never otherwise touched by a test - a test that used it would leak
        // into every other test in the assembly - so this is the only thing
        // asserting the singleton is real.
        Assert.NotNull(NpcRoleRegistry.Shared);
        Assert.Same(NpcRoleRegistry.Shared, NpcRoleRegistry.Shared);
    }

    private static void AssertNoPublicConstructor(Type type)
    {
        ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.True(
            ctors.Length == 0,
            $"{type.FullName} has a public constructor. It must not: a consumer could then manufacture " +
            "permission this package refused it.");
    }
}
