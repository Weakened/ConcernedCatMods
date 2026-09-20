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
            with open(PORT, "w", encoding="utf-8", newline="") as handle:
                handle.write(self._restore)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    _restore = None

    def plant_file(self, path, body):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(STUB % body)
        self._planted.append(path)

    def edit_port(self, extra):
        with open(PORT, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        marker = "        source.Interact(_worker, repeat: false, alt: false);"
        self.assertEqual(1, original.count(marker), "the authorized call moved")
        with open(PORT, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(marker, marker + "\n" + extra))

    def swap_in_port(self, old, new):
        """Replaces one exact piece of the port. For the lifecycle verbs, whose
        defect is not an extra call but the wrong one."""
        with open(PORT, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self.assertEqual(1, original.count(old), "the pinned text moved: " + old)
        with open(PORT, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

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
