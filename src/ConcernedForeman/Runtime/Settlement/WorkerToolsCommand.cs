using System;
using TheConcernedCat.Diagnostics;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>The `cf_worker` console command: the surface CF-SET-002's live
/// evidence is gathered through.
///
/// Two of the leaf's acceptance criteria can only be settled by watching —
/// "the worker walks to a point and stops" and "with no order, it does nothing
/// at all". Neither is observable without a way to make a worker exist and give
/// it exactly one instruction, so this command is part of the deliverable
/// rather than a convenience.
///
/// It grants nothing. Every subcommand goes through the same authority answer
/// the runtime uses on every tick, so a refusal here is the same refusal a
/// worker would make.</summary>
internal sealed class WorkerToolsCommand : ConsoleCommand
{
    private readonly SettlementRuntime _runtime;

    internal WorkerToolsCommand(SettlementRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cf_worker";

    public override string Help =>
        "Concerned Foreman settlement worker spike. Subcommands: status, spawn, " +
        "goto <x> <z>, stop, despawn. Requires the settlement runtime to be " +
        "enabled in the config and this peer to be the host.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.Execute(args);
        }
        catch (Exception exception)
        {
            // #411: a filesystem exception's raw message is a path and this
            // machine's user name, in the text a player pastes into a bug
            // report. SafeFailure keeps the type, which is the useful half.
            output = SafeFailure.Describe(
                Name, args != null && args.Length > 0 ? args[0] : "", exception);
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status", "spawn", "goto", "stop", "despawn" };
    }
}
