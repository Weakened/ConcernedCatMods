"""Identity/transport fixtures only: the byte strings below are NOT game DLLs.

These tests intentionally demonstrate that matching opaque bytes and a supplied
log cannot attest compilation or gameplay. They never build/launch Valheim or
read a real profile; all supplied artifacts live in a temporary fixture tree.
"""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import stat
import sys
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_ladders_candidate as checker


VERSION = "0.2.0"
COMMIT = "0123456789abcdef0123456789abcdef01234567"
OPAQUE_DLL = b"Hash-transport fixture, NOT a PE/.NET/Valheim assembly.\n"
OTHER_DLL = b"Another opaque fixture, NOT a dependency binary.\n"
MANIFEST = '''[package]
namespace = "TheConcernedCat"
name = "ConcernedLadders"
versionNumber = "0.2.0"
[package.dependencies]
denikson-BepInExPack_Valheim = "5.4.2351"
ValheimModding-Jotunn = "2.30.2"
[[build.copy]]
source = "own.dll"
target = "plugins/TheConcernedCat.ConcernedLadders.dll"
[[build.copy]]
source = "CHANGELOG.md"
target = "CHANGELOG.md"
[[build.copy]]
source = "LICENSE"
target = "LICENSE"
'''


class LadderCandidateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.source_manifest = self.root / "thunderstore.toml"
        self.source_manifest.write_text(MANIFEST, encoding="utf-8")
        self.verified = self.root / "build" / checker.DLL_NAME
        self.verified.parent.mkdir()
        self.plugins = self.root / "TCC-Ladders-Dev" / "BepInEx" / "plugins"
        self.installed = self.plugins / "TheConcernedCat-ConcernedLadders" / checker.DLL_NAME
        self.installed.parent.mkdir(parents=True)
        (self.plugins / "Jotunn.dll").write_bytes(OTHER_DLL)
        self.package = self.root / "artifacts" / "TheConcernedCat-ConcernedLadders-0.2.0.zip"
        self.package.parent.mkdir()
        self.downloads = self.root / "Downloads" / self.package.name
        self.downloads.parent.mkdir()
        self.log = self.root / "LogOutput.log"
        self.arguments = {
            "package": self.package,
            "verified_dll": self.verified,
            "plugins": self.plugins,
            "game_log": self.log,
            "source_commit": COMMIT,
            "version": VERSION,
            "downloads_copy": self.downloads,
        }
        self.reset_payload()

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def banner(self, version: str = VERSION, commit: str = COMMIT) -> str:
        return f"[Info   :Concerned Ladders] Concerned Ladders {version}+{commit} loaded. Development candidate: gameplay verification required.\n"

    def cold_log(self, banner: str | None = None) -> str:
        return ("[Message:   BepInEx] BepInEx 5.4.23.5 - valheim (fixture)\n"
                "[Info   :   BepInEx] Loading [Concerned Ladders 0.2.0]\n"
                + (self.banner() if banner is None else banner)
                + "[Message:   BepInEx] Chainloader startup complete\n")

    def entries(self, *, metadata: object | None = None, dll: bytes = OPAQUE_DLL) -> list[tuple[str, bytes]]:
        if metadata is None:
            metadata = {
                "name": "ConcernedLadders",
                "version_number": VERSION,
                "dependencies": ["denikson-BepInExPack_Valheim-5.4.2351", "ValheimModding-Jotunn-2.30.2"],
            }
        manifest = metadata if isinstance(metadata, bytes) else json.dumps(metadata).encode("utf-8")
        return [
            ("manifest.json", manifest),
            ("README.md", b"Identity fixture, not an installable release.\n"),
            ("CHANGELOG.md", b"# Changelog\n## 0.2.0\nFixture only.\n"),
            ("LICENSE", b"Fixture license text.\n"),
            ("icon.png", b"Opaque icon transport fixture, not artwork.\n"),
            (checker.DLL_ENTRY, dll),
        ]

    def write_package(self, entries: list[tuple[str | zipfile.ZipInfo, bytes]] | None = None) -> None:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)  # Deliberately duplicate ZIP paths in regression fixtures.
            with zipfile.ZipFile(self.package, "w", compression=zipfile.ZIP_STORED) as archive:
                for name, data in self.entries() if entries is None else entries:
                    archive.writestr(name, data)
        self.downloads.write_bytes(self.package.read_bytes())
        self.log.write_text(self.cold_log(), encoding="utf-8")

    def reset_payload(self) -> None:
        self.verified.write_bytes(OPAQUE_DLL)
        self.installed.write_bytes(OPAQUE_DLL)
        self.write_package()

    def run_cli(self, **changes: object) -> tuple[int, dict]:
        arguments = {**self.arguments, **changes}
        argv = [argument for name, value in arguments.items()
                for argument in ("--" + name.replace("_", "-"), str(value))]
        output = io.StringIO()
        with mock.patch.object(checker, "DEFAULT_MANIFEST", self.source_manifest), contextlib.redirect_stdout(output):
            result = checker.main(argv)
        self.assertLessEqual(len(output.getvalue().encode("utf-8")), checker.MAX_RECEIPT_BYTES)
        return result, json.loads(output.getvalue())

    def refused(self, message: str | None = None, **changes: object) -> dict:
        code, receipt = self.run_cli(**changes)
        self.assertEqual(1, code, receipt)
        self.assertEqual("IDENTITY_MISMATCH", receipt["status"])
        self.assertFalse(receipt["gameplayVerified"])
        self.assertFalse(receipt["writesPerformed"])
        if message:
            self.assertIn(message, receipt["error"])
        return receipt

    def test_matching_opaque_bytes_return_only_identity_and_do_not_modify_files(self) -> None:
        before = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in self.root.rglob("*") if path.is_file()}
        code, receipt = self.run_cli()
        self.assertEqual(0, code, receipt)
        self.assertEqual("IDENTITY_MATCH_ONLY", receipt["status"])
        self.assertEqual("startup_log_claim_not_binary_attestation", receipt["sourceIdentityBasis"])
        for field in ("gameplayVerified", "binaryMetadataParsed", "binaryProvenanceVerified", "writesPerformed", "gameLaunchPerformed"):
            self.assertFalse(receipt[field])
        expected = hashlib.sha256(OPAQUE_DLL).hexdigest()
        self.assertEqual(expected, receipt["verifiedDll"]["sha256"])
        self.assertEqual(expected, receipt["zipEntryDllSha256"])
        self.assertEqual(expected, receipt["installedDll"]["sha256"])
        self.assertEqual(receipt["package"]["sha256"], receipt["downloadsCopy"]["sha256"])
        self.assertEqual({"namedOwnDllCopies": 1, "matchingOwnHashCopies": 1, "allDllsHashed": 2,
                          "inventoryMatchedOnSecondScan": True}, receipt["duplicateScan"])
        self.assertEqual(COMMIT, receipt["sourceCommitClaim"])
        after = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in self.root.rglob("*") if path.is_file()}
        self.assertEqual(before, after)

    def test_old_reused_invalid_versions_and_short_commit_are_refused(self) -> None:
        for version in ("0.0.9", "0.1.0", "0.1.1", "0.2.0-rc.1", "00.2.0", "0.2.0.0"):
            with self.subTest(version=version):
                self.refused(version=version)
        self.refused("full 40-character", source_commit=COMMIT[:7])
        self.refused("repository TOML", version="0.3.0")

    def test_new_argument_cannot_launder_old_source_version_or_dependency_pins(self) -> None:
        self.source_manifest.write_text(MANIFEST.replace('versionNumber = "0.2.0"', 'versionNumber = "0.1.1"'), encoding="utf-8")
        self.refused("repository TOML")
        self.source_manifest.write_text(MANIFEST.replace('ValheimModding-Jotunn = "2.30.2"', 'ValheimModding-Jotunn = "2.30.3"'), encoding="utf-8")
        self.refused("dependencies differ")
        self.source_manifest.write_text(MANIFEST + '\n[[build.copy]]\nsource = "climb.bundle"\ntarget = "plugins/climb.bundle"\n', encoding="utf-8")
        self.refused("loose assets")

    def test_package_manifest_must_match_identity_version_and_exact_dependencies(self) -> None:
        original = json.loads(self.entries()[0][1])
        invalid = [
            {**original, "name": "ConcernedForeman"},
            {**original, "version_number": "0.1.1"},
            {**original, "dependencies": original["dependencies"][:1]},
            {**original, "dependencies": original["dependencies"] + [original["dependencies"][0]]},
            {**original, "dependencies": original["dependencies"] + ["TheConcernedCat-ConcernedNPC-0.4.0"]},
            [],
            b'{"name":"ConcernedLadders","name":"ConcernedLadders"}',
        ]
        for metadata in invalid:
            with self.subTest(metadata=metadata):
                self.write_package(self.entries(metadata=metadata))
                self.refused()

    def test_zip_wrappers_traversal_windows_paths_and_duplicate_case_are_refused(self) -> None:
        cases = [
            [("wrapper/" + name, data) for name, data in self.entries()],
            self.entries() + [("../payload", b"bad")],
            self.entries() + [("/payload", b"bad")],
            self.entries() + [("C:/payload", b"bad")],
            self.entries() + [("plugins\\payload", b"bad")],
            self.entries() + [("README.md ", b"bad")],
            self.entries() + [("README.md", b"duplicate")],
            self.entries() + [("readme.md", b"duplicate for Windows")],
        ]
        for entries in cases:
            with self.subTest(extra=entries[-1][0]):
                self.write_package(entries)
                self.refused()

    def test_dependency_source_profile_and_undeclared_assets_never_ship_through_gate(self) -> None:
        for extra in ("plugins/Jotunn.dll", "plugins/assembly_valheim.dll", "src/Plugin.cs", "export.r2x", "TCC-Ladders-Dev.r2z", "plugins/climb.bundle"):
            with self.subTest(extra=extra):
                self.write_package(self.entries() + [(extra, b"unsupported fixture")])
                self.refused("unexpected file")
        renamed = self.package.with_suffix(".r2z")
        renamed.write_bytes(self.package.read_bytes())
        self.refused("mod ZIP", package=renamed)

    def test_optional_plugins_directory_is_allowed_but_links_and_missing_files_are_not(self) -> None:
        self.write_package(self.entries() + [("plugins/", b"")])
        self.assertEqual(0, self.run_cli()[0])
        link = zipfile.ZipInfo("README.md")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        entries = [(link if name == "README.md" else name, data) for name, data in self.entries()]
        self.write_package(entries)
        self.refused("ZIP links")
        self.write_package(self.entries()[:-1])
        self.refused("six required files")

    def test_verified_package_and_installed_dll_mismatches_are_refused(self) -> None:
        self.verified.write_bytes(b"Different build fixture")
        self.refused("ZIP DLL differs")
        self.reset_payload()
        self.write_package(self.entries(dll=b"Different packaged fixture"))
        self.refused("ZIP DLL differs")
        self.reset_payload()
        self.installed.write_bytes(b"Different installed fixture")
        self.refused("Installed DLL differs")

    def test_retired_hash_guards_apply_to_actual_transport_and_renamed_plugins(self) -> None:
        # Pin the opaque fixture hashes as retired for this regression; no real DLL is forged.
        retired_package = hashlib.sha256(self.package.read_bytes()).hexdigest()
        with mock.patch.object(checker, "RETIRED_PACKAGE_HASHES", {retired_package}):
            self.refused("retired 0.1.1 package")
        with mock.patch.object(checker, "RETIRED_DLL_HASHES", {hashlib.sha256(OPAQUE_DLL).hexdigest()}):
            self.refused("retired 0.1.1 DLL")
        old = self.plugins / "renamed-old.dll"
        old.write_bytes(b"Retired renamed opaque fixture")
        with mock.patch.object(checker, "RETIRED_DLL_HASHES", {hashlib.sha256(old.read_bytes()).hexdigest()}):
            self.refused("retired 0.1.1 DLL")

    def test_named_and_renamed_duplicates_including_disabled_subfolders_are_refused(self) -> None:
        disabled = self.plugins / "disabled"
        disabled.mkdir()
        for name in (checker.DLL_NAME, checker.DLL_NAME.upper(), "renamed-copy.DLL"):
            with self.subTest(name=name):
                duplicate = disabled / name
                duplicate.write_bytes(OPAQUE_DLL)
                self.refused()
                duplicate.unlink()
        self.installed.unlink()
        self.refused("exactly one named")

    def test_new_matching_renamed_dll_after_initial_scan_is_refused(self) -> None:
        original_scan = checker.installed_dll

        def concurrent_copy(*args: object, **kwargs: object) -> object:
            result = original_scan(*args, **kwargs)
            (self.plugins / "concurrently-added-renamed.dll").write_bytes(OPAQUE_DLL)
            return result

        with mock.patch.object(checker, "installed_dll", side_effect=concurrent_copy):
            self.refused("Plugin tree changed after its initial scan")

    def test_second_inventory_refuses_removal_and_type_change_of_unhashed_entries(self) -> None:
        original_scan = checker.installed_dll
        marker = self.plugins / "mod-notes.txt"
        for replace_with_directory in (False, True):
            with self.subTest(replace_with_directory=replace_with_directory):
                marker.write_text("Unhashed non-DLL inventory entry", encoding="utf-8")

                def concurrent_change(*args: object, **kwargs: object) -> object:
                    result = original_scan(*args, **kwargs)
                    marker.unlink()
                    if replace_with_directory:
                        marker.mkdir()
                    return result

                with mock.patch.object(checker, "installed_dll", side_effect=concurrent_change):
                    self.refused("Plugin tree changed after its initial scan")
                if marker.is_dir():
                    marker.rmdir()

    def test_downloads_requires_equal_bytes_in_a_distinct_file_and_can_be_copied_after_log(self) -> None:
        self.downloads.write_bytes(b"Older differently named package")
        self.refused("Downloads copy differs")
        self.refused("distinct file", downloads_copy=self.package)
        self.downloads.write_bytes(self.package.read_bytes())
        self.assertEqual(0, self.run_cli()[0])

    def test_current_complete_cold_start_requires_exact_full_sha_and_only_one_plugin_banner(self) -> None:
        wrong_banners = [
            self.banner(commit=COMMIT[:7]),
            self.banner(commit="f" * 40),
            self.banner(version="0.1.1"),
            self.banner().replace("+" + COMMIT, ""),
            self.banner() + self.banner(),
        ]
        for banner in wrong_banners:
            with self.subTest(banner=banner):
                self.log.write_text(self.cold_log(banner), encoding="utf-8")
                self.refused()
        for log in (self.banner(), self.cold_log() * 2,
                    self.cold_log().replace("[Message:   BepInEx] Chainloader startup complete\n", "")):
            with self.subTest(log=log):
                self.log.write_text(log, encoding="utf-8")
                self.refused()

    def test_log_predating_candidate_files_is_refused_but_old_matching_evidence_does_not_expire(self) -> None:
        latest = max(path.stat().st_mtime_ns for path in (self.package, self.verified, self.installed))
        os.utime(self.log, ns=(latest - 2_000_000_000, latest - 2_000_000_000))
        self.refused("predates a candidate file")
        historical = 1_600_000_000_000_000_000
        for path in (self.package, self.verified, self.installed):
            os.utime(path, ns=(historical, historical))
        os.utime(self.log, ns=(historical + 2_000_000_000, historical + 2_000_000_000))
        self.assertEqual(0, self.run_cli()[0])

    def test_unreadable_corrupt_or_incomplete_inputs_fail_without_dumping_contents(self) -> None:
        self.refused(game_log=self.root / "missing.log")
        self.package.write_bytes(b"Sensitive malformed archive contents never echoed")
        receipt = self.refused()
        self.assertNotIn("Sensitive", json.dumps(receipt))
        self.reset_payload()
        self.refused("BepInEx/plugins", plugins=self.root)
        with mock.patch.object(checker, "MAX_PLUGIN_ENTRIES", 1):
            self.refused("bounded scan limit")

    def test_linked_plugin_entry_cannot_hide_a_duplicate(self) -> None:
        link = self.plugins / "linked.dll"
        try:
            link.symlink_to(self.verified)
        except (NotImplementedError, OSError):
            self.skipTest("This machine cannot create test symlinks.")
        self.refused("link/reparse")


if __name__ == "__main__":
    unittest.main()
