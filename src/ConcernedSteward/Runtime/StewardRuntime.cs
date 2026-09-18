using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Interop;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Scope;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
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
    private readonly StewardRecordStore _records;
    private readonly RecordBackedJournal _journal;
    private readonly ActorModeOwner _modes = new ActorModeOwner(StewardRole.Worker);
    private readonly UpkeepLoop _loop;
    private readonly StewardMotion _motion;
    private readonly StewardPackStore _pack;
    private readonly DepotStore _depot;
    private readonly WorldFuelTargets _fires;
    private readonly StewardDesignationSite _site = new StewardDesignationSite();

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
        : this(settings, log, Path.Combine(
            Paths.ConfigPath, "ConcernedCatMods", "ConcernedSteward", "settlements"))
    {
    }

    internal StewardRuntime(StewardSettings settings, Action<string> log, string recordRoot)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _records = new StewardRecordStore(recordRoot);
        _journal = new RecordBackedJournal(SaveRecord);
        _loop = new UpkeepLoop(UpkeepLimits.Default, _journal, _modes, Report);
        _motion = new StewardMotion(() => _body);
        _pack = new StewardPackStore(() => _census.Live);
        _depot = new DepotStore(() => _scope.Resolve().DepotKey);
        _fires = new WorldFuelTargets(
            () => _epoch,
            () => _census.Live?.Humanoid,
            log: message => _log(message));
    }

    internal StewardScope Scope => _scope;

    internal StewardIntroduction Introduction => _introduction;

    internal UpkeepLoop Loop => _loop;

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
            return;
        }

        EnsureRestockProbed();
        RefreshCensus();

        _loop.Tick(new UpkeepTick(
            Time.time,
            _settings.TendFiresEnabled.Value && _introduction.IsEngaged && !_recordReadOnly,
            StewardWorldFacts.EvaluateAuthority(_settings.RuntimeEnabled.Value),
            _scope.Resolve(),
            _fires,
            _depot,
            _pack,
            _motion));
    }

    private void RefreshCensus()
    {
        _census = StewardCensusTaker.Take(StewardWorkerPrefab.PrefabName, StewardRole.Worker.Value);
        if (_census.Live == null)
        {
            _body = null;
            return;
        }

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

        _loop.Stop(
            StewardRole.DisplayNameFallbackCapitalised + " died at " +
            where.ToString() + ". Everything he carried is on the ground there — " +
            Describe(dropped) + ". Nothing was recreated.");
        _log(_loop.Explanation);
    }

    private static string Describe(IReadOnlyList<DroppedItem> dropped)
    {
        if (dropped == null || dropped.Count == 0)
        {
            return "he was carrying nothing";
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
        return "Un-marked. The Steward has stopped; anything he is carrying is still his to " +
            "put back, and his record still says so.";
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
            return EnsureBody(StewardSentences.AlreadyAtStage(IntroductionStage.Engaged));
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

        return EnsureBody(string.Join(" ", said.ToArray()));
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
            return "He does not work here.";
        }

        _loop.Stop(StewardSentences.Dismissed());
        Persist();
        return StewardSentences.Dismissed() +
            " His body stays where it is, and nothing has been destroyed.";
    }

    internal string Acknowledge()
    {
        string answer = _loop.Acknowledge();
        _recordedLoss = _loop.Custody.Unaccounted;
        Persist();
        return answer;
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
            return prefix + " He has no body to appear in: " +
                (StewardWorkerPrefab.LastFailure ?? "the prefab was not built") + ".";
        }

        Vector3? at = StewardTargets.LocalPlayerPosition();
        if (at == null)
        {
            return prefix + " There is no local player, so there is nowhere for him to stand.";
        }

        StewardWorkerAI? spawned = StewardWorkerPrefab.Spawn(at.Value, Quaternion.identity);
        if (spawned == null)
        {
            return prefix + " He could not appear here; that ground is not loaded.";
        }

        if (!StewardBody.TryStamp(spawned.gameObject, StewardRole.Worker.Value))
        {
            // Unstamped, so the census will not adopt it and will report it as
            // an unidentified body. Destroying it would be the other option and
            // is worse: it is a live object this process may not own.
            return prefix + " He appeared but could not be given his identity; he will not work. " +
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
        text.AppendLine("  Doing: " + _loop.Phase + " — " + _loop.Explanation);
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

    private bool SaveRecord(IReadOnlyList<UpkeepIntent> open)
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
            CarryingName());
        if (failure == null)
        {
            return true;
        }

        _log("The Steward's record could not be written: " + failure.GetType().Name + ": " +
            failure.Message + ". Nothing was agreed or moved on the strength of it.");
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
