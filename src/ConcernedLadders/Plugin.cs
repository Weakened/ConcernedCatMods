using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;
using TheConcernedCat.ConcernedLadders.Excavation;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Items;
using TheConcernedCat.ConcernedLadders.Pieces;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.ConcernedLadders.Runtime.Animation;
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
    public const string PluginVersion = "1.1.0";

    private ConfigEntry<bool>? _enabled;
    private LadderRuntime? _runtime;
    private LadderPieceRegistration? _pieces;
    private LadderSkill? _skill;
    private OriginalClimbPresenter? _presenter;
    private LadderSnapAlignment? _snap;
    private ExcavationRuntime? _excavation;
    private ConfigEntry<bool>? _trapdoorsEnabled;
    private TrapdoorPieceRegistration? _trapdoors;
    private ExcavationShovelInput? _shovelInput;
    private IronShovelItem? _shovel;
    private ExcavationPreview? _preview;

    private void Awake()
    {
        _enabled = Config.Bind("General", "ClimbingEnabled", true,
            "Enable climbing on Concerned Ladders. Disabling restores player control; placed ladder prefabs remain registered. Restart after changing the installed plugin set.");
        // Placement first: the Left side / Right side snap points exist only when the adapter that owns their
        // placement is installed (without it the game would put a side-mode ghost 100 m above the aim).
        _snap = new LadderSnapAlignment(message => Logger.LogInfo(message));
        if (!_snap.Install(PluginGuid + ".snap")) { _snap = null; }
        _pieces = new LadderPieceRegistration(message => Logger.LogInfo(message), sideModes: _snap != null);
        _pieces.Install();
        // Floor trapdoors (CL-004 #485), unfinished and off by default: with the setting off nothing is cloned or
        // localized and the hammer menu is unchanged. Their failures never touch the ladders.
        _trapdoorsEnabled = Config.Bind(TrapdoorSettings.Section, TrapdoorSettings.Key, TrapdoorSettings.EnabledByDefault,
            TrapdoorSettings.Description);
        if (_trapdoorsEnabled.Value)
        {
            _trapdoors = new TrapdoorPieceRegistration(message => Logger.LogInfo(message));
            try { _trapdoors.Install(); }
            catch (Exception exception)
            {
                Logger.LogError("Trapdoors unavailable: " + SafeFailure.Brief(exception) + ". The ladders are unaffected.");
                try { _trapdoors.Dispose(); } catch { /* Unsubscribe only. */ }
                _trapdoors = null;
            }
        }
        // The skill registers before any player's Skills awake and whether or not climbing is enabled.
        _skill = new LadderSkill(message => Logger.LogInfo(message));
        _skill.Register();
        _runtime = new LadderRuntime(message => Logger.LogInfo(message), _skill);
        // Original climbing animation (Codex, #431). It only reads the runtime's frames; if it cannot bind to the
        // installed rig, the runtime keeps its walking-pose contingency.
        _presenter = new OriginalClimbPresenter(message => Logger.LogInfo(message));
        bool presenting;
        try { presenting = _presenter.Install(PluginGuid + ".presentation"); }
        catch (Exception exception)
        {
            Logger.LogInfo("Original climbing animation unavailable (" + SafeFailure.Brief(exception) + "); the walking pose is used.");
            presenting = false;
        }

        if (presenting)
        {
            _runtime.SetPresenter(_presenter);
            Logger.LogInfo("Climbing gait source LadderGait.blend " + Domain.Animation.GeneratedGait.BlendSha256.Substring(0, 12) +
                           " (Blender " + Domain.Animation.GeneratedGait.Blender + ").");
        }
        else { try { _presenter.Dispose(); } catch { /* Presentation cleanup only. */ } _presenter = null; }
        _runtime.Enabled = _enabled.Value && _pieces.IsReady;
        _runtime.Install(PluginGuid + ".motor");
        InstallExcavation();
        string revision = typeof(Plugin).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? PluginVersion;
        Logger.LogInfo(PluginName + " " + revision + " loaded.");
    }

    private void Update()
    {
        if (_runtime == null || _enabled == null) return;
        _runtime.Enabled = _enabled.Value && _pieces?.IsReady == true;
        _runtime.Update(Time.deltaTime);
        _excavation?.Update();
        _preview?.Update();
    }

    /// <summary>Iron Shovel excavation (#487 slices 2 to 4). The terrain operations register on every peer, because the
    /// tile owner applies them. The Iron Shovel item, its attack input, its placement preview and the read-only console
    /// diagnostic exist only when the off-by-default switch is on; with it off nothing is added. A fault here never stops
    /// the ladders.</summary>
    private void InstallExcavation()
    {
        ConfigEntry<bool> enabled = Config.Bind("Excavation", "Enabled", false,
            "Experimental, off by default (#487): adds the Iron Shovel (Forge: Iron and Wood). While held it previews one " +
            "measured 2x2x2 m or 4x4x4 m basement hole (secondary attack switches the size); attack digs the hole shown, " +
            "through the game's own terrain operation. Also adds the read-only cl_excavate console diagnostic. Not yet " +
            "observed in game. Every player needs the same version " +
            "and the same setting. Turning it off again removes the item from the game, and the game discards unknown items " +
            "from inventories and chests when it loads them. Restart after changing.");
        try
        {
            _excavation = new ExcavationRuntime(message => Logger.LogInfo(message), enabled.Value);
            _excavation.Install();
            if (enabled.Value)
            {
                _excavation.Report = Announce;
                CommandManager.Instance.AddConsoleCommand(new ExcavationCommand(() => _excavation));
                _shovelInput = new ExcavationShovelInput(message => Logger.LogInfo(message));
                if (!_shovelInput.Install(PluginGuid + ".excavation", _excavation)) _shovelInput = null;
                _shovel = new IronShovelItem(message => Logger.LogInfo(message), craftable: _shovelInput != null && _excavation.Available);
                _shovel.Install();
                _excavation.Faulted = () => _shovel?.DisableRecipe();
                _preview = _shovelInput != null ? new ExcavationPreview(message => Logger.LogInfo(message), _excavation) : null;
                _preview?.Install();
            }
        }
        catch (Exception exception)
        {
            Logger.LogInfo("Excavation unavailable (" + SafeFailure.Brief(exception) + "); ladders are unaffected.");
            try { _preview?.Dispose(); } catch { /* Cleanup only. */ }
            try { _shovel?.DisableRecipe(); } catch { /* Cleanup only. */ }
            try { _shovelInput?.Dispose(); } catch { /* Cleanup only. */ }
            try { _shovel?.Dispose(); } catch { /* Cleanup only. */ }
            try { _excavation?.Dispose(); } catch { /* Cleanup only. */ }
            _preview = null;
            _shovelInput = null;
            _shovel = null;
            _excavation = null;
        }
    }

    /// <summary>A dig's later result goes to the console and to the HUD, where the shovel's player sees it.</summary>
    private static void Announce(string text)
    {
        ExcavationCommand.Print(text);
        ExcavationShovelInput.Show(text);
    }

    private void OnDestroy()
    {
        try { _preview?.Dispose(); } catch { /* Excavation cleanup never blocks ladder cleanup. */ }
        try { _shovelInput?.Dispose(); } catch { /* Excavation cleanup never blocks ladder cleanup. */ }
        try { _shovel?.Dispose(); } catch { /* Excavation cleanup never blocks ladder cleanup. */ }
        try { _excavation?.Dispose(); } catch { /* Excavation cleanup never blocks ladder cleanup. */ }
        _preview = null;
        _shovelInput = null;
        _shovel = null;
        _excavation = null;
        try { _runtime?.Dispose(); _presenter?.Dispose(); }
        catch (Exception exception)
        {
            Logger.LogError("Ladder cleanup failed: " + SafeFailure.Brief(exception));
        }
        _snap?.Dispose();
        _pieces?.Dispose();
        _trapdoors?.Dispose();
        _trapdoors = null;
        _snap = null;
        _runtime = null;
        _pieces = null;
    }
}
