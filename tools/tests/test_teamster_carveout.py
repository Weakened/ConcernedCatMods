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
        marker = "to.MoveItemToThis(from, stack);"
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
        self.edit_deposit("        to.MoveItemToThis(from, stack);")
        self.assert_refused("a second whole-stack move passed inside the authorized file")

    def test_the_move_may_not_be_reversed_into_a_withdrawal(self):
        # The one that turns a deposit into a take. `to.MoveItemToThis(from, ..)`
        # puts Gunnar's material in the chest; swapping the receiver and the
        # argument empties the chest into Gunnar - a capability D15 does not
        # grant, spelled with the same token and the same argument names.
        self.swap_in_deposit(
            "to.MoveItemToThis(from, stack);",
            "from.MoveItemToThis(to, stack);")
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
            "to.AddItem(part);",
            "to.AddItem(SomethingElse());")
        self.assert_refused("an arbitrary add passed inside the authorized file")

    def test_the_add_may_not_target_another_inventory(self):
        self.swap_in_deposit(
            "to.AddItem(part);",
            "Player.m_localPlayer.GetInventory().AddItem(part);")
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
            "from.RemoveItem(stack, arrived);",
            "from.RemoveItem(stack, remaining);")
        self.assert_refused("a remove of the asked-for count passed inside the authorized file")

    def test_the_remove_may_not_target_the_destination(self):
        self.swap_in_deposit(
            "from.RemoveItem(stack, arrived);",
            "to.RemoveItem(stack, arrived);")
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
            "ItemDrop.ItemData part = stack.Clone();",
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

    # -- the mint rule's tokens, two of which were never evaluated --

    def test_an_instantiate_in_the_deposit_port_is_refused(self):
        # The loop that refuses these ran its body for ONE of three iterations,
        # because of a stray `token == "Instantiate" and`. So the tuple read as a
        # refusal list and refused one thing. No plant covered the other two.
        self.edit_deposit("        var made = UnityEngine.Object.Instantiate(null);")
        self.assert_refused("an Instantiate in the deposit port passed", marker="#381")

    def test_an_inline_item_database_reach_in_the_deposit_port_is_refused(self):
        # The escape the count-only lookup gate allowed: move SafeRoom's lookup
        # away and put one next to the authorized add. `lookups` stays at one.
        self.edit_deposit(
            "        var template = ObjectDB.instance.GetItemPrefab(itemPrefab);")
        self.assert_refused("an inline item-database reach passed", marker="#381")

    def test_reaching_an_items_own_prefab_component_is_refused(self):
        self.edit_deposit(
            "        var templated = stack.m_dropPrefab.GetComponent<ItemDrop>();")
        self.assert_refused("a template read off an item's own prefab passed", marker="#381")

    def test_the_authorized_lookup_must_stay_in_its_place(self):
        # The count was pinned and the SITE was not, so the one authorized lookup
        # could be anywhere - including beside the add.
        self.swap_in_deposit(
            "database.GetItemPrefab(itemPrefab)",
            "database.GetItemPrefab(SomethingElse())")
        self.assert_refused("the authorized lookup changed what it looks up", marker="#381")

    def test_the_clone_may_not_be_reassigned_before_the_add(self):
        self.swap_in_deposit(
            "                    part.m_equipped = false;",
            "                    part.m_equipped = false;\n"
            "                    part = SomethingElse();")
        self.assert_refused("the clone was replaced between the clone and the add", marker="#381")

    # -- which inventory is which, which no text audit saw before --

    def test_the_source_derivation_may_not_be_reversed(self):
        # THE REVERSAL THE FIRST FIX DID NOT CLOSE. `Move` took two same-typed
        # parameters; renaming them swapped the direction with the call site, all
        # three pinned calls and the caller's own guard byte-identical, and emptied
        # the player's chest into Gunnar. It now derives both from the two roles,
        # and both derivations are pinned - so a reversal has to edit one of them.
        self.swap_in_deposit(
            "        Inventory? from = InventoryOf(worker);\n"
            "        Inventory? to = InventoryOf(destination);",
            "        Inventory? from = InventoryOf(destination);\n"
            "        Inventory? to = InventoryOf(worker);")
        self.assert_refused("the deposit was reversed into a withdrawal", marker="#381")

    def test_the_move_may_not_take_two_interchangeable_inventories(self):
        # Going back to `(Inventory from, Inventory to, ...)` restores the
        # rename attack, so the shape itself is refused: the derivations have to
        # be inside the mover.
        self.swap_in_deposit(
            "    private MoveTally Move(Humanoid? worker, Container? destination, "
            "string itemPrefab, int units)",
            "    private MoveTally Move(Inventory from, Inventory to, string itemPrefab, int units)")
        self.assert_refused(
            "the mover went back to two interchangeable inventory parameters", marker="#381")


DEATH = os.path.join(WORKERS, "GunnarDeathDropPort.cs")
PINNED_DROP = "returned = _humanoid.DropItem(_inventory, item, count);"


class DeathDropIsNarrow(CarveOutFixture):
    """The D16 death-drop allowance (`DECISIONS.md` D16), proved by plants.

    D16 grants one thing: when Gunnar GENUINELY dies, what he carries may go on
    the ground through vanilla's own drop. Two halves need proving. Where the
    drop may be spelled - one pinned call, one file - is the #313 token pin. What
    may REACH it is the D16 verb audit: the handler hangs off the body's own
    death callback, asks the body whether it is dead and asks the decision
    whether to drop, and nothing else in the product names it. Each plant below
    is an escape across one of those lines, and each was run against the
    validator from before this change (origin/main 89ce8b6), which let every one
    of them through.
    """

    _restore_path = DEATH

    def swap_in_death(self, old, new):
        self.swap_in(DEATH, old, new)

    def swap_many_in_death(self, pairs):
        with open(DEATH, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = DEATH
        changed = original
        for old, new in pairs:
            self.assertEqual(1, changed.count(old), "the pinned text moved: " + old)
            changed = changed.replace(old, new, 1)
        with open(DEATH, "w", encoding="utf-8", newline="") as handle:
            handle.write(changed)

    # -- where the drop may be spelled --

    def test_a_drop_is_not_allowed_in_another_worker_file(self):
        # The retire verb is the one D16 names: `retire` must never reach a drop.
        self.plant_file(os.path.join(WORKERS, "ZzRetireDrop.cs"),
                        "        ((dynamic)who).DropItem(cart, null, 1);")
        self.assert_refused("a drop appeared in a worker file that was never authorized to have one",
                            contains="'.DropItem('")

    def test_a_drop_is_not_allowed_outside_adapters_workers(self):
        self.plant_file(os.path.join(DOMAIN, "Zz", "ZzDomainDrop.cs"),
                        "        ((dynamic)who).DropItem(cart, null, 1);")
        self.assert_refused("a drop in Domain/ passed the audit", contains="'.DropItem('")

    def test_vanillas_static_drop_is_refused_too(self):
        # `ItemDrop.DropItem(data, n, at, rot)` builds a ground item from any
        # ItemData this product chose - the spawning D15 and D16 both refuse.
        self.plant_file(os.path.join(ADAPTERS, "ZzSpawner.cs"),
                        "        ItemDrop.DropItem(null, 1, default, default);")
        self.assert_refused("vanilla's static item spawn passed outside the worker folder",
                            contains="'.DropItem('")

    def test_a_drop_is_not_allowed_in_shared_sources_teamster_compiles(self):
        self.plant_file(os.path.join(SHARED_WORKERS, "ZzSharedDrop.cs"),
                        "        ((dynamic)who).DropItem(cart, null, 1);")
        self.assert_refused("a drop in a shared tree Teamster compiles passed", contains="'.DropItem('")

    def test_the_allowance_does_not_follow_the_port_name_elsewhere(self):
        self.plant_file(os.path.join(WORKERS, "Extra", "GunnarDeathDropPort.cs"),
                        "        ((dynamic)who).DropItem(cart, null, 1);")
        self.assert_refused("a second file of the death port's name inherited the allowance",
                            contains="'.DropItem('")

    def test_a_space_and_a_newline_do_not_hide_a_drop(self):
        self.plant_file(os.path.join(WORKERS, "ZzSpacedDrop.cs"),
                        "        ((dynamic)who).\n            DropItem (cart, null, 1);")
        self.assert_refused("whitespace got a drop past the audit", contains="'.DropItem('")

    def test_the_drop_is_authorized_once_and_not_twice(self):
        self.swap_in_death(
            PINNED_DROP,
            PINNED_DROP + "\n                    _humanoid.DropItem(_inventory, item, count);")
        self.assert_refused("a second drop passed inside the authorized file", contains="appears 2 times")

    def test_the_drop_may_not_drop_more_than_the_stack_holds(self):
        # "No minting: exactly what the body holds, once." The count is the
        # stack's own; a different number is a different call.
        self.swap_in_death(PINNED_DROP, "returned = _humanoid.DropItem(_inventory, item, count * 2);")
        self.assert_refused("a drop of a different count passed", contains="'.DropItem('")

    def test_the_drop_may_not_empty_another_inventory(self):
        # "No drop of items the body never held."
        self.swap_in_death(
            PINNED_DROP,
            "returned = _humanoid.DropItem(Player.m_localPlayer.GetInventory(), item, count);")
        self.assert_refused("a drop out of the player's inventory passed", contains="'.DropItem('")

    # -- what may reach it --

    def test_another_verb_in_the_port_may_not_call_the_handler(self):
        # The retire that D16 names, written as the most natural one-liner.
        self.swap_in_death(
            "    private static void Report(string message)",
            "    internal void Retire() { OnDeath(); }\n\n    private static void Report(string message)")
        self.assert_refused("a second caller of the death handler passed", marker="D16 death-drop audit",
                            contains="names OnDeath 3 time(s)")

    def test_no_other_file_may_name_the_handler_or_call_vanillas_death(self):
        # Calling vanilla's own Character.OnDeath() would be this product killing
        # its worker to reach the drop - the death path entered by a verb.
        self.plant_file(os.path.join(WORKERS, "ZzKill.cs"), "        ((dynamic)who).OnDeath();")
        self.assert_refused("a call to OnDeath from another worker file passed",
                            marker="D16 death-drop audit", contains="names OnDeath")

    def test_the_handler_may_not_be_reached_by_name_through_a_message(self):
        self.plant_file(os.path.join(ADAPTERS, "ZzMessage.cs"),
                        "        ((dynamic)who).SendMessage(\"OnDeath\");")
        self.assert_refused("a SendMessage naming the handler passed", marker="D16 death-drop audit",
                            contains="names OnDeath")

    def test_the_handler_may_not_hang_off_another_event(self):
        # The death ANIMATION callback runs before the body is dead; the handler
        # belongs on the death callback alone.
        self.swap_in_death(
            "_humanoid.m_onDeath = (Action)Delegate.Combine(_humanoid.m_onDeath, _onDeath);",
            "_humanoid.m_onDeathAnim = (Action)Delegate.Combine(_humanoid.m_onDeathAnim, _onDeath);")
        self.assert_refused("the handler was hung off the death animation and passed",
                            marker="D16 death-drop audit", contains="subscription(s)")

    def test_the_death_callback_may_not_be_invoked_by_this_product(self):
        self.swap_in_death(
            "    private void OnDestroy()\n    {\n",
            "    private void OnDestroy()\n    {\n        _humanoid?.m_onDeath?.Invoke();\n")
        self.assert_refused("the death callback was invoked from OnDestroy and passed",
                            marker="D16 death-drop audit", contains="mention(s)")

    def test_the_death_callback_may_not_be_touched_elsewhere(self):
        self.plant_file(os.path.join(WORKERS, "ZzInvoke.cs"), "        ((dynamic)who).m_onDeath();")
        self.assert_refused("the death callback was invoked from another file and passed",
                            marker="D16 death-drop audit", contains="touches m_onDeath")

    def test_the_death_predicate_may_not_be_removed(self):
        self.swap_in_death(
            "if (_humanoid == null || !_humanoid.IsDead())",
            "if (_humanoid == null)")
        self.assert_refused("the IsDead() predicate was removed and the drop passed",
                            marker="D16 death-drop audit", contains="IsDead()")

    def test_the_decision_may_not_be_skipped(self):
        self.swap_in_death(
            "DeathDropVerdict verdict = WorkerDeathDrop.Decide(facts);",
            "DeathDropVerdict verdict = DeathDropVerdict.Drop;")
        self.assert_refused("the decision was skipped and the drop passed",
                            marker="D16 death-drop audit", contains="WorkerDeathDrop.Decide")

    def test_the_verdict_may_not_be_consulted_and_ignored(self):
        # The retire guard's lesson: a verdict read and not obeyed is no guard.
        self.swap_in_death(
            "if (verdict != DeathDropVerdict.Drop)",
            "if (verdict == DeathDropVerdict.Unspecified)")
        self.assert_refused("a verdict consulted and ignored passed",
                            marker="D16 death-drop audit", contains="does not REFUSE")

    def test_the_drop_may_not_be_lifted_out_of_the_handler(self):
        # The escape that beat the first retire audit: lift the call into a
        # helper, so the handler's body no longer contains it and anything may
        # call the helper.
        self.swap_many_in_death([
            (PINNED_DROP, "returned = DropOne(item, count);"),
            ("    private static void Report(string message)",
             "    private bool DropOne(ItemDrop.ItemData item, int count) =>\n"
             "        _humanoid!.DropItem(_inventory, item, count);\n\n"
             "    private static void Report(string message)"),
        ])
        self.assert_refused("the drop was lifted into a helper and passed",
                            marker="D16 death-drop audit", contains="inside OnDeath()")

    # -- what may CAUSE the death that reaches it (review of 7330ef9) --

    def test_a_retire_may_not_be_rewritten_as_a_death(self):
        # The blocker an independent review found: `ct_haul retire` rewritten as
        # "set his health to nothing" goes through vanilla's genuine death path
        # with every gate green.
        self.plant_file(os.path.join(WORKERS, "ZzRetireByDeath.cs"),
                        "        ((dynamic)who).SetHealth(0f);")
        self.assert_refused("a retire by SetHealth(0) passed", contains="'SetHealth'")

    def test_nothing_may_hit_him_from_domain(self):
        self.plant_file(os.path.join(DOMAIN, "Zz", "ZzHit.cs"),
                        "        ((dynamic)who).Damage(cart);")
        self.assert_refused("a damage call in Domain/ passed", contains="'.Damage('")

    def test_nothing_may_send_vanillas_damage_message(self):
        # The one plant here that was NOT an escape before this change: outside
        # Adapters/Workers, CT-026's authority audit already refuses the
        # InvokeRPC it needs. Kept as a regression guard for the new
        # 'RPC_Damage' token, which also covers the name sent any other way.
        self.plant_file(os.path.join(ADAPTERS, "ZzRpc.cs"),
                        "        ((dynamic)who).InvokeRPC(\"RPC_Damage\", cart);")
        self.assert_refused("a damage message outside the worker folder passed", contains="'RPC_Damage'")

    def test_the_death_predicate_must_return(self):
        self.swap_in_death(
            "        if (_humanoid == null || !_humanoid.IsDead())\n        {\n",
            "        if (_humanoid == null || !_humanoid.IsDead())\n        {\n            Report(\"not dead\");\n")
        self.assert_refused("an IsDead() guard that does not return passed",
                            marker="D16 death-drop audit", contains="bare `return;`")

    def test_the_decision_is_told_by_the_body_never_by_a_literal(self):
        self.swap_in_death(
            "host.Prepare(_humanoid, genuineDeath: _humanoid.IsDead(), inventoryLoaded: loaded)",
            "host.Prepare(_humanoid, genuineDeath: true, inventoryLoaded: loaded)")
        self.assert_refused("a literal genuineDeath passed", marker="D16 death-drop audit",
                            contains="without asking the body")

    def test_the_handlers_own_delegate_may_not_be_invoked(self):
        self.swap_in_death(
            "    private void OnDestroy()\n    {\n",
            "    private void OnDestroy()\n    {\n        _onDeath?.Invoke();\n")
        self.assert_refused("the handler's delegate was invoked from OnDestroy and passed",
                            marker="D16 death-drop audit", contains="_onDeath delegate")

    def test_the_drop_empties_only_the_dying_bodys_own_inventory(self):
        self.swap_in_death(
            "_inventory = _humanoid.GetInventory();",
            "_inventory = Player.m_localPlayer.GetInventory();")
        self.assert_refused("the drop was pointed at the player's inventory and passed",
                            marker="D16 death-drop audit", contains="assigns _inventory")

    def test_the_handlers_delegate_may_not_be_invoked_in_place_of_its_null_check(self):
        # A review's residual: the mention count stays at five if the null check
        # is swapped for an invocation.
        self.swap_in_death(
            "        if (_humanoid != null && _onDeath != null)\n        {\n",
            "        _onDeath?.Invoke();\n        if (_humanoid != null)\n        {\n")
        self.assert_refused("the delegate was invoked in place of its null check and passed",
                            marker="D16 death-drop audit", contains="invokes its _onDeath delegate")


SURVEY = os.path.join(WORKERS, "GunnarCollectionSurvey.cs")
TOUR_RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.Tour.cs")
QUIET_WARD = "PrivateArea.CheckAccess(position, 0f, flash: false, wardCheck: false)"


class CollectionTourIsBounded(CarveOutFixture):
    """The collection tour (#381) adds a survey over every loaded network object
    and a walk to a chest. Two things that would have been one line long are
    pinned rather than promised: a ward query that lights the guard stone up -
    an RPC to every peer, sent from inside vanilla - and a chest found rather
    than pointed at. Each plant below was run against the validator from before
    this change (origin/main 89ce8b6), which let every one of them through.
    """

    _restore_path = SURVEY

    # -- asking about a ward is a read, only when it says so --

    def test_a_ward_query_with_the_default_flash_is_refused(self):
        # Vanilla's default is flash: true.
        self.swap_in(SURVEY, QUIET_WARD, "PrivateArea.CheckAccess(position)")
        self.assert_refused("a ward query with vanilla's default flash passed",
                            marker="#381 ward-query audit", contains="does not say `flash: false`")

    def test_a_flashing_ward_query_is_refused(self):
        self.swap_in(SURVEY, QUIET_WARD, "PrivateArea.CheckAccess(position, 0f, flash: true, wardCheck: false)")
        self.assert_refused("a flashing ward query passed",
                            marker="#381 ward-query audit", contains="does not say `flash: false`")

    def test_a_positional_flash_is_refused(self):
        self.swap_in(SURVEY, QUIET_WARD, "PrivateArea.CheckAccess(position, 0f, false, false)")
        self.assert_refused("a positional flash argument passed",
                            marker="#381 ward-query audit", contains="does not say `flash: false`")

    def test_a_ward_query_elsewhere_in_teamster_is_held_to_the_same_rule(self):
        self.plant_file(os.path.join(DOMAIN, "Zz", "ZzWard.cs"),
                        "        PrivateArea.CheckAccess(default);")
        self.assert_refused("a flashing ward query in Domain/ passed",
                            marker="#381 ward-query audit", contains="does not say `flash: false`")

    # -- the chest is pointed at, never found --

    def test_a_nearest_chest_search_is_refused_by_name(self):
        self.plant_file(os.path.join(WORKERS, "ZzChests.cs"),
                        "        var chest = FindNearestChest(cart);")
        self.assert_refused("a nearest-chest search passed", marker="#381 destination audit",
                            contains="FindNearestChest")

    def test_the_survey_may_not_reach_a_container_out_of_the_scene(self):
        # The fallback a reviewer would worry about: the survey already walks
        # every loaded network object, so a container is one GetComponent away.
        self.swap_in(
            SURVEY,
            "            judged++;",
            "            judged++;\n"
            "            Container? found = view.GetComponent<Container>();")
        self.assert_refused("the survey reached a container out of the scene", marker="#381 destination audit",
                            contains="reaches a container")

    def test_a_scene_wide_object_search_is_refused(self):
        self.plant_file(os.path.join(WORKERS, "ZzSearch.cs"),
                        "        var all = UnityEngine.Object.FindObjectsOfType<Humanoid>();")
        self.assert_refused("a scene-wide object search passed", marker="#381 destination audit",
                            contains="reaches a container")

    def test_the_tour_may_not_choose_its_own_chest(self):
        # The partial class can reach the field; a tour that set it would be a
        # chest the player never pointed at.
        self.swap_in(
            TOUR_RUNTIME,
            "            Vector3 chest = _destination!.transform.position;",
            "            _destination = _destination;\n"
            "            Vector3 chest = _destination!.transform.position;")
        self.assert_refused("the tour assigned the destination", marker="#381 destination audit",
                            contains="assigns the runtime's _destination")

    def test_the_destination_may_not_be_set_from_anything_but_the_pointed_chest(self):
        runtime = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
        self.swap_in(runtime, "        _destination = chest;", "        _destination = chest ?? _destination;")
        self.assert_refused("the destination was set from something else", marker="#381 destination audit",
                            contains="assigns _destination")

    # -- the spellings a review found around the two audits (review of a6aaf9f) --

    def test_a_ward_query_may_not_hide_behind_using_static(self):
        self.plant_file(os.path.join(WORKERS, "ZzStaticWard.cs"),
                        "        // nothing\n    }\n}\n// using static PrivateArea;\nnamespace Zz2 { using static PrivateArea; class Q { void P() {")
        self.assert_refused("using static PrivateArea passed", marker="#381 ward-query audit",
                            contains="using static PrivateArea")

    def test_a_ward_query_may_not_hide_behind_an_alias(self):
        self.plant_file(os.path.join(WORKERS, "ZzWardAlias.cs"),
                        "        // nothing\n    }\n}\nnamespace Zz3 { using Ward = PrivateArea; class Q { void P() {")
        self.assert_refused("a PrivateArea alias passed", marker="#381 ward-query audit",
                            contains="= PrivateArea;")

    def test_a_container_may_not_be_reached_through_an_alias(self):
        self.plant_file(os.path.join(WORKERS, "ZzChestAlias.cs"),
                        "        // nothing\n    }\n}\nnamespace Zz4 { using Chest = Container; class Q { void P() {")
        self.assert_refused("a Container alias passed", marker="#381 destination audit",
                            contains="reaches a container")

    def test_a_container_may_not_be_reached_by_a_cast(self):
        self.plant_file(os.path.join(WORKERS, "ZzChestCast.cs"),
                        "        var chest = cart as Container;")
        self.assert_refused("a cast to Container passed", marker="#381 destination audit",
                            contains="reaches a container")

    def test_a_container_may_not_be_reached_by_name(self):
        self.plant_file(os.path.join(WORKERS, "ZzChestByName.cs"),
                        "        var chest = ((UnityEngine.Component)cart).GetComponent(\"Container\");")
        self.assert_refused("a string-named GetComponent passed", marker="#381 destination audit",
                            contains="reaches a container")

    def test_every_scene_wide_search_is_refused(self):
        self.plant_file(os.path.join(ADAPTERS, "ZzSearchAll.cs"),
                        "        var all = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(object));")
        self.assert_refused("FindObjectsOfTypeAll passed", marker="#381 destination audit",
                            contains="reaches a container")

    def test_the_destination_may_not_be_filled_in_by_null_coalescing(self):
        runtime = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
        self.swap_in(runtime, "        _destination = null;", "        _destination ??= null;\n        _destination = null;")
        self.assert_refused("a ??= write to the destination passed", marker="#381 destination audit",
                            contains="assigns _destination")

    def test_a_part_of_the_runtime_under_another_name_may_not_choose_the_chest(self):
        path = os.path.join(WORKERS, "ZzRuntimePart.cs")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;\n"
                         "internal sealed partial class GunnarCollectionRuntime\n{\n"
                         "    private void Choose(Container found) { _destination = found; }\n}\n")
        self._planted.append(path)
        self.assert_refused("a runtime part in a differently named file chose the chest",
                            marker="#381 destination audit", contains="assigns the runtime's _destination")


# -- #379: the tour writes every move down before it happens --

PICK_GUARD = (
    "        if (!_tourPlan.IntendPick(\"about to pick \" + expected + \" from a source on the tour\", out string unwritten))\n"
    "        {\n"
    "            CancelTour(\"the next pick could not be written down first, so it was not made: \" + unwritten);\n"
    "            return;\n"
    "        }\n")
PICK_CALL_END = "            nowSeconds: Time.time);\n"
DEPOSIT_GUARD = (
    "        if (!_tourPlan.IntendDeposit(\"about to take what he carries to the chest\", out string unwritten))\n"
    "        {\n"
    "            detail = \"the trip could not be written down first, so nothing was moved: \" + unwritten;\n"
    "            return TourDepositEnd.Refused;\n"
    "        }\n")
DEPOSIT_CALL = "        DepositTally tally = DepositLegs(worker, carried, said);\n"
TOUR_AUDIT = "#379 tour write-ahead audit"


class TourWriteAheadIsPinned(CarveOutFixture):
    """The collection tour's plan (#379) is only a write-ahead record if the tour
    asks it BEFORE every pick and every trip to the chest, and tells it about a
    death before the tour is cancelled. The plan's own tests cannot reach the call
    site - the runtime binds Unity - so the order is pinned in the source. Each
    plant below was run against the validator from before this rule (origin/main
    36025a7), which let every one of them through.
    """

    _restore_path = TOUR_RUNTIME

    def swap_all(self, path, pairs):
        """Several exact replacements in one file, restored as one."""
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self._restore = original
        self._restore_path = path
        changed = original
        for old, new in pairs:
            self.assertEqual(1, changed.count(old), "the pinned text moved: " + old)
            changed = changed.replace(old, new, 1)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(changed)

    # -- a pick --

    def test_the_pick_intent_may_not_follow_the_pick(self):
        self.swap_all(TOUR_RUNTIME, [(PICK_GUARD, ""), (PICK_CALL_END, PICK_CALL_END + "\n" + PICK_GUARD)])
        self.assert_refused("a pick announced after it was made passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendPick(...))")

    def test_the_pick_intent_may_not_be_ignored(self):
        self.swap_in(TOUR_RUNTIME, PICK_GUARD,
                     "        _tourPlan.IntendPick(\"about to pick\", out string unwritten);\n")
        self.assert_refused("a pick whose announcement was not consulted passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendPick(...))")

    def test_a_pick_guard_that_does_not_leave_is_refused(self):
        self.swap_in(TOUR_RUNTIME, PICK_GUARD, PICK_GUARD.replace("            return;\n", ""))
        self.assert_refused("a pick guard with no return passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendPick(...))")

    def test_a_pick_may_not_be_lifted_into_a_helper(self):
        self.swap_in(
            TOUR_RUNTIME,
            "    private void PollTourPick(CollectionTour tour)\n",
            "    private PickRefusal ZzPick(Pickable source, Humanoid worker)\n"
            "    {\n"
            "        return _port.Begin(source, worker, featureEnabled: true, seamAvailable: true,\n"
            "            expectedItemPrefab: \"Stone\", expectedUnits: 1, nowSeconds: Time.time);\n"
            "    }\n\n"
            "    private void PollTourPick(CollectionTour tour)\n")
        self.assert_refused("a pick lifted into an unannounced helper passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendPick(...))")

    # -- a trip to the chest --

    def test_the_deposit_intent_may_not_follow_the_deposit(self):
        self.swap_all(TOUR_RUNTIME, [(DEPOSIT_GUARD, ""), (DEPOSIT_CALL, DEPOSIT_CALL + DEPOSIT_GUARD)])
        self.assert_refused("a deposit announced after it was made passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendDeposit(...))")

    def test_the_deposit_intent_may_not_be_ignored(self):
        self.swap_in(TOUR_RUNTIME, DEPOSIT_GUARD,
                     "        _tourPlan.IntendDeposit(\"about to deposit\", out string unwritten);\n")
        self.assert_refused("a deposit whose announcement was not consulted passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendDeposit(...))")

    def test_a_deposit_may_not_be_lifted_into_a_helper(self):
        self.swap_in(
            TOUR_RUNTIME,
            "    private void FinishTour(CollectionTour tour)\n",
            "    private DepositTally ZzDeposit(Humanoid worker, IReadOnlyList<KeyValuePair<string, int>> carried)\n"
            "    {\n"
            "        return DepositLegs(worker, carried, new StringBuilder());\n"
            "    }\n\n"
            "    private void FinishTour(CollectionTour tour)\n")
        self.assert_refused("a deposit lifted into an unannounced helper passed", marker=TOUR_AUDIT,
                            contains="if (!_tourPlan.IntendDeposit(...))")

    # -- the other half of the runtime --

    def test_the_tour_may_not_route_a_pick_through_the_other_half_of_the_runtime(self):
        self.swap_in(
            COLLECTION_RUNTIME,
            "    private string Designate()\n",
            "    private PickRefusal ZzRoutedPick(Pickable source, Humanoid worker)\n"
            "    {\n"
            "        return _port.Begin(source, worker, featureEnabled: true, seamAvailable: true,\n"
            "            expectedItemPrefab: \"Stone\", expectedUnits: 1, nowSeconds: Time.time);\n"
            "    }\n\n"
            "    private string Designate()\n")
        self.assert_refused("a second pick in the runtime's other half passed", marker=TOUR_AUDIT,
                            contains="inside OrderOnePick()")

    def test_another_part_of_the_runtime_may_not_deposit(self):
        path = os.path.join(WORKERS, "ZzTourPart.cs")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;\n"
                         "internal sealed partial class GunnarCollectionRuntime\n{\n"
                         "    private void ZzEmpty(Humanoid worker)\n"
                         "    {\n"
                         "        DepositLegs(worker, System.Array.Empty<System.Collections.Generic.KeyValuePair<string, int>>(),\n"
                         "            new System.Text.StringBuilder());\n"
                         "    }\n}\n")
        self._planted.append(path)
        self.assert_refused("a deposit from another part of the runtime passed", marker=TOUR_AUDIT,
                            contains="is a part of the collection runtime")

    def test_the_ordered_pick_may_not_lose_its_open_plan_refusal(self):
        self.swap_in(
            COLLECTION_RUNTIME,
            "            distanceMetres: FlatDistance(worker, source),\n"
            "            interruptedTourOpen: _tourPlan.BlocksOrders);\n",
            "            distanceMetres: FlatDistance(worker, source));\n")
        self.assert_refused("an ordered pick that ignores an open plan passed", marker=TOUR_AUDIT,
                            contains="does not pass `interruptedTourOpen")

    # -- D16 --

    def test_prepare_must_tell_the_plan_before_it_returns(self):
        self.swap_in(COLLECTION_RUNTIME, "                _tourPlan.PrepareForDeath();\n", "")
        self.assert_refused("a death the plan never heard of passed", marker=TOUR_AUDIT,
                            contains="Prepare must tell the plan")

    def test_died_must_tell_the_plan_before_the_tour_is_cancelled(self):
        self.swap_in(
            COLLECTION_RUNTIME,
            "            _tourPlan.Died(report.Held, report.Dropped, report.Unresolved, kept);\n",
            "            CancelTour(\"Gunnar died\");\n"
            "            _tourPlan.Died(report.Held, report.Dropped, report.Unresolved, kept);\n")
        self.assert_refused("a tour cancelled before the plan heard of the death passed", marker=TOUR_AUDIT,
                            contains="Died must tell the plan")
