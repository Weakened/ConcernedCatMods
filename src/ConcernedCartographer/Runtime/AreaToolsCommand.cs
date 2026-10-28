using System;
using System.Collections.Generic;
using Jotunn.Entities;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_areas` console command (#296): turn a closed route you
/// drew into a named work area, list, rename and delete them. A development
/// aid until the map editor lands; it edits only this mod's own per-world
/// file and never starts any work.</summary>
internal sealed class AreaToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public AreaToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_areas";

    public override string Help =>
        "Concerned Cartographer work areas (#296). Subcommands: list, fromroute <route> as <name>, " +
        "rename <area> to <name>, delete <area>. Draw the boundary first as a closed route (cc_routes draw).";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteAreaCommand(args);
        }
        catch (Exception exception)
        {
            // A backstop behind the guard inside CartographerRuntime, which
            // knows the subcommand. Scrubbed either way (#389): a filesystem
            // exception's raw message carries the path it failed on, and with it
            // this machine's user name, into text a player screenshots.
            output = ConsoleFailure.Describe("cc_areas", subcommand: "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "list", "fromroute", "rename", "delete" };
    }
}
