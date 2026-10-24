using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Utils;
using TheConcernedCat.ConcernedLadders.Pieces;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders;

/// <summary>Independent custom building pieces and local-owner traversal.
/// Piece registration stays active when climbing is disabled: existing world
/// objects must not disappear just because the player changes a setting.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid, "2.30.2")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernedladders";
    public const string PluginName = "Concerned Ladders";
    public const string PluginVersion = "0.1.0";

    private ConfigEntry<bool>? _enabled;
    private LadderRuntime? _runtime;
    private LadderPieceRegistration? _pieces;

    private void Awake()
    {
        _enabled = Config.Bind("General", "ClimbingEnabled", true,
            "Enable climbing on Concerned Ladders. Disabling restores player control; placed ladder prefabs remain registered. Restart after changing the installed plugin set.");
        _pieces = new LadderPieceRegistration(message => Logger.LogInfo(message));
        _pieces.Install();
        _runtime = new LadderRuntime(message => Logger.LogInfo(message));
        _runtime.Enabled = _enabled.Value && _pieces.IsReady;
        _runtime.Install(PluginGuid + ".motor");
        string revision = typeof(Plugin).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? PluginVersion;
        Logger.LogInfo(PluginName + " " + revision + " loaded. Development candidate: gameplay verification required.");
    }

    private void Update()
    {
        if (_runtime == null || _enabled == null) return;
        _runtime.Enabled = _enabled.Value && _pieces?.IsReady == true;
        _runtime.Update(Time.deltaTime);
    }

    private void OnDestroy()
    {
        try { _runtime?.Dispose(); }
        catch (Exception exception)
        {
            Logger.LogError("Ladder cleanup failed: " + SafeFailure.Brief(exception));
        }
        _pieces?.Dispose();
        _runtime = null;
        _pieces = null;
    }
}
