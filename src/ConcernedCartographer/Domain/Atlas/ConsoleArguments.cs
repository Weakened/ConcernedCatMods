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

    /// <summary>The subcommand the player actually typed, lowercased, or the
    /// empty string when they typed none.
    ///
    /// <b>Why a failure report uses this and not <see cref="Subcommand"/>
    /// (#389).</b> `status` is <see cref="Subcommand"/>'s default for a bare
    /// command, and it is the right default for dispatch in six of the seven
    /// `cc_*` commands — but not in `cc_routes`, whose handler answers a bare
    /// invocation with `list`. So a bare `cc_routes` that threw was reported as
    /// `cc_routes status could not finish`, and `status` is a real and
    /// different `cc_routes` subcommand: the reply named an operation the
    /// player had not asked for and sent their bug report after it.
    ///
    /// Giving the guard a per-command default would fix that one case and keep
    /// the shape that caused it — one more pair of values to hold in step, in a
    /// reply nobody tests by reading. Naming what was typed removes the class:
    /// a failure report describes the player's input, which is something the
    /// guard can actually know. Typing nothing yields the empty string, and
    /// <c>ConsoleFailure.Describe</c> already answers that with the command
    /// alone.</summary>
    public static string Typed(string[]? args)
    {
        if (args is null || args.Length == 0 || args[0] is null)
        {
            return "";
        }

        return args[0].ToLowerInvariant();
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
