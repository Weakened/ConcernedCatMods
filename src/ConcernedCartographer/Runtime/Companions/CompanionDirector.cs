using System;
using BepInEx.Logging;
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

    private const float ScopeRetrySeconds = 2f;
    private const float PresenceIntervalSeconds = 0.25f;
    private const float AnchorRecheckSeconds = 5f;

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
        _placementProbe = new WorldPlacementProbe(log);
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
        _settings.CompanionToolsOnly.Value);

    /// <summary>Called when a world becomes available. Drops any previous
    /// world's state; the next tick resolves the new one.</summary>
    public void OnWorldChanged()
    {
        ReleaseCompass();
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

            UpdateProximityAndInput();
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

    private void RefreshAnchor()
    {
        if (!_anchorSource.TryGetAnchor(out CompanionAnchor current))
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
    private bool InteractPressed(Player player)
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
            if (hovered != null && hovered.GetComponentInParent<BrokenCompassObject>() == null)
            {
                // The player is aiming at something else entirely; their key
                // press belongs to that thing.
                return false;
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
        bool toolsOnly)
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
}
