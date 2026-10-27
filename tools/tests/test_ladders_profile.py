from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from prepare_ladders_profile import describe, main, profile_bytes, write_profile


MANIFEST = '''[package]
namespace = "TheConcernedCat"
name = "ConcernedLadders"
versionNumber = "0.1.0"
[package.dependencies]
denikson-BepInExPack_Valheim = "5.4.2351"
ValheimModding-Jotunn = "2.30.2"
'''


class LadderProfileTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.manifest = self.root / "thunderstore.toml"
        self.manifest.write_text(MANIFEST, encoding="utf-8")

    def tearDown(self) -> None:
        self.directory.cleanup()

    def test_archive_has_import_schema_and_no_unpublished_package_or_executable(self) -> None:
        payload = profile_bytes(describe(self.manifest))
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            self.assertIsNone(archive.testzip())
            self.assertEqual(["export.r2x"], archive.namelist())
            export = json.loads(archive.read("export.r2x"))
        self.assertEqual("TCC-Ladders-Dev", export["profileName"])
        self.assertEqual([
            {"name": "denikson-BepInExPack_Valheim", "version": {"major": 5, "minor": 4, "patch": 2351}, "enabled": True},
            {"name": "ValheimModding-Jotunn", "version": {"major": 2, "minor": 30, "patch": 2}, "enabled": True},
        ], export["mods"])

    def test_changed_manifest_pin_reaches_import_instead_of_silent_stale_default(self) -> None:
        self.manifest.write_text(MANIFEST.replace('"2.30.2"', '"2.30.3"'), encoding="utf-8")
        with zipfile.ZipFile(io.BytesIO(profile_bytes(describe(self.manifest)))) as archive:
            export = json.loads(archive.read("export.r2x"))
        self.assertEqual(3, export["mods"][1]["version"]["patch"])

    def test_foreman_npc_or_other_mod_cannot_sneak_into_clean_profile(self) -> None:
        self.manifest.write_text(MANIFEST + 'TheConcernedCat-ConcernedNPC = "0.4.0"\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exactly"):
            describe(self.manifest)

    def test_wrong_product_and_unpinned_versions_are_refused(self) -> None:
        for invalid in (
            MANIFEST.replace('name = "ConcernedLadders"', 'name = "ConcernedForeman"'),
            MANIFEST.replace('"2.30.2"', '"latest"'),
            MANIFEST.replace('"2.30.2"', '"2.30.*"'),
        ):
            with self.subTest(manifest=invalid):
                self.manifest.write_text(invalid, encoding="utf-8")
                with self.assertRaises(ValueError):
                    describe(self.manifest)

    def test_deterministic_and_idempotent_but_changed_output_is_preserved(self) -> None:
        payload = profile_bytes(describe(self.manifest))
        self.assertEqual(payload, profile_bytes(describe(self.manifest)))
        output = self.root / "profile.r2z"
        self.assertEqual("created", write_profile(output, payload))
        self.assertEqual("unchanged", write_profile(output, payload))
        original = output.read_bytes()
        with self.assertRaises(FileExistsError):
            write_profile(output, payload + b"different")
        self.assertEqual(original, output.read_bytes())

    def test_output_cannot_replace_a_script_or_follow_symlink(self) -> None:
        with self.assertRaisesRegex(ValueError, "extension"):
            write_profile(self.root / "setup.ps1", b"data")
        original = self.root / "original.r2z"
        original.write_bytes(b"preserve")
        link = self.root / "linked.r2z"
        try:
            link.symlink_to(original)
        except (NotImplementedError, OSError):
            self.skipTest("This machine cannot create test symlinks.")
        with self.assertRaisesRegex(ValueError, "symbolic"):
            write_profile(link, b"replacement")
        self.assertEqual(b"preserve", original.read_bytes())

    def test_inspect_is_read_only_and_receipt_does_not_claim_installation(self) -> None:
        output = self.root / "not-created.r2z"
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            self.assertEqual(0, main(["--manifest", str(self.manifest), "--output", str(output), "--inspect"]))
        self.assertFalse(output.exists())
        self.assertEqual("ConcernedLadders", json.loads(capture.getvalue())["product"])
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            self.assertEqual(0, main(["--manifest", str(self.manifest), "--output", str(output)]))
        receipt = json.loads(capture.getvalue())
        self.assertEqual("DEPENDENCY_IMPORT_PREPARED", receipt["status"])
        self.assertFalse(receipt["profileInstalled"])
        self.assertFalse(receipt["ladderDllIncluded"])
        self.assertFalse(receipt["gameLaunched"])


if __name__ == "__main__":
    unittest.main()
