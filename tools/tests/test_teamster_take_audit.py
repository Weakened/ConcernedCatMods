"""Planted violations for D20's chest take (owner decision 2026-10-31, #381).

D20 grants Gunnar the D15 deposit's three vanilla calls with the two roles
exchanged - out of a chest the player marked Take or Both, into his own
inventory - in one file, `Adapters/Workers/GunnarTakePort.cs`, behind its own
off-by-default switch. Two rules carry it: the take port's entry in the
worker-runtime scope audit's pinned-call table (each call verbatim and once),
and `check_teamster_take_moves_only_out_of_the_marked_chest` ("#381 chest-take
audit"), which pins what a call pin cannot see.

Each test plants an escape in the real source tree, runs the real validator and
requires it to refuse. It then runs a copy of the validator WITHOUT those rules -
the dedicated audit's registration removed, and the take port's pinned calls
relaxed to the bare tokens, still once each - and requires the same plant to pass: that is the
proof the escape is caught by the take rules and not by something else.
tearDown restores every file byte for byte and re-runs the clean validator.

The escapes that every authorized port already shares - a call in another file,
a second file of the port's name elsewhere, a second call in the port, a space or a newline before the
paren - are refused by the path-keyed scope audit whatever the take rules say,
so they are proved once, in `test_teamster_carveout.py`, and not here.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
WORKERS = os.path.join(TEAMSTER, "Adapters", "Workers")
PORT = os.path.join(WORKERS, "GunnarTakePort.cs")
TAKE_RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.Take.cs")
RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
DEFAULTS = os.path.join(TEAMSTER, "Domain", "Collection", "GunnarCollectionDefaults.cs")
SETTINGS = os.path.join(TEAMSTER, "TeamsterSettings.cs")
COMMAND = os.path.join(WORKERS, "CollectConsoleCommand.cs")

RULE = "check_teamster_take_moves_only_out_of_the_marked_chest"
TABLE = "TEAMSTER_AUTHORIZED_PORTS = {"
# The take port's three calls, relaxed from their exact spelling to the bare
# token - any receiver, any arguments - and still counted once each by the
# table's own mechanism, which every port shares.
RELAX = (r'''TEAMSTER_TAKE_PORT_CALLS = tuple((token, re.compile(r"\.\s*" + re.escape(token.strip(".(")) + r"\s*\("), what)'''
         r''' for token, _pattern, what in TEAMSTER_TAKE_PORT_CALLS)''' + "\n")


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class ChestTakeIsNarrow(unittest.TestCase):
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
        self.assertEqual(0, code, "a take plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, "rb") as handle:
            original = handle.read()
        text = original.decode("utf-8")
        self.assertEqual(1, text.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "wb") as handle:
            handle.write(text.replace(old, new, 1).encode("utf-8"))

    def without_the_take_rules(self):
        """The validator with the chest-take audit unregistered and the take
        port's pinned calls relaxed to their bare tokens."""
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + RULE + "(errors))\n"
        self.assertEqual(1, source.count(registration), "the rule is not registered exactly once")
        self.assertEqual(1, source.count(TABLE), "the pinned-call table moved")
        source = source.replace(registration, "").replace(TABLE, RELAX + TABLE)
        path = os.path.join(ROOT, "tools", "_zz_without_take_rules.py")
        self.assertFalse(os.path.exists(path), "a previous copy was left behind")
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(source)
        return path

    def assert_refused(self, why, phrase):
        # The success line carries each audit's label too, so the phrase is
        # looked for only on a line the validator printed as an ERROR.
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        refusals = [line for line in out.splitlines() if line.startswith("ERROR: ") and phrase in line]
        self.assertTrue(refusals, why + ": refused, but not by the take rules\n" + out[-3000:])
        code, out = run(self.without_the_take_rules())
        self.assertEqual(0, code, why + ": without the take rules something else still refuses it, so the "
                         "plant does not prove them\n" + out[-3000:])

    def assert_pin_refused(self, why):
        self.assert_refused(why, "Adapters/Workers/GunnarTakePort.cs")

    def assert_audit_refused(self, why):
        self.assert_refused(why, "#381 chest-take audit")

    # --- the three pinned calls -----------------------------------------------------------------

    def test_the_take_may_not_be_reversed_into_a_deposit(self):
        # The same token and the same argument names, the other way round: the
        # move would put Gunnar's material into the chest - a deposit with none
        # of D15's own gates on it.
        self.swap_in(PORT, "gunnar.MoveItemToThis(chest, stack);", "chest.MoveItemToThis(gunnar, stack);")
        self.assert_pin_refused("the take was reversed into a deposit")

    def test_the_remove_may_not_take_the_count_that_was_asked_for(self):
        # Removing what was ASKED FOR destroys whatever did not fit in him: the
        # remainder D20 says stays in the chest.
        self.swap_in(PORT, "chest.RemoveItem(stack, arrived);", "chest.RemoveItem(stack, remaining);")
        self.assert_pin_refused("a remove of the asked-for count passed")

    def test_the_remove_may_not_come_out_of_gunnar(self):
        self.swap_in(PORT, "chest.RemoveItem(stack, arrived);", "gunnar.RemoveItem(stack, arrived);")
        self.assert_pin_refused("a remove from Gunnar instead of the chest passed")

    def test_the_add_may_not_go_into_the_chest(self):
        self.swap_in(PORT, "bool added = gunnar.AddItem(part);", "bool added = chest.AddItem(part);")
        self.assert_pin_refused("an add into the chest passed in the take port")

    def test_the_add_may_only_be_the_clone(self):
        self.swap_in(PORT, "bool added = gunnar.AddItem(part);", "bool added = gunnar.AddItem(stack);")
        self.assert_pin_refused("an add of something other than the clone passed")

    # --- which inventory is which -----------------------------------------------------------------

    def test_the_derivations_may_not_be_reversed(self):
        self.swap_in(PORT,
                     "        Inventory? chest = InventoryOf(source);\n"
                     "        Inventory? gunnar = InventoryOf(worker);",
                     "        Inventory? chest = InventoryOf(worker);\n"
                     "        Inventory? gunnar = InventoryOf(source);")
        self.assert_audit_refused("the take was reversed by swapping its derivations")

    def test_the_mover_may_not_take_two_interchangeable_inventories(self):
        self.swap_in(PORT,
                     "    private MoveTally Move(Container? source, Humanoid? worker, string itemPrefab, int units)",
                     "    private MoveTally Move(Inventory chest, Inventory gunnar, string itemPrefab, int units)")
        self.assert_audit_refused("the mover went back to two renameable inventories")

    def test_the_roles_may_not_be_swapped_after_the_derivations(self):
        # The review's first finding: both derivations stay pinned and every
        # call stays byte-identical, and a deconstruction one line below them
        # turns the take into a deposit out of Gunnar.
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        (chest, gunnar) = (gunnar, chest);\n        int remaining = units;\n")
        self.assert_audit_refused("the roles were swapped after the pinned derivations")

    def test_a_role_may_not_be_reassigned_after_its_derivation(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Inventory? held = chest;\n        chest = gunnar;\n        gunnar = held;\n"
                     "        int remaining = units;\n")
        self.assert_audit_refused("a role was reassigned after its derivation")

    def test_a_role_may_not_be_rewritten_through_a_reference(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Exchange(ref chest, ref gunnar);\n        int remaining = units;\n")
        self.assert_audit_refused("a role was rewritten through a ref argument")

    def test_a_nested_deconstruction_may_not_swap_the_roles(self):
        # The re-review's third finding: one paren level was all the first
        # counter looked at.
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        (chest, (gunnar, _)) = (gunnar, (chest, 0));\n        int remaining = units;\n")
        self.assert_audit_refused("a nested deconstruction swapped the roles")

    def test_the_calls_may_not_move_into_a_helper_whose_parameters_swap_the_roles(self):
        # The re-review's first finding: parameters are bindings, not writes, so
        # `Shift(chest, gunnar, ...)` into `Shift(Inventory gunnar, Inventory
        # chest, ...)` exchanged the roles with every pin still matching.
        self.swap_in(PORT, "                    gunnar.MoveItemToThis(chest, stack);\n",
                     "                    Shift(chest, gunnar, stack);\n")
        self.swap_in(PORT, "    private static Inventory? InventoryOf(Humanoid? worker)",
                     "    private static void Shift(Inventory gunnar, Inventory chest, ItemDrop.ItemData stack)\n"
                     "    {\n        gunnar.MoveItemToThis(chest, stack);\n    }\n\n"
                     "    private static Inventory? InventoryOf(Humanoid? worker)")
        self.assert_audit_refused("the calls moved into a helper whose parameters swap the roles")

    def test_a_local_function_in_the_mover_may_not_shadow_the_roles(self):
        # The re-review's second finding: in this language version a local
        # function may shadow the mover's locals.
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        void Go(Inventory gunnar, Inventory chest) { }\n        int remaining = units;\n")
        self.assert_audit_refused("a local function in the mover shadowed the roles")

    def test_a_lambda_in_the_mover_may_not_shadow_the_roles(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Action<Inventory> go = chest => { };\n        int remaining = units;\n")
        self.assert_audit_refused("a lambda in the mover shadowed a role")

    # --- nothing built from a name --------------------------------------------------------------

    def test_the_clone_must_come_from_the_chests_own_stack(self):
        self.swap_in(PORT, "ItemDrop.ItemData part = stack.Clone();",
                     "ItemDrop.ItemData part = Template(itemPrefab).Clone();")
        self.assert_audit_refused("an item templated from a name rode the authorized add")

    def test_the_clone_may_not_be_reassigned_before_the_add(self):
        self.swap_in(PORT, "                    part.m_equipped = false;\n",
                     "                    part.m_equipped = false;\n                    part = Template(itemPrefab);\n")
        self.assert_audit_refused("the clone was replaced between the clone and the add")

    def test_a_second_item_lookup_is_refused(self):
        self.swap_in(PORT, "            ObjectDB database = ObjectDB.instance;\n",
                     "            ObjectDB database = ObjectDB.instance;\n"
                     "            GameObject? another = database.GetItemPrefab(\"Stone\");\n")
        self.assert_audit_refused("a second item lookup appeared in the take port")

    def test_an_instantiate_is_refused(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        int remaining = units;\n        var made = UnityEngine.Object.Instantiate(_matching[0].m_dropPrefab);\n")
        self.assert_audit_refused("an Instantiate in the take port passed")

    # --- the mint asks for Take ---------------------------------------------------------------------

    def test_the_mint_may_not_ask_for_deposit(self):
        # A chest the player marked for putting things in only would then
        # authorize taking everything out of it.
        self.swap_in(PORT, "subject, NpcContainerUse.Take, world, out refusal",
                     "subject, NpcContainerUse.Deposit, world, out refusal")
        self.assert_audit_refused("the take's mint asked for Deposit")

    def test_the_mint_may_not_ask_for_both(self):
        self.swap_in(PORT, "subject, NpcContainerUse.Take, world, out refusal",
                     "subject, NpcContainerUse.Both, world, out refusal")
        self.assert_audit_refused("the take's mint asked for Both")

    # --- one call site, behind its own switch and gate ---------------------------------------------

    def test_a_second_call_site_is_refused(self):
        self.swap_in(COMMAND, "internal sealed class CollectConsoleCommand",
                     "internal static class ZzSecondTake\n{\n"
                     "    internal static object Take(GunnarTakePort port) => port.TakeOne(null, null, \"\", \"Stone\", 1, "
                     "default, default, _ => default, () => null, 1f, out _);\n}\n\n"
                     "internal sealed class CollectConsoleCommand")
        self.assert_audit_refused("a take from a second call site passed")

    def test_a_second_call_through_an_alias_in_the_take_part_is_refused(self):
        # The review's second finding: `_take.TakeOne(` still counts one, and a
        # helper above the gated method calls the port through a local.
        self.swap_in(TAKE_RUNTIME, "    private static string DescribeTake(",
                     "    private TakeResult Early(Humanoid? worker)\n    {\n        GunnarTakePort port = _take;\n"
                     "        return port.TakeOne(_source, worker, _sourceKey, \"Stone\", 1, _sourceEpoch, "
                     "CurrentEpoch(), Allowance, WorkerPosition, 1f, out _);\n    }\n\n"
                     "    private static string DescribeTake(")
        self.assert_audit_refused("a second take through an alias passed")

    def test_the_gate_may_not_be_told_authority_is_granted(self):
        # The review's third finding: the switches were pinned and the
        # work-authority rule - host, not dedicated, no peers - was not.
        self.swap_in(TAKE_RUNTIME, "authority: TakeAuthority(),", "authority: WorkAuthorityVerdict.Granted,")
        self.assert_audit_refused("the take gate was told authority was granted")

    def test_the_gate_may_not_be_told_the_probe_passed(self):
        self.swap_in(TAKE_RUNTIME, "seamAvailable: _seamAvailable(),", "seamAvailable: true,")
        self.assert_audit_refused("the take gate was told the probe passed")

    def test_the_gate_may_not_be_told_his_record_is_fine(self):
        self.swap_in(TAKE_RUNTIME, "workerRecordUnwritable: _recordUnwritable(),", "workerRecordUnwritable: false,")
        self.assert_audit_refused("the take gate was told his record is writable")

    def test_the_gate_may_not_be_told_nothing_is_in_flight(self):
        self.swap_in(TAKE_RUNTIME, "transferInFlight: _ordered || TourRunning || _port.Phase == PickPhase.Gathering,",
                     "transferInFlight: false,")
        self.assert_audit_refused("the take gate was told nothing is in flight")

    def test_the_gate_may_not_live_in_a_helper_nobody_calls(self):
        # The review's fourth finding: the order check read the whole file, so a
        # gate moved into a dead helper above the take method satisfied it.
        self.swap_in(TAKE_RUNTIME, "        TakeRefusal refusal = TakeOrderGate.Evaluate(request);\n"
                                   "        if (refusal != TakeRefusal.None)\n"
                                   "        {\n"
                                   "            return TakeSentences.Describe(refusal, request.Authority);\n"
                                   "        }\n",
                     "        Unused(request);\n")
        self.swap_in(TAKE_RUNTIME, "    private string DesignateSource()",
                     "    private string Unused(TakeRequest request)\n    {\n"
                     "        TakeRefusal refusal = TakeOrderGate.Evaluate(request);\n"
                     "        if (refusal != TakeRefusal.None)\n"
                     "        {\n"
                     "            return TakeSentences.Describe(refusal, request.Authority);\n"
                     "        }\n\n        return string.Empty;\n    }\n\n"
                     "    private string DesignateSource()")
        self.assert_audit_refused("the take gate was moved into a helper nobody calls")

    def test_a_second_port_is_refused(self):
        self.swap_in(RUNTIME, "    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();\n",
                     "    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();\n"
                     "    private readonly GunnarTakePort _other = new GunnarTakePort();\n")
        self.assert_audit_refused("a second take port was built")

    def test_the_gate_may_not_be_told_a_literal(self):
        self.swap_in(TAKE_RUNTIME, "takeEnabled: TakeOptedIn(),", "takeEnabled: true,")
        self.assert_audit_refused("the take gate was told a literal instead of the switch")

    def test_the_opt_in_must_read_the_take_switch(self):
        self.swap_in(TAKE_RUNTIME, "return OptedIn() && _settings.GunnarChestTakeEnabled.Value;",
                     "return OptedIn();")
        self.assert_audit_refused("the take ran on the collection switch alone")

    def test_the_take_is_off_by_default(self):
        self.swap_in(DEFAULTS, "public const bool ChestTakeEnabled = false;",
                     "public const bool ChestTakeEnabled = true;")
        self.assert_audit_refused("the take was switched on by default")

    def test_the_switch_is_bound_to_the_off_default(self):
        self.swap_in(SETTINGS,
                     'config.Bind("Workers", "GunnarChestTakeEnabled", GunnarCollectionDefaults.ChestTakeEnabled,',
                     'config.Bind("Workers", "GunnarChestTakeEnabled", true,')
        self.assert_audit_refused("the take switch was bound to on")

    def test_the_gate_may_not_be_skipped(self):
        self.swap_in(TAKE_RUNTIME, "TakeRefusal refusal = TakeOrderGate.Evaluate(request);",
                     "TakeRefusal refusal = TakeRefusal.None;")
        self.assert_audit_refused("the take gate was skipped")

    def test_the_gates_refusal_must_return(self):
        self.swap_in(TAKE_RUNTIME,
                     "        if (refusal != TakeRefusal.None)\n"
                     "        {\n"
                     "            return TakeSentences.Describe(refusal, request.Authority);\n"
                     "        }\n",
                     "        if (refusal != TakeRefusal.None)\n"
                     "        {\n"
                     "            _log?.LogInfo(TakeSentences.Describe(refusal, request.Authority));\n"
                     "        }\n")
        self.assert_audit_refused("the take gate's refusal was consulted and ignored")

    def test_an_open_tour_plan_must_refuse(self):
        self.swap_in(TAKE_RUNTIME, "interruptedTourOpen: _tourPlan.BlocksOrders,", "interruptedTourOpen: false,")
        self.assert_audit_refused("a take ignored an open tour plan")

    def test_the_source_may_not_be_set_from_anything_but_the_pointed_chest(self):
        self.swap_in(TAKE_RUNTIME, "        _source = chest;\n", "        _source = _destination;\n")
        self.assert_audit_refused("the source was set from something other than the pointed chest")

    def test_the_source_may_not_be_set_in_another_part_of_the_runtime(self):
        self.swap_in(RUNTIME, "        _destination = chest;\n", "        _destination = chest;\n        _source = chest;\n")
        self.assert_audit_refused("another part of the runtime set the take's source")


if __name__ == "__main__":
    unittest.main()
