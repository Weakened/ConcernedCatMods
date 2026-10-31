"""Planted violations for D19's felling pins in the #400 Foreman runtime audit.

D19 (owner decision 2026-10-31, #282) grants one world call: vanilla's
Destructible.Damage on the four allowlisted small trees, once, in
src/ConcernedForeman/Runtime/Collection/WorldFellingPort.cs. TreeBase and
TreeLog were decided but are unused, and the game's damage handler, the instant
kill and a direct damage application are shortcuts around the pinned hit.

Each test plants an escape in the real source tree, runs the real validator and
requires a refusal naming the entry. It then runs a copy of the validator with
exactly that table entry removed and requires the same plant to pass: a plant
that some other rule also catches proves nothing about this one. Every planted
file and edit is restored by tearDown, which re-runs the clean validator.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
RUNTIME = os.path.join(ROOT, "src", "ConcernedForeman", "Runtime")
PORT = os.path.join(RUNTIME, "Collection", "WorldFellingPort.cs")
SCOPES = os.path.join(RUNTIME, "Collection", "WorkScopeBuilder.cs")

DAMAGE = "Destructible.Damage (D19 small-tree felling)"
TREE_TYPES = "TreeBase/TreeLog (D19: decided, unused)"
SHORTCUT = "destruction shortcut"


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class FellingPins(unittest.TestCase):
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
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "a felling plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def append_to(self, path, text):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original + text)

    def plant_file(self, path, body):
        self.assertFalse(os.path.exists(path), "plant would overwrite a real file: " + path)
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)

    def without(self, label):
        """A copy of the validator with exactly the table entry for `label` removed.

        Written inside tools/ because the validator finds the repository from its
        own location; removed by tearDown like any other plant.
        """
        with open(VALIDATOR, encoding="utf-8") as handle:
            lines = handle.read().split("\n")
        start = [i for i, line in enumerate(lines) if line.lstrip().startswith('("' + label + '"')]
        self.assertEqual(1, len(start), "the entry is not in the validator exactly once: " + label)
        first = start[0]
        last = first
        if not lines[first].rstrip().endswith(")),"):
            # A multi-line audited entry: up to its closing `)),`.
            last = first + 1
            while lines[last].strip() != ")),":
                last += 1
        trimmed = lines[:first] + lines[last + 1:]
        path = os.path.join(ROOT, "tools", "_zz_without_felling_" + str(abs(hash(label))) + ".py")
        self.plant_file(path, "\n".join(trimmed))
        return path

    def assert_refused_by(self, label, why):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(label, out, why + ": refused, but not by " + label + "\n" + out[-3000:])
        code, out = run(self.without(label))
        self.assertNotIn(label, out, why + ": still refused without " + label)
        self.assertEqual(0, code, why + ": another rule also refuses this plant, so it does not prove "
                         + label + "\n" + out[-3000:])

    # --- the one pinned hit ------------------------------------------------------------------------

    def test_a_second_hit_in_the_port_is_refused(self):
        self.swap_in(PORT, "            tree.Damage(hit);\n",
                     "            tree.Damage(hit);\n            tree.Damage(hit);\n")
        self.assert_refused_by(DAMAGE, "two hits per call")

    def test_a_hit_on_another_receiver_in_the_port_is_refused(self):
        self.swap_in(PORT, "            tree.Damage(hit);\n",
                     "            view.GetComponent<Destructible>().Damage(hit);\n")
        self.assert_refused_by(DAMAGE, "the pinned receiver replaced")

    def test_a_hit_in_another_runtime_file_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzFellElsewhere\n{\n"
                       "    internal static void Hit(Destructible tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused_by(DAMAGE, "a damage call outside the port")

    def test_the_allowance_does_not_follow_the_basename(self):
        self.plant_file(os.path.join(RUNTIME, "Construction", "WorldFellingPort.cs"),
                        "namespace Zz;\ninternal static class ZzSameName\n{\n"
                        "    internal static void Hit(Destructible tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused_by(DAMAGE, "the same file name in another folder")

    # --- decided but unused, and shortcuts ---------------------------------------------------------

    def test_naming_tree_base_in_the_runtime_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzMature\n{\n"
                       "    internal static bool Is(UnityEngine.GameObject o) => o.GetComponent<TreeBase>() != null;\n}\n")
        self.assert_refused_by(TREE_TYPES, "TreeBase named")

    def test_naming_tree_log_in_the_runtime_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzLog\n{\n"
                       "    internal static bool Is(UnityEngine.GameObject o) => o.GetComponent<TreeLog>() != null;\n}\n")
        self.assert_refused_by(TREE_TYPES, "TreeLog named")

    def test_an_instant_kill_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzKill\n{\n"
                       "    internal static void Kill(Destructible tree) => tree.DestroyNow();\n}\n")
        self.assert_refused_by(SHORTCUT, "DestroyNow")

    def test_calling_the_damage_handler_by_name_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzHandler\n{\n"
                       "    internal const string Name = \"RPC_Damage\";\n"
                       "    internal static string Method => nameof(RPC_Damage);\n}\n")
        self.assert_refused_by(SHORTCUT, "RPC_Damage named in code")


if __name__ == "__main__":
    unittest.main()
