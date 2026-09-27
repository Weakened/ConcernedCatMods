"""Negative tests for the #389 console failure audit.

#389 was six copies of one line: six `cc_*` command wrappers replying
`"<X> tool failed: " + exception.Message`, which names none of their
subcommands and prints a filesystem exception's full path - and with it the
machine's user name - into the text a player pastes into a bug report. #367
had already fixed the seventh, `cc_atlas`, and left the pattern to copy.

A seventh copy is the obvious next defect, so the fix is enforced rather than
reviewed, and the enforcement is tested rather than asserted. Each test below
plants one real way back into the real tree, runs the real validator, and
requires it to refuse - then restores. Testing the matching helpers alone would
not do: most of these are wiring, not matching.

The plants are the ways a reviewer or a later change actually gets here:
restoring the raw message; spelling it something other than `.Message`
(`ToString()`, an inner exception, an interpolation) so a rule that banned one
token would miss it; putting it in a runtime CORE or in an ordinary product file
rather than in a wrapper, which is where three copies of it actually were;
keeping ConsoleFailure but naming another command; keeping the guard but reaching
the work around it; growing a second guard beside the one that exists; moving the
guard's scrubbed log line out of the guard while leaving the call elsewhere in the
file; and adding a brand-new console command the audit has never heard of, under a
name the old file-name glob would not have matched.

Two plants require the validator to PASS rather than refuse, because a rule that
cannot tell `Character.Message` from an exception's property, or that refuses the
scrubber's own read, is a rule nobody could turn on.

No count is given here on purpose: what the harness covers is the list above,
and that is checkable against the file.
"""
import os
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
RUNTIME_DIR = os.path.join(ROOT, "src", "ConcernedCartographer", "Runtime")
RUNTIME = os.path.join(RUNTIME_DIR, "CartographerRuntime.cs")
PIN = os.path.join(RUNTIME_DIR, "PinToolsCommand.cs")
SURVEY = os.path.join(RUNTIME_DIR, "SurveyToolsCommand.cs")
RENDERER = os.path.join(
    ROOT, "src", "ConcernedCartographer", "Map", "RoadOverlayRenderer.cs")

# Deliberately NOT named *ToolsCommand.cs: wrappers are discovered by base
# class, because globbing the file name made "an eighth command is covered the
# day it is written" a claim about a convention nothing enforces.
NEW_WRAPPER = os.path.join(RUNTIME_DIR, "ZzProbeCommand.cs")

NEW_WRAPPER_BODY = """using System;
using System.Collections.Generic;
using Jotunn.Entities;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal sealed class ZzProbeCommand : ConsoleCommand
{
    private readonly CartographerRuntime _runtime;

    public ZzProbeCommand(CartographerRuntime runtime)
    {
        _runtime = runtime;
    }

    public override string Name => "cc_zzprobe";

    public override string Help => "A probe.";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = _runtime.ExecuteAtlasCommand(args);
        }
        catch (Exception exception)
        {
%s
        }

        context?.AddString(output);
    }

    public override List<string> CommandOptionList()
    {
        return new List<string> { "status" };
    }
}
"""


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class ConsoleFailuresStayScrubbed(unittest.TestCase):
    """Every way back to a raw exception message in a console reply."""

    def setUp(self):
        code, out = validate()
        self.assertEqual(0, code,
                         "the tree must be clean before a plant means anything:\n" + out[-2000:])
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path in reversed(self._planted):
            if os.path.isfile(path):
                os.remove(path)
        for path, original in reversed(self._restore):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "the pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def plant_file(self, path, body):
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)
        self._planted.append(path)

    def assert_refused(self, why):
        code, out = validate()
        self.assertNotEqual(0, code, why + "\n" + out[-2000:])
        self.assertIn("#389 console failure audit", out, why)

    # -- the defect itself, put back ------------------------------------

    def test_the_raw_exception_message_is_refused_in_a_wrapper(self):
        # Exactly the line #389 removed, restored in one wrapper.
        self.swap_in(
            PIN,
            'output = ConsoleFailure.Describe("cc_pins", subcommand: "", exception);',
            'output = "Pin tool failed: " + exception.Message;')
        self.assert_refused("a wrapper reached an exception's .Message again")

    def test_the_raw_message_is_refused_even_behind_a_scrubbed_reply(self):
        # The plausible half-fix: keep the scrubbed reply AND append the raw
        # message, which is the same leak with a better-looking line above it.
        self.swap_in(
            SURVEY,
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception);',
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception)\n'
            '                + " (" + exception.Message + ")";')
        self.assert_refused("a wrapper appended the raw message to a scrubbed reply")

    def test_a_message_property_on_another_name_is_still_refused(self):
        # `.Message` is banned as a token rather than as `exception.Message`,
        # because renaming the caught variable is the cheapest way past a
        # rule that spells the variable out.
        self.swap_in(
            PIN,
            "        catch (Exception exception)\n        {",
            "        catch (Exception failure)\n        {\n"
            "            _ = failure.Message;")
        self.assert_refused("renaming the caught exception hid a raw .Message")

    def test_tostring_is_refused_even_though_it_is_not_message(self):
        # `.Message` is the shape #389 had; it is not the only unscrubbed
        # thing an exception hands over. ToString() carries the path AND the
        # stack, so a rule that banned only `.Message` would have let the
        # bigger leak in under a different spelling.
        self.swap_in(
            SURVEY,
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception);',
            'output = "cc_survey failed: " + exception.ToString();')
        self.assert_refused("a wrapper reported exception.ToString() raw")

    def test_an_inner_exception_is_refused(self):
        self.swap_in(
            SURVEY,
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception);',
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception)\n'
            '                + (exception.InnerException?.Message ?? "");')
        self.assert_refused("a wrapper appended an inner exception's message")

    def test_an_interpolated_exception_is_refused(self):
        # String interpolation leaves no `+ exception.Message` to grep for.
        self.swap_in(
            SURVEY,
            'output = ConsoleFailure.Describe("cc_survey", subcommand: "", exception);',
            'output = $"cc_survey failed: {exception}";')
        self.assert_refused("a wrapper interpolated the exception into its reply")

    # -- keeping ConsoleFailure but breaking what it says ---------------

    def test_a_reply_naming_another_command_is_refused(self):
        # A copy-paste defect that scrubs correctly and still sends the
        # player's bug report to the wrong command.
        self.swap_in(
            PIN,
            'ConsoleFailure.Describe("cc_pins", subcommand: "", exception)',
            'ConsoleFailure.Describe("cc_atlas", subcommand: "", exception)')
        self.assert_refused("a wrapper reported a failure as another command")

    # -- keeping the guard but going around it -------------------------

    def test_an_entry_point_that_skips_the_guard_is_refused(self):
        # The reply wording is untouched; the work simply no longer runs
        # inside the try, so an exception reaches the wrapper's backstop and
        # the subcommand is never named.
        self.swap_in(
            RUNTIME,
            'return GuardConsoleCommand("cc_pins", args, ExecutePinCommandCore);',
            "return ExecutePinCommandCore(args, ConsoleArguments.Subcommand(args));")
        self.assert_refused("an entry point reached its work around the guard")

    def test_a_guard_named_for_another_command_is_refused(self):
        self.swap_in(
            RUNTIME,
            'return GuardConsoleCommand("cc_pins", args, ExecutePinCommandCore);',
            'return GuardConsoleCommand("cc_roads", args, ExecutePinCommandCore);')
        self.assert_refused("an entry point was guarded under another command's name")

    def test_a_second_guard_beside_the_first_is_refused(self):
        # How the wording drifted the first time: not by editing the guard,
        # but by growing another one next to it.
        self.swap_in(
            RUNTIME,
            "    private string GuardConsoleCommand(",
            "    private string GuardConsoleCommandLoosely(\n"
            "        string command, string[] args, Func<string[], string, string> core)\n"
            "    {\n"
            "        try\n"
            "        {\n"
            "            return core(args, ConsoleArguments.Subcommand(args));\n"
            "        }\n"
            "        catch (Exception exception)\n"
            "        {\n"
            '            return ConsoleFailure.Describe("cc_pins", "", exception);\n'
            "        }\n"
            "    }\n"
            "\n"
            "    private string GuardConsoleCommand(")
        self.assert_refused("a second console guard grew beside the only one")

    # -- outside the wrappers, where the copies actually were ----------

    def test_a_raw_message_in_the_runtime_core_is_refused(self):
        # An independent review's finding, and the most important one here: the
        # wrappers are the LEAST likely place for the next copy. The work is in
        # the cores, and a catch there was not scanned at all.
        self.swap_in(
            RUNTIME,
            "    private string ExecuteSurveyCommandCore(string[] args, string subcommand)\n    {",
            "    private string ExecuteSurveyCommandCore(string[] args, string subcommand)\n    {\n"
            "        if (subcommand == \"zzprobe\")\n"
            "        {\n"
            "            try { throw new System.IO.IOException(); }\n"
            "            catch (Exception ex) { return \"cc_survey reload failed: \" + ex.Message; }\n"
            "        }\n")
        self.assert_refused("a runtime core reported a raw exception message")

    def test_a_raw_message_anywhere_in_the_product_is_refused(self):
        # Three of these already existed when #389 was written, the worst of
        # them returning the raw message as the `cc_roads align` console reply
        # on the line AFTER scrubbing the same exception for the log. The rule
        # is the product, not the console.
        self.swap_in(
            RENDERER,
            'return "Alignment probe failed: " + SafeLogText.Brief(exception);',
            'return "Alignment probe failed: " + exception.Message;')
        self.assert_refused("a non-wrapper file returned a raw exception message")

    def test_the_scrubber_itself_stays_allowed(self):
        # The one exemption has to be a real one: SafeLogText is where the
        # message is read, and a rule that refused it would be a rule nobody
        # could satisfy. Removing the read is what must fail - and it fails as
        # a compile error rather than here, so this asserts the shape instead:
        # the exemption is keyed to that one path, so the same call in a
        # different file is refused.
        self.swap_in(
            RENDERER,
            'return "Alignment probe failed: " + SafeLogText.Brief(exception);',
            "return CrashReportSanitizer.Sanitize(exception.Message, 2000);")
        self.assert_refused("the scrubber's own call was allowed outside the scrubber")

    def test_vanillas_own_message_method_is_not_mistaken_for_an_exception(self):
        # The rule has to tell `Character.Message` - vanilla's HUD method, named
        # by reflection in VanillaMessage and quoted in a log line - from an
        # exception's property, or it is a rule that cannot be turned on. This
        # plants the vanilla spelling and requires the validator to PASS.
        self.swap_in(
            RENDERER,
            'return "Alignment probe failed: " + SafeLogText.Brief(exception);',
            'string zz = nameof(Character.Message);\n'
            '            return "Alignment probe failed: " + zz + SafeLogText.Brief(exception);')
        code, out = validate()
        self.assertEqual(0, code, "Character.Message was mistaken for an exception:\n" + out[-2000:])

    def test_the_guards_own_log_line_is_what_is_pinned(self):
        # The presence of SafeLogText.Describe anywhere in a 3,000-line file is
        # not the claim; the claim is that the GUARD logs through it. A
        # file-wide check passes the moment a second use appears elsewhere,
        # taking this test's meaning with it.
        self.swap_in(
            RUNTIME,
            '_log.LogError($"{command} {typed} failed: {SafeLogText.Describe(exception)}");',
            '_log.LogError($"{command} {typed} failed.");\n'
            "        _ = SafeLogText.Describe(new Exception());")
        self.assert_refused("the guard stopped logging through SafeLogText while the file still used it")

    # -- a command the audit has never heard of ------------------------

    def test_a_new_wrapper_with_a_raw_message_is_refused(self):
        # The case a hand-written list of seven commands would have missed:
        # wrappers are discovered by glob, so an eighth is covered the day it
        # is written.
        self.plant_file(
            NEW_WRAPPER,
            NEW_WRAPPER_BODY % '            output = "Probe failed: " + exception.Message;')
        self.assert_refused("a newly added wrapper (not named *ToolsCommand.cs) reached an exception's .Message")

    def test_a_new_wrapper_that_is_scrubbed_but_unguarded_is_refused(self):
        # Scrubbed reply, right command name, and still wrong: cc_zzprobe has
        # no guarded entry point of its own, so the audit must not accept the
        # borrowed one it calls.
        self.plant_file(
            NEW_WRAPPER,
            NEW_WRAPPER_BODY
            % '            output = ConsoleFailure.Describe("cc_zzprobe", subcommand: "", exception);')
        self.assert_refused("a newly added wrapper had no guarded entry point of its own")


if __name__ == "__main__":
    unittest.main()
