using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.Companions.Dialogue;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Map;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Owns the companion feature inside a running game: the character's
/// progress, the collectible in the world, the introduction panel, and the
/// answer to "may this player use the map tools".
///
/// The director is the only place the shared layer meets Unity, and it is
/// built around one ordering rule it must never break: <b>progress reaches
/// disk before the player sees a consequence of it.</b> The shared
/// <c>CompanionProgress</c> enforces the rule; this type is what would
/// otherwise be tempted to break it, by removing the compass the instant a
/// button was clicked. It removes it only once the save has been confirmed,
/// and if the save fails the compass stays exactly where it was, so the worst
/// outcome of a full disk is a player who has to click the same button again.
///
/// Its second job is to be uninteresting when anything is missing. No world,
/// no profile, no anchor, no prefab, no GUI — each of those ends with the map
/// tools available and the companion quietly absent, never with a locked
/// toolbar.</summary>
internal sealed class CompanionDirector : IDisposable
{
    /// <summary>The collectible sits nearer the anchor than Hulgi will, so a
    /// player who has just woken up walks into it rather than past it.</summary>
    private static readonly PlacementRules CompassRules =
        new PlacementRules(minimumRadius: 2.5f, maximumRadius: 6f, maximumHeightDelta: 2.5f);

    /// <summary>Prefabs to draw the compass from, best first. Every one is a
    /// candidate to look up, never an assumption that it exists.</summary>
    private static readonly string[] CompassPrefabCandidates =
    {
        "Wishbone",
        "SilverNecklace",
        "Amber",
        "Coins",
        "Ruby",
    };

    /// <summary>Vanilla pickables whose twinkle the compass borrows, best first.
    /// Each is looked up; none is assumed.</summary>
    private static readonly string[] SparklePrefabCandidates =
    {
        "Pickable_Stone",
        "Pickable_Flint",
        "Pickable_Branch",
        "Pickable_Mushroom",
        "Pickable_Dandelion",
    };

    /// <summary>Hulgi lives further out than the collectible does: close
    /// enough to be part of the camp, far enough not to stand in a doorway.
    /// The 3-10 m band is CC-NPC-004's own requirement.</summary>
    private static readonly PlacementRules HulgiRules =
        new PlacementRules(minimumRadius: 3f, maximumRadius: 10f);

    /// <summary>Sources to extract an animated body from, best first. Every one
    /// is looked up at runtime and refused if its visual subtree turns out to
    /// carry a networking or AI component; none of them is assumed to
    /// exist.</summary>
    private static readonly string[] ActorPrefabCandidates =
    {
        "Player",
    };

    private const float ScopeRetrySeconds = 2f;
    private const float PresenceIntervalSeconds = 0.25f;
    private const float AnchorRecheckSeconds = 5f;

    /// <summary>How often the actor's residency is reconsidered. Slower than
    /// the collectible's presence pass: a companion who has settled somewhere
    /// should look settled, not twitch towards every re-resolved anchor.</summary>
    private const float ResidencyIntervalSeconds = 2f;

    /// <summary>Backoff after a residency pass found nowhere to put him.</summary>
    private const float ActorRetrySeconds = 8f;

    /// <summary>How often the live world and character are re-checked against
    /// the scope the sidecar is addressed to.</summary>
    private const float ScopeRecheckSeconds = 10f;

    /// <summary>How close the player must be to speak to the companion, and to
    /// hear an ambient remark. Roughly conversational; beyond it he is scenery.</summary>
    private const float TalkRadius = 4.5f;

    /// <summary>Shortest gap between two spoken lines, whatever asked for them.
    /// A companion who answers a mashed key sixty times a second is not
    /// characterful, he is a bug.</summary>
    private const float TalkCooldownSeconds = 1.5f;

    /// <summary>How long to wait after a placement attempt found nowhere to
    /// put the collectible. Each attempt probes several dozen candidates, and
    /// the reason they failed -- water, a wall, unloaded ground -- does not
    /// change between two consecutive frames.</summary>
    private const float PlacementRetrySeconds = 5f;

    /// <summary>How often a settled companion looks up to see whether anyone
    /// has built him somewhere better to sit.
    ///
    /// Half a minute, not the two-second residency tick. The sweep costs a
    /// world probe, and a companion who re-evaluates his seating every two
    /// seconds is the exact restlessness the deterministic planner exists to
    /// avoid. Half a minute is fast enough that building a bench and turning
    /// round is enough to see it work.</summary>
    private const float SeatUpgradeSeconds = 30f;

    /// <summary>How close a new spot must be for him to walk there instead of
    /// being put there. Beyond it - a bed claimed across the island - a walk
    /// would take longer than anybody would wait to see him arrive.</summary>
    private const float WalkRelocateMetres = 30f;

    /// <summary>How often the camp is fingerprinted - fires and whether they
    /// burn, seats, doors and whether they are open - so a change is noticed in
    /// about a second and acted on at once, without re-surveying every spot on
    /// a timer. The full survey stays as a 30-second safety net.</summary>
    private const float CampCheckSeconds = 1f;

    /// <summary>Walking pace. A stroll, not a march: he is crossing a camp, not
    /// going anywhere.</summary>
    private const float StrollSpeed = 1.6f;

    /// <summary>How far from home a stroll may take him. The same band the
    /// planner places him in, so he never appears to leave.</summary>
    private const float StrollNearMetres = 3.5f;

    private const float StrollFarMetres = 9f;

    /// <summary>How many spots he will consider before giving up and staying
    /// seated. Bounded, and the bound is reported in the log rather than
    /// hidden.</summary>
    private const int StrollCandidates = 12;

    /// <summary>How close counts as reaching a corner of a route, as opposed to
    /// its end. Tighter than arrival: a corner is usually a doorway or the edge
    /// of a wall, and cutting it by half a metre walks him into the frame.
    /// </summary>
    private const float CornerArrivalMetres = 0.3f;

    /// <summary>The angle between one stroll's direction and the next. An
    /// irrational-ish turn so the candidates spiral around the camp instead of
    /// retracing a circle.</summary>
    private const float StrollTurnDegrees = 137f;

    /// <summary>How far in front of a seat he stands to raise a toast. Enough
    /// to clear the seat's front edge with his whole body.</summary>
    private const float ToastStepMetres = 0.75f;

    /// <summary>How near somebody must be for him to turn and raise the toast
    /// to them rather than to the camp in general.</summary>
    private const float ToastFaceMetres = 8f;

    /// <summary>What must not be where he stands for a toast. Not Occupied: the
    /// seat he just got up from is right behind him, and the body check in
    /// <see cref="FindToastSpot"/> asks the real question about what stands
    /// there.</summary>
    private const PlacementRejection UnsafeToStand =
        PlacementRejection.NotLoaded | PlacementRejection.Water | PlacementRejection.Unsupported |
        PlacementRejection.TooSteep | PlacementRejection.Fire | PlacementRejection.Doorway |
        PlacementRejection.Bed;

    /// <summary>The one source of chance in his day. Only when he drinks and
    /// whether he toasts ride on it; where he goes and sits stays
    /// deterministic.</summary>
    private static readonly System.Random Dice = new System.Random();


    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly CompanionScopeSource _scopeSource;
    private readonly CartographerLegacyProbe _legacyProbe;
    private readonly SpawnAnchorSource _anchorSource;
    private readonly WorldPlacementProbe _placementProbe;
    private readonly PlacementPlanner _planner = new PlacementPlanner(CompassRules);
    private readonly CompanionSidecarStore _store;
    private readonly CompanionStoryPanel _storyPanel;
    private readonly CompassProximity _proximity = new CompassProximity();
    private readonly PlacementPlanner _hulgiPlanner = new PlacementPlanner(HulgiRules);
    private readonly WorldPlacementProbe _hulgiProbe;
    private readonly BedValidityProbe _bedProbe;
    private readonly CompanionActor _actor;
    private readonly KnownBiomeReader _biomes;
    private readonly DialogueCatalog _catalog = HulgiDialogue.BuildCatalog();
    private DialogueRotation? _dialogue;
    private readonly RateLimitedLog _rateLimited;

    private CompanionProgress? _progress;
    private CompanionAnchor _anchor;
    private GameObject? _compass;
    private BrokenCompassObject? _compassBehaviour;
    private string _compassSource = "none";

    private float _scopeElapsed;
    private float _presenceElapsed;
    private float _anchorElapsed;
    private float _placementRetryElapsed;
    private float _residencyElapsed;
    private float _seatUpgradeElapsed;

    /// <summary>A walk to a new spot, when a nearby rehome or upgrade sent him on
    /// foot instead of rebuilding him there. Settled into on arrival.</summary>
    private PlacementResult _relocation;
    private bool _relocating;
    private float _relocationPatience;

    /// <summary>Where a walk out through a door is: heading for the door,
    /// waiting for it to swing, stepping through, or past it and on his way.
    /// </summary>
    private enum DoorPhase
    {
        None = 0,
        ToDoor = 1,
        Opening = 2,
        Through = 3,
        After = 4,
    }

    private DoorPhase _doorPhase;
    private Door? _exitDoor;
    private Vector3 _doorCentre;
    private Vector3 _doorInner;
    private Vector3 _doorOuter;
    private bool _doorClosedBehind;
    private float _doorTimer;
    private readonly List<Vector3> _afterDoorRoute = new List<Vector3>();
    private readonly List<Vector3> _doorScratch = new List<Vector3>();
    private readonly Collider[] _doorBuffer = new Collider[128];

    /// <summary>How long a door takes to swing open before he walks through.
    /// </summary>
    private const float DoorSwingSeconds = 0.9f;

    private float _campCheckElapsed;
    private int _campSignature;

    /// <summary>Set when the camp fingerprint changed, until the survey it
    /// asked for has run. Lets that one survey happen even mid-wander.</summary>
    private bool _campChanged;
    private readonly Collider[] _campBuffer = new Collider[256];
    private int _campMask = -1;

    private RoutineState _routine = RoutineState.Settled;
    private float _routineElapsed;

    /// <summary>How long ago a look for somewhere dry found nowhere in reach;
    /// infinity when there is none on record. Reset by a clear, dry day, so a
    /// new spell of weather starts with a prompt look.</summary>
    private float _sinceShelterSearchFailed = float.PositiveInfinity;

    private Vector3 _strollTarget;

    /// <summary>The route he is walking: the navmesh's corners from where he
    /// stood to the stroll target, and which corner he is heading for.
    /// </summary>
    private readonly List<Vector3> _strollRoute = new List<Vector3>();
    private int _strollCorner;

    /// <summary>Scratch for asking about a route without committing to it.
    /// </summary>
    private readonly List<Vector3> _routeScratch = new List<Vector3>();

    private int _strollTurn;
    private bool _walkReported;

    private readonly DrinkHabit _drinks = new DrinkHabit(() => Dice.NextDouble());
    private bool _drinkingFailed;

    /// <summary>The plan the furniture sweep just made, kept for the rebuild it
    /// is about to cause. Valid for this residency pass only.</summary>
    private PlacementResult _sweptPlan;
    private bool _sweptPlanValid;
    private float _talkCooldown;
    private float _ambientElapsed;
    private int _conversationTurn;
    private float _actorRetryElapsed;
    private AnchorValidity _anchorValidity = AnchorValidity.Unknown;
    private bool _presentationSupported = true;
    private bool _visibilityApplied = true;
    private bool _toolsOnlyApplied;
    private bool _noticedRecorded;

    /// <summary>The measured stone-circle radius is logged once per session,
    /// not once per placement retry.</summary>
    private bool _compassRingReported;
    private bool _promptVisible;
    private int _resumePage;
    private bool _disposed;

    public CompanionDirector(
        CartographerSettings settings, ManualLogSource log, CompanionStoryPanel storyPanel)
    {
        _settings = settings;
        _log = log;
        _storyPanel = storyPanel;
        _scopeSource = new CompanionScopeSource(log);
        _legacyProbe = new CartographerLegacyProbe(log);
        _anchorSource = new SpawnAnchorSource(log);
        _placementProbe = new WorldPlacementProbe(log, CompassRules.FireComfortRadius);
        _hulgiProbe = new WorldPlacementProbe(log, HulgiRules.FireComfortRadius);
        _bedProbe = new BedValidityProbe(log);
        _actor = new CompanionActor(
            log,
            TalkToCompanion,
            () => _settings.CompanionHairPreset.Value,
            () => _settings.CompanionBeardPreset.Value,
            () => _settings.CompanionChestPreset.Value,
            () => _settings.CompanionLegsPreset.Value);
        _biomes = new KnownBiomeReader(log);
        _store = new CompanionSidecarStore(CartographerLegacyProbe.DataDirectory);
        _rateLimited = new RateLimitedLog(log, 30f);

        _storyPanel.WelcomeChosen = WelcomeCompanion;
        _storyPanel.ToolsOnlyChosen = ChooseToolsOnly;
        _storyPanel.Closed = () => _resumePage = _storyPanel.PageIndex;
    }

    /// <summary>The current answer for the product's added affordances.
    /// Defaults to open and only ever closes for a character this build could
    /// positively identify as new.</summary>
    public CompanionFeatureGate Gate { get; private set; } = CompanionFeatureGate.Unresolved;

    public bool IsUnlocked => Gate.IsUnlocked;

    /// <summary>Everything the console tool reports. Kept as one read-only
    /// snapshot so a diagnostic can never mutate state by asking about it.</summary>
    public CompanionStatus Status => new CompanionStatus(
        _settings.CompanionsEnabled.Value,
        _progress != null,
        _progress?.Scope.ToString() ?? "<unresolved>",
        _progress?.QuestState ?? QuestState.Unstarted,
        Gate.IsUnlocked,
        Gate.Reason,
        _progress?.Decision.Reason ?? UnlockReason.NotUnlocked,
        _progress?.LoadOutcome ?? SidecarLoadOutcome.Missing,
        _progress?.Notice,
        _anchor.Kind,
        _anchorSource.ResolvedStartLocationName,
        _compass != null,
        _compassSource,
        _compassBehaviour?.OnNonSolidLayer ?? false,
        _compassBehaviour?.HoverObserved ?? false,
        _settings.CompanionToolsOnly.Value,
        _settings.CompanionVisible.Value,
        _anchorValidity,
        _bedProbe.LookupUnavailable,
        _presentationSupported,
        _actor.Exists,
        _actor.Report?.ToString(),
        _hulgiProbe.SeatSeen,
        DescribeSeating(),
        _biomes.LastObserved,
        _catalog.Count,
        _settings.CompanionAmbientChatter.Value);

    /// <summary>Called when a world becomes available. Drops any previous
    /// world's state; the next tick resolves the new one.</summary>
    public void OnWorldChanged()
    {
        ReleaseCompass();
        _actor.Release();
        _sinceShelterSearchFailed = float.PositiveInfinity;
        _anchorValidity = AnchorValidity.Unknown;
        _presentationSupported = true;
        _actorRetryElapsed = 0f;
        _progress = null;
        _anchor = CompanionAnchor.None;
        _scopeElapsed = ScopeRetrySeconds;
        _anchorElapsed = AnchorRecheckSeconds;
        _noticedRecorded = false;
        _resumePage = 0;
        _toolsOnlyApplied = false;
        _proximity.Reset();
        Gate = CompanionFeatureGate.Unresolved;
    }

    /// <summary>Scene teardown: every GameObject this owns is already gone, so
    /// the references are dropped rather than destroyed again.</summary>
    public void OnSceneTeardown()
    {
        // Unity already destroyed every GameObject in the old scene. Calling
        // Destroy on them again would be talking to a corpse, so the references
        // are dropped instead.
        _actor.Forget();

        // The speech widget lives on Chat.instance, which does not survive a
        // return to the main menu. Forgetting it forces a fresh look-up rather
        // than an invoke against a destroyed component.
        OverheadSpeech.Forget();
        _compass = null;
        _compassBehaviour = null;
        _compassSource = "none";
        _promptVisible = false;
        _storyPanel.ResetForSceneChange();
    }

    /// <summary>The whole mod was switched off mid-session. Take the
    /// collectible out of the world, close the story, and leave the gate OPEN:
    /// a disabled mod may never be the reason something is unavailable.</summary>
    public void Suspend()
    {
        Gate = CompanionFeatureGate.CompanionsDisabled;
        if (_compass != null)
        {
            ReleaseCompass();
        }

        _actor.Release();

        if (_storyPanel.IsVisible)
        {
            _storyPanel.Hide();
        }
    }

    public void Tick(float deltaTime)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_settings.CompanionsEnabled.Value)
            {
                // Opted out entirely: behave exactly like a build without this
                // feature — tools open, nothing in the world, no story.
                Gate = CompanionFeatureGate.CompanionsDisabled;
                if (_compass != null)
                {
                    ReleaseCompass();
                }

                _actor.Release();

                if (_storyPanel.IsVisible)
                {
                    _storyPanel.Hide();
                }

                return;
            }

            if (_scopeSource.CapabilityMissing)
            {
                Gate = CompanionFeatureGate.NoScope;
                return;
            }

            if (_progress == null)
            {
                _scopeElapsed += deltaTime;
                if (_scopeElapsed >= ScopeRetrySeconds)
                {
                    _scopeElapsed = 0f;
                    TryOpenProgress();
                }

                if (_progress == null)
                {
                    Gate = CompanionFeatureGate.Unresolved;
                    return;
                }
            }

            ApplyToolsOnlyPreference();

            _scopeElapsed += deltaTime;
            if (_scopeElapsed >= ScopeRecheckSeconds)
            {
                _scopeElapsed = 0f;
                ReopenIfScopeWentStale();
                if (_progress == null)
                {
                    Gate = CompanionFeatureGate.Unresolved;
                    return;
                }
            }

            Gate = CompanionFeatureGate.FromUnlock(_progress!.IsUnlocked);

            _anchorElapsed += deltaTime;
            if (_anchorElapsed >= AnchorRecheckSeconds)
            {
                _anchorElapsed = 0f;
                RefreshAnchor();
            }

            if (_placementRetryElapsed > 0f)
            {
                _placementRetryElapsed -= deltaTime;
            }

            _presenceElapsed += deltaTime;
            if (_presenceElapsed >= PresenceIntervalSeconds)
            {
                _presenceElapsed = 0f;
                UpdateCollectiblePresence();
            }

            _seatUpgradeElapsed += deltaTime;

            _campCheckElapsed += deltaTime;
            if (_campCheckElapsed >= CampCheckSeconds)
            {
                _campCheckElapsed = 0f;
                NoticeCampChanges();
            }

            _residencyElapsed += deltaTime;
            if (_residencyElapsed >= ResidencyIntervalSeconds)
            {
                _residencyElapsed = 0f;
                UpdateResidency();
            }

            UpdateRoutine(deltaTime);
            UpdateDrinking(deltaTime);
            UpdateProximityAndInput();
            UpdateCompanionInput();
            UpdateAmbientChatter(deltaTime);
        }
        catch (Exception exception)
        {
            // A companion failure must never take the map tools with it.
            Gate = CompanionFeatureGate.NoScope;
            _rateLimited.Error(
                "companion-tick",
                "The companion feature hit an error and stepped aside for this session; every map " +
                $"tool stays available: {SafeLogText.Describe(exception)}");
        }
    }

    private void TryOpenProgress()
    {
        if (!_scopeSource.TryResolve(out CompanionScope scope))
        {
            return;
        }

        LegacyEvidence evidence = _legacyProbe.Evaluate(scope.World.Value);

        _progress = CompanionProgress.Open(
            _store,
            scope,
            CartographerCompanions.BrokenCompass,
            evidence,
            _settings.CompanionToolsOnly.Value);
        _toolsOnlyApplied = _settings.CompanionToolsOnly.Value;

        // A per-character starting offset, so two characters in the same world
        // do not hear the catalogue in the same order. Deterministic, so one
        // character's order is reproducible across sessions.
        int offset = unchecked(
            (scope.Character.Value.GetHashCode() * 397) ^ scope.World.Value.GetHashCode());
        _dialogue = new DialogueRotation(_catalog, startOffset: offset);
        _conversationTurn = 0;

        _log.LogInfo(
            $"Companion progress opened: quest {_progress.QuestState}, " +
            $"access {(_progress.IsUnlocked ? "granted" : "pending introduction")} " +
            $"({_progress.Decision.Reason}), prior-data evidence {evidence}.");

        if (_progress.Notice != null)
        {
            ShowNotice(_progress.Notice);
        }
    }

    /// <summary>Mirrors the config toggle into the session. Only acts on an
    /// actual change, so a player who never touches it never re-resolves.</summary>
    private void ApplyToolsOnlyPreference()
    {
        bool desired = _settings.CompanionToolsOnly.Value;
        if (desired == _toolsOnlyApplied || _progress == null)
        {
            return;
        }

        _toolsOnlyApplied = desired;
        _progress.SetToolsOnlyPreference(desired);
        _log.LogInfo(
            desired
                ? "Tools-only is on: the map tools are available and the introduction is not offered."
                : "Tools-only is off: the introduction is offered again. Access already granted stays granted.");
    }

    /// <summary>Re-resolves home, asking the world whether a claimed bed is
    /// still standing.
    ///
    /// The three-way validity answer is what keeps this quiet. A bed whose
    /// chunk is not loaded returns Unknown, which keeps the bed; only loaded
    /// ground with no bed in it moves the companion to the world's starting
    /// point. Without that distinction, walking two biomes from home would
    /// relocate him every time.</summary>
    private void RefreshAnchor()
    {
        bool haveBed = _anchorSource.TryGetClaimedBed(out CompanionAnchor bed);
        _anchorValidity = haveBed
            ? _bedProbe.Check(new Vector3(bed.Position.X, bed.Position.Y, bed.Position.Z))
            : AnchorValidity.Gone;

        _anchorSource.TryGetDefaultSpawn(out CompanionAnchor fallback);

        CompanionAnchor current = ResidencyPlanner.Resolve(
            haveBed ? bed : CompanionAnchor.None, _anchorValidity, fallback);
        if (!current.IsValid)
        {
            return;
        }

        if (!_anchor.IsValid || _planner.ShouldReposition(_anchor, current))
        {
            _anchor = current;

            // A moved anchor invalidates where the collectible was standing.
            // Dropping it lets the next presence pass rebuild it at the new
            // home; the quest is untouched, so nothing a player did is lost.
            _placementRetryElapsed = 0f;
            if (_compass != null)
            {
                ReleaseCompass();
            }
        }
    }

    /// <summary>Decides whether Hulgi should be standing somewhere, and puts
    /// him there.
    ///
    /// Everything this can do is presentation. Hiding him, failing to build
    /// him, or finding nowhere to put him changes nothing about the player's
    /// access, their progress, or the fact that he joined — which is the
    /// property the whole epic is built on.</summary>
    private void UpdateResidency()
    {
        if (_progress == null)
        {
            return;
        }

        ApplyVisibilityPreference();

        // A swept plan is good for one pass. Anything that defers the rebuild
        // to a later pass must re-plan against the world as it is then.
        _sweptPlanValid = false;

        // A frame has passed since anything was built, so a skinned preset has
        // been posed at least once and can now be asked where it actually is.
        _actor.VerifyAppearance();

        var inputs = new ResidencyInputs(
            _progress.HasCompanion,
            _settings.CompanionVisible.Value,
            _actor.Exists,
            _presentationSupported,
            _anchor,
            _actor.PlacedAnchor,
            _anchorValidity,
            ReadSeatStatus(),
            ReadSeatUpgrade());

        ResidencyAction action = ResidencyPlanner.Decide(inputs);

        // On his way somewhere: only a reason to take him away entirely
        // interrupts. Everything else waits until he has sat down.
        if (_relocating)
        {
            if (action == ResidencyAction.Remove)
            {
                _relocating = false;
                _actor.Release();
            }

            return;
        }

        switch (action)
        {
            case ResidencyAction.Remove:
                _actor.Release();
                return;

            case ResidencyAction.Rehome:
            {
                // Close enough to walk: he gets up and goes, rather than
                // vanishing and reappearing. The same plan a rebuild would use,
                // restricted to spots he can actually reach on foot.
                if (TryBeginRelocation())
                {
                    return;
                }

                // A rehome caused by losing a seat keeps the ordinary backoff.
                // Everything else - the anchor moved, a bed was destroyed - is
                // a player-visible event and settles immediately.
                //
                // The backoff is not politeness. The seat re-check asks the
                // world a slightly different question than the planner does,
                // and any persistent disagreement between them is a loop:
                // lose the seat, re-plan onto it, lose it again, rebuilding a
                // humanoid model every two seconds. Bounded, the same
                // disagreement costs one rebuild every eight.
                bool lostSeat = inputs.Seat == SeatStatus.Lost;
                _actor.Release();
                _actorRetryElapsed = lostSeat ? ActorRetrySeconds : 0f;
                break;
            }

            case ResidencyAction.Place:
                break;

            default:
                return;
        }

        if (_actorRetryElapsed > 0f)
        {
            _actorRetryElapsed -= ResidencyIntervalSeconds;
            return;
        }

        // The sweep, if one ran this pass, already asked the planner this exact
        // question against this exact world. Asking again in the same call is
        // forty-eight more probes for an answer that cannot have changed:
        // there is no frame boundary between the two, so nothing can have been
        // built, claimed or carried away in between.
        PlacementResult placement = _sweptPlanValid
            ? _sweptPlan
            : _hulgiPlanner.Plan(_anchor, _hulgiProbe);

        if (!placement.Found)
        {
            _actorRetryElapsed = ActorRetrySeconds;
            _rateLimited.Warning(
                "companion-actor-placement",
                $"No clear spot near your home point for Hulgi yet ({placement.BlockedBy}); he will " +
                "settle when there is one. Nothing about your tools or progress is affected.");
            return;
        }

        if (!_actor.TryBuild(
                placement.Position, placement.Pose, _anchor, ActorPrefabCandidates, _log,
                placement.Seat))
        {
            // Every fallback was exhausted. Presentation is disabled with an
            // actionable notice; the companion still exists, still counts, and
            // still unlocked the tools.
            _presentationSupported = false;
            ShowNotice(AtlasStrings.Get("companion.presentationUnavailable"));
            return;
        }

        _actor.RememberAnchor(_anchor);
        _actor.SetVisible(_settings.CompanionVisible.Value);
        _visibilityApplied = _settings.CompanionVisible.Value;
        _log.LogInfo($"Hulgi settled near your {DescribeAnchor(_anchor.Kind)}: {_actor.Report}.");

        // The first time he actually appears after joining, the player is told
        // - once, remembered on disk before it is shown, and again only after a
        // reset. The owner's rule for every crew member from here on; the
        // shared layer keeps the flag, each product says its own name.
        if (_progress != null && _progress.ShouldAnnounceJoin && _progress.AnnounceJoin())
        {
            ShowNotice(AtlasStrings.Get("companion.hulgi.joinedCrew"), MessageHud.MessageType.Center);
            _log.LogInfo("Told the player that Hulgi joined their crew.");
        }
    }

    /// <summary><c>cc_companion placement</c>: runs the same plan residency
    /// runs, against the live home point, and says what every candidate was
    /// refused for - so "he never appeared" is a table instead of a guess. The
    /// full per-candidate list goes to the log; the console gets the summary.
    /// Changes nothing: the plan is computed and thrown away.</summary>
    public string DescribePlacement()
    {
        if (!_anchor.IsValid)
        {
            return "Placement: there is no home point yet, so there is nothing to plan around.";
        }

        var refused = new Dictionary<PlacementRejection, int>();
        int ground = 0;
        int groundUsable = 0;
        var seatLines = new List<string>();

        PlacementResult result = _hulgiPlanner.Plan(_anchor, _hulgiProbe, (sample, rejections, isSeat) =>
        {
            string verdict = rejections == PlacementRejection.None ? "usable" : rejections.ToString();
            _log.LogInfo(
                $"[placement] {(isSeat ? "seat  " : "ground")} {sample.Position.X:0.0},{sample.Position.Y:0.0}," +
                $"{sample.Position.Z:0.0} ({sample.Position.HorizontalDistanceTo(_anchor.Position):0.0} m out, " +
                $"{sample.Position.Y - _anchor.Position.Y:+0.0;-0.0} m up): {verdict}");

            if (isSeat)
            {
                seatLines.Add($"{sample.Position.HorizontalDistanceTo(_anchor.Position):0.0} m away: {verdict}");
                return;
            }

            ground++;
            if (rejections == PlacementRejection.None)
            {
                groundUsable++;
                return;
            }

            foreach (PlacementRejection flag in Enum.GetValues(typeof(PlacementRejection)))
            {
                if (flag != PlacementRejection.None && (rejections & flag) == flag)
                {
                    refused[flag] = refused.TryGetValue(flag, out int so) ? so + 1 : 1;
                }
            }
        });

        var reasons = new List<string>();
        foreach (KeyValuePair<PlacementRejection, int> entry in refused)
        {
            reasons.Add($"{entry.Key} {entry.Value}");
        }

        string outcome = !result.Found
            ? $"nothing qualifies, so he is not placed (blocked by {result.BlockedBy})"
            : result.Pose == CompanionPose.SitOnSeat
                ? $"he sits on the seat at {result.Seat.Position.X:0.0},{result.Seat.Position.Y:0.0},{result.Seat.Position.Z:0.0}"
                : $"he sits ({result.Pose}) at {result.Position.X:0.0},{result.Position.Y:0.0},{result.Position.Z:0.0}";

        return $"Placement around your {DescribeAnchor(_anchor.Kind)} at {_anchor.Position.X:0.0}," +
            $"{_anchor.Position.Y:0.0},{_anchor.Position.Z:0.0}:" + Environment.NewLine +
            $"  ground: {ground} spots in the 3-10 m ring, {groundUsable} usable" +
            (reasons.Count > 0 ? "; refused for " + string.Join(", ", reasons) : "") + Environment.NewLine +
            $"  seats: {(seatLines.Count == 0 ? "none free within reach" : string.Join("; ", seatLines))}" +
            Environment.NewLine +
            $"  result: {outcome}." + Environment.NewLine +
            "  Every candidate is listed in the log under [placement].";
    }

    /// <summary>Where the actor actually stands, or null when there is none.
    /// Reported by <c>cc_companion where</c> so "he is somewhere near your
    /// home point" can be checked rather than believed.</summary>
    public string? DescribeActorPosition()
    {
        if (!_actor.Exists)
        {
            return null;
        }

        Vector3 at = _actor.Position;
        Player? player = Player.m_localPlayer;
        string distance = player == null
            ? ""
            : $", {Vector3.Distance(player.transform.position, at):0.0} m from you";
        return $"({at.x:0.0}, {at.y:0.0}, {at.z:0.0}){distance}";
    }

    /// <summary>Where he is sitting, in one line.</summary>
    private string DescribeSeating()
    {
        if (!_actor.Exists)
        {
            return _hulgiProbe.SeatSeen
                ? "a free seat was seen nearby; he is not placed yet"
                : "no free seat seen yet; he is not placed yet";
        }

        CompanionPose pose = _actor.Report?.Pose ?? CompanionPose.SitOnGround;
        switch (pose)
        {
            case CompanionPose.SitOnSeat:
                return "on a seat at " + _actor.Seat + " (a local pose only; the seat is not claimed, " +
                    "and he gives it up if anyone sits down or it is taken away)";
            case CompanionPose.SitByFire:
                return "on the ground by a fire" +
                    (_hulgiProbe.SeatSeen ? "; a seat was seen but was not the best spot" : "");
            default:
                return "on the ground" +
                    (_hulgiProbe.SeatSeen ? "; a seat was seen but was not free or not usable" : "");
        }
    }

    /// <summary>Looks for somewhere better to sit, occasionally.
    ///
    /// Three things keep this cheap. It does not run at all once he is on a
    /// seat, because nothing outranks one and a sweep that cannot change the
    /// answer is a sweep not worth paying for. It does not run without a home
    /// point, because the planner has nothing to plan around. And it runs on
    /// its own slow clock rather than the residency tick.
    ///
    /// It reports a comparison, never a decision: whether moving is worth it is
    /// <see cref="ResidencyPlanner"/>'s to say, and it says yes only to a
    /// strictly better pose.</summary>
    private SeatUpgrade ReadSeatUpgrade()
    {
        // Seated is no longer a reason not to look: a chair inside is worth
        // leaving when the only fire is now outside.
        if (!_actor.Exists || !_anchor.IsValid)
        {
            return SeatUpgrade.NotSurveyed;
        }

        // Not while he is idly on his feet - unless the camp just changed. A
        // fire broken or built is worth reacting to mid-wander; the timer alone
        // is not.
        if (_routine != RoutineState.Settled && !_campChanged)
        {
            return SeatUpgrade.NotSurveyed;
        }

        if (_seatUpgradeElapsed < SeatUpgradeSeconds)
        {
            return SeatUpgrade.NotSurveyed;
        }

        // Not in the middle of a drink either, unless the camp changed: the
        // timer keeps, so the look comes the moment he has put the mug away.
        if (_actor.IsDrinking && !_campChanged)
        {
            return SeatUpgrade.NotSurveyed;
        }

        _seatUpgradeElapsed = 0f;
        _campChanged = false;

        // Measured against the spot as the world stands now, and only spots
        // better than it are asked whether he can walk there - route queries
        // are the expensive part, and most candidates are never better.
        int current = CurrentSpotValue();
        PlacementResult offer = _hulgiPlanner.Plan(
            _anchor, _hulgiProbe, accept: sample =>
                _hulgiPlanner.ValueOf(sample) > current && CanWalkToSpot(sample.Position));
        if (!offer.Found)
        {
            return SeatUpgrade.NotSurveyed;
        }

        _sweptPlan = offer;
        _sweptPlanValid = true;
        return new SeatUpgrade(current, offer.Value);
    }

    /// <summary>How good the spot he occupies is right now, on the planner's
    /// scale: warmth as the world reads it at his feet this moment, and his
    /// seat if he is on one.</summary>
    private int CurrentSpotValue()
    {
        // Where he sits, not where he may be standing for a toast: measured from
        // the step in front of his seat, his own seat can look like somewhere
        // better to go.
        Vector3 at = _actor.RestingPosition;
        PlacementProbeSample here = _hulgiProbe.Probe(new WorldPoint(at.x, at.y, at.z));
        SeatOffer seat = _actor.Pose == CompanionPose.SitOnSeat ? _actor.Seat : SeatOffer.None;
        return _hulgiPlanner.ValueOf(new PlacementProbeSample(
            here.Position, PlacementRejection.None, here.DistanceToFire, seat));
    }

    private bool CanWalkToSpot(WorldPoint spot)
    {
        var target = new Vector3(spot.X, spot.Y, spot.Z);
        return _actor.Exists && _actor.CanWalk &&
            Vector3.Distance(_actor.Position, target) <= WalkRelocateMetres &&
            (TryRoute(target) || TryPlanDoorExit(target, commit: false));
    }

    /// <summary>Starts the walk to the best spot he can reach, when there is one
    /// close enough. False sends the caller down the old rebuild path.</summary>
    private bool TryBeginRelocation()
    {
        if (!_actor.Exists || !_actor.CanWalk || !_settings.CompanionVisible.Value || !_anchor.IsValid)
        {
            return false;
        }

        PlacementResult plan = _sweptPlanValid
            ? _sweptPlan
            : _hulgiPlanner.Plan(_anchor, _hulgiProbe, accept: sample => CanWalkToSpot(sample.Position));
        if (!plan.Found)
        {
            return false;
        }

        Vector3 target = TargetOf(plan);
        if (Vector3.Distance(_actor.Position, target) > WalkRelocateMetres)
        {
            return false;
        }

        _doorPhase = DoorPhase.None;
        _exitDoor = null;
        if (!TryRoute(target))
        {
            if (!TryPlanDoorExit(target, commit: true))
            {
                return false;
            }

            _doorPhase = DoorPhase.ToDoor;
            _doorClosedBehind = false;
        }

        float length = 0f;
        for (int index = 1; index < _routeScratch.Count; index++)
        {
            length += Vector3.Distance(_routeScratch[index - 1], _routeScratch[index]);
        }

        _strollRoute.Clear();
        _strollRoute.AddRange(_routeScratch);
        _strollCorner = 1;
        _strollTarget = target;
        _relocation = plan;
        _relocating = true;
        _actor.StandUp();
        EnterRoutine(RoutineState.Strolling);

        // Generous: a route with a doorway in it is slower than its length.
        _relocationPatience = (length / StrollSpeed * 1.5f) + 6f;
        _actor.RememberAnchor(_anchor);

        if (_doorPhase == DoorPhase.ToDoor)
        {
            // Walked twice more: to the door is in the length already; through it
            // and on from its far side is not.
            float after = 0f;
            for (int index = 1; index < _afterDoorRoute.Count; index++)
            {
                after += Vector3.Distance(_afterDoorRoute[index - 1], _afterDoorRoute[index]);
            }

            _relocationPatience += ((after + 2f) / StrollSpeed * 1.5f) + DoorSwingSeconds + 4f;
        }

        _log.LogInfo(
            $"Hulgi is getting up and walking {length:0.0} m to {DescribeSpot(plan)}" +
            (_doorPhase == DoorPhase.ToDoor ? ", out through the door" : "") +
            (_strollRoute.Count > 2 ? $" by a route with {_strollRoute.Count - 2} turn(s)." : "."));
        return true;
    }

    private static Vector3 TargetOf(PlacementResult plan)
    {
        WorldPoint point = plan.Pose == CompanionPose.SitOnSeat && plan.Seat.IsUsable
            ? plan.Seat.Position
            : plan.Position;
        return new Vector3(point.X, point.Y, point.Z);
    }

    private static string DescribeSpot(PlacementResult plan)
    {
        switch (plan.Pose)
        {
            case CompanionPose.SitOnSeat:
                return plan.Value >= 3 ? "a seat by the fire" : "a seat";
            case CompanionPose.SitByFire:
                return "the fire";
            default:
                return "a quiet spot";
        }
    }

    /// <summary>One step of a walk to a new spot, and the arrival. A seat is
    /// arrived at when he is right beside it - the seat itself is often what
    /// stops him - and then he sits exactly where a rebuild would have put him.
    /// If he cannot get there on foot at all, he is put there the old way,
    /// rather than left standing in the middle of the camp.</summary>
    private void TickRelocation(float deltaTime)
    {
        if (!_actor.Exists)
        {
            _relocating = false;
            return;
        }

        _routineElapsed += deltaTime;
        WalkStep step;
        switch (_doorPhase)
        {
            case DoorPhase.ToDoor:
                step = FollowRoute(deltaTime);
                if (step == WalkStep.Walking && _routineElapsed < _relocationPatience)
                {
                    return;
                }

                if (_exitDoor == null ||
                    Vector3.Distance(_actor.Position, _doorInner) > 1.2f)
                {
                    // Never reached the door. Treated like any failed walk.
                    step = WalkStep.Blocked;
                    break;
                }

                _actor.StopWalking();
                if (DoorState(_exitDoor) == 0)
                {
                    UseDoor(_exitDoor);
                }

                _doorPhase = DoorPhase.Opening;
                _doorTimer = DoorSwingSeconds;
                return;

            case DoorPhase.Opening:
                _doorTimer -= deltaTime;
                if (_doorTimer > 0f)
                {
                    return;
                }

                _doorPhase = DoorPhase.Through;
                return;

            case DoorPhase.Through:
                // The open leaf beside the frame is not a wall, so these few
                // steps skip the sweep; the doorway itself was just walked up to.
                step = _actor.StepToward(_doorOuter, deltaTime, StrollSpeed, 0.35f, checkObstruction: false);
                if (step == WalkStep.Walking && _routineElapsed < _relocationPatience)
                {
                    return;
                }

                if (step != WalkStep.Arrived)
                {
                    break;
                }

                _doorPhase = DoorPhase.After;
                _strollRoute.Clear();
                _strollRoute.AddRange(_afterDoorRoute);
                _strollCorner = 1;
                return;

            case DoorPhase.After:
                step = FollowRoute(deltaTime);
                CloseDoorBehindHim();
                if (step == WalkStep.Walking && _routineElapsed < _relocationPatience)
                {
                    return;
                }

                break;

            default:
                step = FollowRoute(deltaTime);
                if (step == WalkStep.Walking && _routineElapsed < _relocationPatience)
                {
                    return;
                }

                break;
        }

        // However the walk ended, a door he opened does not stay open.
        CloseDoorBehindHim(force: true);
        _doorPhase = DoorPhase.None;
        _relocating = false;
        _actor.StopWalking();

        bool beside = Vector3.Distance(_actor.Position, _strollTarget) <= 1.5f;
        if (step == WalkStep.Arrived || beside)
        {
            _actor.SettleInto(_relocation.Position, _relocation.Pose, _relocation.Seat);
            EnterRoutine(RoutineState.Settled);
            _log.LogInfo($"Hulgi sat down at {DescribeSpot(_relocation)}.");
            return;
        }

        _log.LogInfo(
            "Hulgi could not reach his new spot on foot, so he is put there instead. " +
            "Nothing about your tools or progress is affected.");
        _actor.Release();
        _actorRetryElapsed = 0f;
    }

    /// <summary>Closes the door he came out through, once he is two metres
    /// clear of it - or at once when <paramref name="force"/> says the walk is
    /// over. Only a door he opened, only if it is still open, and only once.
    /// </summary>
    private void CloseDoorBehindHim(bool force = false)
    {
        if (_exitDoor == null || _doorClosedBehind)
        {
            return;
        }

        Vector3 flat = _actor.Position - _doorCentre;
        flat.y = 0f;
        if (!force && flat.magnitude < 2f)
        {
            return;
        }

        _doorClosedBehind = true;
        if (DoorState(_exitDoor) != 0)
        {
            UseDoor(_exitDoor);
            _log.LogInfo("Hulgi closed the door behind him.");
        }
    }

    /// <summary>Fingerprints what makes a spot good or reachable near his home:
    /// every fire and whether it burns, every seat, every door and whether it is
    /// open. When the fingerprint changes he re-plans on the very next pass
    /// instead of waiting for the half-minute survey - which is what makes him
    /// notice a campfire being broken, or built, within a second.
    ///
    /// Order-independent (XOR of per-object hashes), so the order physics
    /// happens to return colliders in cannot look like a change.</summary>
    private void NoticeCampChanges()
    {
        if (!_actor.Exists || !_anchor.IsValid || _relocating)
        {
            return;
        }

        int signature = 0;
        try
        {
            if (_campMask == -1)
            {
                _campMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default_small");
            }

            var centre = new Vector3(_anchor.Position.X, _anchor.Position.Y, _anchor.Position.Z);
            int count = Physics.OverlapSphereNonAlloc(
                centre, HulgiRules.MaximumRadius + 5f, _campBuffer, _campMask, QueryTriggerInteraction.Collide);

            var seen = new HashSet<Component>();
            for (int index = 0; index < count; index++)
            {
                Collider hit = _campBuffer[index];
                if (hit == null)
                {
                    continue;
                }

                Component? thing = (Component?)hit.GetComponentInParent<Fireplace>()
                    ?? (Component?)hit.GetComponentInParent<Chair>()
                    ?? hit.GetComponentInParent<Door>();
                if (thing == null || !seen.Add(thing))
                {
                    continue;
                }

                Vector3 at = thing.transform.position;
                int state = thing is Fireplace fire
                    ? (fire.IsBurning() ? 1 : 2)
                    : thing is Door door
                        ? DoorState(door) + 3
                        : 7;

                unchecked
                {
                    int hash = (Mathf.RoundToInt(at.x * 4f) * 73856093) ^
                        (Mathf.RoundToInt(at.y * 4f) * 19349663) ^
                        (Mathf.RoundToInt(at.z * 4f) * 83492791) ^
                        (state * 2654435);
                    signature ^= hash;
                }
            }
        }
        catch (Exception)
        {
            return;
        }

        if (signature == _campSignature)
        {
            return;
        }

        bool firstLook = _campSignature == 0;
        _campSignature = signature;
        if (!firstLook)
        {
            // This very frame, not on the next two-second residency tick.
            _campChanged = true;
            _seatUpgradeElapsed = SeatUpgradeSeconds;
            _residencyElapsed = ResidencyIntervalSeconds;
            _log.LogInfo("Something changed around Hulgi's camp - a fire, a seat or a door; he looks again.");
        }
    }

    /// <summary>0 closed, anything else open, read the way the door itself
    /// reads it.</summary>
    private static int DoorState(Door door)
    {
        ZNetView? view = door.GetComponent<ZNetView>();
        ZDO? zdo = view == null ? null : view.GetZDO();
        return zdo == null ? 0 : zdo.GetInt(ZDOVars.s_state);
    }

    /// <summary><c>cc_companion summon</c>: sits him on the ground two metres in
    /// front of the player, for testing how he finds his way back to the best
    /// spot he can reach. He looks again at once.</summary>
    public string Summon()
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "Summon: there is no player to bring him to.";
        }

        if (!_actor.Exists)
        {
            return "Summon: Hulgi is not placed right now, so there is nobody to bring over.";
        }

        Vector3 spot = player.transform.position + (player.transform.forward * 2f);
        if (CompanionFooting.TryFind(spot, 2f, 4f, out Vector3 ground, out _))
        {
            spot = ground;
        }

        _relocating = false;
        _actor.StopWalking();
        _actor.SetDownAt(new WorldPoint(spot.x, spot.y, spot.z));
        EnterRoutine(RoutineState.Settled);
        _seatUpgradeElapsed = SeatUpgradeSeconds;

        return "Hulgi is sitting two metres in front of you. Within a couple of seconds he looks " +
            "for the best spot he can reach on foot and walks there - through open doorways, not " +
            "closed ones. If nothing reachable is better than where he is, he stays.";
    }

    /// <summary>Every so often, a drink - with a toast in front of it now and
    /// then. Only while he is sitting or standing about: never mid-walk, never
    /// on his way through a door.</summary>
    private void UpdateDrinking(float deltaTime)
    {
        if (_drinkingFailed)
        {
            return;
        }

        try
        {
            if (_actor.IsDrinking)
            {
                _actor.TickDrink(deltaTime);
                return;
            }

            bool canDrink = _actor.Exists && _actor.CanDrink && _settings.CompanionVisible.Value &&
                !_relocating && (_routine == RoutineState.Settled || _routine == RoutineState.Standing);

            if (_drinks.Tick(deltaTime, canDrink, out DrinkPlan plan))
            {
                StartDrink(plan);
            }
        }
        catch (Exception exception)
        {
            // A drink is a nicety. One that throws is put down, and he goes
            // without for the session - rather than letting it reach the
            // tick's handler, which would step the whole companion aside.
            _drinkingFailed = true;
            try
            {
                _actor.CancelDrink();
            }
            catch
            {
                // Nothing more to put right; the next rebuild starts clean.
            }

            _log.LogInfo(
                "Hulgi's drink hit an error, so he goes without for this session. Nothing else is " +
                $"affected: {SafeLogText.Brief(exception)}");
        }
    }

    private string StartDrink(DrinkPlan plan)
    {
        Vector3? standAt = plan.Toast && _actor.IsOnSeat ? FindToastSpot() : null;

        Player player = Player.m_localPlayer;
        Vector3? toWhom = player != null &&
            Vector3.Distance(player.transform.position, _actor.Position) <= ToastFaceMetres
                ? player.transform.position
                : (Vector3?)null;

        bool started = _actor.BeginDrink(plan, standAt, toWhom, AppearancePlan.HulgiMugs, out string outcome);
        if (started)
        {
            _log.LogInfo(outcome);
        }

        return outcome;
    }

    /// <summary>The ground just in front of his seat, if a person could stand
    /// there: a floor he can step down onto, nothing solid where his body
    /// would be - a table, a wall, a post - and nothing the placement probe
    /// would never put him on, a fire above all. Null when there is no such
    /// spot, and then he drinks without the toast rather than standing inside
    /// the furniture.</summary>
    private Vector3? FindToastSpot()
    {
        Vector3 seat = _actor.Position;
        Vector3 facing = _actor.Facing;
        if (facing == Vector3.zero)
        {
            return null;
        }

        Vector3 candidate = seat + (facing * ToastStepMetres);
        if (!CompanionFooting.TryFind(candidate, 0.3f, 1.5f, out Vector3 ground, out Vector3 normal) ||
            Vector3.Dot(normal, Vector3.up) < 0.75f)
        {
            return null;
        }

        // Down off a seat, or level with a low one. Not up onto something, and
        // not a drop.
        float drop = seat.y - ground.y;
        if (drop < -0.3f || drop > 1.2f || CompanionFooting.IsBodyObstructed(ground))
        {
            return null;
        }

        PlacementProbeSample sample = _hulgiProbe.Probe(new WorldPoint(ground.x, ground.y, ground.z));
        return (sample.Rejections & UnsafeToStand) == 0 ? ground : (Vector3?)null;
    }

    /// <summary><c>cc_companion drink [toast|plain]</c>: a drink now, so it can
    /// be watched without waiting for one. The next drink he has on his own is
    /// the usual while after it.</summary>
    public string Drink(string? how)
    {
        if (!_actor.Exists)
        {
            return "Drink: Hulgi is not placed right now.";
        }

        if (_actor.IsDrinking)
        {
            return "Drink: Hulgi is already having one.";
        }

        if (_drinkingFailed)
        {
            return "Drink: his drinks hit an error earlier this session (see the log), so he is going without.";
        }

        if (_relocating || _routine == RoutineState.Strolling)
        {
            return "Drink: Hulgi is walking. Ask again once he has stopped.";
        }

        if (!_actor.CanDrink)
        {
            return "Drink: there is no animated, visible Hulgi to have one.";
        }

        bool? toast;
        switch (how?.ToLowerInvariant())
        {
            case null:
            case "":
                toast = null;
                break;
            case "toast":
                toast = true;
                break;
            case "plain":
                toast = false;
                break;
            default:
                return "Usage: cc_companion drink [toast|plain]. Without either, chance decides the " +
                    "toast the way it does for the drinks he has on his own.";
        }

        return StartDrink(_drinks.Now(toast));
    }


    /// <summary>The bit of him that is not a statue.
    ///
    /// He sits most of the time, gets up occasionally, walks somewhere else in
    /// his camp, stands looking at it for a moment, and sits back down - and
    /// when it turns dark or wet he goes and finds a roof. That is the whole of
    /// it. <see cref="CampRoutine"/> owns when; this owns where and how, and
    /// keeps both inside the rules: he walks a straight line over ground the
    /// same probe already approved, he never leaves the camp band, he blocks
    /// nothing, and nothing he does touches the world or the save.
    ///
    /// Off by setting, and off entirely on a build whose animator has no
    /// locomotion to drive - a companion who slides reads as broken, where one
    /// who sits still just reads as still.</summary>
    private void UpdateRoutine(float deltaTime)
    {
        // A walk to a new spot runs whether or not wandering is on: it is how he
        // moves house, not how he passes the time.
        if (_relocating)
        {
            TickRelocation(deltaTime);
            return;
        }

        // A mug in his hand holds everything else: he does not get up and
        // wander off mid-swallow, and the wait he is in does not run out under
        // it. A move the camp asks for still happens - it ends the drink.
        if (_actor.IsDrinking)
        {
            return;
        }

        if (!_actor.Exists || !_settings.CompanionWander.Value ||
            !_settings.CompanionVisible.Value || !_anchor.IsValid)
        {
            return;
        }

        // Said once, and said out loud. A companion who never gets up because
        // his animator has no locomotion to drive looks exactly like a
        // companion who simply has not got up yet, and this project has
        // shipped three separate features that failed in silence.
        if (!_actor.CanWalk)
        {
            if (!_walkReported)
            {
                _walkReported = true;
                _log.LogInfo(
                    "This build's companion model has no walking animation to drive, so Hulgi " +
                    "stays seated rather than sliding around. Nothing else is affected.");
            }

            return;
        }

        if (!_walkReported)
        {
            _walkReported = true;
            _log.LogInfo("Hulgi can walk on this build; he will move around his camp.");
        }

        _routineElapsed += deltaTime;

        bool nightOrStorm = IsNightOrStorm();
        _sinceShelterSearchFailed = nightOrStorm
            ? _sinceShelterSearchFailed + deltaTime
            : float.PositiveInfinity;

        bool finished = false;
        if (_routine == RoutineState.Strolling)
        {
            WalkStep step = FollowRoute(deltaTime);
            finished = step != WalkStep.Walking;
        }

        var inputs = new RoutineInputs(
            _routine,
            _routineElapsed,
            finished,
            PlayerWithinTalkRange(),
            IsSheltered(_actor.Position),
            nightOrStorm,
            _actor.Pose,
            _sinceShelterSearchFailed);

        switch (CampRoutine.Decide(inputs))
        {
            case RoutineAction.Stroll:
                BeginStroll();
                break;

            case RoutineAction.Stand:
                _actor.StopWalking();
                EnterRoutine(RoutineState.Standing);
                break;

            case RoutineAction.Settle:
                _actor.StopWalking();
                _actor.SettleWhereHeStands();
                EnterRoutine(RoutineState.Settled);
                bool covered = IsSheltered(_actor.Position);
                if (nightOrStorm && !covered)
                {
                    // However he got here - nowhere dry in reach, a dry spot he
                    // could not get to, or his patience running out on the way -
                    // he has looked and he is sitting in the open. Recording only
                    // the first of those left the other two free to loop.
                    NoteShelterSearchFailed();
                }

                _log.LogInfo("Hulgi sat down" + (covered ? " under cover." : " in the open."));
                break;
        }
    }

    private void EnterRoutine(RoutineState state)
    {
        _routine = state;
        _routineElapsed = 0f;
    }

    /// <summary>Picks somewhere to go and gets him up.
    ///
    /// The candidates are points around his home at walking distance, taken at
    /// a turning angle so consecutive strolls do not retrace the same line, and
    /// each one is put through the placement probe that chose his original spot
    /// - so he can only ever walk to ground he could have been placed on. In
    /// the dark or the wet the first sheltered candidate wins outright; that is
    /// the whole of "he knows to come in out of the rain".
    ///
    /// If nothing passes, he stays sitting. A stroll is a nicety, and there is
    /// no version of this worth a companion standing in a river for.</summary>
    private void BeginStroll()
    {
        bool wantsShelter = IsNightOrStorm();
        Vector3 home = new Vector3(_anchor.Position.X, _anchor.Position.Y, _anchor.Position.Z);
        Vector3? fallback = null;

        for (int step = 0; step < StrollCandidates; step++)
        {
            _strollTurn++;

            // A turning angle rather than a random one: same world, same walk,
            // and no two consecutive strolls along the same line.
            float angle = _strollTurn * StrollTurnDegrees * Mathf.Deg2Rad;
            float radius = StrollNearMetres +
                ((_strollTurn % 3) * (StrollFarMetres - StrollNearMetres) / 2f);
            var candidate = new WorldPoint(
                home.x + (Mathf.Cos(angle) * radius),
                home.y,
                home.z + (Mathf.Sin(angle) * radius));

            PlacementProbeSample sample = _hulgiProbe.Probe(candidate);
            if (!sample.IsUsable)
            {
                continue;
            }

            var point = new Vector3(
                sample.Position.X, sample.Position.Y, sample.Position.Z);

            // Somewhere he can actually get to - around a building, through
            // an open doorway - and not merely somewhere he can see.
            if (!TryRoute(point))
            {
                continue;
            }

            if (wantsShelter && !IsSheltered(point))
            {
                fallback ??= point;
                continue;
            }

            if (StrollTo(point))
            {
                return;
            }
        }

        if (wantsShelter)
        {
            // Every candidate was looked at and none of them is dry. That is a
            // fact about this camp, not about this moment, so it is recorded:
            // the routine will not send him to look again for a while, which is
            // what stops the up-walk-sit-up loop seen in game at 7287919. His
            // ordinary pottering still passes through here, so a roof built
            // nearby is still found at his next stroll; only the extra
            // get-up-just-to-look is held back.
            NoteShelterSearchFailed();
        }

        if (fallback.HasValue)
        {
            // Nowhere dry within reach. Moving anyway is better than sitting in
            // the open pretending the weather is fine.
            if (StrollTo(fallback.Value))
            {
                return;
            }
        }

        // Nothing he could stand on anywhere in the ring. He stays sitting, and
        // the settled floor starts again - without this the routine asked again
        // on the very next frame, and every frame after, twelve probes a time.
        _routineElapsed = 0f;
    }

    /// <summary>Records that a look for somewhere dry came back empty. Said in
    /// the log once per spell of weather, not once per look.</summary>
    private void NoteShelterSearchFailed()
    {
        if (float.IsPositiveInfinity(_sinceShelterSearchFailed))
        {
            _log.LogInfo(
                "Hulgi looked for somewhere dry and ended up in the open, so he is not getting up " +
                "just to look again for a while.");
        }

        _sinceShelterSearchFailed = 0f;
    }

    private bool StrollTo(Vector3 point)
    {
        if (!TryRoute(point))
        {
            return false;
        }

        _strollRoute.Clear();
        _strollRoute.AddRange(_routeScratch);
        _strollCorner = 1;
        _strollTarget = point;
        _actor.StandUp();
        EnterRoutine(RoutineState.Strolling);
        _log.LogInfo(
            $"Hulgi is getting up and walking {Vector3.Distance(_actor.Position, point):0.0} m " +
            $"to {point.ToString("0.#")}" +
            (_strollRoute.Count > 2 ? $" by a route with {_strollRoute.Count - 2} turn(s)" : "") +
            (IsNightOrStorm() ? " (looking for shelter)." : "."));
        return true;
    }

    /// <summary>A way from where he stands to <paramref name="point"/> that does
    /// not go through anything, left in the scratch route.
    ///
    /// Asked of the game's own navmesh first - the same pathfinding its
    /// creatures use, which knows walls from doorways - as a local query only:
    /// no BaseAI, nothing networked, nothing written. The humanoid agent that
    /// does not swim, because a camp stroll is not a swim. A full route or
    /// nothing: a partial one ends at a wall.
    ///
    /// The navmesh builds its tiles on first request, so an early ask can come
    /// back empty for a place that is perfectly reachable. Then, and only then,
    /// a straight line is accepted - if nothing solid stands anywhere on it.
    /// Either way he never walks through a wall: that is what the owner asked
    /// for, and a walk that stops short is better than one that does
    /// not.</summary>
    private bool TryRoute(Vector3 point)
    {
        return _actor.Exists && TryRouteFrom(_actor.Position, point, _routeScratch);
    }

    /// <summary>The same question as <see cref="TryRoute"/>, from anywhere - the
    /// far side of a door, for instance - into <paramref name="route"/>.</summary>
    private static bool TryRouteFrom(Vector3 from, Vector3 point, List<Vector3> route)
    {
        route.Clear();

        try
        {
            Pathfinding pathfinding = Pathfinding.instance;
            if (pathfinding != null &&
                pathfinding.GetPath(
                    from, point, route, Pathfinding.AgentType.HumanoidNoSwim,
                    requireFullPath: true) &&
                route.Count >= 2)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // A navmesh that will not answer is treated like one not built yet.
        }

        route.Clear();
        if (CompanionFooting.IsWayBlocked(from, point))
        {
            return false;
        }

        route.Add(from);
        route.Add(point);
        return true;
    }

    /// <summary>A way out of the building he is in, through a closed door, to
    /// <paramref name="target"/>: reach the door from inside, and route onward
    /// from its far side. Only from under a roof - he lets himself OUT of a
    /// building, never into one; the owner's rule is that a closed door means
    /// the chairs inside are not for him, and he takes the log instead.
    ///
    /// Only doors the local player could open: no key, closable again, guard
    /// stone access allowed. With <paramref name="commit"/> the plan is kept
    /// for the walk; without, it only answers whether one exists.</summary>
    private bool TryPlanDoorExit(Vector3 target, bool commit)
    {
        if (!_actor.Exists || !IsSheltered(_actor.Position))
        {
            return false;
        }

        Vector3 here = _actor.Position;
        int count;
        try
        {
            count = Physics.OverlapSphereNonAlloc(
                here, 10f, _doorBuffer, LayerMask.GetMask("piece", "piece_nonsolid", "Default_small"),
                QueryTriggerInteraction.Ignore);
        }
        catch (Exception)
        {
            return false;
        }

        var doors = new List<Door>();
        for (int index = 0; index < count; index++)
        {
            Door? door = _doorBuffer[index] == null ? null : _doorBuffer[index].GetComponentInParent<Door>();
            if (door != null && !doors.Contains(door) && CanUseDoor(door))
            {
                doors.Add(door);
            }
        }

        doors.Sort((a, b) =>
            Vector3.Distance(here, a.transform.position).CompareTo(Vector3.Distance(here, b.transform.position)));

        foreach (Door door in doors)
        {
            Collider? leaf = door.GetComponentInChildren<Collider>();
            Vector3 centre = leaf != null ? leaf.bounds.center : door.transform.position;
            Vector3 across = door.transform.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 0.01f)
            {
                continue;
            }

            across.Normalize();
            float side = Vector3.Dot(across, here - centre) >= 0f ? 1f : -1f;
            Vector3 inner = Grounded(centre + (across * side * 1.0f));
            Vector3 outer = Grounded(centre - (across * side * 1.1f));

            if (!TryRouteFrom(here, inner, _doorScratch) || !TryRouteFrom(outer, target, _afterDoorRoute))
            {
                continue;
            }

            if (commit)
            {
                _exitDoor = door;
                _doorCentre = centre;
                _doorInner = inner;
                _doorOuter = outer;
                _routeScratch.Clear();
                _routeScratch.AddRange(_doorScratch);
            }

            return true;
        }

        return false;
    }

    private static Vector3 Grounded(Vector3 point)
    {
        return CompanionFooting.TryFind(point, 2f, 4f, out Vector3 ground, out _) ? ground : point;
    }

    /// <summary>A door he may use: closed now, no key, can be closed again, and
    /// guard-stone access allowed here - the same doors the local player could
    /// open, and nothing else.</summary>
    private static bool CanUseDoor(Door door)
    {
        try
        {
            return DoorState(door) == 0 &&
                door.m_keyItem == null &&
                !door.m_canNotBeClosed &&
                (!door.m_checkGuardStone || PrivateArea.CheckAccess(door.transform.position, 0f, flash: false));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Swings a door the way the game does for a player standing where
    /// he stands: through its own UseDoor RPC, away from him. The same RPC
    /// closes it again. The one thing in the world a companion is allowed to
    /// change - see CLAUDE.md.</summary>
    private void UseDoor(Door door)
    {
        try
        {
            ZNetView? view = door.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }

            Vector3 fromHim = (_actor.Position - door.transform.position).normalized;
            view.InvokeRPC("UseDoor", Vector3.Dot(door.transform.forward, fromHim) < 0f);
        }
        catch (Exception exception)
        {
            _log.LogInfo($"Hulgi could not use the door: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>One step along the route: towards the next corner, and on to
    /// the one after when he reaches it. The end of the route is reached with
    /// the ordinary arrival distance; the corners before it more tightly.
    /// </summary>
    private WalkStep FollowRoute(float deltaTime)
    {
        if (_strollRoute.Count < 2)
        {
            return _actor.StepToward(_strollTarget, deltaTime, StrollSpeed);
        }

        int corner = Math.Min(_strollCorner, _strollRoute.Count - 1);
        bool last = corner == _strollRoute.Count - 1;
        WalkStep step = _actor.StepToward(
            _strollRoute[corner], deltaTime, StrollSpeed, last ? (float?)null : CornerArrivalMetres);

        if (step == WalkStep.Arrived && !last)
        {
            _strollCorner = corner + 1;
            return WalkStep.Walking;
        }

        return step;
    }

    private bool PlayerWithinTalkRange()
    {
        Player player = Player.m_localPlayer;
        return player != null &&
            Vector3.Distance(player.transform.position, _actor.Position) <= TalkRadius;
    }

    /// <summary>Whether that point has a roof over it. The game's own test, the
    /// one it uses to decide whether a player is sheltered from rain.</summary>
    private static bool IsSheltered(Vector3 point)
    {
        try
        {
            return Cover.IsUnderRoof(point + (Vector3.up * 0.5f));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether a person would want to be indoors. Night, or weather
    /// wet enough that the game itself considers you exposed.</summary>
    private static bool IsNightOrStorm()
    {
        try
        {
            EnvMan env = EnvMan.instance;
            if (env == null)
            {
                return false;
            }

            return !EnvMan.IsDaylight() || EnvMan.IsWet();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Poses him by hand for a look. Presentation only; see
    /// <see cref="CompanionActor.ForcePose"/>.</summary>
    public string ForcePose(string what)
    {
        return _actor.ForcePose(what);
    }

    /// <summary><c>cc_companion reset [quest|bed|day|all]</c> - puts parts of
    /// the companion's story back to the start so it can be played again.
    /// Added at the owner's request for testing on a disposable world.
    ///
    /// Everything here is either this mod's own data or a vanilla call the game
    /// itself makes: <c>ClearCustomSpawnPoint</c> is what the game does when a
    /// bed is destroyed, and <c>SetNetTime</c> is what its own
    /// <c>skiptime</c> does. No save file is edited, and the map tools are
    /// never locked - see <see cref="CompanionProgress.ResetQuest"/>. The
    /// order under <c>all</c> matters: time and bed first, so the quest reopens
    /// against the home point the player is about to test.</summary>
    public string Reset(string? what)
    {
        string part = string.IsNullOrEmpty(what) ? "quest" : what!.ToLowerInvariant();
        bool all = part == "all";
        if (!all && part != "quest" && part != "bed" && part != "day")
        {
            return "Usage: cc_companion reset [quest|bed|day|all]. quest (the default) restarts " +
                "Hulgi's introduction; bed forgets your bed spawn point in this world; day returns " +
                "this world to the morning of day 1; all does all three.";
        }

        var lines = new List<string>();
        if (all || part == "day")
        {
            lines.Add(ResetDay());
        }

        if (all || part == "bed")
        {
            lines.Add(ForgetBedSpawnPoint());
        }

        if (all || part == "quest")
        {
            lines.Add(ResetIntroduction());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResetDay()
    {
        ZNet net = ZNet.instance;
        EnvMan env = EnvMan.instance;
        if (net == null || env == null)
        {
            return "Day: no world is running, so nothing was changed.";
        }

        if (!net.IsServer())
        {
            return "Day: only the host of this world can change its time - the same rule the " +
                "game's own skiptime follows. Nothing was changed.";
        }

        double morning = env.GetMorningStartSec(1);
        net.SetNetTime(morning);
        return $"Day: this world is back to the morning of day 1, as a new world starts. Anything " +
            "in it that waits on time - crops, smelters, fermenters - now has longer to wait.";
    }

    private string ForgetBedSpawnPoint()
    {
        PlayerProfile? profile = Game.instance == null ? null : Game.instance.GetPlayerProfile();
        if (profile == null)
        {
            return "Bed: no character is loaded, so nothing was changed.";
        }

        if (!profile.HaveCustomSpawnPoint())
        {
            return "Bed: you have no bed spawn point in this world, so there was nothing to forget.";
        }

        profile.ClearCustomSpawnPoint();

        // Re-read the home point on the next tick rather than in five seconds.
        _anchorElapsed = AnchorRecheckSeconds;
        return "Bed: your bed spawn point in this world is forgotten, exactly as if the bed had " +
            "been destroyed. The bed itself is untouched: press Use on it to claim it again, and " +
            "Hulgi will move to it.";
    }

    private string ResetIntroduction()
    {
        if (_progress == null)
        {
            return "Quest: no companion data is open for this character in this world yet, so " +
                "there was nothing to reset.";
        }

        if (!_progress.ResetQuest())
        {
            return _progress.QuestState == QuestState.Unstarted
                ? "Quest: the introduction has not been started, so it is already at the beginning."
                : "Quest: this build is not allowed to rewrite this character's companion data - " +
                  "it belongs to a newer version, or could not be kept safe - so nothing was reset.";
        }

        // The same release-and-reopen a change of world or character uses, so
        // nothing from the finished introduction survives into the new one.
        ReleaseCompass();
        _actor.Release();
        _sinceShelterSearchFailed = float.PositiveInfinity;
        _anchor = CompanionAnchor.None;
        _anchorValidity = AnchorValidity.Unknown;
        _noticedRecorded = false;
        _resumePage = 0;
        _progress = null;
        _proximity.Reset();
        TryOpenProgress();

        _log.LogInfo("Companion introduction reset by console command; map tools stay unlocked.");
        return "Quest: Hulgi's introduction starts again. The Broken Compass returns near your home " +
            "point, and he will join you once you have found it. Your map tools stay unlocked.";
    }

    /// <summary>Whether the seat he is on is still a seat he may have.
    ///
    /// Two things end it, and both are ordinary: the piece is destroyed, and a
    /// player sits in it. The second is the one that matters — the furniture
    /// belongs to whoever built it, and a companion who keeps a chair a player
    /// wants is worse company than one sitting on the grass.
    ///
    /// <c>Chair.IsInUse</c> asks whether a PLAYER is on the attach point, so
    /// our own posed figure never registers as an occupant. That is what makes
    /// this check honest rather than self-satisfying.</summary>
    private SeatStatus ReadSeatStatus()
    {
        if (!_actor.Exists || !_actor.Seat.IsUsable)
        {
            return SeatStatus.NotSeated;
        }

        // Null means the ground there is not loaded, so nothing can be
        // checked. Keep believing he is seated: the alternative moves him every
        // time the player walks out of range of his own camp.
        bool? free = _hulgiProbe.IsSeatStillFree(_actor.Seat.Position);
        return free == false ? SeatStatus.Lost : SeatStatus.Held;
    }

    /// <summary>Says one line.
    ///
    /// Idempotent under mashing: a cooldown swallows repeats, so the vanilla
    /// interaction and the proximity fallback firing on the same frame produce
    /// one line rather than two. Delivered as an ordinary HUD toast, never a
    /// panel — "no modal interruption during combat, loading or death" is
    /// satisfied by never being modal in the first place.</summary>
    public void TalkToCompanion()
    {
        if (_dialogue == null || _talkCooldown > 0f)
        {
            return;
        }

        // Greet first, then range over everything he has to say. The
        // whole-catalogue pass is where biome lines come from, so a well
        // travelled player does not get nothing but travel advisories and an
        // untravelled one never notices a category quietly failing.
        DialogueCategory? category = _conversationTurn == 0
            ? HulgiDialogue.ConversationOrder[0]
            : (DialogueCategory?)null;
        _conversationTurn++;

        DialogueContext context = _biomes.Read();
        if (!_dialogue.TryNext(category, context, out DialogueLine line) &&
            !_dialogue.TryNext(null, context, out line))
        {
            // Nothing eligible at all. Silence beats saying something
            // inappropriate.
            return;
        }

        _talkCooldown = TalkCooldownSeconds;
        Say(AtlasStrings.Get(line.Key));
    }

    /// <summary>Ambient remarks, OFF by default.
    ///
    /// A companion who talks at you unprompted is charming for an evening and
    /// tiresome by the second. So this is opt-in, it only fires while the
    /// player is standing near him, and it uses the same rotation and the same
    /// cooldown as speaking to him — there is one voice, not two.</summary>
    private void UpdateAmbientChatter(float deltaTime)
    {
        if (_talkCooldown > 0f)
        {
            _talkCooldown -= deltaTime;
        }

        if (!_settings.CompanionAmbientChatter.Value || !_actor.Exists)
        {
            return;
        }

        _ambientElapsed += deltaTime;
        if (_ambientElapsed < _settings.CompanionAmbientIntervalSeconds.Value)
        {
            return;
        }

        _ambientElapsed = 0f;

        Player player = Player.m_localPlayer;
        if (player == null || !_settings.CompanionVisible.Value)
        {
            return;
        }

        if (Vector3.Distance(player.transform.position, _actor.Position) > TalkRadius)
        {
            return;
        }

        TalkToCompanion();
    }

    /// <summary>The proximity fallback for talking, mirroring the compass's.
    /// Only while close, only while not aiming at something else, and never
    /// while a menu or a text field owns the key.</summary>
    private void UpdateCompanionInput()
    {
        if (!_actor.Exists || !_settings.CompanionVisible.Value)
        {
            return;
        }

        Player player = Player.m_localPlayer;
        if (player == null || _storyPanel.IsVisible)
        {
            return;
        }

        if (Vector3.Distance(player.transform.position, _actor.Position) > TalkRadius)
        {
            return;
        }

        if (InteractPressed(player, allowCompass: false))
        {
            TalkToCompanion();
        }
    }

    private void ApplyVisibilityPreference()
    {
        bool desired = _settings.CompanionVisible.Value;
        if (desired == _visibilityApplied)
        {
            return;
        }

        _visibilityApplied = desired;
        if (_actor.Exists)
        {
            _actor.SetVisible(desired);
        }
    }

    /// <summary>Re-checks that the live world and character still match the
    /// scope this session's sidecar is addressed to, and reopens if not.
    ///
    /// The independent review's point: a scope is resolved once and then
    /// written to on every transition, and the store's own scope-mismatch guard
    /// only catches a FILE that disagrees — not an in-memory scope that has
    /// gone stale because a world change slipped past the hook that should have
    /// reset it. This is the belt to that hook's braces, and it is cheap:
    /// two accessor reads every ten seconds.</summary>
    private void ReopenIfScopeWentStale()
    {
        if (_progress == null)
        {
            return;
        }

        if (!_scopeSource.TryResolve(out CompanionScope live))
        {
            // Cannot tell. Changing nothing is right: the existing progress is
            // still addressed to a world we successfully resolved once.
            return;
        }

        if (live.Equals(_progress.Scope))
        {
            return;
        }

        _log.LogInfo(
            "The live world or character no longer matches the companion data open in this session, " +
            "so it is being reopened for the current one. No progress is written to the previous scope.");
        ReleaseCompass();
        _actor.Release();
        _sinceShelterSearchFailed = float.PositiveInfinity;
        _anchor = CompanionAnchor.None;
        _anchorValidity = AnchorValidity.Unknown;
        _noticedRecorded = false;
        _resumePage = 0;
        _progress = null;
        _proximity.Reset();
        TryOpenProgress();
    }

    private void UpdateCollectiblePresence()
    {
        if (_progress == null)
        {
            return;
        }

        // Unsaved progress keeps the collectible alive even though the quest
        // says it is finished: the reason it is finished has not reached disk,
        // so taking the compass away now could strand a player with neither.
        bool wanted = _progress.ShouldPresentCollectible || _progress.HasUnsavedChanges;

        if (!wanted)
        {
            if (_compass != null)
            {
                ReleaseCompass();
            }

            return;
        }

        if (_compass != null)
        {
            return;
        }

        if (!_anchor.IsValid && !_anchorSource.TryGetAnchor(out _anchor))
        {
            return;
        }

        if (_placementRetryElapsed > 0f)
        {
            return;
        }

        PlacementResult placement = CompassPlannerFor(_anchor).Plan(_anchor, _placementProbe);
        if (!placement.Found)
        {
            _placementRetryElapsed = PlacementRetrySeconds;
            _rateLimited.Warning(
                "companion-compass-placement",
                "No clear spot near your home point for the broken compass yet " +
                $"({placement.BlockedBy}); it will appear when there is one. Nothing is locked.");
            return;
        }

        SpawnCompass(placement.Position);
    }

    /// <summary>Near a bed the compass keeps its close band. At the world start
    /// it goes outside the ring of standing stones - the owner's call: inside
    /// the circle it read as part of the temple rather than as something lying
    /// on the ground to pick up. The ring is measured each time from the
    /// location itself.</summary>
    private PlacementPlanner CompassPlannerFor(CompanionAnchor anchor)
    {
        if (anchor.Kind != AnchorKind.DefaultSpawn)
        {
            return _planner;
        }

        float ring = SpawnAnchorSource.MeasureStructureRadius(
            new Vector3(anchor.Position.X, anchor.Position.Y, anchor.Position.Z));
        if (ring <= 0f)
        {
            // No location to measure: a generous ring rather than the inside of
            // whatever might be there.
            ring = 8f;
        }

        if (!_compassRingReported)
        {
            _compassRingReported = true;
            _log.LogInfo($"The world start's stone circle reaches {ring:0.0} m; the compass goes just outside it.");
        }

        return new PlacementPlanner(new PlacementRules(
            minimumRadius: ring + 1.5f,
            maximumRadius: ring + 6f,
            maximumHeightDelta: 3f));
    }

    private void SpawnCompass(WorldPoint position)
    {
        try
        {
            string? discovered = LocalVisual.FindPrefabNameContaining("compass");
            string[] candidates;
            if (discovered != null)
            {
                candidates = new string[CompassPrefabCandidates.Length + 1];
                candidates[0] = discovered;
                Array.Copy(CompassPrefabCandidates, 0, candidates, 1, CompassPrefabCandidates.Length);
            }
            else
            {
                candidates = CompassPrefabCandidates;
            }

            LocalVisual.Result visual = LocalVisual.Build("BrokenCompass", candidates, 1.4f, _log);
            visual.Root.transform.position = new Vector3(position.X, position.Y + 0.08f, position.Z);
            visual.Root.transform.rotation = Quaternion.Euler(0f, 35f, 0f);

            _compass = visual.Root;
            _compassSource = visual.Source;

            // The owner: "we can have the object sparkle similar to collectible
            // objects". The vanilla twinkle, borrowed render-only.
            string? sparkle = LocalVisual.TryAttachSparkle(visual.Root, SparklePrefabCandidates, _log);
            _log.LogInfo(sparkle == null
                ? "No vanilla pickable sparkle was found on this build, so the compass does not twinkle."
                : $"The compass twinkles like a pickable (sparkle borrowed from \"{sparkle}\").");

            _compassBehaviour = BrokenCompassObject.Attach(visual.Root, Examine, _log);
            _proximity.Reset();
            _noticedRecorded = false;

            _log.LogInfo(
                $"Broken compass placed near your {DescribeAnchor(_anchor.Kind)} " +
                $"(visual source: {visual.Source}).");
        }
        catch (Exception exception)
        {
            ReleaseCompass();
            _rateLimited.Error(
                "companion-compass-spawn",
                "The broken compass could not be placed this time; the map tools are unaffected and it " +
                $"will be tried again: {SafeLogText.Describe(exception)}");
        }
    }

    private void UpdateProximityAndInput()
    {
        if (_compass == null || _progress == null)
        {
            SetPrompt(false);
            return;
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            SetPrompt(false);
            return;
        }

        float distance = Vector3.Distance(player.transform.position, _compass.transform.position);
        CompassSignal signal = _proximity.Update(distance);

        if (signal != CompassSignal.Idle && !_noticedRecorded)
        {
            // Seeing it is progress worth keeping: a player who finds the
            // compass and logs out has found it.
            _noticedRecorded = true;
            foreach (QuestTransition transition in IntroductionSequence.OnNoticed)
            {
                _progress.Advance(transition);
            }
        }

        bool inReach = signal == CompassSignal.InReach && _progress.ShouldPresentCollectible;
        SetPrompt(inReach && !_storyPanel.IsVisible);

        if (inReach && !_storyPanel.IsVisible && InteractPressed(player))
        {
            Examine();
        }
    }

    /// <summary>The proximity fallback for examining.
    ///
    /// Vanilla's hover raycast is the intended path and the compass implements
    /// <c>Hoverable</c>/<c>Interactable</c> for it, but whether that raycast
    /// reaches a mod-made collider on this build is one of the audit's
    /// unverified rows. Rather than ship an object that might be impossible to
    /// examine, the same key is watched directly while the player is close and
    /// is <b>not</b> already hovering something real — so this never competes
    /// with a workbench, a door, or a chest for the same press.</summary>
    private bool InteractPressed(Player player, bool allowCompass = true)
    {
        try
        {
            if (Minimap.IsOpen() || Minimap.InTextInput() || CcTextFocus.AnyFieldFocused())
            {
                return false;
            }

            if (InventoryGui.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
            {
                return false;
            }

            GameObject? hovered = player.GetHoverObject();
            if (hovered != null)
            {
                bool hoveringOurs = allowCompass
                    ? hovered.GetComponentInParent<BrokenCompassObject>() != null
                    : hovered.GetComponentInParent<CompanionHover>() != null;
                if (!hoveringOurs)
                {
                    // The player is aiming at something else entirely; their
                    // key press belongs to that thing.
                    return false;
                }
            }

            try
            {
                if (ZInput.instance != null && ZInput.GetButtonDown("Use"))
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to the raw key below.
            }

            return Input.GetKeyDown(KeyCode.E);
        }
        catch
        {
            return false;
        }
    }

    private void SetPrompt(bool visible)
    {
        if (visible == _promptVisible)
        {
            return;
        }

        _promptVisible = visible;
        if (visible)
        {
            // The key is the player's own binding, resolved by the game - not a
            // hardcoded [E], which is wrong for anybody who remapped Use or is on
            // a controller.
            // AtlasStrings.Format, so a translation with a broken placeholder
            // falls back instead of throwing out of the presence pass.
            ShowNotice(
                AtlasStrings.Format(
                    "companion.compass.prompt", Localization.instance.Localize("$KEY_Use")),
                MessageHud.MessageType.TopLeft);
        }
    }

    /// <summary>Examining the compass. Idempotent at every layer: the quest
    /// transitions are idempotent, and an already-open panel is not reopened,
    /// so a double click, a held key, or both input paths firing on the same
    /// frame all produce exactly one introduction.</summary>
    public void Examine()
    {
        if (_progress == null || _storyPanel.IsVisible)
        {
            return;
        }

        foreach (QuestTransition transition in IntroductionSequence.OnExamine)
        {
            _progress.Advance(transition);
        }

        if (_progress.Notice != null)
        {
            ShowNotice(_progress.Notice);
        }

        _storyPanel.UiScale = _settings.UiScale.Value;
        _storyPanel.Show(_resumePage, allowChoices: true);

        if (!_storyPanel.IsVisible && !_storyPanel.HasFailed)
        {
            // The GUI was not ready. Say something rather than swallowing the
            // interaction: the quest has advanced and the compass is still there
            // to try again.
            ShowNotice(AtlasStrings.Get("companion.storyUnavailable"));
        }
    }

    /// <summary>Replays the introduction after it has been finished. Reading
    /// is all it does — no choices are offered, because the introduction is
    /// already over and cannot be re-decided.</summary>
    public bool TryReplayStory()
    {
        if (_progress == null || !_progress.CanReplayStory)
        {
            return false;
        }

        _storyPanel.UiScale = _settings.UiScale.Value;
        _storyPanel.Show(0, allowChoices: false);
        return _storyPanel.IsVisible;
    }

    private void WelcomeCompanion()
    {
        Complete(IntroductionSequence.OnWelcome, "companion.welcomed");
    }

    private void ChooseToolsOnly()
    {
        // The explicit choice from the story UI both records the preference
        // and finishes the introduction. The config toggle on its own does
        // not finish anything, which is what lets a player try tools-only and
        // change their mind.
        _settings.CompanionToolsOnly.Value = true;
        _toolsOnlyApplied = true;
        _progress?.SetToolsOnlyPreference(true);
        Complete(IntroductionSequence.OnSkip, "companion.toolsOnlyChosen");
    }

    private void Complete(QuestTransition[] transitions, string noticeKey)
    {
        if (_progress == null)
        {
            return;
        }

        foreach (QuestTransition transition in transitions)
        {
            _progress.Advance(transition);
        }

        if (_progress.HasUnsavedChanges)
        {
            // The advance did not reach disk. Say so, keep the compass, and
            // let the player try again — never pretend it worked.
            ShowNotice(_progress.Notice ?? AtlasStrings.Get("companion.saveFailed"));
            return;
        }

        // Only now, with the decision on disk, may the world lose the object
        // that led to it.
        if (_progress.TryRetirePresentation())
        {
            ReleaseCompass();
        }
        else if (!_progress.ShouldPresentCollectible)
        {
            // Already retired in an earlier session, or the flag would not
            // move. Either way the quest is saved and complete, so the object
            // has no reason to remain.
            ReleaseCompass();
        }

        Gate = CompanionFeatureGate.FromUnlock(_progress.IsUnlocked);
        ShowNotice(AtlasStrings.Get(noticeKey), MessageHud.MessageType.Center);
    }

    private void ReleaseCompass()
    {
        _promptVisible = false;
        _compassBehaviour = null;
        _compassSource = "none";
        _proximity.Reset();

        if (_compass == null)
        {
            return;
        }

        GameObject doomed = _compass;
        _compass = null;
        try
        {
            UnityEngine.Object.Destroy(doomed);
        }
        catch (Exception exception)
        {
            _log.LogWarning(
                $"Could not remove the broken compass object: {SafeLogText.Brief(exception)}");
        }
    }

    private static string DescribeAnchor(AnchorKind kind)
    {
        return kind switch
        {
            AnchorKind.ClaimedBed => "claimed bed",
            AnchorKind.DefaultSpawn => "starting point",
            _ => "home point",
        };
    }

    /// <summary>Hulgi speaking, over his head where a trader speaks.
    ///
    /// A centre-screen message is the game telling the player something. A
    /// bubble over a head is somebody talking to them, and the difference is
    /// most of what makes him read as a person rather than a notification. The
    /// old message is kept as the fallback for a build whose chat widget is not
    /// the shape we expect, because a companion who says nothing at all would
    /// be a worse failure than one who says it in the wrong place.</summary>
    private void Say(string text)
    {
        Transform? head = _actor.SpeechAnchor;
        if (head != null &&
            OverheadSpeech.TrySay(
                head.gameObject, head.position, AtlasStrings.Get("companion.hulgi.name"), text, _log))
        {
            _log.LogInfo("Hulgi: " + text);
            return;
        }

        ShowNotice(text, MessageHud.MessageType.Center);
    }

    private void ShowNotice(string text, MessageHud.MessageType type = MessageHud.MessageType.TopLeft)
    {
        try
        {
            VanillaMessage.Show(Player.m_localPlayer, type, text);
        }
        catch
        {
            // A toast is cosmetic; the log line above it is the record.
        }

        _log.LogInfo(text);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Progress is saved inline at every transition, so there is nothing
        // pending here; the objects are what need releasing.
        ReleaseCompass();
        _actor.Release();
        _storyPanel.Hide();
    }
}

/// <summary>A read-only snapshot for the console tool and the settings panel.
/// Deliberately a value: a diagnostic can never change what it reports.</summary>
internal readonly struct CompanionStatus
{
    public CompanionStatus(
        bool companionsEnabled,
        bool progressOpen,
        string scope,
        QuestState questState,
        bool unlocked,
        CompanionFeatureGate.GateReason gateReason,
        UnlockReason unlockReason,
        SidecarLoadOutcome loadOutcome,
        string? notice,
        AnchorKind anchorKind,
        string? startLocationName,
        bool compassPresent,
        string compassVisualSource,
        bool compassOnNonSolidLayer,
        bool hoverObserved,
        bool toolsOnly,
        bool companionVisible,
        AnchorValidity anchorValidity,
        bool bedLookupUnavailable,
        bool presentationSupported,
        bool actorPresent,
        string? actorReport,
        bool freeSeatSeen,
        string seating,
        string knownBiomesObserved,
        int dialogueLineCount,
        bool ambientChatter)
    {
        CompanionsEnabled = companionsEnabled;
        ProgressOpen = progressOpen;
        Scope = scope;
        QuestState = questState;
        Unlocked = unlocked;
        GateReason = gateReason;
        UnlockReason = unlockReason;
        LoadOutcome = loadOutcome;
        Notice = notice;
        AnchorKind = anchorKind;
        StartLocationName = startLocationName;
        CompassPresent = compassPresent;
        CompassVisualSource = compassVisualSource;
        CompassOnNonSolidLayer = compassOnNonSolidLayer;
        HoverObserved = hoverObserved;
        ToolsOnly = toolsOnly;
        CompanionVisible = companionVisible;
        AnchorValidity = anchorValidity;
        BedLookupUnavailable = bedLookupUnavailable;
        PresentationSupported = presentationSupported;
        ActorPresent = actorPresent;
        ActorReport = actorReport;
        FreeSeatSeen = freeSeatSeen;
        Seating = seating;
        KnownBiomesObserved = knownBiomesObserved;
        DialogueLineCount = dialogueLineCount;
        AmbientChatter = ambientChatter;
    }

    public bool CompanionsEnabled { get; }
    public bool ProgressOpen { get; }
    public string Scope { get; }
    public QuestState QuestState { get; }
    public bool Unlocked { get; }
    public CompanionFeatureGate.GateReason GateReason { get; }
    public UnlockReason UnlockReason { get; }
    public SidecarLoadOutcome LoadOutcome { get; }
    public string? Notice { get; }
    public AnchorKind AnchorKind { get; }
    public string? StartLocationName { get; }
    public bool CompassPresent { get; }
    public string CompassVisualSource { get; }
    public bool CompassOnNonSolidLayer { get; }
    public bool HoverObserved { get; }
    public bool ToolsOnly { get; }
    public bool CompanionVisible { get; }
    public AnchorValidity AnchorValidity { get; }
    public bool BedLookupUnavailable { get; }
    public bool PresentationSupported { get; }
    public bool ActorPresent { get; }
    public string? ActorReport { get; }

    /// <summary>A free seat was found near a candidate at some point this
    /// session.</summary>
    public bool FreeSeatSeen { get; }

    /// <summary>One line about where he is actually sitting: on a seat, on the
    /// ground by a fire, or on the ground. Says which, rather than reporting
    /// what the probe merely noticed.</summary>
    public string Seating { get; }

    /// <summary>The biome spellings this build actually put in
    /// <c>Player.m_knownBiome</c>. Printed by the console tool, which is how
    /// the audit's open row about those spellings gets closed by
    /// observation.</summary>
    public string KnownBiomesObserved { get; }

    public int DialogueLineCount { get; }

    public bool AmbientChatter { get; }
}
