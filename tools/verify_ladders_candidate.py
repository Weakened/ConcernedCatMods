#!/usr/bin/env python3
"""Read-only identity check for a post-0.1.1 Concerned Ladders candidate.

Python 3.11+, standard library only. Compare the supplied verified DLL, package,
installed DLL, Downloads copy and one complete cold-start log. The repository's
thunderstore.toml supplies the required version and dependency pins. Loose assets
are deliberately unsupported until their installation/hash contract is added.

This does not build, install, launch, change a profile, parse .NET metadata, prove
that a DLL was compiled from a commit, or test gameplay. A full SHA in a supplied
startup log is a claim, not a binary attestation. Run after the final import and
cold start: a log older than the package/verified/installed files is refused.
Copying/restoring those files may therefore require another cold start. The
Downloads copy may be made after that start; its actual Known Folder location
must be established by the caller. No arbitrary log-age expiration is imposed.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import re
import stat
import sys
import tomllib
import zipfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = ROOT / "src/ConcernedLadders/Package/thunderstore.toml"
PRODUCT = "ConcernedLadders"
NAMESPACE = "TheConcernedCat"
DLL_NAME = "TheConcernedCat.ConcernedLadders.dll"
DLL_ENTRY = "plugins/" + DLL_NAME
FILES = frozenset({"manifest.json", "README.md", "CHANGELOG.md", "LICENSE", "icon.png", DLL_ENTRY})
COPY_TARGETS = frozenset({"CHANGELOG.md", "LICENSE", DLL_ENTRY})
DEPENDENCIES = frozenset({"denikson-BepInExPack_Valheim", "ValheimModding-Jotunn"})
RETIRED_PACKAGE_HASHES = frozenset({"6ab77af980eb29871ca16b2452217d360f999b4d9ddb4d2edfd61295ef419ee9"})
RETIRED_DLL_HASHES = frozenset({"82a3e45f8ef6938f65ced6c4756b8b1fb569ed281ce2ba5d791388e9260b3649"})
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_LOG_BYTES = 32 * 1024 * 1024
MAX_METADATA_BYTES = 1024 * 1024
MAX_PLUGIN_ENTRIES = 10000
MAX_PLUGIN_DLLS = 512
MAX_RECEIPT_BYTES = 16384
VERSION = re.compile(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\Z")
COMMIT = re.compile(r"[0-9a-fA-F]{40}\Z")


class CandidateError(ValueError):
    """An input cannot establish the narrow identity claim."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise CandidateError(message)


def version_tuple(value: object) -> tuple[int, int, int]:
    require(isinstance(value, str) and len(value) <= 32 and VERSION.fullmatch(value) is not None,
            "Version must be canonical numeric Major.Minor.Patch.")
    return tuple(map(int, value.split(".")))


def fingerprint(value: os.stat_result) -> tuple[int, ...]:
    return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns


@dataclass(frozen=True)
class FileSnapshot:
    path: Path
    sha256: str
    state: tuple[int, ...]
    data: bytes | None = None

    @property
    def mtime_ns(self) -> int:
        return self.state[3]

    def unchanged(self) -> None:
        require(fingerprint(self.path.stat()) == self.state,
                "An input changed during verification; repeat after all file operations stop.")


def snapshot(path: Path, limit: int = MAX_FILE_BYTES, *, keep: bool = False) -> FileSnapshot:
    path = path.absolute()
    require(len(str(path)) <= 1024, "An input path is too long for a bounded identity receipt.")
    before = path.stat()
    require(stat.S_ISREG(before.st_mode), "Every file input must be a regular readable file.")
    require(0 < before.st_size <= limit, "An input is empty or exceeds the bounded file-size limit.")
    digest = hashlib.sha256()
    chunks: list[bytes] = []
    total = 0
    with path.open("rb") as stream:
        require(fingerprint(os.fstat(stream.fileno())) == fingerprint(before),
                "An input was replaced before it could be read.")
        while chunk := stream.read(1024 * 1024):
            total += len(chunk)
            require(total <= limit, "An input grew beyond the bounded file-size limit.")
            digest.update(chunk)
            if keep:
                chunks.append(chunk)
        require(fingerprint(os.fstat(stream.fileno())) == fingerprint(before),
                "An input changed while being read.")
    result = FileSnapshot(path, digest.hexdigest(), fingerprint(before), b"".join(chunks) if keep else None)
    result.unchanged()
    return result


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result: dict = {}
    for key, value in pairs:
        require(key not in result, "Package manifest contains a duplicate JSON key.")
        result[key] = value
    return result


def read_contract(path: Path, version: str) -> tuple[FileSnapshot, list[str]]:
    source = snapshot(path, MAX_METADATA_BYTES, keep=True)
    document = tomllib.loads(source.data.decode("utf-8-sig"))
    package = document.get("package", {})
    require(isinstance(package, dict) and package.get("name") == PRODUCT and
            package.get("namespace") == NAMESPACE, "Repository TOML names a different product.")
    require(package.get("versionNumber") == version,
            "Requested version does not match repository TOML; finish the source version update first.")
    pins = package.get("dependencies", {})
    require(isinstance(pins, dict) and set(pins) == DEPENDENCIES,
            "Repository TOML must declare exactly the two supported ladder dependencies.")
    for pin in pins.values():
        version_tuple(pin)
    build = document.get("build", {})
    copies = build.get("copy", []) if isinstance(build, dict) else []
    require(isinstance(copies, list) and all(isinstance(entry, dict) for entry in copies),
            "Repository TOML build.copy is malformed.")
    targets = [entry.get("target") for entry in copies]
    require(len(targets) == len(COPY_TARGETS) and all(isinstance(item, str) for item in targets) and
            set(targets) == COPY_TARGETS,
            "Only the existing six-file package is supported; loose assets need an explicit verifier extension.")
    return source, sorted(name + "-" + pin for name, pin in pins.items())


def package_dll(archive_file: FileSnapshot, version: str, dependencies: list[str]) -> str:
    require(archive_file.path.suffix.lower() == ".zip", "--package must be a mod ZIP, never a profile .r2z.")
    require(archive_file.sha256 not in RETIRED_PACKAGE_HASHES, "This is the retired 0.1.1 package hash.")
    payloads: dict[str, bytes] = {}
    seen: set[str] = set()
    with zipfile.ZipFile(io.BytesIO(archive_file.data)) as archive:
        entries = archive.infolist()
        require(len(entries) <= len(FILES) + 1, "ZIP has extra entries or duplicate payloads.")
        total = 0
        for entry in entries:
            name = entry.orig_filename
            parts = name.rstrip("/").split("/")
            require(name == entry.filename and name and not name.startswith("/") and
                    "\\" not in name and ":" not in name and
                    all(part and part not in (".", "..") and part == part.rstrip(" .") for part in parts) and
                    not any(ord(char) < 32 or ord(char) == 127 for char in name),
                    "ZIP contains an unsafe, ambiguous or traversing path.")
            key = name.rstrip("/").casefold()
            require(key not in seen, "ZIP contains duplicate paths, including a case-insensitive collision.")
            seen.add(key)
            kind = stat.S_IFMT(entry.external_attr >> 16)
            require(kind in (0, stat.S_IFREG, stat.S_IFDIR) and not (entry.flag_bits & 1),
                    "ZIP links, special files and encrypted entries are unsupported.")
            if entry.is_dir():
                require(name == "plugins/" and entry.file_size == 0 and kind in (0, stat.S_IFDIR),
                        "ZIP directories may only be the optional plugins/ directory; no wrapper folder.")
                continue
            require(kind in (0, stat.S_IFREG) and name in FILES,
                    "ZIP contains an unexpected file; dependency DLLs, source/profile payloads and loose assets are refused.")
            require(entry.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED),
                    "ZIP uses an unsupported compression method.")
            total += entry.file_size
            require(0 < entry.file_size <= MAX_FILE_BYTES and total <= MAX_FILE_BYTES,
                    "ZIP payload is empty or exceeds the bounded expanded-size limit.")
            if name == "manifest.json":
                require(entry.file_size <= MAX_METADATA_BYTES, "ZIP manifest exceeds its size limit.")
            # Reading every allowed entry also validates its CRC; nothing is extracted.
            with archive.open(entry) as stream:
                data = stream.read(entry.file_size + 1)
            require(len(data) == entry.file_size, "ZIP entry size differs from its directory record.")
            payloads[name] = data
    require(set(payloads) == FILES, "ZIP must contain the six required files at their exact package paths.")
    manifest = json.loads(payloads["manifest.json"].decode("utf-8-sig"), object_pairs_hook=unique_object)
    require(isinstance(manifest, dict) and manifest.get("name") == PRODUCT,
            "ZIP manifest names a different product.")
    require(manifest.get("version_number") == version, "ZIP manifest version differs from the requested version.")
    actual = manifest.get("dependencies")
    require(isinstance(actual, list) and all(isinstance(item, str) for item in actual) and
            sorted(actual) == dependencies, "ZIP dependencies differ from repository TOML, or contain duplicates.")
    digest = hashlib.sha256(payloads[DLL_ENTRY]).hexdigest()
    require(digest not in RETIRED_DLL_HASHES, "ZIP contains the retired 0.1.1 DLL hash.")
    return digest


def is_link_or_reparse(path: Path) -> bool:
    info = path.lstat()
    return stat.S_ISLNK(info.st_mode) or bool(getattr(info, "st_file_attributes", 0) & 0x400)


def plugin_inventory(plugins: Path) -> dict[str, int]:
    """Bounded names/types snapshot, repeated before success; not an atomic view."""
    require(plugins.name.casefold() == "plugins" and plugins.parent.name.casefold() == "bepinex" and
            plugins.is_dir(), "--plugins must name the actual profile's BepInEx/plugins directory.")
    require(not is_link_or_reparse(plugins), "A linked/reparse plugin tree cannot establish complete duplicate detection.")
    inventory: dict[str, int] = {}
    entry_count = 0

    def walk_error(error: OSError) -> None:
        raise CandidateError("A plugin directory could not be read; duplicate detection is incomplete.") from error

    for directory, directories, filenames in os.walk(plugins, followlinks=False, onerror=walk_error):
        entry_count += len(directories) + len(filenames)
        require(entry_count <= MAX_PLUGIN_ENTRIES, "Plugin tree exceeds the bounded scan limit.")
        for name in directories + filenames:
            path = Path(directory) / name
            require(not is_link_or_reparse(path),
                    "A link/reparse entry in plugins makes duplicate detection incomplete.")
            inventory[path.relative_to(plugins).as_posix()] = stat.S_IFMT(path.lstat().st_mode)
    return inventory


def installed_dll(plugins: Path, expected_hash: str) -> tuple[FileSnapshot, list[FileSnapshot], dict[str, int]]:
    inventory = plugin_inventory(plugins)
    scanned: list[FileSnapshot] = []
    named: list[FileSnapshot] = []
    matching: list[FileSnapshot] = []
    for relative, kind in inventory.items():
        if kind == stat.S_IFDIR or not relative.lower().endswith(".dll"):
            continue
        require(len(scanned) < MAX_PLUGIN_DLLS, "Plugin DLL count exceeds the bounded scan limit.")
        item = snapshot(plugins / relative)
        require(item.sha256 not in RETIRED_DLL_HASHES, "Plugins still contains the retired 0.1.1 DLL, possibly renamed.")
        scanned.append(item)
        if item.path.name.casefold() == DLL_NAME.casefold():
            named.append(item)
        if item.sha256 == expected_hash:
            matching.append(item)
    require(len(named) == 1, "Expected exactly one named Concerned Ladders DLL anywhere under plugins.")
    require(named[0].sha256 == expected_hash, "Installed DLL differs from the verified/packaged DLL.")
    require(len(matching) == 1, "A renamed or second byte-identical Concerned Ladders DLL is also under plugins.")
    return named[0], scanned, inventory


def check_log(log: FileSnapshot, version: str, commit: str, candidate_files: list[FileSnapshot]) -> dict:
    lines = log.data.decode("utf-8-sig").splitlines()
    starts = [index for index, line in enumerate(lines) if re.fullmatch(
        r"\[Message\s*:\s*BepInEx\s*\]\s+BepInEx [0-9.]+ - valheim(?:\s.*)?", line, re.IGNORECASE)]
    require(len(starts) == 1, "Game log must contain exactly one BepInEx cold-start banner, not an appended or partial log.")
    banners = [(index, line) for index, line in enumerate(lines)
               if re.search(r"\bConcerned Ladders\b.*\bloaded\.", line)]
    require(len(banners) == 1, "Game log must contain exactly one Concerned Ladders startup banner.")
    index, banner = banners[0]
    match = re.fullmatch(r"\[Info\s*:\s*Concerned Ladders\s*\]\s+Concerned Ladders "
                         r"([0-9]+\.[0-9]+\.[0-9]+)\+([0-9a-fA-F]{40}) loaded\.(?: .*)?", banner)
    require(match is not None and match.group(1) == version and match.group(2).lower() == commit,
            "Startup banner must claim the exact requested version and full 40-character source SHA.")
    complete = [i for i, line in enumerate(lines) if re.fullmatch(
        r"\[Message\s*:\s*BepInEx\s*\]\s+Chainloader startup complete", line)]
    require(len(complete) == 1 and starts[0] < index < complete[0],
            "Game log must include a single completed startup after the plugin banner.")
    require(all(log.mtime_ns >= item.mtime_ns for item in candidate_files),
            "Game log predates a candidate file; copying/restoring/building after testing requires a fresh cold start.")
    return {
        "coldStartBanners": 1,
        "pluginStartupBanners": 1,
        "startupCompleted": True,
        "fullSourceCommitBannerMatched": True,
        "mtimeNotBeforePackageVerifiedAndInstalledDll": True,
    }


def verify_candidate(*, package: Path, verified_dll: Path, plugins: Path, game_log: Path,
                     source_commit: str, version: str, downloads_copy: Path,
                     source_manifest: Path | None = None) -> dict:
    require(version_tuple(version) > (0, 1, 1), "Version must be newer than retired 0.1.1; reusing that version is refused.")
    require(COMMIT.fullmatch(source_commit) is not None, "Source commit must be a full 40-character hexadecimal SHA.")
    commit = source_commit.lower()
    contract, dependencies = read_contract(source_manifest or DEFAULT_MANIFEST, version)
    require(verified_dll.name.casefold() == DLL_NAME.casefold(), "--verified-dll must be the product's own named build output.")
    verified = snapshot(verified_dll)
    require(verified.sha256 not in RETIRED_DLL_HASHES, "Verified build output has the retired 0.1.1 DLL hash.")
    archive = snapshot(package, keep=True)
    packaged_hash = package_dll(archive, version, dependencies)
    require(packaged_hash == verified.sha256, "ZIP DLL differs from the supplied verified DLL.")
    installed, scanned, initial_inventory = installed_dll(plugins, verified.sha256)
    require(downloads_copy.suffix.lower() == ".zip", "--downloads-copy must be a mod ZIP.")
    require(not os.path.samefile(package, downloads_copy), "Downloads copy must be a distinct file, not the source package itself.")
    copied = snapshot(downloads_copy)
    require(copied.sha256 == archive.sha256, "Downloads copy differs from the checked package ZIP.")
    log = snapshot(game_log, MAX_LOG_BYTES, keep=True)
    log_checks = check_log(log, version, commit, [archive, verified, installed])
    for item in [contract, archive, verified, copied, log, *scanned]:
        item.unchanged()
    require(plugin_inventory(plugins) == initial_inventory,
            "Plugin tree changed after its initial scan; repeat after all profile operations stop.")

    def evidence(item: FileSnapshot) -> dict:
        return {"path": str(item.path), "sha256": item.sha256}

    return {
        "schemaVersion": 1,
        "status": "IDENTITY_MATCH_ONLY",
        "product": NAMESPACE + "-" + PRODUCT,
        "version": version,
        "sourceCommitClaim": commit,
        "sourceIdentityBasis": "startup_log_claim_not_binary_attestation",
        "binaryMetadataParsed": False,
        "binaryProvenanceVerified": False,
        "gameplayVerified": False,
        "writesPerformed": False,
        "gameLaunchPerformed": False,
        "sourceManifest": evidence(contract),
        "dependencies": dependencies,
        "package": {**evidence(archive), "requiredFileCount": len(FILES)},
        "verifiedDll": evidence(verified),
        "zipEntryDllSha256": packaged_hash,
        "installedDll": evidence(installed),
        "downloadsCopy": evidence(copied),
        "gameLog": {**evidence(log), **log_checks},
        "duplicateScan": {"namedOwnDllCopies": 1, "matchingOwnHashCopies": 1, "allDllsHashed": len(scanned),
                          "inventoryMatchedOnSecondScan": True},
        "limitations": [
            "Hash agreement and a supplied startup log do not prove which source compiled the DLL or that gameplay works.",
            "Renamed assemblies with different bytes cannot be identified as this plugin without metadata inspection.",
            "Two inventory scans form a point-in-time check, not an atomic installation guarantee or a filesystem watcher.",
            "The caller must establish the active manager profile, real Downloads Known Folder and authenticity of the fresh log.",
            "File times are a conservative freshness check, not launch attestation; no game process was observed.",
            "Loose assets are unsupported; the six-file contract permits owned resources embedded in the DLL only.",
        ],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("package", "verified-dll", "plugins", "game-log", "downloads-copy"):
        parser.add_argument("--" + option, type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args(argv)
    try:
        result = verify_candidate(**vars(args))
        encoded = json.dumps(result, indent=2, ensure_ascii=True)
        require(len(encoded.encode("utf-8")) <= MAX_RECEIPT_BYTES, "Identity receipt would exceed its bounded output size.")
        print(encoded)
        return 0
    except (CandidateError, OSError, UnicodeError, json.JSONDecodeError, tomllib.TOMLDecodeError,
            zipfile.BadZipFile, RuntimeError) as error:
        # Do not echo a raw log, manifest, arbitrary ZIP filename, or filesystem exception.
        detail = str(error)[:240] if isinstance(error, CandidateError) else "An input could not be read or parsed; check files and permissions."
        print(json.dumps({"schemaVersion": 1, "status": "IDENTITY_MISMATCH", "error": detail,
                          "gameplayVerified": False, "binaryProvenanceVerified": False,
                          "writesPerformed": False, "gameLaunchPerformed": False}, indent=2))
        return 1


if __name__ == "__main__":
    sys.exit(main())
