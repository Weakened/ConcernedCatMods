using System;
using TheConcernedCat.Diagnostics;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The <c>cf_build</c> console command: the Build Orders menu without
/// the menu.
///
/// <b>It grants nothing of its own.</b> Every word goes through the same
/// <see cref="BuildOrderRuntime"/> the panel uses, which goes through the same
/// <c>BuildOrderDesk</c> that decides everything - so a build order confirmed
/// from the console and one confirmed from the panel are the same order, priced
/// the same way, authorised by the same act. A command surface that could
/// authorise something the panel could not is a second authority, and #280 has
/// exactly one.</summary>
internal sealed class BuildCommand : ConsoleCommand
{
    private readonly BuildOrderRuntime _runtime;

    internal BuildCommand(BuildOrderRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override string Name => "cf_build";

    public override string Help =>
        "Concerned Foreman build orders (Thorstein). Subcommands: status, here (mark a shelter where " +
        "you are standing, facing the way you are facing), turn <degrees>, preview (what it costs, " +
        "from the game's own recipes), confirm (authorise it - nothing is placed before this), " +
        "cancel. Requires the settlement runtime enabled, single player or a host with nobody else " +
        "connected.";

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

    public override List<string> CommandOptionList() =>
        new List<string> { "status", "here", "turn", "preview", "confirm", "cancel" };
}
