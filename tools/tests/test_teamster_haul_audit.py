"""Planted violations for the manual chest-to-chest haul (#381 item 4).

The haul is glue over two pinned ports - D20's take and D15's deposit - and adds
no vanilla call of its own. Three rules carry it: `#381 manifest-haul audit`
(`check_teamster_manifest_haul_is_gated`), which pins the haul's three moves
behind their own gates; and the two narrow allowances it needed in rules that
already existed - `#381 chest-take audit` now admits ONE more `_take.TakeOne(`,
in the haul's file, and `#379 tour write-ahead audit` lets the haul's file call
`DepositLegs(` (and still never pick).

Each test plants an escape in the real source tree, runs the real validator and
requires it to refuse with the named rule. It then runs a copy of the validator
WITHOUT that rule and requires the same plant to pass: that is the proof the
escape is caught by that rule and not by something else. The `Cross*` tests
prove the two widened rules were widened by one file only.

tearDown restores every file byte for byte; the clean validator is run once
before the first plant and once after the last.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
WORKERS = os.path.join(TEAMSTER, "Adapters", "Workers")
HAUL = os.path.join(WORKERS, "GunnarCollectionRuntime.Haul.cs")
TOUR = os.path.join(WORKERS, "GunnarCollectionRuntime.Tour.cs")
CART_TOUR = os.path.join(WORKERS, "GunnarCollectionRuntime.CartTour.cs")
RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
DEFAULTS = os.path.join(TEAMSTER, "Domain", "Collection", "GunnarCollectionDefaults.cs")
SETTINGS = os.path.join(TEAMSTER, "TeamsterSettings.cs")

HAUL_RULE = "check_teamster_manifest_haul_is_gated"
TAKE_RULE = "check_teamster_take_moves_only_out_of_the_marked_chest"
PLAN_RULE = "check_teamster_tour_writes_ahead"
HAUL_PHRASE = "#381 manifest-haul audit"
TAKE_PHRASE = "#381 chest-take audit"
PLAN_PHRASE = "#379 tour write-ahead audit"


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class HaulIsGated(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        code, out = run(VALIDATOR)
        assert code == 0, "the tree must be clean before a plant means anything:\n" + out[-3000:]

    @classmethod
    def tearDownClass(cls):
        code, out = run(VALIDATOR)
        assert code == 0, "a haul plant was left behind:\n" + out[-3000:]

    def setUp(self):
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path in reversed(self._planted):
            if os.path.exists(path):
                os.remove(path)
        for path, original in reversed(self._restore):
            with open(path, "wb") as handle:
                handle.write(original)

    def _keep(self, path):
        with open(path, "rb") as handle:
            original = handle.read()
        if not any(kept == path for kept, _ in self._restore):
            self._restore.append((path, original))
        return original.decode("utf-8-sig") if original.startswith(b"\xef\xbb\xbf") else original.decode("utf-8")

    def _write(self, path, text):
        with open(path, "rb") as handle:
            bom = handle.read(3) == b"\xef\xbb\xbf"
        with open(path, "wb") as handle:
            handle.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))

    def swap_in(self, path, old, new, occurrence=None):
        text = self._keep(path)
        if occurrence is None:
            self.assertEqual(1, text.count(old), "pinned text moved: " + old)
            text = text.replace(old, new, 1)
        else:
            self.assertGreater(text.count(old), occurrence, "pinned text moved: " + old)
            at = -1
            for _ in range(occurrence + 1):
                at = text.index(old, at + 1)
            text = text[:at] + new + text[at + len(old):]
        self._write(path, text)

    def append_member(self, path, member):
        """Adds a member just inside the file's last closing brace - the class."""
        text = self._keep(path)
        at = text.rstrip().rfind("}")
        self.assertGreater(at, 0)
        self._write(path, text[:at] + member + "\n" + text[at:])

    def without(self, rules):
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        for rule in rules:
            registration = "    report.extend(" + rule + "(errors))\n"
            self.assertEqual(1, source.count(registration), rule + " is not registered exactly once")
            source = source.replace(registration, "")
        path = os.path.join(ROOT, "tools", "_zz_without_haul_rules.py")
        self.assertFalse(os.path.exists(path), "a previous copy was left behind")
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(source)
        return path

    def assert_refused(self, why, phrase=HAUL_PHRASE, rules=(HAUL_RULE,)):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        refusals = [line for line in out.splitlines() if line.startswith("ERROR: ") and phrase in line]
        self.assertTrue(refusals, why + ": refused, but not by " + phrase + "\n" + out[-3000:])
        code, out = run(self.without(rules))
        self.assertEqual(0, code, why + ": without " + ", ".join(rules) + " something else still refuses it, so "
                         "the plant does not prove the rule\n" + out[-3000:])

    # --- the take leg ---------------------------------------------------------------------------

    def test_a_take_the_gate_did_not_refuse(self):
        self.swap_in(HAUL, "if (refusal != TakeRefusal.None)", "if (refusal == TakeRefusal.None && false)")
        self.assert_refused("a take with its gate's refusal never acted on")

    def test_a_take_gate_that_refuses_without_returning(self):
        self.swap_in(HAUL, "detail = TakeSentences.Describe(refusal, request.Authority);\n            return HaulLegEnd.Refused;",
                     "detail = TakeSentences.Describe(refusal, request.Authority);")
        self.assert_refused("a take gate whose refusal falls through to the take")

    def test_a_take_gated_on_the_take_switch_instead_of_the_hauls(self):
        self.swap_in(HAUL, "takeEnabled: HaulOptedIn()", "takeEnabled: TakeOptedIn()")
        self.assert_refused("a haul take that ignores Workers/GunnarChestHaulEnabled")

    def test_a_take_that_ignores_what_else_is_in_flight(self):
        self.swap_in(HAUL, "transferInFlight: _ordered || _tour != null || _port.Phase == PickPhase.Gathering",
                     "transferInFlight: false", occurrence=0)
        self.assert_refused("a haul take that does not ask what else is in flight")

    def test_a_take_that_ignores_an_open_tour_plan(self):
        self.swap_in(HAUL, "interruptedTourOpen: _tourPlan.BlocksOrders,\n            sourceDesignated:",
                     "interruptedTourOpen: false,\n            sourceDesignated:")
        self.assert_refused("a haul take while an interrupted tour's plan is open")

    def test_a_take_that_asks_for_more_than_fits(self):
        self.swap_in(HAUL, "TakeOrderGate.UnitsToAsk(wanted, fits!.Value)", "wanted")
        self.assert_refused("a haul take not bounded by the carry limit")

    def test_a_second_take_in_the_haul(self):
        self.append_member(HAUL, "\n    private void Again(Humanoid worker)\n    {\n        _take.TakeOne(_source, worker, "
                                 "_sourceKey, \"Stone\", 1, _sourceEpoch, _sourceEpoch, Allowance, WorkerPosition, 1f, "
                                 "out NpcContainerRefusal _);\n    }\n")
        self.assert_refused("a second, ungated take in the haul's file")

    def test_a_take_lifted_into_a_helper_with_no_gate(self):
        self.swap_in(HAUL, "TakeResult result = _take.TakeOne(", "TakeResult result = Lifted(")
        self.append_member(HAUL, "\n    private TakeResult Lifted(Container? source, Humanoid worker, string key, string cargo, "
                                 "int units, NpcWorldEpoch chosen, NpcWorldEpoch world, Func<Container?, NpcContainerUse> "
                                 "allow, Func<Vector3?> where, float reach, out NpcContainerRefusal why)\n    {\n"
                                 "        return _take.TakeOne(source, worker, key, cargo, units, chosen, world, allow, where, "
                                 "reach, out why);\n    }\n")
        self.assert_refused("the take moved into a helper above which no gate is written")

    # --- the unload leg -------------------------------------------------------------------------

    def test_an_unload_the_gate_did_not_refuse(self):
        self.swap_in(HAUL, "if (refusal != DepositRefusal.None)", "if (refusal == DepositRefusal.Unreadable)")
        self.assert_refused("an unload whose gate refuses only one case")

    def test_an_unload_that_offers_everything_he_holds(self):
        self.swap_in(HAUL, "int offered = holding < wanted ? holding : wanted;", "int offered = holding;")
        self.assert_refused("an unload not bounded by what the tour measured taking")

    def test_an_unload_gated_on_collection_alone(self):
        self.swap_in(HAUL, "featureEnabled: HaulOptedIn()", "featureEnabled: OptedIn()")
        self.assert_refused("a haul unload that ignores the haul's own switch")

    def test_an_unload_that_ignores_what_else_is_in_flight(self):
        self.swap_in(HAUL, "transferInFlight: _ordered || _tour != null || _port.Phase == PickPhase.Gathering",
                     "transferInFlight: _ordered", occurrence=1)
        self.assert_refused("a haul unload that does not ask whether a tour is running")

    def test_a_deposit_straight_through_the_port(self):
        self.append_member(HAUL, "\n    private void Straight(Humanoid worker)\n    {\n        _deposit.DepositOne(worker, "
                                 "_destination, _destinationKey, \"Stone\", 1, _destinationEpoch, _destinationEpoch, "
                                 "Allowance, WorkerPosition, 1f, out NpcContainerRefusal _);\n    }\n")
        self.assert_refused("a deposit that skips DepositLegs' world check and ledger")

    # --- the start ------------------------------------------------------------------------------

    def test_a_start_the_gate_did_not_refuse(self):
        self.swap_in(HAUL, "if (refusal != ChestHaulRefusal.None)", "if (refusal == ChestHaulRefusal.HaulOff)")
        self.assert_refused("a haul started past most of its gate")

    def test_a_start_with_a_cart_assigned(self):
        self.swap_in(HAUL, "cartAssigned: CartHeld(),", "cartAssigned: false,")
        self.assert_refused("a haul that would walk him off an assigned cart")

    def test_a_start_while_he_already_carries_the_cargo(self):
        self.swap_in(HAUL, "carryingCargo: carrying.Count > 0);", "carryingCargo: false);")
        self.assert_refused("a haul whose reconcile would count cargo it never measured taking")

    def test_a_manifest_that_counts_a_cart(self):
        self.swap_in(HAUL, "new HaulCapacity(carry.FreeKilograms, freeSlots), CartCapacity.None);",
                     "new HaulCapacity(carry.FreeKilograms, freeSlots), new CartCapacity(true, 18, 50));")
        self.assert_refused("a manifest planned with cart room no grant covers")

    def test_a_request_gate_offered_a_cart(self):
        self.swap_in(HAUL, "            carry,\n            CartCapacity.None);",
                     "            carry,\n            new CartCapacity(true, 18, 50));")
        self.assert_refused("a request gate admitting a haul on cart room no grant covers")

    def test_a_cart_load_from_the_haul(self):
        self.append_member(HAUL, "\n    private object Cart() => _cartLoad;\n")
        self.assert_refused("a haul reaching the cart-load port")

    def test_a_haul_built_somewhere_else(self):
        self.append_member(TOUR, "\n    private void Elsewhere()\n    {\n        _haul = new ChestHaul(null!, default, "
                                 "default, null!);\n    }\n")
        self.assert_refused("a ChestHaul constructed outside the haul's gated start")

    # --- the switches ---------------------------------------------------------------------------

    def test_the_haul_on_by_default(self):
        self.swap_in(DEFAULTS, "public const bool ChestHaulEnabled = false;", "public const bool ChestHaulEnabled = true;")
        self.assert_refused("the haul on by default")

    def test_the_switch_bound_to_something_else(self):
        self.swap_in(SETTINGS, 'config.Bind("Workers", "GunnarChestHaulEnabled", GunnarCollectionDefaults.ChestHaulEnabled,',
                     'config.Bind("Workers", "GunnarChestHaulEnabled", true,')
        self.assert_refused("the switch bound to a literal on")

    def test_an_opt_in_that_skips_the_hauls_own_switch(self):
        self.swap_in(HAUL, "return TakeOptedIn() && _settings.GunnarChestHaulEnabled.Value;", "return TakeOptedIn();")
        self.assert_refused("the haul's opt-in reading only the take's")

    def test_a_haul_no_longer_watched_every_frame(self):
        self.swap_in(RUNTIME, "            WatchHaulFromFrame();\n", "")
        self.assert_refused("a running haul whose switches and authority are never re-asked")

    # --- the two widened rules, widened by one file only ----------------------------------------

    def test_cross_a_take_in_another_part_of_the_runtime(self):
        self.append_member(TOUR, "\n    private void TakeHere(Humanoid worker)\n    {\n        _take.TakeOne(_source, worker, "
                                 "_sourceKey, \"Stone\", 1, _sourceEpoch, _sourceEpoch, Allowance, WorkerPosition, 1f, "
                                 "out NpcContainerRefusal _);\n    }\n")
        self.assert_refused("a take in the tour file", TAKE_PHRASE, (TAKE_RULE,))

    def test_cross_a_deposit_in_another_part_of_the_runtime(self):
        self.append_member(CART_TOUR, "\n    private void DepositHere(Humanoid worker)\n    {\n        DepositLegs(worker, "
                                      "System.Array.Empty<System.Collections.Generic.KeyValuePair<string, int>>(), "
                                      "new System.Text.StringBuilder());\n    }\n")
        self.assert_refused("a deposit in the cart tour's file", PLAN_PHRASE, (PLAN_RULE,))

    def test_cross_a_pick_in_the_haul(self):
        self.append_member(HAUL, "\n    private void PickHere()\n    {\n        _port.Begin(null!);\n    }\n")
        self.assert_refused("a pick in the haul's file", PLAN_PHRASE, (PLAN_RULE,))


if __name__ == "__main__":
    unittest.main()
