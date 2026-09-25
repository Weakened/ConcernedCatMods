using System;
using System.Collections.Generic;
using Jotunn.Entities;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_routes` console command: route planning tools. Draw and
/// erase happen on the large map with the configured modifier + LeftClick;
/// everything edits only the mod's own atlas.</summary>
internal sealed class RouteToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public RouteToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_routes";

    public override string Help =>
        "Concerned Cartographer routes. Subcommands: list, draw <name>, waypoint <name>, erase, stop, " +
        "snap on|off, measure, name, style, status, color, lock, unlock, archive, unarchive, delete, " +
        "restore, split, merge, undo, redo. Map modes use Modifier+LeftClick (default LeftShift).";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteRouteCommand(args);
        }
        catch (Exception exception)
        {
            // A backstop behind the guard inside CartographerRuntime, which
            // knows the subcommand. Scrubbed either way (#389): a filesystem
            // exception's raw message carries the path it failed on, and with it
            // this machine's user name, into text a player screenshots.
            output = ConsoleFailure.Describe("cc_routes", subcommand: "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string>
        {
            "list", "draw", "waypoint", "erase", "stop", "snap", "measure", "name", "style", "status",
            "color", "lock", "unlock", "archive", "unarchive", "delete", "restore", "split", "merge",
            "undo", "redo",
        };
    }
}
