using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary><c>ct_haul</c>: the development surface Gate B's live evidence is
/// gathered through (#313). It grants nothing: every subcommand goes through the
/// same authority answer, lease rules and haul service the runtime uses, so a
/// refusal here is the refusal Gunnar would give. The player-facing controls are
/// agent E's; this is a development aid only.</summary>
internal sealed class HaulConsoleCommand : ConsoleCommand
{
    internal const string CommandName = "ct_haul";

    private readonly GunnarHaulingRuntime _runtime;

    internal HaulConsoleCommand(GunnarHaulingRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => CommandName;

    public override string Help =>
        "Concerned Teamster: Gunnar hauls an assigned cart (development aid). Subcommands: status, seam, spawn, " +
        "assign (the cart you point at), confirm, go <x> <z> [radius], stop, detach, release, retire, evidence on|off. " +
        "Needs Workers/GunnarHaulingEnabled, single player or a host with nobody else connected.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.Execute(args);
        }
        catch (Exception exception)
        {
            output = "ct_haul failed: " + exception.GetType().Name + ": " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList() =>
        new List<string> { "status", "seam", "spawn", "assign", "confirm", "go", "stop", "detach", "release", "retire", "evidence" };
}
