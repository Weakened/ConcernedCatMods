using System;
using System.Collections.Generic;
using Jotunn.Entities;

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
            output = "Survey tool failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status", "list", "accept", "reject", "reload", "path" };
    }
}
