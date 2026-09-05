using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>The complete read contract Teamster holds against Concerned
/// Cartographer (CT-021): plugin identity, the supported version floor, and
/// every member of the reflective route-read chain. The contract is
/// documented member by member in
/// docs/mods/concerned-teamster/CARTOGRAPHER_CONTRACT.md and statically
/// cross-checked against the Cartographer sources by tools/validate_repo.py,
/// so a Cartographer rename fails validation instead of silently breaking
/// users. There is no compile-time reference in either direction — every
/// name below is a string resolved at runtime.</summary>
public static class CartographerContract
{
    /// <summary>BepInEx GUID Cartographer registers under; the detection key.</summary>
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedcartographer";

    /// <summary>Display name for log lines.</summary>
    public const string ProductName = "Concerned Cartographer";

    /// <summary>Oldest Cartographer this contract supports: 0.10.0, the first
    /// build ever distributed publicly (earlier versions exist only as
    /// internal release candidates on the dev machine). The five contract
    /// source files are byte-identical between 0.10.0 and the current tree,
    /// so every member below is verified at the floor. Versions above the
    /// floor are accepted only when every member still verifies at runtime —
    /// the member probe, not optimism, is the forward-compatibility gate.
    /// (System.Version is spelled out: Valheim ships a global Version type
    /// that captures the bare name in the plugin build.)</summary>
    public static readonly System.Version FloorVersion = new System.Version(0, 10, 0);

    // Owner labels used in verified/missing report entries.
    public const string PluginLabel = "CartographerPlugin";
    public const string RuntimeLabel = "CartographerRuntime";
    public const string StoreLabel = "RouteStore";
    public const string RouteLabel = "AtlasRoute";
    public const string IdLabel = "AtlasId";
    public const string PointLabel = "RoadPoint";

    // The reflective chain, root to leaf. Types are never named — each hop's
    // type is discovered from the previous hop's field/property metadata, so
    // fakes in tests and future Cartographer refactors that keep the shapes
    // both satisfy the same code path.
    public const string RuntimeField = "_runtime";
    public const string RouteStoreField = "_routeStore";
    public const string LivingProperty = "Living";
    public const string ChangeStampProperty = "ChangeStamp";
    public const string RouteIdProperty = "Id";
    public const string RouteNameProperty = "Name";
    public const string RouteArchivedProperty = "Archived";
    public const string RoutePointsProperty = "Points";
    public const string IdValueProperty = "Value";
    public const string PointXProperty = "X";
    public const string PointYProperty = "Y";
    public const string PointZProperty = "Z";

    private const BindingFlags InstanceMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Builds the requirement list for
    /// <see cref="GameMemberProbe"/> from the plugin's runtime type. Each
    /// nested type is resolved from the member metadata of its parent; an
    /// unresolvable hop yields requirements with a null owner, which the
    /// probe reports as "type not found" — actionable, never a throw.</summary>
    public static IReadOnlyList<GameMemberRequirement> BuildRequirements(Type? pluginType)
    {
        var requirements = new List<GameMemberRequirement>(12);

        requirements.Add(new GameMemberRequirement(
            PluginLabel, pluginType, RuntimeField, GameMemberKind.InstanceField));
        Type? runtimeType = ResolveFieldType(pluginType, RuntimeField);

        requirements.Add(new GameMemberRequirement(
            RuntimeLabel, runtimeType, RouteStoreField, GameMemberKind.InstanceField));
        Type? storeType = ResolveFieldType(runtimeType, RouteStoreField);

        requirements.Add(new GameMemberRequirement(
            StoreLabel, storeType, LivingProperty, GameMemberKind.InstanceProperty));
        requirements.Add(new GameMemberRequirement(
            StoreLabel, storeType, ChangeStampProperty, GameMemberKind.InstanceProperty, typeof(long)));

        Type? routeType = ResolveElementType(ResolvePropertyType(storeType, LivingProperty));
        requirements.Add(new GameMemberRequirement(
            RouteLabel, routeType, RouteIdProperty, GameMemberKind.InstanceProperty));
        requirements.Add(new GameMemberRequirement(
            RouteLabel, routeType, RouteNameProperty, GameMemberKind.InstanceProperty, typeof(string)));
        requirements.Add(new GameMemberRequirement(
            RouteLabel, routeType, RouteArchivedProperty, GameMemberKind.InstanceProperty, typeof(bool)));
        requirements.Add(new GameMemberRequirement(
            RouteLabel, routeType, RoutePointsProperty, GameMemberKind.InstanceProperty));

        Type? idType = ResolvePropertyType(routeType, RouteIdProperty);
        requirements.Add(new GameMemberRequirement(
            IdLabel, idType, IdValueProperty, GameMemberKind.InstanceProperty, typeof(Guid)));

        Type? pointType = ResolveElementType(ResolvePropertyType(routeType, RoutePointsProperty));
        requirements.Add(new GameMemberRequirement(
            PointLabel, pointType, PointXProperty, GameMemberKind.InstanceProperty, typeof(float)));
        requirements.Add(new GameMemberRequirement(
            PointLabel, pointType, PointYProperty, GameMemberKind.InstanceProperty, typeof(float)));
        requirements.Add(new GameMemberRequirement(
            PointLabel, pointType, PointZProperty, GameMemberKind.InstanceProperty, typeof(float)));

        return requirements;
    }

    private static Type? ResolveFieldType(Type? owner, string fieldName)
    {
        try
        {
            return owner?.GetField(fieldName, InstanceMembers)?.FieldType;
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolvePropertyType(Type? owner, string propertyName)
    {
        try
        {
            return owner?.GetProperty(propertyName, InstanceMembers)?.PropertyType;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The T of an IEnumerable&lt;T&gt; type (itself or any
    /// implemented interface), or null when the type is not a generic
    /// enumerable — which fails the dependent requirements downstream.</summary>
    private static Type? ResolveElementType(Type? enumerableType)
    {
        try
        {
            if (enumerableType is null)
            {
                return null;
            }

            if (enumerableType.IsGenericType &&
                enumerableType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return enumerableType.GetGenericArguments()[0];
            }

            foreach (Type candidate in enumerableType.GetInterfaces())
            {
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return candidate.GetGenericArguments()[0];
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
