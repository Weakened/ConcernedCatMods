#!/usr/bin/env python3
"""Static repository and Thunderstore package-source validation.

This intentionally does not require Valheim or its licensed assemblies.
"""

from __future__ import annotations

import argparse
import re
import struct
import sys
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "src" / "ConcernedCartographer" / "Package"
CSPROJ = ROOT / "src" / "ConcernedCartographer" / "ConcernedCartographer.csproj"
PLUGIN = ROOT / "src" / "ConcernedCartographer" / "Plugin.cs"
BINARY = ROOT / "src" / "ConcernedCartographer" / "bin" / "Release" / "net48" / "TheConcernedCat.ConcernedCartographer.dll"


def fail(message: str, errors: list[str]) -> None:
    errors.append(message)


def png_dimensions(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise ValueError("not a valid PNG with an IHDR header")
    return struct.unpack(">II", data[16:24])


def read_csproj_version() -> str:
    tree = ET.parse(CSPROJ)
    root = tree.getroot()
    node = root.find(".//Version")
    if node is None or not node.text:
        raise ValueError("<Version> is missing from the C# project")
    return node.text.strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-binary", action="store_true")
    parser.add_argument("--expected-version")
    args = parser.parse_args()

    errors: list[str] = []

    required = [
        ROOT / "README.md",
        ROOT / "LICENSE",
        ROOT / "AGENTS.md",
        ROOT / "CLAUDE.md",
        ROOT / "Environment.props.example",
        ROOT / "DoPrebuild.props",
        PACKAGE / "thunderstore.toml",
        PACKAGE / "README.md",
        PACKAGE / "CHANGELOG.md",
        PACKAGE / "icon.png",
    ]
    for path in required:
        if not path.is_file():
            fail(f"Missing required file: {path.relative_to(ROOT)}", errors)

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    try:
        width, height = png_dimensions(PACKAGE / "icon.png")
        if (width, height) != (256, 256):
            fail(f"icon.png must be 256x256, found {width}x{height}", errors)
    except Exception as exc:
        fail(f"Could not validate icon.png: {exc}", errors)

    try:
        config = tomllib.loads((PACKAGE / "thunderstore.toml").read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"Invalid thunderstore.toml: {exc}", errors)
        config = {}

    package = config.get("package", {})
    build = config.get("build", {})
    publish = config.get("publish", {})
    dependencies = package.get("dependencies", {})

    expected_identity = {
        "namespace": "TheConcernedCat",
        "name": "ConcernedCartographer",
        "websiteUrl": "https://github.com/Weakened/ConcernedCatMods",
    }
    for key, expected in expected_identity.items():
        if package.get(key) != expected:
            fail(f"package.{key} must be {expected!r}", errors)

    description = package.get("description", "")
    if not description or len(description) > 250:
        fail("Thunderstore description must be 1-250 characters", errors)

    if dependencies.get("denikson-BepInExPack_Valheim") != "5.4.2333":
        fail("BepInExPack dependency must be pinned to 5.4.2333", errors)
    if dependencies.get("ValheimModding-Jotunn") != "2.29.2":
        fail("Jotunn dependency must be pinned to 2.29.2", errors)

    categories = publish.get("categories", {}).get("valheim", [])
    for category in ("mods", "client-side", "utility", "ai-generated"):
        if category not in categories:
            fail(f"Missing Valheim publish category: {category}", errors)

    if publish.get("communities") != ["valheim"]:
        fail("Publish communities must be exactly ['valheim']", errors)

    try:
        csproj_version = read_csproj_version()
    except Exception as exc:
        fail(str(exc), errors)
        csproj_version = ""

    toml_version = package.get("versionNumber", "")
    plugin_text = PLUGIN.read_text(encoding="utf-8")
    match = re.search(r'PluginVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"', plugin_text)
    plugin_version = match.group(1) if match else ""

    versions = {
        "csproj": csproj_version,
        "thunderstore.toml": toml_version,
        "Plugin.cs": plugin_version,
    }
    if len(set(versions.values())) != 1 or not all(versions.values()):
        fail(f"Version mismatch: {versions}", errors)

    if args.expected_version and any(value != args.expected_version for value in versions.values()):
        fail(f"Expected version {args.expected_version}, found {versions}", errors)

    copy_entries = build.get("copy", [])
    targets = {entry.get("target") for entry in copy_entries}
    expected_targets = {
        "plugins/TheConcernedCat.ConcernedCartographer.dll",
        "CHANGELOG.md",
        "LICENSE",
    }
    if not expected_targets.issubset(targets):
        fail(f"Missing build.copy target(s): {sorted(expected_targets - targets)}", errors)

    if args.require_binary and not BINARY.is_file():
        fail(f"Release binary is missing: {BINARY.relative_to(ROOT)}", errors)

    prohibited = []
    for path in ROOT.rglob("*.dll"):
        if path == BINARY and args.require_binary:
            continue
        # Any checked-in/source-tree DLL is suspicious; bin/obj are ignored and only local.
        if "bin" not in path.parts and "obj" not in path.parts:
            prohibited.append(path.relative_to(ROOT))
    if prohibited:
        fail(f"Prohibited DLL(s) found outside bin/obj: {prohibited}", errors)

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print("Repository validation passed.")
    print(f"Package identity: {package['namespace']}-{package['name']}-{package['versionNumber']}")
    print(f"Icon: {width}x{height}; description: {len(description)} characters")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
