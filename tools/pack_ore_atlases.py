#!/usr/bin/env python3
"""Atlas builder for ore47 textures and workstations.
Packages 3,266 individual ore PNGs into compact, high-performance atlases.
"""
import os
import sys
import glob
from pathlib import Path
from PIL import Image

WORKSPACE_ROOT = Path("d:/bebebe-main (1)")
SPRITES_DIR = WORKSPACE_ROOT / "_sprites"
ORE_DIR = SPRITES_DIR / "out" / "world" / "ore47"
OUT_ATLAS_DIR = SPRITES_DIR / "out" / "atlases"

# Wang-tile grid layout: 8 columns x 6 rows = 48 cells (47 tiles t0..t46 used)
GRID_COLS = 8
GRID_ROWS = 6
TILE_SIZE = 64

def pack_ore_state(ore_name: str, state: str) -> Path:
    state_dir = ORE_DIR / ore_name / state
    if not state_dir.exists():
        return None
    
    atlas = Image.new("RGBA", (GRID_COLS * TILE_SIZE, GRID_ROWS * TILE_SIZE), (0, 0, 0, 0))
    tiles_found = 0
    
    for t_idx in range(47):
        tile_file = state_dir / f"t{t_idx}.png"
        if tile_file.exists():
            col = t_idx % GRID_COLS
            row = t_idx // GRID_COLS
            with Image.open(tile_file) as tile_img:
                tile_rgba = tile_img.convert("RGBA")
                if tile_rgba.size != (TILE_SIZE, TILE_SIZE):
                    tile_rgba = tile_rgba.resize((TILE_SIZE, TILE_SIZE), Image.NEAREST)
                atlas.paste(tile_rgba, (col * TILE_SIZE, row * TILE_SIZE))
                tiles_found += 1
                
    if tiles_found == 0:
        return None
        
    out_file = OUT_ATLAS_DIR / f"ore_{ore_name}_{state}.png"
    out_file.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out_file, "PNG", optimize=True)
    return out_file

def main():
    if not ORE_DIR.exists():
        print(f"Error: {ORE_DIR} not found")
        return 1
        
    ores = [d.name for d in ORE_DIR.iterdir() if d.is_dir()]
    print(f"Found {len(ores)} ore types to atlas.")
    
    states = ["rich", "normal", "poor"]
    packed = 0
    
    for ore in ores:
        for st in states:
            res = pack_ore_state(ore, st)
            if res:
                packed += 1
                
    print(f"Successfully packed {packed} atlases into {OUT_ATLAS_DIR}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
