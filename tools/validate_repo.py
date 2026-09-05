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
    ("cartographer", "src/ConcernedCartographer", "ConcernedTeamster"),
    ("cartographer", "src/ConcernedCartographer.Tests", "ConcernedTeamster"),
)


def check_cross_product_independence(errors: list[str]) -> list[str]:
    """Fails on any compile-time reference between the two products.

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
        for target in {"ConcernedCartographer", "ConcernedTeamster"}
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
# (WakeUp() is deliberately NOT listed: it re-activates a sleeping body so the
# brake's constraint change takes effect; it injects no force.)
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
                        f"[interop] CT-028 no-force audit: physics-force token {token!r} in "
                        f"{path.relative_to(ROOT)}:{number} — Teamster is observational; it applies "
                        "no force, impulse, or velocity write (the brake only freezes constraints)", errors)

    return [
        f"[interop] CT-028 no-force audit: {scanned} Teamster source files, "
        f"no force/impulse/velocity-write calls ({hits} violations)",
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
        if path.relative_to(teamster_dir).parts[0] in ("obj", "bin"):
            continue
        for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            code = _strip_cs_line_comment(raw).lower()
            for token in TEAMSTER_NETWORK_OWNERSHIP_TOKENS:
                # Case-insensitive so a lowercased `zdo.set(` cannot slip past.
                if token.lower() in code:
                    net_hits += 1
                    fail(
                        f"[interop] CT-026 authority audit: outbound-network/ownership token "
                        f"{token!r} in {path.relative_to(ROOT)}:{number} — Teamster is client-side "
                        "and read-only toward the game; it must send nothing and take no ownership", errors)

    return [
        f"[interop] CT-026 authority policy: {len(features)} features documented, "
        f"no outbound-network/ownership calls in Teamster source ({net_hits} violations)",
    ]


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
    report.extend(check_cross_product_independence(errors))
    report.extend(check_teamster_cartographer_contract(errors))
    report.extend(check_teamster_integration_readonly(errors))
    report.extend(check_teamster_authority_policy(errors))
    report.extend(check_teamster_no_force_injection(errors))

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
