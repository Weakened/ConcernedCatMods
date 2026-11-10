using BepInEx.Configuration;
using UnityEngine;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Config;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Profiles;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;
using Defaults = TheConcernedCat.ConcernedTeamster.Domain.Config.TeamsterDefaults;

namespace TheConcernedCat.ConcernedTeamster;

/// <summary>Configuration binding for Concerned Teamster (CT-001). Only
/// settings whose behavior already exists are bound; feature settings arrive
/// with their features so the config file never advertises unimplemented
/// behavior. Telemetry bounds (CT-003) mirror the domain's hard limits: the
/// config UI clamps via AcceptableValueRange and the sampler re-clamps in
/// code, so no config edit can remove the budget.</summary>
internal sealed class TeamsterSettings
{
    private TeamsterSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<float> sampleIntervalSeconds,
        ConfigEntry<float> searchRadiusMeters,
        ConfigEntry<int> maxCartsPerTick,
        ConfigEntry<int> maxTrackedCarts,
        ConfigEntry<KeyboardShortcut> panelShortcut,
        ConfigEntry<bool> panelWarningsEnabled,
        ConfigEntry<bool> hudWarningHintsEnabled,
        ConfigEntry<float> steepGradeCautionPercent,
        ConfigEntry<int> riskLookaheadPoints,
        ConfigEntry<bool> brakeEnabled,
        ConfigEntry<bool> tripsEnabled,
        ConfigEntry<float> tripRecordSpacingSeconds,
        ConfigEntry<int> tripMaxSamplesPerTrip,
        ConfigEntry<int> tripMaxTripsRetained,
        ConfigEntry<float> uiScale,
        ConfigEntry<bool> onboardingDismissed,
        ConfigEntry<ConfigProfile> activeProfile,
        ConfigEntry<ConfigProfile> lastAppliedProfile,
        ConfigEntry<int> schemaVersion,
        ConfigEntry<bool> gunnarHaulingEnabled,
        ConfigEntry<bool> gunnarCollectionEnabled,
        ConfigEntry<bool> gunnarChestTakeEnabled,
        ConfigEntry<bool> gunnarCartTourEnabled,
        ConfigEntry<bool> gunnarChestHaulEnabled,
        ConfigEntry<bool> gunnarFellsSmallTrees,
        ConfigEntry<bool> gunnarIdleHammerEnabled,
        ConfigEntry<KeyboardShortcut> containerPermissionShortcut,
        ConfigEntry<GunnarPullStrength> gunnarPullStrength,
        ConfigEntry<string> workerBaseCreature)
    {
        GunnarHaulingEnabled = gunnarHaulingEnabled;
        GunnarCollectionEnabled = gunnarCollectionEnabled;
        GunnarChestTakeEnabled = gunnarChestTakeEnabled;
        GunnarCartTourEnabled = gunnarCartTourEnabled;
        GunnarChestHaulEnabled = gunnarChestHaulEnabled;
        GunnarFellsSmallTrees = gunnarFellsSmallTrees;
        GunnarIdleHammerEnabled = gunnarIdleHammerEnabled;
        ContainerPermissionShortcut = containerPermissionShortcut;
        GunnarPullStrength = gunnarPullStrength;
        WorkerBaseCreature = workerBaseCreature;
        Enabled = enabled;
        DebugLogging = debugLogging;
        SampleIntervalSeconds = sampleIntervalSeconds;
        SearchRadiusMeters = searchRadiusMeters;
        MaxCartsPerTick = maxCartsPerTick;
        MaxTrackedCarts = maxTrackedCarts;
        PanelShortcut = panelShortcut;
        PanelWarningsEnabled = panelWarningsEnabled;
        HudWarningHintsEnabled = hudWarningHintsEnabled;
        SteepGradeCautionPercent = steepGradeCautionPercent;
        RiskLookaheadPoints = riskLookaheadPoints;
        BrakeEnabled = brakeEnabled;
        TripsEnabled = tripsEnabled;
        TripRecordSpacingSeconds = tripRecordSpacingSeconds;
        TripMaxSamplesPerTrip = tripMaxSamplesPerTrip;
        TripMaxTripsRetained = tripMaxTripsRetained;
        UiScale = uiScale;
        OnboardingDismissed = onboardingDismissed;
        ActiveProfile = activeProfile;
        LastAppliedProfile = lastAppliedProfile;
        SchemaVersion = schemaVersion;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> DebugLogging { get; }
    public ConfigEntry<float> SampleIntervalSeconds { get; }
    public ConfigEntry<float> SearchRadiusMeters { get; }
    public ConfigEntry<int> MaxCartsPerTick { get; }
    public ConfigEntry<int> MaxTrackedCarts { get; }
    public ConfigEntry<KeyboardShortcut> PanelShortcut { get; }
    public ConfigEntry<bool> PanelWarningsEnabled { get; }
    public ConfigEntry<bool> HudWarningHintsEnabled { get; }
    public ConfigEntry<float> SteepGradeCautionPercent { get; }
    public ConfigEntry<int> RiskLookaheadPoints { get; }
    public ConfigEntry<bool> BrakeEnabled { get; }
    public ConfigEntry<bool> TripsEnabled { get; }
    public ConfigEntry<float> TripRecordSpacingSeconds { get; }
    public ConfigEntry<int> TripMaxSamplesPerTrip { get; }
    public ConfigEntry<int> TripMaxTripsRetained { get; }

    /// <summary>Uniform scale applied to every panel's root transform
    /// (CT-033) — text, buttons, and the panel itself all grow or shrink
    /// together, so nothing can clip relative to its own container. Applies
    /// on the panel's next build (world enter or first open), not live to
    /// an already-open panel.</summary>
    public ConfigEntry<float> UiScale { get; }

    /// <summary>Set once, permanently, when the player taps the first-run
    /// onboarding hint (CT-034). Never reset by the mod itself.</summary>
    public ConfigEntry<bool> OnboardingDismissed { get; }

    /// <summary>The player's chosen settings preset (CT-034). Changing this
    /// value re-applies the preset's settings once, on the next plugin
    /// load; individual settings stay independently editable afterward.</summary>
    public ConfigEntry<ConfigProfile> ActiveProfile { get; }

    /// <summary>Internal bookkeeping — do not edit directly. Records which
    /// profile was last actually applied, so <see cref="ActiveProfile"/>
    /// re-applies only when it genuinely changes (CT-034's idempotence
    /// guarantee) instead of overwriting manually-tweaked settings on every
    /// restart.</summary>
    public ConfigEntry<ConfigProfile> LastAppliedProfile { get; }

    /// <summary>Internal bookkeeping — do not edit (CT-039). Tracks which
    /// version of Teamster's config schema this file was last written by,
    /// so a future update can migrate safely instead of guessing whether
    /// this file predates a renamed or restructured key. Defaults to
    /// <see cref="ConfigSchemaVersion.PreVersioning"/> so every install
    /// from before this concept existed genuinely migrates once, for
    /// real, on its first post-upgrade load — see
    /// <c>Plugin.ApplyConfigSchemaMigrationIfNeeded</c>.</summary>
    public ConfigEntry<int> SchemaVersion { get; }

    /// <summary>Workers (#313): Gunnar's opt-in hauling runtime. Off by default;
    /// installing Teamster for telemetry never enrols anyone in a feature that
    /// moves a real cart (docs/settlement/cart-and-collection/DECISIONS.md D3).
    /// </summary>
    public ConfigEntry<bool> GunnarHaulingEnabled { get; }

    /// <summary>Workers (#381): Gunnar's opt-in collection runtime. Off by
    /// default, and a switch of its own rather than hauling's: picking things up
    /// is a separate capability under a separate owner decision (2026-09-19), and
    /// a player who wanted a cart pulled has not thereby asked for anything to be
    /// picked up.</summary>
    public ConfigEntry<bool> GunnarCollectionEnabled { get; }

    /// <summary>Workers (#381, `DECISIONS.md` D20): whether Gunnar may take from a
    /// chest the player explicitly marked Take or Both. Off by default, its own
    /// switch, and it needs <see cref="GunnarCollectionEnabled"/> as well.</summary>
    public ConfigEntry<bool> GunnarChestTakeEnabled { get; }

    /// <summary>Workers (#381, `DECISIONS.md` D22): whether Gunnar's collection
    /// tour may put what it picks into the cart the player assigned him, parked
    /// inside the area, instead of walking each load to a chest. Off by default,
    /// its own switch, and it needs <see cref="GunnarCollectionEnabled"/> and
    /// <see cref="GunnarHaulingEnabled"/> as well.</summary>
    public ConfigEntry<bool> GunnarCartTourEnabled { get; }

    /// <summary>Workers (#381 item 4): whether Gunnar may walk what the player
    /// names from the chest they chose to take from (D20) to the chest they
    /// chose to put things in (D15), on foot, in planned tours. Off by default,
    /// its own switch, and it needs <see cref="GunnarCollectionEnabled"/> and
    /// <see cref="GunnarChestTakeEnabled"/> as well.</summary>
    public ConfigEntry<bool> GunnarChestHaulEnabled { get; }

    /// <summary>Workers (#381 item 5, Gunnar's half of `DECISIONS.md` D19):
    /// whether Gunnar may fell a small beech or a small fir inside the area you
    /// set, one hit per <c>ct_collect fell</c>, with an axe he carries, taking
    /// only the Wood his own hit dropped. Off by default, its own switch, and it
    /// needs <see cref="GunnarCollectionEnabled"/> as well.</summary>
    public ConfigEntry<bool> GunnarFellsSmallTrees { get; }

    /// <summary>Workers (#381, owner decision 2026-10-31, D21): while Gunnar is
    /// idle beside the cart assigned to him, he now and then walks over, faces it
    /// and plays the vanilla hammer swing. Cosmetic only. Effective only while
    /// <see cref="GunnarHaulingEnabled"/> is on - which is off by default - so
    /// it is read only by an already opted-in Gunnar. <b>Off</b> by default all
    /// the same, like each of the owner's 2026-10-31 grants.</summary>
    public ConfigEntry<bool> GunnarIdleHammerEnabled { get; }

    /// <summary>#374: while looking at a container, press this to change what
    /// Gunnar may do with it. The four states are cycled in one order - off,
    /// take, deposit, both, off - and OFF is where every container starts.
    /// Vanilla's own Use is untouched: this adds a key, it does not replace an
    /// interaction.</summary>
    public ConfigEntry<KeyboardShortcut> ContainerPermissionShortcut { get; }

    /// <summary>Workers (#313): Gunnar's pulling strength. MatchPlayer is the only
    /// supported value (D5); anything else refuses to haul.</summary>
    public ConfigEntry<GunnarPullStrength> GunnarPullStrength { get; }

    /// <summary>Workers (#313): the vanilla creature Gunnar's worker body is
    /// cloned from. A prefab name is data, resolved at run time and failing
    /// closed; read at plugin start.</summary>
    public ConfigEntry<string> WorkerBaseCreature { get; }

    public static TeamsterSettings Bind(ConfigFile config)
    {
        return new TeamsterSettings(
            config.Bind("General", "Enabled", Defaults.Enabled,
                "Master switch for Concerned Teamster cart telemetry features. Disabling leaves carts fully vanilla; the mod never changes cart physics by default either way."),
            config.Bind("Diagnostics", "DebugLogging", Defaults.DebugLogging,
                "Write diagnostic messages for Teamster feature development. Never enables per-frame logging."),
            config.Bind("Telemetry", "SampleIntervalSeconds",
                TelemetrySamplerOptions.DefaultSampleIntervalSeconds,
                new ConfigDescription(
                    "Seconds between cart telemetry samples. Read-only observation of nearby carts; between samples the cost is one comparison per frame.",
                    new AcceptableValueRange<float>(
                        TelemetrySamplerOptions.MinSampleIntervalSeconds,
                        TelemetrySamplerOptions.MaxSampleIntervalSeconds))),
            config.Bind("Telemetry", "SearchRadiusMeters",
                TelemetrySamplerOptions.DefaultSearchRadiusMeters,
                new ConfigDescription(
                    "Carts farther than this from you are not observed.",
                    new AcceptableValueRange<float>(
                        TelemetrySamplerOptions.MinSearchRadiusMeters,
                        TelemetrySamplerOptions.MaxSearchRadiusMeters))),
            config.Bind("Telemetry", "MaxCartsPerTick",
                TelemetrySamplerOptions.DefaultMaxCartsPerTick,
                new ConfigDescription(
                    "Hard budget of carts examined per telemetry tick.",
                    new AcceptableValueRange<int>(
                        TelemetrySamplerOptions.MinMaxCartsPerTick,
                        TelemetrySamplerOptions.MaxMaxCartsPerTick))),
            config.Bind("Telemetry", "MaxTrackedCarts",
                TelemetrySamplerOptions.DefaultMaxTrackedCarts,
                new ConfigDescription(
                    "Hard cap of carts kept in the telemetry table at once.",
                    new AcceptableValueRange<int>(
                        TelemetrySamplerOptions.MinMaxTrackedCarts,
                        TelemetrySamplerOptions.MaxMaxTrackedCarts))),
            config.Bind("Ui", "PanelShortcut", KeyboardShortcut.Empty,
                "Optional keyboard accelerator that toggles the Cart Status panel. The visible Cart button is always the primary path; leave empty for no shortcut."),
            config.Bind("Warnings", "PanelWarningsEnabled", Defaults.PanelWarningsEnabled,
                "Show load/grade warnings in the Cart Status panel."),
            config.Bind("Warnings", "HudWarningHintsEnabled", Defaults.HudWarningHintsEnabled,
                "Also show the current warning as a small HUD hint under the Cart button while pulling. Off by default: the panel is the primary surface and HUD space is precious; enable if you haul with the panel closed."),
            config.Bind("Warnings", "SteepGradeCautionPercent",
                WarningOptions.DefaultSteepGradeCautionPercent,
                new ConfigDescription(
                    "Smoothed climbing grade (percent) that raises the steep-climb caution. Exit hysteresis and anti-flicker hold are fixed so no configuration can make warnings spam.",
                    new AcceptableValueRange<float>(
                        WarningOptions.MinSteepGradeCautionPercent,
                        WarningOptions.MaxSteepGradeCautionPercent))),
            config.Bind("Risk", "LookaheadPoints",
                Domain.Risk.LookaheadOptions.DefaultPoints,
                new ConfigDescription(
                    "Terrain samples taken ahead of the pulled cart (4 m apart) to rate the upcoming descent. 0 disables lookahead. Each sample is one bounded ground-height read per telemetry tick.",
                    new AcceptableValueRange<int>(
                        Domain.Risk.LookaheadOptions.MinPoints,
                        Domain.Risk.LookaheadOptions.MaxPoints))),
            config.Bind("Brake", "Enabled", Defaults.BrakeEnabled,
                "The parking brake feature: a visible button that freezes a parked cart you control until you release it. Explicit per-use, always reversible, never saved into the world, and it releases itself on detach distance, world exit, shutdown, or any capability loss. Disable to remove the button entirely."),
            config.Bind("Trips", "Enabled", Defaults.TripsEnabled,
                "Record hauling trips (position, grade, speed, load while you pull) into Teamster's own per-world sidecar file under BepInEx/config. Never touches Valheim saves; delete the sidecar folder to erase all history."),
            config.Bind("Trips", "RecordSpacingSeconds",
                Domain.Trips.TripRecorderOptions.DefaultRecordSpacingSeconds,
                new ConfigDescription(
                    "Seconds between recorded trip samples while pulling.",
                    new AcceptableValueRange<float>(
                        Domain.Trips.TripRecorderOptions.MinRecordSpacingSeconds,
                        Domain.Trips.TripRecorderOptions.MaxRecordSpacingSeconds))),
            config.Bind("Trips", "MaxSamplesPerTrip",
                Domain.Trips.TripRecorderOptions.DefaultMaxSamplesPerTrip,
                new ConfigDescription(
                    "Hard cap of samples in one trip; longer hauls split into segments.",
                    new AcceptableValueRange<int>(
                        Domain.Trips.TripRecorderOptions.MinMaxSamplesPerTrip,
                        Domain.Trips.TripRecorderOptions.MaxMaxSamplesPerTrip))),
            config.Bind("Trips", "MaxTripsRetained",
                Domain.Trips.TripRecorderOptions.DefaultMaxTripsRetained,
                new ConfigDescription(
                    "Retention: how many trips each world's sidecar keeps; the oldest are pruned first.",
                    new AcceptableValueRange<int>(
                        Domain.Trips.TripRecorderOptions.MinMaxTripsRetained,
                        Domain.Trips.TripRecorderOptions.MaxMaxTripsRetained))),
            config.Bind("Ui", "Scale", UiScaleOptions.DefaultScale,
                new ConfigDescription(
                    "Personal size preference for every Teamster panel, button, and font, layered on top " +
                    "of an automatic baseline the mod already applies for your display (Jötunn's panel " +
                    "canvas is a fixed pixel size, so panels would otherwise render far smaller than " +
                    "intended on a high-resolution or high-DPI screen). 1.0 is the default preference; " +
                    "takes effect the next time a panel opens (or on world enter for the always-visible " +
                    "Cart button), not live on an already-open panel.",
                    new AcceptableValueRange<float>(UiScaleOptions.MinScale, UiScaleOptions.MaxScale))),
            config.Bind("Onboarding", "Dismissed", Defaults.OnboardingDismissed,
                "Set automatically once you dismiss the first-run hint pointing at the Cart button. " +
                "Set back to false to see the hint again."),
            config.Bind("General", "Profile", Defaults.DefaultProfile,
                "A documented settings preset: Minimal (telemetry only, warnings/HUD hints/trips off), " +
                "Standard (Teamster's normal defaults), or EverythingObservational (every read-only " +
                "feature on). The parking brake stays off in every profile — enable Brake.Enabled " +
                "yourself if you want it. Changing this re-applies the preset's values once, on the " +
                "next load; your own edits to individual settings are never overwritten again until " +
                "you change this value to something else."),
            config.Bind("General", "LastAppliedProfile", Defaults.DefaultProfile,
                "Internal bookkeeping — do not edit. Records which profile was last applied so a " +
                "restart with no Profile change never re-applies it."),
            config.Bind("Internal", "ConfigSchemaVersion", ConfigSchemaVersion.PreVersioning,
                "Internal bookkeeping — do not edit. Tracks which version of Teamster's config " +
                "schema this file was last written by, so a future update can migrate safely."),
            config.Bind("Workers", "GunnarHaulingEnabled", GunnarHaulingDefaults.HaulingEnabled,
                "Let Gunnar, the Teamster's worker, hitch himself to a cart you explicitly assign and pull it " +
                "through the game's own cart attach, walking like any creature. OFF by default. Works only in " +
                "single player or as the host with nobody else connected; he never takes ownership, never " +
                "touches the parking brake, never changes cart mass or physics and never teleports anything."),
            config.Bind("Workers", "GunnarCollectionEnabled", GunnarCollectionDefaults.CollectionEnabled,
                "Let Gunnar pick up a loose stone or a fallen branch you point at, through the source's own " +
                "vanilla pickup, and carry what it gives in his own inventory. OFF by default, and separate " +
                "from GunnarHaulingEnabled. Those two things are the only ones he will touch: no plants, no " +
                "food, no chests, no piles somebody dropped, no trees. Works only in single player or as the " +
                "host with nobody else connected, only on a source this session already controls - he never " +
                "takes control of one - and only while he is standing next to it; nothing here moves him."),
            config.Bind("Workers", "GunnarChestTakeEnabled", GunnarCollectionDefaults.ChestTakeEnabled,
                "Let Gunnar take an item you name out of a chest you chose and marked for taking (or both), into " +
                "his own inventory, counting both sides and leaving whatever does not fit in the chest. OFF by " +
                "default, and it also needs GunnarCollectionEnabled. He never takes from a chest you have not " +
                "marked, never picks a chest himself, never takes everything, and works only in single player or " +
                "as the host with nobody else connected, standing next to the chest."),
            config.Bind("Workers", "GunnarCartTourEnabled", GunnarCollectionDefaults.CartTourEnabled,
                "Let Gunnar's collection tour put what it picks into the cart you assigned him ('ct_collect start " +
                "cart'), parked inside the area, counting both sides and keeping whatever does not fit, instead of " +
                "walking each load to a chest. OFF by default, and it also needs GunnarCollectionEnabled and " +
                "GunnarHaulingEnabled. He never chooses a cart, never fills one that is hitched, open, moving or " +
                "busy, never puts in anything but what that tour picked, and never empties the cart - that is " +
                "yours. Single player or the host with nobody else connected."),
            config.Bind("Workers", "GunnarChestHaulEnabled", GunnarCollectionDefaults.ChestHaulEnabled,
                "Let Gunnar haul what you name ('ct_collect haul Stone:40 Wood') from the chest you chose to take " +
                "from to the chest you chose to put things in, on foot: he plans the tours first, then for each one " +
                "takes his load at the first chest, walks it to the second, puts it in and counts what he still " +
                "carries. OFF by default, and it also needs GunnarCollectionEnabled and GunnarChestTakeEnabled. " +
                "Both chests must carry your mark (Take or Both, Deposit or Both); he never chooses a chest, never " +
                "uses a cart for it, and stops with the rest on him when the second chest is full. Single player or " +
                "the host with nobody else connected."),
            config.Bind("Workers", "GunnarFellsSmallTrees", GunnarCollectionDefaults.FellingEnabled,
                "Let Gunnar fell a small beech or a small fir inside the area you set ('ct_collect area'), one hit " +
                "each time you run 'ct_collect fell', with an axe he carries (have him take one from a chest you " +
                "marked for taking). He takes only the wood his own hit dropped, and only what he can carry; resin, " +
                "cones and the rest lie on the ground. OFF by default, and it also needs GunnarCollectionEnabled. " +
                "Never a full-size tree, never within 15 m of you, anyone else or anything you built, never where a " +
                "ward denies you, and only while he stands at the tree - nothing here walks him. Single player or the host " +
                "with nobody else connected."),
            config.Bind("Workers", "GunnarIdleHammerEnabled", GunnarHaulingDefaults.IdleHammerEnabled,
                "While Gunnar is idle near the cart you assigned him, he now and then walks over to it, faces it " +
                "and plays the game's own hammer swing. Scenery only: it repairs nothing, uses nothing and changes " +
                "nothing about the cart, and it never happens while the cart is hitched to anyone or he has a job. " +
                "Takes effect only while GunnarHaulingEnabled is on, and under the same rule - single player or " +
                "the host with nobody else connected. OFF by default; with it off he simply stands and waits."),
            config.Bind("Workers", "ContainerPermissionShortcut",
                new KeyboardShortcut(KeyCode.F9),
                "While looking at a chest, press this to choose what Gunnar may do with it: nothing, take " +
                "from it, deposit into it, or both, cycled in that order. EVERY container starts at nothing - " +
                "a chest is yours until you say otherwise, once, per chest - and your choices are kept per " +
                "world beside Teamster's other files, never in the world itself, so nobody else who loads " +
                "that world inherits them. Leave empty for no key, in which case no container can be " +
                "enabled at all."),
            config.Bind("Workers", "GunnarPullStrength", GunnarHaulingDefaults.PullStrength,
                "How strongly Gunnar pulls. MatchPlayer, the only supported value, gives his body your " +
                "character's own base mass, so he pulls a cart exactly as hard as you do. Any other value " +
                "keeps him from hauling."),
            config.Bind("Workers", "WorkerBaseCreature", GunnarHaulingDefaults.WorkerBaseCreature,
                "The vanilla creature Gunnar's worker body is cloned from (its mind, loot and gear removed). " +
                "Read at game start. If it does not exist in your game build, Gunnar is unavailable and the log says why."));
    }
}
