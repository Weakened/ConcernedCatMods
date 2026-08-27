using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_pins` console command: the scriptable surface of the
/// Pin Workbench. Every operation edits only the mod's own atlas and the
/// player's own or managed pins — foreign pins are read-only.</summary>
internal sealed class PinToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public PinToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_pins";

    public override string Help =>
        "Concerned Cartographer pin workbench. Subcommands: edit (opens the panel), status, list, adopt, adoptall, create, " +
        "name, icon, icons, category, color, size, note, tag+, tag-, setstatus, check, uncheck, scope, " +
        "move, dup, archive, unarchive, delete, restore, deleted, dups, merge, undo, redo, coords. " +
        "Most target the managed pin nearest you.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecutePinCommand(args);
        }
        catch (Exception exception)
        {
            output = "Pin tool failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string>
        {
            "edit", "status", "list", "adopt", "adoptall", "create", "name", "icon", "icons", "category",
            "color", "size", "note", "tag+", "tag-", "setstatus", "check", "uncheck", "scope",
            "move", "dup", "archive", "unarchive", "delete", "restore", "deleted", "dups",
            "merge", "undo", "redo", "coords",
        };
    }
}
