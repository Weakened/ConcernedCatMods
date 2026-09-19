using System;
using System.Globalization;
using Jotunn.Entities;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.Settlement.Designations;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary><c>cs_steward</c>: everything a player does to the Steward.
///
/// <b>Every branch answers, and every answer says what to do next.</b> A
/// command that silently does nothing is indistinguishable from a broken mod,
/// and this one governs a worker who spends most of his time standing still on
/// purpose — so "why is nothing happening" has to be answerable from the
/// console, not from a log file.
///
/// Nothing here decides anything. Each verb hands the request to
/// <see cref="StewardRuntime"/>, which hands it to the game-free layer, which
/// refuses or does not. That is what lets every refusal be tested without the
/// game.</summary>
internal sealed class StewardCommand : ConsoleCommand
{
    private readonly StewardRuntime _runtime;

    internal StewardCommand(StewardRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => "cs_steward";

    public override string Help =>
        "The Steward: area <radius> | depot | clear area|depot | runes | recruit | dismiss | " +
        "resolve | status";

    public override void Run(string[] args)
    {
        string[] words = args ?? new string[0];
        string verb = words.Length == 0 ? "status" : words[0].ToLowerInvariant();

        try
        {
            Console.instance?.Print(Answer(verb, words));
        }
        catch (Exception exception)
        {
            // A console command must never be the thing that takes the game
            // down, and a caught failure the player can read is worth more than
            // a stack trace they cannot.
            Console.instance?.Print(
                "The Steward could not answer that: " + exception.GetType().Name + ": " +
                exception.Message);
        }
    }

    private string Answer(string verb, string[] args)
    {
        switch (verb)
        {
            case "area":
                return MarkArea(args);

            case "depot":
            case "supply":
                return _runtime.MarkSupplyDepot();

            case "clear":
                return Clear(args);

            case "runes":
            case "flint":
                return _runtime.ReadTheRunes();

            case "recruit":
            case "hire":
                return _runtime.Recruit();

            case "dismiss":
                return _runtime.Dismiss();

            case "resolve":
                return _runtime.Acknowledge();

            case "status":
                return _runtime.Status();

            default:
                return "Not a thing the Steward does. " + Help;
        }
    }

    private string MarkArea(string[] args)
    {
        if (args.Length < 2)
        {
            return "How big? \"cs_steward area <radius in metres>\". Between " +
                DesignationBook.MinRadius.ToString("0.#", CultureInfo.InvariantCulture) + " and " +
                DesignationBook.MaxRadius.ToString("0.#", CultureInfo.InvariantCulture) + ".";
        }

        if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
        {
            return "\"" + args[1] + "\" is not a number of metres.";
        }

        return _runtime.MarkSettlementArea(radius);
    }

    private string Clear(string[] args)
    {
        if (args.Length < 2)
        {
            return "Clear what? \"cs_steward clear area\" or \"cs_steward clear depot\".";
        }

        switch (args[1].ToLowerInvariant())
        {
            case "area":
            case "settlement":
                // Clearing the settlement clears the depot with it: a supply
                // chest belongs to a settlement, and one belonging to a
                // settlement that no longer exists is state nobody asked for.
                // He is NOT dismissed by it — letting somebody go is its own
                // explicit act.
                return _runtime.Undesignate(DesignationKind.SettlementArea);

            case "depot":
            case "supply":
                return _runtime.Undesignate(DesignationKind.SupplyContainer);

            default:
                return "Clear \"area\" or \"depot\".";
        }
    }

    /// <summary>What tab-completion offers. The verbs only: a radius is a
    /// number and a chest is whatever the player is looking at.</summary>
    public override System.Collections.Generic.List<string> CommandOptionList() =>
        new System.Collections.Generic.List<string>
        {
            "area", "depot", "clear", "runes", "recruit", "dismiss", "resolve", "status",
        };
}

/// <summary>A read-only report of what the Steward can see, for bug reports and
/// for the manual test pass.
///
/// Separate from <c>cs_steward status</c> because it is a different audience: a
/// player wants one paragraph about why nothing is happening, and a person
/// diagnosing a problem wants the scan. It places nothing, marks nothing and
/// changes nothing.</summary>
internal sealed class StewardFiresCommand : ConsoleCommand
{
    private readonly StewardRuntime _runtime;

    internal StewardFiresCommand(StewardRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => "cs_fires";

    public override string Help => "What the Steward sees: every fire in the settlement, and why.";

    public override void Run(string[] args)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            var scan = _runtime.Loop.LastScan;
            text.AppendLine("Fires the Steward last looked at: " +
                scan.Examined.ToString(CultureInfo.InvariantCulture) + " of " +
                scan.Offered.ToString(CultureInfo.InvariantCulture) +
                (scan.Truncated ? " (more than he looks at in one go)" : string.Empty));

            foreach (var verdict in scan.Considered)
            {
                text.AppendLine(
                    "  " + verdict.Status + "  " +
                    Domain.Upkeep.FuelMath.Describe(verdict.Target.Fuel, verdict.Target.MaxFuel) +
                    "  " + verdict.Target.FuelItemName + "  " + verdict.Target.Position);
            }

            if (scan.Examined == 0)
            {
                text.AppendLine("  (nothing — he may not have looked yet, or there is no scope)");
            }

            text.Append(StewardRole.DisplayNameFallbackCapitalised + ": " + _runtime.Loop.Explanation);
            Console.instance?.Print(text.ToString());
        }
        catch (Exception exception)
        {
            Console.instance?.Print(
                "The Steward could not answer that: " + exception.GetType().Name + ": " +
                exception.Message);
        }
    }
}
