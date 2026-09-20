using System;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>What a console command says when it could not finish.
///
/// <b>Why the wording is not left to the catch block (#367).</b> The console
/// wrappers each reported <c>exception.Message</c> verbatim, and a filesystem
/// exception's message routinely carries the full path it failed on — which
/// means the machine's user name and the profile's location. This product
/// scrubs that everywhere else, through <c>SafeLogText</c>, precisely so a
/// player can paste a log or a screenshot into a public bug report. The one
/// place it did not was the failure path of <c>cc_atlas support</c>: the
/// support command, whose whole job is producing something safe to share, and
/// whose failure text is the thing a player screenshots.
///
/// It also said only "Atlas tool failed", so a reply gave no hint which of
/// fourteen subcommands had failed.
///
/// Both are one line of wording, and wording is testable — which is the other
/// reason it is here rather than in a <c>catch</c> inside a class that needs
/// BepInEx.</summary>
internal static class ConsoleFailure
{
    /// <summary>The reply for a subcommand that threw: what was being done, the
    /// exception's type and scrubbed message, and nothing about this machine.
    /// </summary>
    /// <param name="command">The console command, such as <c>cc_atlas</c>.</param>
    /// <param name="subcommand">The subcommand attempted. Blank is tolerated —
    /// a failure report is not the place to add a second failure.</param>
    /// <param name="exception">What went wrong. Null is tolerated.</param>
    public static string Describe(string command, string subcommand, Exception exception)
    {
        string what = string.IsNullOrWhiteSpace(subcommand)
            ? command
            : command + " " + subcommand;

        string why = SafeLogText.Brief(exception);
        return string.IsNullOrEmpty(why)
            ? what + " could not finish."
            : what + " could not finish: " + why;
    }
}
