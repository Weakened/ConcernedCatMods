using System;
using System.Reflection;
using TheConcernedCat.ConcernedTeamster.Domain;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Resolves the running game's version label for the environment
/// banner (CT-001 logic, relocated in CT-002 so no Valheim type name exists
/// outside Adapters/). Every lookup is reflective: compile-time binding to
/// the game's version API threw MissingMethodException on Concerned
/// Cartographer when the live game and the publicized reference disagreed,
/// and the type itself is resolved by name so even its removal degrades to
/// "unknown" instead of a type-load failure.</summary>
public static class GameVersionResolver
{
    public static string Resolve()
    {
        try
        {
            Type? versionType = Type.GetType("Version, assembly_valheim", throwOnError: false);
            if (versionType is null)
            {
                return EnvironmentBanner.Unknown;
            }

            const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;

            object? currentVersion =
                versionType.GetField("CurrentVersion", publicStatic)?.GetValue(null) ??
                versionType.GetProperty("CurrentVersion", publicStatic)?.GetValue(null);
            if (currentVersion is not null)
            {
                return currentVersion.ToString() ?? EnvironmentBanner.Unknown;
            }

            foreach (MethodInfo method in versionType.GetMethods(publicStatic))
            {
                if (method.Name != "GetVersionString" || method.ReturnType != typeof(string))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object?[] arguments = new object?[parameters.Length];
                for (int index = 0; index < parameters.Length; index++)
                {
                    arguments[index] = parameters[index].HasDefaultValue
                        ? parameters[index].DefaultValue
                        : parameters[index].ParameterType.IsValueType
                            ? Activator.CreateInstance(parameters[index].ParameterType)
                            : null;
                }

                return method.Invoke(null, arguments) as string ?? EnvironmentBanner.Unknown;
            }
        }
        catch
        {
            // The version banner is cosmetic; never fail startup over it.
        }

        return EnvironmentBanner.Unknown;
    }
}
