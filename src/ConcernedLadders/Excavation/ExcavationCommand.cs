using System;
using Jotunn.Entities;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary><c>cl_excavate</c>: the read-only console diagnostic for the excavation adapter (#487), registered only
/// when <c>[Excavation] Enabled</c> is on. Since slice 3 it cannot dig: the Iron Shovel's own press is the only way a
/// dig happens, because the issue forbids excavation driven by chat, menus or other UI, and the console is UI. The
/// slice-2 <c>dig</c> verb is gone (a lead default pending owner confirmation, EXCAVATION.md 7.4).
///
/// <c>plan</c> only reads and describes the rectangle the held shovel shows now, including what it would be charged. <c>status</c> says
/// whether the probe, the registration and the switch allow anything, and whether a dig is still unconfirmed.
///
/// Registered through Jötunn: Jötunn 2.30.2's CommandManager reflects for the 13-parameter
/// <c>Terminal.ConsoleCommand</c> constructor, which is the one the installed game (1.0.21) has, read from both
/// decompiled assemblies. (The Jötunn 2.29.2 mismatch that removed other products' commands, #307, does not apply to
/// this product's pin.) Not observed in game.
///
/// Jötunn keeps a registered command for the session, so the command never holds the runtime itself: it asks the
/// plugin for the current one on every use, and answers "unavailable" once an install fault has disposed it (#487
/// slice 4, the follow-up from slice 2's review).</summary>
internal sealed class ExcavationCommand : ConsoleCommand
{
    private readonly Func<ExcavationRuntime?> _current;

    internal ExcavationCommand(Func<ExcavationRuntime?> current) => _current = current ?? throw new ArgumentNullException(nameof(current));

    public override string Name => "cl_excavate";

    public override string Help => "Concerned Ladders excavation (experimental, read-only; dig with the Iron Shovel): plan | status";

    public override void Run(string[] args)
    {
        try
        {
            Print(Answer(args ?? new string[0]));
        }
        catch (Exception exception)
        {
            Print("Excavation could not answer that: " + SafeFailure.Brief(exception));
        }
    }

    /// <summary>Unity's own null test, which a destroyed console passes as non-null under ?. .</summary>
    internal static void Print(string text)
    {
        Console console = Console.instance;
        if (console != null) console.Print(text);
    }

    private string Answer(string[] args)
    {
        string verb = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        ExcavationRuntime? runtime = _current();
        if (runtime == null) return "Excavation is unavailable this session (it stopped during start-up; see the log). Nothing is changed.";
        if (verb == "status") return runtime.Status();
        if (verb == "dig") return "The console cannot dig. Equip the Iron Shovel: attack sets the corners, the wheel the depth, and attack digs.";
        if (verb != "plan") return Help;
        return runtime.PlanAtAim();
    }
}
