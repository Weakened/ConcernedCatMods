"""Planted violations for the #480 format-character audit.

The compiler drops a Unicode format (Cf) character from an identifier, so a
soft hyphen or a zero-width space inside `Destroy` or `SetTrigger` spells the
same call to C# and nothing to a text audit. #473 closed this for Teamster's
scope; #480 closes it for every product and library source under src/.

Each test plants a split name the product's own audit exists to catch, runs the
real validator and requires the #480 audit to refuse it. It then runs a copy of
the validator with exactly that rule's registration removed and requires the
same plant to pass: the proof that the product's own audit could not see it.
tearDown restores every file and re-runs the clean validator.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
# #372 (F2e): her own body file is gone (Concerned NPC's NpcBody); any Steward
# source carries the plant as well, and this one replaced it.
STEWARD_BODY = os.path.join(ROOT, "src", "ConcernedSteward", "Runtime", "StewardSavedBodies.cs")
FOREMAN_POSE = os.path.join(ROOT, "src", "ConcernedForeman", "Runtime", "Construction", "BuildPose.cs")
NPC_PLANT = os.path.join(ROOT, "src", "ConcernedNPC", "Custody", "ZzPlanted480.cs")
TEST_PLANT = os.path.join(ROOT, "src", "ConcernedNPC.Tests", "ZzPlanted480.cs")

RULE = "check_source_has_no_format_characters"
PHRASE = "#480 format-character audit"
SOFT_HYPHEN = "­"
ZERO_WIDTH_SPACE = "​"


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class FormatCharactersAreRefused(unittest.TestCase):
    def setUp(self):
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "the tree must be clean before a plant means anything:\n" + out[-3000:])
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path in reversed(self._planted):
            if os.path.exists(path):
                os.remove(path)
        for path, original in reversed(self._restore):
            with open(path, "wb") as handle:
                handle.write(original)
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "a #480 plant was left behind:\n" + out[-3000:])

    def append_to(self, path, text):
        with open(path, "rb") as handle:
            original = handle.read()
        self._restore.append((path, original))
        with open(path, "wb") as handle:
            handle.write(original + text.encode("utf-8"))

    def plant_file(self, path, body):
        self.assertFalse(os.path.exists(path), "plant would overwrite a real file: " + path)
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)

    def without_rule(self):
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + RULE + "(errors))\n"
        self.assertEqual(1, source.count(registration), "the rule is not registered exactly once")
        path = os.path.join(ROOT, "tools", "_zz_without_format_characters.py")
        self.plant_file(path, source.replace(registration, ""))
        return path

    def assert_refused(self, why, character):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(PHRASE + ":", out, why + ": refused, but not by the #480 audit\n" + out[-3000:])
        self.assertIn("holds the format character U+%04X" % ord(character), out, out[-3000:])
        code, out = run(self.without_rule())
        self.assertEqual(0, code, why + ": without the #480 audit something else still refuses it, so the "
                         "plant does not prove that rule\n" + out[-3000:])

    def test_a_soft_hyphen_hides_a_destruction_from_the_steward_audit(self):
        self.append_to(STEWARD_BODY, "\ninternal static class Zz480Steward\n{\n"
                       "    internal static void Gone(dynamic view) => view.Dest" + SOFT_HYPHEN + "roy();\n}\n")
        self.assert_refused("a soft hyphen split Destroy past the Steward audit", SOFT_HYPHEN)

    def test_a_zero_width_space_hides_a_trigger_from_the_foreman_audit(self):
        self.append_to(FOREMAN_POSE, "\ninternal static class Zz480Foreman\n{\n"
                       "    internal static void Wave(dynamic animation) => animation.SetTr"
                       + ZERO_WIDTH_SPACE + "igger(\"wave\");\n}\n")
        self.assert_refused("a zero-width space split SetTrigger past the #400 audit", ZERO_WIDTH_SPACE)

    def test_a_format_character_in_the_library_is_refused(self):
        self.plant_file(NPC_PLANT, "internal static class Zz480Library\n{\n"
                        "    internal static void Gone(dynamic view) => view.Dest" + ZERO_WIDTH_SPACE + "roy();\n}\n")
        self.assert_refused("a zero-width space in ConcernedNPC passed", ZERO_WIDTH_SPACE)

    def test_a_test_project_may_hold_one_as_data(self):
        # Not a plant: tests are outside the rule on purpose, and this pins it.
        self.plant_file(TEST_PLANT, "internal static class Zz480Data\n{\n"
                        "    internal const string Hyphen = \"" + SOFT_HYPHEN + "\";\n}\n")
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "a test project's data was refused\n" + out[-3000:])


if __name__ == "__main__":
    unittest.main()
