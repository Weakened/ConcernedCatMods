using System;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.ConcernedForeman.Domain.Ladders;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>Where vanilla offers a teleport on Use, offer the climb instead
/// (CF-LAD-004, LADDERS.md L2).
///
/// <b>One patch, one method.</b> <c>Ladder.Interact</c> is the whole of
/// vanilla's ladder behaviour: it sets the character's position and rotation to
/// the ladder's target and returns. A prefix on it is the smallest possible
/// place to stand, and it is the only place this workstream stands. The two
/// patches on the player's own motor belong to CF-LAD-002
/// (<see cref="ClimbMotor"/>); this one does not duplicate, wrap or race them —
/// it asks <see cref="ClimbController.TryMountByUse"/>, which is that
/// workstream's own seam for exactly this, and does what it says.
///
/// <b>With the feature off there is no patch.</b> <c>Ladders/Enabled = false</c>
/// when the game starts means <see cref="Install"/> is never called, so
/// <c>Ladder.Interact</c> is untouched IL and the game is the game. Switched off
/// later, or with <c>Ladders/UseTeleport = true</c>, the prefix reads one bool
/// and hands the method back — vanilla's teleport, exactly, in every state.
///
/// Nothing is sent, nothing is written to the world, no ladder is reserved, and
/// no piece is changed. The hover text is vanilla's: the key that used to
/// teleport you now starts a climb, and nothing else about the ladder is
/// different.</summary>
internal static class LadderInteraction
{
    private static Harmony? _harmony;
    private static ClimbController? _controller;
    private static LadderSettings? _settings;
    private static Action<string>? _log;
    private static bool _saidItFailed;

    /// <summary>Whether the prefix is in place. False means every ladder
    /// teleports, which is vanilla.</summary>
    internal static bool Installed { get; private set; }

    /// <summary>Why not, when <see cref="Installed"/> is false. Never an empty
    /// string in front of a person.</summary>
    internal static string Unavailable { get; private set; } = "the ladder interaction has not been started yet";

    /// <summary>Installs the prefix. Its own Harmony id, deliberately not the
    /// climb's: <see cref="ClimbMotor.Remove"/> unpatches everything under its
    /// id, and these two are removed for different reasons at different
    /// times.</summary>
    internal static bool Install(
        string harmonyId, ClimbController controller, LadderSettings settings, Action<string> log)
    {
        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log;

        if (Installed)
        {
            return true;
        }

        try
        {
            MethodInfo? interact = FindInteract();
            if (interact == null)
            {
                Unavailable =
                    "this build of Valheim has no Ladder.Interact to stand in front of, so ladders keep " +
                    "vanilla behaviour";
                log("Ladder climbing: " + Unavailable + ".");
                return false;
            }

            _harmony = new Harmony(harmonyId);
            _harmony.Patch(interact, prefix: new HarmonyMethod(typeof(LadderInteraction), nameof(BeforeInteract)));
            Installed = true;
            Unavailable = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            Unavailable = "the ladder interaction could not be patched (" + exception.Message + ")";
            log("Ladder climbing: " + Unavailable + ". Ladders keep vanilla behaviour.");
            Remove();
            return false;
        }
    }

    /// <summary>Takes the prefix out again. The plugin is stopping; a climber
    /// has already been handed back by the controller before this runs.</summary>
    internal static void Remove()
    {
        try
        {
            _harmony?.UnpatchSelf();
        }
        catch
        {
            // Teardown is best effort; a failure here must not stop a shutdown.
        }

        _harmony = null;
        _controller = null;
        _settings = null;
        _log = null;
        Installed = false;
        Unavailable = "the ladder interaction has been stopped";
    }

    /// <summary><c>Ladder.Interact</c>, whatever this build calls its first
    /// parameter and whether or not the interface is implemented explicitly.
    /// Declared on <c>Ladder</c> itself, returning a bool, taking three
    /// arguments of which the last two are the hold and alternate flags: that
    /// is the <c>Interactable</c> shape, and nothing else on the type has
    /// it.</summary>
    private static MethodInfo? FindInteract()
    {
        const BindingFlags Where =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in typeof(Ladder).GetMethods(Where))
        {
            if (method.ReturnType != typeof(bool) || method.IsGenericMethod)
            {
                continue;
            }

            if (!method.Name.Equals("Interact", StringComparison.Ordinal) &&
                !method.Name.EndsWith(".Interact", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 3 &&
                parameters[1].ParameterType == typeof(bool) &&
                parameters[2].ParameterType == typeof(bool))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>The Use press.
    ///
    /// Returning true runs vanilla's teleport, unchanged. Returning false
    /// suppresses it, and <c>__result</c> then says the interaction was
    /// handled, which is what vanilla's own teleport reports.
    ///
    /// Deliberately no injected arguments beyond the instance and the result:
    /// Harmony matches injected parameters by the original's parameter
    /// <i>names</i>, and a name is not something a decompile of one build may
    /// promise about the next. Everything the decision needs is already
    /// readable without them, and a repeated Use from a held key is handled by
    /// the controller's own re-mount gate.</summary>
    private static bool BeforeInteract(Ladder __instance, ref bool __result)
    {
        ClimbController? controller = _controller;
        LadderSettings? settings = _settings;
        if (controller == null || settings == null || __instance == null)
        {
            return true;
        }

        try
        {
            LadderUseOutcome outcome = LadderUse.Decide(
                settings.Enabled.Value, settings.UseTeleport.Value, controller.IsClimbing);

            switch (outcome)
            {
                case LadderUseOutcome.AlreadyClimbing:
                    // Vanilla's Use is a teleport to the other end of the
                    // ladder. Doing that to somebody already on it would throw
                    // them off the run they are climbing.
                    __result = true;
                    return false;

                case LadderUseOutcome.TryToClimb:
                    if (controller.TryMountByUse(__instance))
                    {
                        __result = true;
                        return false;
                    }

                    // The climb refused: out of reach, on the wrong side, still
                    // in the cooldown after letting go, or unavailable on this
                    // build. The teleport is vanilla's to do, exactly as before.
                    return true;

                default:
                    return true;
            }
        }
        catch (Exception exception)
        {
            if (!_saidItFailed)
            {
                _saidItFailed = true;
                _log?.Invoke(
                    "Ladder climbing could not take the Use press (" + exception.Message +
                    "), so vanilla handled it. This is said once.");
            }

            return true;
        }
    }
}
