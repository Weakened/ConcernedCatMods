using System;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Splitting a console command's arguments into a subcommand and the
/// rest, once, totally, and in the domain where it can be asserted.
///
/// <b>Why this is not two expressions in the command handler (#367).</b> Adding
/// a try/catch around the atlas switch moved a question that used to be
/// unasked: what the handler does with arguments that are not there. Left
/// inline, a null array became "could not finish: NullReferenceException" —
/// caught rather than crashing, which is better than before and still a bug
/// report from somebody whose console said something meaningless. Answering it
/// inside the guard would also have made the answer untestable, because the
/// handler needs BepInEx and compiles into no test assembly.
///
/// <b>An empty token is deliberately not the default subcommand.</b>
/// <c>cc_atlas ""</c> resolves to the empty string, reaches the switch's
/// <c>default</c>, and gets the usage reply — which is what it did before the
/// guard existed. Mapping it to <c>status</c> would have been a silent
/// behaviour change smuggled in by a refactor, and it would answer a typo with
/// a status line instead of telling the player their argument was not
/// understood.</summary>
internal static class ConsoleArguments
{
    /// <summary>What a command with no arguments at all means.</summary>
    public const string DefaultSubcommand = "status";

    /// <summary>The subcommand, lowercased for matching. No arguments means
    /// <see cref="DefaultSubcommand"/>; a blank or null first argument means the
    /// empty string, which no switch recognises and every switch's default
    /// answers.</summary>
    public static string Subcommand(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return DefaultSubcommand;
        }

        return args[0] is null ? "" : args[0].ToLowerInvariant();
    }

    /// <summary>Everything after the subcommand, space-joined, with the caller's
    /// original casing preserved — names, queries and patterns are the player's
    /// text, not tokens to match.</summary>
    public static string Remainder(string[]? args)
    {
        if (args is null || args.Length <= 1)
        {
            return "";
        }

        return string.Join(" ", args, 1, args.Length - 1);
    }
}
