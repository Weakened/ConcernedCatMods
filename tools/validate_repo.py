#!/usr/bin/env python3
"""Static repository and Thunderstore package-source validation.

Validates every product in the monorepo (Concerned Cartographer, Concerned
Teamster, Concerned Foreman and Concerned Steward) on every run. This intentionally does not require
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

QUOTE = chr(34)

# The marker extension, mirrored from MarkerFile.Extension. A file with this
# suffix is this build's own bookkeeping and belongs under "state".
MARKER_EXTENSION = ".dat"

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
    "foreman": {
        "display": "Concerned Foreman",
        "project_dir": ROOT / "src" / "ConcernedForeman",
        "csproj": "ConcernedForeman.csproj",
        "package_name": "ConcernedForeman",
        "dll_name": "TheConcernedCat.ConcernedForeman.dll",
    },
    "steward": {
        "display": "Concerned Steward",
        "project_dir": ROOT / "src" / "ConcernedSteward",
        "csproj": "ConcernedSteward.csproj",
        "package_name": "ConcernedSteward",
        "dll_name": "TheConcernedCat.ConcernedSteward.dll",
    },
}

# CNPC-000 (#371): a LIBRARY is a second kind of shipped package, and the
# difference from a product is the whole point of the category.
#
# A product is a mod a player installs for what it does. Products must never
# reference each other, because the day one does, two release cadences become
# one (CT-021, check_cross_product_independence below).
#
# A library ships no gameplay. It exists so that several products can share one
# runtime AND one release cadence for that runtime: a compatible fix to it
# reaches every product without any of them being rebuilt, which source sharing
# under src/Shared can never do. That is why a library may be referenced, and
# why the rules that make the reference safe are enforced here rather than left
# to whoever edits a csproj next (check_library_consumers).
#
# Everything a product must have, a library must have too: the four package
# files, a 256x256 icon, the version agreeing in three places, and exactly one
# DLL in its ZIP.
LIBRARIES: dict[str, dict[str, object]] = {
    "concernednpc": {
        "display": "Concerned NPC",
        "project_dir": ROOT / "src" / "ConcernedNPC",
        "csproj": "ConcernedNPC.csproj",
        "package_name": "ConcernedNPC",
        "dll_name": "TheConcernedCat.ConcernedNPC.dll",
        "plugin_guid": "com.theconcernedcat.valheim.concernednpc",
        "namespace": "ConcernedNPC",
    },
}

# Both kinds are validated identically as packages; only the relationship rules
# differ.
PACKAGES: dict[str, dict[str, object]] = {**PRODUCTS, **LIBRARIES}

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
    """Runs every static check for one package; returns its report lines.

    Products and libraries are checked by the same rules: the four package
    files, a 256x256 icon, one version in three places, one DLL in the ZIP.
    """
    spec = PACKAGES[key]
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

    if expected_version:
        changelog_text = (package / "CHANGELOG.md").read_text(encoding="utf-8")
        if not re.search(rf"^##\s+{re.escape(expected_version)}\b", changelog_text, re.MULTILINE):
            fail(f"{prefix} CHANGELOG.md has no '## {expected_version}' section", errors)

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


# CT-021: products must never reference each other at compile time; the v0.5
# integration is a runtime capability probe over string member names. Each
# entry scans one product's project tree for compile-time coupling onto the
# other product: csproj Project/Package/assembly references and C# `using`
# directives or InternalsVisibleTo grants naming the other product's root
# namespace. String literals (the reflective contract) are allowed by design.
CROSS_PRODUCT_RULES: tuple[tuple[str, str, str], ...] = (
    ("teamster", "src/ConcernedTeamster", "ConcernedCartographer"),
    ("teamster", "src/ConcernedTeamster.Tests", "ConcernedCartographer"),
    ("teamster", "src/ConcernedTeamster", "ConcernedForeman"),
    ("teamster", "src/ConcernedTeamster.Tests", "ConcernedForeman"),
    ("cartographer", "src/ConcernedCartographer", "ConcernedTeamster"),
    ("cartographer", "src/ConcernedCartographer.Tests", "ConcernedTeamster"),
    ("cartographer", "src/ConcernedCartographer", "ConcernedForeman"),
    ("cartographer", "src/ConcernedCartographer.Tests", "ConcernedForeman"),
    # CF-SET-002: Foreman is a third independent product and inherits the same
    # rule in both directions. Its only shared code is source-linked from
    # src/Shared, which belongs to no product.
    ("foreman", "src/ConcernedForeman", "ConcernedCartographer"),
    ("foreman", "src/ConcernedForeman", "ConcernedTeamster"),
    # CC-SET-002 (#317): src/Interop.Tests is the one tree allowed to hold both
    # halves of a cross-product capability, and only as two separately compiled
    # harness assemblies that meet through the BCL capability map
    # (docs/settlement/cart-and-collection/CONTRACTS.md §9). The provider harness
    # may compile Teamster's haul sources; the consumer harness compiles only
    # src/Shared. Neither may pull in the other side or a third product, or the
    # test would stop proving that separately built products agree.
    ("interop", "src/Interop.Tests", "ConcernedForeman"),
    ("interop", "src/Interop.Tests", "ConcernedCartographer"),
    ("interop", "src/Interop.Tests/Provider", "ConcernedForeman"),
    ("interop", "src/Interop.Tests/Provider", "ConcernedCartographer"),
    ("interop", "src/Interop.Tests/Consumer", "ConcernedTeamster"),
    ("interop", "src/Interop.Tests/Consumer", "ConcernedForeman"),
    ("interop", "src/Interop.Tests/Consumer", "ConcernedCartographer"),
    # CS-001 (#340): the Steward is a fourth independent product and inherits
    # the same rule in both directions. Its only shared code is source-linked
    # from src/Shared, which belongs to no product; it finds a hauler at
    # runtime through a GUID string and the BCL capability map, never a
    # reference.
    ("steward", "src/ConcernedSteward", "ConcernedCartographer"),
    ("steward", "src/ConcernedSteward", "ConcernedTeamster"),
    ("steward", "src/ConcernedSteward", "ConcernedForeman"),
    ("steward", "src/ConcernedSteward.Tests", "ConcernedCartographer"),
    ("steward", "src/ConcernedSteward.Tests", "ConcernedTeamster"),
    ("steward", "src/ConcernedSteward.Tests", "ConcernedForeman"),
    ("teamster", "src/ConcernedTeamster", "ConcernedSteward"),
    ("teamster", "src/ConcernedTeamster.Tests", "ConcernedSteward"),
    ("cartographer", "src/ConcernedCartographer", "ConcernedSteward"),
    ("cartographer", "src/ConcernedCartographer.Tests", "ConcernedSteward"),
    ("foreman", "src/ConcernedForeman", "ConcernedSteward"),
    ("foreman", "src/ConcernedForeman.Tests", "ConcernedSteward"),
    ("interop", "src/Interop.Tests", "ConcernedSteward"),
    ("interop", "src/Interop.Tests/Provider", "ConcernedSteward"),
    ("interop", "src/Interop.Tests/Consumer", "ConcernedSteward"),
)


def check_cross_product_independence(errors: list[str]) -> list[str]:
    """Fails on any compile-time reference between any two products.

    Csproj side: Compile (source-linking, the repo's own sharing idiom),
    ProjectReference, Reference (Include AND child text, so a HintPath under
    an innocuous Include is caught), and PackageReference. C# side: plain,
    static, alias, and global `using` directives plus InternalsVisibleTo.
    String literals (the CT-021 reflective contract) are allowed by design.
    """
    using_pattern_by_target = {
        target: re.compile(
            r"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?"
            r"TheConcernedCat\." + target + r"\b|"
            r"InternalsVisibleTo\(\s*\"TheConcernedCat\." + target + r"\b")
        for target in {
            "ConcernedCartographer", "ConcernedTeamster", "ConcernedForeman", "ConcernedSteward"}
    }
    checked_projects = 0
    for owner, project_rel, target in CROSS_PRODUCT_RULES:
        project_dir = ROOT / Path(project_rel)
        if not project_dir.is_dir():
            fail(f"[{owner}] Cross-product audit: missing directory {project_rel}", errors)
            continue
        checked_projects += 1

        for csproj in sorted(project_dir.glob("*.csproj")):
            try:
                tree = ET.parse(csproj)
            except Exception as exc:
                fail(f"[{owner}] Cross-product audit could not parse {csproj.name}: {exc}", errors)
                continue
            for node in tree.getroot().iter():
                tag = node.tag.rsplit("}", 1)[-1]
                if tag not in ("Compile", "ProjectReference", "Reference", "PackageReference"):
                    continue
                include = node.attrib.get("Include", "")
                inner_text = "".join(node.itertext())
                if target in include or target in inner_text:
                    fail(
                        f"[{owner}] Forbidden compile-time reference to {target!r} "
                        f"in {csproj.relative_to(ROOT)}: <{tag} Include=\"{include}\">", errors)

        pattern = using_pattern_by_target[target]
        for path in sorted(project_dir.rglob("*.cs")):
            parts = path.relative_to(project_dir).parts
            if parts[0] in ("obj", "bin"):
                continue
            for number, line in enumerate(
                    path.read_text(encoding="utf-8-sig").splitlines(), start=1):
                if pattern.search(line):
                    fail(
                        f"[{owner}] Forbidden compile-time coupling onto {target}: "
                        f"{path.relative_to(ROOT)}:{number}", errors)

    return [
        f"[interop] Cross-product independence: {checked_projects} project trees audited, "
        "no compile-time reference in either direction",
    ]


def check_every_product_pair_is_audited(errors: list[str]) -> list[str]:
    """Fails if a product pair is missing from CROSS_PRODUCT_RULES.

    CROSS_PRODUCT_RULES is written by hand, so a new product added to PRODUCTS
    is exempt from the independence scan until someone remembers to add its
    pairs - silently, and with the validator green. This closes that: every
    ordered pair of distinct products must be covered for the product's own
    source directory.
    """
    covered = {(owner, target) for owner, project_rel, target in CROSS_PRODUCT_RULES
               if project_rel == f"src/{Path(project_rel).name}"
               and Path(project_rel).name == str(PRODUCTS.get(owner, {}).get("package_name", ""))}
    pairs = 0
    for owner, owner_spec in PRODUCTS.items():
        for target, target_spec in PRODUCTS.items():
            if owner == target:
                continue
            pairs += 1
            if (owner, str(target_spec["package_name"])) not in covered:
                fail(
                    f"[interop] Cross-product audit does not cover {owner} -> "
                    f"{target_spec['package_name']}: add "
                    f'("{owner}", "src/{owner_spec["package_name"]}", '
                    f'"{target_spec["package_name"]}") to CROSS_PRODUCT_RULES', errors)
    return [f"[interop] Cross-product audit covers all {pairs} ordered product pairs"]


def check_library_consumers(errors: list[str]) -> list[str]:
    """The rules that make depending on a library package safe.

    A library may be referenced where a product may not, so the reference has
    to carry its own guarantees, and all of them have to be true together:

    1. The library depends on no product. A shared runtime that reaches back
       into one of its consumers is a circular dependency wearing a hat.
    2. A product references it as a ProjectReference with Private false, so the
       library's DLL is NOT copied into the product's output and cannot be
       smuggled into the product's ZIP. The player gets it from its own
       package, once.
    3. A product that references it pins it in thunderstore.toml, so the
       storefront installs it.
    4. A product that references it declares BepInDependency on its plugin
       GUID, so a missing library is a clear dependency failure at load rather
       than an NRE somewhere later.

    Three and four without two would ship it twice; two without three would
    install a mod whose dependency nobody fetches; two without four turns a
    missing package into a mystery. So it is all four or none, and a stale pin
    or dependency left behind after a reference is removed fails too.
    """
    report: list[str] = []
    for lib_key, lib_spec in LIBRARIES.items():
        lib_dir: Path = lib_spec["project_dir"]  # type: ignore[assignment]
        lib_name = str(lib_spec["package_name"])
        lib_guid = str(lib_spec["plugin_guid"])
        if not lib_dir.is_dir():
            fail(f"[{lib_key}] Library directory is missing: src/{lib_name}", errors)
            continue

        # 1. The library must not reference any product, in either idiom.
        product_names = {str(spec["package_name"]) for spec in PRODUCTS.values()}
        for csproj in sorted(lib_dir.glob("*.csproj")):
            try:
                tree = ET.parse(csproj)
            except Exception as exc:
                fail(f"[{lib_key}] Could not parse {csproj.name}: {exc}", errors)
                continue
            for node in tree.getroot().iter():
                tag = node.tag.rsplit("}", 1)[-1]
                if tag not in ("Compile", "ProjectReference", "Reference", "PackageReference"):
                    continue
                text = node.attrib.get("Include", "") + "".join(node.itertext())
                for product_name in product_names:
                    if product_name in text:
                        fail(
                            f"[{lib_key}] A library may not reference a product: "
                            f"{product_name} in {csproj.relative_to(ROOT)}", errors)
        product_using = re.compile(
            r"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?TheConcernedCat\."
            r"(" + "|".join(sorted(product_names)) + r")\b")
        for path in sorted(lib_dir.rglob("*.cs")):
            if path.relative_to(lib_dir).parts[0] in ("obj", "bin"):
                continue
            for number, line in enumerate(
                    path.read_text(encoding="utf-8-sig").splitlines(), start=1):
                if product_using.search(line):
                    fail(
                        f"[{lib_key}] A library may not use a product's namespace: "
                        f"{path.relative_to(ROOT)}:{number}", errors)

        # 2 to 4, for every product, in both directions.
        consumers = 0
        for product_key, product_spec in PRODUCTS.items():
            project_dir: Path = product_spec["project_dir"]  # type: ignore[assignment]
            csproj = project_dir / str(product_spec["csproj"])
            plugin = project_dir / "Plugin.cs"
            toml_path = project_dir / "Package" / "thunderstore.toml"
            if not csproj.is_file():
                continue

            references = []
            try:
                tree = ET.parse(csproj)
            except Exception as exc:
                fail(f"[{product_key}] Could not parse {csproj.name}: {exc}", errors)
                continue
            for node in tree.getroot().iter():
                tag = node.tag.rsplit("}", 1)[-1]
                if tag not in ("ProjectReference", "Reference", "PackageReference", "Compile"):
                    continue
                text = node.attrib.get("Include", "") + "".join(node.itertext())
                if lib_name not in text:
                    continue
                if tag != "ProjectReference":
                    fail(
                        f"[{product_key}] {lib_name} must be a ProjectReference with Private "
                        f"false, not a <{tag}>: {csproj.relative_to(ROOT)}", errors)
                    continue
                references.append(node)

            referenced = bool(references)
            for node in references:
                private = node.attrib.get("Private", "")
                child = node.find("{*}Private")
                if child is not None:
                    private = (child.text or "").strip()
                if private.lower() != "false":
                    fail(
                        f"[{product_key}] The {lib_name} ProjectReference needs "
                        f"<Private>false</Private>, or its DLL is copied into this product's "
                        f"output and can reach its ZIP: {csproj.relative_to(ROOT)}", errors)

            pinned = False
            if toml_path.is_file():
                try:
                    config = tomllib.loads(toml_path.read_text(encoding="utf-8"))
                    dependencies = config.get("package", {}).get("dependencies", {})
                    pinned = f"{EXPECTED_NAMESPACE}-{lib_name}" in dependencies
                except Exception as exc:
                    fail(f"[{product_key}] Invalid thunderstore.toml: {exc}", errors)

            declared = False
            if plugin.is_file():
                declared = f'BepInDependency("{lib_guid}"' in plugin.read_text(encoding="utf-8")

            if referenced:
                consumers += 1
                if not pinned:
                    fail(
                        f"[{product_key}] References {lib_name} but does not pin "
                        f"{EXPECTED_NAMESPACE}-{lib_name} in thunderstore.toml, so the "
                        "storefront would not install it", errors)
                if not declared:
                    fail(
                        f"[{product_key}] References {lib_name} but declares no "
                        f'BepInDependency("{lib_guid}"), so a missing library would be an NRE '
                        "rather than a dependency error", errors)
            else:
                if pinned:
                    fail(
                        f"[{product_key}] Pins {EXPECTED_NAMESPACE}-{lib_name} in "
                        "thunderstore.toml but references nothing from it: a stale pin makes "
                        "players install a package this product does not use", errors)
                if declared:
                    fail(
                        f"[{product_key}] Declares BepInDependency on {lib_guid} but references "
                        "nothing from it: a stale hard dependency refuses to load without a "
                        "package this product does not use", errors)

        report.append(
            f"[{lib_key}] Library package: depends on no product; {consumers} of "
            f"{len(PRODUCTS)} products consume it, each by ProjectReference with Private false, "
            "a Thunderstore pin and a BepInDependency")
    return report


# CNPC-000 (#371): the never-coexist rule - one identity never has a
# presentation body and a worker body at once - is enforced by an arbiter in the
# library, and the arbiter is only the truth if it is the ONLY thing holding an
# identity's mode. Today each worker product constructs its own ActorModeOwner
# from its own compiled copy of the shared source, which is harmless while
# nothing consumes the library and a silent second owner the moment something
# does. A review of the contract surface asked for this rule rather than three
# per-leaf acceptance criteria, because an acceptance criterion is a promise and
# this is a check.
ARBITER_BYPASS = re.compile(r"\bnew\s+ActorModeOwner\s*\(")

# CNPC-000 (#371): the NPC library may own the MECHANISM of writing a file, and
# must never own the FORMAT or the PATH.
#
# That distinction is the program's acceptance criterion. A pre-refactor data
# directory, dropped in unchanged, has to keep working with no migration code
# having run, and that holds only while every durable name, row tag, schema
# number and directory stays with the role that already writes it. An atomic
# write helper handed a path is shared plumbing; a library that composes a path
# or names a schema has become a second author of somebody else's save file,
# and it would look perfectly reasonable in review.
#
# So the file primitives are confined to a named, pinned allow-list - the two
# files that exist to be that plumbing - while composing a path and naming a
# schema are refused everywhere, the allow-list included.
#
# Proposed by the agent that moved the custody ledger, which proved it for its
# own two folders with a test. This is the mechanical backstop for the folders
# nobody has written yet.
# Every File and Directory member except the ones that only ask a question.
#
# The first version of this named the members it knew, and a review found six
# ways past it in a single pass - WriteAllBytes, ReadAllBytes, CreateText,
# OpenWrite, AppendText, AppendAllLines - because a word boundary after "Open"
# does not match "OpenWrite". A deny-list over a namespace somebody else owns is
# the wrong shape: it is only ever as complete as the last person to think about
# it. Name what may be called instead.
LIBRARY_FILE_APIS = re.compile(
    r"\b(?:File|Directory)\.(?!Exists\b)\w+"
    r"|\bnew\s+Stream(?:Writer|Reader)\b"
    r"|\bnew\s+File(?:Stream|Info)\b")

# Refused everywhere in the library, the allow-list included: a path the library
# composes is a directory it has chosen, and a schema it names is a format it
# has taken ownership of.
# Choosing where data lives, and naming what shape it is in. Refused
# everywhere, the plumbing included: a library that picks a directory or names a
# schema has taken ownership of somebody else's save file.
LIBRARY_FORMAT_OWNERSHIP = re.compile(
    r"\bPath\.(?:Combine|Join|GetTempPath|GetTempFileName)\b|\bSchemaVersion\b")

# Reading a component of a path somebody else chose. That is not ownership - the
# plumbing has to check that the path it was handed is absolute and names a
# directory before it writes there - but it is one string concatenation away
# from composition, so it is confined to the same two pinned files as the file
# primitives rather than allowed everywhere.
LIBRARY_PATH_INSPECTION = re.compile(r"\bPath\.(?:GetDirectoryName|GetFullPath|GetFileName)\b")

# The only two files that may call a file primitive, and what each is for. A
# third entry is a deliberate edit with a reason, not a convenience.
LIBRARY_PERSISTENCE_PLUMBING = {
    "Persistence/NpcAtomicText.cs": "write to a temporary file then replace, on a path it is given",
    "Persistence/NpcSidecarFile.cs": "read and write one sidecar whose path and format the role owns",
}


def check_the_npc_library_writes_no_file(errors: list[str]) -> list[str]:
    """Fails on a file API or a schema constant inside the NPC library.

    The library holds runtime behaviour, never a format. Roles keep their own
    files, their own row tags and their own schema numbers, because those are
    what an existing player's save is made of.
    """
    library = LIBRARIES.get("concernednpc")
    if library is None:
        return []
    project_dir: Path = library["project_dir"]  # type: ignore[assignment]
    if not project_dir.is_dir():
        return []

    scanned = 0
    seen_plumbing = set()
    for path in sorted(project_dir.rglob("*.cs")):
        relative = path.relative_to(project_dir)
        if relative.parts[0] in ("obj", "bin"):
            continue
        scanned += 1
        key = "/".join(relative.parts)
        is_plumbing = key in LIBRARY_PERSISTENCE_PLUMBING
        if is_plumbing:
            seen_plumbing.add(key)

        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), start=1):
            stripped = line.strip()
            if stripped.startswith("///") or stripped.startswith("//"):
                continue

            owned = LIBRARY_FORMAT_OWNERSHIP.search(line)
            if owned:
                fail(
                    f"[concernednpc] The NPC library may not compose a path or name a schema: "
                    f"{owned.group(0)!r} at {path.relative_to(ROOT)}:{number}. A role owns where its "
                    "data lives and what shape it is in.", errors)

            inspected = LIBRARY_PATH_INSPECTION.search(line)
            if inspected and not is_plumbing:
                fail(
                    f"[concernednpc] Only the named persistence plumbing may take a path apart: "
                    f"{inspected.group(0)!r} at {path.relative_to(ROOT)}:{number}. Reading a path's "
                    "pieces is one concatenation away from choosing where data lives.", errors)

            match = LIBRARY_FILE_APIS.search(line)
            if match and not is_plumbing:
                fail(
                    f"[concernednpc] Only the named persistence plumbing may touch a file: "
                    f"{match.group(0)!r} at {path.relative_to(ROOT)}:{number}. Take a path and hand "
                    "the writing to Persistence/NpcAtomicText.cs.", errors)

    for key in sorted(set(LIBRARY_PERSISTENCE_PLUMBING) - seen_plumbing):
        fail(
            f"[concernednpc] The persistence allow-list names {key}, which does not exist. An "
            "allow-list that outlives its file is an exemption nobody is watching.", errors)

    return [
        f"[concernednpc] Library persistence audit: {scanned} sources; no path composed, no schema "
        f"named, file primitives confined to {len(LIBRARY_PERSISTENCE_PLUMBING)} pinned files",
    ]


# The finish verdict. The first version of this rule matched the literal
# "JobPlanVerdict.NothingToDo" line by line, and an independent reviewer walked
# past it four ways: an alias, a static import, a cast, and a line break after
# the dot. Matching the bare identifier instead is worse, not better - a
# different enum in this same folder has a member of the same name. So the
# qualified spelling is matched across newlines, and every way of avoiding the
# qualified spelling is banned outright.
PLANNING_FINISH_VERDICT = re.compile(r"JobPlanVerdict\s*\.\s*NothingToDo", re.S)

# Reading the verdict is not deciding it: a driver switching on the answer
# consumes the decision rather than making one.
PLANNING_VERDICT_READ = re.compile(
    r"case\s+JobPlanVerdict\s*\.\s*NothingToDo"
    r"|[=!]=\s*JobPlanVerdict\s*\.\s*NothingToDo"
    r"|JobPlanVerdict\s*\.\s*NothingToDo\s*[=!]=", re.S)

# Spellings that would let the verdict reach the compiler unqualified.
PLANNING_VERDICT_INDIRECTION = re.compile(
    r"using\s+\w+\s*=\s*[\w.]*\bJobPlanVerdict\b"
    r"|using\s+static\s+[\w.]*\bJobPlanVerdict\b"
    r"|\(\s*JobPlanVerdict\s*\)")

# Parameters that state a claim rather than a quantity. Zero for
# `leftForAnotherRound` says the plan covered the whole job; an empty `carrying`
# says the NPC holds nothing this job may spend. Neither is checkable inside this
# library, and both were silent once, and both times a job reported itself
# finished with targets untouched. The neighbours are included because a rule
# keyed to exactly two literals is one rename away from silence; this is a
# backstop, and the compiler is the primary enforcement.
PLANNING_CLAIM_PARAMETERS = re.compile(
    r"^(?:left|carry|carrying|carried|remaining|outstanding|covered|leftover)"
    r"|(?:LeftOver|ForAnotherRound|Carried|Carrying|Remaining|Outstanding)$",
    re.I)


def _npc_library_sources(folder=None) -> list[Path]:
    """Sources of the NPC library, or of one folder of it. [] when absent."""
    library = LIBRARIES.get("concernednpc")
    if library is None:
        return []
    project_dir: Path = library["project_dir"]  # type: ignore[assignment]
    root = project_dir if folder is None else project_dir / folder
    if not root.is_dir():
        return []
    return [path for path in sorted(root.rglob("*.cs"))
            if path.relative_to(project_dir).parts[0] not in ("obj", "bin")]


def _npc_code(path: Path) -> str:
    """The file with comments blanked and line count preserved.

    The first version skipped only lines that *started* with a comment marker, so
    a trailing `//` both hid a violation and counted a mention.
    """
    out = []
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        out.append(re.sub(r"/\*.*?\*/", "", _strip_cs_line_comment(raw)))
    return "\n".join(out)


def _line_of(code: str, index: int) -> int:
    return code.count("\n", 0, index) + 1


def _parameter_defaults(code: str):
    """(line, parameter name) for each default value, found by scanning.

    A default is an `=` inside parentheses. String and character literals are
    skipped, `==`/`!=`/`<=`/`>=`/`=>` are not defaults, and attribute arguments
    are stepped over because their named properties also use `=`. Because the
    scan tracks parenthesis depth across the whole file, it cannot be evaded by
    where the newlines fall - which is how a one-line expression-bodied member
    and a split parameter list both got past the first version of this rule.
    """
    depth = 0
    index = 0
    length = len(code)
    while index < length:
        char = code[index]
        if char in "\"'":
            quote = char
            index += 1
            while index < length and code[index] != quote:
                index += 2 if code[index] == "\\" else 1
            index += 1
            continue
        if char == "(":
            depth += 1
        elif char == ")":
            depth = max(0, depth - 1)
        elif char == "[":
            while index < length and code[index] != "]":
                index += 1
        elif char == "=" and depth > 0:
            before = code[index - 1] if index else " "
            after = code[index + 1] if index + 1 < length else " "
            if before not in "=!<>" and after not in "=>":
                end = index
                while end > 0 and code[end - 1].isspace():
                    end -= 1
                start = end
                while start > 0 and (code[start - 1].isalnum() or code[start - 1] == "_"):
                    start -= 1
                yield _line_of(code, index), code[start:end]
        index += 1


def check_npc_planning_decides_nothing_to_do_once(errors: list[str]) -> list[str]:
    """Fails unless exactly one place in the library decides a job is finished.

    NothingToDo is the one verdict a job may be reported finished on without
    doing anything, and it has had three separate ways in - a conclusive empty
    snapshot, a trip whose stops were all dropped, and a first trip the chest cap
    emptied. Every one was a blocker, because every one was another place that
    could close a job with its targets untouched.

    Scoped to the whole library rather than to Planning/, because the job driver
    lives in Jobs/ and could otherwise answer the verdict freely.
    """
    sources = _npc_library_sources()
    if not sources:
        fail(
            "[concernednpc] The NPC library is missing, so the verdict audit checked nothing. "
            "Point this rule at its new home rather than leaving it green over an empty set.",
            errors)
        return []

    sites = []
    for path in sources:
        code = _npc_code(path)
        for indirect in PLANNING_VERDICT_INDIRECTION.finditer(code):
            fail(
                f"[concernednpc] The finish verdict may not be reached under another name: "
                f"{indirect.group(0).strip()!r} at "
                f"{path.relative_to(ROOT)}:{_line_of(code, indirect.start())}. An alias, a static "
                "import or a cast puts a second decision site past this audit.", errors)
        reads = {match.end() for match in PLANNING_VERDICT_READ.finditer(code)}
        for match in PLANNING_FINISH_VERDICT.finditer(code):
            if match.end() in reads:
                continue
            sites.append(f"{path.relative_to(ROOT)}:{_line_of(code, match.start())}")

    if len(sites) != 1:
        fail(
            f"[concernednpc] A job may be reported finished on the NothingToDo verdict and on "
            f"nothing else, so it is decided once: expected 1 site, found {len(sites)} "
            f"({', '.join(sites) if sites else 'none'}). Each extra one is another way to close a "
            "job with work still to do.", errors)

    return [
        f"[concernednpc] Planning verdict audit: {len(sources)} library sources; the finish verdict "
        f"is decided at {len(sites)} site, reachable under no other name",
    ]


def check_npc_planning_never_defaults_a_claim(errors: list[str]) -> list[str]:
    """Fails on a default value for a parameter that states a claim.

    A call site that stays silent about what the plan left out, or about what the
    NPC is already carrying, is not omitting a detail - it is asserting something
    it was never asked. Both were silent once and both produced the same failure.

    The sites are found by scanning parentheses rather than by line shape,
    because a reviewer got a default past the first version of this rule on a
    one-line expression-bodied member, whose line ends in a semicolon, and again
    by splitting the default across two lines.
    """
    sources = _npc_library_sources("Planning")
    if not sources:
        fail(
            "[concernednpc] The planning folder is missing, so the defaulted-claim audit checked "
            "nothing. Point this rule at the pipeline's new home rather than leaving it green "
            "over an empty set.", errors)
        return []

    checked = 0
    for path in sources:
        for number, name in _parameter_defaults(_npc_code(path)):
            checked += 1
            if name and PLANNING_CLAIM_PARAMETERS.search(name):
                fail(
                    f"[concernednpc] {name!r} states a claim, so it may not carry a default "
                    f"({path.relative_to(ROOT)}:{number}). Nothing in this library can check it, "
                    "and a caller that stays silent asserts it by accident.", errors)

    return [
        f"[concernednpc] Planning claim audit: {len(sources)} sources, {checked} parameter defaults "
        f"scanned, none of them a claim",
    ]



def check_library_consumers_do_not_bypass_the_arbiter(errors: list[str]) -> list[str]:
    """Once a product consumes the NPC library, its modes come from the arbiter.

    Scoped to consumers on purpose: a product that has not adopted the library
    still owns its own mode owner, and saying otherwise would fail the build for
    code that is correct today.
    """
    library = LIBRARIES.get("concernednpc")
    if library is None:
        return []
    lib_name = str(library["package_name"])

    checked = 0
    consumers = 0
    for product_key, product_spec in PRODUCTS.items():
        project_dir: Path = product_spec["project_dir"]  # type: ignore[assignment]
        csproj = project_dir / str(product_spec["csproj"])
        if not csproj.is_file():
            continue
        checked += 1
        if lib_name not in csproj.read_text(encoding="utf-8-sig"):
            continue
        consumers += 1
        for path in sorted(project_dir.rglob("*.cs")):
            if path.relative_to(project_dir).parts[0] in ("obj", "bin"):
                continue
            for number, line in enumerate(
                    path.read_text(encoding="utf-8-sig").splitlines(), start=1):
                if ARBITER_BYPASS.search(line):
                    fail(
                        f"[{product_key}] Consumes {lib_name} but builds its own "
                        f"ActorModeOwner: {path.relative_to(ROOT)}:{number}. One identity would "
                        "have two mode owners, and the never-coexist rule would be advice rather "
                        "than enforcement. Take the mode from the arbiter.", errors)

    return [
        f"[interop] Arbiter bypass audit: {checked} products checked, {consumers} consume "
        f"{lib_name}, none builds its own mode owner",
    ]


# CT-021: Teamster reads these exact Cartographer members reflectively at
# runtime (docs/mods/concerned-teamster/CARTOGRAPHER_CONTRACT.md, mirrored in
# Domain/Cartographer/CartographerContract.cs). Both products live in this
# monorepo, so the contract is statically cross-checked here: renaming a
# member below must fail validation and force a coordinated update (contract
# class, contract document, version floor decision) instead of silently
# breaking the shipped integration for users.
# Each pattern pins the member KIND as well as its name: fields must be
# followed by "=" or ";", properties by "{" (possibly on the next line — the
# search runs over whole-file text and \s spans newlines). A property→field
# refactor that keeps the name would break the runtime probe, so it must
# break this tripwire too.
TEAMSTER_CARTOGRAPHER_CONTRACT: tuple[tuple[str, str], ...] = (
    ("src/ConcernedCartographer/Plugin.cs",
     r"private\s+CartographerRuntime\?\s+_runtime\s*[=;]"),
    ("src/ConcernedCartographer/Runtime/CartographerRuntime.cs",
     r"private\s+RouteStore\s+_routeStore\s*[=;]"),
    ("src/ConcernedCartographer/Domain/Atlas/RouteStore.cs",
     r"public\s+IEnumerable<AtlasRoute>\s+Living\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/RouteStore.cs",
     r"public\s+long\s+ChangeStamp\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/AtlasRoute.cs",
     r"public\s+AtlasId\s+Id\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/AtlasRoute.cs",
     r"public\s+string\s+Name\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/AtlasRoute.cs",
     r"public\s+bool\s+Archived\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/AtlasRoute.cs",
     r"public\s+List<RoadPoint>\s+Points\s*\{"),
    ("src/ConcernedCartographer/Domain/Atlas/AtlasId.cs",
     r"public\s+Guid\s+Value\s*\{"),
    ("src/ConcernedCartographer/Domain/RoadPoint.cs",
     r"public\s+float\s+X\s*\{"),
    ("src/ConcernedCartographer/Domain/RoadPoint.cs",
     r"public\s+float\s+Y\s*\{"),
    ("src/ConcernedCartographer/Domain/RoadPoint.cs",
     r"public\s+float\s+Z\s*\{"),
)


def check_teamster_cartographer_contract(errors: list[str]) -> list[str]:
    """Fails when a Cartographer member of Teamster's CT-021 runtime read
    contract no longer appears in the Cartographer sources."""
    present = 0
    for rel_path, pattern in TEAMSTER_CARTOGRAPHER_CONTRACT:
        path = ROOT / Path(rel_path)
        if not path.is_file():
            fail(
                f"[interop] CT-021 contract file missing: {rel_path} — update "
                "docs/mods/concerned-teamster/CARTOGRAPHER_CONTRACT.md and "
                "Domain/Cartographer/CartographerContract.cs together with this change",
                errors)
            continue
        if re.search(pattern, path.read_text(encoding="utf-8")):
            present += 1
        else:
            fail(
                f"[interop] CT-021 contract member no longer matches {pattern!r} in "
                f"{rel_path} — Teamster reads this member reflectively at runtime; "
                "update docs/mods/concerned-teamster/CARTOGRAPHER_CONTRACT.md, "
                "Domain/Cartographer/CartographerContract.cs, and the version floor "
                "decision together with the Cartographer change", errors)

    total = len(TEAMSTER_CARTOGRAPHER_CONTRACT)
    return [f"[interop] CT-021 Cartographer contract: {present}/{total} members present at source level"]


# CT-024: the whole Cartographer integration path (CT-021..CT-024) is
# read-only by contract — Teamster reflects into Cartographer only through
# GetField/GetProperty/GetValue reads. Any token that could mutate state or
# invoke behavior appearing in these files must fail validation and force a
# conscious, reviewed design change; it would break the no-atlas-mutation
# promise. Comments count too: the rule is absolute so the check stays
# simple and unarguable (same stance as the adapter-isolation scan).
TEAMSTER_INTEGRATION_MUTATION_TOKENS = (
    "SetValue",
    "SetField",
    ".Invoke(",
    "GetMethod(",
    "GetMethods(",
    "InvokeMember",
    "Activator.CreateInstance",
    "CreateDelegate",
)


def check_teamster_integration_readonly(errors: list[str]) -> list[str]:
    """Fails on mutating/invoking reflection anywhere in the integration path."""
    domain_files = sorted((ROOT / "src" / "ConcernedTeamster" / "Domain" / "Cartographer").glob("*.cs"))
    if len(domain_files) < 8:
        fail(
            "[interop] CT-024 read-only audit: expected at least 8 files in "
            f"src/ConcernedTeamster/Domain/Cartographer, found {len(domain_files)} — the audit "
            "no longer covers the integration path (was the directory moved?)", errors)
    paths = list(domain_files)
    paths.append(ROOT / "src" / "ConcernedTeamster" / "Adapters" / "CartographerCapability.cs")
    checked = 0
    for path in paths:
        if not path.is_file():
            fail(f"[interop] CT-024 read-only audit: expected file missing: {path.relative_to(ROOT)}", errors)
            continue
        checked += 1
        for number, line in enumerate(
                path.read_text(encoding="utf-8").splitlines(), start=1):
            for token in TEAMSTER_INTEGRATION_MUTATION_TOKENS:
                if token in line:
                    fail(
                        f"[interop] CT-024 read-only audit: mutating/invoking reflection token "
                        f"{token!r} in {path.relative_to(ROOT)}:{number} — the Cartographer "
                        "integration must stay read-only (no atlas mutation)", errors)

    return [f"[interop] CT-024 read-only audit: {checked} integration files free of mutating reflection"]


# CT-026: the multiplayer authority policy document must list every
# TeamsterFeature the enum defines, and the source must contain no
# outbound-network or ownership-takeover call — Teamster is client-side,
# read-only toward the game, and sends nothing, so an unmodded peer's
# experience is provably unaltered. Both are tripwires: a new feature or an
# accidental network/ownership call fails the build.
TEAMSTER_NETWORK_OWNERSHIP_TOKENS = (
    "InvokeRPC",
    "ZRoutedRpc",
    "RegisterRPC",
    "SetOwner",
    "ClaimOwnership",
    "GetZDO().Set",
    "ZDO.Set",
    "m_nview.InvokeRPC",
)

# #313 (CT-NPC-002): the one scoped allowance of the CT-026 audit. Gunnar's
# opt-in worker runtime writes his identity into his OWN worker body's network
# object (docs/settlement/cart-and-collection/DECISIONS.md D9). In
# src/ConcernedTeamster/Adapters/Workers/ only, a network-object write line is
# allowed when every `.Set(` call on it takes a "tcc.worker." literal key. Every
# other CT-026 token (RPC, ownership) stays absolute there too, and the
# worker-runtime scope audit below fails any other key in that folder.
TEAMSTER_WORKERS_DIR = ("Adapters", "Workers")
TEAMSTER_WORKER_IDENTITY_WRITE_TOKENS = ("GetZDO().Set", "ZDO.Set")
TEAMSTER_WORKER_SET_CALL = re.compile(r"\.Set\s*\(")
TEAMSTER_WORKER_IDENTITY_SET = re.compile(r"\.Set\s*\(\s*\"tcc\.worker\.[a-z0-9][a-z0-9.\-]*\"\s*,")


def _is_worker_identity_write(code: str) -> bool:
    """True when the (comment-stripped) line has at least one `.Set(` call and
    every one of them writes a "tcc.worker." literal key."""
    calls = TEAMSTER_WORKER_SET_CALL.findall(code)
    return bool(calls) and len(calls) == len(TEAMSTER_WORKER_IDENTITY_SET.findall(code))

# CT-041: the beta privacy audit — Teamster has no telemetry and phones
# nothing home, so no internet-egress-capable API may appear anywhere in
# its source. This is a different concern from the game-network/ownership
# tokens above (Valheim's own RPC/ZDO system): these are general .NET/Unity
# APIs capable of reaching an external host. `Application.OpenURL` is
# deliberately NOT on this list — the Report a Bug button uses it to open
# the player's own browser on an explicit click, which sends no Teamster
# data anywhere; it is the one intentional exception, exactly like the
# brake's Rigidbody.constraints write is the no-force audit's.
TEAMSTER_INTERNET_EGRESS_TOKENS = (
    "HttpClient",
    "HttpWebRequest",
    "WebRequest",
    "WebClient",
    "UnityWebRequest",
    "TcpClient",
    "UdpClient",
    "System.Net.Sockets",
    "new Socket(",
)


def _strip_cs_line_comment(line: str) -> str:
    """Everything from the first // (covers // and ///) removed. Teamster's
    only network/ownership token mentions are in doc comments stating their
    absence, so comment-stripping keeps the audit true without flagging them.
    No // appears inside a string literal in the audited files (checked)."""
    index = line.find("//")
    return line if index < 0 else line[:index]


# CT-028: cooperative diagnostics help crews understand a cart without
# adding "a newton of modded force". Teamster applies no physics force,
# impulse, or velocity write anywhere — the only rigidbody touch is the
# parking brake's constraint freeze/unfreeze (CT-012), which holds a cart in
# place rather than pushing it. This audit fails on any force/impulse/
# velocity-write API so a future change cannot quietly start pushing carts.
# It also covers direct teleport writes (position/rotation assignment and the
# kinematic Move* pair) because "no teleporting carts" is a sibling safety
# invariant — the honest way to enforce both at once. (WakeUp() is
# deliberately NOT listed: it re-activates a sleeping body so the brake's
# constraint change takes effect; it injects no force and moves nothing.)
TEAMSTER_FORCE_TOKENS = (
    "AddForce",
    "AddTorque",
    "AddExplosionForce",
    "AddRelativeForce",
    "AddRelativeTorque",
    "AddForceAtPosition",
    ".velocity =",
    ".velocity=",
    ".angularVelocity =",
    ".angularVelocity=",
    ".linearVelocity =",
    ".linearVelocity=",
    ".AddImpulse",
    ".MovePosition",
    ".MoveRotation",
    # Teleport writes (no cart teleports): direct transform/body position or
    # rotation assignment. Reads (Vector3 p = t.position;) are untouched.
    "transform.position =",
    "transform.position=",
    "transform.localPosition =",
    "transform.localPosition=",
    "transform.rotation =",
    "transform.rotation=",
    ".Teleport(",
)


def check_teamster_no_force_injection(errors: list[str]) -> list[str]:
    """Fails on any physics force/impulse/velocity write in Teamster source
    (CT-028 zero-force guarantee). Comments are stripped so prose is fine."""
    teamster_dir: Path = PRODUCTS["teamster"]["project_dir"]  # type: ignore[assignment]
    hits = 0
    scanned = 0
    for path in sorted(teamster_dir.rglob("*.cs")):
        if path.relative_to(teamster_dir).parts[0] in ("obj", "bin"):
            continue
        scanned += 1
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            code = _strip_cs_line_comment(raw)
            for token in TEAMSTER_FORCE_TOKENS:
                if token in code:
                    hits += 1
                    fail(
                        f"[interop] CT-028 no-force audit: force/teleport token {token!r} in "
                        f"{path.relative_to(ROOT)}:{number} — Teamster is observational; it applies "
                        "no force, impulse, velocity write, or teleport (the brake only freezes "
                        "constraints)", errors)

    return [
        f"[interop] CT-028 no-force audit: {scanned} Teamster source files, "
        f"no force/impulse/velocity-write/teleport calls ({hits} violations)",
    ] + check_teamster_worker_runtime_scope(errors)


# #313 (CT-NPC-002) worker-runtime scope audit. Gunnar's opt-in hauling runtime
# in src/ConcernedTeamster/Adapters/Workers/ is the only Teamster code allowed
# to call a cart's own attach and detach, the only code that writes a mass (and
# only in Gunnar's own calibration file, on his own body), and the only code
# that writes a network object (only "tcc.worker." keys, on his own worker).
# Inside that folder it additionally may not teleport or write any position,
# rotation, velocity, kinematic, gravity, collision, constraint or joint
# connection, request or claim ownership, send an RPC, interact with a cart, or
# apply vanilla's extra pull mass itself. CT-002/CT-026/CT-028 stay absolute for
# every other folder; this audit only narrows what the one allowance permits.
TEAMSTER_WORKER_CALIBRATION_FILE = "TeamsterWorkerBody.cs"

# The one file that may write Gunnar's identity into his own worker object, and
# the one place the prefab clone is built (so it is also the only file allowed
# the factory's own component surgery).
TEAMSTER_WORKER_IDENTITY_FILE = "TeamsterWorkerPrefab.cs"

# `Detach()` and `DetachAll()` both release joints, and `DetachAll` releases
# every cart on the client (review R-313 m7).
TEAMSTER_CART_ATTACH_CALLS = re.compile(r"\.AttachTo\s*\(|\.Detach\s*\(|\.DetachAll\s*\(")
TEAMSTER_MASS_WRITE = re.compile(r"(\.mass|\bm_originalMass|\bm_baseMass)\s*[-+*/&|^]?=(?!=)")

# Compound forms (`|=`, `&=`, `^=`) count: `constraints |= FreezeAll` is still a
# constraint write.
TEAMSTER_WORKER_FORBIDDEN_ASSIGNMENT = re.compile(
    r"\.(position|localPosition|rotation|localRotation|velocity|linearVelocity|angularVelocity|"
    r"isKinematic|useGravity|detectCollisions|constraints|connectedBody|enabled|"
    r"m_attachJoin|m_attachedObject|m_useRequester|"
    r"m_breakForce|m_detachDistance|m_playerExtraPullMass|m_spring|m_springDamping|"
    r"m_itemWeightMassFactor|m_attachOffset|m_attachPoint|"
    r"xMotion|yMotion|zMotion|angularXMotion|angularYMotion|angularZMotion|"
    r"anchor|connectedAnchor|autoConfigureConnectedAnchor|breakForce|breakTorque|"
    r"xDrive|yDrive|zDrive|targetPosition|targetRotation)"
    r"\s*[-+*/&|^]?=(?!=)")
TEAMSTER_WORKER_FORBIDDEN_TOKENS = (
    "Teleport",
    "MovePosition",
    "MoveRotation",
    "AddForce",
    "AddTorque",
    "AddExplosionForce",
    "AddRelativeForce",
    "AddRelativeTorque",
    "SetPosition",
    "SetRotation",
    "SetOwner",
    "ClaimOwnership",
    "InvokeRPC",
    "ZRoutedRpc",
    "RPC_RequestOwn",
    ".Interact(",
    "SetExtraMass",
    "SetMass",
    "UpdateMass",
    "DetachAll",
    ".Translate(",
    ".Rotate(",
    ".RotateAround(",
)

# Allowed only in the prefab factory, which builds the inactive clone and strips
# the base creature's components; anywhere else in the runtime, switching
# components on and off or reaching through reflection is out of scope.
TEAMSTER_WORKER_FACTORY_ONLY_TOKENS = (
    "SetActive",
    "DestroyImmediate",
    "GetMethod(",
    "GetField(",
    "GetProperty(",
    "MethodInfo",
    "FieldInfo",
    "PropertyInfo",
    "Activator.CreateInstance",
)

# Never anywhere in Teamster outside the worker runtime: using a cart, applying
# vanilla's extra pull mass, or writing a body's kinematic flag or joint link.
# (The parking brake's own constraint write stays where CT-002 allows it.)
TEAMSTER_OUTSIDE_WORKERS_TOKENS = (".Interact(", "SetExtraMass")

# The one owner-authorized exception to the worker runtime's token list
# (owner decision, 2026-09-19, for #381 Gunnar collection).
#
# Gunnar's collection role has to pick up loose branches and stones, and
# vanilla's only route to that is `Pickable.Interact`, which Foreman already
# calls legitimately from its own sanctioned port because Foreman carries no
# such audit. Rather than let Teamster's source avoid spelling a banned token
# while the behaviour changed anyway - which would have left this audit green
# and meaningless - the allowance is explicit, named, and here.
#
# It is deliberately narrower than what was asked for. The agent requested three
# APIs; `Pickable.Interact` is the only one loose pickup needs. Picking runs
# `RPC_Pick` and the ownership claim *inside vanilla*, on a pickable this process
# already owns, so the port spells no RPC of its own - verified against Foreman's
# port, which makes exactly one game call and names no `InvokeRPC` anywhere.
# Felling a tree (`TreeBase.Damage`) and the cosmetic hammer animation
# (`ZSyncAnimation.SetTrigger`) are separate capabilities, were NOT authorized,
# and would each need their own owner decision.
#
# So: one token, in one file. Every other forbidden token still fails in that
# file, and this token still fails in every other file, inside Workers and out.
# Ownership takeover, teleports, forces, cart interaction and arbitrary RPC are
# untouched.
TEAMSTER_COLLECTION_PORT_FILE = "GunnarCollectionPort.cs"
TEAMSTER_COLLECTION_PORT_TOKEN = ".Interact("

TEAMSTER_OUTSIDE_WORKERS_ASSIGNMENT = re.compile(r"\.(isKinematic|connectedBody)\s*[-+*/&|^]?=(?!=)")


def check_teamster_worker_runtime_scope(errors: list[str]) -> list[str]:
    """Fails when the worker runtime's allowance reaches beyond Gunnar's own
    body and the cart's own attach/detach (#313). Comments are stripped."""
    teamster_dir: Path = PRODUCTS["teamster"]["project_dir"]  # type: ignore[assignment]
    workers_dir = teamster_dir.joinpath(*TEAMSTER_WORKERS_DIR)
    if not workers_dir.is_dir() or not any(workers_dir.glob("*.cs")):
        fail(
            "[interop] #313 worker-runtime scope audit: src/ConcernedTeamster/Adapters/Workers has no "
            "sources, so the audit no longer covers Gunnar's runtime (was it moved?)", errors)
        return []

    hits = 0
    worker_files = 0
    for path in sorted(teamster_dir.rglob("*.cs")):
        parts = path.relative_to(teamster_dir).parts
        if parts[0] in ("obj", "bin"):
            continue
        in_workers = parts[:2] == TEAMSTER_WORKERS_DIR
        worker_files += 1 if in_workers else 0
        rel = path.relative_to(ROOT)
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            code = _strip_cs_line_comment(raw)
            problems: list[str] = []
            if not in_workers and TEAMSTER_CART_ATTACH_CALLS.search(code):
                problems.append("a cart attach/detach call outside Adapters/Workers")
            if TEAMSTER_MASS_WRITE.search(code) and not (
                    in_workers and path.name == TEAMSTER_WORKER_CALIBRATION_FILE):
                problems.append(f"a mass write outside Gunnar's calibration file ({TEAMSTER_WORKER_CALIBRATION_FILE})")
            if in_workers:
                set_calls = TEAMSTER_WORKER_SET_CALL.findall(code)
                if set_calls and (path.name != TEAMSTER_WORKER_IDENTITY_FILE
                                  or len(set_calls) != len(TEAMSTER_WORKER_IDENTITY_SET.findall(code))):
                    # The key alone is not enough: the object written has to be
                    # Gunnar's own worker, which only the factory creates
                    # (review R-313 m7).
                    problems.append(
                        "a network-object write outside "
                        f"{TEAMSTER_WORKER_IDENTITY_FILE}, or with a key other than a \"tcc.worker.\" literal")
                assignment = TEAMSTER_WORKER_FORBIDDEN_ASSIGNMENT.search(code)
                if assignment:
                    problems.append(f"a forbidden write '{assignment.group(0).strip()}'")
                for token in TEAMSTER_WORKER_FORBIDDEN_TOKENS:
                    if token in code:
                        if (token == TEAMSTER_COLLECTION_PORT_TOKEN
                                and path.name == TEAMSTER_COLLECTION_PORT_FILE):
                            # The owner-authorized collection pickup, and only
                            # it: every other token below still fails here.
                            continue
                        problems.append(f"the forbidden token {token!r}")
                if path.name != TEAMSTER_WORKER_IDENTITY_FILE:
                    for token in TEAMSTER_WORKER_FACTORY_ONLY_TOKENS:
                        if token in code:
                            problems.append(
                                f"the token {token!r}, which belongs to the prefab factory "
                                f"({TEAMSTER_WORKER_IDENTITY_FILE}) alone")
            else:
                for token in TEAMSTER_OUTSIDE_WORKERS_TOKENS:
                    if token in code:
                        problems.append(f"the forbidden token {token!r} outside Adapters/Workers")
                outside = TEAMSTER_OUTSIDE_WORKERS_ASSIGNMENT.search(code)
                if outside:
                    problems.append(f"a forbidden write '{outside.group(0).strip()}' outside Adapters/Workers")
            for problem in problems:
                hits += 1
                fail(
                    f"[interop] #313 worker-runtime scope audit: {problem} in {rel}:{number} — Gunnar moves "
                    "only through the vanilla motor, attaches and detaches only through the cart's own "
                    "methods, calibrates only his own body and writes only his own identity", errors)

    return [
        f"[interop] #313 worker-runtime scope audit: {worker_files} worker files; {TEAMSTER_COLLECTION_PORT_TOKEN!r} owner-authorized in {TEAMSTER_COLLECTION_PORT_FILE} alone; cart attach/detach/detach-all only "
        f"in Adapters/Workers, mass writes only in {TEAMSTER_WORKER_CALIBRATION_FILE}, network-object writes only "
        f"'tcc.worker.*' keys in {TEAMSTER_WORKER_IDENTITY_FILE}, no teleport/pose/velocity/constraint/joint/cart-"
        f"tuning writes, no component surgery or reflection outside the prefab factory ({hits} violations)",
    ]


def check_teamster_authority_policy(errors: list[str]) -> list[str]:
    """Fails if the policy doc omits a TeamsterFeature, or if any outbound
    network / ownership-takeover token appears in Teamster source."""
    teamster_dir: Path = PRODUCTS["teamster"]["project_dir"]  # type: ignore[assignment]
    enum_file = teamster_dir / "Domain" / "Authority" / "TeamsterFeature.cs"
    policy_doc = ROOT / "docs" / "mods" / "concerned-teamster" / "AUTHORITY_POLICY.md"

    features: list[str] = []
    if not enum_file.is_file():
        fail(f"[interop] CT-026 authority audit: missing {enum_file.relative_to(ROOT)}", errors)
    else:
        # Strip // comments first so a brace or member-shaped word inside a
        # doc comment can neither truncate the enum body nor be miscounted.
        code_only = "\n".join(_strip_cs_line_comment(line) for line in
                              enum_file.read_text(encoding="utf-8").splitlines())
        match = re.search(r"enum\s+TeamsterFeature\s*\{([^}]*)\}", code_only, re.DOTALL)
        if not match:
            fail("[interop] CT-026 authority audit: could not parse the TeamsterFeature enum", errors)
        else:
            for line in match.group(1).splitlines():
                member = re.match(r"\s*([A-Za-z_]\w*)\s*(?:=[^,]+)?,?\s*$", line)
                if member:
                    features.append(member.group(1))
            if not features:
                fail(
                    "[interop] CT-026 authority audit: parsed zero TeamsterFeature members — "
                    "the enum format changed and the doc tripwire would be a false green", errors)

    if not policy_doc.is_file():
        fail(f"[interop] CT-026 authority audit: missing {policy_doc.relative_to(ROOT)}", errors)
    elif features:
        doc_text = policy_doc.read_text(encoding="utf-8")
        for feature in features:
            if f"`{feature}`" not in doc_text:
                fail(
                    f"[interop] CT-026 authority audit: feature {feature!r} is not documented in "
                    "AUTHORITY_POLICY.md — every TeamsterFeature needs a matrix row", errors)

    net_hits = 0
    for path in sorted(teamster_dir.rglob("*.cs")):
        parts = path.relative_to(teamster_dir).parts
        if parts[0] in ("obj", "bin"):
            continue
        in_workers = parts[:2] == TEAMSTER_WORKERS_DIR
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            code_raw = _strip_cs_line_comment(raw)
            code = code_raw.lower()
            for token in TEAMSTER_NETWORK_OWNERSHIP_TOKENS:
                # Case-insensitive so a lowercased `zdo.set(` cannot slip past.
                if token.lower() in code:
                    if (in_workers and token in TEAMSTER_WORKER_IDENTITY_WRITE_TOKENS
                            and _is_worker_identity_write(code_raw)):
                        # The one scoped allowance (#313): Gunnar's identity in
                        # his own worker body's network object.
                        continue
                    net_hits += 1
                    fail(
                        f"[interop] CT-026 authority audit: outbound-network/ownership token "
                        f"{token!r} in {path.relative_to(ROOT)}:{number} — Teamster is client-side "
                        "and read-only toward the game; it must send nothing and take no ownership", errors)

    return [
        f"[interop] CT-026 authority policy: {len(features)} features documented, "
        f"no outbound-network/ownership calls in Teamster source ({net_hits} violations)",
    ]


def check_teamster_no_internet_egress(errors: list[str]) -> list[str]:
    """CT-041 privacy audit: fails on any internet-egress-capable API in
    Teamster source. Comments are stripped so prose stating their absence
    is fine. `Application.OpenURL` is intentionally not checked for — see
    TEAMSTER_INTERNET_EGRESS_TOKENS's own comment."""
    teamster_dir: Path = PRODUCTS["teamster"]["project_dir"]  # type: ignore[assignment]
    hits = 0
    scanned = 0
    for path in sorted(teamster_dir.rglob("*.cs")):
        if path.relative_to(teamster_dir).parts[0] in ("obj", "bin"):
            continue
        scanned += 1
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            code = _strip_cs_line_comment(raw)
            for token in TEAMSTER_INTERNET_EGRESS_TOKENS:
                if token in code:
                    hits += 1
                    fail(
                        f"[interop] CT-041 privacy audit: internet-egress token {token!r} in "
                        f"{path.relative_to(ROOT)}:{number} — Teamster has no telemetry and must "
                        "never phone home", errors)

    return [
        f"[interop] CT-041 privacy audit: {scanned} Teamster source files, "
        f"no internet-egress calls ({hits} violations)",
    ]


def check_companion_body_fails_closed(errors: list[str]) -> list[str]:
    """CC-NPC-010 no-wake audit: the extracted companion body must be built
    dark and refused rather than switched on over a surviving game script.

    This is a source audit rather than a unit test because the thing being
    guarded is Unity object lifetime, which no test host here can run: the
    defect (#308) was that re-parenting a prefab subtree into an ACTIVE root
    runs Awake on the game's own scripts, and CharacterAnimEvent.Awake
    dereferences a Character the extraction has guaranteed is absent.

    Everything is anchored on the ONE line that re-parents the visual, because
    that line is the extraction: a second one is a second extraction path, and
    it must make its own no-wake decision rather than inherit this one by
    accident. Around that anchor the audit asks three questions and no more —
    is the root dark before the visual lands in it, is the script pass run
    before it is switched on, and does something refuse the candidate in
    between. Deliberately not a C# parser: it is the smallest check that
    cannot pass while the body is switched on over a script the pass could not
    remove - which is narrower than "while the defect is present", and is what
    it actually verifies.

    The second half covers the three accessory paths - hair and beards,
    garments, and the mug put in his hand for a drink - which parent a piece
    onto the LIVE figure and so wake it the same way. Each must refuse its
    piece on a survivor before that re-parent, and the check is ordered -
    condition, then the return, then the re-parent - so no comment and no
    unrelated `return false;` elsewhere in the method can stand in for it.
    Comments are stripped before any of this is read."""
    path = (ROOT / "src" / "ConcernedCartographer" / "Runtime" / "Companions" /
            "CompanionActor.cs")
    if not path.is_file():
        fail(
            "[companions] CC-NPC-010 no-wake audit: CompanionActor.cs is missing — the audit no "
            "longer covers the extraction (was it moved or renamed?)", errors)
        return []

    code = [_strip_cs_line_comment(line) for line in
            path.read_text(encoding="utf-8").splitlines()]

    # `root`, not `_root`: the local being built, never the field that
    # SetVisible toggles on a finished actor.
    dark = re.compile(r"(?<![\w.])root\.SetActive\(false\)")
    lit = re.compile(r"(?<![\w.])root\.SetActive\(true\)")

    anchors = [n for n, line in enumerate(code)
               if "visual.transform.SetParent(root.transform" in line]
    if len(anchors) != 1:
        fail(
            "[companions] CC-NPC-010 no-wake audit: expected exactly one re-parent of the "
            f"extracted visual in {path.relative_to(ROOT)}, found {len(anchors)} — a second "
            "extraction path must make its own no-wake decision rather than inherit this one",
            errors)
        return []

    reparent = anchors[0]

    # Bounded to THIS root's lifetime - from the line that creates it to the
    # line the visual lands on. Searching the whole file above would let an
    # unrelated `root.SetActive(false)` in some earlier method stand in for the
    # one that matters.
    born = next((n for n in range(reparent - 1, -1, -1)
                 if re.search(r"(?<![\w.])root\s*=\s*new GameObject\(", code[n])), None)
    if born is None or not any(dark.search(line) for line in code[born:reparent]):
        fail(
            "[companions] CC-NPC-010 no-wake audit: the visual is re-parented into a root that "
            f"was never made inactive in {path.relative_to(ROOT)}:{reparent + 1} — re-parenting "
            "into a live object is what runs Awake, and that is exactly how #308 threw on every "
            "placement", errors)
        return []

    after = code[reparent:]

    # A call may wrap across lines, so each position is read together with the
    # two lines below it. Cheap, and it keeps a formatting change from being
    # reported as a missing guard.
    def statement(index: int) -> str:
        return " ".join(after[index:index + 3])

    def refuses(lines: list[str], condition: int, token: str) -> bool:
        """True when the guard opened at `lines[condition]` returns with
        `token` unconditionally, at the top level of its own block.

        Brace counting, not parsing, and only as much as the job needs. Three
        things it must not accept, each of which an earlier version did:

        - a `return` further down the method, outside the guard - which is what
          a later `if (_bodyModel == null) { return false; }` was doing;
        - a `return` nested one level deeper, inside a condition of its own,
          because then the fall-through still wears the piece;
        - a `return` in an `else` branch, for the same reason.

        Hence `before == 1`: the line must sit directly inside the guard's own
        braces. A brace that does not balance - an escaped `{{` in a log
        message, say - therefore ends the scan without a match and fails the
        audit, rather than running past the block and finding something later.
        A braceless `if (x) return false;` is accepted, on that line or the one
        below it, because that is the same guard written shorter."""
        depth = 0
        opened = False
        for offset, line in enumerate(lines[condition:]):
            before = depth
            depth += line.count("{") - line.count("}")
            if not opened:
                if depth > 0:
                    opened = True
                    if token in line:
                        return True
                    continue
                if offset <= 1 and token in line:
                    return True
                if offset > 1:
                    return False
                continue
            if before == 1 and token in line:
                return True
            if before == 1 and re.search(r"(?<!\w)else(?!\w)", line):
                return False
            if depth <= 0:
                return False
        return False

    enable = next((n for n, line in enumerate(after) if lit.search(line)), None)
    pass_call = next((n for n, line in enumerate(after)
                      if "RemoveBehaviours(" in line and "root," in statement(n)), None)
    if enable is None or pass_call is None or pass_call > enable:
        fail(
            "[companions] CC-NPC-010 no-wake audit: the extracted body must have the source's own "
            f"scripts removed BEFORE it is switched on in {path.relative_to(ROOT)} — the pass and "
            "the enable are missing or in the wrong order", errors)
        return []

    # From AFTER the pass line, or the `out string survivors` declaration on it
    # would satisfy the test on its own; and the return has to follow the
    # condition, or any unrelated `return null;` in the window would do.
    between = after[pass_call + 1:enable]
    guard = next((n for n, line in enumerate(between) if "survivors.Length > 0" in line), None)
    if guard is None or not refuses(between, guard, "return null;"):
        fail(
            "[companions] CC-NPC-010 no-wake audit: nothing refuses the candidate between the "
            f"script-removal pass and root.SetActive(true) in {path.relative_to(ROOT)} — a script "
            "this build will not let us destroy must fail the extraction closed, because a "
            "disabled component still receives Awake when its object is activated", errors)
        return []

    # Every call site, not just the ones that look like accessories: a new
    # caller under any local name has to be looked at, and counting only the
    # ones spelled `piece` would let it in unseen.
    call_sites = [n for n, line in enumerate(code)
                  if "RemoveBehaviours(" in line and "private static int" not in line]
    if len(call_sites) != 4:
        fail(
            "[companions] CC-NPC-010 no-wake audit: expected exactly four RemoveBehaviours call "
            f"sites in {path.relative_to(ROOT)} (the body and the three accessory paths), found "
            f"{len(call_sites)} — a new caller hands a game prefab's subtree to the live figure "
            "too, and has to make its own refusal rather than inherit theirs", errors)
        return []

    # Every accessory path hands its piece to the live figure, so a script
    # that survived removal wakes there exactly as it would in the body.
    accessories = [n for n in call_sites if "piece," in " ".join(code[n:n + 3])]
    if len(accessories) != 3:
        fail(
            "[companions] CC-NPC-010 no-wake audit: expected exactly three accessory script passes "
            f"in {path.relative_to(ROOT)}, found {len(accessories)} — hair/beard, garments and the "
            "drinking mug are the three that parent a piece onto the live figure", errors)
        return []

    for call in accessories:
        attach = next((n for n in range(call + 1, len(code))
                       if "piece.transform.SetParent(" in code[n]), None)
        if attach is None:
            fail(
                "[companions] CC-NPC-010 no-wake audit: the accessory pass at "
                f"{path.relative_to(ROOT)}:{call + 1} is not followed by the re-parent it is "
                "supposed to guard — the audit no longer covers that path", errors)
            return []

        window = code[call + 1:attach]
        guard = next((n for n, line in enumerate(window) if "Survivors.Length > 0" in line), None)
        if guard is None or not refuses(window, guard, "return false;"):
            fail(
                "[companions] CC-NPC-010 no-wake audit: the accessory pass at "
                f"{path.relative_to(ROOT)}:{call + 1} does not refuse the piece before parenting it "
                f"on at line {attach + 1} — a source script that survived removal would wake the "
                "moment the piece joins the live figure", errors)
            return []

    return [
        "[companions] CC-NPC-010 no-wake audit: the extracted body is assembled dark, and a "
        "surviving game script refuses the candidate instead of being switched on",
        "[companions] CC-NPC-010 no-wake audit: all three accessory paths (hair/beard, garments, "
        "the drinking mug) refuse their piece on a surviving source script before it is parented "
        "onto the live figure",
    ]


def _cs_block(code: list[str], start: int) -> list[str]:
    """The lines of the braced block that opens at or after `start`. Brace
    counting on comment-stripped lines, which is all a method body needs."""
    depth = 0
    opened = False
    block = []
    for line in code[start:]:
        depth += line.count("{") - line.count("}")
        opened = opened or depth > 0
        block.append(line)
        if opened and depth <= 0:
            break
    return block


def check_companion_talk_is_not_a_reach(errors: list[str]) -> list[str]:
    """CC-NPC-011 talk audit: speaking to a companion is not a reach, and every
    companion prompt shows the player's real key.

    Both were seen by the owner in game. In Player.Interact the return value of
    Interactable.Interact decides one thing: whether the player plays
    DoInteractAnimation, the arm-raising reach used for chests and doors. The
    game's own talking NPCs, Trader and Raven, return false - so every value
    CompanionHover.Interact can produce, as a `return` or as an expression body,
    must be the literal `false`; a computed one could quietly become true again.
    And `$KEY_Use` is only a token until Localization.Localize turns it into the
    live binding, remaps and gamepad included, so every `$KEY_` token in Hulgi's
    and the compass's hover text must sit INSIDE a Localize call's string
    argument - not merely on the same line as one.

    Line and single-line block comments are stripped first, so a comment can
    satisfy neither rule. A Localize call whose argument is not one string
    literal on one line does not count; that is deliberate, and it fails
    loudly rather than passing on trust."""
    companions = ROOT / "src" / "ConcernedCartographer" / "Runtime" / "Companions"

    def read(name: str) -> list[str] | None:
        path = companions / name
        if not path.is_file():
            fail(f"[companions] CC-NPC-011 talk audit: {name} is missing — the audit no longer "
                 "covers it", errors)
            return None
        return [re.sub(r"/\*.*?\*/", "", _strip_cs_line_comment(line))
                for line in path.read_text(encoding="utf-8").splitlines()]

    def member(code: list[str], owner: str, signature: str, file: str) -> list[str] | None:
        at_class = next((n for n, line in enumerate(code) if f"class {owner}" in line), None)
        at = None if at_class is None else next(
            (n for n in range(at_class, len(code)) if signature in code[n]), None)
        if at is None:
            fail(f"[companions] CC-NPC-011 talk audit: {owner}.{signature.split('(')[0].split()[-1]} "
                 f"is missing from {file} — the audit no longer covers it", errors)
            return None
        # An expression body ends at its semicolon; a block body at its brace.
        if "=>" in " ".join(code[at:at + 2]).split("{")[0]:
            body = []
            for line in code[at:]:
                body.append(line)
                if ";" in line:
                    break
            return body
        return _cs_block(code, at)

    actor = read("CompanionActor.cs")
    compass = read("BrokenCompassObject.cs")
    if actor is None or compass is None:
        return []

    interact = member(actor, "CompanionHover", "public bool Interact(", "CompanionActor.cs")
    if interact is None:
        return []
    text = " ".join(interact)
    head = text.split("{")[0]
    values = ([head.split("=>", 1)[1].split(";", 1)[0].strip()] if "=>" in head
              else [value.strip() for value in re.findall(r"\breturn\b([^;]*);", text)])
    if not values or any(value != "false" for value in values):
        fail(
            "[companions] CC-NPC-011 talk audit: CompanionHover.Interact must produce the literal "
            f"`false` on every path, found {values} — anything else lets Player.Interact make "
            "the player raise an arm every time somebody talks to him", errors)
        return []

    localize = re.compile(r'Localization\.instance\.Localize\(\s*"[^"]*"\s*\)')
    for owner, code, file in (("CompanionHover", actor, "CompanionActor.cs"),
                              ("BrokenCompassObject", compass, "BrokenCompassObject.cs")):
        hover = member(code, owner, "public string GetHoverText(", file)
        if hover is None:
            return []
        resolved = sum(call.count("$KEY_") for line in hover for call in localize.findall(line))
        raw = [line.strip() for line in hover if "$KEY_" in localize.sub("", line)]
        if resolved == 0:
            fail(
                f"[companions] CC-NPC-011 talk audit: {owner}.GetHoverText no longer names a key "
                f"through Localization.instance.Localize in {file} — the player has to be told "
                "what to press", errors)
            return []
        if raw:
            fail(
                f"[companions] CC-NPC-011 talk audit: {owner}.GetHoverText has a `$KEY_` token "
                f"outside a Localize call in {file}: {raw} — it reaches the screen as the literal "
                "token instead of the player's key", errors)
            return []

    return [
        "[companions] CC-NPC-011 talk audit: talking to Hulgi is not a reach, and his prompt and "
        "the compass's show the player's own key",
    ]


SOLUTION_FOLDER_TYPE = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"

SOLUTION_ENTRY = re.compile(
    r'^\s*Project\("(?P<type>\{[0-9A-Fa-f-]+\})"\)\s*=\s*'
    r'"(?P<name>[^"]*)"\s*,\s*"(?P<path>[^"]*)"\s*,\s*"(?P<guid>\{[0-9A-Fa-f-]+\})"\s*$',
    re.MULTILINE)

SOLUTION_NESTING = re.compile(
    r'^\s*(?P<child>\{[0-9A-Fa-f-]+\})\s*=\s*(?P<parent>\{[0-9A-Fa-f-]+\})\s*$',
    re.MULTILINE)

ADD_A_PROJECT = (
    "add it by hand (a Project/EndProject pair, its 12 ProjectConfigurationPlatforms "
    "rows, and a NestedProjects row under the src folder) — `dotnet sln add` also "
    "creates solution folders mirroring the directories, and a folder whose name "
    "matches a project is what broke the solution in #341"
)


def check_solution_integrity(errors: list[str]) -> list[str]:
    """What loading the solution cannot tell us, plus a friendlier duplicate-name check.

    MSBuild is the authority on whether the file loads, and CI now asks it
    directly (`dotnet restore ConcernedCatMods.sln` in repo-checks.yml), which
    covers duplicate names, dangling nesting rows, a missing header, a missing
    EndProject and conflict markers — with MSBuild's own rules rather than a
    regex approximating them. That step exists because #341's real cause was
    that no CI step ever loaded the solution at all.

    Two things a load cannot know are checked here:

    * a `src/**/*.csproj` that is in the repository and in nobody's solution.
      A restore of a solution that never mentions it exits 0, and the suite
      inside it simply never runs;
    * duplicate entry names, kept as a fast local pre-check so the failure
      arrives with an explanation instead of as MSB5004.

    The duplicate key is the **solution-folder-qualified** name, which is
    MSBuild's own key: two projects called `Provider` under different folders
    are legal and must not be rejected here.
    """
    solution = ROOT / "ConcernedCatMods.sln"
    if not solution.is_file():
        fail("[solution] Missing required file: ConcernedCatMods.sln", errors)
        return []

    try:
        text = solution.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeDecodeError) as exception:
        # Never let this abort the run: every later check, including the
        # prohibited-DLL sweep, still has to report.
        fail(f"[solution] ConcernedCatMods.sln could not be read as UTF-8 "
             f"({type(exception).__name__}); MSBuild will not load it either", errors)
        return []

    declared = len(re.findall(r'^\s*Project\("', text, re.MULTILINE))
    entries = list(SOLUTION_ENTRY.finditer(text))
    if len(entries) != declared:
        fail(f"[solution] ConcernedCatMods.sln has {declared} Project entries but "
             f"{len(entries)} could be parsed; the unparsed ones are exempt from "
             f"every rule below, so this is fixed before anything else is trusted", errors)
        return []

    if declared == 0:
        fail("[solution] ConcernedCatMods.sln declares no projects at all", errors)
        return []

    folders: dict[str, str] = {}
    parents: dict[str, str] = {}
    names: dict[str, tuple[str, str]] = {}
    listed: set[str] = set()
    projects = 0

    for match in SOLUTION_NESTING.finditer(text):
        parents[match["child"].upper()] = match["parent"].upper()

    for match in entries:
        if match["type"].upper() == SOLUTION_FOLDER_TYPE:
            folders[match["guid"].upper()] = match["name"]

    def qualified(guid: str, name: str) -> str:
        """MSBuild's uniqueness key: the path of folder names down to this entry."""
        segments = [name]
        seen = {guid.upper()}
        parent = parents.get(guid.upper())
        while parent is not None and parent not in seen and parent in folders:
            seen.add(parent)
            segments.append(folders[parent])
            parent = parents.get(parent)
        return "\\".join(reversed(segments)).casefold()

    for match in entries:
        name, kind, raw, guid = match["name"], match["type"].upper(), match["path"], match["guid"]
        key = qualified(guid, name)
        previous = names.get(key)
        if previous is not None:
            fail(f"[solution] ConcernedCatMods.sln has two entries named '{name}' in the same "
                 f"place (a {previous[0]} at {previous[1]} and a "
                 f"{'folder' if kind == SOLUTION_FOLDER_TYPE else 'project'} at {raw}); "
                 f"MSBuild refuses the solution with MSB5004", errors)
        names[key] = ("folder" if kind == SOLUTION_FOLDER_TYPE else "project", raw)

        if kind == SOLUTION_FOLDER_TYPE:
            continue

        projects += 1
        if not raw.casefold().endswith(".csproj"):
            fail(f"[solution] ConcernedCatMods.sln lists '{raw}' as a project, but only a "
                 f".csproj can be built; MSBuild fails this with MSB4025 or MSB4040", errors)
            continue

        candidate = Path(raw.replace("\\", "/"))
        if candidate.is_absolute() or ".." in candidate.parts:
            fail(f"[solution] ConcernedCatMods.sln points at '{raw}', which is outside the "
                 f"repository; every project path must be relative to the solution", errors)
            continue

        path = ROOT / candidate
        if not path.is_file():
            fail(f"[solution] ConcernedCatMods.sln references a project file that is not "
                 f"there: {raw}", errors)
        # Recorded whether or not it exists. A case-wrong path on Windows finds
        # the file and canonicalises, on Linux it does not — and reporting the
        # same project as both missing and unlisted would be two errors that
        # contradict each other.
        listed.add(candidate.as_posix().casefold())

    for csproj in sorted((ROOT / "src").rglob("*.csproj")):
        parts = csproj.relative_to(ROOT).parts
        if "bin" in parts or "obj" in parts:
            continue
        if csproj.relative_to(ROOT).as_posix().casefold() not in listed:
            fail(f"[solution] {csproj.relative_to(ROOT)} exists but is not in "
                 f"ConcernedCatMods.sln, so no solution-wide build or test ever reaches it. "
                 f"The solution belongs to the lead (docs/settlement/cart-and-collection/"
                 f"TASKS.md §2): {ADD_A_PROJECT}", errors)

    return [f"[solution] {projects} projects, every path inside the repository, "
            f"every name unique in its folder, nothing under src/ left out"]


# The characters a UTF-8 lead byte turns into when its bytes are read as
# Windows-1252: a run starting with one of those, followed by another
# character from the same block, is text that was decoded with the wrong
# codec and re-encoded. Written as escapes rather than literals, so this
# file does not itself contain what it is looking for.
MOJIBAKE_LEAD = "\u00c2\u00c3\u00e2"
MOJIBAKE_TAIL = (
    "\u0080-\u00bf\u20ac\u201a\u0192\u201e\u2026\u2020\u2021\u02c6\u2030"
    "\u0160\u2039\u0152\u017d\u2018\u2019\u201c\u201d\u2022\u2013\u2014"
    "\u02dc\u2122\u0161\u203a\u0153\u017e\u0178")
MOJIBAKE = re.compile("[" + MOJIBAKE_LEAD + "][" + MOJIBAKE_TAIL + "]")


def _cartographer_known_names() -> tuple[set[str], set[str]]:
    """Every file name and suffix the fresh-install probe recognises.

    Parsed out of the sources rather than restated here, so this check stays true
    as those lists change instead of becoming a third copy of them.
    """
    names: set[str] = set()
    suffixes: set[str] = set()

    sources = (
        ROOT / "src/ConcernedCartographer/Domain/Companions/LegacyEvidenceRule.cs",
        ROOT / "src/ConcernedCartographer/Runtime/Companions/CartographerLegacyProbe.cs",
    )
    for source in sources:
        if not source.exists():
            continue
        for raw in source.read_text(encoding="utf-8-sig").splitlines():
            code = _strip_cs_line_comment(raw).strip()
            if not code.startswith(QUOTE) or not code.endswith(QUOTE + ","):
                continue
            literal = code[1:-2]
            if not literal:
                continue
            if literal.startswith("."):
                suffixes.add(literal)
            else:
                names.add(literal)

    return names, suffixes


def _literal_arguments(code: str, call: str) -> list[str]:
    """The string literals passed to `call` on this line.

    A non-literal argument (a world uid concatenated with a suffix) is not
    returned: its suffix is covered by the probe's own suffix list, and guessing
    at an expression is how a check starts reporting things that are not true.
    """
    found: list[str] = []
    index = code.find(call)
    while index >= 0:
        rest = code[index + len(call):].lstrip()
        if rest.startswith(QUOTE):
            closing = rest.find(QUOTE, 1)
            if closing > 0 and rest[closing + 1:].lstrip().startswith(")"):
                found.append(rest[1:closing])
        index = code.find(call, index + 1)

    return found


def _probe_knows(literal: str, names: set[str], suffixes: set[str]) -> bool:
    if literal in names:
        return True

    return any(literal.endswith(suffix) for suffix in suffixes)


def check_cartographer_root_holds_only_names_the_probe_knows(errors: list[str]) -> list[str]:
    """Nothing lands in the probed directory that the probe cannot account for.

    `CartographerLegacyProbe` decides new-versus-returning player by listing the
    product's data directory and asking whether every name in it is one this
    build writes for itself. Adding a file there is therefore the same act as
    changing who gets the #264 introduction, and the mechanism has been wrong
    twice: `survey-rules.tsv` made every fresh install look like a returning
    player, and `author-id.dat` (#343) did it again and reached main.

    Two rules, both about the invariant rather than about a spelling:

    1. Only `CartographerPaths` composes the directory, so there is one owner.
    2. Every name handed to `CartographerPaths.InRoot` is one the probe knows -
       a name this build writes for itself, or player evidence. A name in
       neither list is exactly #343.

    Rule 2 is the one that matters, and the first version of this check did not
    have it: it forbade the token `Paths.ConfigPath` and nothing else, so moving
    a marker back into the probed root - one token, and #343's shape - passed
    green. A check that cannot fail on the defect it was written for is worth
    nothing.

    A marker is additionally required to be under `state/`. `Directory.GetFiles`
    does not descend, which keeps it out of the probe's listing by construction,
    and out of the config editor #304 reported.
    """
    def flag(message: str) -> None:
        fail(message, errors)

    project_dir = PRODUCTS["cartographer"]["project_dir"]
    owner_relative = Path("src/ConcernedCartographer/CartographerPaths.cs")
    needle = "Paths.ConfigPath"
    in_root = "CartographerPaths.InRoot("

    if not (ROOT / owner_relative).exists():
        flag(
            f"[cartographer-paths] {owner_relative} is missing; it is the one place allowed "
            "to compose the product's data directory"
        )

    known_names, known_suffixes = _cartographer_known_names()
    if not known_names:
        flag(
            "[cartographer-paths] could not read the probe's known file names, so the "
            "root-contents rule cannot be checked"
        )
        return []

    # The shared companion sources are compiled into this product and write into
    # the same directory, so they are audited with it. They cannot use
    # CartographerPaths - the shared layer stays BepInEx-free - which is exactly
    # why they need the second rule rather than the first.
    roots = [Path(project_dir), ROOT / "src" / "Shared" / "Companions"]
    checked = 0

    for root in roots:
        if not root.exists():
            continue
        for path in sorted(root.rglob("*.cs")):
            relative = path.relative_to(ROOT)
            if relative == owner_relative:
                continue
            if any(part in ("obj", "bin") for part in relative.parts):
                continue

            try:
                text = path.read_text(encoding="utf-8-sig")
            except (OSError, UnicodeDecodeError) as problem:
                flag(f"[cartographer-paths] could not read {relative}: {problem}")
                continue

            checked += 1
            for number, raw in enumerate(text.splitlines(), start=1):
                code = _strip_cs_line_comment(raw)

                if needle in code:
                    flag(
                        f"[cartographer-paths] {relative}:{number} composes {needle} directly. "
                        "Use CartographerPaths.InRoot(name) for a file a player edited or "
                        "caused, or CartographerPaths.InState(name) for this build's own "
                        "bookkeeping - a file in the root decides who the fresh-install probe "
                        "calls a returning player (#343, #363)."
                    )

                for literal in _literal_arguments(code, in_root):
                    if literal.endswith(MARKER_EXTENSION):
                        flag(
                            f"[cartographer-paths] {relative}:{number} writes "
                            + QUOTE + literal + QUOTE
                            + " into the probed root. A marker belongs under "
                            "CartographerPaths.InState: Directory.GetFiles does not descend, "
                            "which keeps it out of the probe's listing and out of a config "
                            "editor (#304, #343)."
                        )
                    elif not _probe_knows(literal, known_names, known_suffixes):
                        flag(
                            f"[cartographer-paths] {relative}:{number} writes "
                            + QUOTE + literal + QUOTE
                            + " into the probed root, and the probe does not know that name. "
                            "Add it to CartographerFirstRunFiles (if this build writes it for "
                            "itself) or to the probe's evidence lists (if a player action "
                            "creates it) - an unknown name there makes every fresh install "
                            "look like a returning player (#343)."
                        )

    return [
        f"[cartographer-paths] one owner for the data directory; {checked} sources audited, "
        "every name written into it is one the fresh-install probe knows"
    ]


def check_no_mojibake(errors: list[str]) -> list[str]:
    """No tracked text file carries double-encoded UTF-8.

    An em dash that went through a cp1252 round trip comes back as a run of
    seven unreadable characters. Worse than unreadable, it is silent: the file
    is still valid UTF-8, so nothing complains, and it survives every review
    that does not happen to look at that line. A Foreman comment carried one
    through four merges before this check existed.
    """
    scanned = 0
    for path in sorted(ROOT.rglob("*")):
        if path.suffix.lower() not in (".cs", ".md", ".ps1", ".py", ".txt", ".json", ".toml", ".yml"):
            continue
        parts = path.relative_to(ROOT).parts
        if any(part in ("bin", "obj", ".git", "artifacts", "node_modules") for part in parts):
            continue
        if parts and parts[0] == ".claude":
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        scanned += 1
        match = MOJIBAKE.search(text)
        if match is not None:
            line = text.count(chr(10), 0, match.start()) + 1
            fail(f"[encoding] {path.relative_to(ROOT)}:{line} carries double-encoded UTF-8 "
                 f"({match.group(0)!r}); the file is valid UTF-8, so only this check sees it", errors)

    return [f"[encoding] {scanned} text files scanned, none double-encoded"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--product", choices=[*PACKAGES.keys(), "all"], default="cartographer",
        help="Which product --require-binary/--expected-version apply to "
             "(static validation always covers all products).")
    parser.add_argument("--require-binary", action="store_true")
    parser.add_argument("--expected-version")
    args = parser.parse_args()

    scoped = list(PACKAGES.keys()) if args.product == "all" else [args.product]
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
    for key in PACKAGES:
        report.extend(validate_product(
            key,
            errors,
            require_binary=args.require_binary and key in scoped,
            expected_version=args.expected_version if key in scoped else None,
        ))

    report.extend(check_solution_integrity(errors))
    report.extend(check_no_mojibake(errors))
    report.extend(check_cartographer_root_holds_only_names_the_probe_knows(errors))
    check_teamster_adapter_isolation(errors)
    report.extend(check_cross_product_independence(errors))
    report.extend(check_every_product_pair_is_audited(errors))
    report.extend(check_library_consumers(errors))
    report.extend(check_library_consumers_do_not_bypass_the_arbiter(errors))
    report.extend(check_the_npc_library_writes_no_file(errors))
    report.extend(check_npc_planning_decides_nothing_to_do_once(errors))
    report.extend(check_npc_planning_never_defaults_a_claim(errors))
    report.extend(check_teamster_cartographer_contract(errors))
    report.extend(check_teamster_integration_readonly(errors))
    report.extend(check_teamster_authority_policy(errors))
    report.extend(check_teamster_no_force_injection(errors))
    report.extend(check_teamster_no_internet_egress(errors))
    report.extend(check_companion_body_fails_closed(errors))
    report.extend(check_companion_talk_is_not_a_reach(errors))

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
