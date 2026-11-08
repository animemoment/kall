using System;
using System.Collections.Generic;
using Godot;
using Game.Simulation;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 19-01: `FarmZoneRenderer._Draw` рисовал зону ПОШТУЧНО — до 4 `DrawLine` + `DrawRect`
/// на клетку при каждом redraw (троттлинг 0.1 с). На ферме 200×200 это 40 000 `DrawRect`
/// и до 160 000 `DrawLine` за кадр, то есть фризы &gt; 100 мс, пока зона выбрана или под
/// курсором. Игрок видел: навёл курсор на большую ферму — игра дёргается/замирает на кадр,
/// повторяюще.
///
/// Фикс: геометрия зоны считается один раз на набор тайлов и кэшируется. Заливка —
/// максимальными прямоугольниками (склейка пробегов по строкам и по столбцам), контур —
/// склеенными отрезками по внешним граням. Число примитивов перестаёт зависеть от площади.
///
/// Что проверяется (headless):
///  1. Заливка прямоугольной фермы 200×200 — один `Rect2` вместо 40 000.
///  2. Покрытие заливки совпадает с набором тайлов клетка в клетку (нет дыр, нет наложений);
///     проверено на прямоугольнике, L-форме, «кольце» с дыркой, двух несвязных кусках,
///     одной клетке и клетках с отрицательными координатами.
///  3. Контур прямоугольника 200×200 — 4 отрезка (периметр склеен), длина совпадает.
///  4. Контур покрывает ровно внешние грани, каждую ровно один раз (на тех же фигурах).
///  5. Кэш возвращает готовую геометрию на повторный вызов и пересобирает её, когда
///     ZoneManager заменил набор тайлов (copy-on-write); удалённые зоны выбрасываются.
///  6. Источник правки (сам баг визуальный, фриз в headless не измерить): в `_Draw` нет
///     обхода клеток и построения геометрии — только чтение кэша и отрисовка готовых
///     списков; `ZoneManager` не меняет набор тайлов на месте (иначе кэш протух бы).
///
/// Запуск: godot --headless --path . --script res://tests/run_farm_zone_renderer.gd
/// </summary>
public partial class TestFarmZoneRenderer : RefCounted
{
    private const int Tile = 64; // = FarmZoneRenderer.TileSize

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[FZR] 19-01: зона рисуется пачкой примитивов, а не по клетке");
        GD.Print("==================================================");

        bool ok = true;
        ok &= FillIsBatched();
        ok &= FillCoversExactlyTiles();
        ok &= BordersAreMerged();
        ok &= BordersCoverExactlyOuterEdges();
        ok &= GeometryIsCached();
        ok &= DrawUsesCachedGeometry();

        GD.Print("[FZR] примечание: фриз кадра в headless не измеряется — проверены " +
                 "счётчики примитивов и источник правки (см. п.6 в шапке)");
        GD.Print(ok ? "[FZR] PASS" : "[FZR] FAIL");
        return ok;
    }

    // ------------------------------------------------------------------ 1. батчинг заливки

    private static bool FillIsBatched()
    {
        var tiles = MakeRect(0, 0, 200, 200); // 40 000 клеток — пример из аудита
        var rects = new List<Rect2>();
        FarmZoneRenderer.BuildFillRects(tiles, rects);

        if (tiles.Count != 40000 || rects.Count != 1)
        {
            GD.PrintErr($"[FZR] FAIL: заливка 200×200 не схлопнулась в один Rect2 " +
                        $"(клеток {tiles.Count}, прямоугольников {rects.Count})");
            return false;
        }
        var only = rects[0];
        if (!IsTileAligned(only) || Math.Abs(only.Size.X - 200 * Tile) > 0.01f ||
            Math.Abs(only.Size.Y - 200 * Tile) > 0.01f)
        {
            GD.PrintErr($"[FZR] FAIL: прямоугольник заливки не покрывает зону: {only}");
            return false;
        }

        // Форма «два этажа»: 4×2 плюс 2×2 сверху — прямоугольников должно быть мало,
        // а не по числу клеток.
        var ell = MakeRect(0, 0, 4, 2);
        foreach (var t in MakeRect(0, 2, 2, 2))
            ell.Add(t);
        var ellRects = new List<Rect2>();
        FarmZoneRenderer.BuildFillRects(ell, ellRects);
        if (ellRects.Count >= ell.Count)
        {
            GD.PrintErr($"[FZR] FAIL: L-форма не склеена (клеток {ell.Count}, " +
                        $"прямоугольников {ellRects.Count})");
            return false;
        }

        GD.Print($"[FZR] OK: заливка: 1 прямоугольник вместо 40000 DrawRect (200×200), " +
                 $"L-форма {ell.Count} клеток → {ellRects.Count} прямоугольника");
        return true;
    }

    // ------------------------------------------------- 2. заливка = ровно набор тайлов

    private static bool FillCoversExactlyTiles()
    {
        var shapes = new (string Name, HashSet<(int X, int Y)> Tiles)[]
        {
            ("одна клетка", MakeRect(7, 7, 1, 1)),
            ("блок 3×3", MakeRect(0, 0, 3, 3)),
            ("L-форма", LShape()),
            ("кольцо с дыркой", Ring()),
            ("два куска", TwoPieces()),
            ("отрицательные координаты", MakeRect(-5, -3, 4, 3)),
        };

        bool ok = true;
        foreach (var (name, tiles) in shapes)
        {
            var rects = new List<Rect2>();
            FarmZoneRenderer.BuildFillRects(tiles, rects);
            if (!CheckCoverage(name, tiles, rects))
                ok = false;
        }

        // Пустой набор (Work-зона: тайлы не материализуются) — ничего не рисуем.
        var empty = new List<Rect2>();
        FarmZoneRenderer.BuildFillRects(new HashSet<(int X, int Y)>(), empty);
        if (empty.Count != 0)
        {
            GD.PrintErr($"[FZR] FAIL: пустой набор дал {empty.Count} прямоугольников");
            ok = false;
        }

        if (ok)
            GD.Print("[FZR] OK: заливка покрывает ровно клетки зоны — без дыр и наложений " +
                     "(прямоугольник, L, кольцо, два куска, отрицательные координаты)");
        return ok;
    }

    private static bool CheckCoverage(string name, HashSet<(int X, int Y)> tiles, List<Rect2> rects)
    {
        var covered = new HashSet<(int X, int Y)>();
        long area = 0;
        foreach (var r in rects)
        {
            if (!IsTileAligned(r))
            {
                GD.PrintErr($"[FZR] FAIL ({name}): прямоугольник не выровнен по сетке: {r}");
                return false;
            }
            int x0 = (int)MathF.Round(r.Position.X) / Tile;
            int y0 = (int)MathF.Round(r.Position.Y) / Tile;
            int w = (int)MathF.Round(r.Size.X) / Tile;
            int h = (int)MathF.Round(r.Size.Y) / Tile;
            if (w <= 0 || h <= 0)
            {
                GD.PrintErr($"[FZR] FAIL ({name}): пустой прямоугольник {r}");
                return false;
            }
            area += (long)w * h;
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    covered.Add((x, y));
        }

        if (area != covered.Count)
        {
            GD.PrintErr($"[FZR] FAIL ({name}): прямоугольники перекрываются " +
                        $"(площадь {area}, разных клеток {covered.Count}) — на карте были бы тёмные полосы");
            return false;
        }
        if (covered.Count != tiles.Count || !covered.SetEquals(tiles))
        {
            GD.PrintErr($"[FZR] FAIL ({name}): заливка не равна набору тайлов " +
                        $"(клеток {tiles.Count}, закрашено {covered.Count})");
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------- 3. контур склеен

    private static bool BordersAreMerged()
    {
        var tiles = MakeRect(0, 0, 200, 200);
        var lines = new List<(Vector2 From, Vector2 To)>();
        FarmZoneRenderer.BuildBorderSegments(tiles, lines);

        if (lines.Count != 4)
        {
            GD.PrintErr($"[FZR] FAIL: периметр 200×200 не склеен в 4 отрезка (получено {lines.Count})");
            return false;
        }

        float total = 0f;
        foreach (var (from, to) in lines)
            total += from.DistanceTo(to);
        float expected = 4f * 200f * Tile;
        if (Math.Abs(total - expected) > 0.5f)
        {
            GD.PrintErr($"[FZR] FAIL: длина контура {total}, ожидалось {expected}");
            return false;
        }

        GD.Print($"[FZR] OK: контур 200×200 — 4 отрезка вместо 800 (длина {total} px)");
        return true;
    }

    // ------------------------------------------- 4. контур = ровно внешние грани

    private static bool BordersCoverExactlyOuterEdges()
    {
        var shapes = new (string Name, HashSet<(int X, int Y)> Tiles)[]
        {
            ("одна клетка", MakeRect(7, 7, 1, 1)),
            ("блок 3×3", MakeRect(0, 0, 3, 3)),
            ("L-форма", LShape()),
            ("кольцо с дыркой", Ring()),
            ("два куска", TwoPieces()),
            ("отрицательные координаты", MakeRect(-5, -3, 4, 3)),
        };

        bool ok = true;
        foreach (var (name, tiles) in shapes)
        {
            var lines = new List<(Vector2 From, Vector2 To)>();
            FarmZoneRenderer.BuildBorderSegments(tiles, lines);

            var expected = OuterEdges(tiles);
            var actual = new HashSet<(int X0, int Y0, int X1, int Y1)>();
            if (!ExpandSegments(name, lines, actual))
            {
                ok = false;
                continue;
            }
            if (!actual.SetEquals(expected))
            {
                GD.PrintErr($"[FZR] FAIL ({name}): контур не совпадает с внешними гранями " +
                            $"(нарисовано {actual.Count}, нужно {expected.Count})");
                ok = false;
            }
        }

        if (ok)
            GD.Print("[FZR] OK: контур покрывает ровно внешние грани, каждую один раз " +
                     "(включая внутреннюю границу кольца)");
        return ok;
    }

    private static bool ExpandSegments(string name, List<(Vector2 From, Vector2 To)> lines,
        HashSet<(int X0, int Y0, int X1, int Y1)> edges)
    {
        foreach (var (from, to) in lines)
        {
            if (!IsTileAligned(from) || !IsTileAligned(to))
            {
                GD.PrintErr($"[FZR] FAIL ({name}): отрезок не выровнен по сетке {from} → {to}");
                return false;
            }
            int fx = (int)MathF.Round(from.X) / Tile;
            int fy = (int)MathF.Round(from.Y) / Tile;
            int tx = (int)MathF.Round(to.X) / Tile;
            int ty = (int)MathF.Round(to.Y) / Tile;

            if (fy == ty)
            {
                int x0 = Math.Min(fx, tx);
                int x1 = Math.Max(fx, tx);
                for (int x = x0; x < x1; x++)
                    if (!edges.Add((x, fy, x + 1, fy)))
                    {
                        GD.PrintErr($"[FZR] FAIL ({name}): грань ({x},{fy})-({x + 1},{fy}) нарисована дважды");
                        return false;
                    }
            }
            else if (fx == tx)
            {
                int y0 = Math.Min(fy, ty);
                int y1 = Math.Max(fy, ty);
                for (int y = y0; y < y1; y++)
                    if (!edges.Add((fx, y, fx, y + 1)))
                    {
                        GD.PrintErr($"[FZR] FAIL ({name}): грань ({fx},{y})-({fx},{y + 1}) нарисована дважды");
                        return false;
                    }
            }
            else
            {
                GD.PrintErr($"[FZR] FAIL ({name}): диагональный отрезок {from} → {to}");
                return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------------ 5. кэш

    private static bool GeometryIsCached()
    {
        var cache = new FarmZoneGeometryCache();
        var tiles = MakeRect(0, 0, 200, 200);
        var zone = new Zone(1, "тест", ZoneKind.Farm, tiles);

        var first = cache.Get(zone.Id, zone.Tiles);
        var second = cache.Get(zone.Id, zone.Tiles);
        if (!ReferenceEquals(first, second))
        {
            GD.PrintErr("[FZR] FAIL: повторный вызов пересобрал геометрию — " +
                        "значит кэша нет и redraw снова стоит O(клеток)");
            return false;
        }

        // ZoneManager меняет набор тайлов copy-on-write: при той же зоне приходит новый
        // HashSet. Кэш обязан пересобрать геометрию, иначе зона рисовалась бы по старым клеткам.
        zone.Tiles = MakeRect(0, 0, 10, 10);
        var third = cache.Get(zone.Id, zone.Tiles);
        if (third.FillRects.Count != 1 ||
            Math.Abs(third.FillRects[0].Size.X - 10 * Tile) > 0.01f ||
            Math.Abs(third.FillRects[0].Size.Y - 10 * Tile) > 0.01f)
        {
            GD.PrintErr($"[FZR] FAIL: после замены набора тайлов кэш отдал старую геометрию " +
                        $"(прямоугольников {third.FillRects.Count}, ждали один 10×10)");
            return false;
        }

        // Страховка на случай правки набора на месте (сейчас в ZoneManager её нет):
        // изменилось число клеток — кэш пересобирается, а не отдаёт старое.
        zone.Tiles.Add((50, 50));
        var fourth = cache.Get(zone.Id, zone.Tiles);
        if (fourth.FillRects.Count != 2)
        {
            GD.PrintErr($"[FZR] FAIL: правка набора на месте не замечена " +
                        $"(прямоугольников {fourth.FillRects.Count}, ждали два)");
            return false;
        }
        zone.Tiles.Remove((50, 50));

        // Снесённая зона не должна держать свой набор тайлов в кэше.
        cache.Prune(new List<Zone> { zone });
        if (cache.Count != 1)
        {
            GD.PrintErr($"[FZR] FAIL: живая зона выброшена из кэша (Count {cache.Count})");
            return false;
        }
        cache.Prune(new List<Zone>());
        if (cache.Count != 0)
        {
            GD.PrintErr($"[FZR] FAIL: кэш держит геометрию снесённой зоны (Count {cache.Count})");
            return false;
        }

        // Две Work-зоны делят один пустой набор тайлов (клетки не материализуются),
        // но геометрия у них раздельная по id и пустая — рисовать нечего.
        var workA = new Zone(2, "w1", ZoneKind.Work, new HashSet<(int X, int Y)>());
        var workB = new Zone(3, "w2", ZoneKind.Work, new HashSet<(int X, int Y)>());
        var geoA = cache.Get(workA.Id, workA.Tiles);
        var geoB = cache.Get(workB.Id, workB.Tiles);
        if (ReferenceEquals(geoA, geoB) || geoA.FillRects.Count != 0 ||
            geoA.BorderSegments.Count != 0)
        {
            GD.PrintErr("[FZR] FAIL: Work-зона без тайлов дала геометрию");
            return false;
        }

        GD.Print($"[FZR] OK: кэш отдаёт готовую геометрию повторно, пересобирает после " +
                 $"замены набора тайлов и выбрасывает снесённые зоны");
        return true;
    }

    // --------------------------------------------------- 6. источник правки (по коду)

    private static bool DrawUsesCachedGeometry()
    {
        const string path = "res://src/ui/renderers/FarmZoneRenderer.cs";
        string src = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[FZR] FAIL: не удалось прочитать {path}");
            return false;
        }

        string draw = MethodBody(src, "public override void _Draw()");
        if (draw == null)
        {
            GD.PrintErr("[FZR] FAIL: не найден _Draw в FarmZoneRenderer");
            return false;
        }

        bool ok = true;
        // Признак старого бага: цикл по клеткам зоны прямо в _Draw.
        if (System.Text.RegularExpressions.Regex.IsMatch(draw, @"foreach\s*\(\s*var\s*\(\s*x\s*,\s*y\s*\)"))
        {
            GD.PrintErr("[FZR] FAIL: _Draw снова обходит клетки зоны — вернулся per-tile рендер (19-01)");
            ok = false;
        }
        if (!draw.Contains("_geometryCache.Get("))
        {
            GD.PrintErr("[FZR] FAIL: _Draw не берёт геометрию из кэша");
            ok = false;
        }
        if (draw.Contains("BuildFillRects(") || draw.Contains("BuildBorderSegments("))
        {
            GD.PrintErr("[FZR] FAIL: _Draw строит геометрию сам — на каждом redraw это O(клеток)");
            ok = false;
        }
        // Склейка должна жить в кэше: пересборка на смену набора тайлов.
        if (!src.Contains("BuildFillRects(tiles, FillRects)") ||
            !src.Contains("BuildBorderSegments(tiles, BorderSegments)"))
        {
            GD.PrintErr("[FZR] FAIL: кэш не собирает геометрию через BuildFillRects/BuildBorderSegments");
            ok = false;
        }

        // Кэш держится на том, что набор тайлов заменяют ссылкой, а не правят на месте.
        const string managerPath = "res://src/simulation/zones/ZoneManager.cs";
        string manager = Godot.FileAccess.GetFileAsString(managerPath);
        if (string.IsNullOrEmpty(manager))
        {
            GD.PrintErr($"[FZR] FAIL: не удалось прочитать {managerPath}");
            return false;
        }
        var inPlace = System.Text.RegularExpressions.Regex.Match(manager,
            @"\bTiles\.(Add|Remove|Clear|UnionWith|ExceptWith|IntersectWith|SymmetricExceptWith)\s*\(");
        if (inPlace.Success)
        {
            GD.PrintErr($"[FZR] FAIL: ZoneManager правит набор тайлов на месте ({inPlace.Value}) — " +
                        "кэш геометрии протухнет, зона будет рисоваться по старым клеткам");
            ok = false;
        }

        if (ok)
            GD.Print("[FZR] OK: _Draw только читает кэш и рисует готовые списки; " +
                     "геометрия пересобирается на смену набора тайлов");
        return ok;
    }

    /// <summary>Тело метода по сигнатуре: сопоставление фигурных скобок.</summary>
    private static string MethodBody(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return null;
        int open = src.IndexOf('{', at);
        if (open < 0) return null;
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(open, i - open + 1);
            }
        }
        return null;
    }

    // --------------------------------------------------------------------- утилиты

    private static bool IsTileAligned(Rect2 r) => IsTileAligned(r.Position) && IsTileAligned(r.Size);

    private static bool IsTileAligned(Vector2 v) =>
        Math.Abs(v.X / Tile - MathF.Round(v.X / Tile)) < 0.0001f &&
        Math.Abs(v.Y / Tile - MathF.Round(v.Y / Tile)) < 0.0001f;

    private static HashSet<(int X, int Y)> MakeRect(int x0, int y0, int w, int h)
    {
        var set = new HashSet<(int X, int Y)>();
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                set.Add((x, y));
        return set;
    }

    /// <summary>4×2 плюс 2×2 сверху — форма, которую нельзя покрыть одним прямоугольником.</summary>
    private static HashSet<(int X, int Y)> LShape()
    {
        var set = MakeRect(10, 10, 4, 2);
        foreach (var tile in MakeRect(10, 12, 2, 2))
            set.Add(tile);
        return set;
    }

    /// <summary>Кольцо 6×6 без центра 2×2 — у контура есть внутренняя граница.</summary>
    private static HashSet<(int X, int Y)> Ring()
    {
        var set = MakeRect(0, 0, 6, 6);
        foreach (var tile in MakeRect(2, 2, 2, 2))
            set.Remove(tile);
        return set;
    }

    /// <summary>Два несвязных куска: кэш и склейка не должны их путать.</summary>
    private static HashSet<(int X, int Y)> TwoPieces()
    {
        var set = MakeRect(0, 0, 2, 2);
        foreach (var tile in MakeRect(5, 5, 3, 1))
            set.Add(tile);
        return set;
    }

    /// <summary>Набор внешних граней в клетках (грань нормализована: от меньшей точки к большей).</summary>
    private static HashSet<(int X0, int Y0, int X1, int Y1)> OuterEdges(HashSet<(int X, int Y)> tiles)
    {
        var edges = new HashSet<(int X0, int Y0, int X1, int Y1)>();
        foreach (var (x, y) in tiles)
        {
            if (!tiles.Contains((x, y - 1))) edges.Add((x, y, x + 1, y));               // верх
            if (!tiles.Contains((x, y + 1))) edges.Add((x, y + 1, x + 1, y + 1));       // низ
            if (!tiles.Contains((x - 1, y))) edges.Add((x, y, x, y + 1));               // лево
            if (!tiles.Contains((x + 1, y))) edges.Add((x + 1, y, x + 1, y + 1));       // право
        }
        return edges;
    }
}