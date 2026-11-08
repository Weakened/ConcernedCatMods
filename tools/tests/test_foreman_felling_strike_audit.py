"""Planted violations for #282 leaf 2's felling strike audit (check_foreman_felling_strike_callers).

D19's one pinned `tree.Damage(hit)` sits inside WorldFellingPort.StrikeOnce, which
`cf_collect fell` and a collection order's strike in WorldSourcePickupPort both
reach. The D19 audit counts `.Damage(`, so it cannot see a new caller of the verb:
this audit pins the declaration and exactly those two calls, verbatim.

Each test plants an escape in the real source tree, runs the real validator and
requires the strike audit to refuse it. It then runs a copy of the validator with
exactly that rule's registration removed and requires the same plant to pass:
that is the proof the escape existed before the rule and that no other rule
catches it. tearDown restores every file byte for byte and re-runs the clean
validator.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
FOREMAN = os.path.join(ROOT, "src", "ConcernedForeman")
RUNTIME = os.path.join(FOREMAN, "Runtime")
PORT = os.path.join(RUNTIME, "Collection", "WorldFellingPort.cs")
PICKUP = os.path.join(RUNTIME, "Collection", "WorldSourcePickupPort.cs")
SCOPES = os.path.join(RUNTIME, "Collection", "WorkScopeBuilder.cs")
PANEL = os.path.join(FOREMAN, "Ui", "CollectionOrderPanel.cs")
SHARED_SETTLEMENT = os.path.join(ROOT, "src", "Shared", "Settlement", "Collection", "CollectionOrder.cs")

RULE = "check_foreman_felling_strike_callers"
PHRASE = "#282 felling strike audit"

ORDER_CALL = ("        FellOutcome outcome = _felling.StrikeOnce(\n"
              "            tree, view, kind, axe!, humanoid, workerAt, _spawned,")


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class FellingStrikeAudit(unittest.TestCase):
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
        self.assertEqual(0, code, "a strike plant was left behind:\n" + out[-3000:])

    def _read(self, path):
        with open(path, "rb") as handle:
            original = handle.read()
        self._restore.append((path, original))
        return original

    def swap_in(self, path, old, new):
        original = self._read(path)
        old_b, new_b = old.encode("utf-8"), new.encode("utf-8")
        self.assertEqual(1, original.count(old_b), "pinned text moved: " + old)
        with open(path, "wb") as handle:
            handle.write(original.replace(old_b, new_b, 1))

    def append_to(self, path, text):
        original = self._read(path)
        with open(path, "wb") as handle:
            handle.write(original + text.encode("utf-8"))

    def plant_file(self, path, body):
        self.assertFalse(os.path.exists(path), "plant would overwrite a real file: " + path)
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)

    def without_rule(self):
        """A copy of the validator with exactly this rule's registration removed."""
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + RULE + "(errors))\n"
        self.assertEqual(1, source.count(registration), "the rule is not registered exactly once")
        path = os.path.join(ROOT, "tools", "_zz_without_felling_strike.py")
        self.plant_file(path, source.replace(registration, ""))
        return path

    def assert_refused(self, why):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(PHRASE, out, why + ": refused, but not by the strike audit\n" + out[-3000:])
        code, out = run(self.without_rule())
        self.assertEqual(0, code, why + ": without the strike audit something else still refuses it, so the plant "
                         "does not prove that rule\n" + out[-3000:])

    def test_a_second_order_strike_in_the_pickup_port_is_refused(self):
        self.swap_in(PICKUP, ORDER_CALL, ORDER_CALL.replace(
            "        FellOutcome outcome = _felling.StrikeOnce(",
            "        _felling.StrikeOnce(\n            tree, view, kind, axe!, humanoid, workerAt, _spawned, out _, out _, out _, out _);\n"
            "        FellOutcome outcome = _felling.StrikeOnce(", 1))
        self.assert_refused("two strikes per order call")

    def test_the_order_strike_through_another_receiver_is_refused(self):
        self.swap_in(PICKUP, ORDER_CALL, ORDER_CALL.replace("_felling.StrikeOnce(", "_other!.StrikeOnce(", 1))
        self.assert_refused("the pinned receiver replaced")

    def test_a_strike_from_another_runtime_file_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzStrikeElsewhere\n{\n"
                       "    internal static void Hit(WorldFellingPort port) => port.StrikeOnce(null!, null!, null!, null!, null!, "
                       "default, null!, out _, out _, out _, out _);\n}\n")
        self.assert_refused("a strike from elsewhere in Runtime/")

    def test_a_strike_from_the_ui_is_refused(self):
        self.append_to(PANEL, "\ninternal static class ZzPanelStrike\n{\n"
                       "    internal static void Hit(dynamic port) => port.StrikeOnce();\n}\n")
        self.assert_refused("a strike from Ui/")

    def test_a_strike_from_a_linked_shared_tree_is_refused(self):
        self.append_to(SHARED_SETTLEMENT, "\ninternal static class ZzSharedStrike\n{\n"
                       "    internal static void Hit(dynamic port) => port.StrikeOnce();\n}\n")
        self.assert_refused("a strike from src/Shared/Settlement, which Foreman compiles in")

    def test_the_allowance_does_not_follow_the_basename(self):
        self.plant_file(os.path.join(RUNTIME, "Construction", "WorldSourcePickupPort.cs"),
                        "namespace Zz;\ninternal static class ZzSameName\n{\n"
                        "    internal static void Hit(dynamic _felling) => _felling.StrikeOnce();\n}\n")
        self.assert_refused("the pickup port's file name in another folder")

    def test_a_removed_order_strike_is_refused(self):
        self.swap_in(PICKUP, ORDER_CALL, ORDER_CALL.replace(
            "        FellOutcome outcome = _felling.StrikeOnce(", "        FellOutcome outcome = FellOutcome.NoEffect; _ = (", 1))
        self.assert_refused("the order's strike removed while the audit still promises it")


if __name__ == "__main__":
    unittest.main()
