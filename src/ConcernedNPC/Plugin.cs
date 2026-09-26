using BepInEx;

namespace TheConcernedCat.ConcernedNPC;

/// <summary>Concerned NPC: the runtime the Concerned Cat companions share.
///
/// <b>This package is not a mod you install for what it does.</b> On its own it
/// changes nothing: no patch, no prefab, no command, no world state, no file.
/// Installed by itself it loads, says so once, and stops. It is here so that
/// Hulgi, Thorstein, Gunnar and Sunniva can share one set of answers to the
/// questions that are the same for all of them - who am I after a reload, where
/// is camp, which chests may I use, what is my job, how do I plan it, what do I
/// carry, and what happens when I am interrupted - and so that a fix to those
/// answers can ship without rebuilding every mod that relies on them.
///
/// <b>What it deliberately does not know.</b> Nothing about the roles. It has
/// no idea what a cart is, what resin is for, or why anyone would want a
/// shelter. Roles register what they want done; this decides how it is planned,
/// carried, executed and recovered. A type in here that mentions a role by name
/// is a defect.
///
/// <b>Why it is a plugin at all</b>, given that it patches nothing: so that a
/// consumer can declare <c>BepInDependency</c> on this GUID. Then a player who
/// installs Concerned Foreman without this package is told so by BepInEx at
/// load, in one line, instead of meeting a null reference somewhere later in
/// their evening.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.theconcernedcat.valheim.concernednpc";
    public const string PluginName = "Concerned NPC";
    public const string PluginVersion = "0.2.0";

    private void Awake()
    {
        // Which build this actually is, not just which version it claims: a
        // test profile keeps whatever DLL was last copied into it, and two
        // builds of one version read alike in the log without the commit.
        Logger.LogInfo(
            $"{PluginName} {PluginVersion} loaded. Release: ConcernedNPC@{ResolveInformationalVersion()}.");
        Logger.LogInfo(
            "Concerned NPC is a shared runtime. On its own it does nothing: it patches nothing, " +
            "registers nothing and writes nothing. Install a Concerned Cat NPC mod to use it.");
    }

    /// <summary>The release identity including the build commit (the SDK stamps
    /// InformationalVersion as "0.2.0+&lt;sha&gt;"), so the load line names the
    /// exact binary and nothing about the player.</summary>
    private static string ResolveInformationalVersion()
    {
        try
        {
            return System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Plugin).Assembly)
                ?.InformationalVersion ?? PluginVersion;
        }
        catch
        {
            // The plain version is an acceptable release identity fallback.
            return PluginVersion;
        }
    }
}
