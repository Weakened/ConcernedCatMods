using System;
using System.Collections.Generic;
using Jotunn.Entities;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_sync` console command: explicit sharing and
/// review-before-apply for the collaborative atlas. Nothing is ever applied
/// without a preview-capable, player-initiated apply.</summary>
internal sealed class SyncToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public SyncToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_sync";

    public override string Help =>
        "Concerned Cartographer atlas sharing. Subcommands: status, share, inbox, preview <author>, " +
        "apply <author> [mine|theirs], clear. Only pins/routes scoped table/server travel; deletions " +
        "propagate as tombstones and can never resurrect.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteSyncCommand(args);
        }
        catch (Exception exception)
        {
            // A backstop behind the guard inside CartographerRuntime, which
            // knows the subcommand. Scrubbed either way (#389): a filesystem
            // exception's raw message carries the path it failed on, and with it
            // this machine's user name, into text a player screenshots.
            output = ConsoleFailure.Describe("cc_sync", subcommand: "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status", "share", "inbox", "preview", "apply", "clear" };
    }
}
