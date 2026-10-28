#!/usr/bin/env python3
"""Prepare a manager-importable, dependency-only Concerned Ladders profile.

CL-003. This never creates a live profile, installs a DLL, or downloads a mod.
The r2modman importer parses export.r2x as YAML; JSON is its YAML 1.2 subset.
Schema source: ebkr/r2modmanPlus @ 9776867fff5411048a17bca3e9e7e34779737e56,
src/utils/ProfileUtils.ts::parseYamlToExportFormat. DLLs are deliberately absent:
that importer's extractConfigsToImportedProfile refuses executable extensions.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import sys
import tomllib
import zipfile
from pathlib import Path

PROFILE_NAME = "TCC-Ladders-Dev"
PRODUCT_NAME = "ConcernedLadders"
DEPENDENCIES = ("denikson-BepInExPack_Valheim", "ValheimModding-Jotunn")
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = ROOT / "src" / PRODUCT_NAME / "Package" / "thunderstore.toml"
DEFAULT_OUTPUT = ROOT / "artifacts" / "profiles" / f"{PROFILE_NAME}.r2z"


def describe(manifest: Path) -> dict:
    raw = manifest.read_bytes()
    package = tomllib.loads(raw.decode("utf-8-sig")).get("package", {})
    if not isinstance(package, dict) or package.get("name") != PRODUCT_NAME or package.get("namespace") != "TheConcernedCat":
        raise ValueError("Expected the TheConcernedCat ConcernedLadders package manifest.")
    version = package.get("versionNumber", "")
    if not isinstance(version, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("The product needs an exact numeric Major.Minor.Patch version.")
    dependencies = package.get("dependencies", {})
    if not isinstance(dependencies, dict) or set(dependencies) != set(DEPENDENCIES):
        raise ValueError("The isolated profile requires exactly BepInExPack_Valheim and Jotunn.")
    for name, pin in dependencies.items():
        if not isinstance(pin, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", pin):
            raise ValueError(f"Dependency {name} needs an exact numeric version, received {pin!r}.")
    return {
        "product": PRODUCT_NAME,
        "version": version,
        "profileName": PROFILE_NAME,
        "dependencies": {name: dependencies[name] for name in DEPENDENCIES},
        "manifestSha256": hashlib.sha256(raw).hexdigest(),
    }


def profile_bytes(description: dict) -> bytes:
    export = {
        "profileName": PROFILE_NAME,
        "mods": [
            {
                "name": name,
                "version": dict(zip(("major", "minor", "patch"), map(int, pin.split(".")))),
                "enabled": True,
            }
            for name, pin in description["dependencies"].items()
        ],
    }
    payload = (json.dumps(export, indent=2, ensure_ascii=True) + "\n").encode("utf-8")
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        entry = zipfile.ZipInfo("export.r2x", (1980, 1, 1, 0, 0, 0))
        entry.create_system = 3
        entry.external_attr = 0o100644 << 16
        archive.writestr(entry, payload)
    return buffer.getvalue()


def write_profile(output: Path, payload: bytes) -> str:
    # A repeat of the same request is harmless; a changed pin needs a new output
    # filename so an earlier, possibly already tested artifact stays intact.
    if output.suffix.lower() != ".r2z":
        raise ValueError("Profile output must have the .r2z extension.")
    for component in (output, *output.parents):
        if component.is_symlink():
            raise ValueError("Profile output and its parents must not be symbolic links.")
    if output.exists():
        if output.is_file() and output.read_bytes() == payload:
            return "unchanged"
        raise FileExistsError("Output already exists with different contents; choose a new output path.")
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation prevents silently replacing a concurrently created file.
    with output.open("xb") as stream:
        stream.write(payload)
    return "created"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--inspect", action="store_true", help="Print the manifest contract without writing a file.")
    args = parser.parse_args(argv)
    try:
        result = describe(args.manifest)
        if not args.inspect:
            payload = profile_bytes(result)
            result.update({
                "writeResult": write_profile(args.output, payload),
                "archive": str(args.output.absolute()),
                "archiveSha256": hashlib.sha256(payload).hexdigest(),
                "status": "DEPENDENCY_IMPORT_PREPARED",
                "profileInstalled": False,
                "ladderDllIncluded": False,
                "gameLaunched": False,
            })
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, TypeError) as error:
        print(f"Profile preparation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
