from PIL import Image
import json

atlas = Image.open(r"shots\resources_ground_view.png")
data = json.load(open(r"shots\resources_ground_view.json", encoding="utf-8"))

# Создаю одно большое превью 30x30 с масштабированием 2x (640x640)
preview = Image.new("RGBA", (30*64, 30*64), (0, 0, 0, 0))
for i, entry in enumerate(data["entries"][:900]):
    x, y, w, h = entry["cell"]["x"], entry["cell"]["y"], entry["cell"]["w"], entry["cell"]["h"]
    cell = atlas.crop((x, y, x+w, y+h)).convert("RGBA")
    # Масштабирую 2x для лучшей читаемости
    cell = cell.resize((128, 128), Image.LANCZOS)
    px = i % 30
    py = i // 30
    preview.paste(cell, (px*128, py*128), cell)
preview.save(r"shots\preview_full_30x30.png", "PNG")

# Отдельно первые 16 ячеек крупно (еда)
preview1 = Image.new("RGBA", (4*128, 4*128), (0, 0, 0, 0))
for i in range(16):
    entry = data["entries"][i]
    x, y, w, h = entry["cell"]["x"], entry["cell"]["y"], entry["cell"]["w"], entry["cell"]["h"]
    cell = atlas.crop((x, y, x+w, y+h)).convert("RGBA")
    cell = cell.resize((128, 128), Image.LANCZOS)
    px = i % 4
    py = i // 4
    preview1.paste(cell, (px*128, py*128), cell)
preview1.save(r"shots\preview_first16.png", "PNG")

# Статистика по trim
import statistics
trims = [(e["trim"]["w"], e["trim"]["h"]) for e in data["entries"]]
trims = [(e["trim"]["w"], e["trim"]["h"]) for e in data["entries"]]
print("Trim sizes median:", statistics.median([t[0] for t in trims]), "x", statistics.median([t[1] for t in trims]))
