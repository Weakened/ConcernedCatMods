"""Emit the CI matrix for tools/tests: one entry per test class that has tests.

The plant suites each run the whole validator once per planted violation, and
run one after another they had grown to 3-4 hours in a single CI job. Every CI
job has its own checkout, so the classes can run in parallel safely - a plant
written into one runner's tree is invisible to every other runner.

The matrix is discovered rather than listed by hand on purpose: a hand-written
list would silently stop running a test file or class added later, and a test
that is not run is a green that means nothing. A module that fails to import, or
a module that yields no test class at all, fails discovery instead of being
skipped.

Prints a JSON array of "module.Class" names for unittest. With --count, prints
the total number of test methods instead (used by the summary job).
"""
import importlib.util
import inspect
import json
import sys
import unittest
from pathlib import Path

TESTS = Path(__file__).resolve().parent / "tests"


def discover():
    entries = []
    total = 0
    loader = unittest.TestLoader()
    for path in sorted(TESTS.glob("test_*.py")):
        name = path.stem
        spec = importlib.util.spec_from_file_location(name, path)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
        found = 0
        for cls_name, cls in inspect.getmembers(module, inspect.isclass):
            if cls.__module__ != name or not issubclass(cls, unittest.TestCase):
                continue
            count = loader.loadTestsFromTestCase(cls).countTestCases()
            if count:
                entries.append(f"tools.tests.{name}.{cls_name}")
                total += count
                found += 1
        if not found:
            raise SystemExit(f"{path.name} has no runnable test class; refusing to drop it from CI.")
    # The matrix must run everything the old single `unittest discover` step ran.
    # A TestCase imported from a helper module, or a load_tests hook, would be
    # missed above; this makes that a failure rather than a smaller green.
    expected = loader.discover(str(TESTS), "test_*.py").countTestCases()
    if total != expected:
        raise SystemExit(f"matrix covers {total} tests but unittest discover finds {expected}; refusing.")
    return entries, total


if __name__ == "__main__":
    entries, total = discover()
    if "--count" in sys.argv:
        print(total)
    else:
        print(json.dumps(entries))
