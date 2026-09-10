#!/usr/bin/env python3
"""Canonicalize a ZIP so identical payloads produce identical bytes."""

from __future__ import annotations

import argparse
import os
import zipfile
from dataclasses import dataclass
from pathlib import Path

CANONICAL_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
REGULAR_FILE_MODE = 0o100644
DIRECTORY_MODE = 0o40755


@dataclass(frozen=True)
class Entry:
    name: str
    data: bytes
    is_directory: bool


def _read_entries(path: Path) -> list[Entry]:
    entries: list[Entry] = []
    seen: set[str] = set()
    with zipfile.ZipFile(path, "r") as source:
        for info in source.infolist():
            name = info.filename.replace("\\", "/")
            if name in seen:
                raise ValueError(f"Duplicate ZIP entry: {name}")
            seen.add(name)
            entries.append(Entry(name, source.read(info), info.is_dir()))
    return sorted(entries, key=lambda entry: entry.name)


def canonicalize_zip(path: Path) -> None:
    """Atomically rewrite *path* with stable order, metadata, and storage."""
    path = path.resolve()
    if not path.is_file():
        raise FileNotFoundError(path)

    entries = _read_entries(path)
    temporary = path.with_name(f".{path.name}.reproducible.tmp")
    try:
        with zipfile.ZipFile(
            temporary,
            "w",
            compression=zipfile.ZIP_STORED,
            allowZip64=True,
        ) as target:
            for entry in entries:
                info = zipfile.ZipInfo(entry.name, CANONICAL_TIMESTAMP)
                info.create_system = 3
                mode = DIRECTORY_MODE if entry.is_directory else REGULAR_FILE_MODE
                info.external_attr = mode << 16
                info.compress_type = zipfile.ZIP_STORED
                target.writestr(info, entry.data)

        with zipfile.ZipFile(temporary, "r") as check:
            bad_entry = check.testzip()
            if bad_entry is not None:
                raise ValueError(f"CRC validation failed for ZIP entry: {bad_entry}")
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Rewrite a ZIP into a byte-reproducible canonical form."
    )
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    canonicalize_zip(args.archive)
    print(f"Canonicalized reproducible archive: {args.archive}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
