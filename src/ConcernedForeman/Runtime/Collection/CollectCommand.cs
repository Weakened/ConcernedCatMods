using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>The <c>cf_collect</c> console command: the development surface for
/// Gate C's live evidence (#315). It grants nothing of its own: every order
/// goes through the loop's acceptance, and every act through the same authority
/// answer the worker uses. Console commands are development aids; the player
/// controls of COOP-04 are agent E's.</summary>
internal sealed class CollectCommand : ConsoleCommand
{
    private readonly CollectionRuntime _runtime;

    internal CollectCommand(CollectionRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => "cf_collect";

    public override string Help =>
        "Concerned Foreman collection (Thorstein). Subcommands: status, preview [radius], " +
        "start <stone> <wood> [hold] (look at the destination chest, or add hold), pause, resume, cancel. " +
        "Requires the settlement runtime enabled, single player or a host with nobody else connected.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.Execute(args);
        }
        catch (Exception exception)
        {
            output = "Collection command failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList() =>
        new List<string> { "status", "preview", "start", "pause", "resume", "cancel" };
}
