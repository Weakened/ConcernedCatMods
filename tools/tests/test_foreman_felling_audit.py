"""Planted violations for D19's felling audit (check_foreman_felling_scope).

D19 (owner decision 2026-10-31, #282) grants one world call: vanilla's
Destructible.Damage on the four allowlisted small trees, once, in
src/ConcernedForeman/Runtime/Collection/WorldFellingPort.cs. TreeBase and
TreeLog were decided but are unused, and the game's damage handler, the instant
kill and a direct damage application are shortcuts around the pinned hit. The
scope is the whole Foreman product and every shared tree its project compiles
in - not Runtime/ alone, which the #400 audit walks.

Each test plants an escape in the real source tree, runs the real validator and
requires the D19 audit to refuse it. It then runs a copy of the validator with
exactly that rule's registration removed and requires the same plant to pass:
that is the proof the escape existed before the rule and that no other rule
catches it. tearDown restores every file and re-runs the clean validator.
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
SCOPES = os.path.join(RUNTIME, "Collection", "WorkScopeBuilder.cs")
PANEL = os.path.join(FOREMAN, "Ui", "CollectionOrderPanel.cs")
PLUGIN = os.path.join(FOREMAN, "Plugin.cs")
DOMAIN_DIR = os.path.join(FOREMAN, "Domain", "Construction")
SHARED_SETTLEMENT = os.path.join(ROOT, "src", "Shared", "Settlement", "Collection", "CollectionOrder.cs")
SHARED_LADDERS_DIR = os.path.join(ROOT, "src", "Shared", "Ladders")

RULE = "check_foreman_felling_scope"
PHRASE = "D19 felling audit"


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class FellingAudit(unittest.TestCase):
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

    def without_rule(self):
        """A copy of the validator with exactly this rule's registration removed."""
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + RULE + "(errors))\n"
        self.assertEqual(1, source.count(registration), "the rule is not registered exactly once")
        path = os.path.join(ROOT, "tools", "_zz_without_felling_scope.py")
        self.plant_file(path, source.replace(registration, ""))
        return path

    def assert_refused(self, why):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(PHRASE, out, why + ": refused, but not by the D19 audit\n" + out[-3000:])
        code, out = run(self.without_rule())
        self.assertEqual(0, code, why + ": without the D19 audit something else still refuses it, so the plant "
                         "does not prove that rule\n" + out[-3000:])

    # --- the one pinned hit ------------------------------------------------------------------------

    def test_a_second_hit_in_the_port_is_refused(self):
        self.swap_in(PORT, "            tree.Damage(hit);\n",
                     "            tree.Damage(hit);\n            tree.Damage(hit);\n")
        self.assert_refused("two hits per call")

    def test_a_hit_on_another_receiver_in_the_port_is_refused(self):
        self.swap_in(PORT, "            tree.Damage(hit);\n",
                     "            view.GetComponent<Destructible>().Damage(hit);\n")
        self.assert_refused("the pinned receiver replaced")

    def test_a_hit_in_another_runtime_file_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzFellElsewhere\n{\n"
                       "    internal static void Hit(Destructible tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused("a damage call elsewhere in Runtime/")

    def test_the_allowance_does_not_follow_the_basename(self):
        self.plant_file(os.path.join(RUNTIME, "Construction", "WorldFellingPort.cs"),
                        "namespace Zz;\ninternal static class ZzSameName\n{\n"
                        "    internal static void Hit(Destructible tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused("the same file name in another folder")

    # --- outside Runtime/: the whole product ------------------------------------------------------

    def test_a_hit_in_the_ui_is_refused(self):
        self.append_to(PANEL, "\ninternal static class ZzPanelFell\n{\n"
                       "    internal static void Hit(Destructible tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused("a damage call in Ui/")

    def test_a_tree_base_hit_in_the_plugin_is_refused(self):
        self.append_to(PLUGIN, "\ninternal static class ZzPluginFell\n{\n"
                       "    internal static void Hit(TreeBase tree, HitData hit) => tree.Damage(hit);\n}\n")
        self.assert_refused("TreeBase.Damage in Plugin.cs")

    def test_a_tree_log_hit_in_domain_is_refused(self):
        self.plant_file(os.path.join(DOMAIN_DIR, "ZzLogCutter.cs"),
                        "namespace Zz;\ninternal static class ZzLogCutter\n{\n"
                        "    internal static void Cut(TreeLog log, HitData hit) => log.Damage(hit);\n}\n")
        self.assert_refused("TreeLog.Damage in Domain/")

    def test_a_hit_in_a_linked_shared_tree_is_refused(self):
        self.append_to(SHARED_SETTLEMENT, "\ninternal static class ZzSharedFell\n{\n"
                       "    internal static void Hit(dynamic tree, object hit) => tree.Damage(hit);\n}\n")
        self.assert_refused("a damage call in src/Shared/Settlement, which Foreman compiles in")

    def test_naming_tree_base_in_a_linked_shared_tree_is_refused(self):
        self.plant_file(os.path.join(SHARED_LADDERS_DIR, "ZzMature.cs"),
                        "namespace Zz;\ninternal static class ZzMature\n{\n"
                        "    internal const string Kind = nameof(TreeBase);\n}\n")
        self.assert_refused("TreeBase named in src/Shared/Ladders")

    # --- shortcuts ---------------------------------------------------------------------------------

    def test_an_instant_kill_is_refused(self):
        self.append_to(PANEL, "\ninternal static class ZzKill\n{\n"
                       "    internal static void Kill(Destructible tree) => tree.DestroyNow();\n}\n")
        self.assert_refused("DestroyNow")

    def test_calling_the_damage_handler_by_name_is_refused(self):
        self.append_to(SCOPES, "\ninternal static class ZzHandler\n{\n"
                       "    internal const string Name = \"RPC_Damage\";\n}\n")
        self.assert_refused("RPC_Damage named in code")


if __name__ == "__main__":
    unittest.main()
