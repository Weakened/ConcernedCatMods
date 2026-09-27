using System;

namespace TheConcernedCat.Diagnostics;

/// <summary>What a console command or a log line says when something threw,
/// with nothing about this machine in it (#411).
///
/// <b>The defect this exists to end, three times over.</b> Every product wrote
/// `"<X> failed: " + exception.Message` somewhere. That names none of the
/// command's subcommands, so a bug report says only that something failed; and a
/// filesystem exception's message routinely carries the full path it failed on,
/// which is the machine's user name and the profile's location, printed into the
/// text a player pastes into an issue or a Discord thread. It fires in exactly the
/// situation where they are already asking for help. #367 fixed one command,
/// #389 fixed the other six in that product, and this is the same fix for the
/// other two products — as shared source, because a fourth copy was the
/// alternative.
///
/// <b>Why the type is here and not in a product.</b> It is pure text over a BCL
/// exception, with no Unity, BepInEx or Jötunn type and no product namespace, so
/// it belongs under <c>src/Shared/</c> by the same rule
/// <see cref="PathScrubber"/> does. Wording being identical across products is a
/// feature: a player who has learned what one of these replies means has learned
/// all of them.</summary>
/// <b>Internal, like every other shared-source type here.</b> This is compiled
/// into each consumer rather than referenced, so each assembly gets its own copy
/// and none of them publishes it. That is not a style choice: ConcernedNPC is a
/// LIBRARY whose public surface is pinned by `PublicSurfaceTests` and costs a
/// version bump to change, and a `public` type here would have silently become
/// part of that package's API the moment the library compiled this folder. The
/// test caught it; `internal` is the fix, and it matches `src/Shared/Workers`
/// and `src/Shared/Interop`.
internal static class SafeFailure
{
    /// <summary>The reply for a subcommand that threw: what was being attempted,
    /// the exception's type, its scrubbed message, and nothing else.</summary>
    /// <param name="command">The console command, such as <c>ct_haul</c>.</param>
    /// <param name="subcommand">What the player typed after it. Blank is
    /// tolerated and answered with the command alone — a failure report is not
    /// the place to add a second failure, and naming a subcommand the player did
    /// not type points their bug report at the wrong operation.</param>
    /// <param name="exception">What went wrong. Null is tolerated.</param>
    public static string Describe(string command, string subcommand, Exception? exception)
    {
        string what = string.IsNullOrWhiteSpace(subcommand)
            ? command
            : command + " " + subcommand;

        string why = Brief(exception);
        return string.IsNullOrEmpty(why)
            ? what + " could not finish."
            : what + " could not finish: " + why;
    }

    /// <summary>An exception's type and scrubbed message, for a log line or a
    /// reply built by hand. The type is the useful half and is kept; the message
    /// is the half that carries the path.</summary>
    public static string Brief(Exception? exception)
    {
        if (exception == null)
        {
            return "";
        }

        string message = PathScrubber.Scrub(exception.Message, keepFileName: false);
        return string.IsNullOrEmpty(message)
            ? exception.GetType().Name
            : exception.GetType().Name + ": " + message;
    }

    /// <summary>The full scrubbed description, stack trace included, for a log
    /// that a player may upload. <c>ToString()</c> carries the path AND the
    /// stack, which is why it is scrubbed rather than trusted.</summary>
    public static string Describe(Exception? exception) =>
        exception == null
            ? ""
            : PathScrubber.Scrub(exception.ToString(), keepFileName: false, maxLength: 8000);
}
