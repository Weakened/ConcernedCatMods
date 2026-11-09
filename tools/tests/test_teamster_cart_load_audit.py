"""Planted violations for D22's cart load (owner decision 2026-10-31, #381).

D22 grants Gunnar's collection tour the D15 deposit's three vanilla calls with a
different destination - out of his own inventory, into the container of the
cart the player explicitly assigned him - in one file,
`Adapters/Workers/GunnarCartLoadPort.cs`, behind its own off-by-default switch.
Two rules carry it: the cart-load port's entry in the worker-runtime scope
audit's pinned-call table (each call verbatim and once), and
`check_teamster_cart_load_moves_only_into_the_assigned_cart` ("#381 cart-load
audit"), which pins what a call pin cannot see: which inventory is which, that
nothing is built from a name, that the cart is the assigned one and is only
loaded, and that the one load is reached only behind its own switch and gates.

Each test plants an escape in the real source tree, runs the real validator and
requires it to refuse. It then runs a copy of the validator WITHOUT those rules -
the dedicated audit's registration removed, and the cart-load port's pinned
calls relaxed to the bare tokens, still once each - and requires the same plant
to pass: that is the proof the escape is caught by the cart-load rules and not
by something else. tearDown restores every file byte for byte and re-runs the
clean validator.

The escapes that every authorized port already shares - a call in another file,
a second file of the port's name elsewhere, a second call in the port, a space
or a newline before the paren - are refused by the path-keyed scope audit
whatever the cart-load rules say, so they are proved once, in
`test_teamster_carveout.py`, and not here. The role-exchange escapes the take's
five reviews found are planted again here against this port, because the shared
role audit is only as good as the arguments each port hands it.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
WORKERS = os.path.join(TEAMSTER, "Adapters", "Workers")
PORT = os.path.join(WORKERS, "GunnarCartLoadPort.cs")
SUBJECT = os.path.join(WORKERS, "AssignedCartContainer.cs")
CART_RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.CartTour.cs")
TOUR = os.path.join(WORKERS, "GunnarCollectionRuntime.Tour.cs")
RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
HAULING = os.path.join(WORKERS, "GunnarHaulingRuntime.cs")
DEFAULTS = os.path.join(TEAMSTER, "Domain", "Collection", "GunnarCollectionDefaults.cs")
SETTINGS = os.path.join(TEAMSTER, "TeamsterSettings.cs")
COMMAND = os.path.join(WORKERS, "CollectConsoleCommand.cs")

RULE = "check_teamster_cart_load_moves_only_into_the_assigned_cart"
TABLE = "TEAMSTER_AUTHORIZED_PORTS = {"
RELAX = (r'''TEAMSTER_CART_LOAD_PORT_CALLS = tuple((token, re.compile(r"\.\s*" + re.escape(token.strip(".(")) + r"\s*\("), what)'''
         r''' for token, _pattern, what in TEAMSTER_CART_LOAD_PORT_CALLS)''' + "\n")


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class CartLoadIsNarrow(unittest.TestCase):
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
        self.assertEqual(0, code, "a cart-load plant was left behind:\n" + out[-3000:])

    def swap_in(self, path, old, new):
        with open(path, "rb") as handle:
            original = handle.read()
        text = original.decode("utf-8")
        self.assertEqual(1, text.count(old), "pinned text moved: " + old)
        if not any(saved == path for saved, _ in self._restore):
            self._restore.append((path, original))
        with open(path, "wb") as handle:
            handle.write(text.replace(old, new, 1).encode("utf-8"))

    def without_the_cart_load_rules(self):
        """The validator with the cart-load audit unregistered and the cart-load
        port's pinned calls relaxed to their bare tokens."""
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + RULE + "(errors))\n"
        self.assertEqual(1, source.count(registration), "the rule is not registered exactly once")
        self.assertEqual(1, source.count(TABLE), "the pinned-call table moved")
        source = source.replace(registration, "").replace(TABLE, RELAX + TABLE)
        path = os.path.join(ROOT, "tools", "_zz_without_cart_load_rules.py")
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
        self.assertTrue(refusals, why + ": refused, but not by the cart-load rules\n" + out[-3000:])
        code, out = run(self.without_the_cart_load_rules())
        self.assertEqual(0, code, why + ": without the cart-load rules something else still refuses it, so the "
                         "plant does not prove them\n" + out[-3000:])

    def assert_pin_refused(self, why):
        self.assert_refused(why, "Adapters/Workers/GunnarCartLoadPort.cs")

    def assert_audit_refused(self, why):
        self.assert_refused(why, "#381 cart-load audit")

    # --- the three pinned calls -----------------------------------------------------------------

    def test_the_load_may_not_be_reversed_into_an_unload(self):
        # The same token and the same argument names, the other way round: the
        # move would empty the cart into Gunnar - a take D22 never granted.
        self.swap_in(PORT, "cart.MoveItemToThis(gunnar, stack);", "gunnar.MoveItemToThis(cart, stack);")
        self.assert_pin_refused("the load was reversed into an unload")

    def test_the_remove_may_not_take_the_count_that_was_asked_for(self):
        # Removing what was ASKED FOR destroys whatever did not fit in the cart:
        # the remainder D22 says stays in him.
        self.swap_in(PORT, "gunnar.RemoveItem(stack, arrived);", "gunnar.RemoveItem(stack, remaining);")
        self.assert_pin_refused("a remove of the asked-for count passed")

    def test_the_remove_may_not_come_out_of_the_cart(self):
        self.swap_in(PORT, "gunnar.RemoveItem(stack, arrived);", "cart.RemoveItem(stack, arrived);")
        self.assert_pin_refused("a remove from the cart instead of Gunnar passed")

    def test_the_add_may_not_go_into_gunnar(self):
        self.swap_in(PORT, "bool added = cart.AddItem(part);", "bool added = gunnar.AddItem(part);")
        self.assert_pin_refused("an add into Gunnar passed in the cart-load port")

    def test_the_add_may_only_be_the_clone(self):
        self.swap_in(PORT, "bool added = cart.AddItem(part);", "bool added = cart.AddItem(stack);")
        self.assert_pin_refused("an add of something other than the clone passed")

    # --- which inventory is which -----------------------------------------------------------------

    def test_the_derivations_may_not_be_reversed(self):
        self.swap_in(PORT,
                     "        Inventory? gunnar = InventoryOf(worker);\n"
                     "        Inventory? cart = InventoryOf(AssignedCartContainer.BedOf(assigned));\n"
                     "        if (gunnar == null || cart == null",
                     "        Inventory? gunnar = InventoryOf(AssignedCartContainer.BedOf(assigned));\n"
                     "        Inventory? cart = InventoryOf(worker);\n"
                     "        if (gunnar == null || cart == null")
        self.assert_audit_refused("the load was reversed by swapping its derivations")

    def test_the_destination_may_not_be_derived_from_a_chest(self):
        # A container handed in from outside is a container somebody else chose;
        # the bed is derived from the assigned cart, in the mover.
        self.swap_in(PORT,
                     "    private MoveTally Move(Humanoid? worker, Vagon? assigned, string itemPrefab, int units)",
                     "    private MoveTally Move(Humanoid? worker, Container? destination, string itemPrefab, int units)")
        self.assert_audit_refused("the mover took a container instead of the assigned cart")

    def test_the_mover_may_not_take_two_interchangeable_inventories(self):
        self.swap_in(PORT,
                     "    private MoveTally Move(Humanoid? worker, Vagon? assigned, string itemPrefab, int units)",
                     "    private MoveTally Move(Inventory gunnar, Inventory cart, string itemPrefab, int units)")
        self.assert_audit_refused("the mover went back to two renameable inventories")

    def test_the_roles_may_not_be_swapped_after_the_derivations(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        (gunnar, cart) = (cart, gunnar);\n        int remaining = units;\n")
        self.assert_audit_refused("the roles were swapped after the pinned derivations")

    def test_a_role_may_not_be_reassigned_after_its_derivation(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Inventory? held = cart;\n        cart = gunnar;\n        gunnar = held;\n"
                     "        int remaining = units;\n")
        self.assert_audit_refused("a role was reassigned after its derivation")

    def test_a_role_may_not_be_rewritten_through_a_reference(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Exchange(ref gunnar, ref cart);\n        int remaining = units;\n")
        self.assert_audit_refused("a role was rewritten through a ref argument")

    def test_a_nested_deconstruction_may_not_swap_the_roles(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        (gunnar, (cart, _)) = (cart, (gunnar, 0));\n        int remaining = units;\n")
        self.assert_audit_refused("a nested deconstruction swapped the roles")

    def test_a_guarded_deconstruction_may_not_swap_the_roles(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        if (units > 0) (gunnar, cart) = (cart, gunnar);\n        int remaining = units;\n")
        self.assert_audit_refused("a deconstruction behind an if swapped the roles")

    def test_an_overload_may_not_return_the_other_partys_inventory(self):
        self.swap_in(PORT, "return container == null ? null : container.GetInventory();",
                     "return _held == null ? null : _held.GetInventory();")
        self.assert_audit_refused("an overload returned the other party's inventory")

    def test_an_overload_may_not_answer_from_a_field_cache(self):
        self.swap_in(PORT, "return container == null ? null : container.GetInventory();",
                     "return _g ?? (container == null ? null : container.GetInventory());")
        self.assert_audit_refused("an overload answered from a field cache")

    def test_the_port_may_not_declare_another_type(self):
        self.swap_in(PORT, "    private readonly List<ItemDrop.ItemData> _matching",
                     "    private static class Stash { internal static Inventory? G; }\n\n"
                     "    private readonly List<ItemDrop.ItemData> _matching")
        self.assert_audit_refused("the port declared a type to hide state in")

    def test_the_port_may_not_hold_an_inventory_in_a_field(self):
        self.swap_in(PORT, "    private readonly List<ItemDrop.ItemData> _matching",
                     "    private Inventory? _held;\n\n    private readonly List<ItemDrop.ItemData> _matching")
        self.assert_audit_refused("the port held an inventory in a field")

    def test_the_assigned_cart_parameter_may_not_be_reassigned(self):
        # The parameter the destination is derived from is never written: a cart
        # swapped in below the entry point is a cart nobody assigned.
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        assigned = null;\n        int remaining = units;\n")
        self.assert_audit_refused("the assigned-cart parameter was reassigned")

    def test_the_calls_may_not_move_into_a_helper_whose_parameters_swap_the_roles(self):
        self.swap_in(PORT, "                    cart.MoveItemToThis(gunnar, stack);\n",
                     "                    Shift(gunnar, cart, stack);\n")
        self.swap_in(PORT, "    private static Inventory? InventoryOf(Humanoid? worker)",
                     "    private static void Shift(Inventory cart, Inventory gunnar, ItemDrop.ItemData stack)\n"
                     "    {\n        cart.MoveItemToThis(gunnar, stack);\n    }\n\n"
                     "    private static Inventory? InventoryOf(Humanoid? worker)")
        self.assert_audit_refused("the calls moved into a helper whose parameters swap the roles")

    def test_a_lambda_in_the_mover_may_not_shadow_the_roles(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        Action<Inventory> go = cart => { };\n        int remaining = units;\n")
        self.assert_audit_refused("a lambda in the mover shadowed a role")

    # --- nothing built from a name --------------------------------------------------------------

    def test_the_clone_must_come_from_his_own_stack(self):
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
        self.assert_audit_refused("a second item lookup appeared in the cart-load port")

    def test_an_instantiate_is_refused(self):
        self.swap_in(PORT, "        int remaining = units;\n",
                     "        int remaining = units;\n        var made = UnityEngine.Object.Instantiate(_matching[0].m_dropPrefab);\n")
        self.assert_audit_refused("an Instantiate in the cart-load port passed")

    # --- the cart is the assigned one, and it is only loaded --------------------------------------

    def test_the_mint_may_not_ask_for_take(self):
        self.swap_in(PORT, "subject, NpcContainerUse.Deposit, world, out refusal",
                     "subject, NpcContainerUse.Take, world, out refusal")
        self.assert_audit_refused("the cart load's mint asked for Take")

    def test_the_port_may_not_ask_the_designated_chest_instead(self):
        # A chest's subject reads the player's container mark; the cart's reads
        # the assignment. Swapping them makes any marked chest a "cart".
        self.swap_in(PORT, "        var subject = new AssignedCartContainer(\n"
                           "            assigned, expectedKey, designatedIn, allowance, workerPosition, parkedAt, reachMetres, mostDriftMetres);",
                     "        var subject = new DesignatedChest(\n"
                     "            AssignedCartContainer.BedOf(assigned), expectedKey, designatedIn, _ => default, workerPosition, reachMetres);")
        self.assert_audit_refused("the cart load asked a chest's subject")

    def test_the_bed_must_be_the_carts_own_container(self):
        self.swap_in(SUBJECT, "return bed != null && ReferenceEquals(bed.m_wagon, cart) ? bed : null;",
                     "return bed;")
        self.assert_audit_refused("a container not riding the cart passed as its bed")

    def test_a_cart_whose_id_is_not_the_leases_is_refused(self):
        self.swap_in(SUBJECT, "                    || !string.Equals(LiveKeyOf(_cart), _expectedKey, StringComparison.Ordinal)\n",
                     "")
        self.assert_audit_refused("a cart with another id passed as the assigned one")

    def test_a_cart_that_moved_is_refused(self):
        self.swap_in(SUBJECT, "                    || !Parked())", "                    )")
        self.assert_audit_refused("a cart that moved since the tour began passed")

    def test_the_bed_may_not_be_redirected_from_static_state(self):
        # The review of #381 item 2 (M1): with only two lines of BedOf pinned, an
        # early return from a static field pointed the load at any container and
        # every pin stayed green.
        self.swap_in(SUBJECT, "    private readonly Vagon? _cart;\n",
                     "    private readonly Vagon? _cart;\n    internal static Container? Redirect;\n")
        self.swap_in(SUBJECT, "        try\n        {\n            Container? bed = cart == null ? null : cart.m_container;\n",
                     "        try\n        {\n            if (Redirect != null) { return Redirect; }\n"
                     "            Container? bed = cart == null ? null : cart.m_container;\n")
        self.assert_audit_refused("the bed was redirected from static state")

    def test_the_bed_may_not_fall_back_to_another_container(self):
        self.swap_in(SUBJECT, "            return bed != null && ReferenceEquals(bed.m_wagon, cart) ? bed : null;\n"
                              "        }\n        catch (Exception)\n        {\n            return null;\n",
                     "            return bed != null && ReferenceEquals(bed.m_wagon, cart) ? bed : null;\n"
                     "        }\n        catch (Exception)\n        {\n            return Fallback(cart);\n")
        self.assert_audit_refused("the bed fell back to another container")

    def test_the_subject_may_not_hold_static_state(self):
        self.swap_in(SUBJECT, "    private readonly Vagon? _cart;\n",
                     "    private readonly Vagon? _cart;\n    private static Container? _cached;\n")
        self.assert_audit_refused("the subject held a static container")

    def test_parked_may_not_answer_yes(self):
        self.swap_in(SUBJECT, "    private bool Parked()\n    {\n", "    private bool Parked()\n    {\n        return true;\n")
        self.assert_audit_refused("Parked always said yes")

    def test_reach_may_not_answer_yes(self):
        self.swap_in(SUBJECT, "    private bool WithinReach()\n    {\n",
                     "    private bool WithinReach()\n    {\n        return true;\n")
        self.assert_audit_refused("WithinReach always said yes")

    def test_the_offer_may_not_be_everything_he_carries(self):
        self.swap_in(CART_RUNTIME, "        return _tourCartOffer;\n", "        return carried;\n")
        self.assert_audit_refused("the offer returned everything he carries")

    def test_a_pick_outside_a_cart_tour_may_not_count(self):
        self.swap_in(CART_RUNTIME, "        if (!CartTourRunning || string.IsNullOrEmpty(material) || units <= 0)\n",
                     "        if (string.IsNullOrEmpty(material) || units <= 0)\n")
        self.assert_audit_refused("a pick outside a cart tour counted toward the cart")

    def test_the_cart_tour_must_arrive_inside_the_reach(self):
        self.swap_in(CART_RUNTIME, "CollectionTourLimits.AtTheCart(_limits), refuseIfCarrying: true,",
                     "CollectionTourLimits.Default(_limits), refuseIfCarrying: true,")
        self.assert_audit_refused("the cart tour arrived at the edge of the reach")

    def test_a_hitched_cart_is_in_use(self):
        self.swap_in(SUBJECT, "if (bed.IsInUse() || _cart.InUse() || _cart.IsAttached())", "if (bed.IsInUse())")
        self.assert_audit_refused("a hitched cart passed as not in use")

    def test_a_cart_not_owned_here_is_refused(self):
        self.swap_in(SUBJECT, "if (view == null || !view.IsValid() || !view.IsOwner() || !bed.IsOwner())",
                     "if (view == null || !view.IsValid())")
        self.assert_audit_refused("a cart this client does not own passed")

    def test_the_subject_may_not_allow_itself(self):
        self.swap_in(SUBJECT, "NpcContainerUse allowed = _allowance(_cart);",
                     "NpcContainerUse allowed = NpcContainerUse.Deposit;")
        self.assert_audit_refused("the subject allowed the load itself")

    def test_the_allowance_may_not_answer_take(self):
        self.swap_in(HAULING, "return ReferenceEquals(LeasedCart(), cart) ? NpcContainerUse.Deposit : NpcContainerUse.Off;",
                     "return ReferenceEquals(LeasedCart(), cart) ? NpcContainerUse.Both : NpcContainerUse.Off;")
        self.assert_audit_refused("the assignment allowed taking out of the cart")

    def test_the_allowance_must_be_for_the_leased_cart(self):
        self.swap_in(HAULING, "return ReferenceEquals(LeasedCart(), cart) ? NpcContainerUse.Deposit : NpcContainerUse.Off;",
                     "return cart != null ? NpcContainerUse.Deposit : NpcContainerUse.Off;")
        self.assert_audit_refused("the assignment allowed any cart")

    def test_the_allowance_must_refuse_a_busy_cart(self):
        self.swap_in(HAULING, "if (cart == null || _faulted || !AssignedCartFacts.IsIdle(_executor))",
                     "if (cart == null || _faulted)")
        self.assert_audit_refused("the assignment allowed a cart the executor is using")

    def test_a_containers_m_container_may_not_be_read_elsewhere(self):
        # The only container a load reaches is the assigned cart's own, through
        # BedOf; reading a cart's container somewhere else is how another one
        # would be found.
        self.swap_in(CART_RUNTIME, "    private Vagon? AssignedVagonNow()",
                     "    private Container? Nearby(Vagon cart) => cart.m_container;\n\n"
                     "    private Vagon? AssignedVagonNow()")
        self.assert_audit_refused("a cart's container was read outside BedOf")

    # --- one call site, behind its own switch and gates --------------------------------------------

    def test_a_second_call_site_is_refused(self):
        self.swap_in(COMMAND, "internal sealed class CollectConsoleCommand",
                     "internal static class ZzSecondLoad\n{\n"
                     "    internal static object Load(GunnarCartLoadPort port) => port.LoadOne(null, null, \"\", \"Stone\", 1, "
                     "default, default, _ => default, () => null, default, 1f, 1f, out _);\n}\n\n"
                     "internal sealed class CollectConsoleCommand")
        self.assert_audit_refused("a load from a second call site passed")

    def test_a_second_call_through_an_alias_in_the_cart_part_is_refused(self):
        self.swap_in(CART_RUNTIME, "    private void SpendCartTourPick(",
                     "    private DepositResult Early(Humanoid? worker)\n    {\n        GunnarCartLoadPort port = _cartLoad;\n"
                     "        return port.LoadOne(worker, AssignedVagonNow(), _tourCartId, \"Stone\", 1, _tourCartEpoch, "
                     "CurrentEpoch(), _cartAllowance, WorkerPosition, default, 1f, 1f, out _);\n    }\n\n"
                     "    private void SpendCartTourPick(")
        self.assert_audit_refused("a second load through an alias passed")

    def test_a_second_port_is_refused(self):
        self.swap_in(RUNTIME, "    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();\n",
                     "    private readonly GunnarDepositPort _deposit = new GunnarDepositPort();\n"
                     "    private readonly GunnarCartLoadPort _other = new GunnarCartLoadPort();\n")
        self.assert_audit_refused("a second cart-load port was built")

    def test_the_opt_in_must_read_the_cart_tour_switch(self):
        self.swap_in(CART_RUNTIME,
                     "return OptedIn() && _settings.GunnarHaulingEnabled.Value && _settings.GunnarCartTourEnabled.Value;",
                     "return OptedIn() && _settings.GunnarHaulingEnabled.Value;")
        self.assert_audit_refused("the cart tour ran without its own switch")

    def test_the_cart_tour_is_off_by_default(self):
        self.swap_in(DEFAULTS, "public const bool CartTourEnabled = false;", "public const bool CartTourEnabled = true;")
        self.assert_audit_refused("filling a cart was switched on by default")

    def test_the_switch_is_bound_to_the_off_default(self):
        self.swap_in(SETTINGS,
                     'config.Bind("Workers", "GunnarCartTourEnabled", GunnarCollectionDefaults.CartTourEnabled,',
                     'config.Bind("Workers", "GunnarCartTourEnabled", true,')
        self.assert_audit_refused("the cart tour switch was bound to on")

    def test_the_load_may_not_name_another_cart(self):
        self.swap_in(CART_RUNTIME, "                _tourCartId,\n", "                cart.Cart.SessionId,\n")
        self.assert_audit_refused("the load named a cart other than the one the tour began with")

    def test_the_load_may_not_be_allowed_by_a_literal(self):
        self.swap_in(CART_RUNTIME, "                _cartAllowance,\n", "                _ => NpcContainerUse.Deposit,\n")
        self.assert_audit_refused("the load was allowed by a literal instead of the assignment")

    def test_the_start_gate_must_refuse_with_a_return(self):
        self.swap_in(CART_RUNTIME,
                     "        if (refusal != CartTourRefusal.None)\n"
                     "        {\n"
                     "            return CartTourGate.Describe(refusal, request.Authority);\n"
                     "        }\n",
                     "        if (refusal != CartTourRefusal.None)\n"
                     "        {\n"
                     "            _log?.LogInfo(CartTourGate.Describe(refusal, request.Authority));\n"
                     "        }\n")
        self.assert_audit_refused("the start gate's refusal was consulted and ignored")

    def test_the_start_may_not_begin_with_a_load_he_brought(self):
        self.swap_in(CART_RUNTIME, "refuseIfCarrying: true,", "refuseIfCarrying: false,")
        self.assert_audit_refused("a cart tour began with a load he already carried")

    def test_the_start_gate_may_not_be_told_he_carries_nothing(self):
        self.swap_in(CART_RUNTIME, "carryingCollected: carrying.Count > 0,", "carryingCollected: false,")
        self.assert_audit_refused("the start gate was told he carries nothing")

    def test_the_load_must_offer_only_what_this_tour_picked(self):
        self.swap_in(TOUR, "IReadOnlyList<KeyValuePair<string, int>> carried = PickedByThisTour(\n"
                           "            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable));",
                     "IReadOnlyList<KeyValuePair<string, int>> carried =\n"
                     "            _deposit.Carrying(worker, GunnarCollectionAllowlist.IsCollectedYield, out bool readable);")
        self.assert_audit_refused("the load offered everything he carries")

    def test_the_load_gate_may_not_be_told_authority_is_granted(self):
        self.swap_in(TOUR, "            authority: CartTourAuthority(),\n", "            authority: WorkAuthorityVerdict.Granted,\n")
        self.assert_audit_refused("the load gate was told authority was granted")

    def test_the_load_gate_may_not_be_told_the_cart_is_fine(self):
        self.swap_in(TOUR, "cartWatch: WatchTourCart(),", "cartWatch: CartTourRefusal.None,")
        self.assert_audit_refused("the load gate was told the cart is still the parked one")

    def test_the_load_gate_may_not_be_told_a_literal_switch(self):
        self.swap_in(TOUR, "            cartTourEnabled: CartTourOptedIn(),\n", "            cartTourEnabled: true,\n")
        self.assert_audit_refused("the load gate was told a literal instead of the switch")

    def test_the_load_gate_may_not_be_told_nothing_is_in_flight(self):
        self.swap_in(TOUR, "            transferInFlight: _ordered || _port.Phase == PickPhase.Gathering,\n"
                           "            cartWatch: WatchTourCart(),\n",
                     "            transferInFlight: false,\n"
                     "            cartWatch: WatchTourCart(),\n")
        self.assert_audit_refused("the load gate was told nothing is in flight")

    def test_the_load_gates_refusal_must_return(self):
        self.swap_in(TOUR, "            default:\n"
                           "                detail = CartTourGate.Describe(refusal, request.Authority);\n"
                           "                return TourDepositEnd.Refused;\n",
                     "            default:\n"
                     "                detail = CartTourGate.Describe(refusal, request.Authority);\n"
                     "                break;\n")
        self.assert_audit_refused("the load gate's refusal fell through to the load")

    def test_the_load_must_be_written_down_first(self):
        # #379: a load the plan was not told about is a move nothing could say
        # later had happened.
        self.swap_in(TOUR, "        if (!_tourPlan.IntendDeposit(\"about to put what he carries into the cart assigned to him\", out string unwritten))\n"
                           "        {\n"
                           "            detail = \"the trip could not be written down first, so nothing was moved: \" + unwritten;\n"
                           "            return TourDepositEnd.Refused;\n"
                           "        }\n",
                     "")
        self.assert_audit_refused("a load into the cart was made before the plan was told")

    def test_the_legs_may_not_be_called_from_elsewhere(self):
        self.swap_in(RUNTIME, "    private string Cancel()\n",
                     "    private void Sneak(Humanoid? worker) => CartLoadLegs(worker, System.Array.Empty<KeyValuePair<string, int>>(), new System.Text.StringBuilder());\n\n"
                     "    private string Cancel()\n")
        self.assert_audit_refused("the cart's legs were reached outside the tour's gated trip")

    def test_the_tick_may_not_hand_the_tour_a_busy_cart(self):
        # A haul, a joint or a pending park belongs to the executor; the tour
        # steering him then would walk him away from a hitched cart.
        self.swap_in(HAULING, "if (drive != null && (!HoldsACart || AssignedCartFacts.IsIdle(_executor)) && drive(ai))",
                     "if (drive != null && drive(ai))")
        self.assert_audit_refused("the worker tick handed the tour his body beside a busy cart")


if __name__ == "__main__":
    unittest.main()
