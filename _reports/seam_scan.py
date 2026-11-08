"""Поиск «швов» на скриншоте: резкие 1-px линии, характерные для стыков чанков.

Идея: для каждой пары соседних столбцов (строк) считаем среднюю абсолютную разницу.
Полностью равномерный участок даёт маленькую дельту; шов — выброс на 1-2 столбца.
Печатаем топ-12 выбросов с их координатами и во сколько раз они больше медианы.
"""
import sys
from PIL import Image
import numpy as np

path = sys.argv[1]
img = Image.open(path).convert("RGB")
a = np.asarray(img, dtype=np.int16)
h, w, _ = a.shape

# Обрезаем HUD по краям, чтобы не ловить панели интерфейса.
# Верхняя панель (кнопки времени, день) занимает y<60 — иначе она даёт ложные выбросы.
x0, x1 = 420, 1500
y0, y1 = 70, 900
core = a[y0:y1, x0:x1]

col_delta = np.abs(np.diff(core, axis=1)).mean(axis=(0, 2))
row_delta = np.abs(np.diff(core, axis=0)).mean(axis=(1, 2))

def report(name, arr, label):
    med = float(np.median(arr)) or 1e-6
    idx = np.argsort(arr)[-12:][::-1]
    print(f"--- {name} (медиана {med:.4f}) ---")
    for i in idx:
        ratio = arr[i] / med
        if ratio < 1.6:
            break
        print(f"  {label}={i + (x0 if label == 'x' else y0):5d}  delta={arr[i]:7.4f}  x{ratio:5.2f} от медианы")

report("вертикальные линии (стыки по X)", col_delta, "x")
report("горизонтальные линии (стыки по Y)", row_delta, "y")
print(f"size={w}x{h}")
