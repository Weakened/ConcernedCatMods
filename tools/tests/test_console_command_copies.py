"""Negative tests for the #391 console-command copy pin.

Cartographer, Foreman, Steward and Teamster each carry `VanillaConsoleCommands.cs`,
because Jotunn's console commands are dead on Valheim 1.0.x and products may not
share game-typed source. A fix that reaches one copy and not the others brings back
the silent, total loss of that product's commands. The validator compares the copies
after removing only what each may own: usings, namespace, comments, whitespace, and
its own scrubber's spelling of `Brief`.

Each plant below edits one real copy in the real tree, runs the real validator, and
requires it to refuse, then restores the file. The plants are the divergences that
matter: the constructor-shape logic changed in one copy, a statement added to one, a
statement removed from one, the scrubbed failure text replaced by the raw message in
one (which the `Brief` normalisation must not hide), a fifth unpinned copy, and a
pinned copy that disappears. Two plants require the validator to PASS, because a pin
that refused a reworded comment or a product's own namespace could never be kept on.

No count is given on purpose: what the harness covers is the list above, and that is
checkable against the file.
"""
import os
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
TEAMSTER = os.path.join(ROOT, "src", "ConcernedTeamster", "Adapters", "VanillaConsoleCommands.cs")
FOREMAN = os.path.join(ROOT, "src", "ConcernedForeman", "Runtime", "VanillaConsoleCommands.cs")
STEWARD = os.path.join(ROOT, "src", "ConcernedSteward", "Runtime", "VanillaConsoleCommands.cs")
EXTRA = os.path.join(ROOT, "src", "ConcernedSteward", "Domain", "VanillaConsoleCommandsCopy.cs")
LABEL = "#391 console-command copies"


def run_validator():
    return subprocess.run(
        [sys.executable, VALIDATOR], cwd=ROOT, capture_output=True, text=True,
        encoding="utf-8", errors="replace")


def read(path):
    with open(path, "rb") as handle:
        return handle.read()


def write(path, data):
    with open(path, "wb") as handle:
        handle.write(data)


class ConsoleCommandCopyPinTests(unittest.TestCase):

    def plant(self, path, old, new):
        original = read(path)
        self.assertIn(old, original, f"plant anchor missing from {path}")
        write(path, original.replace(old, new, 1))
        self.addCleanup(write, path, original)

    def assert_refused(self):
        result = run_validator()
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(LABEL, result.stderr)

    def assert_passes(self):
        result = run_validator()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(LABEL + ": 4 copies agree", result.stdout)

    def test_constructor_logic_changed_in_one_copy_is_refused(self):
        self.plant(TEAMSTER, b"int index = 3; index < parameters.Length",
                   b"int index = 4; index < parameters.Length")
        self.assert_refused()

    def test_statement_added_to_one_copy_is_refused(self):
        self.plant(FOREMAN, b"arguments[1] = command.Help;",
                   b"arguments[1] = command.Help;\n        arguments[1] ??= string.Empty;")
        self.assert_refused()

    def test_statement_removed_from_one_copy_is_refused(self):
        self.plant(STEWARD, b"arguments[1] = command.Help;", b"")
        self.assert_refused()

    def test_raw_exception_message_in_one_copy_is_refused(self):
        # Brief(...) is normalised across products; the raw message must not be.
        self.plant(TEAMSTER, b"falling back to J\xc3\xb6tunn: \" +\n                Brief(exception)",
                   b"falling back to J\xc3\xb6tunn: \" +\n                exception.Message")
        self.assert_refused()

    def test_unpinned_fifth_copy_is_refused(self):
        write(EXTRA, read(STEWARD))
        self.addCleanup(os.remove, EXTRA)
        self.assert_refused()

    def test_missing_pinned_copy_is_refused(self):
        original = read(FOREMAN)
        os.remove(FOREMAN)
        self.addCleanup(write, FOREMAN, original)
        self.assert_refused()

    def test_reworded_comment_still_passes(self):
        self.plant(TEAMSTER, b"// The game hands the command its own name as args[0]; every",
                   b"// The game passes the command's own name as args[0], and every")
        self.assert_passes()

    def test_product_namespace_and_usings_still_pass(self):
        self.plant(STEWARD, b"using System;", b"using System;\nusing System.Linq;")
        self.assert_passes()


if __name__ == "__main__":
    unittest.main()
