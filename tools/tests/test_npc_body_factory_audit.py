"""Destructive mutation tests for the #372 body factory audit
(issue #372, CNPC-001; tools/validate_repo.py
check_npc_consumer_bodies_come_from_the_gated_build).

The owner's acceptance criterion, from the CNPC-000 re-review: "a factory that
ignores a disposed lease, and an instantiation outside the factory, must each
fail the gate." Each test below changes the real source tree, runs the real
repository validator, and restores the exact original bytes.

Every plant was verified to PASS the validator with this rule's one call removed
from main() - the copy without the rule - before being added, so each is an
escape this rule is the one to catch rather than one another audit already
refuses. That is why no instantiation plant here is in Foreman: #400 already
refuses an unpinned instantiation anywhere in Foreman and every shared tree it
compiles in, which covers every shared tree Steward and Teamster compile in too.
#400 does not refuse a network view put on a new object, so that plant is in
Foreman as well as Steward. Setting
NPC_BODY_PLANT_VALIDATOR to another validator's path re-runs that check,
expecting every plant to get through it.
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.environ.get("NPC_BODY_PLANT_VALIDATOR") or os.path.join(ROOT, "tools", "validate_repo.py")
EXPECT_REFUSAL = not os.environ.get("NPC_BODY_PLANT_VALIDATOR")
LABEL = "[interop] #372 body factory audit"

STEWARD = os.path.join(ROOT, "src", "ConcernedSteward")
STEWARD_PREFAB = os.path.join(STEWARD, "Runtime", "StewardWorkerPrefab.cs")
STEWARD_RUNTIME = os.path.join(STEWARD, "Runtime", "StewardRuntime.cs")
TEAMSTER_PREFAB = os.path.join(ROOT, "src", "ConcernedTeamster", "Adapters", "Workers", "TeamsterWorkerPrefab.cs")
FOREMAN_SETTLEMENT = os.path.join(ROOT, "src", "ConcernedForeman", "Runtime", "Settlement", "SettlementRuntime.cs")
FOREMAN_MARKER = "        WorkerBodyHandOver? handOver = TakeWorkerBody;\n"
FACTORY = os.path.join(ROOT, "src", "ConcernedNPC", "Body", "NpcWorkerPrefabFactory.cs")
GATE = os.path.join(ROOT, "src", "ConcernedNPC", "Body", "NpcBodyBuildGate.cs")

STEWARD_SPAWN = ("        GameObject? instance = NpcWorkerPrefabFactory.TrySpawnRoleBody(\n"
                 "            lease, StewardNpcRole.Contract, _prefab, position, rotation,\n"
                 "            body => StewardBody.TryStamp(body, key), out failure);\n")
STEWARD_MARKER = "        // The figure at home goes before her worker body appears: never both.\n"
TEAMSTER_INSTANTIATION = "        GameObject instance = UnityEngine.Object.Instantiate(Prefab, position, rotation);\n"
GATE_LEASE_CHECK = ("        if (!lease.IsActive)\n"
                    "        {\n"
                    "            return NpcBuildPermission.Refused(\n"
                    "                \"the lease for \" + lease.Identity + \" is no longer held, so permission to build "
                    "this body has gone\");\n"
                    "        }\n\n")
GATE_GRANT = "        return NpcBuildPermission.Granted();\n"
GATE_GROUND = "        if (!groundLoaded)\n"
FACTORY_REFUSAL = "        if (!permission.IsGranted)\n"
FACTORY_BUILD = "        GameObject instance = UnityEngine.Object.Instantiate(prefab!, position, rotation);\n"
FACTORY_GATE = ("        NpcBuildPermission permission = NpcBodyBuildGate.May(\n"
                "            lease, contract, tally, ready, prefabFailure, groundLoaded);\n")
FACTORY_DISCARD = "    private static void Discard(GameObject instance, ZNetView? view)\n"


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class BodiesComeOnlyFromTheGatedBuild(unittest.TestCase):
    def setUp(self):
        if EXPECT_REFUSAL:
            code, out = validate()
            self.assertEqual(0, code, "the tree must be clean before a plant means anything:\n" + out[-3000:])
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path, original in reversed(self._restore):
            with open(path, "wb") as handle:
                handle.write(original)
        for path in reversed(self._planted):
            if os.path.isfile(path):
                os.remove(path)
            elif os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
        if EXPECT_REFUSAL:
            code, out = validate()
            self.assertEqual(0, code, "a #372 plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, "rb") as handle:
            original = handle.read()
        text = original.decode("utf-8-sig")
        self.assertEqual(1, text.count(old), "pinned text moved: " + old)
        if all(path != seen for seen, _ in self._restore):
            self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(text.replace(old, new, 1))

    def plant_file(self, path, text):
        self.assertFalse(os.path.exists(path), "would overwrite " + path)
        directory = os.path.dirname(path)
        missing = []
        while not os.path.isdir(directory):
            missing.append(directory)
            directory = os.path.dirname(directory)
        for created in reversed(missing):
            os.mkdir(created)
            self._planted.append(created)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(text)
        self._planted.append(path)

    def assert_refused(self, why):
        code, out = validate()
        if EXPECT_REFUSAL:
            self.assertNotEqual(0, code, why + "\n" + out[-3000:])
            self.assertIn(LABEL, out, why)
        else:
            self.assertEqual(0, code, "already refused without the #372 rule: " + why + "\n" + out[-3000:])

    # ---- an instantiation outside the factory --------------------------------

    def test_a_consumer_that_instantiates_its_own_prefab_is_refused(self):
        # The exact bypass the re-review named: the role holds a prefab, calls
        # the engine directly, and never speaks to the arbiter.
        self.swap_in(STEWARD_PREFAB, STEWARD_SPAWN,
                     "        GameObject? instance = UnityEngine.Object.Instantiate(_prefab!, position, rotation);\n")
        self.assert_refused("the Steward's prefab was instantiated without a lease")

    def test_a_second_creation_path_beside_the_gated_one_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        UnityEngine.Object.Instantiate(_census.Live!.gameObject, at.Value, "
                                      "Quaternion.identity);\n")
        self.assert_refused("a second body was created in the runtime without the gate")

    def test_a_method_group_spells_no_call_and_is_still_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        Func<GameObject, Vector3, Quaternion, GameObject> make = "
                                      "UnityEngine.Object.Instantiate;\n")
        self.assert_refused("a method group reached the engine's instantiation")

    def test_a_generic_spelling_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        UnityEngine.Object.Instantiate<GameObject>(_census.Live!.gameObject);\n")
        self.assert_refused("a generic instantiation got past the call pattern")

    def test_a_saved_object_created_directly_is_refused(self):
        # The scene builds a body from a saved object on the next zone load,
        # so creating the object is creating the body - without the gate.
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        ZDOMan.instance.CreateNewZDO(at.Value, 0);\n")
        self.assert_refused("a CS_Steward saved object was created without the gate")

    def test_the_pending_teamster_site_cannot_grow(self):
        self.swap_in(TEAMSTER_PREFAB, TEAMSTER_INSTANTIATION,
                     TEAMSTER_INSTANTIATION + "        UnityEngine.Object.Instantiate(Prefab, position, rotation);\n")
        self.assert_refused("Teamster's one pending instantiation became two")

    def test_the_pending_allowance_does_not_follow_its_path_into_another_product(self):
        self.plant_file(
            os.path.join(STEWARD, "Adapters", "Workers", "TeamsterWorkerPrefab.cs"),
            "namespace Planted;\n\ninternal static class Planted\n{\n"
            "    internal static UnityEngine.GameObject Make(UnityEngine.GameObject Prefab, UnityEngine.Vector3 "
            "position, UnityEngine.Quaternion rotation)\n    {\n"
            "        return UnityEngine.Object.Instantiate(Prefab, position, rotation);\n    }\n}\n")
        self.assert_refused("Teamster's pending allowance was inherited by a Steward file of the same path")

    def test_a_consumer_that_stops_calling_the_gated_build_is_refused(self):
        # A spawn path with no caller is not an adoption.
        self.swap_in(STEWARD_PREFAB, STEWARD_SPAWN,
                     "        GameObject? instance = null;\n        failure = \"not built\";\n")
        self.assert_refused("the Steward no longer spawns through the gated build")

    # ---- a saved object made from a new GameObject (#372 re-review) ----------

    def test_a_network_view_added_to_a_new_object_is_refused(self):
        # A network view makes a saved object the scene keeps and rebuilds: a
        # body created without the gate, spelling no Instantiate at all.
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        new GameObject(\"CS_Steward\").AddComponent<ZNetView>();\n")
        self.assert_refused("a network view was put on a new object in the Steward")

    def test_a_qualified_network_view_added_in_foreman_is_refused(self):
        self.swap_in(FOREMAN_SETTLEMENT, FOREMAN_MARKER,
                     "        new GameObject(\"Thorstein\").AddComponent<global::ZNetView>();\n" + FOREMAN_MARKER)
        self.assert_refused("a network view was put on a new object in Foreman")

    def test_the_type_taking_overload_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        new GameObject(\"CS_Steward\").AddComponent(typeof(ZNetView));\n")
        self.assert_refused("a network view was added through the Type-taking overload")

    def test_an_alias_of_the_network_view_is_refused(self):
        self.plant_file(
            os.path.join(STEWARD, "Runtime", "PlantedSavedObject.cs"),
            "using UnityEngine;\nusing View = ZNetView;\n\nnamespace Planted;\n\ninternal static class Planted\n{\n"
            "    internal static void Make() => new GameObject(\"CS_Steward\").AddComponent<View>();\n}\n")
        self.assert_refused("a renamed network view was added to a new object")

    def test_a_generic_helper_that_adds_its_type_parameter_is_refused(self):
        self.plant_file(
            os.path.join(STEWARD, "Runtime", "PlantedSavedObject.cs"),
            "using UnityEngine;\n\nnamespace Planted;\n\ninternal static class Planted\n{\n"
            "    private static T Add<T>(GameObject g) where T : Component => g.AddComponent<T>();\n\n"
            "    internal static void Make() => Add<ZNetView>(new GameObject(\"CS_Steward\"));\n}\n")
        self.assert_refused("a generic helper added a network view")

    def test_a_subclass_of_the_network_view_is_refused(self):
        self.plant_file(
            os.path.join(STEWARD, "Runtime", "PlantedSavedObject.cs"),
            "using UnityEngine;\n\nnamespace Planted;\n\ninternal sealed class SavedView : ZNetView\n{\n}\n\n"
            "internal static class Planted\n{\n"
            "    internal static void Make() => new GameObject(\"CS_Steward\").AddComponent<SavedView>();\n}\n")
        self.assert_refused("a network view subclass was added to a new object")

    # Found by the exact-head review of 195e2d2: names the first rule's leading
    # word boundary missed, and a constructor that spells no AddComponent.

    def test_jotunns_get_or_add_component_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        new GameObject(\"CS_Steward\").GetOrAddComponent<ZNetView>();\n")
        self.assert_refused("Jotunn's GetOrAddComponent added a network view")

    def test_jotunns_add_component_copy_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        new GameObject(\"CS_Steward\").AddComponentCopy(_census.Live!.View);\n")
        self.assert_refused("a network view was copied onto a new object")

    def test_a_game_object_constructed_with_the_view_type_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        _ = new GameObject(\"CS_Steward\", typeof(ZNetView));\n")
        self.assert_refused("a GameObject was constructed with a network view")

    def test_a_target_typed_game_object_with_component_types_is_refused(self):
        self.plant_file(
            os.path.join(STEWARD, "Runtime", "PlantedSavedObject.cs"),
            "using System;\nusing UnityEngine;\n\nnamespace Planted;\n\ninternal static class Planted\n{\n"
            "    internal static GameObject Make(Type[] parts)\n    {\n"
            "        GameObject made = new(Name(), parts);\n        return made;\n    }\n\n"
            "    private static string Name() => \"CS_Steward\";\n}\n")
        self.assert_refused("a GameObject was constructed with component types it was handed")

    def test_an_asynchronous_instantiation_is_refused(self):
        self.swap_in(STEWARD_RUNTIME, STEWARD_MARKER,
                     STEWARD_MARKER + "        UnityEngine.Object.InstantiateAsync(_census.Live!.gameObject);\n")
        self.assert_refused("an asynchronous instantiation got past the word pattern")

    # ---- a factory that ignores a disposed lease -----------------------------

    def test_a_gate_that_never_asks_about_the_lease_is_refused(self):
        self.swap_in(GATE, GATE_LEASE_CHECK, "")
        self.assert_refused("the gate builds under a lease that has gone")

    def test_a_gate_that_asks_and_ignores_the_answer_is_refused(self):
        self.swap_in(GATE, "        if (!lease.IsActive)\n", "        if (!lease.IsActive && lease == null)\n")
        self.assert_refused("the lease check was consulted and could never refuse")

    def test_a_lease_check_that_is_no_longer_last_is_refused(self):
        self.swap_in(GATE, GATE_LEASE_CHECK + GATE_GRANT,
                     GATE_LEASE_CHECK + "        if (!groundLoaded)\n        {\n"
                                        "            return NpcBuildPermission.Refused(\"late\");\n        }\n\n"
                     + GATE_GRANT)
        self.assert_refused("a refusal after the lease check lets a stale lease's answer be older than the build")

    def test_an_early_grant_before_the_lease_is_asked_is_refused(self):
        self.swap_in(GATE, GATE_GROUND, "        if (tally.MaySpawn)\n        {\n"
                                        "            return NpcBuildPermission.Granted();\n        }\n\n" + GATE_GROUND)
        self.assert_refused("the gate grants before it reaches the lease")

    def test_a_factory_that_ignores_the_gates_refusal_is_refused(self):
        self.swap_in(FACTORY, FACTORY_REFUSAL, "        if (!permission.IsGranted && lease == null)\n")
        self.assert_refused("a disposed lease's refusal is read and not obeyed")

    def test_lifting_the_instantiation_into_a_helper_the_gate_never_sees_is_refused(self):
        self.swap_in(FACTORY, FACTORY_BUILD, "        GameObject instance = Make(prefab!, position, rotation);\n")
        self.swap_in(FACTORY, FACTORY_DISCARD,
                     "    private static GameObject Make(GameObject? prefab, Vector3 position, Quaternion rotation)\n"
                     "    {\n"
                     "        GameObject instance = UnityEngine.Object.Instantiate(prefab!, position, rotation);\n"
                     "        return instance;\n"
                     "    }\n\n" + FACTORY_DISCARD)
        self.assert_refused("the instantiation moved where no gate stands in front of it")

    def test_building_before_the_gate_is_asked_is_refused(self):
        self.swap_in(FACTORY, FACTORY_BUILD, "")
        self.swap_in(FACTORY, FACTORY_GATE, FACTORY_BUILD + FACTORY_GATE)
        self.assert_refused("the body is created before the lease is asked about")

    def test_a_second_gate_call_that_skips_the_lease_is_refused(self):
        # Asking the gate twice lets the second answer, not the one in front of
        # the build, decide; the population is one.
        self.swap_in(FACTORY, FACTORY_GATE,
                     "        _ = NpcBodyBuildGate.May(null, contract, tally, ready, prefabFailure, groundLoaded);\n"
                     + FACTORY_GATE)
        self.assert_refused("the factory asks the gate a second time")


if __name__ == "__main__":
    unittest.main()
