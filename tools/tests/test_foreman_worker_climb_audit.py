"""Planted violations for #334's worker ladder motor (owner decision 2026-11-01, DECISIONS.md D14.6).

An explicitly ordered Thorstein may have his own body moved along a checked Concerned Ladders
path by one scoped motor, Runtime/Ladders/WorkerClimbMotor.cs, with vanilla's ground motor
suspended. Two validator rules hold that grant to its shape:

* the #400 runtime capability audit, which now spells `isKinematic` beside the other physics
  writes and pins the motor's two kinematic writes and its one position write; and
* the #334 worker climb audit (check_foreman_worker_climb_scope), which pins the sweep in front of
  the position write, the worker's own rigidbody, who may create, request, start and end a lease,
  and ForemanWorkerAI's ground-motor suspension.

Each test plants an escape in the real source tree, runs the real validator and requires the
named audit to refuse it. It then runs a copy of the validator with exactly that rule removed and
requires the same plant to pass: the proof that the escape existed before the rule and that no
other rule catches it. tearDown restores every file byte for byte and re-runs the clean validator.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
FOREMAN = os.path.join(ROOT, "src", "ConcernedForeman")
RUNTIME = os.path.join(FOREMAN, "Runtime")
MOTOR = os.path.join(RUNTIME, "Ladders", "WorkerClimbMotor.cs")
CLIMB_RUNTIME = os.path.join(RUNTIME, "Ladders", "WorkerClimbRuntime.cs")
AI = os.path.join(RUNTIME, "Settlement", "ForemanWorkerAI.cs")
BUILD_POSE = os.path.join(RUNTIME, "Construction", "BuildPose.cs")
PANEL = os.path.join(FOREMAN, "Ui", "CollectionOrderPanel.cs")
CONSTRUCTION_PLANT = os.path.join(RUNTIME, "Construction", "ZzPlanted334.cs")
SHARED_PLANT = os.path.join(ROOT, "src", "Shared", "Workers", "Traversal", "ZzPlanted334.cs")

SWEEP = ("        if (!_clear(from, position))\n"
         "        {\n"
         "            return false;\n"
         "        }\n"
         "\n")
WRITE = "        _body.position = target;\n"
SUSPENSION = ("        if (_climbLease != null)\n"
              "        {\n"
              "            return true;\n"
              "        }\n"
              "\n"
              "        _planner.BeginTick();")


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class _Plants(unittest.TestCase):
    PHRASE = ""

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
        self.assertEqual(0, code, "a #334 plant was left behind:\n" + out[-3000:])

    def _read(self, path):
        with open(path, "rb") as handle:
            raw = handle.read()
        self._restore.append((path, raw))
        return raw.decode("utf-8-sig"), raw.startswith(b"\xef\xbb\xbf")

    def _write(self, path, text, bom):
        with open(path, "wb") as handle:
            handle.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))

    def swap_in(self, path, old, new):
        text, bom = self._read(path)
        self.assertEqual(1, text.count(old), "pinned text moved: " + old)
        self._write(path, text.replace(old, new, 1), bom)

    def append_to(self, path, extra):
        text, bom = self._read(path)
        self._write(path, text + extra, bom)

    def plant_file(self, path, body):
        self.assertFalse(os.path.exists(path), "plant would overwrite a real file: " + path)
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)

    def without_rule(self):
        raise NotImplementedError

    def _validator_copy(self, name, replacements):
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        for old, new in replacements:
            self.assertEqual(1, source.count(old), "the rule moved in the validator: " + old)
            source = source.replace(old, new, 1)
        path = os.path.join(ROOT, "tools", name)
        self.plant_file(path, source)
        return path

    def assert_refused(self, why):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        self.assertIn(self.PHRASE, out, why + ": refused, but not by " + self.PHRASE + "\n" + out[-3000:])
        code, out = run(self.without_rule())
        self.assertEqual(0, code, why + ": without the rule something else still refuses it, so the plant "
                         "does not prove that rule\n" + out[-3000:])


class KinematicWritesArePinned(_Plants):
    """#400's physics-write spelling gained isKinematic with #334."""

    PHRASE = "[foreman] #400 runtime capability audit"

    def without_rule(self):
        return self._validator_copy("_zz_without_kinematic_pins.py", (
            (r"linearVelocity|angularVelocity|useGravity|isKinematic)\s*[-+*/&|^]",
             r"linearVelocity|angularVelocity|useGravity)\s*[-+*/&|^]"),
            ('        ("Runtime/Ladders/WorkerClimbMotor.cs", _foreman_exact(r"\\blease\\s*\\.\\s*_body\\s*\\.\\s*'
             'isKinematic\\s*=\\s*true\\s*;"), 1),\n', ""),
            ('        ("Runtime/Ladders/WorkerClimbMotor.cs", _foreman_exact(r"\\b_body\\s*\\.\\s*isKinematic\\s*=\\s*'
             '_wasKinematic\\s*;"), 1),\n', ""),
        ))

    def test_a_kinematic_switch_elsewhere_in_the_runtime_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334Freeze\n{\n"
                       "    internal static void Freeze(UnityEngine.Rigidbody body) { body.isKinematic = true; }\n}\n")
        self.assert_refused("another runtime file froze a body's physics")

    # A kinematic write in a linked shared tree is already refused by another rule
    # with this one removed, so it proves nothing here. Outside Runtime/ is this
    # rule's own ground (#497 made the #400 scope the whole product).
    def test_a_kinematic_switch_in_the_ui_is_refused(self):
        self.append_to(PANEL, "\ninternal static class Zz334UiFreeze\n{\n"
                       "    internal static void Freeze(UnityEngine.Rigidbody body) { body.isKinematic = true; }\n}\n")
        self.assert_refused("the UI froze a body's physics")

    def test_a_lease_that_never_suspends_physics_is_refused(self):
        self.swap_in(MOTOR, "lease._body.isKinematic = true;", "lease._body.isKinematic = false;")
        self.assert_refused("the lease left gravity and contacts acting on the body it positions")

    def test_a_restore_that_leaves_the_body_frozen_is_refused(self):
        self.swap_in(MOTOR, "_body.isKinematic = _wasKinematic;", "_body.isKinematic = true;")
        self.assert_refused("restoring the body left its physics off")

    def test_a_second_restore_write_is_refused(self):
        self.swap_in(MOTOR, "_body.isKinematic = _wasKinematic;",
                     "_body.isKinematic = _wasKinematic;\n        _body.isKinematic = _wasKinematic;")
        self.assert_refused("a second copy inherited the pinned restore")


class WorkerClimbMotorIsConfined(_Plants):
    """The #334 worker climb audit."""

    PHRASE = "[foreman] #334 worker climb audit"

    def without_rule(self):
        return self._validator_copy("_zz_without_worker_climb.py", (
            ("    report.extend(check_foreman_worker_climb_scope(errors))\n", ""),
        ))

    # --- the one position write ----------------------------------------------------------------

    def test_a_write_without_its_sweep_is_refused(self):
        self.swap_in(MOTOR, SWEEP + WRITE, WRITE)
        self.assert_refused("the ladder motor wrote a position nothing swept")

    def test_a_sweep_after_the_write_is_refused(self):
        self.swap_in(MOTOR, SWEEP + WRITE, WRITE + "\n" + SWEEP.rstrip("\n") + "\n")
        self.assert_refused("the sweep ran after the body had already moved")

    def test_a_sweep_from_a_guessed_start_is_refused(self):
        self.swap_in(MOTOR, "var from = new WorkPoint(at.x, at.y, at.z);", "var from = position;")
        self.assert_refused("a zero-length sweep from the target vouched for every step")

    def test_another_objects_rigidbody_is_refused(self):
        self.swap_in(MOTOR, "Rigidbody body = ai.GetComponent<Rigidbody>();",
                     "Rigidbody body = Player.m_localPlayer.GetComponent<Rigidbody>();")
        self.assert_refused("the ladder motor took the player's body")

    def test_a_second_rigidbody_lookup_in_the_motor_is_refused(self):
        self.swap_in(MOTOR, "Rigidbody body = ai.GetComponent<Rigidbody>();",
                     "Rigidbody body = ai.GetComponent<Rigidbody>();\n"
                     "        Rigidbody other = ai.transform.parent.GetComponent<Rigidbody>();")
        self.assert_refused("the motor reached for a second body")

    # --- the ground-motor suspension -------------------------------------------------------------

    def test_sleeping_outside_a_lease_is_refused(self):
        self.swap_in(AI, "public override bool IsSleeping() => _climbLease != null;",
                     "public override bool IsSleeping() => true;")
        self.assert_refused("the worker's ground motor was switched off with no lease holding him")

    def test_another_sleeping_override_in_the_runtime_is_refused(self):
        self.plant_file(CONSTRUCTION_PLANT, "namespace Zz;\ninternal sealed class Zz334Sleeper : BaseAI\n{\n"
                        "    public override bool IsSleeping() => true;\n}\n")
        self.assert_refused("another AI switched a ground motor off")

    def test_a_sleeping_override_in_a_linked_shared_tree_is_refused(self):
        self.plant_file(SHARED_PLANT, "namespace Zz;\ninternal class Zz334SharedSleeper : Zz334Base\n{\n"
                        "    public override bool IsSleeping() => true;\n}\n")
        self.assert_refused("a shared tree Foreman compiles switched a ground motor off")

    def test_planning_while_leased_is_refused(self):
        self.swap_in(AI, SUSPENSION, "        _planner.BeginTick();")
        self.assert_refused("ForemanWorkerAI kept planning and calling MoveTo under a ladder lease")

    def test_a_job_goal_while_leased_is_refused(self):
        self.swap_in(AI, "if (_faulted || _climbLease != null || !(arrivalTolerance > 0f)",
                     "if (_faulted || !(arrivalTolerance > 0f)")
        self.assert_refused("a job could set a walk goal for a body on a ladder")

    # --- who may make, ask for, start and end a lease -------------------------------------------

    def test_a_lease_requested_from_another_file_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334Request\n{\n"
                       "    internal static object? Take(TheConcernedCat.ConcernedForeman.Runtime.Settlement.ForemanWorkerAI ai)"
                       " => TheConcernedCat.ConcernedForeman.Runtime.Ladders.WorkerClimbMotor.TrySuspend("
                       "ai, TheConcernedCat.Workers.WorkerKey.Thorstein, System.Guid.NewGuid(), (a, b) => true);\n}\n")
        self.assert_refused("construction asked for a ladder lease")

    def test_a_lease_request_without_the_live_sweep_is_refused(self):
        self.swap_in(CLIMB_RUNTIME, "_receiver.IsMoveClear)", "(a, b) => true)")
        self.assert_refused("the climb runtime handed the motor a sweep that always says clear")

    def test_a_lease_built_elsewhere_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334Build\n{\n"
                       "    internal static object Make(dynamic ai, dynamic body) => "
                       "new WorkerClimbMotor(ai, body, default, System.Guid.NewGuid(), null);\n}\n")
        self.assert_refused("a second place built a ladder lease")

    def test_a_lease_started_elsewhere_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334Start\n{\n"
                       "    internal static bool Grab(dynamic ai) => ai.TryBeginClimb(new object());\n}\n")
        self.assert_refused("a second place started a ladder lease on the body")

    def test_a_lease_ended_elsewhere_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334End\n{\n"
                       "    internal static bool Drop(dynamic ai, object lease) => ai.EndClimb(lease);\n}\n")
        self.assert_refused("a second place gave the body back under the lease")

    def test_a_route_tick_installed_elsewhere_is_refused(self):
        self.append_to(BUILD_POSE, "\ninternal static class Zz334Tick\n{\n"
                       "    internal static void Hook(dynamic ai) { ai.RouteTick = null; }\n}\n")
        self.assert_refused("a second place drove the body's route tick")

    def test_the_success_sentence_states_the_text_audits_limits(self):
        code, out = run(VALIDATOR)
        self.assertEqual(0, code)
        self.assertIn("[foreman] #334 worker climb audit", out)
        self.assertIn("does not follow delegates or prove control flow", out)


if __name__ == "__main__":
    unittest.main()
