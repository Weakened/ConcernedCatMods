"""Planted violations for Gunnar's half of D19's small-tree felling (#381 item 5).

The owner granted felling on #381 (2026-10-31) without setting Gunnar's scope;
the lead default is Thorstein's built scope. Gunnar may land one vanilla
`Destructible.Damage` per explicit `ct_collect fell`, in one file,
`Adapters/Workers/GunnarFellingPort.cs`, behind its own off-by-default switch,
and take only the Wood that hit dropped through the collection port's one pinned
`Humanoid.Pickup`. Two rules carry it: the felling port's entry in the #313
worker-runtime scope audit's pinned-call table (the one `.Damage(` in Teamster,
verbatim and once), and `check_teamster_felling_port` ("D19 Gunnar felling
audit"), which pins what a call pin cannot see - what is struck, with what hit,
behind which rule, from which verb, and what is taken.

Each test plants an escape in the real source tree, runs the real validator and
requires it to refuse. It then runs a copy of the validator WITHOUT those rules -
the felling audit unregistered and the felling port's pinned call relaxed to the
bare token, still once - and requires the same plant to pass: that is the proof
the escape is caught by the felling rules and not by something else. `also=`
names a wider, older rule that refuses the plant as well; it is unregistered in
the copy too, so the plant still proves the felling rules on their own.

Cost: the tree is checked clean once for the class and once at the end, and
every test restores each file byte for byte and asserts it did, so each plant
costs two validator runs instead of four.

The escapes every authorized port shares - a `.Damage(` in another file, a
second file of the port's name elsewhere, a space or a newline before the paren
- are refused by the path-keyed token list whatever the felling rules say, and
are proved in `test_teamster_carveout.py`.
"""
import os
import subprocess
import sys
import unittest


ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster")
WORKERS = os.path.join(TEAMSTER, "Adapters", "Workers")
PORT = os.path.join(WORKERS, "GunnarFellingPort.cs")
PICKUP = os.path.join(WORKERS, "GunnarCollectionPort.cs")
FELL = os.path.join(WORKERS, "GunnarCollectionRuntime.Fell.cs")
RUNTIME = os.path.join(WORKERS, "GunnarCollectionRuntime.cs")
TOUR = os.path.join(WORKERS, "GunnarCollectionRuntime.Tour.cs")
DEFAULTS = os.path.join(TEAMSTER, "Domain", "Collection", "GunnarCollectionDefaults.cs")
GATE = os.path.join(TEAMSTER, "Domain", "Collection", "FellOrderGate.cs")
SHARED = os.path.join(ROOT, "src", "Shared", "Workers", "Felling", "SmallTreeFelling.cs")

RULE = "check_teamster_felling_port"
TABLE = "TEAMSTER_AUTHORIZED_PORTS = {"
RELAX = (r'''TEAMSTER_FELLING_PORT_CALLS = tuple((token, re.compile(r"\.\s*" + re.escape(token.strip(".(")) + r"\s*\("), what)'''
         r''' for token, _pattern, what in TEAMSTER_FELLING_PORT_CALLS)''' + "\n")
COPY = os.path.join(ROOT, "tools", "_zz_without_felling_rules.py")


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class FellingIsNarrow(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        code, out = run(VALIDATOR)
        if code != 0:
            raise AssertionError("the tree must be clean before a plant means anything:\n" + out[-3000:])

    @classmethod
    def tearDownClass(cls):
        code, out = run(VALIDATOR)
        if code != 0:
            raise AssertionError("a felling plant was left behind:\n" + out[-3000:])

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
        for path, original in self._restore:
            with open(path, "rb") as handle:
                self.assertEqual(original, handle.read(), "not restored byte for byte: " + path)
        for path in self._planted:
            self.assertFalse(os.path.exists(path), "a planted file was left behind: " + path)

    def swap_in(self, path, old, new):
        with open(path, "rb") as handle:
            original = handle.read()
        text = original.decode("utf-8")
        self.assertEqual(1, text.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "wb") as handle:
            handle.write(text.replace(old, new, 1).encode("utf-8"))

    def without_the_felling_rules(self, also=()):
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        for rule in (RULE,) + tuple(also):
            registration = "    report.extend(" + rule + "(errors))\n"
            self.assertEqual(1, source.count(registration), rule + " is not registered exactly once")
            source = source.replace(registration, "")
        self.assertEqual(1, source.count(TABLE), "the pinned-call table moved")
        source = source.replace(TABLE, RELAX + TABLE)
        self.assertFalse(os.path.exists(COPY), "a previous copy was left behind")
        self._planted.append(COPY)
        with open(COPY, "w", encoding="utf-8", newline="") as handle:
            handle.write(source)
        return COPY

    def assert_refused(self, why, phrase="D19 Gunnar felling audit", also=()):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-3000:])
        refusals = [line for line in out.splitlines() if line.startswith("ERROR: ") and phrase in line]
        self.assertTrue(refusals, why + ": refused, but not by the felling rules\n" + out[-3000:])
        code, out = run(self.without_the_felling_rules(also))
        self.assertEqual(0, code, why + ": without the felling rules something else still refuses it, so the "
                         "plant does not prove them\n" + out[-3000:])

    # --- what is struck -------------------------------------------------------------------------

    def test_the_hit_may_not_land_on_his_own_body(self):
        # `worker` is Gunnar's Humanoid, and Character.Damage is the death path
        # D16 says no voluntary verb may reach.
        self.swap_in(PORT, "            tree.Damage(hit);\n", "            worker.Damage(hit);\n")
        self.assert_refused("the felling hit was turned on Gunnar's own body", phrase="GunnarFellingPort.cs")

    def test_the_strike_may_not_be_handed_a_character(self):
        self.swap_in(PORT, "        Destructible tree, ZNetView view,", "        Character tree, ZNetView view,")
        self.assert_refused("the strike's receiver became a character")

    def test_the_tree_may_not_be_replaced_before_the_hit(self):
        self.swap_in(PORT, "        HitData hit = HitFrom(axe, worker, workerAt, treeAt);\n",
                     "        HitData hit = HitFrom(axe, worker, workerAt, treeAt);\n"
                     "        tree = view.GetComponentInChildren<Destructible>();\n")
        self.assert_refused("the struck object was re-bound inside the strike")

    def test_the_finder_may_not_read_another_prefab(self):
        self.swap_in(PORT, "Array.IndexOf(AllowlistedHashes, candidate.GetZDO().GetPrefab()) < 0",
                     "candidate.GetZDO().GetPrefab() == 0")
        self.assert_refused("the finder stopped reading the four allowlisted prefabs")

    def test_a_second_finder_call_may_not_pick_an_unmeasured_tree(self):
        # Review of 373cef8: after the site guard, a second look re-binds what
        # is struck to a tree the site rule never measured.
        self.swap_in(PORT, "        _spawned.Clear();\n        try\n",
                     "        TryFindTree(area, workerAt, out target, out view, out kind, out none);\n"
                     "        _spawned.Clear();\n        try\n")
        self.assert_refused("a second finder call after the site rule passed")

    def test_the_site_facts_may_not_be_replaced(self):
        # Positional, so no named-argument pin counts it.
        self.swap_in(PORT, "        FellSiteRefusal refusal = SmallTreeFelling.CheckSite(site, reachMetres);\n",
                     "        site = new FellSiteFacts(true, true, true, true, true, true, 99f, 99f, 0f, true, 9, 0);\n"
                     "        FellSiteRefusal refusal = SmallTreeFelling.CheckSite(site, reachMetres);\n")
        self.assert_refused("made-up site facts answered the rule")

    def test_the_ward_may_not_be_asked_the_lenient_way(self):
        # Vanilla's default lets one granting ward outvote a denying one.
        self.swap_in(PORT, "            return PrivateArea.CheckAccess(at, 0f, flash: false, wardCheck: true);\n",
                     "            return PrivateArea.CheckAccess(at, 0f, flash: false, wardCheck: false);\n")
        self.assert_refused("the lenient ward question passed")

    def test_an_axe_may_not_be_made(self):
        self.swap_in(PORT, "        return best;\n    }\n",
                     "        return best ?? ObjectDB.instance.GetItemPrefab(\"AxeStone\").GetComponent<ItemDrop>()"
                     ".m_itemData.Clone();\n    }\n")
        self.assert_refused("an axe minted from the item database passed")

    # --- with what hit --------------------------------------------------------------------------

    def test_the_hit_may_not_be_made_up(self):
        self.swap_in(PORT, "HitData hit = HitFrom(axe, worker, workerAt, treeAt);", "HitData hit = new HitData();")
        self.assert_refused("a hit not built from his axe passed")

    def test_the_damage_may_not_be_multiplied(self):
        self.swap_in(PORT, "        made.m_damage.Add(axe.GetDamage());\n",
                     "        made.m_damage.Add(axe.GetDamage());\n        made.m_damage.Modify(10f);\n")
        self.assert_refused("a multiplied hit passed")

    def test_the_attacker_may_not_be_someone_else(self):
        self.swap_in(PORT, "made.SetAttacker(worker);", "made.SetAttacker(Player.m_localPlayer);")
        self.assert_refused("a hit in the player's name passed")

    # --- behind which rule ----------------------------------------------------------------------

    def test_the_site_verdict_may_not_be_ignored(self):
        self.swap_in(PORT, "        if (refusal != FellSiteRefusal.Allowed)\n",
                     "        if (refusal == FellSiteRefusal.Unspecified)\n")
        self.assert_refused("a strike the site rule refused could still land")

    def test_a_player_close_by_may_not_be_ignored(self):
        self.swap_in(PORT, "nearestPlayerMetres: PlayerClearanceMetres(treeAt),",
                     "nearestPlayerMetres: float.PositiveInfinity,")
        self.assert_refused("the 15 m player clearance was not measured")

    def test_a_ward_may_not_be_ignored(self):
        self.swap_in(PORT, "wardAllows: WardAllows(treeAt),", "wardAllows: true,")
        self.assert_refused("the ward was not asked")

    def test_the_area_may_not_be_assumed(self):
        self.swap_in(PORT, "explicitArea: areaChosen,", "explicitArea: true,")
        self.assert_refused("an area nobody set in this world load was assumed")

    def test_the_order_gate_may_not_be_ignored(self):
        self.swap_in(FELL, "        if (refusal != FellRefusal.None)\n", "        if (refusal == FellRefusal.NoArea)\n")
        self.assert_refused("ct_collect fell could reach the port past its order gate")

    def test_the_opt_in_needs_its_own_switch(self):
        self.swap_in(FELL, "return OptedIn() && _settings.GunnarFellsSmallTrees.Value;", "return OptedIn();")
        self.assert_refused("felling ran on the collection switch alone")

    def test_felling_may_not_be_on_by_default(self):
        self.swap_in(DEFAULTS, "public const bool FellingEnabled = false;", "public const bool FellingEnabled = true;")
        self.assert_refused("felling was on by default")

    # --- from which verb ------------------------------------------------------------------------

    def test_a_tour_may_not_strike(self):
        self.swap_in(TOUR, "    private bool TourRunning => _tour != null || _haul != null;\n",
                     "    private bool TourRunning => _tour != null || _haul != null;\n"
                     "    private void Swing() { _felling.StrikeOnce(null!, null!, null!, null!, null!, default, null!, "
                     "out _, out _, out _, out _); }\n")
        self.assert_refused("a tour reached the strike directly")

    def test_a_tour_may_not_fell(self):
        # Felling inside a tour is not built: a second caller of the felling.
        self.swap_in(TOUR, "    private bool TourRunning => _tour != null || _haul != null;\n",
                     "    private bool TourRunning => _tour != null || _haul != null;\n"
                     "    private void Chop() { _felling.TryFellOne(null, null, true, FellOptedIn, FellAuthority, 0f, "
                     "_port, WoodRoom, _ => { }); }\n")
        self.assert_refused("a tour reached the felling")

    def test_the_dispatcher_may_not_be_called_from_a_tour(self):
        # Review of 373cef8: every literal is blanked, so `Execute(new[] { "fell" })`
        # reached `case "fell":` unseen.
        self.swap_in(TOUR, "    private bool TourRunning => _tour != null || _haul != null;\n",
                     "    private bool TourRunning => _tour != null || _haul != null;\n"
                     "    private void Chop() { Execute(new[] { \"fell\" }); }\n")
        self.assert_refused("a tour dispatched ct_collect fell")

    def test_a_cancel_may_not_reach_a_hit(self):
        # No retire, cancel, unload or teardown verb may land a hit.
        self.swap_in(RUNTIME, "        string what = _orderedSource;\n",
                     "        Fell();\n        string what = _orderedSource;\n")
        self.assert_refused("ct_collect cancel reached a felling hit")

    # --- what is taken --------------------------------------------------------------------------

    def test_the_take_may_not_be_handed_other_drops(self):
        self.swap_in(PORT, "pickup.TakeFelled(worker, _spawned, room.Value)",
                     "pickup.TakeFelled(worker, new List<ItemDrop>(ItemDrop.s_instances), room.Value)")
        self.assert_refused("the felled take was handed drops the hit did not make")

    def test_the_take_may_not_skip_the_chooser(self):
        self.swap_in(PICKUP, "ItemDrop drop = spawned[_felledChosen[pick]];", "ItemDrop drop = spawned[pick];")
        self.assert_refused("the felled take took drops the chooser did not choose")

    def test_the_pinned_pickup_may_not_gain_a_caller(self):
        self.swap_in(PICKUP, "        _accounting.ForgetJob();\n",
                     "        _accounting.ForgetJob();\n        TakeDropped(null!);\n")
        self.assert_refused("the pinned pickup's helper gained a third caller")

    # --- decided, unused ------------------------------------------------------------------------

    def test_tree_base_may_not_be_named_in_a_linked_shared_tree(self):
        # Foreman compiles src/Shared/Workers in too, so its own D19 audit
        # refuses this as well.
        self.swap_in(SHARED, "    public const float ClearanceMetres = 15f;\n",
                     "    public const float ClearanceMetres = 15f;\n"
                     "    public static string Full() => nameof(TreeBase);\n")
        self.assert_refused("TreeBase was named in a shared tree Teamster compiles in",
                            also=("check_foreman_felling_scope",))

    def test_tree_log_may_not_be_named_in_domain(self):
        self.swap_in(GATE, "    public const string WoodPrefab = \"Wood\";\n",
                     "    public const string WoodPrefab = \"Wood\";\n"
                     "    public static string Log() => nameof(TreeLog);\n")
        self.assert_refused("TreeLog was named in Teamster's domain")

    def test_the_instant_kill_is_refused(self):
        # The carried-material audit counts every `Destroy...(` too.
        self.swap_in(FELL, "        Humanoid? worker = _worker();\n",
                     "        Humanoid? worker = _worker();\n        ((dynamic)worker!).DestroyNow();\n")
        self.assert_refused("the instant kill passed", also=("check_teamster_retire_guards_carried_material",))


if __name__ == "__main__":
    unittest.main()
