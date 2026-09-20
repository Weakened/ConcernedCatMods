using System;
using System.Collections.Generic;
using Jotunn.Entities;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_atlas` console command: the scriptable Atlas Drawer —
/// display filters, layer toggles, and saved views. Filters are
/// display-only and can never cause data loss.</summary>
internal sealed class AtlasToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public AtlasToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_atlas";

    public override string Help =>
        "Concerned Cartographer atlas drawer and maintenance. Subcommands: status, query <text>, clear, " +
        "pins on|off, cluster on|off, dirt on|off, paved on|off, view save|apply|del <name>, views, " +
        "compat, backup, backups, restore <n>, support. Drawer panel: DrawerHotkey (default L) on the large map.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteAtlasCommand(args);
        }
        catch (Exception exception)
        {
            // A backstop behind the guard inside ExecuteAtlasCommand, which
            // knows the subcommand. Scrubbed either way (#367): a filesystem
            // exception's raw message carries the path it failed on, and with it
            // this machine's user name, into text a player screenshots.
            output = ConsoleFailure.Describe("cc_atlas", subcommand: "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string>
        {
            "status", "query", "clear", "pins", "cluster", "dirt", "paved", "view", "views",
            "compat", "backup", "backups", "restore", "support",
        };
    }
}
