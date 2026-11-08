#!/usr/bin/env python3
"""Build the raw-resource atlas contract.

Reads the 90-row catalog from `_plan/RESOURCE_ATLAS_LAYOUT.md`, validates the
source atlas (16 x 6 cells of 64 x 64), copies it to
`ui/assets/textures/resources_raw/` and writes `resources_raw_atlas.json`
with `cell` and `trim` rects for every resource.

The atlas is copied as-is: pixel art is never resampled here. `trim` is the
alpha bounding box inside a cell; the UI uses it to fit an icon into a 60 x 60
box with a 2 px margin, so the source keeps its original pixels.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import shutil
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DOC = ROOT / "_plan" / "RESOURCE_ATLAS_LAYOUT.md"
SOURCE_ATLAS = ROOT / "ui" / "assets" / "textures" / "icons" / "Natural resource.png"
SOURCE_UID = "uid://dk6aenpogdk6x"
OUT_DIR = ROOT / "ui" / "assets" / "textures" / "resources_raw"
OUT_ATLAS = OUT_DIR / "resources_raw_atlas.png"
OUT_JSON = OUT_DIR / "resources_raw_atlas.json"
PREVIEW = ROOT / "_tools" / "shots" / "resources_raw_preview.png"

CELL = 64
COLUMNS = 16
ROWS = 6
COUNT = 90

ROW_RE = re.compile(
    r"^\|\s*(\d+)\s*\|\s*`([^`]+)`\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*$"
)


def parse_catalog(doc: Path) -> list[dict]:
    if not doc.exists():
        raise SystemExit(f"catalog: missing {doc}")
    entries = []
    for line in doc.read_text(encoding="utf-8").splitlines():
        match = ROW_RE.match(line.strip())
        if not match:
            continue
        entries.append(
            {
                "index": int(match.group(1)),
                "id": match.group(2).strip(),
                "name_ru": match.group(3).strip(),
                "category": match.group(4).strip(),
            }
        )
    entries.sort(key=lambda entry: entry["index"])
    if len(entries) != COUNT:
        raise SystemExit(f"catalog: expected {COUNT} rows, got {len(entries)}")
    indices = [entry["index"] for entry in entries]
    if indices != list(range(COUNT)):
        raise SystemExit(f"catalog: indices are not 0..{COUNT - 1}: {indices}")
    ids = [entry["id"] for entry in entries]
    if len(set(ids)) != COUNT:
        raise SystemExit("catalog: duplicate ids")
    return entries


def load_atlas() -> Image.Image:
    if not SOURCE_ATLAS.exists():
        raise SystemExit(f"atlas: missing {SOURCE_ATLAS}")
    image = Image.open(SOURCE_ATLAS).convert("RGBA")
    expected = (COLUMNS * CELL, ROWS * CELL)
    if image.size != expected:
        raise SystemExit(f"atlas: expected {expected[0]}x{expected[1]}, got {image.width}x{image.height}")
    return image


def cell_rect(index: int) -> tuple[int, int, int, int]:
    row, col = divmod(index, COLUMNS)
    return col * CELL, row * CELL, CELL, CELL


def analyze_cell(image: Image.Image, index: int) -> dict:
    x, y, w, h = cell_rect(index)
    tile = image.crop((x, y, x + w, y + h))
    bbox = tile.getchannel("A").getbbox()
    if bbox is None:
        raise SystemExit(f"cell {index}: fully transparent")
    x0, y0, x1, y1 = bbox
    return {
        "cell": {"x": x, "y": y, "w": w, "h": h},
        "trim": {"x": x0, "y": y0, "w": x1 - x0, "h": y1 - y0},
    }


def build_preview(image: Image.Image, entries: list[dict]) -> None:
    tile_out = 128
    pad = 6
    label_h = 16
    sheet_cols = 12
    sheet_rows = math.ceil(len(entries) / sheet_cols)
    width = sheet_cols * (tile_out + pad) + pad
    height = sheet_rows * (tile_out + label_h + pad) + pad
    sheet = Image.new("RGBA", (width, height), (28, 28, 32, 255))
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 13)
    except OSError:
        font = ImageFont.load_default()

    for position, entry in enumerate(entries):
        index = entry["index"]
        x, y, _, _ = cell_rect(index)
        tile = image.crop((x, y, x + CELL, y + CELL)).resize((tile_out, tile_out), Image.NEAREST)
        sheet_row, sheet_col = divmod(position, sheet_cols)
        sx = pad + sheet_col * (tile_out + pad)
        sy = pad + sheet_row * (tile_out + label_h + pad)
        for yy in range(0, tile_out, 16):
            for xx in range(0, tile_out, 16):
                shade = (58, 58, 62, 255) if ((xx // 16 + yy // 16) % 2 == 0) else (44, 44, 48, 255)
                draw.rectangle([sx + xx, sy + yy, sx + xx + 15, sy + yy + 15], fill=shade)
        sheet.alpha_composite(tile, (sx, sy))
        draw.rectangle([sx, sy, sx + tile_out - 1, sy + tile_out - 1], outline=(100, 100, 108, 255))
        draw.text((sx + 2, sy + tile_out + 1), f'{index} {entry["id"]}', fill=(235, 235, 240, 255), font=font)

    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(PREVIEW)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-preview", action="store_true", help="skip the contact sheet")
    args = parser.parse_args()

    entries = parse_catalog(DOC)
    image = load_atlas()

    packed = []
    empty = []
    edge_touch = []
    over_60 = []
    for entry in entries:
        info = analyze_cell(image, entry["index"])
        packed.append({**entry, **info})
        trim = info["trim"]
        if trim["w"] <= 0 or trim["h"] <= 0:
            empty.append(entry["index"])
        if trim["x"] <= 1 or trim["y"] <= 1 or trim["x"] + trim["w"] >= CELL - 1 or trim["y"] + trim["h"] >= CELL - 1:
            edge_touch.append(entry["index"])
        if trim["w"] > 60 or trim["h"] > 60:
            over_60.append(entry["index"])

    if empty:
        raise SystemExit(f"atlas: empty cells after analysis: {empty}")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(SOURCE_ATLAS, OUT_ATLAS)

    data = {
        "version": 1,
        "generated_by": "tools/pack_resources_atlas.py",
        "atlas": {
            "path": f"res://{OUT_ATLAS.relative_to(ROOT).as_posix()}",
            "source_path": f"res://{SOURCE_ATLAS.relative_to(ROOT).as_posix()}",
            "source_uid": SOURCE_UID,
            "width": image.width,
            "height": image.height,
            "cell": CELL,
            "columns": COLUMNS,
            "rows": ROWS,
        },
        "entries": packed,
    }
    OUT_JSON.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    if not args.no_preview:
        build_preview(image, entries)

    print(f"catalog: {len(entries)} entries, indices 0..{COUNT - 1}")
    print(f"atlas: {image.width}x{image.height}, {COLUMNS}x{ROWS} cells of {CELL}px")
    print(f"edge-touch: {len(edge_touch)} -> {edge_touch}")
    print(f"over-60: {len(over_60)} -> {over_60}")
    print(f"wrote: {OUT_ATLAS.relative_to(ROOT)}")
    print(f"wrote: {OUT_JSON.relative_to(ROOT)}")
    if not args.no_preview:
        print(f"preview: {PREVIEW.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())