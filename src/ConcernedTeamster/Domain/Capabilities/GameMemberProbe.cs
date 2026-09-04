using System;
using System.Collections.Generic;
using System.Reflection;

namespace TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

/// <summary>Verifies that the members an adapter compiled against still exist
/// with the expected shapes (CT-002). Pure reflection over caller-supplied
/// <see cref="Type"/> objects: production passes runtime-resolved game types,
/// tests pass fake surfaces, and this core never references game assemblies.
/// The probe never throws — any reflection surprise becomes a missing entry,
/// because a probe crash at startup would defeat its fail-closed purpose.</summary>
public static class GameMemberProbe
{
    public static GameCapabilityReport Probe(IReadOnlyList<GameMemberRequirement> requirements)
    {
        var verified = new List<string>();
        var missing = new List<string>();
        try
        {
            foreach (GameMemberRequirement requirement in requirements)
            {
                string display = requirement.DisplayName;
                try
                {
                    string? problem = ProbeOne(requirement);
                    if (problem is null)
                    {
                        verified.Add(display);
                    }
                    else
                    {
                        missing.Add($"{display} ({problem})");
                    }
                }
                catch (Exception exception)
                {
                    missing.Add($"{display} (probe error: {exception.GetType().Name})");
                }
            }
        }
        catch (Exception exception)
        {
            missing.Add($"capability probe failed ({exception.GetType().Name})");
        }

        return new GameCapabilityReport(verified, missing);
    }

    /// <summary>Returns null when the requirement verifies, otherwise the
    /// reason it cannot be trusted.</summary>
    private static string? ProbeOne(GameMemberRequirement requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement.MemberName))
        {
            return "empty member name";
        }

        if (requirement.OwnerType is null)
        {
            return "type not found";
        }

        switch (requirement.Kind)
        {
            case GameMemberKind.InstanceField:
            case GameMemberKind.StaticField:
            {
                // NonPublic is included because later leaves probe private
                // members (for example the cart instance registry); existence
                // and type are what make an access safe, not visibility.
                // FlattenHierarchy keeps a static member verifiable if a game
                // update hoists it into a base class.
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                    (requirement.Kind == GameMemberKind.StaticField
                        ? BindingFlags.Static | BindingFlags.FlattenHierarchy
                        : BindingFlags.Instance);
                FieldInfo? field = requirement.OwnerType.GetField(requirement.MemberName, flags);
                if (field is null)
                {
                    return "field not found";
                }

                if (requirement.ExpectedType is not null && field.FieldType != requirement.ExpectedType)
                {
                    return $"field type is {field.FieldType.Name}, expected {requirement.ExpectedType.Name}";
                }

                return null;
            }

            case GameMemberKind.InstanceMethod:
            {
                Type[] parameters;
                if (requirement.ParameterTypes is null)
                {
                    parameters = Type.EmptyTypes;
                }
                else
                {
                    parameters = new Type[requirement.ParameterTypes.Count];
                    for (int index = 0; index < requirement.ParameterTypes.Count; index++)
                    {
                        Type? parameterType = requirement.ParameterTypes[index];
                        if (parameterType is null)
                        {
                            return $"parameter {index} type not found";
                        }

                        parameters[index] = parameterType;
                    }
                }

                MethodInfo? method = requirement.OwnerType.GetMethod(
                    requirement.MemberName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    parameters,
                    modifiers: null);
                if (method is null)
                {
                    return "method not found";
                }

                if (requirement.ExpectedType is not null && method.ReturnType != requirement.ExpectedType)
                {
                    return $"return type is {method.ReturnType.Name}, expected {requirement.ExpectedType.Name}";
                }

                return null;
            }

            default:
                return $"unsupported member kind {requirement.Kind}";
        }
    }
}
