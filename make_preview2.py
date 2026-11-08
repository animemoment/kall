from PIL import Image
import json
import os

atlas = Image.open(r"shots\resources_ground_view.png")
data = json.load(open(r"shots\resources_ground_view.json", encoding="utf-8"))

# Отдельное превью по 8 категорий x 10 ячеек = 80 + 2 для утилиты
CATEG_GROUPS = [
    ("еда", 20),
    ("руда", 23),
    ("энергия", 6),
    ("химия", 12),
    ("камень", 6),
    ("заполнитель", 5),
    ("лес", 6),
    ("волокно", 16),
]

W, H = 8 * 132, 3 * 132  # 8 категорий, по 3 ряда
preview = Image.new("RGBA", (W, H), (255, 255, 255, 255))  # белый фон для контраста
y = 0
for cat, count in CATEG_GROUPS:
    x = 0
    items = [e for e in data["entries"] if e["category"] == cat]
    for i, entry in enumerate(items):
        cx, cy, cw, ch = entry["cell"]["x"], entry["cell"]["y"], entry["cell"]["w"], entry["cell"]["h"]
        cell = atlas.crop((cx, cy, cx+cw, cy+ch)).convert("RGBA")
        cell = cell.resize((128, 128), Image.LANCZOS)
        preview.paste(cell, (x, y), cell)
        x += 132
        if x >= 7 * 132:
            x = 0
            y += 132
    # Заголовок категории
    from PIL import ImageDraw
    draw = ImageDraw.Draw(preview)
    draw.rectangle([0, y-10, 6*132, y-2], fill=(60, 60, 60, 200))
    draw.text((5, y-8), cat, fill=(255, 255, 255, 255))
    y += 40

preview.save(r"shots\preview_categories.png", "PNG")
print("Saved preview_categories.png", preview.size)
