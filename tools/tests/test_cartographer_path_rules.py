"""Self-tests for the three #304 Cartographer path rules (#392).

Each rule was mutation-proved by hand during #304's review; those proofs lived
only in a transcript. Here each escape the rules exist to refuse is planted into
the real tree, the real validator is run, and it must refuse with the phrase only
that rule produces. Then the same plant is run against a copy of the validator
with that one rule's registration removed, and the refusal must disappear - so
the test proves *that* rule is what refuses, not a neighbour catching it by
accident. Every plant is removed again and the tree is required to be clean.

The rules:

- check_cartographer_prior_names_stay_known_to_the_probe: a name an older build
  wrote (a `Prior...Name(s)` declaration) must still be one the fresh-install
  probe recognises (#343).
- check_cartographer_editor_extensions_agree: the validator's copy of the
  configuration editor's extension list and CartographerConfigFiles' copy say
  the same thing, in the same order.
- check_cartographer_root_holds_only_names_the_probe_knows: nothing lands in the
  probed directory that the probe cannot account for, and nothing a
  configuration editor opens is written anywhere in it unless a player is meant
  to edit it.
"""
import os
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VALIDATOR = os.path.join(ROOT, "tools", "validate_repo.py")
CARTOGRAPHER = os.path.join(ROOT, "src", "ConcernedCartographer")
PROBE = os.path.join(CARTOGRAPHER, "Runtime", "Companions", "CartographerLegacyProbe.cs")
BACKUP = os.path.join(CARTOGRAPHER, "Persistence", "AtlasBackupTools.cs")
CONFIG_FILES = os.path.join(CARTOGRAPHER, "Domain", "Storage", "CartographerConfigFiles.cs")
SHARED_COMPANIONS = os.path.join(ROOT, "src", "Shared", "Companions")

PRIOR_NAMES = "check_cartographer_prior_names_stay_known_to_the_probe"
EDITOR_EXTENSIONS = "check_cartographer_editor_extensions_agree"
ROOT_CONTENTS = "check_cartographer_root_holds_only_names_the_probe_knows"

MARKER = "[cartographer-paths]"


def run(validator):
    done = subprocess.run([sys.executable, validator], cwd=ROOT,
                          capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


class PathRuleFixture(unittest.TestCase):
    def setUp(self):
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "tree is not clean:\n" + out[-2000:])
        self._restore = []
        self._planted = []

    def tearDown(self):
        for path in reversed(self._planted):
            if os.path.exists(path):
                os.remove(path)
        for path, original in reversed(self._restore):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(original)
        code, out = run(VALIDATOR)
        self.assertEqual(0, code, "a plant was left behind:\n" + out[-2000:])

    def swap_in(self, path, old, new):
        with open(path, encoding="utf-8-sig") as handle:
            original = handle.read()
        self.assertEqual(1, original.count(old), "pinned text moved: " + old)
        self._restore.append((path, original))
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace(old, new, 1))

    def plant_file(self, path, body):
        self.assertFalse(os.path.exists(path), "plant would overwrite a real file: " + path)
        self._planted.append(path)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(body)

    def unregistered(self, rule):
        """A copy of the validator with exactly one rule's registration removed.

        Written inside tools/ because the validator finds the repository from its
        own location. Removed by tearDown like any other plant.
        """
        with open(VALIDATOR, encoding="utf-8") as handle:
            source = handle.read()
        registration = "    report.extend(" + rule + "(errors))\n"
        self.assertEqual(1, source.count(registration),
                         "the rule is not registered exactly once: " + rule)
        path = os.path.join(ROOT, "tools", "_zz_unregistered_" + rule + ".py")
        self.plant_file(path, source.replace(registration, ""))
        return path

    def assert_refused_by(self, rule, phrase, why):
        code, out = run(VALIDATOR)
        self.assertNotEqual(0, code, why + "\n" + out[-2000:])
        self.assertIn(MARKER, out, why)
        self.assertIn(phrase, out, why + ": refused, but not by " + rule + "\n" + out[-2000:])

        # The same plant with the rule unregistered: its refusal must vanish,
        # and nothing else may refuse in its place.
        code, out = run(self.unregistered(rule))
        self.assertNotIn(phrase, out, why + ": still refused without " + rule)
        self.assertEqual(0, code,
                         why + ": another rule also refuses this plant, so it does not "
                         "prove " + rule + "\n" + out[-2000:])


class PriorNamesStayKnown(PathRuleFixture):
    PHRASE = "as a name an older build wrote, and the fresh-install probe no longer knows it"

    def test_dropping_a_prior_name_from_the_probe_is_refused(self):
        # #343's shape: the probe's evidence list loses `support-report.txt`,
        # which AtlasBackupTools still records as what an older build wrote. A
        # veteran whose only trace was that file becomes a new player.
        self.swap_in(PROBE, '        "support-report.txt",\n', "")
        self.assert_refused_by(PRIOR_NAMES, self.PHRASE,
                               "a prior name the probe forgot")

    def test_a_new_prior_name_the_probe_never_learned_is_refused(self):
        self.swap_in(BACKUP, 'private const string PriorReportName = "support-report.txt";',
                     'private const string PriorReportName = "support-report-zz.txt";')
        self.assert_refused_by(PRIOR_NAMES, self.PHRASE,
                               "a prior name that was never added to the probe")


class EditorExtensionsAgree(PathRuleFixture):
    PHRASE = "the editor's extension list differs between"

    def test_dropping_an_extension_from_the_csharp_copy_is_refused(self):
        # The docstring's own example: drop ".txt" from one copy alone and a
        # generated .txt file starts passing as something no editor opens.
        self.swap_in(CONFIG_FILES, '".cfg", ".txt", ".json",', '".cfg", ".json",')
        self.assert_refused_by(EDITOR_EXTENSIONS, self.PHRASE,
                               "the C# copy dropped an extension")

    def test_adding_an_extension_to_the_csharp_copy_is_refused(self):
        self.swap_in(CONFIG_FILES, '".yaml", ".ini",', '".yaml", ".ini", ".log",')
        self.assert_refused_by(EDITOR_EXTENSIONS, self.PHRASE,
                               "the C# copy grew an extension")

    def test_reordering_the_csharp_copy_is_refused(self):
        self.swap_in(CONFIG_FILES, '".cfg", ".txt", ".json",', '".txt", ".cfg", ".json",')
        self.assert_refused_by(EDITOR_EXTENSIONS, self.PHRASE,
                               "the C# copy was reordered")


class RootHoldsOnlyKnownNames(PathRuleFixture):
    REPORT = 'string path = CartographerPaths.InRoot("support-report.log");'

    def test_an_unknown_name_in_the_probed_root_is_refused(self):
        # #343: a name the probe does not know makes every fresh install look
        # like a returning player.
        self.swap_in(BACKUP, self.REPORT,
                     'string path = CartographerPaths.InRoot("support-report.zz");')
        self.assert_refused_by(ROOT_CONTENTS, "and the probe does not know that name",
                               "an unknown name written into the probed root")

    def test_a_marker_in_the_probed_root_is_refused(self):
        self.swap_in(BACKUP, self.REPORT,
                     'string path = CartographerPaths.InRoot("author-id.dat");')
        self.assert_refused_by(ROOT_CONTENTS, "A marker belongs under",
                               "a marker moved back into the probed root")

    def test_an_editor_extension_under_state_is_refused(self):
        # #304's exact report: author-id.txt sat in state/, and the editor
        # descends into state/ like anywhere else. A rule that looked only at
        # InRoot would miss it.
        self.swap_in(BACKUP, self.REPORT,
                     'string path = CartographerPaths.InState("support-report.txt");')
        self.assert_refused_by(ROOT_CONTENTS, "a mod manager's configuration editor opens that",
                               "a generated .txt file under state/")

    def test_the_shared_companion_sources_are_audited_too(self):
        # The shared layer is compiled into this product and writes into the
        # same directory, so a root composed there is the same defect.
        self.plant_file(os.path.join(SHARED_COMPANIONS, "ZzPathProbe.cs"),
                        "namespace TheConcernedCat.Companions;\n\n"
                        "internal static class ZzPathProbe\n{\n"
                        '    internal static string Where => CartographerPaths.InRoot("zz-shared.bin");\n'
                        "}\n")
        self.assert_refused_by(ROOT_CONTENTS, "and the probe does not know that name",
                               "an unknown name written from the shared companion sources")


if __name__ == "__main__":
    unittest.main()
