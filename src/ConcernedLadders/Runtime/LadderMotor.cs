using System;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>The entire private game API boundary. Bind once; refuse mounts if the installed game does not fit.</summary>
internal sealed class LadderMotor : IDisposable
{
    private const string ForemanMotor = "com.theconcernedcat.valheim.concernedforeman.ladders";
    private AccessTools.FieldRef<Character, Vector3>? _move;
    private AccessTools.FieldRef<Character, float>? _fallHeight;
    private AccessTools.FieldRef<Character, float>? _groundTouch;
    private AccessTools.FieldRef<Character, Vector3>? _walkVelocity;
    private AccessTools.FieldRef<Character, bool>? _run;
    private Func<Player, bool>? _takeInput;
    private Harmony? _harmony;
    private MethodInfo? _motion;

    internal bool Ready { get; private set; }

    internal bool Install(string id, Action<string> log)
    {
        try
        {
            _move = AccessTools.FieldRefAccess<Character, Vector3>("m_moveDir");
            _fallHeight = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
            // UpdateMotion, which this motor replaces while climbing, is the only place that ages
            // m_lastGroundTouch; m_currentVel is the walk controller's remembered target velocity.
            _groundTouch = AccessTools.FieldRefAccess<Character, float>("m_lastGroundTouch");
            _walkVelocity = AccessTools.FieldRefAccess<Character, Vector3>("m_currentVel");
            // m_run is the processed Run request PlayerController hands to SetControls each tick: the
            // player's current binding, controller and native toggle-run setting. m_running is the walk
            // controller's own result, which is stale while this motor replaces UpdateMotion.
            _run = AccessTools.FieldRefAccess<Character, bool>("m_run");
            _motion = AccessTools.Method(typeof(Character), "UpdateMotion", new[] { typeof(float) });
            MethodInfo jump = AccessTools.Method(typeof(Character), "Jump", new[] { typeof(bool) });
            MethodInfo input = AccessTools.Method(typeof(Player), "TakeInput", Type.EmptyTypes);
            // Drop and the Jump/Drop/Use priority (N07) need the native control tick, before the game applies the
            // crouch edge (sneak toggle) or turns a crouched Jump into a dodge; Use after a departure is held back
            // at the game's own interaction call.
            MethodInfo controls = AccessTools.Method(typeof(Player), "SetControls", new[]
            {
                typeof(Vector3), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
            });
            MethodInfo interact = AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });
            if (_motion == null || jump == null || input == null || input.ReturnType != typeof(bool) || controls == null ||
                interact == null || interact.ReturnType != typeof(void) || !HasParameters(controls, "jump", "crouch"))
            {
                log("Ladder climbing unavailable: the installed game's motor/input API did not match. Pieces remain registered.");
                return false;
            }

            _takeInput = AccessTools.MethodDelegate<Func<Player, bool>>(input);
            _harmony = new Harmony(id);
            _harmony.Patch(_motion, prefix: new HarmonyMethod(typeof(LadderMotor), nameof(BeforeMotion)));
            _harmony.Patch(jump, prefix: new HarmonyMethod(typeof(LadderMotor), nameof(BeforeJump)));
            _harmony.Patch(controls, prefix: new HarmonyMethod(typeof(LadderMotor), nameof(BeforeControls)));
            _harmony.Patch(interact, prefix: new HarmonyMethod(typeof(LadderMotor), nameof(BeforeInteract)));
            Ready = true;
            return true;
        }
        catch (Exception exception)
        {
            log("Ladder climbing unavailable: motor binding failed (" + SafeFailure.Brief(exception) + ").");
            Dispose();
            return false;
        }
    }

    internal bool HasForemanMotor()
    {
        if (_motion == null) { return false; }
        Patches? patches = Harmony.GetPatchInfo(_motion);
        if (patches == null) { return false; }
        foreach (string owner in patches.Owners)
        {
            if (string.Equals(owner, ForemanMotor, StringComparison.Ordinal)) { return true; }
        }

        return false;
    }

    internal bool TakesInput(Player player) => Ready && _takeInput != null && _takeInput(player);
    internal Vector3 MoveDirection(Player player) => _move != null ? _move(player) : Vector3.zero;
    internal float FallHeight(Player player) => _fallHeight != null ? _fallHeight(player) : float.NaN;
    internal bool RunIntent(Player player) => _run != null && _run(player);

    /// <summary>Only the valid owner while climbing, or a normal release, may rebase the fall reference.</summary>
    internal void SetFallReference(Player player, float height)
    {
        if (_fallHeight != null && LadderSegment.Finite(new Vector3(0f, height, 0f))) { _fallHeight(player) = height; }
    }

    /// <summary>A release off the rails starts airborne, as vanilla's own ForceJump does (m_lastGroundTouch = 1).
    /// Ground contact resets it on the next physics step if the player really is standing on something, and
    /// clearing the walk controller's stale target stops it cancelling the push-off or shoving back into the rails.</summary>
    internal void BeginAirborne(Player player, Vector3 horizontal)
    {
        if (_groundTouch != null) { _groundTouch(player) = 1f; }
        SetWalkVelocity(player, horizontal);
    }

    /// <summary>The walk controller steers airborne bodies toward its remembered velocity. Setting it to the
    /// departure's horizontal part keeps native air control from braking a jump the moment it starts.</summary>
    internal void SetWalkVelocity(Player player, Vector3 horizontal)
    {
        horizontal.y = 0f;
        if (_walkVelocity != null && LadderSegment.Finite(horizontal)) { _walkVelocity(player) = horizontal; }
    }

    public void Dispose()
    {
        Ready = false;
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch cleanup only, best effort during shutdown. */ }
        _harmony = null;
    }

    private static bool BeforeMotion(Character __instance, float __0) =>
        !(LadderRuntime.Current?.Drive(__instance, __0) ?? false);

    private static bool BeforeJump(Character __instance) =>
        !(LadderRuntime.Current?.Jump(__instance) ?? false);

    private static void BeforeControls(Player __instance, ref bool jump, ref bool crouch) =>
        LadderRuntime.Current?.Controls(__instance, ref jump, ref crouch);

    private static bool BeforeInteract(Player __instance) =>
        !(ReferenceEquals(__instance, Player.m_localPlayer) && (LadderRuntime.Current?.SuppressInteract() ?? false));

    private static bool HasParameters(MethodInfo method, params string[] names)
    {
        ParameterInfo[] parameters = method.GetParameters();
        foreach (string name in names)
        {
            if (Array.FindIndex(parameters, parameter => parameter.Name == name && parameter.ParameterType == typeof(bool)) < 0) { return false; }
        }

        return true;
    }
}
