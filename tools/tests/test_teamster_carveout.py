"""Negative tests for the owner-authorized Gunnar pickup carve-out.

The owner granted one capability, `Pickable.Interact`, in one file, on the
condition that the narrowness be proved by tests rather than asserted. The first
version was proved interactively and the proof was written down as prose, which
is not a test: an independent review then got a banned cart interaction into
Teamster with the whole gate green, four different ways.

Each test below is one of those ways. They plant a real violation into the real
tree, run the real validator, and require it to refuse - then remove the plant.
Testing the matching helpers alone would not do: three of the four escapes were
in the wiring, not the matching.
"""
import os
import shutil
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
WORKERS = os.path.join(ROOT, "src", "ConcernedTeamster", "Adapters", "Workers")
PORT = os.path.join(WORKERS, "GunnarCollectionPort.cs")
HAULING = os.path.join(WORKERS, "GunnarHaulingRuntime.cs")

STUB = """namespace TheConcernedCat.ConcernedTeamster.Zz;

internal sealed class ZzCarveoutProbe
{
    internal void Probe(object cart, object who)
    {
%s
    }
}
"""


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class CarveOutIsNarrow(unittest.TestCase):
    """Every way an independent review got past the first version."""

    def setUp(self):
        code, _ = validate()
        self.assertEqual(0, code, "the tree must be clean before a plant means anything")
        self._planted = []

    def tearDown(self):
        for path in self._planted:
            if os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
            elif os.path.exists(path):
                os.remove(path)
        if self._restore is not None:
            with open(self._restore_path, "w", encoding="utf-8", newline="") as handle:
                handle.write(self._restore)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    _restore = None
    _restore_path = PORT

    def plant_file(self, path, body):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(STUB % body)
        self._planted.append(path)

    def edit_port(self, extra):
        with open(PORT, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = PORT
        marker = "        source.Interact(_worker, repeat: false, alt: false);"
        self.assertEqual(1, original.count(marker), "the authorized call moved")
        with open(PORT, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(marker, marker + "\n" + extra))

    def swap_in(self, path, old, new):
        """Replaces one exact piece of a real source file. For defects that are
        not an extra call but the wrong one, or a guard in the wrong place."""
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = path
        self.assertEqual(1, original.count(old), "the pinned text moved: " + old)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def swap_in_port(self, old, new):
        self.swap_in(PORT, old, new)

    def assert_refused(self, why, marker="#313"):
        code, out = validate()
        self.assertNotEqual(0, code, why + "\n" + out[-2000:])
        self.assertIn(marker, out, why)

    def test_a_space_before_the_paren_does_not_hide_a_cart_interaction(self):
        # `.Interact(` never appears in `Interact (`, and the Release build is
        # happy either way, so this shipped.
        self.plant_file(os.path.join(WORKERS, "ZzSpaced.cs"),
                        "        ((dynamic)cart).Interact (who, false, false);")
        self.assert_refused("a space before the paren got a cart interaction past the audit")

    def test_a_newline_between_receiver_and_member_does_not_hide_it(self):
        # The break goes between the dot and the member. Splitting before
        # the dot leaves `.Interact(` intact on the second line, which the
        # old substring match caught - so that plant tests nothing.
        self.plant_file(os.path.join(WORKERS, "ZzSplit.cs"),
                        "        ((dynamic)cart).\n            Interact(who, false, false);")
        self.assert_refused("a line split got a cart interaction past the audit")

    def test_the_allowance_does_not_follow_the_file_name_to_another_directory(self):
        # The exception keyed on the basename, so any file so named inherited it.
        self.plant_file(os.path.join(WORKERS, "Extra", "GunnarCollectionPort.cs"),
                        "        ((dynamic)cart).Interact(who, false, false);")
        self.assert_refused("a second file of the same name inherited the allowance")

    def test_the_allowance_is_the_pickup_call_and_not_any_interact(self):
        # The owner authorized Pickable.Interact and said not to weaken the
        # cart-interaction guard; the first version allowed `.Interact(` on
        # anything inside the authorized file.
        self.edit_port("        ((dynamic)_cart).Interact(_worker, repeat: false, alt: false);")
        self.assert_refused("a cart interaction passed inside the authorized file")

    def test_a_comment_marker_inside_a_string_does_not_blind_the_line(self):
        self.plant_file(os.path.join(WORKERS, "ZzUrl.cs"),
                        '        string wiki = "https://example.invalid";'
                        ' ((dynamic)cart).Interact(who, false, false);')
        self.assert_refused("a // inside a string truncated the line and hid the call")

    def test_reflection_outside_workers_is_not_claimed_to_be_audited(self):
        # Not a plant: reflection outside Adapters/Workers genuinely is not
        # audited, and Teamster uses it legitimately in ten files. What must not
        # happen is the summary claiming otherwise, which is how a reader comes
        # to believe a guarantee that does not exist.
        code, out = validate()
        self.assertEqual(0, code)
        self.assertIn("reflection elsewhere in Teamster is not audited", out)

    # -- the two lifecycle verbs, now that the port has callers (#381) --
    #
    # Not an escape a review found in the audit: a defect a review found in the
    # port itself, and the one the merge commit for main says must not be got
    # backwards. `Forget()` is a JOB ending and must KEEP the record of what was
    # picked, because the source still exists and is still inside the window
    # where vanilla has dropped its items but not yet marked it picked. Only
    # `ForgetWorld()` may drop that record. Crossed, `begin - pick - forget -
    # begin` on one source yields a second full load out of nothing.

    def test_the_job_verb_may_not_forget_the_world(self):
        self.swap_in_port(
            "        Release();\n        _accounting.ForgetJob();",
            "        Release();\n        _accounting.ForgetWorld();")
        self.assert_refused(
            "a cancelled job wiping the unconfirmed-source record re-opens the mint",
            marker="#381")

    # -- the retire verb may not delete what a worker is carrying (#381) --
    #
    # Retiring a body destroys its network object, and a character's inventory
    # lives in that object: nothing is dropped. Harmless until an ordered pick
    # could put a stone into Gunnar. The decision is unit-tested; what a unit
    # test cannot reach is the call site, because the runtime binds Unity, so the
    # audit pins the guard above every removal and this plant moves it below.

    def test_a_body_removal_may_not_sit_above_the_carried_material_guard(self):
        self.swap_in(
            HAULING,
            "            int pointedHolds = ItemsHeldBy(pointed, out bool pointedReadable);\n"
            "            RetireVerdict pointedVerdict = WorkerRetirement.Decide(forced, pointedHolds, pointedReadable);\n"
            "            if (!WorkerRetirement.Allows(pointedVerdict))\n"
            "            {\n"
            "                return WorkerRetirement.Describe(pointedVerdict, pointedHolds);\n"
            "            }\n"
            "\n"
            "            _persistedBodies.Remove(view.GetZDO().m_uid);\n"
            "            view.Destroy();",
            "            _persistedBodies.Remove(view.GetZDO().m_uid);\n"
            "            view.Destroy();\n"
            "            int pointedHolds = ItemsHeldBy(pointed, out bool pointedReadable);\n"
            "            RetireVerdict pointedVerdict = WorkerRetirement.Decide(forced, pointedHolds, pointedReadable);\n"
            "            if (!WorkerRetirement.Allows(pointedVerdict))\n"
            "            {\n"
            "                return WorkerRetirement.Describe(pointedVerdict, pointedHolds);\n"
            "            }")
        self.assert_refused(
            "a body was destroyed before anything asked what it was carrying",
            marker="#381")

    def test_a_removal_may_not_escape_the_audit_by_using_the_other_spelling(self):
        # The second way out of the world, which the no-argument `Destroy()`
        # pattern cannot see: `ZNetScene.Destroy(go)` resets the network object,
        # destroys the ZDO and then the GameObject - body gone, inventory gone.
        # Planted in a helper it made five removal sites while the rule reported
        # four and passed at exit 0. The population of destruction-shaped calls
        # is what catches it now.
        with open(HAULING, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = HAULING
        anchor = "    private static int ItemsHeldBy(TeamsterWorkerAI? ai, out bool readable)"
        self.assertEqual(1, original.count(anchor), "the helper anchor moved")
        with open(HAULING, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(
                anchor,
                "    private static void Sweep(GameObject body)\n"
                "    {\n"
                "        ZNetScene.instance.Destroy(body);\n"
                "    }\n\n" + anchor,
                1))
        self.assert_refused(
            "ZNetScene.Destroy(go) took a body out of the world with the audit still green",
            marker="#381")

    def test_a_guard_that_is_consulted_and_ignored_does_not_count(self):
        # A bare `WorkerRetirement.Allows(v)` in a log line sits above the
        # removal just as well as a refusal does, and the counts stayed at
        # 2/2/2 while the carrying body was retired regardless of the verdict.
        # The rule now wants the refusing `if (!...)` shape. It is still not
        # control flow, and the summary line says so.
        self.swap_in(
            HAULING,
            "            if (!WorkerRetirement.Allows(pointedVerdict))\n"
            "            {\n"
            "                return WorkerRetirement.Describe(pointedVerdict, pointedHolds);\n"
            "            }",
            "            _log.LogInfo(\"verdict: \" + WorkerRetirement.Allows(pointedVerdict));")
        self.assert_refused(
            "a guard that is consulted and then ignored counted as a guard",
            marker="#381")

    def test_a_removal_may_not_escape_the_audit_by_moving_to_another_file(self):
        # The other half of the same escape: scoping the sweep to one file would
        # leave "put the helper next door" open. The population of removals
        # across the worker folder is pinned per file, so a new one anywhere is a
        # deliberate edit to the rule.
        self.plant_file(os.path.join(WORKERS, "ZzRemover.cs"),
                        "        ((dynamic)cart).Destroy();")
        self.assert_refused(
            "a body removal appeared in a file the rule does not account for",
            marker="#381")

    def test_a_removal_may_not_escape_the_audit_by_moving_into_a_helper(self):
        # The escape an independent review walked through: the removal stays in
        # the file but steps outside the text the rule reads, the count drops to
        # one, and the success line asserts a guarantee that is false. The method
        # boundary was where the first version stopped looking.
        self.swap_in(
            HAULING,
            "            _persistedBodies.Remove(view.GetZDO().m_uid);\n"
            "            view.Destroy();",
            "            _persistedBodies.Remove(view.GetZDO().m_uid);\n"
            "            RemoveBodyNow(view);")
        with open(HAULING, encoding="utf-8-sig") as handle:
            moved = handle.read()
        anchor = "    private static int ItemsHeldBy(TeamsterWorkerAI? ai, out bool readable)"
        self.assertEqual(1, moved.count(anchor), "the helper anchor moved")
        with open(HAULING, "w", encoding="utf-8", newline="") as handle:
            handle.write(moved.replace(
                anchor,
                "    private static void RemoveBodyNow(ZNetView view) { view.Destroy(); }\n\n" + anchor,
                1))
        self.assert_refused(
            "a removal lifted one line into a helper left the retire verb unguarded",
            marker="#381")

    def test_the_retire_verb_may_not_stop_asking_what_a_body_holds(self):
        self.swap_in(
            HAULING,
            "            RetireVerdict pointedVerdict = WorkerRetirement.Decide(forced, pointedHolds, pointedReadable);\n"
            "            if (!WorkerRetirement.Allows(pointedVerdict))",
            "            RetireVerdict pointedVerdict = RetireVerdict.MayRetire;\n"
            "            if (!WorkerRetirement.Allows(pointedVerdict))")
        self.assert_refused(
            "the retire verb stopped counting what a body holds on one of its paths",
            marker="#381")

    def test_the_world_verb_may_not_merely_forget_the_job(self):
        self.swap_in_port(
            "        Release();\n        _accounting.ForgetWorld();",
            "        Release();\n        _accounting.ForgetJob();")
        self.assert_refused(
            "a world going away must drop the record; keeping it refuses picks of whatever "
            "inherits those ids in the next world",
            marker="#381")


if __name__ == "__main__":
    unittest.main()
