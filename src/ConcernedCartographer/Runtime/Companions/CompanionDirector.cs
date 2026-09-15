using System;
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
    private float _talkCooldown;
    private float _ambientElapsed;
    private int _conversationTurn;
    private float _actorRetryElapsed;
    private AnchorValidity _anchorValidity = AnchorValidity.Unknown;
    private bool _presentationSupported = true;
    private bool _visibilityApplied = true;
    private bool _toolsOnlyApplied;
    private bool _noticedRecorded;
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
            () => _settings.CompanionBeardPreset.Value);
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

            _residencyElapsed += deltaTime;
            if (_residencyElapsed >= ResidencyIntervalSeconds)
            {
                _residencyElapsed = 0f;
                UpdateResidency();
            }

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

        var inputs = new ResidencyInputs(
            _progress.HasCompanion,
            _settings.CompanionVisible.Value,
            _actor.Exists,
            _presentationSupported,
            _anchor,
            _actor.PlacedAnchor,
            _anchorValidity,
            ReadSeatStatus());

        switch (ResidencyPlanner.Decide(inputs))
        {
            case ResidencyAction.Remove:
                _actor.Release();
                return;

            case ResidencyAction.Rehome:
                _actor.Release();
                _actorRetryElapsed = 0f;
                break;

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

        PlacementResult placement = _hulgiPlanner.Plan(_anchor, _hulgiProbe);
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
        ShowNotice(AtlasStrings.Get(line.Key), MessageHud.MessageType.Center);
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

        PlacementResult placement = _planner.Plan(_anchor, _placementProbe);
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
            ShowNotice(AtlasStrings.Get("companion.compass.prompt"), MessageHud.MessageType.TopLeft);
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
