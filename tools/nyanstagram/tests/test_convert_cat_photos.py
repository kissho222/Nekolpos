from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from argparse import Namespace
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import convert_cat_photos as converter  # noqa: E402


class ConvertCatPhotosTests(unittest.TestCase):
    def test_conversion_normalizes_orientation_strips_metadata_and_keeps_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            input_dir = root / "input"
            output_dir = root / "output"
            input_dir.mkdir()
            source = input_dir / "private-original-name.png"
            original = Image.new("RGB", (2000, 1000), "red")
            exif = Image.Exif()
            exif[274] = 6
            original.save(source, exif=exif)
            original_hash = hashlib.sha256(source.read_bytes()).hexdigest()

            manifest_path = root / "manifest.json"
            catalog_path = root / "catalog.json"
            manifest_path.write_text(
                json.dumps(
                    {
                        "Photos": [
                            {
                                "SourceFileName": source.name,
                                "PhotoId": "",
                                "CatName": "テスト猫",
                                "ContributorName": "テスト投稿者",
                                "ContributorUrl": "",
                                "Approved": True,
                                "AllowGameUse": True,
                                "AllowPromotionUse": False,
                            }
                        ]
                    },
                    ensure_ascii=False,
                ),
                encoding="utf-8",
            )
            catalog_path.write_text('{"Photos": []}', encoding="utf-8")

            converter.run(
                Namespace(
                    input_dir=input_dir,
                    manifest=manifest_path,
                    catalog=catalog_path,
                    output_dir=output_dir,
                    max_dimension=1024,
                    quality=82,
                    write_manifest_template=False,
                    force=False,
                    dry_run=False,
                )
            )

            output = output_dir / "CAT_000001.jpg"
            self.assertTrue(output.is_file())
            self.assertEqual(original_hash, hashlib.sha256(source.read_bytes()).hexdigest())
            with Image.open(output) as image:
                self.assertEqual("RGB", image.mode)
                self.assertEqual((512, 1024), image.size)
                self.assertEqual(0, len(image.getexif()))

            catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
            self.assertEqual("CAT_000001", catalog["Photos"][0]["PhotoId"])
            self.assertEqual("CAT_000001.jpg", catalog["Photos"][0]["ImageFileName"])
            self.assertNotIn("SourceFileName", catalog["Photos"][0])

    def test_rejects_unlisted_source_image(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            input_dir = root / "input"
            input_dir.mkdir()
            (input_dir / "unlisted.jpg").write_bytes(b"not-decoded-in-dry-run")
            manifest_path = root / "manifest.json"
            catalog_path = root / "catalog.json"
            manifest_path.write_text('{"Photos": []}', encoding="utf-8")
            catalog_path.write_text('{"Photos": []}', encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "未登録"):
                converter.run(
                    Namespace(
                        input_dir=input_dir,
                        manifest=manifest_path,
                        catalog=catalog_path,
                        output_dir=root / "output",
                        max_dimension=1024,
                        quality=82,
                        write_manifest_template=False,
                        force=False,
                        dry_run=True,
                    )
                )
