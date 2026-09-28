"""Mutation tests for the #368 single-owner product data roots."""
import os
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
PRODUCTS = {
    "cartographer": os.path.join(ROOT, "src", "ConcernedCartographer",
                                 "Runtime", "CartographerRuntime.cs"),
    "teamster": os.path.join(ROOT, "src", "ConcernedTeamster",
                            "Adapters", "TripRecordingService.cs"),
    "foreman": os.path.join(ROOT, "src", "ConcernedForeman",
                           "Runtime", "Settlement", "SettlementRecords.cs"),
    "steward": os.path.join(ROOT, "src", "ConcernedSteward",
                           "Runtime", "StewardRuntime.cs"),
}
OWNER = os.path.join(ROOT, "src", "ConcernedTeamster", "TeamsterPaths.cs")


def validate():
    done = subprocess.run([sys.executable, VALIDATOR], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class ProductDataRootsHaveOneOwner(unittest.TestCase):
    def setUp(self):
        code, out = validate()
        self.assertEqual(0, code, "tree is not clean:\n" + out[-2000:])
        self._restore = []

    def tearDown(self):
        for path, original in reversed(self._restore):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        code, out = validate()
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def assert_refused(self, why):
        code, out = validate()
        self.assertNotEqual(0, code, why + "\n" + out[-2000:])
        self.assertIn("[product-paths]", out, why)

    def plant_direct_root(self, product, marker):
        path = PRODUCTS[product]
        self.swap_in(path, marker,
                     marker + "\n    private static string RootProbe => Paths.ConfigPath;")
        self.assert_refused(product + " composed a second data root")

    def test_cartographer_consumer_cannot_compose_root(self):
        self.plant_direct_root(
            "cartographer", "internal sealed class CartographerRuntime : IDisposable")

    def test_teamster_consumer_cannot_compose_root(self):
        self.plant_direct_root(
            "teamster", "internal sealed class TripRecordingService")

    def test_foreman_consumer_cannot_compose_root(self):
        self.plant_direct_root(
            "foreman", "internal sealed class SettlementRecords")

    def test_steward_consumer_cannot_compose_root(self):
        self.plant_direct_root(
            "steward", "internal sealed class StewardRuntime")

    def test_owner_must_actually_compose_the_root(self):
        self.swap_in(OWNER, "Paths.ConfigPath", "Environment.CurrentDirectory")
        self.assert_refused("the named owner stopped composing its product root")


if __name__ == "__main__":
    unittest.main()
