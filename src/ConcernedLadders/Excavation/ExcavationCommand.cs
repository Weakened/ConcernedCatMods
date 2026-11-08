using System;
using Jotunn.Entities;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary><c>cl_excavate</c>: the temporary console harness for the excavation adapter (#487 slice 2), registered only
/// when <c>[Excavation] Enabled</c> is on. It exists so the terrain operation can be measured in game before the Iron
/// Shovel, its preview and its input exist (later slices); the shovel's deliberate action replaces it as the way a
/// player digs.
///
/// <c>plan small|large</c> only reads and describes. <c>dig small|large</c> runs the full commit gate. <c>status</c>
/// says whether the probe, the registration and the switch allow anything, and whether a dig is still unconfirmed.
///
/// Registered through Jötunn: Jötunn 2.30.2's CommandManager reflects for the 13-parameter
/// <c>Terminal.ConsoleCommand</c> constructor, which is the one the installed game (1.0.21) has, read from both
/// decompiled assemblies. (The Jötunn 2.29.2 mismatch that removed other products' commands, #307, does not apply to
/// this product's pin.) Not observed in game.</summary>
internal sealed class ExcavationCommand : ConsoleCommand
{
    private readonly ExcavationRuntime _runtime;

    internal ExcavationCommand(ExcavationRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public override string Name => "cl_excavate";

    public override string Help => "Concerned Ladders excavation (experimental): plan small|large | dig small|large | status";

    public override void Run(string[] args)
    {
        try
        {
            Console.instance?.Print(Answer(args ?? new string[0]));
        }
        catch (Exception exception)
        {
            Console.instance?.Print("Excavation could not answer that: " + SafeFailure.Brief(exception));
        }
    }

    private string Answer(string[] args)
    {
        string verb = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        if (verb == "status") return _runtime.Status();
        if (verb != "plan" && verb != "dig") return Help;
        if (args.Length < 2 || !TryMode(args[1], out ExcavationMode mode))
        {
            return "Say which size: " + verb + " small (" + ExcavationReport.ModeName(ExcavationMode.Small) + ") or " + verb +
                   " large (" + ExcavationReport.ModeName(ExcavationMode.Large) + ").";
        }
        return verb == "plan" ? _runtime.PlanAtAim(mode) : _runtime.DigAtAim(mode);
    }

    private static bool TryMode(string word, out ExcavationMode mode)
    {
        switch (word.ToLowerInvariant())
        {
            case "small": case "2": mode = ExcavationMode.Small; return true;
            case "large": case "4": mode = ExcavationMode.Large; return true;
            default: mode = ExcavationMode.Small; return false;
        }
    }
}
