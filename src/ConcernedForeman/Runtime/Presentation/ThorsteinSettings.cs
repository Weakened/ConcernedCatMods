using BepInEx.Configuration;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>The <c>Thorstein</c> configuration section (#295, #298).
///
/// The figure is part of the settlement half, so it is shown only when
/// <c>Settlement/SettlementRuntimeEnabled</c> is on as well; this switch is for
/// somebody who has the settlement on and would rather not see him at home.
/// The four appearance entries are the player's override of the owner's stock
/// reference. An explicit choice is never overwritten, and an empty entry means
/// the reference (#298).</summary>
internal sealed class ThorsteinSettings
{
    private ThorsteinSettings(
        ConfigEntry<bool> showAtHome,
        ConfigEntry<string> hair,
        ConfigEntry<string> beard,
        ConfigEntry<string> chest,
        ConfigEntry<string> legs,
        ConfigEntry<bool> workerLook)
    {
        ShowAtHome = showAtHome;
        Hair = hair;
        Beard = beard;
        ChestGarment = chest;
        LegsGarment = legs;
        WorkerLook = workerLook;
    }

    internal ConfigEntry<bool> ShowAtHome { get; }

    internal ConfigEntry<string> Hair { get; }

    internal ConfigEntry<string> Beard { get; }

    internal ConfigEntry<string> ChestGarment { get; }

    internal ConfigEntry<string> LegsGarment { get; }

    /// <summary>#295 S6 (owner decision 2026-10-29): his own hair and beard on
    /// his worker body, locally. On by default so the playtest judges it.</summary>
    internal ConfigEntry<bool> WorkerLook { get; }

    internal static ThorsteinSettings Bind(ConfigFile config)
    {
        ConfigEntry<bool> show = config.Bind(
            "Thorstein",
            "ShowAtHome",
            true,
            "While Thorstein is recruited, resting, and has no worker body in the world, show him at your " +
            "bed (or the world start) as a local figure. Nobody else sees it and nothing is saved. When he " +
            "is given a worker body (cf_worker spawn) the figure goes first; the two never exist together. " +
            "Needs Settlement/SettlementRuntimeEnabled.");

        ConfigEntry<string> hair = config.Bind(
            "Thorstein",
            "Hair",
            string.Empty,
            "Empty for the owner's reference (\"Mullet\"). Otherwise a hair label as the character screen " +
            "shows it, or its prefab name. A name this build does not have falls back to the reference.");

        ConfigEntry<string> beard = config.Bind(
            "Thorstein",
            "Beard",
            string.Empty,
            "Empty for the owner's reference (\"Mustache\"). Otherwise a beard label or prefab name.");

        ConfigEntry<string> chest = config.Bind(
            "Thorstein",
            "ChestGarment",
            string.Empty,
            "Empty for the default leather tunic. Otherwise a chest item's label or prefab name. Clothing " +
            "only: nothing is given to him or taken from anyone.");

        ConfigEntry<string> legs = config.Bind(
            "Thorstein",
            "LegsGarment",
            string.Empty,
            "Empty for the default leather pants. Otherwise a legs item's label or prefab name.");

        ConfigEntry<bool> workerLook = config.Bind(
            "Thorstein",
            "WorkerLook",
            true,
            "While Thorstein works, put his own hair and beard (the Hair and Beard settings above) on his " +
            "worker body, locally. The worker body itself is unchanged: same creature, nothing saved, nothing " +
            "networked. A piece that will not attach or does not fit on that body is taken off and he keeps the " +
            "plain look. Applies the next time his worker body loads.");

        return new ThorsteinSettings(show, hair, beard, chest, legs, workerLook);
    }
}
