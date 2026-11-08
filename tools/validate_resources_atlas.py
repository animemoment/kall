#!/usr/bin/env python3
"""Validate `resources_raw_atlas.json` against the atlas image.

Checks:
- 90 entries, unique indices 0..89, unique snake_case ids;
- atlas dimensions match the declared grid;
- every cell is non-empty and its `trim` matches the alpha bounding box;
- `trim` stays inside its cell; cells do not overlap.

Warnings (use --strict to fail on them):
- content touches the cell edge (possible clipping, no 2 px margin);
- content is larger than 60 x 60 (will be scaled by the UI).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_JSON = ROOT / "ui" / "assets" / "textures" / "resources_raw" / "resources_raw_atlas.json"
COUNT = 90
ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")


def fs_path(res_path: str) -> Path:
    prefix = "res://"
    if res_path.startswith(prefix):
        return ROOT / res_path[len(prefix):]
    path = Path(res_path)
    if path.is_absolute():
        return path
    return ROOT / path


def rect_key(rect: dict) -> tuple[int, int, int, int]:
    return rect["x"], rect["y"], rect["w"], rect["h"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", default=str(DEFAULT_JSON), help="atlas JSON path")
    parser.add_argument("--strict", action="store_true", help="treat warnings as errors")
    args = parser.parse_args()

    json_path = Path(args.json)
    if not json_path.exists():
        print(f"FAIL: missing {json_path}")
        return 1

    try:
        data = json.loads(json_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"FAIL: invalid JSON: {exc}")
        return 1

    errors: list[str] = []
    warnings: list[str] = []

    atlas = data.get("atlas")
    if not isinstance(atlas, dict):
        print("FAIL: missing atlas object")
        return 1

    cell = atlas.get("cell")
    columns = atlas.get("columns")
    rows = atlas.get("rows")
    width = atlas.get("width")
    height = atlas.get("height")
    for name, value in (("cell", cell), ("columns", columns), ("rows", rows), ("width", width), ("height", height)):
        if not isinstance(value, int) or value <= 0:
            errors.append(f"atlas.{name}: expected a positive integer, got {value!r}")
    if errors:
        for message in errors:
            print(f" ERROR {message}")
        print(f"FAIL: {len(errors)} error(s)")
        return 1

    assert isinstance(cell, int) and isinstance(columns, int) and isinstance(rows, int)
    assert isinstance(width, int) and isinstance(height, int)

    if width != columns * cell:
        errors.append(f"atlas.width: {width} != columns({columns}) * cell({cell})")
    if height != rows * cell:
        errors.append(f"atlas.height: {height} != rows({rows}) * cell({cell})")

    atlas_path = atlas.get("path")
    if not isinstance(atlas_path, str) or not atlas_path:
        errors.append("atlas.path: missing")
        atlas_image = None
    else:
        image_path = fs_path(atlas_path)
        if not image_path.exists():
            errors.append(f"atlas.path: file not found: {image_path}")
            atlas_image = None
        else:
            atlas_image = Image.open(image_path).convert("RGBA")
            if atlas_image.size != (width, height):
                errors.append(f"atlas image: {atlas_image.width}x{atlas_image.height} != {width}x{height}")

    entries = data.get("entries")
    if not isinstance(entries, list):
        errors.append("entries: expected a list")
        entries = []
    if len(entries) != COUNT:
        errors.append(f"entries: expected {COUNT}, got {len(entries)}")

    seen_indices: set[int] = set()
    seen_ids: set[str] = set()
    rects: list[tuple[int, int, int, int]] = []
    edge_touch: list[int] = []
    over_60: list[int] = []

    for position, entry in enumerate(entries):
        where = f"entries[{position}]"
        if not isinstance(entry, dict):
            errors.append(f"{where}: expected an object")
            continue

        index = entry.get("index")
        resource_id = entry.get("id")
        name_ru = entry.get("name_ru")
        category = entry.get("category")
        cell_rect = entry.get("cell")
        trim = entry.get("trim")

        if not isinstance(index, int) or not 0 <= index < COUNT:
            errors.append(f"{where}.index: expected 0..{COUNT - 1}, got {index!r}")
            continue
        if index in seen_indices:
            errors.append(f"{where}.index: duplicate {index}")
        seen_indices.add(index)

        if not isinstance(resource_id, str) or not ID_RE.match(resource_id):
            errors.append(f"{where}.id: invalid snake_case id: {resource_id!r}")
        elif resource_id in seen_ids:
            errors.append(f"{where}.id: duplicate {resource_id}")
        else:
            seen_ids.add(resource_id)

        if not isinstance(name_ru, str) or not name_ru.strip():
            errors.append(f"{where}.name_ru: empty")
        if not isinstance(category, str) or not category.strip():
            errors.append(f"{where}.category: empty")

        if not isinstance(cell_rect, dict):
            errors.append(f"{where}.cell: expected an object")
            continue
        try:
            cx, cy, cw, ch = rect_key(cell_rect)
        except KeyError:
            errors.append(f"{where}.cell: expected x/y/w/h")
            continue
        expected_x = (index % columns) * cell
        expected_y = (index // columns) * cell
        if (cx, cy, cw, ch) != (expected_x, expected_y, cell, cell):
            errors.append(
                f"{where}.cell: {(cx, cy, cw, ch)} != {(expected_x, expected_y, cell, cell)}"
            )
        if cx < 0 or cy < 0 or cx + cw > width or cy + ch > height:
            errors.append(f"{where}.cell: outside atlas")
        rects.append((cx, cy, cw, ch))

        if not isinstance(trim, dict):
            errors.append(f"{where}.trim: expected an object")
            continue
        try:
            tx, ty, tw, th = rect_key(trim)
        except KeyError:
            errors.append(f"{where}.trim: expected x/y/w/h")
            continue
        if tw <= 0 or th <= 0:
            errors.append(f"{where}.trim: empty")
            continue
        if tx < 0 or ty < 0 or tx + tw > cell or ty + th > cell:
            errors.append(f"{where}.trim: {(tx, ty, tw, th)} outside cell 0..{cell}")
        if tx <= 1 or ty <= 1 or tx + tw >= cell - 1 or ty + th >= cell - 1:
            edge_touch.append(index)
        if tw > 60 or th > 60:
            over_60.append(index)

        if atlas_image is None:
            continue
        tile = atlas_image.crop((cx, cy, cx + cell, cy + cell))
        bbox = tile.getchannel("A").getbbox()
        if bbox is None:
            errors.append(f"{where}: cell is transparent")
            continue
        actual = (bbox[0], bbox[1], bbox[2] - bbox[0], bbox[3] - bbox[1])
        if (tx, ty, tw, th) != actual:
            errors.append(f"{where}.trim: {(tx, ty, tw, th)} != actual {actual}")

    expected_indices = set(range(COUNT))
    if seen_indices != expected_indices:
        missing = sorted(expected_indices - seen_indices)
        extra = sorted(seen_indices - expected_indices)
        if missing:
            errors.append(f"entries: missing indices {missing}")
        if extra:
            errors.append(f"entries: unexpected indices {extra}")

    for i in range(len(rects)):
        for j in range(i + 1, len(rects)):
            ax, ay, aw, ah = rects[i]
            bx, by, bw, bh = rects[j]
            if ax < bx + bw and bx < ax + aw and ay < by + bh and by < ay + ah:
                errors.append(f"entries: overlapping cells at positions {i} and {j}")

    if edge_touch:
        warnings.append(f"edge-touch ({len(edge_touch)}): {edge_touch}")
    if over_60:
        warnings.append(f"over-60 ({len(over_60)}): {over_60}")

    for message in warnings:
        print(f" WARN {message}")
    for message in errors:
        print(f" ERROR {message}")

    if errors:
        print(f"FAIL: {len(errors)} error(s), {len(warnings)} warning(s)")
        return 1
    if args.strict and warnings:
        print(f"FAIL: {len(warnings)} warning(s) under --strict")
        return 1

    print(
        f"OK: {len(entries)} entries, atlas {width}x{height}, "
        f"{columns}x{rows} cells of {cell}px, {len(warnings)} warning(s)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())