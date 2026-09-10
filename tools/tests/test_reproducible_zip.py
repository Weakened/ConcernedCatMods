from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from reproducible_zip import CANONICAL_TIMESTAMP, canonicalize_zip


class ReproducibleZipTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def _write_archive(
        self,
        name: str,
        entries: list[tuple[str, bytes]],
        timestamp: tuple[int, int, int, int, int, int],
        compression: int,
    ) -> Path:
        path = self.root / name
        with zipfile.ZipFile(path, "w", compression=compression) as archive:
            for entry_name, data in entries:
                info = zipfile.ZipInfo(entry_name, timestamp)
                info.compress_type = compression
                archive.writestr(info, data)
        return path

    def test_identical_payloads_become_byte_identical(self) -> None:
        payloads = [
            ("plugins/Mod.dll", b"compiled bytes"),
            ("manifest.json", b'{"name":"Example"}'),
            ("README.md", b"hello"),
        ]
        first = self._write_archive(
            "first.zip",
            payloads,
            (2026, 9, 10, 12, 0, 0),
            zipfile.ZIP_DEFLATED,
        )
        second = self._write_archive(
            "second.zip",
            list(reversed(payloads)),
            (2025, 1, 2, 3, 4, 6),
            zipfile.ZIP_STORED,
        )

        canonicalize_zip(first)
        canonicalize_zip(second)

        self.assertEqual(first.read_bytes(), second.read_bytes())
        self.assertEqual(
            hashlib.sha256(first.read_bytes()).digest(),
            hashlib.sha256(second.read_bytes()).digest(),
        )

        with zipfile.ZipFile(first, "r") as archive:
            infos = archive.infolist()
            self.assertEqual(
                [info.filename for info in infos],
                sorted(entry_name for entry_name, _ in payloads),
            )
            self.assertTrue(
                all(info.date_time == CANONICAL_TIMESTAMP for info in infos)
            )
            self.assertTrue(
                all(info.compress_type == zipfile.ZIP_STORED for info in infos)
            )
            self.assertEqual(
                {info.filename: archive.read(info) for info in infos},
                dict(payloads),
            )

    def test_duplicate_entry_fails_without_replacing_source(self) -> None:
        source = self.root / "duplicate.zip"
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(source, "w") as archive:
                archive.writestr("manifest.json", b"first")
                archive.writestr("manifest.json", b"second")
        original = source.read_bytes()

        with self.assertRaisesRegex(ValueError, "Duplicate ZIP entry"):
            canonicalize_zip(source)

        self.assertEqual(source.read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
