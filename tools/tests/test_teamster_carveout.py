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
COLLECTION_RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
ADAPTERS = os.path.join(TEAMSTER, "Adapters")
DOMAIN = os.path.join(TEAMSTER, "Domain")
UI = os.path.join(TEAMSTER, "Ui")

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
        # Files first, then the directories that held them: rmtree on a tree the
        # validator subprocess has just walked can lose a race and, with
        # ignore_errors, lose it silently - which is how an empty planted
        # directory survived a green run. An empty directory is invisible to
        # `git status`, so nothing downstream would have caught it either.
        for path in reversed(self._planted):
            if os.path.isfile(path):
                os.remove(path)
        for path in reversed(self._planted):
            if os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
                if os.path.isdir(path):
                    os.rmdir(path)
        if self._restore is not None:
            with open(self._restore_path, "w", encoding="utf-8", newline="") as handle:
                handle.write(self._restore)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    _restore = None
    _restore_path = PORT

    def plant_file(self, path, body):
        directory = os.path.dirname(path)
        # Remember a directory this test had to create, so tearDown takes it away
        # again. An empty directory is invisible to `git status`, so one left
        # behind is not caught by the usual end-of-run check.
        if not os.path.isdir(directory):
            self._planted.append(directory)
        os.makedirs(directory, exist_ok=True)
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

    def assert_refused(self, why, marker="#313", contains=None):
        # `marker` alone is a weak claim for the #381 audits: their SUCCESS line
        # carries the marker too, so a refusal by some unrelated rule would
        # satisfy it. `contains` names a phrase only the intended failure
        # message can produce.
        code, out = validate()
        if contains is not None:
            self.assertIn(contains, out, why + "\n" + out[-2000:])
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

    # -- the take, which is the call that actually moves the material --
    #
    # A review decompiled `Humanoid.Pickup` against the installed assembly: it
    # adds to `m_inventory` and then destroys the dropped item's network object
    # through `ZNetScene.instance.Destroy(go)`. So the enforcement pinned the
    # pick and left the TAKE unpinned, free to change its receiver or its
    # arguments, while the rule and three documents said "one pinned call". The
    # call itself predates this branch and is plainly inside what the owner
    # authorized; it was the claim that was too wide, not the code.

    def test_the_take_may_not_change_its_receiver_inside_the_authorized_file(self):
        self.swap_in_port(
            "            if (_worker.Pickup(dropped, autoequip: false, autoPickupDelay: false))",
            "            if (Player.m_localPlayer.Pickup(dropped, autoequip: false, autoPickupDelay: false))")
        self.assert_refused(
            "the take was re-pointed at the player and passed inside the authorized file",
            marker="#313")

    def test_the_take_is_not_allowed_in_another_worker_file(self):
        self.plant_file(os.path.join(WORKERS, "ZzTaker.cs"),
                        "        ((dynamic)who).Pickup(cart, autoequip: false, autoPickupDelay: false);")
        self.assert_refused("a take appeared in a file that was never authorized to have one",
                            marker="#313")

    def test_the_take_is_authorized_once_and_not_twice(self):
        self.swap_in_port(
            "            if (_worker.Pickup(dropped, autoequip: false, autoPickupDelay: false))\n",
            "            if (_worker.Pickup(dropped, autoequip: false, autoPickupDelay: false))\n"
            "            {\n"
            "                _worker.Pickup(dropped, autoequip: false, autoPickupDelay: false);\n"
            "            }\n")
        self.assert_refused("a second take in the port passed as the authorized one", marker="#313")

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

    def test_the_other_spelling_does_not_escape_by_moving_to_another_file_either(self):
        # The population is pinned PER FILE, so the previous test alone would
        # leave "put the sweep next door" open - exactly the escape the
        # no-argument pattern already had to close once. Proved separately
        # rather than assumed: a rule that happened to read only the hauling
        # runtime would pass the test above and fail this one.
        self.plant_file(os.path.join(WORKERS, "ZzSweeper.cs"),
                        "        ZNetScene.instance.Destroy((UnityEngine.GameObject)cart);")
        self.assert_refused(
            "ZNetScene.Destroy(go) in a file the rule does not account for stayed invisible",
            marker="#381")

    def test_a_destruction_may_not_change_what_it_is_routed_through(self):
        # The hole a second review proved in the fix above: a pinned POPULATION
        # catches an ADDED destruction and says nothing about a SUBSTITUTED one.
        # GunnarCollectionRuntime.cs destroys its own plugin component through
        # Unity's static, and expects zero body removals - so rewriting that one
        # call as the vanilla scene's removal of a body left every count in the
        # rule unchanged and the audit green, with a body and its inventory
        # leaving the world from a file that has no retirement guard anywhere
        # near it. Detected now by the one thing a text audit can read: which
        # receiver the destruction is routed through.
        self.swap_in(
            COLLECTION_RUNTIME,
            "            UnityEngine.Object.Destroy(runtime);",
            "            ZNetScene.instance.Destroy(runtime._worker()!.gameObject);")
        self.assert_refused(
            "a destruction re-routed from Unity's static to the vanilla scene left every count "
            "unchanged and the audit green",
            marker="#381")

    def test_a_newline_between_the_receiver_and_the_destruction_does_not_hide_it(self):
        # The same evasion `_audit_token` was rebuilt for, walking straight back
        # in through the receiver parser: C# lets the break go between the
        # receiver and the member, so a line ending in the dot read as a bare
        # static call. Every pinned count unchanged, gate green, a body and its
        # inventory out of the world from the file with no guard near it. This
        # branch had already paid for that lesson once for tokens and the new
        # rule did not inherit it.
        self.swap_in(
            COLLECTION_RUNTIME,
            "            UnityEngine.Object.Destroy(runtime);",
            "            ZNetScene.instance.\n"
            "                Destroy(runtime._worker()!.gameObject);")
        self.assert_refused(
            "a line break between the receiver and Destroy hid a routed destruction",
            marker="#381")

    def test_a_local_named_Object_is_not_trusted_as_the_type(self):
        # The other half: the receiver test is a name comparison, so a local
        # called `Object` inherited the static allowance. Refused rather than
        # documented - the unqualified spelling is not in the allowed set at all,
        # and `UnityEngine.Object.Destroy(x)` is what a real static destroy says.
        self.swap_in(
            COLLECTION_RUNTIME,
            "            UnityEngine.Object.Destroy(runtime);",
            "            var Object = ZNetScene.instance;\n"
            "            Object.Destroy(runtime._worker()!.gameObject);")
        self.assert_refused(
            "a local named Object was trusted as UnityEngine.Object",
            marker="#381")

    def test_the_take_is_not_allowed_outside_the_worker_folder_either(self):
        # `.Pickup(` went into the worker token list and not the outside-workers
        # one, so a take in Adapters/ or Domain/ passed while four sentences said
        # it could not. The enforcement claim being wider than the enforcement is
        # the defect that round existed to fix.
        for where, label in ((ADAPTERS, "Adapters"), (DOMAIN, "Domain")):
            with self.subTest(tree=label):
                self.plant_file(
                    os.path.join(where, "ZzOutside.cs"),
                    "        ((dynamic)who).Pickup(cart, autoequip: false, autoPickupDelay: false);")
                self.assert_refused(
                    "a take outside Adapters/Workers passed in " + label, marker="#313")
                # Each subtest plants its own file; clear it before the next so
                # the tree is clean when the second plant is measured.
                for path in self._planted:
                    if os.path.isfile(path):
                        os.remove(path)
                self._planted = []

    # --- #401: the population pins walk the whole product, not one folder. ---
    #
    # Each of the five below passed at exit 0 before this issue, with every
    # pinned count unchanged, because the walk stopped at Adapters/Workers.

    def test_a_destruction_outside_the_worker_folder_is_counted(self):
        self.plant_file(os.path.join(ADAPTERS, "ZzReaper.cs"),
                        "        UnityEngine.Object.Destroy(cart);")
        self.assert_refused(
            "a destruction outside Adapters/Workers was counted by nobody",
            marker="#381",
            contains="destroys something 1 time(s); this rule expects 0")

    def test_a_routed_destruction_outside_the_worker_folder_is_counted(self):
        # The substitution shape, one folder over: a destruction routed through
        # an instance is how a NETWORKED body leaves the world, inventory and all.
        self.plant_file(os.path.join(ADAPTERS, "ZzRoutedReaper.cs"),
                        "        _scene.Destroy(cart);")
        self.assert_refused(
            "a destruction routed through an instance outside Adapters/Workers was unpinned",
            marker="#381",
            contains="routes a destruction through something other than Unity's Object statics")

    def test_a_body_removal_outside_the_worker_folder_is_counted(self):
        self.plant_file(os.path.join(UI, "ZzRemover.cs"), "        view.Destroy();")
        self.assert_refused(
            "a body removal outside Adapters/Workers was counted by nobody",
            marker="#381",
            contains="takes a body out of the world 1 time(s); this rule expects 0")

    def test_the_population_pin_keys_on_the_path_outside_the_worker_folder_too(self):
        # Basename keying is the escape this carve-out has already been corrected
        # for twice. A second `TeamsterWorkerBody.cs`, now that the walk leaves
        # the worker folder, would inherit that file's allowance of one removal.
        self.plant_file(os.path.join(DOMAIN, "TeamsterWorkerBody.cs"),
                        "        view.Destroy();")
        self.assert_refused(
            "a file outside Adapters/Workers inherited an allowance by basename",
            marker="#381",
            contains="takes a body out of the world 1 time(s); this rule expects 0")

    def test_the_population_pin_descends_outside_the_worker_folder(self):
        self.plant_file(os.path.join(DOMAIN, "Hauling", "Zz", "ZzDeepReaper.cs"),
                        "        UnityEngine.Object.Destroy(cart);")
        self.assert_refused(
            "a destruction in a subdirectory outside Adapters/Workers was invisible",
            marker="#381",
            contains="destroys something 1 time(s); this rule expects 0")

    def test_znet_scene_is_forbidden_outside_the_worker_folder(self):
        self.plant_file(os.path.join(DOMAIN, "ZzOutsideDestroy.cs"),
                        "        ZNetScene.instance.Destroy((UnityEngine.GameObject)cart);")
        self.assert_refused(
            "ZNetScene outside Adapters/Workers could remove a body and its inventory",
            marker="#313")

    def test_inventory_add_item_is_forbidden_in_the_worker_folder(self):
        self.plant_file(os.path.join(WORKERS, "ZzInventoryMover.cs"),
                        "        ((dynamic)who.GetInventory()).AddItem(cart);")
        self.assert_refused(
            "Inventory.AddItem in Adapters/Workers moved material without a pinned authority",
            marker="#313")

    def test_inventory_add_item_is_forbidden_outside_the_worker_folder(self):
        self.plant_file(os.path.join(ADAPTERS, "ZzInventoryMover.cs"),
                        "        ((dynamic)who.GetInventory()).AddItem(cart);")
        self.assert_refused(
            "Inventory.AddItem outside Adapters/Workers moved material without a pinned authority",
            marker="#313")

    def test_block_comment_trivia_does_not_hide_inventory_add_item(self):
        self.plant_file(os.path.join(WORKERS, "ZzCommentedInventoryMover.cs"),
                        "        ((dynamic)who.GetInventory()).AddItem /* rationale */ (cart);")
        self.assert_refused(
            "block-comment trivia hid Inventory.AddItem from the audit",
            marker="#313")

    def test_interpolation_expression_comment_does_not_hide_inventory_add_item(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzInterpolatedInventoryMover.cs"),
            '        var moved = $"{((dynamic)who.GetInventory()).AddItem /* rationale */ (cart)}";')
        self.assert_refused(
            "an interpolated expression hid Inventory.AddItem from the audit",
            marker="#313")

    def test_interpolation_format_text_does_not_hide_later_inventory_add_item(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzFormattedInventoryMover.cs"),
            '        var label = $"{0://}"; ((dynamic)who.GetInventory()).AddItem(cart);')
        self.assert_refused(
            "interpolation format text hid a later Inventory.AddItem from the audit",
            marker="#313")

    def test_nullable_interpolation_format_does_not_hide_later_inventory_add_item(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzNullableFormattedInventoryMover.cs"),
            '        var label = $"{value as int?://}"; ((dynamic)who.GetInventory()).AddItem(cart);')
        self.assert_refused(
            "a nullable interpolation format hid a later Inventory.AddItem",
            marker="#313")

    def test_nullable_alignment_format_does_not_hide_later_inventory_add_item(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzAlignedNullableInventoryMover.cs"),
            '        var label = $"{value as int?,10://}"; ((dynamic)who.GetInventory()).AddItem(cart);')
        self.assert_refused(
            "nullable interpolation alignment hid a later Inventory.AddItem",
            marker="#313")

    def test_inventory_add_item_inside_a_block_comment_is_not_code(self):
        self.plant_file(os.path.join(WORKERS, "ZzCommentOnly.cs"),
                        "        /* ((dynamic)who.GetInventory()).AddItem(cart); */")
        code, out = validate()
        self.assertEqual(
            0, code,
            "a block-comment-only AddItem spelling was treated as executable code\n" + out[-2000:])

    def test_block_comment_trivia_does_not_hide_worker_destruction(self):
        self.plant_file(os.path.join(WORKERS, "ZzCommentedDestroy.cs"),
                        "        view.Destroy /* rationale */ ();")
        self.assert_refused(
            "block-comment trivia hid a worker destruction from the pinned population",
            marker="#381")

    def test_interpolation_format_text_does_not_hide_later_worker_destruction(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzFormattedDestroy.cs"),
            '        var label = $"{0://}"; view.Destroy();')
        self.assert_refused(
            "interpolation format text hid a later worker destruction",
            marker="#381")

    def test_nullable_interpolation_format_does_not_hide_later_worker_destruction(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzNullableFormattedDestroy.cs"),
            '        var label = $"{value as int?://}"; view.Destroy();')
        self.assert_refused(
            "a nullable interpolation format hid a later worker destruction",
            marker="#381")

    def test_nullable_alignment_format_does_not_hide_later_worker_destruction(self):
        self.plant_file(
            os.path.join(WORKERS, "ZzAlignedNullableDestroy.cs"),
            '        var label = $"{value as int?,10://}"; view.Destroy();')
        self.assert_refused(
            "nullable interpolation alignment hid a later worker destruction",
            marker="#381")

    def test_the_population_pin_descends_into_subdirectories(self):
        # `glob("*.cs")` does not descend, so the same planted removal one folder
        # down was invisible while the success sentence said "anywhere in
        # Adapters/Workers". The #313 scope audit over the very same directory
        # uses rglob and DID count the file, which is what made this a defect
        # rather than a judgement call.
        self.plant_file(os.path.join(WORKERS, "Sweep", "ZzSweeper.cs"),
                        "        ZNetScene.instance.Destroy((UnityEngine.GameObject)cart);")
        self.assert_refused(
            "a removal one directory down was invisible to a rule claiming to cover the folder",
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
