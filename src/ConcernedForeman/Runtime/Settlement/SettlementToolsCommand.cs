using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>The `cf_settle` console command: the four explicit player acts.
///
/// Separate from `cf_worker` on purpose. `cf_worker` puts a body in the world
/// and gives it one instruction, which is what CF-SET-002 had to demonstrate.
/// This one is about what a player has <i>marked</i> and who they have hired,
/// which persists whether or not anybody is standing anywhere.</summary>
internal sealed class SettlementToolsCommand : ConsoleCommand
{
    private readonly SettlementRuntime _runtime;

    internal SettlementToolsCommand(SettlementRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cf_settle";

    public override string Help =>
        "Concerned Foreman settlement designation. Subcommands: status, area <radius>, " +
        "harvest <radius>, supply (look at a chest), recruit [name], dismiss [name], " +
        "clear <area|harvest|supply> [yes]. Requires the settlement runtime to be enabled " +
        "in the config and this peer to be the host.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteSettlement(args);
        }
        catch (Exception exception)
        {
            output = "Settlement tool failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string>
        {
            "status", "area", "harvest", "supply", "recruit", "dismiss", "clear",
        };
    }
}
