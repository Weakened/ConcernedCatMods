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
    /// <summary>Every type a consumer can see, sorted. Twelve, and each is here
    /// because a role genuinely needs it:
    ///
    /// the identity and the role contract it declares (<c>NpcIdentity</c>,
    /// <c>INpcRole</c>, <c>INpcDataPaths</c>, <c>NpcBodyContract</c>,
    /// <c>NpcBodyKind</c>); the registry it declares itself to and the outcome
    /// it gets back (<c>NpcRoleRegistry</c>, <c>RoleRegistration</c>,
    /// <c>RoleRegistrationStatus</c>); the world load it announces
    /// (<c>NpcWorldEpoch</c>); and the body claim with the lease a grant carries
    /// (<c>BodyClaim</c>, <c>BodyClaimStatus</c>, <c>BodyLease</c>).
    ///
    /// Everything else - the arbiter, the mode owner, the slug rules, and all
    /// six seams the later leaves implement - is internal, and stays internal
    /// until a leaf can say which role needs it and accept the version
    /// bump.</summary>
    private static readonly string[] Expected =
    {
        "TheConcernedCat.ConcernedNPC.Bodies.BodyClaim",
        "TheConcernedCat.ConcernedNPC.Bodies.BodyClaimStatus",
        "TheConcernedCat.ConcernedNPC.Bodies.BodyLease",
        "TheConcernedCat.ConcernedNPC.Roles.INpcDataPaths",
        "TheConcernedCat.ConcernedNPC.Roles.INpcRole",
        "TheConcernedCat.ConcernedNPC.Roles.NpcBodyContract",
        "TheConcernedCat.ConcernedNPC.Roles.NpcBodyKind",
        "TheConcernedCat.ConcernedNPC.Roles.NpcIdentity",
        "TheConcernedCat.ConcernedNPC.Roles.NpcRoleRegistry",
        "TheConcernedCat.ConcernedNPC.Roles.RoleRegistration",
        "TheConcernedCat.ConcernedNPC.Roles.RoleRegistrationStatus",
        "TheConcernedCat.ConcernedNPC.Work.NpcWorldEpoch",
    };

    [Fact]
    public void The_public_surface_is_exactly_what_is_written_down()
    {
        string[] actual = typeof(NpcRoleRegistry).Assembly
            .GetExportedTypes()
            .Where(type => type.FullName != null
                && type.FullName.StartsWith("TheConcernedCat.ConcernedNPC.", StringComparison.Ordinal)
                && !type.FullName.StartsWith("TheConcernedCat.ConcernedNPC.Tests.", StringComparison.Ordinal))
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
