"""Destructive mutation tests for the #295 lift: the figure dresser's audits.

The dresser moved from Concerned Foreman into Concerned NPC so that Thorstein,
Sunniva and Gunnar share one copy. Its engine writes used to be pinned by
Foreman's #400 audit and its helpers confined by Foreman's "#295 figure-parts
audit"; both moved with it ("NPC figure-parts audit"), and a second rule
("#295 one-dresser audit") refuses a product that grows its own copy back.

Each test changes the real source tree, runs the real repository validator, and
restores the exact original text. Every plant is an escape the previous home of
these rules already had a plant for, or one the move itself opened (a basename
in another library folder, a public helper, a dresser member that takes any
object, a consumer's own copy).
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
LIBRARY = os.path.join(ROOT, "src", "ConcernedNPC")
DRESSING = os.path.join(LIBRARY, "Body", "Dressing")
PARTS = os.path.join(DRESSING, "NpcFigureParts.cs")
DRESSER = os.path.join(DRESSING, "NpcFigureDresser.cs")
STEWARD = os.path.join(ROOT, "src", "ConcernedSteward")

AUDIT = "[concernednpc] NPC figure-parts audit"
ONE_HOME = "[interop] #295 one-dresser audit"


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class NpcFigurePartsArePinned(unittest.TestCase):
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
        self.assertEqual(0, code, "a #295 plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def after(self, path, marker, planted):
        self.swap_in(path, marker, marker + "\n" + planted)

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

    # --- the helpers' confinement ----------------------------------------------------------------

    def test_a_helper_called_from_another_library_file_is_refused(self):
        self.plant_file(LIBRARY, ("Body", "ZzFigureEscape.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzFigureEscape\n"
                        "{\n"
                        "    internal static void Run(ZNetView view) =>\n"
                        "        Dressing.NpcFigureParts.Drop(view.gameObject);\n"
                        "}\n")
        self.assert_refused("a library file outside the dresser destroyed through NpcFigureParts.Drop")

    def test_an_extra_helper_call_in_the_dresser_is_refused(self):
        marker = "            NpcFigureParts.Drop(_root);"
        self.after(DRESSER, marker, "            NpcFigureParts.Drop(Player.m_localPlayer);")
        self.assert_refused("a second Drop in the dresser went unpinned")

    def test_a_helper_method_group_is_refused(self):
        marker = "            NpcFigureParts.Drop(_root);"
        self.after(DRESSER, marker, "            System.Action<UnityEngine.Object?> drop = NpcFigureParts.Drop;")
        self.assert_refused("a method group carried Drop out of the audit")

    def test_making_the_helpers_public_is_refused(self):
        self.swap_in(PARTS, "internal static class NpcFigureParts", "public static class NpcFigureParts")
        self.assert_refused("public helpers would hand every consumer a destroy-anything call")

    # --- the census ------------------------------------------------------------------------------

    def test_an_unpinned_destruction_in_another_library_folder_is_refused(self):
        self.plant_file(LIBRARY, ("Work", "ZzReap.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Work;\n"
                        "internal static class ZzReap\n"
                        "{\n"
                        "    internal static void Run(UnityEngine.GameObject target) =>\n"
                        "        UnityEngine.Object.Destroy(target);\n"
                        "}\n")
        self.assert_refused("a destruction outside every pinned site passed the census")

    def test_the_allowance_does_not_follow_a_basename_to_another_folder(self):
        self.plant_file(LIBRARY, ("Work", "NpcFigureParts.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Work;\n"
                        "internal static class ZzSameName\n"
                        "{\n"
                        "    internal static void Drop(UnityEngine.Object? target)\n"
                        "    {\n"
                        "        UnityEngine.Object.Destroy(target);\n"
                        "    }\n"
                        "}\n")
        self.assert_refused("a same-named file in another folder inherited the parts file's allowance")

    def test_a_second_pose_write_in_the_parts_file_is_refused(self):
        marker = "        figure.rotation = facing;"
        self.after(PARTS, marker, "        figure.localScale = spot;")
        self.assert_refused("a pose write the census never pinned went through")

    def test_a_changed_receiver_is_a_different_program(self):
        self.swap_in(PARTS, "        body.sharedMesh = mesh;", "        body.transform.parent = null;")
        self.assert_refused("the mesh swap was rewritten into a re-parenting and kept its allowance")

    # --- the door consumers use ------------------------------------------------------------------

    def test_a_dresser_member_that_takes_any_object_is_refused(self):
        marker = "    public void TakeDown()"
        self.swap_in(DRESSER, marker,
                     "    public void Discard(GameObject anything) { }\n\n" + marker)
        self.assert_refused("a public dresser member took an arbitrary GameObject")

    def test_a_third_public_type_in_the_dressing_folder_is_refused(self):
        self.plant_file(DRESSING, ("ZzDoor.cs",),
                        "namespace TheConcernedCat.ConcernedNPC.Body.Dressing;\n"
                        "public static class ZzDoor\n"
                        "{\n"
                        "}\n")
        self.assert_refused("the dressing folder's public surface grew past the two pinned types")

    # --- no third copy ---------------------------------------------------------------------------

    def test_a_consumer_that_reads_the_palette_itself_is_refused(self):
        self.plant_file(STEWARD, ("Runtime", "ZzOwnPalette.cs"),
                        "internal static class ZzOwnPalette\n"
                        "{\n"
                        "    internal static object? Read() =>\n"
                        "        UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizaton>();\n"
                        "}\n")
        self.assert_refused("Steward grew its own palette reader", rule=ONE_HOME)

    def test_success_sentences_state_the_text_audits_limits(self):
        code, out = validate()
        self.assertEqual(0, code)
        self.assertIn("it does not follow indirection or prove control flow", out)
        self.assertIn("Cartographer's pre-library Hulgi copy exempt by name", out)


if __name__ == "__main__":
    unittest.main()
