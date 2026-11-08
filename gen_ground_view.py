"""Генератор процедурного атласа вида на земле (90 ресурсов, 64x64, сетка 16x6)."""
from PIL import Image, ImageDraw
import random, json

random.seed(42)


def lerp(c1, c2, t):
    return (int(c1[0] + (c2[0] - c1[0]) * t), int(c1[1] + (c2[1] - c1[1]) * t), int(c1[2] + (c2[2] - c1[2]) * t))


def draw_bags(cx, cy, draw, r, base, mid, dark, n=5, size=7, gap=11):
    for i in range(n):
        ang = 2 * 3.14159265 * i / n
        x = cx + gap * (0.8 + 0.3 * r.random()) * (1 if i % 2 == 0 else -1)
        y = cy + gap * (0.8 + 0.3 * r.random()) * (1 if i % 2 == 1 else -1)
        s = size + 1 if r.random() < 0.3 else size
        draw.ellipse([x - s, y - s, x + s, y + s], fill=mid if r.random() < 0.5 else dark)
    draw.ellipse([cx - size*1.2, cy - size*1.2, cx + size*1.2, cy + size*1.2], fill=base)


def draw_wood(cx, cy, draw, r, base, mid, dark):
    ang = r.randint(0, 360)
    rad = 3.14159265 * ang / 180
    ax = cx + 13 * r.cos(rad); ay = cy + 13 * r.sin(rad)
    bx = cx - 13 * r.cos(rad); by = cy - 13 * r.sin(rad)
    draw.line([(ax - 3, ay), (bx - 3, by)], fill=dark, width=7, joint="curve")
    draw.line([(ax, ay), (bx, by)], fill=mid, width=7, joint="curve")
    draw.line([(ax + 3, ay), (bx + 3, by)], fill=base, width=7, joint="curve")
    for ex, ey in ((ax, ay), (bx, by)):
        draw.ellipse([ex - 4, ey - 4, ex + 4, ey + 4], fill=lerp(base, dark, 0.4))
        draw.ellipse([ex - 2.5, ey - 2.5, ex + 2.5, ey + 2.5], fill=lerp(base, dark, 0.6))


def draw_crystal(cx, cy, draw, r, base, mid, dark, n=4, size=9):
    pts = [(cx + size * r.cos(2 * 3.14159265 * i / n), cy + size * r.sin(2 * 3.14159265 * i / n)) for i in range(n)]
    draw.polygon(pts, fill=mid)
    for i in range(n):
        draw.line([pts[i], pts[(i + 2) % n]], fill=dark, width=1)
    draw.polygon(pts[:2], fill=(255, 255, 255, 150))


def draw_stone(cx, cy, draw, r, base, mid, dark, n=4):
    for _ in range(n):
        np_ = r.randint(5, 7)
        pts = []
        for i in range(np_):
            ang = i * 2 * 3.14159265 / np_
            rad = 6 + 4 * r.random()
            pts.append((cx + rad * r.cos(ang + r.random()), cy + rad * r.sin(ang + r.random())))
        draw.polygon(pts, fill=mid)
        draw.line(pts, fill=dark, width=1)


def draw_grain(cx, cy, draw, r, base, mid, dark, n=16):
    for _ in range(n):
        x = cx + 12 * r.cos(2 * 3.14159265 * r.random())
        y = cy + 12 * r.sin(2 * 3.14159265 * r.random())
        s = max(3 + 2 * r.random(), 2 + 2 * r.random())
        draw.ellipse([x - s, y - s, x + s, y + s], fill=lerp(base, mid, r.random()))


def draw_mound(cx, cy, draw, r, base, mid, dark, n=7):
    for i in range(n):
        x = cx + 8 * r.cos(2 * 3.14159265 * i / n)
        y = cy + 6 * r.sin(2 * 3.14159265 * i / n)
        s = 4 + 3 * r.random()
        draw.ellipse([x - s, y - s, x + s, y + s], fill=mid if r.random() < 0.6 else dark)


def draw_ball(cx, cy, draw, r, base, mid, dark):
    for rad, fill in [(11, dark), (8, mid), (5, base), (3, mid)]:
        draw.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=fill)
    for _ in range(6):
        ang = r.random() * 3.14159265
        draw.line([(cx + 11 * r.cos(ang), cy + 11 * r.sin(ang)), (cx - 11 * r.cos(ang), cy - 11 * r.sin(ang))], fill=lerp(base, mid, r.random()), width=2)


def draw_liquid(cx, cy, draw, r, base, mid, dark):
    draw.ellipse([cx - 15, cy - 8, cx + 15, cy + 8], fill=mid, outline=dark, width=2)
    draw.ellipse([cx - 10, cy - 5, cx + 10, cy + 5], fill=base)
    draw.ellipse([cx - 5, cy - 3, cx + 4, cy + 2], fill=(255, 255, 255, 200))


def draw_cell(index, entry):
    id_, category, name = entry
    base, mid, dark = PALETTES[category][index % len(PALETTES[category])]
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    r = random.Random(index + 13)
    cx, cy = 32, 32
    draw.rectangle([1, 1, 62, 62], outline=(110, 110, 110, 100), width=1)

    if "вода" in id_ or "масл" in id_ or "латекс" in id_:
        draw_liquid(cx, cy, draw, r, base, mid, dark)
    elif "уго" in id_ or "неф" in id_ or "газ" in id_:
        draw_mound(cx, cy, draw, r, base, mid, dark, n=6)
    elif "зерн" in id_ or "семя" in id_ or "рис" in id_ or "пшени" in id_ or "овёс" in id_ or "ячм" in id_ or "рож" in id_ or "просо" in id_ or "кукуруза" in id_ or "подсол" in id_ or "рапс" in id_:
        draw_grain(cx, cy, draw, r, base, mid, dark, n=18)
    elif "картоф" in id_ or "свёкл" in id_ or "горох" in id_ or "боб" in id_ or "бакла" in id_:
        draw_bags(cx, cy, draw, r, base, mid, dark, n=6, size=9, gap=13)
    elif "гранит" in id_ or "известняк" in id_ or "мрамор" in id_ or "сланц" in id_:
        draw_stone(cx, cy, draw, r, base, mid, dark, n=3)
    elif "базальт" in id_:
        draw_stone(cx, cy, draw, r, base, mid, dark, n=2)
        draw.line([(cx - 20, cy + 10), (cx - 10, cy + 6), (cx, cy + 12), (cx + 10, cy + 8), (cx + 20, cy + 11)], fill=dark, width=2)
    elif "песч" in id_ or "песк" in id_ or "грав" in id_ or "глин" in id_ or "мергел" in id_:
        draw_grain(cx, cy, draw, r, base, mid, dark, n=14)
    elif ("руда" in id_ or "купорос" in id_ or "гален" in id_ or "сфалер" in id_ or "касситер" in id_
          or "вольфра" in id_ or "молибд" in id_ or "ванад" in id_ or "никел" in id_ or "кобал" in id_
          or "ильмен" in id_ or "боксит" in id_ or "киновар" in id_ or "пирит" in id_ or "золот" in id_
          or "серебр" in id_ or "платин" in id_ or "урани" in id_ or "монацит" in id_ or "марган" in id_ or "хро" in id_):
        draw_crystal(cx, cy, draw, r, base, mid, dark, n=4, size=10)
    elif "соль" in id_ or "поташ" in id_ or "фосфорит" in id_ or "нитрат" in id_ or "гипс" in id_ or "асбест" in id_ or "графит" in id_ or "каолин" in id_ or "магнезит" in id_ or "флюорит" in id_ or "бура" in id_:
        draw_crystal(cx, cy, draw, r, base, mid, dark, n=3, size=8)
    elif "дере" in id_ or "кор" in id_ or "смол" in id_ or "гутта" in id_:
        draw_wood(cx, cy, draw, r, base, mid, dark)
    elif "хлоп" in id_ or "лён" in id_ or "шерст" in id_ or "шёлк" in id_ or "джут" in id_ or "коноп" in id_ or "вощ" in id_ or "жир" in id_ or "кожа" in id_ or "кость" in id_:
        draw_ball(cx, cy, draw, r, base, mid, dark)
    elif "зерно" in id_ or "овощ" in id_ or "мясо" in id_ or "птиц" in id_ or "рыб" in id_ or "молок" in id_ or "яйц" in id_ or "ткан" in id_ or "волок" in id_:
        draw_bags(cx, cy, draw, r, base, mid, dark, n=4, size=8, gap=12)
    else:
        draw_bags(cx, cy, draw, r, base, mid, dark, n=4, size=8, gap=12)

    draw.ellipse([16, 48, 48, 56], fill=(0, 0, 0, 50))
    alpha = img.getchannel("A")
    bbox = alpha.getbbox() or (32, 32, 32, 32)
    return img, tuple(bbox)


RAW = [
    ("wheat", "еда", "пшеница"), ("rye", "еда", "рожь"), ("barley", "еда", "ячмень"), ("oats", "еда", "овёс"),
    ("maize", "еда", "кукуруза"), ("rice", "еда", "рис"), ("potato", "еда", "картофель"), ("sugar_beet", "еда", "свёкла"),
    ("sugar_cane", "еда", "сахарный тростник"), ("peas", "еда", "горох"), ("beans", "еда", "бобы"), ("sunflower", "еда", "подсолнечник"),
    ("rapeseed", "еда", "рапс"), ("flax_seed", "еда", "лён"), ("milk", "еда", "молоко"), ("eggs", "еда", "яйца"),
    ("beef", "еда", "говядина"), ("pork", "еда", "свинина"), ("poultry", "еда", "птица"), ("fish_sea", "еда", "рыба морская"),
    ("iron_hematite", "руда", "железная руда"), ("iron_magnetite", "руда", "магнитный железняк"), ("manganese_ore", "руда", "марганцевая руда"),
    ("chromite", "руда", "хромит"), ("tungsten_wolframite", "руда", "вольфрамит"), ("molybdenum_ore", "руда", "молибденовая руда"),
    ("vanadium_ore", "руда", "ванадиевая руда"), ("nickel_ore", "руда", "никелевая руда"), ("cobalt_ore", "руда", "кобальтовая руда"),
    ("titanium_ilmenite", "руда", "ильменит"), ("copper_chalcopyrite", "руда", "халькозин"), ("copper_oxide", "руда", "медный купорос"),
    ("lead_galena", "руда", "галенит"), ("zinc_sphalerite", "руда", "сфалерит"), ("tin_cassiterite", "руда", "касситерит"), ("bauxite", "руда", "боксит"),
    ("mercury_cinnabar", "руда", "киноварь"), ("pyrite", "руда", "пирит"), ("gold_ore", "руда", "золотая руда"), ("silver_ore", "руда", "серебряная руда"),
    ("platinum_ore", "руда", "платиновая руда"), ("uranium_pitchblende", "руда", "насмывник"), ("monazite", "руда", "монацит"),
    ("bituminous_coal", "энергия", "битуминозный уголь"), ("anthracite", "энергия", "антрацит"), ("coking_coal", "энергия", "коксующийся уголь"),
    ("oil_light_paraffinic", "энергия", "лёгкая нефть"), ("oil_heavy_sour", "энергия", "тяжёлая сернистая нефть"), ("natural_gas_dry", "энергия", "сухой газ"),
    ("sulfur", "химия", "сера"), ("rock_salt", "химия", "каменная соль"), ("potash", "химия", "поташ"), ("phosphate_rock", "химия", "фосфорит"),
    ("nitrate_chile", "химия", "нитрат чилийский"), ("gypsum", "химия", "гипс"), ("asbestos", "химия", "асбест"), ("graphite", "химия", "графит"),
    ("kaolin", "химия", "каолин"), ("magnesite", "химия", "магнезит"), ("fluorite", "химия", "флюорит"), ("borax", "химия", "бура"),
    ("stone", "камень", "камень"), ("granite", "камень", "гранит"), ("limestone", "камень", "известняк"), ("marble", "камень", "мрамор"),
    ("basalt", "камень", "базальт"), ("slate", "камень", "сланец"),
    ("sand", "заполнитель", "песок"), ("gravel", "заполнитель", "гравий"), ("clay", "заполнитель", "глина"), ("fireclay", "заполнитель", "огнеупорная глина"), ("marl", "заполнитель", "мергель"),
    ("wood_softwood", "лес", "мягкая древесина"), ("wood_hardwood", "лес", "твёрдая древесина"), ("cork_bark", "лес", "пробка"),
    ("resin", "лес", "смола"), ("rubber_latex", "лес", "каучуковый латекс"), ("gutta_percha", "лес", "гуттаперча"),
    ("cotton", "волокно", "хлопок"), ("flax_fiber", "волокно", "льняное волокно"), ("hemp_fiber", "волокно", "конопляное волокно"),
    ("jute", "волокно", "джут"), ("wool", "волокно", "шерсть"), ("silk", "волокно", "шёлк"), ("hides", "волокно", "шкура"),
    ("bone", "волокно", "кость"), ("beeswax", "волокно", "пчелиный воск"), ("tallow", "волокно", "жир животный"),
    ("water", "утилита", "вода"), ("ice", "утилита", "лёд"),
]

PALETTES = {
    "еда": [
        ((154, 187, 70), (187, 218, 80), (130, 162, 40)), ((230, 201, 40), (245, 218, 45), (210, 180, 30)),
        ((186, 110, 110), (210, 140, 140), (160, 90, 90)), ((218, 165, 32), (238, 185, 50), (198, 145, 20)),
        ((96, 160, 96), (120, 180, 120), (76, 130, 76)), ((178, 34, 34), (205, 50, 50), (145, 25, 25)),
        ((210, 180, 140), (225, 195, 155), (185, 155, 120)), ((106, 90, 205), (130, 110, 225), (86, 70, 170)),
        ((150, 100, 60), (180, 130, 80), (120, 80, 40)), ((235, 200, 160), (250, 220, 180), (210, 170, 130)),
    ],
    "руда": [
        ((128, 128, 128), (160, 160, 160), (100, 100, 100)), ((139, 69, 19), (160, 90, 30), (110, 50, 10)),
        ((95, 158, 160), (115, 180, 185), (75, 130, 130)), ((60, 60, 60), (80, 80, 80), (45, 45, 45)),
        ((218, 165, 32), (238, 185, 50), (170, 120, 15)), ((255, 250, 240), (255, 255, 255), (230, 230, 230)),
        ((138, 43, 226), (160, 60, 245), (110, 30, 190)), ((178, 34, 34), (205, 50, 50), (145, 25, 25)),
        ((64, 164, 211), (90, 185, 230), (50, 140, 190)), ((176, 196, 222), (200, 218, 240), (150, 170, 200)),
        ((95, 158, 160), (120, 190, 195), (75, 130, 130)),
    ],
    "энергия": [
        ((40, 40, 40), (60, 60, 60), (25, 25, 25)), ((80, 80, 80), (100, 100, 100), (60, 60, 60)),
        ((20, 20, 20), (35, 35, 35), (10, 10, 10)), ((30, 30, 25), (50, 50, 40), (20, 20, 15)),
        ((90, 90, 110), (110, 110, 130), (70, 70, 90)), ((20, 20, 20), (40, 40, 35), (10, 10, 8)),
    ],
    "химия": [
        ((255, 215, 0), (255, 235, 85), (215, 180, 0)), ((245, 245, 245), (255, 255, 255), (225, 225, 225)),
        ((205, 133, 63), (225, 155, 90), (175, 110, 40)), ((147, 112, 219), (170, 135, 235), (120, 90, 185)),
        ((60, 179, 113), (85, 200, 135), (45, 150, 90)), ((245, 245, 220), (255, 255, 240), (225, 225, 200)),
        ((192, 192, 192), (210, 210, 210), (170, 170, 170)), ((30, 30, 30), (50, 50, 50), (20, 20, 20)),
        ((255, 230, 200), (255, 245, 220), (230, 210, 170)), ((160, 180, 210), (200, 220, 240), (130, 150, 180)),
    ],
    "камень": [
        ((160, 160, 160), (185, 185, 185), (140, 140, 140)), ((119, 136, 153), (145, 160, 180), (100, 115, 135)),
        ((229, 228, 226), (240, 240, 238), (210, 208, 206)), ((253, 251, 252), (255, 255, 255), (243, 243, 243)),
        ((66, 66, 66), (85, 85, 85), (50, 50, 50)), ((70, 75, 80), (90, 95, 100), (55, 60, 65)),
    ],
    "заполнитель": [
        ((222, 184, 135), (238, 200, 150), (198, 160, 110)), ((128, 128, 128), (150, 150, 150), (110, 110, 110)),
        ((178, 130, 80), (195, 150, 100), (155, 115, 70)), ((155, 120, 70), (175, 140, 90), (135, 105, 55)),
        ((165, 145, 125), (185, 165, 145), (145, 125, 105)),
    ],
    "лес": [
        ((139, 90, 43), (160, 110, 60), (115, 75, 30)), ((101, 67, 33), (125, 85, 50), (85, 55, 20)),
        ((210, 180, 140), (230, 200, 160), (185, 155, 110)), ((92, 118, 46), (110, 140, 60), (75, 100, 35)),
        ((255, 250, 240), (255, 255, 255), (240, 245, 235)), ((110, 80, 40), (130, 100, 60), (90, 60, 25)),
    ],
    "волокно": [
        ((255, 253, 208), (255, 255, 225), (240, 245, 190)), ((218, 218, 196), (235, 235, 215), (195, 195, 175)),
        ((210, 180, 140), (230, 200, 160), (185, 155, 110)), ((230, 210, 170), (245, 225, 190), (205, 180, 140)),
        ((255, 250, 240), (255, 255, 255), (245, 245, 235)), ((255, 253, 235), (255, 255, 245), (240, 248, 230)),
        ((128, 90, 60), (150, 110, 80), (110, 75, 45)), ((245, 245, 240), (255, 255, 255), (235, 235, 230)),
    ],
    "утилита": [
        ((70, 130, 180), (100, 160, 210), (50, 110, 160)), ((225, 245, 254), (240, 255, 255), (200, 235, 250)),
    ],
}


def main():
    WIDTH, HEIGHT = 1024, 384
    COLS, ROWS = 16, 6
    CELL = 64

    atlas = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    entries_out = []

    for index, entry in enumerate(RAW):
        col = index % COLS
        row = index // COLS
        x, y = col * CELL, row * CELL

        cell_img, trim = draw_cell(index, entry)
        atlas.paste(cell_img, (x, y), cell_img)

        entries_out.append({
            "index": index,
            "id": entry[0],
            "name_ru": entry[2],
            "category": entry[1],
            "cell": {"x": x, "y": y, "w": CELL, "h": CELL},
            "trim": {"x": trim[0], "y": trim[1], "w": trim[2] - trim[0], "h": trim[3] - trim[1]},
        })

    atlas.save(r"shots\resources_ground_view.png", "PNG")
    print(f"Saved atlas: shots/resources_ground_view.png ({WIDTH}x{HEIGHT}, {len(RAW)} entries)")

    atlas_json = {
        "version": 1,
        "atlas": {
            "path": "res://shots/resources_ground_view.png",
            "width": WIDTH,
            "height": HEIGHT,
            "cell": CELL,
            "columns": COLS,
            "rows": ROWS,
        },
        "entries": entries_out,
    }
    with open(r"shots\resources_ground_view.json", "w", encoding="utf-8") as f:
        json.dump(atlas_json, f, ensure_ascii=False, indent=1)
    print("Saved atlas json: shots/resources_ground_view.json")

    colors_total = 0
    empty_cells = 0
    for entry in entries_out:
        x, y, w, h = entry["cell"]["x"], entry["cell"]["y"], entry["cell"]["w"], entry["cell"]["h"]
        cell_img = atlas.crop((x, y, x + w, y + h)).convert("RGBA")
        colors_total += len(cell_img.convert("RGB").getcolors(maxcolors=100000) or [])
        alpha = cell_img.getchannel("A")
        non_alpha = sum(v for k, v in alpha.getcolors(maxcolors=100000) or [])
        if non_alpha < 20:
            empty_cells += 1
    print(f"Average colors per cell: {colors_total / len(entries_out):.1f}")
    print(f"Empty-ish cells (alpha < 20): {empty_cells}")

    from collections import Counter
    counts = Counter(e["category"] for e in entries_out)
    for cat, n in sorted(counts.items()):
        print(f"  {cat}: {n}")


if __name__ == "__main__":
    main()
