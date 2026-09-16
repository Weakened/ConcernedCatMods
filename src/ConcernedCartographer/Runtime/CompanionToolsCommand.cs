using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The `cc_companion` console command.
///
/// It exists for three separate reasons, and all three matter:
///
/// <list type="bullet">
/// <item><b>A way out.</b> <c>toolsonly on</c> reaches the tools without ever
/// meeting Hulgi, from a surface that is available before any of this
/// product's own UI is. Nobody can be stuck behind a compass they cannot
/// find.</item>
/// <item><b>An honest status.</b> <c>status</c> prints what was actually
/// resolved — the quest stage, why access was granted, where the home point
/// came from, which prefab the visual used, and whether vanilla hovering has
/// ever reached the object. Several of those are the audit's open evidence
/// rows, answered by observation rather than assumption.</item>
/// <item><b>Replay.</b> Reading the introduction again, once it is
/// over.</item>
/// </list>
///
/// It is a diagnostic, a preference surface and - since <c>pose</c> - a way to
/// look at him in a shape his own lifecycle does not produce. It cannot advance
/// the quest, grant anything the policy would not grant on its own, or remove
/// access.</summary>
internal sealed class CompanionToolsCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public CompanionToolsCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_companion";

    public override string Help =>
        "Concerned Companions. Subcommands: status, toolsonly <on|off>, story (replay), " +
        "show <on|off> (companion visibility), where (home point), placement (why he sits " +
        "where he does, or why he is not placed), summon (sit him in front of you to watch " +
        "him find his way back), drink [toast|plain] (watch him have one now), doors [list|clear|all " +
        "<on|off>] (which doors companions may use), appearance (list this " +
        "build's hair/beard presets), pose <stand|ground|seat> (look at him in one), " +
        "path (data folder), reset [quest|bed|day|all] (replay: restart the introduction, " +
        "forget your bed spawn point, return the world to day 1; tools stay unlocked).";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteCompanionCommand(args);
        }
        catch (Exception exception)
        {
            output = "Companion tool failed: " + exception.Message;
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string>
            { "status", "toolsonly", "story", "show", "where", "appearance", "pose", "path" };
    }
}
