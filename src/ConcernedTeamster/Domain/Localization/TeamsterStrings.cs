using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedTeamster.Domain.Localization;

/// <summary>The localization catalog for Concerned Teamster's user-facing
/// strings (CT-032), mirroring the pattern proven in Concerned Cartographer:
/// English defaults, override loading from a translator file, a template
/// generator, and English fallback so a partial translation can never blank
/// the UI. Additions over the Cartographer version: a missing-key is tracked
/// so the caller can log it ONCE (fallback to the key text meanwhile), and
/// overrides whose <c>{n}</c> placeholders do not match the English are
/// rejected at parse time, not left to fail at format time. Pure and
/// game-free; the adapter wires file IO and the game language.</summary>
public static class TeamsterStrings
{
    public const string TemplateHeader =
        "# ConcernedTeamster strings v1 — key<TAB>translation (missing keys fall back to English)";

    // English catalog. Keys are stable contracts (namespaced by surface);
    // values may carry {0},{1}... placeholders resolved by Format.
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // Route picker (CT-022)
        ["routes.pick"] = "Pick a route to profile.",
        ["routes.selected"] = "Selected: {0}",
        ["routes.lostSelection"] = "Selected route is no longer available in Cartographer — pick another.",
        ["routes.none"] = "No routes in this world yet — draw one in Cartographer.",
        ["routes.unreadable"] = "Cartographer routes are not readable right now.",
        ["routes.unreadableCleared"] = "Cartographer routes are not readable right now — selection cleared.",
        ["routes.unnamed"] = "(unnamed route)",
        ["routes.noGeometry"] = "(no usable geometry)",

        // Route report (CT-024)
        ["report.title"] = "Route report: {0}",
        ["report.needProfile"] = "Select a route and let it finish profiling first.",
        ["report.noProblems"] =
            "No problem sections: every sampled grade stays under {0}% and nothing went unsampled.",
        ["report.loadUnavailableNoModel"] = "Load advice unavailable: no calibration data loaded.",
        ["report.loadUnavailableNoGrade"] = "Load advice unavailable: no sampled grade data yet.",

        // Shared panel chrome
        ["ui.close"] = "Close",

        // Cart Status surface (CT-005): presenter lines + panel chrome.
        // Numbers arrive pre-formatted (invariant culture) so translations
        // reorder words, never digits.
        ["status.title"] = "Cart Status",
        ["status.cartButton"] = "Cart",
        ["status.tripsButton"] = "Trips",
        ["status.routesButton"] = "Routes",
        ["status.manifestButton"] = "Manifest",
        ["status.guidanceButton"] = "Guidance",
        ["status.engageBrake"] = "Engage brake",
        ["status.releaseBrake"] = "Release brake",
        ["status.telemetryOff"] = "Cart telemetry is unavailable — see the log for details.",
        ["status.noCart"] = "No cart nearby.",
        ["status.pullingThisCart"] = "Pulling this cart",
        ["status.nearbyCart"] = "Nearby cart",
        ["status.totalMass"] = "Total mass: {0}",
        ["status.breakdownCargoUnknown"] = "Base {0} + cargo unknown",
        ["status.breakdown"] = "Base {0} + cargo {1}",
        ["status.breakdownScaled"] = "Base {0} + cargo {1} × {2}",
        ["status.gradeUnavailable"] = "Grade: unavailable",
        ["status.grade"] = "Grade: {0}% {1}",
        ["status.gradeWordClimbing"] = "climbing",
        ["status.gradeWordDescending"] = "descending",
        ["status.gradeWordLevel"] = "level",
        ["status.surface"] = "Surface: {0}",
        ["status.surfaceUntouched"] = "untouched ground",
        ["status.surfaceDirt"] = "dirt path",
        ["status.surfaceCultivated"] = "cultivated soil",
        ["status.surfacePaved"] = "paved road",
        ["status.surfaceUnknown"] = "unknown",
        ["status.pulledByYou"] = "Pulled by you",
        ["status.attachedOtherPuller"] = "Attached to another puller",
        ["status.notAttached"] = "Not attached",
        ["status.stale"] = "STALE — last update {0} s ago",
        ["status.updated"] = "Updated {0} s ago",

        // Cargo manifest surface (CT-007): presenter lines + panel chrome.
        ["manifest.title"] = "Cargo Manifest",
        ["manifest.filterPlaceholder"] = "filter items…",
        ["manifest.colItem"] = "Item",
        ["manifest.colCount"] = "Count",
        ["manifest.colUnit"] = "Unit",
        ["manifest.colLine"] = "Line",
        ["manifest.noContainer"] = "No cart container available.",
        ["manifest.empty"] = "Cart is empty.",
        ["manifest.noMatch"] = "No items match \"{0}\".",
        ["manifest.totalLine"] = "Total weight: {0} · {1} items",
        ["manifest.totalLineWithUnknown"] = "Total weight: {0} (+{1} unknown) · {2} items",
        ["manifest.captured"] = "Captured {0} s ago",
        ["manifest.capturedStale"] = "STALE — captured {0} s ago",
        ["manifest.row"] = "{0}   ×{1}   unit {2}   line {3}",
        ["manifest.overflow"] = "… {0} more — sort or filter to narrow",

        // Load model verdict text (CT-008) — quoted verbatim by warnings,
        // diagnostics, guidance, and route reports.
        ["load.invalidQuery"] = "invalid grade or mass",
        ["load.contradictory"] = "contradictory calibration rows cover this query; re-run the protocol",
        ["load.outsideCoverage"] = "outside calibrated coverage — no row answers this grade and mass",
        ["load.rowDescribe"] = "a {0} row {1} {2}% with mass {3}",
        ["load.verbFailedAt"] = "failed at",
        ["load.verbClimbedAt"] = "climbed at",
        ["load.verbMarginalAt"] = "was marginal at",
        ["load.basisPrior"] = "Prior",
        ["load.basisDerivedConstant"] = "DerivedConstant",
        ["load.basisMeasured"] = "Measured",

        // Warnings (CT-009): situation + action pairs and the composed line.
        ["warn.cueDanger"] = "[!!] DANGER",
        ["warn.cueCaution"] = "[!] CAUTION",
        ["warn.line"] = "{0} — {1} {2}",
        ["warn.impossibleSituation"] = "This load cannot climb this grade ({0}, mass {1}; {2}).",
        ["warn.impossibleAction"] = "Lighten the load or find a shallower path.",
        ["warn.marginalSituation"] = "This climb is marginal ({0}, mass {1}; {2}).",
        ["warn.marginalAction"] = "Expect stalls — consider dropping some cargo.",
        ["warn.steepSituation"] = "Steep climb ahead ({0}) with cart mass {1}.",
        ["warn.steepAction"] = "Check your load before committing; calibration has no verdict here.",

        // Stuck diagnostics (CT-013): the composed line, class labels, and
        // evidence/action pairs.
        ["diag.line"] = "[?] STUCK — {0}: {1} {2}",
        ["diag.labelImpossibleLoad"] = "overloaded for this grade",
        ["diag.labelMarginalLoad"] = "load is marginal here",
        ["diag.labelSteepClimb"] = "steep climb",
        ["diag.labelObstruction"] = "obstruction or grounded chassis",
        ["diag.labelUnclear"] = "cause unclear",
        ["diag.noTerrainEvidence"] = "pulling with no movement and no terrain data.",
        ["diag.noTerrainAction"] = "Look for obstacles around the wheels.",
        ["diag.descentEvidence"] = "not moving on a {0} descent — stalls there are unusual.",
        ["diag.descentAction"] = "Check for obstacles or a grounded chassis.",
        ["diag.mildGradeEvidence"] = "near-level ground ({0}) does not explain a stall.",
        ["diag.mildGradeAction"] = "Look for rocks, stumps, or a terrain lip at the wheels; back up and re-approach.",
        ["diag.verdictEvidence"] = "{0}.",
        ["diag.impossibleAction"] = "Lighten the load or find a shallower path.",
        ["diag.marginalAction"] = "Drop some cargo and try again.",
        ["diag.provenYetStuckEvidence"] = "this load is proven to climb {0} ({1}), yet the cart is not moving.",
        ["diag.provenYetStuckAction"] = "Something blocks the cart — check the wheels and the ground line.",
        ["diag.steepClimbEvidence"] = "a {0} climb with no calibration verdict.",
        ["diag.steepClimbAction"] = "Try a shallower route, or lighten the load and retry.",
        ["diag.unclearClimbEvidence"] = "a {0} climb; calibration has no verdict and the grade alone is not conclusive.",
        ["diag.unclearClimbAction"] = "Check for obstacles first, then try with less cargo.",

        // Cooperative effort (CT-028). Values never begin or end with
        // whitespace (the translator file trims line ends); joins live in
        // code or in placeholders.
        ["coop.helpingCount"] = "{0} helping",
        ["coop.hinderingCount"] = "{0} hindering",
        ["coop.unclearCount"] = "{0} unclear",
        ["coop.nameList"] = "({0})",
        ["coop.you"] = "you",
        ["coop.teammate"] = "a teammate",
        ["coop.crewLine"] = "Crew: {0}.",
        ["coop.evenWithHelp"] = "Even with help, {0}",
        ["coop.nobodyHelping"] = "Nobody is helping the cart along.",

        // Recovery guidance (CT-014): panel chrome, titles, and steps.
        ["recovery.title"] = "Recovery Guidance",
        ["recovery.noDiagnosis"] = "No active diagnosis — guidance appears here when your cart is stuck.",
        ["recovery.crewNow"] = "Crew right now: {0}.",
        ["recovery.extraHands"] =
            "Extra hands will not beat this — the fix is less weight or a shallower line, not more pushing.",
        ["recovery.titleOverloaded"] = "Overloaded for this grade — the load must come down",
        ["recovery.titleMarginal"] = "Marginal load — a lighter cart makes this climb",
        ["recovery.titleSteep"] = "Steep, uncalibrated climb",
        ["recovery.titleObstruction"] = "Something is physically blocking the cart",
        ["recovery.titleUnclear"] = "Cause unclear — safe general steps",
        ["recovery.stepBrakeHold"] = "Detach, then hold the cart with the parking brake while you work.",
        ["recovery.stepRetryClimb"] = "Retry the climb straight uphill at a steady pace.",
        ["recovery.stepRouteAround"] =
            "If it still stalls, route around: a longer, shallower path beats a stuck cart.",
        ["recovery.stepBackDown"] = "Back the cart down to level ground first.",
        ["recovery.stepBrakeScout"] = "Use the parking brake to hold it while you scout.",
        ["recovery.stepShallowerLine"] =
            "Look for a shallower line — even a few degrees less grade helps more than pushing harder.",
        ["recovery.stepSwitchback"] = "Cut the slope diagonally (switchback) instead of attacking it straight on.",
        ["recovery.stepSecondTrip"] = "Unloading part of the cargo for a second trip is slower but certain.",
        ["recovery.stepCheckWheels"] =
            "Walk around the cart and check each wheel for rocks, stumps, or a terrain lip.",
        ["recovery.stepBackUpAngle"] = "Back up two or three meters and approach again at a slight angle.",
        ["recovery.stepHoe"] = "A hoe can level the offending lip — the vanilla tool is the intended fix.",
        ["recovery.stepWheelHole"] =
            "If a wheel dropped into a hole, pull backward out of it rather than forward through it.",
        ["recovery.stepReattach"] = "Detach and re-attach the cart to reset the pull joint.",
        ["recovery.stepDifferentLine"] = "Back up a few meters and try a slightly different line.",
        ["recovery.stepCheckCaught"] =
            "Check the wheels and the ground line for anything the cart could be caught on.",
        ["recovery.stepBrakeInvestigate"] = "On a slope, hold the cart with the parking brake while you investigate.",
        ["recovery.stepUnloadSome"] = "If nothing helps, unload some cargo — a lighter cart forgives more.",
        ["recovery.unloadNothingProven"] =
            "No load is proven to climb this grade yet — unload as much as you can carry, " +
            "or pick a shallower path.",
        ["recovery.unloadAtLeast"] =
            "Unload at least {0} weight (down to total mass {1}, the heaviest load a {2} row " +
            "proved at this grade).",
        ["recovery.unloadAlreadyUnder"] =
            "Your mass ({0}) is already at or under the proven {1} for this grade — the load is " +
            "probably not the blocker; check for obstructions.",
    };

    private static Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _missingReported = new(StringComparer.Ordinal);

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> Defaults => English;

    /// <summary>Sink for the once-only missing-English-key warning. The
    /// adapter layer points this at the plugin log; the domain stays pure
    /// (an unset or throwing reporter never affects string resolution).</summary>
    public static Action<string>? MissingKeyReporter { get; set; }

    public static bool HasKey(string key) => English.ContainsKey(key);

    public static void LoadOverrides(Dictionary<string, string> overrides)
    {
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Resolves a key: a valid override wins, else the English
    /// default, else the key text itself. A key with no English default is a
    /// programming error, not a translation gap — it is reported ONCE
    /// through <see cref="MissingKeyReporter"/> (wired to the plugin log by
    /// the adapter layer), and the key text renders meanwhile so the UI is
    /// never blank.</summary>
    public static string Get(string key, out bool known)
    {
        known = English.ContainsKey(key);
        // The once-only flag is consumed only while a sink exists, so an
        // unknown key resolved before the adapter wires the reporter still
        // gets its one log line afterwards instead of vanishing.
        if (!known && MissingKeyReporter is { } report && ShouldReportMissing(key))
        {
            try
            {
                report(key);
            }
            catch
            {
                // A faulty reporter must never break string resolution.
            }
        }

        if (_overrides.TryGetValue(key, out string? translated) && !string.IsNullOrEmpty(translated))
        {
            return translated;
        }

        return known ? English[key] : key;
    }

    public static string Get(string key) => Get(key, out _);

    /// <summary>Resolves and formats a key. A broken translation (bad
    /// placeholders) falls back to the English format; a NaN of arguments
    /// never crashes the UI.</summary>
    public static string Format(string key, params object[] arguments)
    {
        string template = Get(key, out _);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            return English.TryGetValue(key, out string? english)
                ? string.Format(CultureInfo.InvariantCulture, english, arguments)
                : key;
        }
    }

    /// <summary>Records that a key had no English default, returning true
    /// only the first time so the caller logs it exactly once.</summary>
    public static bool ShouldReportMissing(string key)
    {
        return _missingReported.Add(key);
    }

    /// <summary>The set of placeholder indices in a string (e.g. {0},{2} →
    /// {0,2}). Used to validate that a translation keeps the same slots.</summary>
    public static SortedSet<int> PlaceholderIndices(string value)
    {
        var indices = new SortedSet<int>();
        foreach (Match match in PlaceholderPattern.Matches(value ?? string.Empty))
        {
            if (int.TryParse(match.Groups[1].Value, out int index))
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    /// <summary>The translator template: header plus every key with its
    /// English text, tab-separated with newlines/tabs escaped.</summary>
    public static IEnumerable<string> TranslatorTemplate()
    {
        yield return TemplateHeader;
        foreach (KeyValuePair<string, string> entry in English)
        {
            yield return entry.Key + "\t" + Escape(entry.Value);
        }
    }

    /// <summary>Parses a translation file. A row is skipped (and counted)
    /// when it is malformed, names an unknown key, or its placeholders do not
    /// match the English default — a placeholder mismatch would format wrong
    /// or throw, so it never enters the override set.</summary>
    public static Dictionary<string, string> ParseOverrides(IEnumerable<string> lines, out int skippedRows)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        skippedRows = 0;
        foreach (string rawLine in lines ?? Array.Empty<string>())
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                skippedRows++;
                continue;
            }

            string key = line.Substring(0, tab);
            if (!English.TryGetValue(key, out string? english))
            {
                skippedRows++;
                continue;
            }

            string value = Unescape(line.Substring(tab + 1));
            if (!PlaceholderIndices(value).SetEquals(PlaceholderIndices(english)))
            {
                skippedRows++;
                continue;
            }

            overrides[key] = value;
        }

        return overrides;
    }

    // Percent-encoding (mirroring the invertible scheme proven in
    // Concerned Cartographer's AtlasText — reimplemented here, not
    // referenced, to keep the products independent). Single-pass and fully
    // round-trippable: unlike sequential \-escapes, a backslash before a
    // 't'/'n'/'r' cannot be mis-decoded because only "%HH" triples decode.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '%': builder.Append("%25"); break;
                case '\t': builder.Append("%09"); break;
                case '\n': builder.Append("%0A"); break;
                case '\r': builder.Append("%0D"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '%' && index + 2 < value.Length &&
                TryParseHex(value[index + 1], value[index + 2], out char decoded))
            {
                builder.Append(decoded);
                index += 2;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool TryParseHex(char high, char low, out char value)
    {
        value = '\0';
        if (!TryHexDigit(high, out int hi) || !TryHexDigit(low, out int lo))
        {
            return false;
        }

        value = (char)((hi << 4) | lo);
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        value = 0;
        return false;
    }
}
