#!/usr/bin/env python3
"""Convert private cat photos into metadata-free JPEGs for the local Nyanstagram prototype."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff"}
PHOTO_ID_PATTERN = re.compile(r"^CAT_(\d{6})$")
CATALOG_FIELDS = (
    "PhotoId",
    "CatName",
    "ContributorName",
    "ContributorUrl",
    "ImageFileName",
    "Approved",
    "AllowGameUse",
    "AllowPromotionUse",
)


def project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def parse_arguments() -> argparse.Namespace:
    root = project_root()
    parser = argparse.ArgumentParser(
        description="実猫の原本を変更せず、向き補正済み・メタデータなしJPEGへ一括変換します。"
    )
    parser.add_argument("--input-dir", type=Path, default=root / "tools/nyanstagram/input")
    parser.add_argument("--manifest", type=Path, default=root / "tools/nyanstagram/conversion-manifest.json")
    parser.add_argument(
        "--catalog",
        type=Path,
        default=root / "Assets/StreamingAssets/Nyanstagram/Local/nyanstagram-local-catalog.json",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=root / "Assets/StreamingAssets/Nyanstagram/Local/Images",
    )
    parser.add_argument("--max-dimension", type=int, default=1024, help="JPEGの長辺上限 (初期値: 1024)")
    parser.add_argument("--quality", type=int, default=82, help="JPEG品質 1-95 (初期値: 82)")
    parser.add_argument(
        "--write-manifest-template",
        action="store_true",
        help="入力フォルダの画像名から、編集用の私用マニフェストを作成して終了します。",
    )
    parser.add_argument("--force", action="store_true", help="テンプレート作成時、既存マニフェストを上書きします。")
    parser.add_argument("--dry-run", action="store_true", help="設定とID割当だけを検証し、画像・JSONを書き換えません。")
    return parser.parse_args()


def read_json(path: Path, label: str) -> dict[str, Any]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise ValueError(f"{label}がありません: {path}") from error
    except json.JSONDecodeError as error:
        raise ValueError(f"{label}のJSONが不正です: {path} ({error.msg})") from error
    if not isinstance(document, dict):
        raise ValueError(f"{label}のルートはオブジェクトである必要があります: {path}")
    return document


def image_files(input_dir: Path) -> list[Path]:
    if not input_dir.is_dir():
        raise ValueError(f"入力フォルダがありません: {input_dir}")
    return sorted(
        (path for path in input_dir.iterdir() if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS),
        key=lambda path: path.name.casefold(),
    )


def write_manifest_template(input_dir: Path, manifest_path: Path, force: bool) -> None:
    if manifest_path.exists() and not force:
        raise ValueError(f"既存の私用マニフェストを保護するため作成を中止しました: {manifest_path} (--force で上書き)")

    photos = []
    for source in image_files(input_dir):
        photos.append(
            {
                "SourceFileName": source.name,
                "PhotoId": "",
                "CatName": "",
                "ContributorName": "",
                "ContributorUrl": "",
                "Approved": False,
                "AllowGameUse": False,
                "AllowPromotionUse": False,
            }
        )
    write_json_atomically(manifest_path, {"Photos": photos})
    print(f"私用マニフェストを作成しました: {manifest_path} ({len(photos)}件)")


def require_string(record: dict[str, Any], field: str, label: str, allow_empty: bool = False) -> str:
    value = record.get(field)
    if not isinstance(value, str) or (not allow_empty and not value.strip()):
        raise ValueError(f"{label} の {field} は{'空でない' if not allow_empty else ''}文字列にしてください。")
    return value.strip()


def require_bool(record: dict[str, Any], field: str, label: str) -> bool:
    value = record.get(field)
    if not isinstance(value, bool):
        raise ValueError(f"{label} の {field} は true または false にしてください。")
    return value


def validate_file_name(name: str, label: str) -> None:
    if Path(name).name != name or name in {"", ".", ".."}:
        raise ValueError(f"{label} の SourceFileName は入力フォルダ直下のファイル名だけにしてください。")
    if Path(name).suffix.lower() not in SUPPORTED_EXTENSIONS:
        raise ValueError(f"{label} の SourceFileName は対応画像形式ではありません: {name}")


def validate_catalog_entry(entry: dict[str, Any], label: str) -> None:
    missing = [field for field in CATALOG_FIELDS if field not in entry]
    if missing:
        raise ValueError(f"{label} に必須フィールドがありません: {', '.join(missing)}")
    photo_id = require_string(entry, "PhotoId", label)
    if not PHOTO_ID_PATTERN.fullmatch(photo_id):
        raise ValueError(f"{label} の PhotoId は CAT_000001 形式にしてください。")
    require_string(entry, "CatName", label)
    require_string(entry, "ContributorName", label)
    require_string(entry, "ContributorUrl", label, allow_empty=True)
    image_file_name = require_string(entry, "ImageFileName", label)
    if Path(image_file_name).name != image_file_name or not image_file_name.lower().endswith(".jpg"):
        raise ValueError(f"{label} の ImageFileName はパスを含まない .jpg ファイル名にしてください。")
    for field in ("Approved", "AllowGameUse", "AllowPromotionUse"):
        require_bool(entry, field, label)


def next_photo_id(used_ids: set[str]) -> str:
    highest = 0
    for photo_id in used_ids:
        match = PHOTO_ID_PATTERN.fullmatch(photo_id)
        if match:
            highest = max(highest, int(match.group(1)))
    candidate = highest + 1
    while f"CAT_{candidate:06d}" in used_ids:
        candidate += 1
    return f"CAT_{candidate:06d}"


def prepare_records(
    manifest: dict[str, Any], catalog: dict[str, Any], input_dir: Path
) -> tuple[list[tuple[dict[str, Any], dict[str, Any]]], list[dict[str, Any]]]:
    manifest_photos = manifest.get("Photos")
    catalog_photos = catalog.get("Photos")
    if not isinstance(manifest_photos, list):
        raise ValueError("私用マニフェストの Photos は配列にしてください。")
    if not isinstance(catalog_photos, list):
        raise ValueError("ローカルカタログの Photos は配列にしてください。")

    catalog_by_id: dict[str, dict[str, Any]] = {}
    for index, entry in enumerate(catalog_photos):
        if not isinstance(entry, dict):
            raise ValueError(f"ローカルカタログ Photos[{index}] はオブジェクトにしてください。")
        validate_catalog_entry(entry, f"ローカルカタログ Photos[{index}]")
        photo_id = entry["PhotoId"]
        if photo_id in catalog_by_id:
            raise ValueError(f"ローカルカタログに重複したPhotoIdがあります: {photo_id}")
        catalog_by_id[photo_id] = entry

    source_names: set[str] = set()
    used_ids = set(catalog_by_id)
    prepared: list[tuple[dict[str, Any], dict[str, Any]]] = []
    for index, record in enumerate(manifest_photos):
        label = f"私用マニフェスト Photos[{index}]"
        if not isinstance(record, dict):
            raise ValueError(f"{label} はオブジェクトにしてください。")
        source_name = require_string(record, "SourceFileName", label)
        validate_file_name(source_name, label)
        if source_name in source_names:
            raise ValueError(f"私用マニフェストに同じSourceFileNameがあります: {source_name}")
        source_names.add(source_name)
        if not (input_dir / source_name).is_file():
            raise ValueError(f"入力原本がありません: {input_dir / source_name}")

        requested_id = require_string(record, "PhotoId", label, allow_empty=True)
        if requested_id and not PHOTO_ID_PATTERN.fullmatch(requested_id):
            raise ValueError(f"{label} の PhotoId は空または CAT_000001 形式にしてください。")
        if not requested_id:
            requested_id = next_photo_id(used_ids)
            record["PhotoId"] = requested_id
        elif requested_id in used_ids and requested_id not in catalog_by_id:
            raise ValueError(f"私用マニフェストに重複したPhotoIdがあります: {requested_id}")
        used_ids.add(requested_id)

        cat_name = require_string(record, "CatName", label)
        contributor_name = require_string(record, "ContributorName", label)
        contributor_url = require_string(record, "ContributorUrl", label, allow_empty=True)
        generated = {
            "PhotoId": requested_id,
            "CatName": cat_name,
            "ContributorName": contributor_name,
            "ContributorUrl": contributor_url,
            "ImageFileName": f"{requested_id}.jpg",
            "Approved": require_bool(record, "Approved", label),
            "AllowGameUse": require_bool(record, "AllowGameUse", label),
            "AllowPromotionUse": require_bool(record, "AllowPromotionUse", label),
        }
        prepared.append((record, generated))

    unlisted = [path.name for path in image_files(input_dir) if path.name not in source_names]
    if unlisted:
        raise ValueError("私用マニフェストに未登録の入力画像があります: " + ", ".join(unlisted))

    return prepared, list(catalog_by_id.values())


def load_pillow() -> tuple[Any, Any]:
    try:
        from PIL import Image, ImageOps
    except ModuleNotFoundError as error:
        raise ValueError("Pillowが未導入です。python -m pip install -r tools/nyanstagram/requirements.txt を実行してください。") from error
    return Image, ImageOps


def convert_image(source: Path, destination: Path, max_dimension: int, quality: int) -> None:
    Image, ImageOps = load_pillow()
    with Image.open(source) as opened:
        oriented = ImageOps.exif_transpose(opened)
        if "A" in oriented.getbands():
            rgba = oriented.convert("RGBA")
            converted = Image.new("RGB", rgba.size, "white")
            converted.paste(rgba, mask=rgba.getchannel("A"))
        else:
            converted = oriented.convert("RGB")
        converted.thumbnail((max_dimension, max_dimension), Image.Resampling.LANCZOS)
        temporary = destination.with_suffix(destination.suffix + ".tmp")
        try:
            # EXIF/ICC/GPS are intentionally not passed to save(), so the output contains pixels only.
            converted.save(temporary, format="JPEG", quality=quality, optimize=True, progressive=True)
            temporary.replace(destination)
        finally:
            if temporary.exists():
                temporary.unlink()


def write_json_atomically(path: Path, document: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    try:
        temporary.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        temporary.replace(path)
    finally:
        if temporary.exists():
            temporary.unlink()


def run(args: argparse.Namespace) -> None:
    if not 1 <= args.quality <= 95:
        raise ValueError("--quality は1から95にしてください。")
    if args.max_dimension < 1:
        raise ValueError("--max-dimension は1以上にしてください。")
    if args.write_manifest_template:
        write_manifest_template(args.input_dir, args.manifest, args.force)
        return

    manifest = read_json(args.manifest, "私用マニフェスト")
    catalog = read_json(args.catalog, "ローカルカタログ")
    prepared, existing_catalog_entries = prepare_records(manifest, catalog, args.input_dir)
    if args.dry_run:
        print(f"検証完了: {len(prepared)}件。画像・JSONは変更していません。")
        return

    args.output_dir.mkdir(parents=True, exist_ok=True)
    updated_catalog = {entry["PhotoId"]: entry for entry in existing_catalog_entries}
    for record, generated in prepared:
        source = args.input_dir / record["SourceFileName"]
        destination = args.output_dir / generated["ImageFileName"]
        convert_image(source, destination, args.max_dimension, args.quality)
        updated_catalog[generated["PhotoId"]] = generated
        print(f"変換完了: {generated['PhotoId']}.jpg")

    ordered_catalog = sorted(updated_catalog.values(), key=lambda entry: int(entry["PhotoId"].split("_")[1]))
    write_json_atomically(args.catalog, {"Photos": ordered_catalog})
    write_json_atomically(args.manifest, manifest)
    print(f"ローカルカタログを更新しました: {args.catalog}")


def main() -> int:
    try:
        run(parse_arguments())
        return 0
    except ValueError as error:
        print(f"エラー: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
