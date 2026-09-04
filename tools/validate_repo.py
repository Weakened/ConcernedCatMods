#!/usr/bin/env python3
"""Static repository and Thunderstore package-source validation.

Validates every product in the monorepo (Concerned Cartographer and
Concerned Teamster) on every run. This intentionally does not require
Valheim or its licensed assemblies.

``--product`` scopes only the binary/version flags (``--require-binary``,
``--expected-version``) and defaults to ``cartographer`` so historical
invocations keep their exact meaning; static validation always covers all
products.
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

PRODUCTS: dict[str, dict[str, object]] = {
    "cartographer": {
        "display": "Concerned Cartographer",
        "project_dir": ROOT / "src" / "ConcernedCartographer",
        "csproj": "ConcernedCartographer.csproj",
        "package_name": "ConcernedCartographer",
        "dll_name": "TheConcernedCat.ConcernedCartographer.dll",
    },
    "teamster": {
        "display": "Concerned Teamster",
        "project_dir": ROOT / "src" / "ConcernedTeamster",
        "csproj": "ConcernedTeamster.csproj",
        "package_name": "ConcernedTeamster",
        "dll_name": "TheConcernedCat.ConcernedTeamster.dll",
    },
}

EXPECTED_NAMESPACE = "TheConcernedCat"
EXPECTED_WEBSITE = "https://github.com/Weakened/ConcernedCatMods"
EXPECTED_DEPENDENCIES = {
    "denikson-BepInExPack_Valheim": "5.4.2333",
    "ValheimModding-Jotunn": "2.29.2",
}
EXPECTED_CATEGORIES = ("mods", "client-side", "utility", "ai-generated")


def fail(message: str, errors: list[str]) -> None:
    errors.append(message)


def png_dimensions(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise ValueError("not a valid PNG with an IHDR header")
    return struct.unpack(">II", data[16:24])


def read_csproj_version(csproj: Path) -> str:
    tree = ET.parse(csproj)
    root = tree.getroot()
    node = root.find(".//Version")
    if node is None or not node.text:
        raise ValueError(f"<Version> is missing from {csproj.relative_to(ROOT)}")
    return node.text.strip()


def validate_product(key: str, errors: list[str], require_binary: bool,
                     expected_version: str | None) -> list[str]:
    """Runs every static check for one product; returns its report lines."""
    spec = PRODUCTS[key]
    project_dir: Path = spec["project_dir"]  # type: ignore[assignment]
    package = project_dir / "Package"
    csproj = project_dir / str(spec["csproj"])
    plugin = project_dir / "Plugin.cs"
    dll_name = str(spec["dll_name"])
    binary = project_dir / "bin" / "Release" / "net48" / dll_name
    prefix = f"[{key}]"

    required = [
        csproj,
        plugin,
        package / "thunderstore.toml",
        package / "README.md",
        package / "CHANGELOG.md",
        package / "icon.png",
    ]
    missing = [path for path in required if not path.is_file()]
    for path in missing:
        fail(f"{prefix} Missing required file: {path.relative_to(ROOT)}", errors)
    if missing:
        return []

    width = height = 0
    try:
        width, height = png_dimensions(package / "icon.png")
        if (width, height) != (256, 256):
            fail(f"{prefix} icon.png must be 256x256, found {width}x{height}", errors)
    except Exception as exc:
        fail(f"{prefix} Could not validate icon.png: {exc}", errors)

    try:
        config = tomllib.loads((package / "thunderstore.toml").read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"{prefix} Invalid thunderstore.toml: {exc}", errors)
        config = {}

    package_table = config.get("package", {})
    build = config.get("build", {})
    publish = config.get("publish", {})
    dependencies = package_table.get("dependencies", {})

    expected_identity = {
        "namespace": EXPECTED_NAMESPACE,
        "name": str(spec["package_name"]),
        "websiteUrl": EXPECTED_WEBSITE,
    }
    for toml_key, expected in expected_identity.items():
        if package_table.get(toml_key) != expected:
            fail(f"{prefix} package.{toml_key} must be {expected!r}", errors)

    description = package_table.get("description", "")
    if not description or len(description) > 250:
        fail(f"{prefix} Thunderstore description must be 1-250 characters", errors)

    for dependency, pin in EXPECTED_DEPENDENCIES.items():
        if dependencies.get(dependency) != pin:
            fail(f"{prefix} {dependency} dependency must be pinned to {pin}", errors)

    categories = publish.get("categories", {}).get("valheim", [])
    for category in EXPECTED_CATEGORIES:
        if category not in categories:
            fail(f"{prefix} Missing Valheim publish category: {category}", errors)

    if publish.get("communities") != ["valheim"]:
        fail(f"{prefix} Publish communities must be exactly ['valheim']", errors)

    try:
        csproj_version = read_csproj_version(csproj)
    except Exception as exc:
        fail(f"{prefix} {exc}", errors)
        csproj_version = ""

    toml_version = package_table.get("versionNumber", "")
    plugin_text = plugin.read_text(encoding="utf-8")
    match = re.search(r'PluginVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"', plugin_text)
    plugin_version = match.group(1) if match else ""

    versions = {
        "csproj": csproj_version,
        "thunderstore.toml": toml_version,
        "Plugin.cs": plugin_version,
    }
    if len(set(versions.values())) != 1 or not all(versions.values()):
        fail(f"{prefix} Version mismatch: {versions}", errors)

    if expected_version and any(value != expected_version for value in versions.values()):
        fail(f"{prefix} Expected version {expected_version}, found {versions}", errors)

    copy_entries = build.get("copy", [])
    targets = {entry.get("target") for entry in copy_entries}
    expected_targets = {
        f"plugins/{dll_name}",
        "CHANGELOG.md",
        "LICENSE",
    }
    if not expected_targets.issubset(targets):
        fail(f"{prefix} Missing build.copy target(s): {sorted(expected_targets - targets)}", errors)

    # Exactly one DLL may ship: the product's own plugin. A second DLL in the
    # copy list would smuggle a foreign or cross-product binary into the ZIP.
    for entry in copy_entries:
        target = str(entry.get("target", ""))
        source = str(entry.get("source", ""))
        for value in (target, source):
            if value.lower().endswith(".dll") and not value.endswith(dll_name):
                fail(f"{prefix} build.copy may only ship {dll_name}, found {value!r}", errors)

    if require_binary and not binary.is_file():
        fail(f"{prefix} Release binary is missing: {binary.relative_to(ROOT)}", errors)

    return [
        f"{prefix} Package identity: "
        f"{package_table.get('namespace', '?')}-{package_table.get('name', '?')}-"
        f"{package_table.get('versionNumber', '?')}",
        f"{prefix} Icon: {width}x{height}; description: {len(description)} characters",
    ]


# CT-002 architecture rule: only src/ConcernedTeamster/Adapters/ may name
# Valheim types. The tokens are unambiguous Valheim identifiers; generic
# names (Container, Inventory, Player, Character, Version) are excluded to
# avoid false positives — real coupling to them requires one of the listed
# gateway identifiers or a publicized game reference anyway, and the domain
# layer is additionally proven game-free by compiling into the net10 test
# project without game assemblies.
TEAMSTER_GAME_TOKENS = (
    "Vagon",
    "ZNetView",
    "ZDOID",
    "ZDOVars",
    "ZDO",
    "Humanoid",
    "ItemDrop",
    "ZSFX",
    "Heightmap",
    "ZLog",
    "MessageHud",
    "m_localPlayer",
)


def check_teamster_adapter_isolation(errors: list[str]) -> None:
    """Fails on Valheim identifiers outside Adapters/ (comments included:
    the rule is absolute so the check can stay simple and unarguable)."""
    project_dir: Path = PRODUCTS["teamster"]["project_dir"]  # type: ignore[assignment]
    pattern = re.compile(
        r"global::Version|\b(?:" + "|".join(TEAMSTER_GAME_TOKENS) + r")\b")
    for path in sorted(project_dir.rglob("*.cs")):
        parts = path.relative_to(project_dir).parts
        if parts[0] in ("obj", "bin", "Adapters"):
            continue
        for number, line in enumerate(
                path.read_text(encoding="utf-8").splitlines(), start=1):
            match = pattern.search(line)
            if match:
                fail(
                    f"[teamster] Valheim identifier {match.group(0)!r} outside "
                    f"Adapters/: {path.relative_to(ROOT)}:{number}", errors)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--product", choices=[*PRODUCTS.keys(), "all"], default="cartographer",
        help="Which product --require-binary/--expected-version apply to "
             "(static validation always covers all products).")
    parser.add_argument("--require-binary", action="store_true")
    parser.add_argument("--expected-version")
    args = parser.parse_args()

    scoped = list(PRODUCTS.keys()) if args.product == "all" else [args.product]
    errors: list[str] = []

    required_root_files = [
        ROOT / "README.md",
        ROOT / "LICENSE",
        ROOT / "AGENTS.md",
        ROOT / "CLAUDE.md",
        ROOT / "Environment.props.example",
        ROOT / "DoPrebuild.props",
    ]
    for path in required_root_files:
        if not path.is_file():
            fail(f"Missing required file: {path.relative_to(ROOT)}", errors)

    report: list[str] = []
    for key in PRODUCTS:
        report.extend(validate_product(
            key,
            errors,
            require_binary=args.require_binary and key in scoped,
            expected_version=args.expected_version if key in scoped else None,
        ))

    check_teamster_adapter_isolation(errors)

    prohibited = []
    for path in ROOT.rglob("*.dll"):
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
    for line in report:
        print(line)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
