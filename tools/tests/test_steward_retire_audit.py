"""Destructive mutation tests for Concerned Steward's S1 retire audit
(owner decision 2026-10-28, docs/mods/concerned-steward/DECISIONS.md S1).

Each test changes the real source tree, runs the real repository validator, and
restores the exact original text. Every plant is a way Sunniva's body (or any
networked body) could leave the world outside the one guarded verb, and each was
verified to pass the validator that lacked this rule (origin/main d72b397)
(and the last five also the validator at d0d559e, whose rule they got past)
before being added here; setting STEWARD_PLANT_VALIDATOR to another validator's
path re-runs that check, expecting every plant to get through it.
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
LABEL = "[steward] S1 retire audit"

PORT_GUARD = """        StewardRetireVerdict verdict = StewardRetirement.Decide(facts);
        if (!StewardRetirement.Allows(verdict))
        {
            answer = StewardRetirement.Describe(verdict, held);
            return StewardRetireOutcome.Refused;
        }
"""
PORT_REMOVAL = "            view.Destroy();\n"
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

    def test_this_suite_is_not_vacuous(self):
        # Not a plant: the guard text every plant above edits is still where
        # the plants expect it, so a refactor cannot turn them into no-ops.
        with open(PORT, encoding="utf-8-sig") as handle:
            port = handle.read()
        for pinned in (PORT_GUARD, PORT_REMOVAL, PORT_VIEW):
            self.assertEqual(1, port.count(pinned), pinned)
        with open(RUNTIME, encoding="utf-8-sig") as handle:
            self.assertEqual(1, handle.read().count(RUNTIME_MARKER))


if __name__ == "__main__":
    unittest.main()
