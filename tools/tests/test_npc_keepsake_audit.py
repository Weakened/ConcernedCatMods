"""Destructive mutation tests for the #295 S2 shared companion keepsake.

The keepsake (owner decision 2026-10-30) is one copy in Concerned NPC for
Thorstein's carpenter's square, Gunnar's buckle and Sunniva's flint. Its engine
writes sit in the NPC census of the figure-parts audit; the "NPC keepsake audit"
confines the rest. Each test plants an escape, runs the real validator, and
restores the exact text.
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
LIBRARY = os.path.join(ROOT, "src", "ConcernedNPC")
KEEPSAKES = os.path.join(LIBRARY, "Body", "Keepsakes")
PARTS = os.path.join(KEEPSAKES, "NpcKeepsakeParts.cs")
KEEPSAKE = os.path.join(KEEPSAKES, "NpcKeepsake.cs")
FOREMAN = os.path.join(ROOT, "src", "ConcernedForeman")

AUDIT = "[concernednpc] NPC keepsake audit"
CENSUS = "[concernednpc] NPC figure-parts audit"


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class NpcKeepsakeIsConfined(unittest.TestCase):
    def setUp(self):
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
        for path in reversed(self._planted):
            if os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
        code, out = validate()
        self.assertEqual(0, code, "a keepsake plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def plant_file(self, root, relative, body):
        path = os.path.join(root, *relative)
        directory = os.path.dirname(path)
        if not os.path.isdir(directory):
            os.makedirs(directory)
            self._planted.append(directory)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)
        self._planted.append(path)

    def assert_refused(self, why, rule=AUDIT):
        code, out = validate()
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(rule, out, why)

    def test_a_helper_called_from_another_library_file_is_refused(self):
        self.plant_file(LIBRARY, ("Body", "ZzKeepsakeEscape.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzKeepsakeEscape\n"
                        "{\n"
                        "    internal static void Run(UnityEngine.GameObject anyone) =>\n"
                        "        Keepsakes.NpcKeepsakeParts.Drop(anyone);\n"
                        "}\n")
        self.assert_refused("a library file outside Body/Keepsakes destroyed through the keepsake's helpers")

    def test_an_extra_helper_call_is_refused(self):
        marker = "        NpcKeepsakeParts.Drop(_root);"
        self.swap_in(KEEPSAKE, marker, marker + "\n        NpcKeepsakeParts.Drop(Player.m_localPlayer);")
        self.assert_refused("a second Drop in the keepsake went unpinned")

    def test_the_helpers_must_stay_internal(self):
        self.swap_in(PARTS, "internal static class NpcKeepsakeParts", "public static class NpcKeepsakeParts")
        self.assert_refused("public helpers would hand every consumer a build-and-destroy-anything call")

    def test_a_public_member_that_takes_any_object_is_refused(self):
        marker = "    public void Remove()"
        self.swap_in(KEEPSAKE, marker, "    public void Adopt(UnityEngine.GameObject anything) { }\n\n" + marker)
        self.assert_refused("a public keepsake member took an arbitrary object")

    def test_a_second_public_type_is_refused(self):
        self.plant_file(KEEPSAKES, ("ZzDoor.cs",),
                        "namespace TheConcernedCat.ConcernedNPC.Body.Keepsakes;\npublic static class ZzDoor { }\n")
        self.assert_refused("the keepsake folder's public surface grew")

    def test_a_network_view_is_refused(self):
        marker = "    private static int NonSolidLayer()"
        self.swap_in(KEEPSAKE, marker,
                     "    private static bool Saved(UnityEngine.GameObject g) => g.GetComponent<ZNetView>() != null;\n\n"
                     + marker)
        self.assert_refused("a keepsake reached for a network view and could be saved or replicated")

    def test_a_new_engine_write_in_the_parts_file_is_refused(self):
        marker = "        keepsake.rotation = facing;"
        self.swap_in(PARTS, marker, marker + "\n        keepsake.localScale = Vector3.one;")
        self.assert_refused("an unpinned pose write in the keepsake's parts", rule=CENSUS)

    def test_an_unpinned_component_is_refused(self):
        marker = "        part.AddComponent<MeshRenderer>().sharedMaterials = materials;"
        self.swap_in(PARTS, marker, marker + "\n        part.AddComponent<Rigidbody>();")
        self.assert_refused("an unpinned AddComponent in the keepsake's parts", rule=CENSUS)

    def test_a_product_that_builds_its_own_keepsake_is_refused(self):
        self.plant_file(FOREMAN, ("Runtime", "ZzOwnKeepsake.cs"),
                        "internal static class ZzOwnKeepsake\n"
                        "{\n"
                        "    internal static void Run(UnityEngine.GameObject g, UnityEngine.Material[] m) =>\n"
                        "        g.AddComponent<UnityEngine.MeshRenderer>().sharedMaterials = m;\n"
                        "}\n")
        self.assert_refused("Foreman grew its own render-only keepsake")

    def test_success_sentence_states_the_limits(self):
        code, out = validate()
        self.assertEqual(0, code)
        self.assertIn("Broken Compass exempt", out)
        self.assertIn("does not follow indirection or prove control flow", out)


if __name__ == "__main__":
    unittest.main()
