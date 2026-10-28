"""Negative tests for the #390 provider-floor audit.

A consumer refuses a provider older than its FloorVersion. In #369/#370 the haul
floor said 1.0.5 while Teamster shipped 1.0.4, and every cooperative order was
silently refused with the whole gate green. Each test below plants that drift (or
a way of hiding a floor from the audit) into the real tree, runs the real
validator, requires the refusal, and puts the tree back. Each was run against the
validator from before this rule (origin/main 89ce8b6), which let every one of
them through.
"""
import os
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
SRC = os.path.join(ROOT, "src")
HAUL_GATE = os.path.join(SRC, "Shared", "Settlement", "Collection", "Cooperation", "HaulProviderGate.cs")
RESTOCK_GATE = os.path.join(SRC, "ConcernedSteward", "Domain", "Interop", "RestockProviderGate.cs")
PRESENCE_GATE = os.path.join(SRC, "Shared", "Settlement", "Collection", "Cooperation", "CompanionPresenceClient.cs")
CARTOGRAPHER_CONTRACT = os.path.join(SRC, "ConcernedTeamster", "Domain", "Cartographer", "CartographerContract.cs")
TEAMSTER = os.path.join(SRC, "ConcernedTeamster")


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class ProviderFloorsAreShipped(unittest.TestCase):

    def setUp(self):
        code, _ = validate()
        self.assertEqual(0, code, "the tree must be clean before a plant means anything")
        self._restores = []
        self._planted = []

    def tearDown(self):
        for path in reversed(self._planted):
            if os.path.isfile(path):
                os.remove(path)
        for path, original in reversed(self._restores):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    def swap(self, path, old, new):
        # newline="" both ways: a project file is CRLF in the tree, and reading
        # it translated would put it back LF and leave the tree dirty.
        with open(path, encoding="utf-8-sig", newline="") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "the pinned text moved: " + old)
        self._restores.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def assert_refused(self, why, contains):
        code, out = validate()
        self.assertIn(contains, out, why + "\n" + out[-2000:])
        self.assertNotEqual(0, code, why)
        self.assertIn("#390 provider-floor audit", out, why)

    # -- the drift itself, from both ends --

    def test_a_haul_floor_above_what_teamster_ships_is_refused(self):
        self.swap(HAUL_GATE, "new System.Version(1, 0, 5)", "new System.Version(1, 0, 6)")
        self.assert_refused("the #369 drift - floor above the shipped Teamster - passed",
                            contains="HaulProviderGate.FloorVersion is 1.0.6")

    def test_teamster_shipping_below_the_floor_is_refused(self):
        # All three spellings move together, so the package check stays green and
        # only the floor rule can see it - the shape #369 actually had.
        swapped = 0
        for rel in (("ConcernedTeamster.csproj",), ("Package", "thunderstore.toml"), ("Plugin.cs",)):
            path = os.path.join(TEAMSTER, *rel)
            with open(path, encoding="utf-8-sig", newline="") as handle:
                text = handle.read()
            for old in ("<Version>1.0.5</Version>", 'versionNumber = "1.0.5"', 'PluginVersion = "1.0.5"'):
                if old in text:
                    self.swap(path, old, old.replace("1.0.5", "1.0.4"))
                    swapped += 1
                    break
        # All three, or the package check would refuse the plant for a
        # different reason and this would prove nothing about the floor rule.
        self.assertEqual(3, swapped, "Teamster's version did not move in all three places")
        self.assert_refused("Teamster shipping below the haul floor passed", contains="ships 1.0.4")

    def test_the_restock_floor_is_held_to_the_same_rule(self):
        self.swap(RESTOCK_GATE, "new System.Version(1, 0, 5)", "new System.Version(2, 0, 0)")
        self.assert_refused("a restock floor above the shipped Teamster passed",
                            contains="RestockProviderGate.FloorVersion is 2.0.0")

    def test_the_presence_floor_is_held_to_the_same_rule(self):
        self.swap(PRESENCE_GATE, "new System.Version(1, 2, 2)", "new System.Version(1, 2, 3)")
        self.assert_refused("a presence floor above the shipped Cartographer passed",
                            contains="PresenceProviderGate.FloorVersion is 1.2.3")

    def test_the_cartographer_contract_floor_is_held_to_the_same_rule(self):
        self.swap(CARTOGRAPHER_CONTRACT, "new System.Version(0, 10, 0)", "new System.Version(9, 0, 0)")
        self.assert_refused("a Cartographer contract floor above the shipped Cartographer passed",
                            contains="CartographerContract.FloorVersion is 9.0.0")

    # -- a floor the audit cannot see is a floor nobody compares --

    def test_a_new_unlisted_floor_is_refused(self):
        path = os.path.join(TEAMSTER, "Domain", "ZzFloor.cs")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("namespace Zz;\ninternal static class ZzGate\n{\n"
                         "    public static readonly System.Version FloorVersion = new System.Version(99, 0, 0);\n}\n")
        self._planted.append(path)
        self.assert_refused("an unlisted floor passed", contains="does not know")

    def test_a_new_unlisted_floor_with_an_expression_body_is_refused(self):
        path = os.path.join(TEAMSTER, "Domain", "ZzArrowFloor.cs")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("namespace Zz;\ninternal static class ZzArrowGate\n{\n"
                         "    public static System.Version FloorVersion => new System.Version(99, 0, 0);\n}\n")
        self._planted.append(path)
        self.assert_refused("an unlisted expression-bodied floor passed", contains="does not know")

    def test_a_new_unlisted_floor_as_an_auto_property_is_refused(self):
        path = os.path.join(TEAMSTER, "Domain", "ZzAutoFloor.cs")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("namespace Zz;\ninternal static class ZzAutoGate\n{\n"
                         "    public static System.Version FloorVersion { get; } = new System.Version(99, 0, 0);\n}\n")
        self._planted.append(path)
        self.assert_refused("an unlisted auto-property floor passed", contains="does not know")

    def test_a_floor_spelled_so_it_cannot_be_read_is_refused(self):
        # Four components, or a parse, would leave the number unread - and an
        # unread floor is how the drift hides.
        self.swap(HAUL_GATE, "new System.Version(1, 0, 5)", "System.Version.Parse(\"1.0.6\")")
        self.assert_refused("a floor the audit cannot read passed", contains="could not read HaulProviderGate.FloorVersion")


if __name__ == "__main__":
    unittest.main()
