"""Destructive mutation tests for the #400 Foreman runtime capability audit.

Each test changes the real source tree, runs the real repository validator, and
restores the exact original text. The useful claim is that the validator rejects
the planted program, including the same-change test-stub escape from #400; these
are not tests of isolated regular-expression helpers.
"""
import os
import shutil
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
FOREMAN = os.path.join(ROOT, "src", "ConcernedForeman")
RUNTIME = os.path.join(FOREMAN, "Runtime")
BUILD_POSE = os.path.join(RUNTIME, "Construction", "BuildPose.cs")
PLACER = os.path.join(RUNTIME, "Construction", "WorldPiecePlacer.cs")
PICKUP = os.path.join(RUNTIME, "Collection", "WorldSourcePickupPort.cs")
STUBS = os.path.join(ROOT, "src", "ConcernedForeman.Tests", "VanillaStubs.cs")


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class ForemanRuntimeCapabilitiesArePinned(unittest.TestCase):
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
        self.assertEqual(0, code, "a #400 plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def after(self, path, marker, planted):
        self.swap_in(path, marker, marker + "\n" + planted)

    def plant_file(self, relative, body):
        path = os.path.join(RUNTIME, *relative)
        directory = os.path.dirname(path)
        if not os.path.isdir(directory):
            os.makedirs(directory)
            self._planted.append(directory)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)
        self._planted.append(path)

    def assert_refused(self, why):
        code, out = validate()
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn("[foreman] #400 runtime capability audit", out, why)

    def test_same_change_stub_edit_cannot_enable_set_trigger(self):
        stub = "    public void SetFloat(int hash, float value) => Floats[hash] = value;"
        self.after(STUBS, stub, "\n    public void SetTrigger(int hash) { }")
        call = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, call, "        animation.SetTrigger(ForwardSpeed);")
        self.assert_refused("adding SetTrigger to the test double and using it stayed green")

    def test_ownership_takeover_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker, "        ((dynamic)body).SetOwner(1L);")
        self.assert_refused("a worker runtime took ownership")

    def test_teleport_is_refused_even_when_split_across_lines(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker,
                   "        ((dynamic)body).\n            Teleport(UnityEngine.Vector3.zero);")
        self.assert_refused("whitespace hid a worker teleport")

    def test_force_write_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker,
                   "        ((dynamic)body).AddForce(UnityEngine.Vector3.up);")
        self.assert_refused("a worker runtime injected force")

    def test_velocity_write_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker,
                   "        body.GetComponent<UnityEngine.Rigidbody>().linearVelocity = UnityEngine.Vector3.up;")
        self.assert_refused("a worker runtime wrote velocity")

    def test_position_write_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker,
                   "        body.transform.position = UnityEngine.Vector3.zero;")
        self.assert_refused("a worker runtime wrote position")

    def test_item_prefab_instantiation_is_refused(self):
        marker = (
            "        foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))\n"
            "        {")
        self.after(BUILD_POSE, marker,
                   "            UnityEngine.Object.Instantiate(item.m_dropPrefab);")
        self.assert_refused("an item drop was spawned through Object.Instantiate")

    def test_another_drop_call_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker,
                   "        ((dynamic)body.Humanoid).DropItem(body.Inventory, null, 1);")
        self.assert_refused("an unpinned item-drop path appeared")

    def test_arbitrary_rpc_is_refused(self):
        marker = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, marker, "        ((dynamic)body.View).InvokeRPC(\"swing\");")
        self.assert_refused("a worker runtime sent an arbitrary RPC")

    def test_place_piece_must_keep_do_attack_false(self):
        self.swap_in(
            PLACER,
            "player.PlacePiece(piece, at, facing, doAttack: false, cheated: false);",
            "player.PlacePiece(piece, at, facing, doAttack: true, cheated: false);")
        self.assert_refused("PlacePiece enabled vanilla's internal SetTrigger branch")

    def test_place_piece_must_keep_cheated_false(self):
        self.swap_in(
            PLACER,
            "player.PlacePiece(piece, at, facing, doAttack: false, cheated: false);",
            "player.PlacePiece(piece, at, facing, doAttack: false, cheated: true);")
        self.assert_refused("PlacePiece marked a paid piece as cheated")

    def test_authorized_pickup_cannot_change_receiver(self):
        self.swap_in(
            PICKUP,
            "added = humanoid.Pickup(itemDrop.gameObject, autoequip: false, autoPickupDelay: false);",
            "added = Player.m_localPlayer.Pickup(itemDrop.gameObject, autoequip: false, autoPickupDelay: false);")
        self.assert_refused("the authorized pickup moved from the worker to the player")

    def test_an_authorized_call_is_authorized_once_not_twice(self):
        call = "        animation.SetFloat(ForwardSpeed, 0f);"
        self.after(BUILD_POSE, call, call)
        self.assert_refused("a second copy inherited an existing allowance")

    def test_allowance_does_not_follow_a_basename_to_another_directory(self):
        self.plant_file(
            ("ZzEscape", "BuildPose.cs"),
            "internal static class ZzEscape\n"
            "{\n"
            "    private const int ForwardSpeed = 1;\n"
            "    internal static void Run(dynamic animation)\n"
            "    {\n"
            "        animation.SetFloat(ForwardSpeed, 0f);\n"
            "    }\n"
            "}\n")
        self.assert_refused("a same-named file in another directory inherited the allowance")

    def test_success_sentence_states_the_text_audits_limits(self):
        code, out = validate()
        self.assertEqual(0, code)
        self.assertIn("source-text audit strips comments", out)
        self.assertIn("does not follow indirection or prove control flow", out)


if __name__ == "__main__":
    unittest.main()
