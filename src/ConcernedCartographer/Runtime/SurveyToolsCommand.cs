using System;
using System.Collections.Generic;
using Jotunn.Entities;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_survey` console command: review-before-commit for the
/// opt-in survey rules. Observations never become pins without an explicit
/// accept.</summary>
internal sealed class SurveyToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public SurveyToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_survey";

    public override string Help =>
        "Concerned Cartographer survey review. Subcommands: status, list, accept <n|all>, " +
        "reject <n|all>, reload, path. Enable via Survey/SurveyRulesEnabled; rules live in survey-rules.tsv.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteSurveyCommand(args);
        }
        catch (Exception exception)
        {
            // A backstop behind the guard inside CartographerRuntime, which
            // knows the subcommand. Scrubbed either way (#389): a filesystem
            // exception's raw message carries the path it failed on, and with it
            // this machine's user name, into text a player screenshots.
            output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status", "list", "accept", "reject", "reload", "path" };
    }
}
