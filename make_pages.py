from PIL import Image
import json

atlas = Image.open(r"shots\resources_ground_view.png")
data = json.load(open(r"shots\resources_ground_view.json", encoding="utf-8"))

# Разбиваем на страницы по 10 ячеек (10x9 grid)
PAGE_W = 10
PAGE_H = 9
CELL = 64

for page in range(0, 9):
    page_img = Image.new("RGBA", (10*CELL, 9*CELL), (0, 0, 0, 0))
    for i in range(90):
        page_idx = page * 10 + i
        if page_idx >= len(data["entries"]):
            break
        entry = data["entries"][page_idx]
        x, y, w, h = entry["cell"]["x"], entry["cell"]["y"], entry["cell"]["w"], entry["cell"]["h"]
        cell = atlas.crop((x, y, x+w, y+h))
        px = i % PAGE_W
        py = i // PAGE_W
        page_img.paste(cell, (px*CELL, py*CELL), cell)
    page_img.save(f"shots/ground_page_{page+1:02d}.png", "PNG")
    print(f"page_{page+1:02d}.png saved")
