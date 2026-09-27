using System;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.Ladders;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>The two places the climb has to stand in front of vanilla, and the
/// two private fields it has to read.
///
/// LADDERS.md L7 allows exactly this much: the local player's own motor, and
/// nothing else. Both patches are prefixes that only ever say "this body is on
/// a ladder this frame"; with <c>Ladders/Enabled = false</c>, with no climb in
/// progress, or for any character that is not the local climber, they fall
/// straight through to vanilla, so the game behaves exactly as it does without
/// the mod.
///
/// <list type="bullet">
/// <item><c>Character.UpdateMotion</c> — the motor. Nothing smaller works: it is
/// the method that applies gravity, the ground force, walking, swimming and
/// slipping, and a climb that leaves any of them running fights the physics
/// engine for the player's body. Vanilla short-circuits the same method for its
/// own debug fly, which is the shape this follows.</item>
/// <item><c>Character.Jump</c> — letting go. A climber is not on the ground, so
/// vanilla's own jump would refuse anyway; the prefix turns that refusal into a
/// deliberate let-go instead of a keypress that does nothing.</item>
/// </list>
///
/// Field access goes through Harmony's skip-visibility accessors, bound once,
/// because a direct call to a publicized private member throws
/// <c>FieldAccessException</c> at JIT on Mono (the repository has already been
/// burnt by this: `CODEBASE_GUIDE.md` §16). If either field cannot be bound the
/// climb refuses to start rather than climbing badly: without
/// <c>m_maxAirAltitude</c> a descent ends in fall damage, and without
/// <c>m_moveDir</c> there is no input to climb with.</summary>
internal static class ClimbMotor
{
    private static AccessTools.FieldRef<Character, Vector3>? _moveDir;
    private static AccessTools.FieldRef<Character, float>? _maxAirAltitude;
    private static Harmony? _harmony;
    private static bool _bound;

    /// <summary>The climb may run: both patches are in place and both fields
    /// are readable.</summary>
    internal static bool Ready { get; private set; }

    /// <summary>Why not, when <see cref="Ready"/> is false. Never an empty
    /// string in front of a person.</summary>
    internal static string Unavailable { get; private set; } = "Ladder climbing has not been started yet.";

    /// <summary>Binds the fields and installs the two prefixes. Everything
    /// fails soft: a game this does not fit leaves vanilla untouched and says
    /// so, and the controller then refuses every mount.</summary>
    internal static bool Install(string harmonyId, Action<string> log)
    {
        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (Ready)
        {
            return true;
        }

        if (!Bind(log))
        {
            return false;
        }

        try
        {
            MethodInfo? updateMotion = AccessTools.Method(typeof(Character), "UpdateMotion", new[] { typeof(float) });
            MethodInfo? jump = AccessTools.Method(typeof(Character), nameof(Character.Jump), new[] { typeof(bool) });
            if (updateMotion == null || jump == null)
            {
                Unavailable =
                    "this build of Valheim does not have the character motor members ladder climbing drives " +
                    "(Character.UpdateMotion / Character.Jump); ladders keep vanilla behaviour";
                log("Ladder climbing unavailable: " + Unavailable + ".");
                return false;
            }

            _harmony = new Harmony(harmonyId);
            _harmony.Patch(updateMotion, prefix: new HarmonyMethod(typeof(ClimbMotor), nameof(BeforeUpdateMotion)));
            _harmony.Patch(jump, prefix: new HarmonyMethod(typeof(ClimbMotor), nameof(BeforeJump)));
            Ready = true;
            Unavailable = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            Unavailable = "the character motor could not be patched (" + SafeFailure.Brief(exception) + ")";
            log("Ladder climbing unavailable: " + Unavailable + ".");
            Remove();
            return false;
        }
    }

    /// <summary>Takes the patches out again. The plugin is stopping, and a
    /// player left on a ladder has already been handed back by the controller
    /// before this runs.</summary>
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
        Ready = false;
    }

    /// <summary>The character's movement intent, in world space and flat. It is
    /// vanilla's own <c>m_moveDir</c>, which <c>PlayerController</c> fills in
    /// every physics step from the player's keys or stick.</summary>
    internal static bool TryReadMoveDir(Character character, out Vector3 moveDir)
    {
        moveDir = Vector3.zero;
        if (character == null || _moveDir == null)
        {
            return false;
        }

        moveDir = _moveDir(character);
        return true;
    }

    /// <summary>Holds the character's fall reference at its own feet.
    ///
    /// A climber is technically in the air for the whole climb, and vanilla
    /// works out both the landing sound and fall damage from the highest point
    /// reached since the last ground contact. Without this, climbing ten metres
    /// down a ladder and stepping off would land as a ten metre fall. Vanilla
    /// does exactly the same thing for its own gravity-free motion.</summary>
    internal static void PinFallHeight(Character character)
    {
        if (character == null || _maxAirAltitude == null)
        {
            return;
        }

        _maxAirAltitude(character) = character.transform.position.y;
    }

    private static bool Bind(Action<string> log)
    {
        if (_bound)
        {
            return _moveDir != null && _maxAirAltitude != null;
        }

        _bound = true;
        _moveDir = TryBind<Vector3>("m_moveDir");
        _maxAirAltitude = TryBind<float>("m_maxAirAltitude");
        if (_moveDir != null && _maxAirAltitude != null)
        {
            return true;
        }

        Unavailable =
            "this build of Valheim does not expose the character fields ladder climbing reads (" +
            (_moveDir == null ? "m_moveDir " : string.Empty) +
            (_maxAirAltitude == null ? "m_maxAirAltitude" : string.Empty) +
            "); ladders keep vanilla behaviour";
        log("Ladder climbing unavailable: " + Unavailable + ".");
        return false;
    }

    private static AccessTools.FieldRef<Character, TField>? TryBind<TField>(string field)
    {
        try
        {
            return AccessTools.FieldRefAccess<Character, TField>(field);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Vanilla's motor runs unless this exact body is on a ladder this
    /// frame. Two reference comparisons for every other character in the world.</summary>
    private static bool BeforeUpdateMotion(Character __instance, float dt) =>
        !ClimbController.DrivesMotion(__instance, dt);

    private static bool BeforeJump(Character __instance) =>
        !ClimbController.LetsGoOnJump(__instance);
}
