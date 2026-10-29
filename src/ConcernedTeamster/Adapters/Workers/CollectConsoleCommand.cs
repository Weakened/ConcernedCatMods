using System;
using TheConcernedCat.Diagnostics;
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
        "Concerned Teamster: Gunnar collects loose stones and fallen branches (development aid). " +
        "Subcommands: status, pick (the one you point at), destination (the chest you point at), deposit, " +
        "area [radius] (around where you stand), start (a tour of that area into that chest), stop, cancel, " +
        "chest. Needs Workers/GunnarCollectionEnabled, single player or a host with nobody else connected.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.Execute(args);
        }
        catch (Exception exception)
        {
            // #411: see HaulConsoleCommand. Same line, same leak.
            output = SafeFailure.Describe(
                CommandName, args != null && args.Length > 0 ? args[0] : "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList() =>
        new List<string> { "status", "pick", "destination", "deposit", "area", "start", "stop", "cancel", "chest" };
}
