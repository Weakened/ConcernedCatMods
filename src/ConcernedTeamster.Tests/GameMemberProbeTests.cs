using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace ConcernedTeamster.Tests;

/// <summary>CT-002: the capability probe must verify a complete fake game
/// surface, disable on every simulated game change without throwing, and name
/// each missing member actionably. Fake types mirror the shapes of the real
/// cart surface recorded in CART_INTERNALS.md.</summary>
public class GameMemberProbeTests
{
    // -- fake game surface (shapes mirror the verified cart internals) --

    private class FakeCharacter
    {
        public bool IsTeleporting() => false;
    }

    private sealed class FakePlayer : FakeCharacter
    {
        public static FakePlayer? m_localPlayer = null;
    }

    private sealed class FakeLocalPlayerHolder : FakePlayer2Base { }

    private class FakePlayer2Base
    {
        public static FakePlayer2Base? m_localPlayer = null;
    }

    private sealed class FakeInventory
    {
        public float GetTotalWeight() => 0f;
    }

    private sealed class FakeContainer
    {
        public FakeInventory GetInventory() => new();
    }

    private struct FakeZdoId { }

    private sealed class FakeZdo
    {
        public FakeZdoId m_uid = default;
    }

    private sealed class FakeNetView
    {
        public bool IsValid() => false;

        public FakeZdo GetZDO() => new();
    }

    private sealed class FakeVagon
    {
        public static List<FakeVagon> m_instances = new();

        public float m_baseMass = 0f;
        public float m_itemWeightMassFactor = 0f;
        public FakeContainer? m_container = null;

        public bool IsAttached() => false;

        public bool IsAttached(FakeCharacter character) => character is null;
    }

    private struct FakeVector3 { }

    private sealed class FakeRigidbody
    {
        public FakeVector3 linearVelocity => default;
    }

    private sealed class FakeRigidbodySetOnly
    {
        public FakeVector3 linearVelocity
        {
            set { }
        }
    }

    private sealed class FakeVagonWrongFieldType
    {
        public double m_baseMass = 0d;
    }

    private sealed class FakeVagonWrongReturnType
    {
        public int IsAttached() => 0;
    }

    /// <summary>The exact requirement shape CartAdapter submits, bound to the
    /// fake surface, so the mechanism is tested end to end.</summary>
    private static List<GameMemberRequirement> CompleteRequirements()
    {
        return new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
            new("Vagon", typeof(FakeVagon), "m_itemWeightMassFactor", GameMemberKind.InstanceField, typeof(float)),
            new("Vagon", typeof(FakeVagon), "m_container", GameMemberKind.InstanceField, typeof(FakeContainer)),
            new("Vagon", typeof(FakeVagon), "IsAttached", GameMemberKind.InstanceMethod, typeof(bool)),
            new("Vagon", typeof(FakeVagon), "IsAttached", GameMemberKind.InstanceMethod, typeof(bool),
                new Type?[] { typeof(FakeCharacter) }),
            new("Container", typeof(FakeContainer), "GetInventory", GameMemberKind.InstanceMethod, typeof(FakeInventory)),
            new("Inventory", typeof(FakeInventory), "GetTotalWeight", GameMemberKind.InstanceMethod, typeof(float)),
            new("ZNetView", typeof(FakeNetView), "IsValid", GameMemberKind.InstanceMethod, typeof(bool)),
            new("ZNetView", typeof(FakeNetView), "GetZDO", GameMemberKind.InstanceMethod, typeof(FakeZdo)),
            new("ZDO", typeof(FakeZdo), "m_uid", GameMemberKind.InstanceField, typeof(FakeZdoId)),
            new("Player", typeof(FakePlayer), "m_localPlayer", GameMemberKind.StaticField, typeof(FakePlayer)),
            new("Vagon", typeof(FakeVagon), "m_instances", GameMemberKind.StaticField, typeof(List<FakeVagon>)),
            new("Rigidbody", typeof(FakeRigidbody), "linearVelocity", GameMemberKind.InstanceProperty, typeof(FakeVector3)),
        };
    }

    [Fact]
    public void Probe_CompleteFakeSurface_EnablesWithEveryMemberVerified()
    {
        GameCapabilityReport report = GameMemberProbe.Probe(CompleteRequirements());

        Assert.True(report.Enabled);
        Assert.Empty(report.MissingMembers);
        Assert.Equal(13, report.VerifiedMembers.Count);
        Assert.Contains("Vagon.m_baseMass", report.VerifiedMembers);
        Assert.Contains("Player.m_localPlayer", report.VerifiedMembers);
        Assert.Contains("Rigidbody.linearVelocity", report.VerifiedMembers);
    }

    [Fact]
    public void Probe_MissingProperty_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Rigidbody", typeof(FakeCharacter), "linearVelocity", GameMemberKind.InstanceProperty, typeof(FakeVector3)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Rigidbody.linearVelocity (property not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_PropertyWithoutGetter_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Rigidbody", typeof(FakeRigidbodySetOnly), "linearVelocity", GameMemberKind.InstanceProperty, typeof(FakeVector3)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Rigidbody.linearVelocity (property has no getter)", report.MissingMembers);
    }

    [Fact]
    public void Probe_WrongPropertyType_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Rigidbody", typeof(FakeRigidbody), "linearVelocity", GameMemberKind.InstanceProperty, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Rigidbody.linearVelocity (property type is FakeVector3, expected Single)", report.MissingMembers);
    }

    [Fact]
    public void Probe_GenericStaticFieldWithWrongItemType_Disables()
    {
        // Mirrors the registry check: List<FakeVagon> expected, so a
        // List<object> (as if the game changed the registry's item type)
        // must fail the probe.
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "m_instances", GameMemberKind.StaticField, typeof(List<object>)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Single(report.MissingMembers);
    }

    [Fact]
    public void Probe_MissingField_DisablesAndNamesTheMember()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeCharacter), "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.m_baseMass (field not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_WrongFieldType_DisablesAndExplainsBothTypes()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagonWrongFieldType), "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.m_baseMass (field type is Double, expected Single)", report.MissingMembers);
    }

    [Fact]
    public void Probe_MissingMethodOverload_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "IsAttached", GameMemberKind.InstanceMethod, typeof(bool),
                new Type?[] { typeof(string) }),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.IsAttached (method not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_WrongReturnType_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagonWrongReturnType), "IsAttached", GameMemberKind.InstanceMethod, typeof(bool)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.IsAttached (return type is Int32, expected Boolean)", report.MissingMembers);
    }

    [Fact]
    public void Probe_OwnerTypeNotResolved_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", null, "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.m_baseMass (type not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_StaticRequirementForInstanceField_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "m_baseMass", GameMemberKind.StaticField, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.m_baseMass (field not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_InstanceRequirementForStaticField_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Player", typeof(FakePlayer), "m_localPlayer", GameMemberKind.InstanceField, typeof(FakePlayer)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Player.m_localPlayer (field not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_EmptyMemberName_DisablesWithoutThrowing()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "", GameMemberKind.InstanceField, typeof(float)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon. (empty member name)", report.MissingMembers);
    }

    [Fact]
    public void Probe_UnresolvableParameterType_Disables()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeVagon), "IsAttached", GameMemberKind.InstanceMethod, typeof(bool),
                new Type?[] { null }),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Contains("Vagon.IsAttached (parameter 0 type not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_StaticFieldHoistedToBaseClass_StillVerifies()
    {
        // FlattenHierarchy keeps the probe green if a game update moves a
        // static member up the hierarchy without changing its meaning.
        var requirements = new List<GameMemberRequirement>
        {
            new("Player", typeof(FakeLocalPlayerHolder), "m_localPlayer", GameMemberKind.StaticField,
                typeof(FakePlayer2Base)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.True(report.Enabled);
        Assert.Contains("Player.m_localPlayer", report.VerifiedMembers);
    }

    [Fact]
    public void Probe_InstanceMethodInheritedFromBaseClass_StillVerifies()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Player", typeof(FakePlayer), "IsTeleporting", GameMemberKind.InstanceMethod, typeof(bool)),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.True(report.Enabled);
    }

    [Fact]
    public void Probe_MultipleMissingMembers_ReportsEveryOne()
    {
        var requirements = new List<GameMemberRequirement>
        {
            new("Vagon", typeof(FakeCharacter), "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
            new("Vagon", typeof(FakeVagon), "m_baseMass", GameMemberKind.InstanceField, typeof(float)),
            new("ZDO", null, "m_uid", GameMemberKind.InstanceField, null),
        };

        GameCapabilityReport report = GameMemberProbe.Probe(requirements);

        Assert.False(report.Enabled);
        Assert.Single(report.VerifiedMembers);
        Assert.Equal(2, report.MissingMembers.Count);
        Assert.Contains("Vagon.m_baseMass (field not found)", report.MissingMembers);
        Assert.Contains("ZDO.m_uid (type not found)", report.MissingMembers);
    }

    [Fact]
    public void Probe_NoRequirements_IsVacuouslyEnabled()
    {
        // Adapters always submit at least one requirement; this documents the
        // boundary behavior rather than an intended use.
        GameCapabilityReport report = GameMemberProbe.Probe(new List<GameMemberRequirement>());

        Assert.True(report.Enabled);
        Assert.Empty(report.VerifiedMembers);
        Assert.Empty(report.MissingMembers);
    }
}
