"""Destructive mutation tests for Concerned Steward's S1 retire audit
(owner decision 2026-10-28, docs/mods/concerned-steward/DECISIONS.md S1).

Each test changes the real source tree, runs the real repository validator, and
restores the exact original text. Every plant is a way Sunniva's body (or any
networked body) could leave the world outside the one guarded verb, and each was
verified to pass the validator that lacked this rule (origin/main d72b397)
before being added here. The plants found by successive reviews were also each
verified to pass the validator of the head they were found against (d0d559e:
five, e4f0392: four, 203d449: three, 2103c58: four, 75d837d: two, 80c3f3a:
four, 8144fbd: three, 3b23929: one, 3252bcd: one), whose rule they got past; the last
eleven were checked against origin/main 0171cf6 (still without this rule) instead of d72b397, whose own rules no longer
pass the merged tree. Setting STEWARD_PLANT_VALIDATOR to another validator's path
re-runs that check, expecting every plant to get through it.
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.environ.get("STEWARD_PLANT_VALIDATOR") or os.path.join(ROOT, "tools", "validate_repo.py")
EXPECT_REFUSAL = not os.environ.get("STEWARD_PLANT_VALIDATOR")
STEWARD = os.path.join(ROOT, "src", "ConcernedSteward")
PORT = os.path.join(STEWARD, "Runtime", "StewardRetirementPort.cs")
RUNTIME = os.path.join(STEWARD, "Runtime", "StewardRuntime.cs")
COMMAND = os.path.join(STEWARD, "Runtime", "StewardCommand.cs")
DOMAIN = os.path.join(STEWARD, "Domain", "Retirement", "StewardRetirement.cs")
DECIDE_OPEN = "    public static StewardRetireVerdict Decide(in StewardRetireFacts facts)\n    {\n"
ALLOWS = ("    public static bool Allows(StewardRetireVerdict verdict) => "
          "verdict == StewardRetireVerdict.MayRetire;\n")
LABEL = "[steward] S1 retire audit"

PORT_GUARD = """        StewardRetireVerdict verdict = StewardRetirement.Decide(facts);
        if (!StewardRetirement.Allows(verdict))
        {
            answer = StewardRetirement.Describe(verdict, held);
            return StewardRetireOutcome.Refused;
        }
"""
PORT_REMOVAL = "            view.Destroy();\n"
PORT_DECIDE = "        StewardRetireVerdict verdict = StewardRetirement.Decide(facts);\n"
PORT_RETIRED = "        return StewardRetireOutcome.Retired;\n"
PORT_ALLOWS = "        if (!StewardRetirement.Allows(verdict))\n"
PORT_VIEW_CHECK = "        if (view == null || !view.IsValid() || !view.IsOwner())\n"
PORT_SUCCESS ="        answer = \"Her worker body has left"
LOOKED_UP ="ZNetScene.instance.FindInstance(ZDOID.None)!.GetComponent<ZNetView>()"
PORT_VIEW = "        ZNetView? view = body.View;\n"
RUNTIME_MARKER = "            _census = new StewardCensus(StewardCensusVerdict.Unknown, 0, 0, null);\n            _log(answer);\n"


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class StewardRetireIsTheOnlyRemoval(unittest.TestCase):
    def setUp(self):
        if EXPECT_REFUSAL:
            code, out = validate()
            self.assertEqual(0, code, "the tree must be clean before a plant means anything:\n" + out[-3000:])
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path, original in reversed(self._restore):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        for path in reversed(self._planted):
            if os.path.isfile(path):
                os.remove(path)
            elif os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
        if EXPECT_REFUSAL:
            code, out = validate()
            self.assertEqual(0, code, "an S1 plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        if all(path != seen for seen, _ in self._restore):
            self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def assert_refused(self, why):
        code, out = validate()
        if EXPECT_REFUSAL:
            self.assertNotEqual(0, code, why + "\n" + out[-3000:])
            self.assertIn(LABEL, out, why)
        else:
            # The validator without the rule: the plant must get through, or it
            # is not an escape this rule is the one to catch.
            self.assertEqual(0, code, "already refused without the S1 rule: " + why + "\n" + out[-3000:])

    def test_a_second_removal_in_the_runtime_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            _census.Live?.View?.Destroy();\n")
        self.assert_refused("the runtime removed her body without the port")

    def test_lifting_the_removal_into_a_helper_is_refused(self):
        self.swap_in(PORT, PORT_REMOVAL, "            Remove(view);\n")
        self.swap_in(PORT, "internal static class StewardRetirementPort\n{\n",
                     "internal static class StewardRetirementPort\n{\n"
                     "    private static void Remove(ZNetView view) => view.Destroy();\n\n")
        self.assert_refused("the removal moved into a helper the guard never sees")

    def test_consulting_the_verdict_without_refusing_is_refused(self):
        self.swap_in(PORT, "        if (!StewardRetirement.Allows(verdict))\n",
                     "        bool allowed = StewardRetirement.Allows(verdict);\n        if (false)\n")
        self.assert_refused("the guard was consulted and ignored")

    def test_deleting_the_decision_is_refused(self):
        self.swap_in(PORT, "        StewardRetireVerdict verdict = StewardRetirement.Decide(facts);\n",
                     "        StewardRetireVerdict verdict = StewardRetireVerdict.MayRetire;\n")
        self.assert_refused("the body was removed without asking what it holds")

    def test_removing_through_the_scene_with_an_argument_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            ZNetScene.instance.Destroy(_census.Live!.gameObject);\n")
        self.assert_refused("ZNetScene.Destroy(go) took a body out of the world")

    def test_a_receiver_split_across_lines_is_still_routed(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            ZNetScene.instance.\n                Destroy(_census.Live!.gameObject);\n")
        self.assert_refused("a newline between receiver and member hid a routed destruction")

    def test_unity_destroying_her_game_object_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            UnityEngine.Object.Destroy(_census.Live!.gameObject);\n")
        self.assert_refused("Unity's static destroyed her body's object outside the port")

    def test_destroying_the_zdo_directly_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            ZDOMan.instance.DestroyZDO(_census.Live!.View!.GetZDO());\n")
        self.assert_refused("the body's ZDO was destroyed directly")

    def test_a_second_caller_of_the_port_is_refused(self):
        self.swap_in(COMMAND, "            case \"retire\":\n",
                     "            case \"retire-now\":\n"
                     "                StewardRetirementPort.Retire(null, default, out string ignored);\n"
                     "                return ignored;\n\n"
                     "            case \"retire\":\n")
        self.assert_refused("a second caller reached the port without the runtime's witnesses")

    def test_removing_an_object_that_is_not_her_body_is_refused(self):
        self.swap_in(PORT, PORT_VIEW,
                     "        ZNetView? view = ZNetScene.instance.FindInstance(ZDOID.None)?.GetComponent<ZNetView>();\n")
        self.assert_refused("the removal acted on a looked-up object rather than her body")

    def test_moving_the_port_into_a_subfolder_is_refused(self):
        with open(PORT, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore.append((PORT, original))
        moved_dir = os.path.join(STEWARD, "Runtime", "Moved")
        os.makedirs(moved_dir)
        self._planted.append(moved_dir)
        moved = os.path.join(moved_dir, "StewardRetirementPort.cs")
        with open(moved, "w", encoding="utf-8", newline="") as handle:
            handle.write(original)
        self._planted.insert(0, moved)
        os.remove(PORT)
        self.assert_refused("the allowance followed a basename into another folder")

    # The five below each kept every pin of d0d559e's rule green: the view and
    # body swaps and the two using spellings came out of the review of that
    # head, the method group out of fixing them. Each was verified to pass that
    # head's validator as well as d72b397's.

    def test_reassigning_the_view_before_the_removal_is_refused(self):
        self.swap_in(PORT, PORT_REMOVAL,
                     "            view = ZNetScene.instance.FindInstance(ZDOID.None)?.GetComponent<ZNetView>();\n"
                     + PORT_REMOVAL)
        self.assert_refused("the declaration was hers but the removal hit a looked-up object")

    def test_swapping_the_body_parameter_is_refused(self):
        self.swap_in(PORT, PORT_VIEW,
                     "        body = StewardBody.FindLive(\"someone-else\") ?? body;\n" + PORT_VIEW)
        self.assert_refused("the guard read one body and the removal acted on another")

    def test_a_second_caller_through_using_static_is_refused(self):
        self.swap_in(COMMAND, "using System;\n",
                     "using System;\nusing static TheConcernedCat.ConcernedSteward.Runtime.StewardRetirementPort;\n")
        self.swap_in(COMMAND, "            case \"retire\":\n",
                     "            case \"retire-now\":\n"
                     "                Retire(null, default, out string ignored);\n"
                     "                return ignored;\n\n"
                     "            case \"retire\":\n")
        self.assert_refused("a bare Retire( under using static is a caller the pin cannot count")

    def test_a_second_caller_through_a_using_alias_is_refused(self):
        self.swap_in(COMMAND, "using System;\n",
                     "using System;\nusing Port = TheConcernedCat.ConcernedSteward.Runtime.StewardRetirementPort;\n")
        self.swap_in(COMMAND, "            case \"retire\":\n",
                     "            case \"retire-now\":\n"
                     "                Port.Retire(null, default, out string ignored);\n"
                     "                return ignored;\n\n"
                     "            case \"retire\":\n")
        self.assert_refused("an aliased Port.Retire( is a caller the pin cannot count")

    def test_a_second_caller_through_a_method_group_is_refused(self):
        self.swap_in(COMMAND, "            case \"retire\":\n",
                     "            case \"retire-now\":\n"
                     "                RetireVerb verb = StewardRetirementPort.Retire;\n"
                     "                return verb(null, default, out string ignored).ToString();\n\n"
                     "            case \"retire\":\n")
        self.assert_refused("a method group handed to a delegate is a caller with no parenthesis")

    # The four below each kept every pin of e4f0392's rule green (review of that
    # head): its `view` pins were positional and blind to scope. Each was
    # verified to pass e4f0392's validator as well as d72b397's.

    def test_a_local_function_shadowing_view_called_before_the_guard_is_refused(self):
        self.swap_in(PORT, PORT_DECIDE, "        Erase(" + LOOKED_UP + ");\n" + PORT_DECIDE)
        self.swap_in(PORT, PORT_REMOVAL, "            Erase(view);\n")
        self.swap_in(PORT, PORT_RETIRED,
                     PORT_RETIRED + "\n        static void Erase(ZNetView view) => view.Destroy();\n")
        self.assert_refused("a local function's own `view` removed a looked-up object before the guard")

    def test_a_block_bodied_local_function_shadowing_view_is_refused(self):
        self.swap_in(PORT, PORT_DECIDE, "        Erase(" + LOOKED_UP + ");\n" + PORT_DECIDE)
        self.swap_in(PORT, PORT_REMOVAL, "            Erase(view);\n")
        self.swap_in(PORT, PORT_RETIRED,
                     PORT_RETIRED + "\n        static void Erase(ZNetView view)\n        {\n"
                     "            view.Destroy();\n        }\n")
        self.assert_refused("the same escape with a block body and no `=>`")

    def test_a_lambda_whose_parameter_is_view_is_refused(self):
        self.swap_in(PORT, PORT_REMOVAL,
                     "            Action<ZNetView> erase = view => view.Destroy();\n"
                     "            erase(" + LOOKED_UP + ");\n")
        self.assert_refused("a lambda's `view` removed a looked-up object after the guard")

    def test_a_deconstruction_into_view_is_refused(self):
        self.swap_in(PORT, PORT_REMOVAL,
                     "            (view, _) = (" + LOOKED_UP + ", 0);\n" + PORT_REMOVAL)
        self.assert_refused("a deconstruction reassigned `view` with no `view =` to count")

    # The three below each kept every pin of 203d449's rule green (review of
    # that head): the method's end was found by indentation and the guard was
    # pinned by position, not scope. Each was verified to pass 203d449's
    # validator as well as d72b397's.

    def test_a_misindented_brace_hiding_a_retarget_and_goto_is_refused(self):
        self.swap_in(PORT, PORT_VIEW, PORT_VIEW + "        again:\n")
        self.swap_in(PORT, PORT_SUCCESS,
                     "        if (body.IsOwned)\n        {\n            answer = string.Empty;\n    }\n"
                     "        view = " + LOOKED_UP + ";\n"
                     "        if (view != null)\n        {\n            goto again;\n        }\n\n"
                     + PORT_SUCCESS)
        self.assert_refused("a `}` at method indentation ended the audited text; after it, `view` was "
                            "pointed at a looked-up object and the removal re-run")

    def test_a_goto_past_the_guard_is_refused(self):
        self.swap_in(PORT, PORT_DECIDE,
                     "        if (ledger.UnresolvedSteps < 0)\n        {\n            goto decided;\n        }\n\n"
                     + PORT_DECIDE)
        self.swap_in(PORT, PORT_VIEW, "        decided:\n" + PORT_VIEW)
        self.assert_refused("a goto jumped from above the decision to below the refusal")

    # The four below each kept every pin of 2103c58's rule green (review of
    # that head): depth counted braces only, and the guard's verdict could be
    # overwritten. Each was verified to pass 2103c58's validator as well as
    # d72b397's.

    def test_a_braceless_condition_in_front_of_the_refusal_is_refused(self):
        self.swap_in(PORT, PORT_ALLOWS, "        if (ledger.UnresolvedSteps == 0)\n" + PORT_ALLOWS)
        self.assert_refused("a braceless if made the refusal conditional at brace depth 1")

    def test_an_else_in_front_of_the_refusal_is_refused(self):
        self.swap_in(PORT, PORT_ALLOWS, "        if (!ledger.UnresolvedLoss) { }\n        else " + PORT_ALLOWS.lstrip())
        self.assert_refused("an else branch the refusal never reaches")

    def test_a_braceless_condition_in_front_of_the_ownership_check_is_refused(self):
        self.swap_in(PORT, PORT_VIEW_CHECK, "        if (ledger.RoundOpen)\n" + PORT_VIEW_CHECK)
        self.assert_refused("the ownership check ran only when a condition held")

    def test_overwriting_the_verdict_before_the_refusal_is_refused(self):
        self.swap_in(PORT, PORT_ALLOWS, "        verdict = StewardRetireVerdict.MayRetire;\n" + PORT_ALLOWS)
        self.assert_refused("the verdict refused on was not the one decided")

    # The two below each kept every pin of 75d837d's rule green (review of
    # that head): the refusing blocks' contents were unchecked. Each was
    # verified to pass 75d837d's validator as well as origin/main 0171cf6's.

    def test_a_conditional_return_inside_the_refusal_is_refused(self):
        self.swap_in(PORT, "            answer = StewardRetirement.Describe(verdict, held);\n"
                           "            return StewardRetireOutcome.Refused;\n",
                     "            answer = StewardRetirement.Describe(verdict, held);\n"
                     "            if (body.ItemCount > 100) answer += \".\"; else return StewardRetireOutcome.Refused;\n")
        self.assert_refused("a heavily laden body fell through the refusal to the removal")

    def test_a_conditional_return_inside_the_ownership_check_is_refused(self):
        self.swap_in(PORT, "not this game's to remove.\";\n            return StewardRetireOutcome.NotOurs;\n",
                     "not this game's to remove.\";\n"
                     "            if (ZNet.instance != null) answer += \".\"; else return StewardRetireOutcome.NotOurs;\n")
        self.assert_refused("a body this game does not own fell through the ownership check to the removal")

    # The four below each kept every pin of 80c3f3a's rule green (review of
    # that head). Each was verified to pass 80c3f3a's validator as well as
    # origin/main 0171cf6's.

    def test_conditional_compilation_around_the_guard_is_refused(self):
        self.swap_in(PORT, PORT_GUARD,
                     "#if !NETFRAMEWORK\n        held += 0;\n" + PORT_GUARD + "#endif\n        held += 0;\n")
        self.assert_refused("the tests (net10.0) compiled the guard; the plugin (net48) shipped without it")

    def test_conditional_compilation_in_the_decision_is_refused(self):
        self.swap_in(DOMAIN, DECIDE_OPEN,
                     DECIDE_OPEN + "#if NETFRAMEWORK\n        return StewardRetireVerdict.MayRetire;\n#endif\n")
        self.assert_refused("the shipped build's decision granted everything, in a file the port audit never read")

    def test_an_interpolation_hiding_a_verdict_overwrite_is_refused(self):
        self.swap_in(PORT, PORT_ALLOWS,
                     "        answer = $\"{(verdict = StewardRetireVerdict.MayRetire)}\";\n" + PORT_ALLOWS)
        self.assert_refused("an interpolation hole the literal mask blanked overwrote the verdict")

    def test_an_early_retired_while_she_stands_is_refused(self):
        self.swap_in(PORT, PORT_DECIDE,
                     "        if (ledger.FuelCarried < 0)\n        {\n            answer = string.Empty;\n"
                     "            return StewardRetireOutcome.Retired;\n        }\n\n" + PORT_DECIDE)
        self.assert_refused("the runtime was told she had left while her body still stood")

    # The three below each kept every pin of 8144fbd's rule green (review of
    # that head): the directive check read comment-stripped text with a narrow
    # idea of whitespace, and [Conditional] splits the builds without one. Each
    # was verified to pass 8144fbd's validator as well as origin/main 0171cf6's.

    def test_a_directive_hidden_behind_a_region_comment_opener_is_refused(self):
        self.swap_in(DOMAIN, ALLOWS,
                     "#region Allows /*\n#if NETFRAMEWORK\n"
                     "    public static bool Allows(StewardRetireVerdict verdict) => true;\n#else\n"
                     + ALLOWS + "#endif\n#endregion */\n")
        self.assert_refused("`#region x /*` made the comment stripper blank the `#if` below it")

    def test_a_directive_after_a_form_feed_is_refused(self):
        self.swap_in(DOMAIN, DECIDE_OPEN,
                     DECIDE_OPEN + "\f#if NETFRAMEWORK\n        return StewardRetireVerdict.MayRetire;\n\f#endif\n")
        self.assert_refused("C# reads a form feed as whitespace in front of a directive")

    def test_a_conditional_attribute_is_refused(self):
        self.swap_in(DOMAIN, ALLOWS,
                     ALLOWS + "\n    [System.Diagnostics.Conditional(\"NET\")]\n"
                     "    private static void OnlyInTheTests() { }\n")
        self.assert_refused("a [Conditional] method's calls exist in one build and not the other")
        # (An uncalled method: this pins the token rather than demonstrating a
        # whole escape; any call to it would vanish from one build only.)

    # Review of 3b23929: verified to pass 3b23929's validator as well as
    # origin/main 0171cf6's.

    def test_code_after_a_unicode_line_separator_in_a_comment_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            // tidy\u2028UnityEngine.Object.Destroy(_census.Live!.gameObject);\n")
        self.assert_refused("C# ended the comment at U+2028 and compiled the destruction the stripper hid")

    # Review of 3252bcd: verified to pass 3252bcd's validator as well as
    # origin/main 0171cf6's.

    def test_a_generic_nullable_in_an_interpolation_hiding_a_destruction_is_refused(self):
        self.swap_in(RUNTIME, RUNTIME_MARKER,
                     RUNTIME_MARKER + "            string tidy = $\"{new System.Collections.Generic.List<int?>()"
                     ".Count://}\"; UnityEngine.Object.Destroy(_census.Live!.gameObject);\n")
        self.assert_refused("the stripper read `int?` as a conditional, so `//` in the format hid the destruction")

    def test_a_guard_wrapped_in_a_condition_is_refused(self):
        self.swap_in(PORT, PORT_GUARD,
                     "        if (ledger.UnresolvedSteps >= 0)\n        {\n" + PORT_GUARD + "        }\n")
        self.assert_refused("the decision and its refusal ran only when a condition held")

    def test_this_suite_is_not_vacuous(self):
        # Not a plant: the guard text every plant above edits is still where
        # the plants expect it, so a refactor cannot turn them into no-ops.
        with open(PORT, encoding="utf-8-sig") as handle:
            port = handle.read()
        for pinned in (PORT_GUARD, PORT_REMOVAL, PORT_VIEW, PORT_DECIDE, PORT_RETIRED, PORT_SUCCESS,
                       PORT_ALLOWS, PORT_VIEW_CHECK):
            self.assertEqual(1, port.count(pinned), pinned)
        with open(RUNTIME, encoding="utf-8-sig") as handle:
            self.assertEqual(1, handle.read().count(RUNTIME_MARKER))
        with open(DOMAIN, encoding="utf-8-sig") as handle:
            domain = handle.read()
        self.assertEqual(1, domain.count(DECIDE_OPEN))
        self.assertEqual(1, domain.count(ALLOWS))


if __name__ == "__main__":
    unittest.main()
