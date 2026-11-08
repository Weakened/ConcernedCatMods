"""Destructive mutation tests for the #458 Teamster figure audit.

Gunnar's presentation figure is built, stood, dressed and taken down by
Concerned NPC, whose own "NPC figure-parts audit" pins every engine write it
makes. What Teamster must not do is reach round that library, so its audit
("[teamster] #458 figure audit") lets one file name the figure library, pins
each figure call in it, refuses any engine write of that file's own, and holds
the rest of Adapters/Companions to building nothing.

Each test changes the real source tree, runs the real repository validator, and
restores the exact original text. Every plant was verified to pass the
validator that lacked this rule (origin/main 4457f20's tools/validate_repo.py,
copied into tools/ so its ROOT is this tree) before being added here: setting
TEAMSTER_FIGURE_PLANT_VALIDATOR to such a validator's path re-runs that check,
expecting every plant except the #313 regression pin to get through it.
The nine plants an independent review constructed against the first version
of this rule (4674c97) were also each verified to get through that version's
validator, whose rule they defeated.
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.environ.get("TEAMSTER_FIGURE_PLANT_VALIDATOR") or os.path.join(ROOT, "tools", "validate_repo.py")
EXPECT_REFUSAL = not os.environ.get("TEAMSTER_FIGURE_PLANT_VALIDATOR")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
COMPANIONS = os.path.join(TEAMSTER, "Adapters", "Companions")
PRESENTER = os.path.join(COMPANIONS, "GunnarFigurePresenter.cs")
RUNTIME = os.path.join(COMPANIONS, "GunnarPresentation.cs")
HAULING = os.path.join(TEAMSTER, "Adapters", "Workers", "GunnarHaulingRuntime.cs")
SHARED_WORKERS = os.path.join(ROOT, "src", "Shared", "Workers")

LABEL = "[teamster] #458 figure audit"
SCOPE_313 = "[interop] #313 worker-runtime scope audit"

APPLIED = "        _dresser.Apply();\n"
COUNT = "    private bool HasHeadroom(Vector3 spot)"
DISPOSE = "        _leases.ReleaseWorker();\n    }\n\n    /// <summary>For <c>ct_haul status</c>.</summary>"
ARRIVED = "        BodiesChanged?.Invoke();\n        return \"Gunnar arrived at \""
EXTRACTION = ('NpcPresentationBody.TryExtract(\n'
              '            source, "Player", "CT_GunnarFigure", "CT_GunnarFigure_Source", string.Empty, _log)')


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class TeamsterFigureIsConfined(unittest.TestCase):
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
            self.assertEqual(0, code, "a #458 plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        if all(path != seen for seen, _ in self._restore):
            self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def after(self, path, marker, planted):
        self.swap_in(path, marker, marker + planted)

    def plant_file(self, root, relative, body):
        path = os.path.join(root, *relative)
        directory = os.path.dirname(path)
        if not os.path.isdir(directory):
            os.makedirs(directory)
            self._planted.append(directory)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)
        self._planted.append(path)

    def assert_refused(self, why, rule=LABEL, old_rule=False):
        code, out = validate()
        if EXPECT_REFUSAL or old_rule:
            self.assertNotEqual(0, code, why + "\n" + out[-3000:])
            self.assertIn(rule, out, why)
        else:
            # The validator without the rule: the plant must get through, or it
            # is not an escape this rule is the one to catch.
            self.assertEqual(0, code, "already refused without the #458 rule: " + why + "\n" + out[-3000:])

    # --- one file names the figure library ------------------------------------------------------

    def test_an_extraction_from_another_companion_file_is_refused(self):
        self.plant_file(COMPANIONS, ("ZzFigureEscape.cs",),
                        "namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;\n"
                        "internal static class ZzFigureEscape\n"
                        "{\n"
                        "    internal static object Run(UnityEngine.GameObject source) =>\n"
                        "        TheConcernedCat.ConcernedNPC.Body.NpcPresentationBody.TryExtract(\n"
                        "            source, \"Player\", \"X\", \"Y\", string.Empty, null);\n"
                        "}\n")
        self.assert_refused("a second Teamster file built a figure")

    def test_the_allowance_does_not_follow_a_basename_to_another_folder(self):
        self.plant_file(TEAMSTER, ("Ui", "Companions", "GunnarFigurePresenter.cs"),
                        "namespace TheConcernedCat.ConcernedTeamster.Ui.Companions;\n"
                        "using TheConcernedCat.ConcernedNPC.Body;\n"
                        "internal static class ZzSameName\n"
                        "{\n"
                        "    internal static object Run(UnityEngine.GameObject source, System.Action<string> _log) =>\n"
                        "        " + EXTRACTION + ";\n"
                        "}\n")
        self.assert_refused("a same-named file in another folder inherited the presenter's allowance")

    def test_a_method_group_in_the_runtime_is_refused(self):
        self.swap_in(RUNTIME, COUNT,
                     "    private static readonly object Extract = (System.Func<UnityEngine.GameObject?, string, string, "
                     "string, string, System.Action<string>?, TheConcernedCat.ConcernedNPC.Body.NpcPresentationFigure>)"
                     "TheConcernedCat.ConcernedNPC.Body.NpcPresentationBody.TryExtract;\n\n" + COUNT)
        self.assert_refused("a method group carried the extraction out of the pinned file")

    def test_the_worker_dresser_is_refused_even_in_the_worker_runtime(self):
        self.swap_in(HAULING, ARRIVED,
                     "        _ = nameof(TheConcernedCat.ConcernedNPC.Body.Dressing.NpcWorkerDresser.TryDress);\n" + ARRIVED)
        self.assert_refused("Gunnar's worker body was dressed without an owner decision")

    def test_a_shared_tree_naming_the_dresser_is_refused(self):
        self.plant_file(SHARED_WORKERS, ("ZzFigureName.cs",),
                        "namespace TheConcernedCat.Workers;\n"
                        "internal static class ZzFigureName\n"
                        "{\n"
                        "    internal const string Name = nameof(TheConcernedCat.ConcernedNPC.Body.Dressing.NpcFigureDresser);\n"
                        "}\n")
        self.assert_refused("a shared tree Teamster compiles in named the figure dresser")

    # --- every figure call in that file is pinned ----------------------------------------------

    def test_a_second_dresser_is_refused(self):
        self.after(PRESENTER, APPLIED,
                   "        var second = new NpcFigureDresser(figure, look, \"CT_GunnarFigure_Look\", _log);\n")
        self.assert_refused("a second dresser was built for one figure")

    def test_a_stand_with_another_argument_is_refused(self):
        self.after(PRESENTER, APPLIED, "        _dresser.StandAt(spot + Vector3.up * 3f, facing);\n")
        self.assert_refused("an unpinned stand moved the figure")

    def test_a_second_take_down_is_refused(self):
        self.after(PRESENTER, APPLIED, "        _dresser.TakeDown();\n")
        self.assert_refused("an unpinned take-down skipped the lease order")

    # --- that file writes nothing to the engine itself -----------------------------------------

    def test_a_collider_added_to_the_figure_is_refused(self):
        self.after(PRESENTER, APPLIED, "        source!.AddComponent<UnityEngine.CapsuleCollider>();\n")
        self.assert_refused("the figure grew a collider of Teamster's own")

    def test_a_pose_write_through_the_figures_root_is_refused(self):
        # Spelled through a local, so the CT-028 no-teleport audit's
        # `transform.position =` token does not already catch it.
        self.after(PRESENTER, APPLIED, "        var root = figure.Root!.transform;\n        root.localScale = spot;\n")
        self.assert_refused("the figure was resized past the library")

    def test_a_network_view_lookup_is_refused(self):
        self.after(PRESENTER, APPLIED, "        var view = source!.GetComponent<ZNetView>();\n")
        self.assert_refused("the figure file reached for a network view")

    def test_reflection_is_refused(self):
        self.after(PRESENTER, APPLIED, "        typeof(NpcFigureDresser).GetMethod(\"TakeDown\");\n")
        self.assert_refused("reflection reached a library member past the pins")

    def test_code_hidden_in_an_interpolated_string_is_refused(self):
        self.after(PRESENTER, APPLIED, "        _log($\"{UnityEngine.Object.Instantiate(source!)}\");\n")
        self.assert_refused("an interpolation hole instantiated past the string blanking")

    # --- escapes the first independent review constructed against 4674c97 ----------------------

    def test_a_generic_instantiation_is_refused(self):
        # Of the game's own player prefab: a live Player, the thing CLAUDE.md forbids.
        self.after(PRESENTER, APPLIED, "        UnityEngine.Object.Instantiate<GameObject>(source!);\n")
        self.assert_refused("a type argument carried an instantiation past the pattern")

    def test_a_char_literal_cannot_hide_code_from_the_scan(self):
        self.after(PRESENTER, APPLIED,
                   "        const char Q = '\"'; var view = source!.GetComponent<ZNetView>(); _log(\"x\");\n")
        self.assert_refused("a char literal paired the quotes wrongly and hid a network-view lookup")

    def test_a_verbatim_string_cannot_hide_code_from_the_scan(self):
        self.after(PRESENTER, APPLIED,
                   "        string at = @\"C:\\\"; var view = source!.GetComponent<ZNetView>(); _log(\"x\");\n")
        self.assert_refused("a verbatim string's backslash hid a network-view lookup")

    def test_a_new_object_in_the_figure_file_is_refused(self):
        self.after(PRESENTER, APPLIED, "        var extra = new GameObject(\"CT_GunnarExtra\");\n")
        self.assert_refused("the figure file built an object of its own")

    def test_a_target_typed_second_dresser_is_refused(self):
        self.after(PRESENTER, APPLIED,
                   "        NpcFigureDresser second = new(figure, look, \"CT_GunnarFigure_Look\", _log);\n")
        self.assert_refused("a target-typed construction built a second dresser unseen by the count")

    def test_a_primitive_in_the_companion_layer_is_refused(self):
        self.plant_file(COMPANIONS, ("ZzPrimitive.cs",),
                        "namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;\n"
                        "internal static class ZzPrimitive\n"
                        "{\n"
                        "    internal static UnityEngine.GameObject Run() =>\n"
                        "        UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Capsule);\n"
                        "}\n")
        self.assert_refused("a solid primitive was built beside the figure")

    def test_finding_and_moving_the_figure_from_the_runtime_is_refused(self):
        self.swap_in(RUNTIME, DISPOSE,
                     "        _leases.ReleaseWorker();\n        GameObject.Find(\"CT_GunnarFigure\").transform"
                     ".SetPositionAndRotation(Vector3.zero, Quaternion.identity);\n    }\n\n"
                     "    /// <summary>For <c>ct_haul status</c>.</summary>")
        self.assert_refused("the runtime found the figure by name and moved it")

    def test_a_body_builder_in_the_companion_layer_is_refused(self):
        self.plant_file(COMPANIONS, ("ZzBodies.cs",),
                        "namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;\n"
                        "internal static class ZzBodies\n"
                        "{\n"
                        "    internal const string Name = nameof(TheConcernedCat.ConcernedNPC.Body.NpcWorldBodies);\n"
                        "}\n")
        self.assert_refused("the companion layer reached for the library's body builders")

    def test_the_shared_companions_tree_is_in_scope(self):
        self.plant_file(os.path.join(ROOT, "src", "Shared", "Companions"), ("ZzFigureName.cs",),
                        "namespace TheConcernedCat.Companions;\n"
                        "internal static class ZzFigureName\n"
                        "{\n"
                        "    internal const string Name = \"NpcFigureDresser\";\n"
                        "}\n")
        self.assert_refused("the shared tree the project file compiles in but the audit skipped")

    # --- the rest of the companion layer builds nothing ----------------------------------------

    def test_an_activation_in_the_companion_runtime_is_refused(self):
        self.swap_in(RUNTIME, DISPOSE,
                     "        _leases.ReleaseWorker();\n        Game.instance.gameObject.SetActive(false);\n    }\n\n"
                     "    /// <summary>For <c>ct_haul status</c>.</summary>")
        self.assert_refused("the companion runtime switched an object on or off")

    def test_an_instantiation_in_the_companion_layer_is_refused(self):
        self.plant_file(COMPANIONS, ("ZzBuilder.cs",),
                        "namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;\n"
                        "internal static class ZzBuilder\n"
                        "{\n"
                        "    internal static UnityEngine.GameObject Run(UnityEngine.GameObject source) =>\n"
                        "        UnityEngine.Object.Instantiate(source);\n"
                        "}\n")
        self.assert_refused("the companion layer built an object of its own")

    # --- the #313 audit still refuses component surgery in the worker runtime -------------------

    def test_component_surgery_in_the_worker_runtime_is_still_refused_by_313(self):
        self.swap_in(HAULING, ARRIVED, "        body.gameObject.SetActive(true);\n" + ARRIVED)
        self.assert_refused("component surgery in Adapters/Workers outside the prefab factory",
                            rule=SCOPE_313, old_rule=True)


if __name__ == "__main__":
    unittest.main()
