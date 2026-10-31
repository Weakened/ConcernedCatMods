using System;
using TheConcernedCat.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.ConcernedSteward;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using TheConcernedCat.ConcernedSteward.Domain.Interop;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Quest;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Scope;
using TheConcernedCat.ConcernedSteward.Domain.Speech;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Everything the Steward is, wired to one world.
///
/// The pieces are deliberately dumb about each other: the domain decides, the
/// adapters read and write the game, and this class is the only place that
/// knows both exist. That is what lets the whole decision surface — every
/// refusal, every measurement, every recovery — be exercised without the game
/// running.
///
/// <b>The world-load epoch is the spine.</b> A fresh one is minted on every
/// load, and it is what makes every remembered object identity stale at exactly
/// the moment it stops meaning anything. Designations, fire keys and the depot
/// all hang off it.</summary>
internal sealed class StewardRuntime
{
    private readonly StewardSettings _settings;
    private readonly Action<string> _log;
    private readonly StewardScope _scope = new StewardScope();
    private readonly StewardIntroduction _introduction = new StewardIntroduction();
    private readonly StewardQuest _quest;
    private readonly ResinWatch _resin = new ResinWatch();
    private readonly ResinSightings _sightings;
    private readonly StewardAppearance _appearance;
    private readonly SunnivaPresentation _presentation;
    private readonly StewardRecordStore _records;
    private readonly RecordBackedJournal _journal;
    private readonly StewardNpcAdoption _npc;
    private readonly StewardMaintenancePlan _plan;
    private readonly UpkeepLoop _loop;
    private readonly StewardMotion _motion;
    private readonly StewardPackStore _pack;
    private readonly DepotStore _depot;
    private readonly WorldFuelTargets _fires;
    private readonly StewardDesignationSite _site = new StewardDesignationSite();
    private readonly StewardVoice _voice;
    private bool _hasThresholds;
    private (float Below, float To, int Most) _thresholdInputs;
    private MaintenanceThresholds _thresholds;

    private string _epoch = string.Empty;
    private bool _installed;
    private bool _recordReadOnly;
    private int _recordedLoss;
    private string _carryingName = string.Empty;
    private StewardWorkerAI? _body;
    private StewardCensus _census = new StewardCensus(StewardCensusVerdict.Unknown, 0, 0, null);
    private RestockDiscovery _restock = RestockDiscovery.NotProbed;
    private bool _restockProbed;
    private string? _recordNotice;

    internal StewardRuntime(StewardSettings settings, Action<string> log)
        : this(settings, log, StewardPaths.Settlements)
    {
    }

    internal StewardRuntime(StewardSettings settings, Action<string> log, string recordRoot)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _records = new StewardRecordStore(recordRoot);
        // A lambda rather than a method group: SaveRecord grew two optional
        // parameters for the quest's write-before-you-believe-it ordering, and
        // a method group with optional parameters does not convert.
        _journal = new RecordBackedJournal(open => SaveRecord(open));
        _npc = new StewardNpcAdoption(
            NpcRoleRegistry.Shared, new StewardNpcRole(recordRoot), message => _log(message));
        _plan = new StewardMaintenancePlan(recordRoot, _npc.Registry, _npc.Identity);
        _quest = new StewardQuest(new RuntimeQuestRecorder(this));
        _sightings = new ResinSightings(
            () => _settings.QuestPickupItem.Value, message => _log(message));
        _appearance = new StewardAppearance(
            () => StewardLooks.From(
                _settings.ModelIndex.Value,
                _settings.Garment.Value,
                _settings.GarmentLegs.Value,
                hairItem: _settings.HairStyle.Value,
                hairColour: _settings.HairColour.Value,
                skinColour: string.Empty),
            message => _log(message));
        // #382: what she says reaches the screen, not only the log. The sink
        // reads the setting on every line, so muting her takes effect at once.
        _voice = new StewardVoice((line, weight) =>
        {
            if (_settings.SpeakOnScreen.Value)
            {
                HudSpeech.Say(line, weight);
            }
        });
        _loop = new UpkeepLoop(
            UpkeepLimits.WithCarry(_settings.CarryPerTrip.Value), _journal, Report, _plan, _npc, _voice);
        _motion = new StewardMotion(() => _body);
        _pack = new StewardPackStore(() => _census.Live);
        _depot = new DepotStore(() => _scope.Resolve().DepotKey);
        _fires = new WorldFuelTargets(
            () => _epoch,
            () => _census.Live?.Humanoid,
            log: message => _log(message));
        // #382: at rest, with no worker body in the world, she is a
        // player-style figure at her home post. Concerned NPC's arbiter keeps
        // the two from ever coexisting; see SunnivaPresentation.
        _presentation = new SunnivaPresentation(_settings, _npc, FigureInputs, message => _log(message));
    }

    internal StewardScope Scope => _scope;

    internal StewardIntroduction Introduction => _introduction;

    /// <summary>Sunniva's introduction: what has been found, read and said.
    /// Separate from <see cref="Introduction"/> on purpose — that one is
    /// authority over a player's materials and fails closed; this one is a story
    /// and is monotonic.</summary>
    internal StewardQuest Quest => _quest;

    internal UpkeepLoop Loop => _loop;

    /// <summary>The Steward's registration with Concerned NPC and his hold on
    /// his own body. Read by the status command so a player can be told the
    /// truthful reason when the library refused him one.</summary>
    internal StewardNpcAdoption Npc => _npc;

    internal StewardCensus Census => _census;

    internal RestockDiscovery Restock => _restock;

    internal bool RecordIsReadOnly => _recordReadOnly;

    internal string Epoch => _epoch;

    /// <summary>Builds the prefab and hooks the body's lifecycle. Called once,
    /// at plugin start, so the prefab is registered before any world's objects
    /// are created — otherwise the host destroys a saved Steward as an unknown
    /// prefab and everything in his pack with him.</summary>
    internal void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;

        // The Steward declares himself to Concerned NPC before anything else,
        // once per process. Registration is per process and not per world: the
        // library says so, and a role re-offered on every load is refused as a
        // duplicate from the second load onwards.
        //
        // His prefab is still registered by THIS product, below, under the name
        // it has always had. Handing the library durable facts is not the same
        // as handing it the prefab, and the difference is the one that cannot be
        // undone: a prefab registered under another name, or later than the main
        // menu, deletes every saved Steward on the next load.
        RoleRegistration registration = _npc.Register();
        if (registration.IsRegistered)
        {
            _log("The Steward is registered with Concerned NPC as " +
                registration.Identity.Value + ".");
        }

        if (!HudSpeech.Available)
        {
            _log("This game build has no message display the Steward recognises, so what she " +
                "says is written to this log only.");
        }

        StewardBody.ErrorLog = message => _log(message);
        StewardBody.Loaded = OnBodyLoaded;
        StewardBody.Died = OnBodyDied;
        StewardWorkerPrefab.Install(_settings.BaseCreature.Value, message => _log(message));
    }

    // ------------------------------------------------------------------
    // World lifecycle
    // ------------------------------------------------------------------

    internal void OnWorldLoaded()
    {
        // A fresh identity space. Every remembered object key from a previous
        // run stops resolving at exactly this moment, which is the point.
        _epoch = StewardIdentity.NewEpoch();
        _scope.UseIdentityEpoch(_epoch);

        // Two epochs, deliberately, and they are not the same thing. This
        // product's own epoch is what makes a remembered fire key or chest key
        // stale; the library's is what stamps a body hold, and only the library
        // may mint it. Neither is derived from the other, and neither is
        // written to disk.
        _npc.NoteWorldLoaded();
        _presentation.OnWorldLoaded();
        _restockProbed = false;
        _restock = RestockDiscovery.NotProbed;

        SettlementScope? scope = CurrentScope();
        if (scope == null)
        {
            _recordReadOnly = true;
            _recordNotice = "No world is loaded, so the Steward's record was not read.";
            return;
        }

        RecordLoadReport report = _records.Load(scope.Value);
        _recordReadOnly = !report.MayWrite;
        _journal.Writable = report.MayWrite;
        _recordNotice = report.Notice;
        _recordedLoss = report.RecordedLoss;
        _carryingName = report.FuelItemName;

        _introduction.Restore(report.Stage, report.Furthest);
        _quest.Restore(report.QuestStage, report.QuestFurthest);
        foreach (Designation designation in report.Designations)
        {
            if (!_scope.Restore(designation))
            {
                _log("A line in the Steward's record could not be taken and was left alone.");
            }
        }

        _journal.Restore(report.OpenIntents);

        if (report.Notice != null)
        {
            _log(report.Notice);
        }

        RefreshCensus();

        if (_recordedLoss > 0)
        {
            _loop.Custody.RestoreLoss(_recordedLoss);
        }

        ScopeSnapshot recoveredScope = _scope.Resolve();
        WorkAuthorityVerdict recoveryAuthority =
            StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value);
        var planEvidence = new NpcPlanEvidence(
            _npc.World,
            _census.Saved,
            bodyDied: false,
            areaIsReadable: recoveredScope.Settlement != null,
            areaMoved: false,
            mayWork: recoveryAuthority == WorkAuthorityVerdict.Granted && !_recordReadOnly,
            containersAvailable: recoveredScope.IsReady && _depot.IsAvailable && _pack.IsAvailable,
            routeAvailable: _motion.IsPresent,
            playerPaused: !_settings.TendFiresEnabled.Value,
            at: Time.time);
        _plan.OnWorldLoaded(scope.Value, planEvidence, _pack, _carryingName);
        if (!string.IsNullOrEmpty(_plan.FuelItemName))
        {
            _carryingName = _plan.FuelItemName;
        }

        // Reconciles against what he is ACTUALLY carrying and abandons whatever
        // was in flight. Nothing is replayed.
        _loop.OnWorldLoaded(_pack, _carryingName);
        if (!string.IsNullOrEmpty(_loop.Explanation))
        {
            _log(_loop.Explanation);
        }
    }

    internal void OnWorldUnloaded()
    {
        _loop.Stop("The world went away.");

        // The body hold ends with the world it stood in. Left standing it would
        // report a body destroyed with the scene as still there, and no holder
        // string would survive the load to release it. The figure goes first,
        // so it is down before its lease ends with the world.
        _presentation.OnWorldUnloaded();
        _npc.NoteWorldUnloaded();
        // The baseline was about a pack that has gone. Kept, the next world's
        // first reading would be compared against somebody else's inventory.
        _resin.Forget();
        _voice.Reset();
        _body = null;
        _census = new StewardCensus(StewardCensusVerdict.Unknown, 0, 0, null);
        StewardBody.ForgetAll();
        _epoch = string.Empty;
        _scope.UseIdentityEpoch(null);
        _restockProbed = false;
        _restock = RestockDiscovery.NotProbed;
    }

    /// <summary>One frame. Keeps the census fresh and drives the upkeep loop.
    ///
    /// The loop is driven from here rather than from inside the body's own AI
    /// tick, so that a Steward who is not loaded still reports why he is doing
    /// nothing. He is the thing a player is looking for when they ask, and "no
    /// answer at all" is the one response that reads as a broken mod.</summary>
    internal void Update()
    {
        if (!_settings.RuntimeEnabled.Value || !StewardWorldFacts.WorldIsUp)
        {
            // Nothing of hers is shown while the runtime is off.
            _presentation.YieldToWorker();
            return;
        }

        EnsureRestockProbed();
        RefreshCensus();
        WatchForResin();
        NoticeSheWouldSpeak();

        _loop.Tick(new UpkeepTick(
            Time.time,
            _settings.TendFiresEnabled.Value && _introduction.IsEngaged && !_recordReadOnly,
            StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value),
            _scope.Resolve(),
            _fires,
            _depot,
            _pack,
            _motion,
            _npc.World,
            CurrentThresholds()));

        // After the census, so a body that appeared this frame has already
        // taken the figure down and its lease with it.
        _presentation.Tick(Time.deltaTime);
    }

    /// <summary>What the figure at home needs to know, read once per pass.
    /// "Resting" is the retire verb's own ledger (S1): nothing carried, no
    /// round, no plan, nothing unreceipted, nothing unaccounted for.</summary>
    private SunnivaFigureInputs FigureInputs()
    {
        // A CS_Steward body with no identity stamped might be hers (a stamp that
        // failed leaves exactly that), so it is unknown, never "none".
        int saved = _census.Verdict == StewardCensusVerdict.Unknown || _census.Unidentified > 0
            ? -1
            : _census.Saved;
        bool settled = _introduction.IsEngaged && _quest.Stage >= StewardQuestStage.Settled;
        bool resting = _loop.Custody.Carried == 0
            && !_loop.IsWorking && _loop.Phase != UpkeepPhase.NeedsAttention
            && !_plan.HasActivePlan && !_plan.IsBlocked
            && _journal.UnresolvedIntents.Count == 0
            && !_loop.Custody.HasLoss;

        ScopeSnapshot scope = _scope.Resolve();
        Vector3? home = scope.IsReady
            ? new Vector3(scope.DepotPoint.X, scope.DepotPoint.Y, scope.DepotPoint.Z)
            : scope.Settlement != null
                ? new Vector3(scope.SettlementCentre.X, scope.SettlementCentre.Y, scope.SettlementCentre.Z)
                : (Vector3?)null;
        return new SunnivaFigureInputs(saved, settled, resting, home);
    }

    /// <summary>The refuel thresholds as configured, or the shipped ones and a
    /// single log line when the configuration describes a loop. Read every
    /// tick, so a changed setting applies to the next round.</summary>
    private MaintenanceThresholds CurrentThresholds()
    {
        float below = _settings.RefuelBelowSeconds.Value;
        float to = _settings.RefuelToSeconds.Value;
        int most = _settings.MostUnitsPerLight.Value;

        // Asked every frame, so worked out only when a setting moved: an
        // invalid setting would otherwise build its refusal sentence sixty
        // times a second.
        if (_hasThresholds && below.Equals(_thresholdInputs.Below) && to.Equals(_thresholdInputs.To)
            && most == _thresholdInputs.Most)
        {
            return _thresholds;
        }

        _thresholds = MaintenanceThresholds.FromSettings(
            below, to, most, out string? refusal, _loop.Limits.MaxUnitsPerTrip);
        _thresholdInputs = (below, to, most);
        _hasThresholds = true;
        if (refusal != null)
        {
            _log(refusal);
        }

        return _thresholds;
    }

    private void RefreshCensus()
    {
        _census = StewardCensusTaker.Take(StewardWorkerPrefab.PrefabName, StewardRole.Worker.Value);
        if (_census.Live == null)
        {
            // No body to hold. Released unconditionally, because releasing one
            // we do not have changes nothing and checking first is how a hold
            // survives the thing it was a hold on.
            _npc.ReleaseBody();
            _body = null;
            return;
        }

        // Her worker body is here, so the figure at home goes - down first, then
        // its lease - before the worker lease is asked for: the arbiter refuses
        // a worker claim while the figure's lease is held.
        _presentation.YieldToWorker();

        // Re-asked every tick on purpose: asking again as the same holder is
        // AlreadyHeld, which is a grant, and a runtime that asked once would
        // never notice the arbiter disagreeing with it.
        _npc.ClaimBody();

        StewardWorkerAI? ai = _census.Live.GetComponent<StewardWorkerAI>();
        if (!ReferenceEquals(ai, _body))
        {
            _body = ai;
            BindBody(ai);
        }
    }

    private void BindBody(StewardWorkerAI? ai)
    {
        if (ai == null)
        {
            return;
        }

        // Dressed when a body binds, which is the one moment a live, owned
        // VisEquipment is in front of us. Idempotent, so re-binding after a
        // reload costs nothing and a body that arrives undressed is dressed.
        _appearance.Apply(ai.gameObject);

        ai.UseSitePolicy(new WorldStewardSitePolicy());
        ai.ErrorLog = message => _log(message);
        ai.DebugLog = _settings.DebugLogging.Value ? message => _log(message) : null;
        ai.AuthorityGate = () =>
            _settings.RuntimeEnabled.Value
            && StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value)
                == WorkAuthorityVerdict.Granted;
    }

    private void OnBodyLoaded(StewardBody body)
    {
        if (!StewardRole.IsSteward(body.Key))
        {
            // Not his. A body carrying somebody else's identity is left
            // entirely alone.
            return;
        }

        RefreshCensus();
        _log(StewardRole.DisplayNameFallbackCapitalised + " is here, carrying " +
            body.ItemCount.ToString(CultureInfo.InvariantCulture) + " thing(s).");
    }

    /// <summary>He died holding a player's wood.
    ///
    /// The items are already on the ground — his body put them there through
    /// vanilla's own drop before this runs — so nothing is lost and nothing is
    /// recreated. What the ledger needs to know is that they are no longer in
    /// his hands, and that is exactly what is recorded: the units leave
    /// <see cref="FuelPlace.Carried"/> and become unaccounted for, because
    /// "somewhere on the ground where he fell" is not a place this ledger can
    /// name. The Steward stops until a person says they have looked.</summary>
    private void OnBodyDied(StewardBody body, IReadOnlyList<DroppedItem> dropped, Vector3 where)
    {
        if (!StewardRole.IsSteward(body.Key))
        {
            return;
        }

        int carried = _loop.Custody.Carried;
        if (carried > 0)
        {
            _loop.Custody.RecordLost(carried);
            _recordedLoss = _loop.Custody.Unaccounted;
            SaveRecord(_journal.UnresolvedIntents);
        }

        string death = StewardRole.DisplayNameFallbackCapitalised + " died at " +
            where.ToString() + ". Everything she carried is on the ground there — " +
            Describe(dropped) + ". Nothing was recreated.";
        if (carried > 0 || _loop.Custody.HasLoss)
        {
            _loop.RequireAttention(death);
        }
        else
        {
            // No material ever left a named place. End any empty reservation
            // instead of turning an ordinary empty-handed death into a custody
            // incident that needs a human resolution.
            _loop.Stop(death);
        }

        _log(_loop.Explanation);
    }

    private static string Describe(IReadOnlyList<DroppedItem> dropped)
    {
        if (dropped == null || dropped.Count == 0)
        {
            return "she was carrying nothing";
        }

        var text = new System.Text.StringBuilder();
        for (int index = 0; index < dropped.Count; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            text.Append(dropped[index].Count.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(dropped[index].SharedName);
        }

        return text.ToString();
    }

    // ------------------------------------------------------------------
    // The player's own acts
    // ------------------------------------------------------------------

    internal string MarkSettlementArea(float radius)
    {
        string? blocked = WhyNotAvailable();
        if (blocked != null)
        {
            return blocked;
        }

        Vector3? at = StewardTargets.LocalPlayerPosition();
        if (at == null)
        {
            return "There is no local player, so there is nowhere to mark.";
        }

        DesignationResult result = _scope.Designate(
            DesignationRequest.Area(
                DesignationKind.SettlementArea, new SitePoint(at.Value.x, at.Value.y, at.Value.z), radius),
            _site);
        Persist();
        return result.Describe();
    }

    internal string MarkSupplyDepot()
    {
        string? blocked = WhyNotAvailable();
        if (blocked != null)
        {
            return blocked;
        }

        if (!StewardTargets.TryResolveHoveredContainer(
                out Container? container, out string? key, out string failure))
        {
            return "Nothing was marked: " + failure;
        }

        Vector3 at = container!.transform.position;
        DesignationResult result = _scope.ReplaceStaleDepot(
            DesignationRequest.Container(new SitePoint(at.x, at.y, at.z), key), _site);
        Persist();
        return result.Describe();
    }

    internal string Undesignate(DesignationKind kind)
    {
        string? blocked = WhyNotAvailable();
        if (blocked != null)
        {
            return blocked;
        }

        _scope.Undesignate(kind);
        _loop.Stop("What the Steward was working on was un-marked.");
        Persist();
        return "Un-marked. The Steward has stopped; anything she is carrying is still her to " +
            "put back, and her record still says so.";
    }

    /// <summary>Takes the introduction as far as it will go in one command, so
    /// a player who just wants him working types one thing.
    ///
    /// Resumable rather than atomic: each step is checked and recorded on its
    /// own, so a refusal part way through leaves the introduction exactly where
    /// it got to, and running the command again continues from there instead of
    /// starting over. That is also what makes it safe after a crash.</summary>
    internal string Recruit()
    {
        string? blocked = WhyNotAvailable();
        if (blocked != null)
        {
            return blocked;
        }

        if (_introduction.IsEngaged)
        {
            return MoveIn(EnsureBody(StewardSentences.AlreadyAtStage(IntroductionStage.Engaged)));
        }

        // #382: taking her on is moving her in, so it is asked from inside the
        // settlement or not at all - before any stage is recorded or any beat is
        // said, so "she has set her pack down by the hearth" is never told
        // about a body that did not appear.
        RefreshCensus();
        if (_census.Verdict != StewardCensusVerdict.Present && !PlayerIsInsideSettlement())
        {
            return "Stand inside your marked settlement to take her on; she moves in where you " +
                "are. Nothing was agreed yet.";
        }

        bool returning = _introduction.HasEverEngaged;
        var facts = new IntroductionFacts(
            authorised: StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value)
                == WorkAuthorityVerdict.Granted,
            hasSettlementArea: _scope.Book.Has(DesignationKind.SettlementArea),
            recordWritable: !_recordReadOnly);

        var said = new List<string>();
        foreach (IntroductionStage stage in new[]
        {
            IntroductionStage.Noticed, IntroductionStage.Offered, IntroductionStage.Engaged,
        })
        {
            IntroductionResult step = _introduction.Advance(stage, facts);
            if (step.Outcome == IntroductionOutcome.Refused)
            {
                Persist();
                said.Add(step.Describe());
                return string.Join(" ", said.ToArray());
            }

            // Every advance is written down before the next one is taken, so a
            // failure at any point leaves a record of exactly what happened.
            if (step.Changed)
            {
                if (!Persist())
                {
                    return "The Steward's record could not be written, so nothing was agreed. " +
                        "Nothing is ever agreed in memory alone.";
                }

                // A returning Steward is not introduced again.
                if (!returning || stage == IntroductionStage.Engaged)
                {
                    said.Add(returning ? StewardSentences.WelcomeBack() : step.Describe());
                }
            }
        }

        return MoveIn(EnsureBody(string.Join(" ", said.ToArray())));
    }

    /// <summary>The last beat of her introduction: she does not treat it as a
    /// posting, she moves in. Told only once her body is actually standing in
    /// the settlement - after <see cref="EnsureBody"/>, never before it - so the
    /// beat is never said about a body that did not appear. Re-asked by a later
    /// recruit, so a first attempt whose body failed still gets its beat.
    /// Costs nothing if it refuses: the employment is already recorded.</summary>
    private string MoveIn(string answer)
    {
        if (_census.Verdict != StewardCensusVerdict.Present)
        {
            return answer;
        }

        string[] settled = AdvanceQuest(StewardQuestStage.Settled);
        return settled.Length == 0 ? answer : answer + " " + string.Join(" ", settled);
    }

    // ------------------------------------------------------------------
    // Sunniva's introduction
    // ------------------------------------------------------------------

    /// <summary>One look at the player's pack, and only while there is a reason
    /// to look.
    ///
    /// <b>It stops costing anything the moment the object is found.</b> After
    /// that the quest can never produce another, so the watch is not run at all
    /// - which is why walking the player's stacks every tick is affordable: it
    /// happens once per world, until it happens.</summary>
    private void WatchForResin()
    {
        if (_quest.ObjectHasBeenFound || _recordReadOnly)
        {
            return;
        }

        ResinSighting sighting = _resin.Observe(
            _sightings.CountCarried(), _sightings.AWindowIsOpen());
        if (sighting == ResinSighting.PickedUp)
        {
            NoticeResinPickedUp();
        }
    }

    /// <summary>A player has picked up resin. Produces the one flint and steel,
    /// if it has not been produced.
    ///
    /// <b>Called from an inventory-changed callback</b>, which is somebody
    /// else's callback list, so it must never throw and must be cheap when there
    /// is nothing to do. Both are true: the quest answers "already" on one enum
    /// comparison, and the one write that can happen is the record the Steward
    /// writes anyway.</summary>
    internal void NoticeResinPickedUp()
    {
        StewardQuestResult result = _quest.NoticeResinPickedUp(QuestFacts());
        if (!result.Changed)
        {
            return;
        }

        SayQuest(result.Stage);
    }

    /// <summary>The player looks at the runes properly.</summary>
    internal string ReadTheRunes()
    {
        if (_quest.Stage < StewardQuestStage.Found)
        {
            return "You have nothing with runes on it.";
        }

        if (_quest.Stage > StewardQuestStage.Found)
        {
            return string.Join(
                " ", StewardQuestSentences.LinesFor(StewardQuestStage.Examined)) + " " +
                StewardQuestSentences.Describe(_quest.Stage);
        }

        string[] said = AdvanceQuest(StewardQuestStage.Examined);
        return said.Length == 0
            ? "Not yet: " + StewardQuestSentences.DescribeRefusal(
                _quest.Stage == StewardQuestStage.Found
                    ? StewardQuestRefusal.NotRecorded
                    : StewardQuestRefusal.OutOfOrder) + "."
            : string.Join(" ", said);
    }

    /// <summary>She turns up, once there is something to turn up to.
    ///
    /// The trigger is the settlement being marked and not a timer or a distance:
    /// the premise of this character is that there is already something worth
    /// looking after, so the moment a player says "this is mine, this far" is
    /// the moment she has a reason to be here. Asked from the tick, and silent
    /// every time but one.</summary>
    private void NoticeSheWouldSpeak()
    {
        if (_quest.Stage != StewardQuestStage.Examined
            || !_scope.Book.Has(DesignationKind.SettlementArea))
        {
            return;
        }

        AdvanceQuest(StewardQuestStage.Answered);
    }

    /// <summary>Takes one beat and says it. Returns the lines so a command can
    /// hand them back and the tick can let them go to the log.</summary>
    private string[] AdvanceQuest(StewardQuestStage to)
    {
        StewardQuestResult result = _quest.Advance(to, QuestFacts());
        if (!result.Changed)
        {
            return Array.Empty<string>();
        }

        string[] lines = StewardQuestSentences.LinesFor(result.Stage);
        foreach (string line in lines)
        {
            _log(line);
        }

        // On screen as well: the beat that arrives from the tick (she turns
        // up when the settlement is marked) has no console to land in.
        _voice.Narrate(lines);
        return lines;
    }

    /// <summary>The discovery, said where the player will see it. Its first
    /// line - the sentence #382 specifies word for word - goes to the middle of
    /// the screen; the rest follow in the corner.</summary>
    private void SayQuest(StewardQuestStage stage)
    {
        string[] lines = StewardQuestSentences.LinesFor(stage);
        foreach (string line in lines)
        {
            _log(line);
        }

        _voice.Narrate(
            lines, stage == StewardQuestStage.Found ? SpeechWeight.Important : SpeechWeight.Aside);
    }

    private StewardQuestFacts QuestFacts() =>
        new StewardQuestFacts(
            somebodyIsThere: StewardWorldFacts.WorldIsUp && CurrentScope() != null,
            recordWritable: !_recordReadOnly);

    /// <summary>The quest's way of writing a stage down BEFORE it is believed.
    ///
    /// A nested type rather than the runtime implementing the interface itself:
    /// the recorder is one method with one caller, and making the whole runtime
    /// an <c>IStewardQuestRecorder</c> would put a public-looking write path on
    /// the type every command already holds.</summary>
    private sealed class RuntimeQuestRecorder : IStewardQuestRecorder
    {
        private readonly StewardRuntime _runtime;

        internal RuntimeQuestRecorder(StewardRuntime runtime)
        {
            _runtime = runtime;
        }

        public bool TryRecord(StewardQuestStage stage, StewardQuestStage furthest) =>
            _runtime.SaveRecord(_runtime._journal.UnresolvedIntents, stage, furthest);
    }

    internal string Dismiss()
    {
        string? blocked = WhyNotAvailable();
        if (blocked != null)
        {
            return blocked;
        }

        if (!_introduction.Dismiss())
        {
            return "She does not work here.";
        }

        _loop.Stop(StewardSentences.Dismissed());
        Persist();
        return StewardSentences.Dismissed() +
            " Her body stays where it is, and nothing has been destroyed.";
    }

    internal string Acknowledge()
    {
        string answer = _loop.Acknowledge(_pack, _carryingName);
        _recordedLoss = _loop.Custody.Unaccounted;
        Persist();
        return answer;
    }

    private bool PlayerIsInsideSettlement()
    {
        Vector3? at = StewardTargets.LocalPlayerPosition();
        Designation? home = _scope.Resolve().Settlement;
        return at != null && home != null
            && home.Contains(new SitePoint(at.Value.x, at.Value.y, at.Value.z));
    }

    /// <summary>Spawns his body if there is not one. Refuses rather than
    /// spawning a second: two bodies with one identity is the state the census
    /// exists to notice, and creating it deliberately would be worse than any
    /// accident.</summary>
    private string EnsureBody(string prefix)
    {
        RefreshCensus();

        // Anything but "there is none" is answered by the census itself: he is
        // already here, he is out of range, there are two of him, or the world
        // could not be asked. Only Missing gets as far as spawning.
        if (_census.Verdict != StewardCensusVerdict.Missing)
        {
            return prefix + " " + _census.Describe();
        }

        if (!StewardWorkerPrefab.IsReady)
        {
            return prefix + " She has no body to appear in: " +
                (StewardWorkerPrefab.LastFailure ?? "the prefab was not built") + ".";
        }

        Vector3? at = StewardTargets.LocalPlayerPosition();
        if (at == null)
        {
            return prefix + " There is no local player, so there is nowhere for her to stand.";
        }

        // #382: she moves INTO the settlement. Her body is saved where it
        // first stands, so it first stands inside the ground the player marked
        // as home, never wherever the player happened to type the command.
        Designation? home = _scope.Resolve().Settlement;
        if (home == null || !home.Contains(new SitePoint(at.Value.x, at.Value.y, at.Value.z)))
        {
            return prefix + " She will move in once you are standing inside your marked " +
                "settlement; run \"cs_steward recruit\" again from there.";
        }

        // The figure at home goes before her worker body appears: never both.
        _presentation.YieldToWorker();
        StewardWorkerAI? spawned = StewardWorkerPrefab.Spawn(at.Value, Quaternion.identity);
        if (spawned == null)
        {
            return prefix + " She could not appear here; that ground is not loaded.";
        }

        if (!StewardBody.TryStamp(spawned.gameObject, StewardRole.Worker.Value))
        {
            // Unstamped, so the census will not adopt it and will report it as
            // an unidentified body. Destroying it would be the other option and
            // is worse: it is a live object this process may not own.
            return prefix + " She appeared but could not be given her identity; she will not work. " +
                "Please report this.";
        }

        RefreshCensus();
        return prefix + " " + _census.Describe();
    }

    // ------------------------------------------------------------------
    // Status
    // ------------------------------------------------------------------

    /// <summary>What he is doing and why, in the order a player would want it.
    ///
    /// Every line is something that could be the reason he is standing still,
    /// so the answer to "why is nothing happening" is always on screen rather
    /// than in a log.</summary>
    internal string Status()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(StewardRole.DisplayNameFallbackCapitalised + " (" + StewardRole.Worker + ")");
        text.AppendLine("  Runtime: " + (_settings.RuntimeEnabled.Value ? "on" : "OFF (the default)"));
        text.AppendLine("  Fire tending: " + (_settings.TendFiresEnabled.Value ? "on" : "OFF (the default)"));
        text.AppendLine("  Allowed here: " +
            WorkAuthorityPolicy.Describe(
                StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value)));
        text.AppendLine("  Introduction: " + _introduction);
        text.AppendLine("  Scope: " + _scope.Resolve().Describe());
        text.AppendLine("  Body: " + _census.Describe());
        text.AppendLine("  " + _presentation.Describe());
        text.AppendLine("  Doing: " + _loop.Phase + " — " + _loop.Explanation);
        if (_loop.Attention.Length != 0)
        {
            text.AppendLine("  Needs you: " + _loop.Attention);
        }

        if (_loop.LastOutcome != null)
        {
            text.AppendLine("  Last round: " + _loop.LastOutcome.Value.Describe());
        }

        text.AppendLine("  Quest: " + StewardQuestSentences.Describe(_quest.Stage));
        text.AppendLine("  Wood: " + _loop.Custody.Describe());
        text.AppendLine("  Trips: " + _loop.TripsCompleted.ToString(CultureInfo.InvariantCulture) +
            ", fuel added: " + _loop.UnitsBurnedTotal.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("  " + _restock.LogLine);
        text.Append("  Record: " + (_recordReadOnly ? "READ-ONLY — " : "writable") +
            (_recordNotice ?? string.Empty));
        return text.ToString();
    }

    // ------------------------------------------------------------------
    // Record
    // ------------------------------------------------------------------

    private SettlementScope? CurrentScope()
    {
        long world = StewardWorldFacts.WorldId();

        // One settlement per world for this slice. The id is a constant rather
        // than a name the player types, because there is nothing yet to tell two
        // of them apart and inventing one now would put a guess in a save.
        return world == 0L ? (SettlementScope?)null : new SettlementScope(world, new SettlementId("home"));
    }

    private bool Persist() => SaveRecord(_journal.UnresolvedIntents);

    /// <summary>Writes the whole record.
    ///
    /// <paramref name="questStage"/> and <paramref name="questFurthest"/> are
    /// the <b>proposed</b> quest stage rather than the current one, and they
    /// exist because the quest's guarantee is an ordering: the flint and steel
    /// is written down before it is believed in, so a game that closes between
    /// the two comes back with the object found rather than about to be found
    /// again. Every other caller passes null and gets the quest as it stands.
    /// </summary>
    private bool SaveRecord(
        IReadOnlyList<UpkeepIntent> open,
        StewardQuestStage? questStage = null,
        StewardQuestStage? questFurthest = null)
    {
        if (_recordReadOnly)
        {
            return false;
        }

        SettlementScope? scope = CurrentScope();
        if (scope == null)
        {
            return false;
        }

        Exception? failure = _records.Save(
            scope.Value,
            _introduction.Stage,
            _introduction.FurthestReached,
            _scope.Book.Designations,
            open,
            _loop.Custody.Unaccounted,
            CarryingName(),
            questStage ?? _quest.Stage,
            questFurthest ?? _quest.FurthestReached);
        if (failure == null)
        {
            return true;
        }

        // #411: a record-write failure is a filesystem failure, so this line
        // carried the path it failed on and the machine's user name.
        _log("The Steward's record could not be written: " + SafeFailure.Brief(failure) +
            ". Nothing was agreed or moved on the strength of it.");
        return false;
    }

    /// <summary>The fuel name to record, so a reload can count his pack with
    /// the same predicate the withdrawal used. The name of whatever he is
    /// carrying now, or the last one recorded.</summary>
    private string CarryingName()
    {
        foreach (UpkeepIntent intent in _journal.UnresolvedIntents)
        {
            if (intent.Step != UpkeepStep.Feed && !string.IsNullOrEmpty(intent.Detail))
            {
                return intent.Detail;
            }
        }

        return _carryingName;
    }

    private string? WhyNotAvailable()
    {
        if (!_settings.RuntimeEnabled.Value)
        {
            return "The Steward is switched off. Set [Steward] StewardRuntimeEnabled = true to " +
                "opt in; nothing in this mod touches your world until you do.";
        }

        if (!StewardWorldFacts.WorldIsUp)
        {
            return "No world is loaded.";
        }

        WorkAuthorityVerdict verdict =
            StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value);
        if (verdict != WorkAuthorityVerdict.Granted)
        {
            return WorkAuthorityPolicy.Describe(verdict);
        }

        if (_recordReadOnly)
        {
            return "The Steward's record cannot be written, so nothing new is being agreed. " +
                (_recordNotice ?? string.Empty);
        }

        return null;
    }

    private void Report(string message) => _log(message);

    // ------------------------------------------------------------------
    // The optional restock provider
    // ------------------------------------------------------------------

    /// <summary>Looks once per world session for a mod that could bring wood to
    /// the depot, and writes down what it found.
    ///
    /// <b>The answer changes nothing.</b> Bulk restocking is not built; this is
    /// the probe, so that the shape of "the other mod is absent, or speaks a
    /// different version" is settled while there is nothing behind it. Absent,
    /// too old, mismatched or broken all leave the Steward tending fires from
    /// the marked chest exactly as before.</summary>
    private void EnsureRestockProbed()
    {
        if (_restockProbed)
        {
            return;
        }

        _restockProbed = true;
        _restock = RestockProviderGate.Evaluate(LookUpRestockProvider);
        _log(_restock.LogLine);
    }

    /// <summary>The only place in this product that asks BepInEx about another
    /// mod. One GUID string, one public property name, and only BCL types
    /// cross: there is no compile-time reference to anything.</summary>
    private static RestockProviderLookup LookUpRestockProvider()
    {
        if (!Chainloader.PluginInfos.TryGetValue(HaulContract.ProviderGuid, out PluginInfo info)
            || info == null)
        {
            return RestockProviderLookup.NotFound();
        }

        // System.Version: the game declares a global Version class of its own.
        System.Version? version = info.Metadata?.Version;
        object? instance = info.Instance;
        if (instance == null)
        {
            return RestockProviderLookup.Detected(version, null, "the plugin instance is not available");
        }

        try
        {
            PropertyInfo? property = instance.GetType().GetProperty(
                CapabilityMap.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
            {
                return RestockProviderLookup.Detected(
                    version, null, "it publishes no " + CapabilityMap.PropertyName + " property");
            }

            return RestockProviderLookup.Detected(version, property.GetValue(instance));
        }
        catch (Exception exception)
        {
            return RestockProviderLookup.Detected(
                version, null, "reading its capabilities threw " + exception.GetType().Name);
        }
    }
}
