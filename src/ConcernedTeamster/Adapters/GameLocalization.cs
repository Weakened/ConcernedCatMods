using System;
using System.Reflection;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Cosmetic-only access to the game's localizer (CT-007), fully
/// reflective and deliberately outside the cart capability: if anything
/// about the game's Localization type changes, every lookup falls back to
/// the raw token — a readable "$item_stone" beats a disabled manifest. The
/// verified surface (assembly_guiutils: static Localization instance,
/// string Localize(string)) is recorded in CART_INTERNALS.md.</summary>
public static class GameLocalization
{
    private static Func<string, string>? _localize;
    private static bool _resolved;

    /// <summary>Localizes a token, or returns it unchanged when the game's
    /// localizer is unavailable in any way. Never throws.</summary>
    public static string LocalizeOrRaw(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        try
        {
            Func<string, string>? localize = ResolveLocalize();
            if (localize is null)
            {
                return token;
            }

            string localized = localize(token);
            return string.IsNullOrEmpty(localized) ? token : localized;
        }
        catch
        {
            return token;
        }
    }

    private static Func<string, string>? ResolveLocalize()
    {
        if (_resolved)
        {
            return _localize;
        }

        _resolved = true;
        try
        {
            Type? localizationType = Type.GetType("Localization, assembly_guiutils", throwOnError: false);
            if (localizationType is null)
            {
                return null;
            }

            const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
            object? instance =
                localizationType.GetField("instance", publicStatic)?.GetValue(null) ??
                localizationType.GetProperty("instance", publicStatic)?.GetValue(null);
            if (instance is null)
            {
                // The localizer initializes lazily during startup; retry on
                // the next lookup instead of caching the miss forever.
                _resolved = false;
                return null;
            }

            MethodInfo? method = localizationType.GetMethod(
                "Localize",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                new[] { typeof(string) },
                modifiers: null);
            if (method is null || method.ReturnType != typeof(string))
            {
                return null;
            }

            _localize = (Func<string, string>)method.CreateDelegate(typeof(Func<string, string>), instance);
            return _localize;
        }
        catch
        {
            return null;
        }
    }
}
