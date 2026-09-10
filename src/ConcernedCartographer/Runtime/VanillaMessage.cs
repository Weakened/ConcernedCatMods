using System;
using System.Reflection;
using HarmonyLib;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Version-tolerant wrapper over Character.Message.
///
/// C# bakes optional arguments into the call site, so a game update that
/// merely APPENDS a parameter turns every direct <c>Message(type, text)</c>
/// call into a MissingMethodException — and Mono raises it while JIT-compiling
/// the containing method, before a single line of it runs. Valheim 1.0 did
/// exactly that (added a trailing <c>bool log = false</c>), and because one
/// call site sits in <see cref="CartographerRuntime.Tick"/> the whole mod died
/// on frame one instead of losing one cosmetic toast (DEF-v0.10-001).
///
/// Resolving the overload by name and padding the argument array from the LIVE
/// parameter list keeps a HUD toast exactly as cosmetic as it should be.</summary>
internal static class VanillaMessage
{
    private static readonly FastInvokeHandler? MessageInvoker;
    private static readonly object?[] TrailingDefaults = Array.Empty<object?>();

    // Initialize the binding atomically. Even a reflection/handler failure
    // must only disable cosmetic toasts, never fail this type's initializer.
    static VanillaMessage()
    {
        try
        {
            MethodInfo? method = ResolveMessageMethod(typeof(Character));
            if (method is null) return;
            object?[] defaults = BuildTrailingDefaults(method);
            FastInvokeHandler invoker = HarmonyLib.MethodInvoker.GetHandler(method);
            TrailingDefaults = defaults;
            MessageInvoker = invoker;
        }
        catch
        {
            MessageInvoker = null;
        }
    }

    /// <summary>False when the vanilla method could not be resolved at all;
    /// callers log this once rather than silently dropping every toast.</summary>
    public static bool Available => MessageInvoker is not null;

    /// <summary>Shows a vanilla HUD message, or does nothing if this game
    /// build has no recognisable Character.Message. Never throws.</summary>
    public static void Show(Character? character, MessageHud.MessageType type, string text)
    {
        if (MessageInvoker is null || character == null)
        {
            return;
        }

        try
        {
            object?[] arguments = new object?[2 + TrailingDefaults.Length];
            arguments[0] = type;
            arguments[1] = text;
            Array.Copy(TrailingDefaults, 0, arguments, 2, TrailingDefaults.Length);
            MessageInvoker(character, arguments);
        }
        catch
        {
            // A toast is never worth taking a caller down with it.
        }
    }

    /// <summary>Picks the (MessageType, string, ...) overload with the fewest
    /// parameters, so appended optional arguments stay invisible to us.</summary>
    internal static MethodInfo? ResolveMessageMethod(Type characterType)
    {
        try
        {
            MethodInfo? best = null;
            int bestLength = int.MaxValue;
            foreach (MethodInfo candidate in AccessTools.GetDeclaredMethods(characterType))
            {
                if (candidate.IsStatic || candidate.IsGenericMethod || candidate.ReturnType != typeof(void) ||
                    candidate.Name != nameof(Character.Message))
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 2 ||
                    parameters[0].ParameterType != typeof(MessageHud.MessageType) ||
                    parameters[1].ParameterType != typeof(string))
                {
                    continue;
                }

                // Only known optional tails are safe to fill. Unknown required,
                // pointer, or by-ref arguments are unsupported capabilities.
                bool fillable = true;
                for (int index = 2; index < parameters.Length; index++)
                {
                    if (parameters[index].ParameterType.IsByRef ||
                        parameters[index].ParameterType.IsPointer ||
                        !parameters[index].IsOptional || !parameters[index].HasDefaultValue)
                    {
                        fillable = false;
                        break;
                    }
                }

                if (fillable && parameters.Length < bestLength)
                {
                    best = candidate;
                    bestLength = parameters.Length;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static object?[] BuildTrailingDefaults(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] defaults = new object?[parameters.Length - 2];
        for (int index = 0; index < defaults.Length; index++)
        {
            defaults[index] = parameters[index + 2].DefaultValue;
        }
        return defaults;
    }
}
