using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary><c>ct_collect</c>: the surface the owner's in-game evidence for the
/// collection carve-out (#381) is gathered through, the way <c>ct_haul</c> is for
/// #313. It grants nothing: every subcommand goes through the same off-by-default
/// switch, the same work-authority answer and the same port the runtime uses, so
/// a refusal here is the refusal Gunnar would give.</summary>
internal sealed class CollectConsoleCommand : ConsoleCommand
{
    internal const string CommandName = "ct_collect";

    private readonly GunnarCollectionRuntime _runtime;

    internal CollectConsoleCommand(GunnarCollectionRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => CommandName;

    public override string Help =>
        "Concerned Teamster: Gunnar picks up one loose stone or fallen branch you point at " +
        "(development aid). Subcommands: status, pick, cancel. Needs Workers/GunnarCollectionEnabled, " +
        "single player or a host with nobody else connected, and Gunnar standing next to the thing.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.Execute(args);
        }
        catch (Exception exception)
        {
            output = "ct_collect failed: " + exception.GetType().Name + ": " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList() =>
        new List<string> { "status", "pick", "cancel" };
}
