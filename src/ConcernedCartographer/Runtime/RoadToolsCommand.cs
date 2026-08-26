using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_roads` console command: proximity-based road correction
/// tools. All operations act on the mod's own atlas only — they can never
/// modify Valheim terrain or world saves.</summary>
internal sealed class RoadToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public RoadToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_roads";

    public override string Help =>
        "Concerned Cartographer road tools. Subcommands: status, delete, kind, hide, unhide, " +
        "split, join, rebuild, undo. Each targets the recorded road nearest you; an optional " +
        "number sets the search radius in meters (e.g. 'cc_roads delete 20').";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteRoadCommand(args);
        }
        catch (Exception exception)
        {
            output = "Road tool failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status", "delete", "kind", "hide", "unhide", "split", "join", "rebuild", "undo" };
    }
}
