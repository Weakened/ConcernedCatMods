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
DEPOSIT = os.path.join(WORKERS, "GunnarDepositPort.cs")
HAULING = os.path.join(WORKERS, "GunnarHaulingRuntime.cs")
COLLECTION_RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
ADAPTERS = os.path.join(TEAMSTER, "Adapters")
DOMAIN = os.path.join(TEAMSTER, "Domain")
SHARED_WORKERS = os.path.join(ROOT, "src", "Shared", "Workers")
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


class CarveOutFixture(unittest.TestCase):
    """Plant a real violation into the real tree, run the real validator, require
    it to refuse, then put the tree back.

    Separated from the tests so a second allowance can reuse it without
    re-running the first one's plants. The deposit class used to derive from
    `CarveOutIsNarrow` and inherited its twenty-three tests along with these
    helpers, so the whole suite ran twice for no extra coverage - and every one
    of those runs is a full validator pass over the tree.

    This class deliberately has no tests of its own: `unittest` would run them
    here and again in every subclass.
    """

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


class CarveOutIsNarrow(CarveOutFixture):
    """Every way an independent review got past the first version."""

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


class DepositCarveOutIsNarrow(CarveOutFixture):
    """The D15 deposit allowance, proved the way the pickup allowance was.

    Three calls move a player's material into a vanilla container, and each is
    pinned verbatim in one file. Every plant below is an escape somebody could
    actually write - most of them are the same shapes that got past the pickup
    allowance, applied to the new one before rather than after a review found
    them, plus two that are specific to a transfer: reversing its direction, and
    removing the count that was ASKED FOR rather than the count that ARRIVED.

    Inherits the fixture, so each plant also proves the tree is clean before and
    after it.
    """

    _restore_path = DEPOSIT

    def edit_deposit(self, extra):
        """Adds a line right after the authorized whole-stack move."""
        with open(DEPOSIT, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = DEPOSIT
        marker = "                to.MoveItemToThis(from, stack);"
        self.assertEqual(1, original.count(marker), "the authorized move call moved")
        with open(DEPOSIT, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(marker, marker + "\n" + extra))

    def swap_in_deposit(self, old, new):
        self.swap_in(DEPOSIT, old, new)

    # -- the move itself --

    def test_a_move_is_not_allowed_in_another_worker_file(self):
        self.plant_file(os.path.join(WORKERS, "ZzMover.cs"),
                        "        ((dynamic)cart).MoveItemToThis(who, null);")
        self.assert_refused("a container move appeared in a file that was never authorized to have one")

    def test_a_move_is_not_allowed_outside_adapters_workers(self):
        # The escape #401 named: a token refused only inside Adapters/Workers is
        # a token a helper in Domain/ may spell.
        self.plant_file(os.path.join(DOMAIN, "Zz", "ZzDomainMover.cs"),
                        "        ((dynamic)cart).MoveItemToThis(who, null);")
        self.assert_refused("a container move in Domain/ passed the audit")

    def test_the_allowance_does_not_follow_the_deposit_file_name_elsewhere(self):
        self.plant_file(os.path.join(WORKERS, "Extra", "GunnarDepositPort.cs"),
                        "        ((dynamic)cart).MoveItemToThis(who, null);")
        self.assert_refused("a second file of the deposit port's name inherited the allowance")

    def test_the_move_is_authorized_once_and_not_twice(self):
        self.edit_deposit("                to.MoveItemToThis(from, stack);")
        self.assert_refused("a second whole-stack move passed inside the authorized file")

    def test_the_move_may_not_be_reversed_into_a_withdrawal(self):
        # The one that turns a deposit into a take. `to.MoveItemToThis(from, ..)`
        # puts Gunnar's material in the chest; swapping the receiver and the
        # argument empties the chest into Gunnar - a capability D15 does not
        # grant, spelled with the same token and the same argument names.
        self.swap_in_deposit(
            "                to.MoveItemToThis(from, stack);",
            "                from.MoveItemToThis(to, stack);")
        self.assert_refused("the move was reversed into a withdrawal and passed")

    def test_a_space_before_the_paren_does_not_hide_a_move(self):
        self.plant_file(os.path.join(WORKERS, "ZzSpacedMove.cs"),
                        "        ((dynamic)cart).MoveItemToThis (who, null);")
        self.assert_refused("a space before the paren got a container move past the audit")

    def test_a_newline_between_receiver_and_member_does_not_hide_a_move(self):
        self.plant_file(os.path.join(WORKERS, "ZzSplitMove.cs"),
                        "        ((dynamic)cart).\n            MoveItemToThis(who, null);")
        self.assert_refused("a line split got a container move past the audit")

    # -- the add, which is the half that could put an arbitrary item anywhere --

    def test_the_add_may_only_be_the_clone_of_the_stack_being_removed_from(self):
        # THE ESCAPE THIS PIN EXISTS FOR. `AddItem` can put any ItemData into any
        # inventory. Authorized here only as `to.AddItem(part)` - the clone of a
        # stack the very next lines remove from - so an add of anything else is a
        # product putting an item it got from somewhere else into a player's
        # chest, which is not what D15 granted and is how cargo would be minted.
        self.swap_in_deposit(
            "                to.AddItem(part);",
            "                to.AddItem(SomethingElse());")
        self.assert_refused("an arbitrary add passed inside the authorized file")

    def test_the_add_may_not_target_another_inventory(self):
        self.swap_in_deposit(
            "                to.AddItem(part);",
            "                Player.m_localPlayer.GetInventory().AddItem(part);")
        self.assert_refused("an add into the player's own inventory passed inside the authorized file")

    def test_an_add_is_not_allowed_in_another_worker_file(self):
        self.plant_file(os.path.join(WORKERS, "ZzAdder.cs"),
                        "        ((dynamic)cart).AddItem(who);")
        self.assert_refused("an add appeared in a file that was never authorized to have one")

    def test_an_add_is_not_allowed_outside_adapters_workers(self):
        self.plant_file(os.path.join(ADAPTERS, "Zz", "ZzAdapterAdder.cs"),
                        "        ((dynamic)cart).AddItem(who);")
        self.assert_refused("an add in Adapters/ passed the audit")

    # -- the remove, which is where conservation is kept or lost --

    def test_the_remove_may_not_take_the_count_that_was_asked_for(self):
        # The conservation escape, and the subtlest one here. `moved` is what
        # the destination was MEASURED to gain; `remaining` is what the caller
        # wanted. Removing the second destroys whatever the chest refused - the
        # exact failure the measured-delta discipline exists to prevent - and it
        # is a one-word edit that compiles.
        self.swap_in_deposit(
            "                    from.RemoveItem(stack, moved);",
            "                    from.RemoveItem(stack, remaining);")
        self.assert_refused("a remove of the asked-for count passed inside the authorized file")

    def test_the_remove_may_not_target_the_destination(self):
        self.swap_in_deposit(
            "                    from.RemoveItem(stack, moved);",
            "                    to.RemoveItem(stack, moved);")
        self.assert_refused("a remove from the destination passed inside the authorized file")

    def test_a_remove_is_not_allowed_in_another_worker_file(self):
        self.plant_file(os.path.join(WORKERS, "ZzRemover.cs"),
                        "        ((dynamic)cart).RemoveItem(who, 1);")
        self.assert_refused("a remove appeared in a file that was never authorized to have one")

    def test_a_remove_is_not_allowed_outside_adapters_workers(self):
        self.plant_file(os.path.join(DOMAIN, "Zz", "ZzDomainRemover.cs"),
                        "        ((dynamic)cart).RemoveItem(who, 1);")
        self.assert_refused("a remove in Domain/ passed the audit")

    # -- the deposit port has no allowance for the pickup calls, and vice versa --

    def test_the_deposit_port_does_not_inherit_the_pickup_allowance(self):
        self.edit_deposit("        ((dynamic)cart).Interact(who, repeat: false, alt: false);")
        self.assert_refused("the deposit port inherited the collection port's allowance")

    def test_the_collection_port_does_not_inherit_the_deposit_allowance(self):
        self.edit_port("        to.MoveItemToThis(from, stack);")
        self.assert_refused("the collection port inherited the deposit port's allowance")

    # -- the shared sources the Teamster assembly is actually built from --

    def test_a_move_is_not_allowed_in_shared_sources_teamster_compiles(self):
        # `ConcernedTeamster.csproj` compiles `..\Shared\Workers` into the
        # Teamster assembly, and the scope audit iterated only
        # `src/ConcernedTeamster` - while its own success sentence said
        # "everywhere else in the product". Same shape as the folder gap #401
        # closed, one directory over. Nothing in src/Shared spells any of the
        # five today, so this proves the rule rather than a defect.
        self.plant_file(os.path.join(SHARED_WORKERS, "ZzSharedMover.cs"),
                        "        ((dynamic)cart).MoveItemToThis(who, null);")
        self.assert_refused("a container move in a shared source Teamster compiles passed the audit")

    def test_an_add_is_not_allowed_in_shared_sources_teamster_compiles(self):
        self.plant_file(os.path.join(SHARED_WORKERS, "ZzSharedAdder.cs"),
                        "        ((dynamic)cart).AddItem(who);")
        self.assert_refused("an add in a shared source Teamster compiles passed the audit")

    def test_shared_workers_does_not_inherit_the_worker_runtime_allowances(self):
        # The shared tree's own folder is literally "Workers", so a scope check
        # written as `parts[:2] == ("Adapters", "Workers")` against the wrong
        # root would hand src/Shared/Workers the worker runtime's allowances -
        # a widening produced by the rule that closed one.
        self.plant_file(os.path.join(SHARED_WORKERS, "ZzSharedPicker.cs"),
                        "        ((dynamic)cart).Interact(who, false, false);")
        self.assert_refused("a shared source inherited the worker runtime's allowance")

    # -- the mint the authorized AddItem could otherwise have carried --

    def test_the_clone_must_come_from_the_stack_being_removed_from(self):
        # THE ESCAPE THIS RULE EXISTS FOR. `to.AddItem(part)` is pinned verbatim,
        # but `part` is a local whose initializer is two lines above and was
        # unpinned. An ObjectDB lookup in its place creates material from a name
        # - what D15 lists under "no synthetic or replacement resources" - while
        # the pinned call stays byte-identical.
        self.swap_in_deposit(
            "                    ItemDrop.ItemData part = stack.Clone();",
            "                    ItemDrop.ItemData part = ObjectDB.instance"
            ".GetItemPrefab(itemPrefab).GetComponent<ItemDrop>().m_itemData.Clone();")
        self.assert_refused(
            "an item built from a name rode the authorized add", marker="#381")

    def test_a_second_item_lookup_in_the_deposit_port_is_refused(self):
        self.swap_in_deposit(
            "            ObjectDB database = ObjectDB.instance;",
            "            ObjectDB database = ObjectDB.instance;\n"
            "            GameObject? another = database.GetItemPrefab(itemPrefab);")
        self.assert_refused(
            "a second item-prefab lookup appeared in the deposit port", marker="#381")

    # -- the permit mint stays unforgeable now that the type is public --

    def test_a_public_constructor_on_the_permit_is_refused(self):
        self.swap_in(
            os.path.join(ROOT, "src", "ConcernedNPC", "Storage", "NpcContainerPermit.cs"),
            "    private NpcContainerPermit(",
            "    public NpcContainerPermit(")
        self.assert_refused(
            "a public constructor on the now-public permit passed the audit",
            marker="permit-mint")
