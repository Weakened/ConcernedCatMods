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
CORE = os.path.join(DRESSING, "NpcDressing.cs")
WORKER = os.path.join(DRESSING, "NpcWorkerDresser.cs")
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

    def test_a_dresser_member_that_takes_an_array_of_objects_is_refused(self):
        # The review's escape: a denylist of engine types matched `GameObject g`
        # and missed `GameObject[] g`, generics, `object` and every game type.
        marker = "    public void TakeDown()"
        self.swap_in(DRESSER, marker,
                     "    public void Adopt(System.Collections.Generic.IEnumerable<GameObject> pieces) { }\n\n"
                     + marker)
        self.assert_refused("a public dresser member took a collection of arbitrary objects")

    def test_a_dresser_member_that_takes_a_game_type_is_refused(self):
        marker = "    public void TakeDown()"
        self.swap_in(DRESSER, marker, "    public void Face(Player viewer) { }\n\n" + marker)
        self.assert_refused("a public dresser member took the player")

    def test_a_velocity_write_anywhere_in_the_library_is_refused(self):
        self.plant_file(LIBRARY, ("Body", "ZzShove.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzShove\n"
                        "{\n"
                        "    internal static void Run(UnityEngine.Rigidbody body) =>\n"
                        "        body.linearVelocity = UnityEngine.Vector3.zero;\n"
                        "}\n")
        self.assert_refused("a velocity write passed a census that used to live under #400")

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

    # --- #295 S6: the worker-body dresser (owner decision 2026-10-29) ---------------------------

    def test_the_worker_dresser_cannot_skip_its_guard(self):
        self.swap_in(WORKER, "        if (!IsThatRolesWorkerBody(identity, body, out refusal))\n",
                     "        refusal = string.Empty;\n        if (false)\n")
        self.assert_refused("the worker dresser dressed a body it never proved was the role's")

    def test_the_guard_must_still_compare_the_saved_prefab(self):
        self.swap_in(WORKER,
                     "!string.Equals(prefab.name, role.Body.PrefabName, StringComparison.Ordinal)",
                     "prefab.name.Length == 0")
        self.assert_refused("the guard stopped checking that the body is the role's worker prefab")

    def test_the_guard_must_still_compare_the_saved_identity(self):
        self.swap_in(WORKER, "            || stored != identity)", "            || stored.IsEmpty)")
        self.assert_refused("the guard stopped checking that the body carries the role's identity")

    def test_a_worker_dresser_member_that_takes_any_object_is_refused(self):
        marker = "    public void TakeOff()"
        self.swap_in(WORKER, marker, "    public void Adorn(GameObject anything) { }\n\n" + marker)
        self.assert_refused("a public worker-dresser member took an arbitrary GameObject")

    def test_the_shared_dressing_stays_internal(self):
        self.swap_in(CORE, "internal sealed class NpcDressing", "public sealed class NpcDressing")
        self.assert_refused("the shared dressing went public and would dress anything it was handed")

    def test_the_shared_dressing_is_built_only_by_the_two_dressers(self):
        self.plant_file(LIBRARY, ("Body", "ZzDressAnything.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzDressAnything\n"
                        "{\n"
                        "    internal static object Run(UnityEngine.GameObject anyone, "
                        "TheConcernedCat.ConcernedNPC.Appearance.NpcLookSpec spec) =>\n"
                        "        new Dressing.NpcDressing(anyone, null, null, spec, \"h\", null);\n"
                        "}\n")
        self.assert_refused("a third place built the dressing on an object it chose")

    def test_a_qualified_construction_elsewhere_is_refused(self):
        # The review's escape: `new Dressing.NpcDressing(` slipped past a
        # pattern that only knew the bare name.
        self.plant_file(LIBRARY, ("Work", "ZzQualified.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Work;\n"
                        "internal static class ZzQualified\n"
                        "{\n"
                        "    internal static object Run(UnityEngine.GameObject anyone, "
                        "TheConcernedCat.ConcernedNPC.Appearance.NpcLookSpec spec) =>\n"
                        "        new TheConcernedCat.ConcernedNPC.Body.Dressing.NpcDressing(anyone, null, null, spec, "
                        "\"h\", null);\n"
                        "}\n")
        self.assert_refused("a fully qualified construction carried the dressing to an arbitrary object")

    def test_a_global_qualified_second_construction_in_a_dresser_is_refused(self):
        marker = "    public void TakeOff() => _dressing.TakeOff();"
        self.swap_in(WORKER, marker, marker + "\n\n    private static object Zz(GameObject g, NpcLookSpec s) =>\n"
                     "        new global::TheConcernedCat.ConcernedNPC.Body.Dressing.NpcDressing(g, null, null, s, "
                     "\"h\", null);")
        self.assert_refused("a second, unguarded dressing was built inside the worker dresser")

    def test_a_target_typed_construction_in_a_dresser_is_refused(self):
        marker = "    public void TakeOff() => _dressing.TakeOff();"
        self.swap_in(WORKER, marker, marker + "\n\n    private static NpcDressing Zz(GameObject g, NpcLookSpec s) =>\n"
                     "        new(g, null, null, s, \"h\", null);")
        self.assert_refused("a target-typed new() built a dressing the count could not see")

    def test_an_aliased_dressing_in_a_dresser_is_refused(self):
        self.swap_in(WORKER, "using UnityEngine;\n",
                     "using UnityEngine;\nusing Zd = TheConcernedCat.ConcernedNPC.Body.Dressing.NpcDressing;\n")
        self.assert_refused("an alias carried the dressing under another name")

    def test_a_body_tint_in_the_constructor_is_refused(self):
        self.swap_in(CORE, "        _wholeFigure = wholeFigure;\n",
                     "        _wholeFigure = wholeFigure;\n        TintBody(0, NpcLookRgb.White, \"skin tone\");\n")
        self.assert_refused("a body tint outside Apply reached a worker body")

    # --- #295 S3: the figure's talk point -----------------------------------------------------------

    def test_the_talk_behaviour_named_outside_the_dressing_folder_is_refused(self):
        self.plant_file(LIBRARY, ("Body", "ZzTalkAnything.cs"),
                        "namespace TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzTalkAnything\n"
                        "{\n"
                        "    internal static object? Run(UnityEngine.GameObject anyone) =>\n"
                        "        anyone.GetComponent<Dressing.NpcFigureTalk>();\n"
                        "}\n")
        self.assert_refused("the talk behaviour was reached from outside Body/Dressing")

    def test_a_second_talk_point_component_is_refused(self):
        marker = "        CapsuleCollider collider = point.AddComponent<CapsuleCollider>();"
        self.swap_in(PARTS, marker, marker + "\n        point.AddComponent<Rigidbody>();")
        self.assert_refused("the talk point grew a rigidbody and could push or be pushed")

    # --- S3/S4 review: "never blocks" is pinned, not review-only ---------------------------------

    def test_a_talk_point_on_a_solid_layer_is_refused(self):
        self.swap_in(PARTS, "            point.layer = nonSolidLayer;", "            point.layer = 0;")
        self.assert_refused("the talk point was put on the Default layer, which blocks a body")

    def test_a_talk_point_whose_trigger_fallback_is_turned_off_is_refused(self):
        self.swap_in(PARTS, "            collider.isTrigger = true;", "            collider.isTrigger = false;")
        self.assert_refused("the talk point's no-layer fallback became a solid capsule")

    def test_a_changed_non_solid_layer_list_is_refused(self):
        dresser = os.path.join(DRESSING, "NpcFigureDresser.cs")
        self.swap_in(dresser, 'in new[] { "piece_nonsolid", "item" }', 'in new[] { "Default", "piece_nonsolid" }')
        self.assert_refused("the talk point's layer could be chosen from a solid layer")

    def test_a_legitimate_new_constraint_elsewhere_in_the_library_is_not_refused(self):
        # Not a plant: the target-typed `new(` rule is about the two dresser
        # files. Ordinary code elsewhere in the library - a `where T : new()`
        # constraint, a target-typed `new()` of a list - must stay legal
        # (#295 S6 review; the rule used to scan every library file).
        self.plant_file(LIBRARY, ("Work", "ZzOrdinaryGeneric.cs"),
                        "using System.Collections.Generic;\n"
                        "namespace TheConcernedCat.ConcernedNPC.Work;\n"
                        "internal static class ZzOrdinaryGeneric\n"
                        "{\n"
                        "    internal static T Make<T>() where T : new() => new T();\n"
                        "    internal static List<int> Empty() { List<int> list = new(); return list; }\n"
                        "}\n")
        code, out = validate()
        self.assertEqual(0, code, "ordinary generic code was refused by a rule meant for the dressers:\n"
                         + out[-3000:])

    def test_a_body_tint_moved_above_the_worker_return_is_refused(self):
        self.swap_in(CORE,
                     "            if (!_wholeFigure)\n            {\n",
                     "            TintBody(1, hairColour, \"hair tint\");\n            if (!_wholeFigure)\n            {\n")
        self.assert_refused("a worker body's renderer would be tinted before the worker-mode return")

    def test_a_model_swap_without_the_whole_figure_flag_is_refused(self):
        self.swap_in(CORE, "(_wholeFigure && ApplyModel((int)_spec.Model))", "ApplyModel((int)_spec.Model)")
        self.assert_refused("a worker body could be given another mesh")

    def test_the_worker_dresser_calls_no_engine_write_helper(self):
        marker = "    public void TakeOff() => _dressing.TakeOff();"
        self.swap_in(WORKER, marker, marker + "\n\n    private static void Zz(GameObject g) => NpcFigureParts.Drop(g);")
        self.assert_refused("the worker dresser reached for an engine-write helper directly")

    def test_success_sentences_state_the_text_audits_limits(self):
        code, out = validate()
        self.assertEqual(0, code)
        self.assertIn("it does not follow indirection or prove control flow", out)
        self.assertIn("Cartographer's pre-library Hulgi copy exempt by name", out)


if __name__ == "__main__":
    unittest.main()
