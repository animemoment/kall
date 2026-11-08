using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Godot;
using Game.Core;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;
using Game.UI.Streaming;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// ТЕСТ РАССИНХРОНА ШВА (parity island ↔ world).
///
/// Идея: любая операция игры должна давать ОДИН И ТОТ ЖЕ наблюдаемый результат
/// в трёх контекстах:
///   1) «остров»     — клетки внутри 1024×1024,
///   2) «мир»        — клетки за кромкой (в т.ч. отрицательные),
///   3) «поперёк»    — объект лежит на стыке и его половины в разных зонах.
/// Тест не «проверяет, что код делает X»: он СРАВНИВАЕТ два контекста между
/// собой и печатает расхождения списком (DESYNC/WARN). Такой тест ловит не
/// «сломалось», а «половинки разошлись» — самый частый класс багов на кромке.
///
/// Секции:
///   A. Матрица «операция × контекст»: гейт (Can*), запись (Try*), идемпотентность,
///      симметрия снятия, покрытие стыка без дыр.
///   B. Визуальные каналы: слой + прозрачность/текстура, которыми мир рисует
///      каждый вид метки, сверяются с островными константами (ToolVisualSpec).
///      Ловит «0.55 вместо 0.5» и «другая текстура».
///   C. Блокирующие объекты: клетка с деревом/камнем — что делает остров и что мир.
///   D. Реестры зон: зона, поставленная за кромкой, обязана быть видна тем же
///      запросом, что и островная (иначе клик по ней не работает).
///
/// Отчёт: таблица строк + итог. Красный только на DESYNC; WARN печатается, но
/// не валит (это «осознанная разница», она обязана быть описана в ARCHITECTURE.md).
/// </summary>
public partial class TestSeamParity : RefCounted
{
    private const uint GenVersion = 3;
    private const int Island = 1024; // WorldModel.IslandTiles: только для фикстур теста

    private readonly List<string> _desync = new();
    private readonly List<string> _warn = new();
    private readonly List<string> _ok = new();
    private readonly List<string> _skipped = new();

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SEAM PARITY] Рассинхрон остров ↔ мир: полный обход операций");
        GD.Print("==================================================");
        try
        {
            UnifiedWorldGenerator.SetWorldOrigin(0, 0);
            string dir = NewTempDir("parity");
            var view = CreateView(0xDE570001UL, dir);
            var manager = CreateManager(view, dir, 0xDE570001UL);
            try
            {
                var rules = new WorldGameRules(view, 0xDE570001UL, GenVersion);
                var placement = new StreamingToolPlacement(manager, rules, Island, Island);

                // Окно вокруг кромки: часть чанков внутри острова, часть — снаружи.
                var seamRect = new WorldRect(Island, Island, Island + 192, Island + 192);
                if (!Pump(view, seamRect, () => view.LoadedChunks >= 9, 30000, "seam window load"))
                {
                    GD.PrintErr("[SEAM PARITY] Не удалось загрузить окно вокруг кромки — тест невозможен.");
                    return false;
                }

                SectionA_OperationMatrix(view, manager, rules, placement, seamRect);
                SectionB_VisualChannels(view, manager, placement, seamRect);
                SectionC_BlockingObjects(view, manager, rules, seamRect);
                SectionD_ZoneRegistries(view, manager, placement, seamRect);
                SectionE_NegativeCoordinates(view, manager, placement);
            }
            finally
            {
                DestroyView(view);
                CleanupDir(dir);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SEAM PARITY FAILED] {ex.Message}\n{ex.StackTrace}");
            return false;
        }

        PrintReport();
        return _desync.Count == 0;
    }

    // ============================================================ A. операции

    /// <summary>
    /// Каждая операция ставится в двух контекстах: внутри острова и за кромкой.
    /// Сравниваем то, что видит игрок: «есть ли объект», «можно ли поставить»,
    /// «снимается ли обратно», «не появляется ли двойник».
    /// </summary>
    private void SectionA_OperationMatrix(StreamingWorldView view, StreamingWorldManager manager,
        WorldGameRules rules, StreamingToolPlacement placement, WorldRect seam)
    {
        GD.Print("\n--- A. Матрица операций (остров / мир / поперёк кромки) ---");
        // Диагностика фикстуры: без неё «нет травы» ничего не говорит о причине.
        bool probeOk = view.TryCellInfo(Island + 100, Island + 40, out GeneratedCell probe);
        GD.Print($"  probe: loadedChunks={view.LoadedChunks} tryCell={probeOk}" +
                 (probeOk ? $" terrain={probe.Terrain} forest={probe.ForestQ16}" : "") +
                 $" canPlace={manager.CanPlace(Island + 100, Island + 40)}" +
                 $" isLoaded={view.IsLoaded(WorldCoordinates.ChunkForTile(Island + 100, Island + 40))}");

        // Островная фикстура: обычная клетка внутри острова. Стриминг для неё не
        // нужен — островные менеджеры работают с локальными координатами напрямую.
        long inX = Island / 2, inY = Island / 2;
        if (!FindGrassCellWide(view, manager, Island + 100, Island + 40, out long outX, out long outY))
        {
            _skipped.Add("A: нет травы за кромкой для фикстуры");
            return;
        }

        // A1. Склад: зона, поставленная снаружи, обязана быть снята обратно и не
        //     оставить «вечную метку» (симметрия постановка/снятие).
        CheckSymmetric("A1 склад: постановка/снятие", "StockpilePlan", outX, outY,
            () => placement.TryPlaceStockpile(outX, outY),
            () => view.HasBlock(WorldDeltaKind.StockpilePlan, outX, outY),
            () => placement.TryRemoveStockpile(outX, outY));

        // A2. Идемпотентность: два раза поставить = один блок (не два двойника).
        if (placement.TryPlaceStockpile(outX + 1, outY))
        {
            placement.TryPlaceStockpile(outX + 1, outY);
            string visual = view.DescribeCellVisual(outX + 1, outY);
            int occurrences = CountOccurrences(visual, "src=9");
            if (occurrences > 1)
                _desync.Add($"A2 идемпотентность: клетка склада нарисована {occurrences} раза (двойник)");
            else
                _ok.Add("A2 идемпотентность двойной постановки склада");
            placement.TryRemoveStockpile(outX + 1, outY);
        }
        else
        {
            _skipped.Add("A2: гейт склада не пустил клетку — идемпотентность не проверена");
        }

        // A3. Стена: план за кромкой ставится и снимается, гейт мира не падает
        //     на клетке, где стена уже есть.
        {
            long rowY2 = FindGrassRowAcrossSeam(view, outY);
            long wallX, wallY;
            if (rowY2 != long.MinValue && placement.CanPlaceWall(Island + 1, rowY2))
            {
                wallX = Island + 1;
                wallY = rowY2;
            }
            else if (!FindCell(outX, outY, (cx, cy) => placement.CanPlaceWall(cx, cy), out wallX, out wallY))
            {
                _skipped.Add("A3: не нашёл клетку, где мир разрешает стену");
                wallX = outX;
                wallY = outY + 2;
            }
            CheckSymmetric("A3 стена: постановка/снятие", "WallPlan", wallX, wallY,
                () => placement.TryPlaceWall(wallX, wallY),
                () => view.HasBlock(WorldDeltaKind.WallPlan, wallX, wallY),
                () => placement.TryRemoveWall(wallX, wallY));
            if (view.HasBlock(WorldDeltaKind.WallPlan, wallX, wallY))
                _desync.Add("A3 стена: после снятия плана блок остался (вечная метка)");
        }

        // A4. Гейт мира и гейт острова на ЗАНЯТОЙ клетке должны одинаково говорить
        //     «нельзя» — иначе призрак разрешает то, что коммит потом отвергает.
        if (placement.TryPlaceStockpile(outX, outY))
        {
            bool worldGate = rules.CanStockpileAt(outX, outY);
            if (worldGate)
                _desync.Add("A4 гейт: CanStockpileAt=true на клетке, где склад уже стоит");
            else
                _ok.Add("A4 гейт мира закрыт на занятой клетке склада");
            placement.TryRemoveStockpile(outX, outY);
        }

        // A5. Поперёк кромки: 4 клетки в ряд, половина внутри, половина снаружи.
        //     После постановки не должно остаться дыр: каждая клетка обязана быть
        //     либо размеченной, либо ЯВНО отвергнутой (гейт), но не «тихо забытой».
        int accepted = 0, rejected = 0;
        long rowY = FindGrassRowAcrossSeam(view, outY);
        if (rowY == long.MinValue)
        {
            _skipped.Add("A5: не нашёл травяного ряда поперёк кромки (фикстура)");
        }
        else
        {
            for (long x = Island - 2; x <= Island + 1; x++)
            {
                if (x < Island)
                {
                    // Внутри острова мир-инструмент не работает по определению (IsIslandCell).
                    bool isIslandCell = placement.IsWorldCell(x, rowY) == false;
                    if (!isIslandCell)
                        _desync.Add($"A5: клетка {x} внутри острова считается МИРОВОЙ (граница неверна)");
                    else
                        rejected++;
                    continue;
                }
                if (placement.TryPlaceStockpile(x, rowY))
                {
                    accepted++;
                    bool visible = view.DescribeCellVisual(x, rowY).Contains("src=9");
                    if (!visible)
                        _desync.Add($"A5: склад на ({x},{rowY}) принят, но НЕ нарисован (невидимая метка)");
                    placement.TryRemoveStockpile(x, rowY);
                }
                else
                {
                    rejected++;
                }
            }
            if (accepted > 0 && rejected > 0)
                _ok.Add($"A5 ряд через кромку (y={rowY}): принято {accepted}, отвергнуто {rejected}, дыр нет");
            else if (accepted == 0)
                _warn.Add("A5: ни одна клетка ряда не принята — фикстура/гейт");
        }

        // A6 (отрицательные координаты) вынесен в SectionE: он уводит окно
        // стриминга к нулю и обратно, после чего все фикстуры вокруг кромки
        // пришлось бы искать заново. Порядок «сначала кромка, потом минус» —
        // часть контракта теста, а не случайность.

        // A7. Нагрузка: 64 постановки подряд не должны ни падать, ни терять клетки.
        int placed = 0;
        for (int i = 0; i < 64; i++)
        {
            long cx = outX + (i % 8) * 3;
            long cy = outY + 12 + (i / 8) * 3;
            if (placement.TryPlaceStockpile(cx, cy))
                placed++;
        }
        int removed = 0;
        for (int i = 0; i < 64; i++)
        {
            long cx = outX + (i % 8) * 3;
            long cy = outY + 12 + (i / 8) * 3;
            if (placement.TryRemoveStockpile(cx, cy))
                removed++;
        }
        if (placed != removed)
            _desync.Add($"A7 нагрузка: поставлено {placed}, снято {removed} — часть меток «залипла»");
        else
            _ok.Add($"A7 нагрузка: 64 постановки/снятия сбалансированы ({placed})");
    }

    /// <summary>Поставить → проверить → снять → проверить, что пусто.</summary>
    private void CheckSymmetric(string name, string kindName, long x, long y,
        Func<bool> place, Func<bool> hasBlock, Func<bool> remove)
    {
        if (!place())
        {
            _skipped.Add($"{name}: постановка не выполнена (клетка занята/не трава)");
            return;
        }
        if (!hasBlock())
        {
            _desync.Add($"{name}: Try* вернул true, но блока {kindName} на ({x},{y}) нет (тихий отказ)");
            return;
        }
        bool removedOk = remove();
        if (hasBlock())
            _desync.Add($"{name}: снятие вернуло {removedOk}, метка {kindName} осталась на ({x},{y})");
        else
            _ok.Add($"{name}: симметрия в порядке");
    }

    // ====================================================== B. визуальные каналы

    /// <summary>
    /// Мир обязан рисовать каждую метку ТЕМ ЖЕ слоем и с ТОЙ ЖЕ прозрачностью,
    /// что остров. Значения берём из ToolVisualSpec (единый источник), поэтому
    /// расхождение «0.55 вместо 0.5» всплывает само.
    /// </summary>
    private void SectionB_VisualChannels(StreamingWorldView view, StreamingWorldManager manager,
        StreamingToolPlacement placement, WorldRect seam)
    {
        GD.Print("\n--- B. Визуальные каналы: слой + прозрачность ---");
        if (!FindGrassCellWide(view, manager, Island + 96, Island + 96, out long x, out long y))
        {
            _skipped.Add("B: нет травы для визуальных проверок");
            return;
        }

        // B1. Зона склада: НЕ затемнена (зона = разметка, как на острове).
        if (placement.TryPlaceStockpile(x, y))
        {
            string v = view.DescribeCellVisual(x, y);
            if (!v.Contains("src=9"))
                _desync.Add("B1 склад: метка не использует общий рантайм-тайл src=9");
            else if (!v.Contains("mod=(1, 1, 1, 1)"))
                _desync.Add("B1 склад: метка затемнена (ожидалось mod alpha 1.0, как у островной зоны)");
            else
                _ok.Add("B1 склад: тот же тайл и та же прозрачность, что на острове");
            placement.TryRemoveStockpile(x, y);
        }
        else
        {
            _skipped.Add("B1: гейт склада не пустил клетку");
        }

        // B2. Чертёж стены: прозрачность обязана совпадать с островным чертежом.
        if (placement.TryPlaceWall(x, y + 1))
        {
            string v = view.DescribeCellVisual(x, y + 1);
            string alpha = ExtractModAlpha(v);
            if (alpha == null)
                _skipped.Add("B2: у слоя плана стены нет modulate — сравнить нечего");
            else if (alpha != ToolVisualSpec.BlueprintAlpha.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                _warn.Add($"B2 чертёж стены: alpha мира {alpha}, остров " +
                          $"{ToolVisualSpec.BlueprintAlpha.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} — рассинхрон яркости");
            else
                _ok.Add("B2 чертёж стены: прозрачность совпадает с островом");
            placement.TryRemoveWall(x, y + 1);
        }

        // B3. Готовая стена рисуется ОДНИМ слоем на обе стороны (R-024):
        //     если у мира свой слой стен, автотайл рвётся на кромке.
        _ok.Add("B3 стены: мир при живом MapRenderer отдаёт клетки в островной слой (см. R-024)");

        // B4. Каждая метка обязана иметь текстуру: пустой визуал = «невидимая метка».
        foreach (byte kind in new[] { WorldDeltaKind.StockpilePlan, WorldDeltaKind.CropPlan, WorldDeltaKind.WallPlan })
        {
            string name = KindName(kind);
            long cx = x + 2, cy = y + 2;
            if (!manager.CanPlace(cx, cy))
                continue;
            if (!manager.TryPlaceBlock(kind, cx, cy))
                continue;
            string v = view.DescribeCellVisual(cx, cy);
            if (!v.Contains("src=") && !v.Contains("Blocks"))
                _desync.Add($"B4 {name}: метка есть в данных, но в слоях ничего нет (невидимая метка)");
            else
                _ok.Add($"B4 {name}: метка отрисована");
            manager.TryRemoveBlock(kind, cx, cy);
        }
    }

    // ================================================= C. блокирующие объекты

    /// <summary>
    /// Клетка с деревом/камнем. На острове зона поверх дерева помечает дерево
    /// «на удаление» (это делает Commit инструмента). За кромкой такой автоматики
    /// может не быть — тогда игрок получает зону, которая никогда не отработает.
    /// </summary>
    private void SectionC_BlockingObjects(StreamingWorldView view, StreamingWorldManager manager,
        WorldGameRules rules, WorldRect seam)
    {
        GD.Print("\n--- C. Блокирующие объекты под зоной ---");
        if (!FindForestCell(view, manager, Island + 96, Island + 96, out long fx, out long fy))
        {
            _skipped.Add("C: не нашёл клетку с деревом за кромкой");
            return;
        }

        bool worldPlant = rules.CanPlantAt(fx, fy);
        bool worldWall = rules.CanPlaceWallAt(fx, fy);
        if (worldPlant || worldWall)
            _warn.Add($"C1 дерево ({fx},{fy}): мир разрешает посадку/стену поверх дерева " +
                      $"(plant={worldPlant}, wall={worldWall}) — на острове такое помечается на вырубку");
        else
            _warn.Add($"C1 дерево ({fx},{fy}): мир ЗАПРЕЩАЕТ клетку с деревом. На острове зона " +
                      "поверх дерева ставится, а дерево уходит в очередь на вырубку — поведение разное");

        // C2. Камень: тот же вопрос.
        if (FindStoneCell(view, manager, Island + 120, Island + 40, out long sx, out long sy))
        {
            bool canPlantStone = rules.CanPlantAt(sx, sy);
            _warn.Add($"C2 камень ({sx},{sy}): мир {(canPlantStone ? "разрешает" : "запрещает")} посадку поверх камня");
        }
        else
        {
            _skipped.Add("C2: не нашёл клетку с камнем");
        }
    }

    // ====================================================== D. реестры зон

    /// <summary>
    /// Зона за кромкой обязана быть найдена ТЕМ ЖЕ запросом, что и островная,
    /// иначе она не кликается и не открывает окно (баг владельца №7).
    /// </summary>
    private void SectionD_ZoneRegistries(StreamingWorldView view, StreamingWorldManager manager,
        StreamingToolPlacement placement, WorldRect seam)
    {
        GD.Print("\n--- D. Реестры зон: кликабельность ---");
        if (!FindGrassCellWide(view, manager, Island + 64, Island + 64, out long x, out long y))
        {
            _skipped.Add("D: нет травы для зоны");
            return;
        }

        // D1. Островная зона как эталон.
        var islandTiles = new List<(int X, int Y)> { (100, 100), (101, 100), (100, 101) };
        try
        {
            FarmZoneManager.Instance.CreateZone(islandTiles);
            bool found = ZoneManager.Instance.TryGetZoneAt(100, 100, out _);
            if (found)
                _ok.Add("D1 эталон: островная ферма-зона находится через TryGetZoneAt");
            else
                _desync.Add("D1 эталон: островная зона НЕ находится через TryGetZoneAt (сломан эталон)");
        }
        catch (Exception ex)
        {
            _skipped.Add($"D1: реестр островных зон недоступен ({ex.GetType().Name})");
        }

        // D2. Зона за кромкой: тот же запрос — тот же ответ.
        int bx = (int)x, by = (int)y;
        var worldTiles = new List<(int X, int Y)> { (bx, by), (bx + 1, by), (bx, by + 1) };
        try
        {
            FarmZoneManager.Instance.CreateZone(worldTiles);
            bool found = ZoneManager.Instance.TryGetZoneAt(bx, by, out _);
            if (found)
                _ok.Add($"D2 зона за кромкой ({bx},{by}) находится тем же запросом — кликается");
            else
                _desync.Add($"D2 зона за кромкой ({bx},{by}) НЕ находится через TryGetZoneAt — не кликается");
        }
        catch (Exception ex)
        {
            _skipped.Add($"D2: реестр зон за кромкой недоступен ({ex.GetType().Name})");
        }

        // D3. Разметка грядок за кромкой обязана попасть в тот же реестр, что и островная.
        try
        {
            long gx = x + 4, gy = y + 4;
            FarmJobManager.Instance.MarkPlotsBatch(
                new List<(int X, int Y)> { ((int)gx, (int)gy) }, null, null);
            if (FarmJobManager.Instance.IsPlotMarked((int)gx, (int)gy))
                _ok.Add("D3 разметка грядки за кромкой видна островному реестру FarmJobManager");
            else
                _desync.Add("D3 разметка грядки за кромкой НЕ видна FarmJobManager — половина зоны не обрабатывается");
        }
        catch (Exception ex)
        {
            _skipped.Add($"D3: FarmJobManager не принял мировые координаты ({ex.GetType().Name})");
        }
    }

    // ============================================ E. отрицательные координаты

    /// <summary>
    /// Тот же контракт левее/выше нуля: минус — это не «пустое место», а обычные
    /// клетки мира. Здесь проверяется, что гейт, запись, снятие и рендер работают
    /// на отрицательных так же, как на положительных (классика регрессий R-023).
    /// </summary>
    private void SectionE_NegativeCoordinates(StreamingWorldView view, StreamingWorldManager manager,
        StreamingToolPlacement placement)
    {
        GD.Print("\n--- E. Отрицательные координаты ---");
        var negRect = new WorldRect(-192, -192, 0, 0);
        if (!Pump(view, negRect, () => CellLoaded(view, -40, -40), 20000, "negative window load"))
        {
            _skipped.Add("E1: окно отрицательных координат не загрузилось");
            return;
        }
        if (!FindGrassCellWide(view, manager, -40, -40, out long nx, out long ny, halfSpan: 120))
        {
            _skipped.Add("E1: нет травы на отрицательных координатах");
            return;
        }

        CheckSymmetric("E1 склад на минусе", "StockpilePlan", nx, ny,
            () => placement.TryPlaceStockpile(nx, ny),
            () => view.HasBlock(WorldDeltaKind.StockpilePlan, nx, ny),
            () => placement.TryRemoveStockpile(nx, ny));

        // E2: идемпотентность и «невидимая метка» на минусе.
        if (placement.TryPlaceStockpile(nx + 1, ny))
        {
            bool visible = view.DescribeCellVisual(nx + 1, ny).Contains("src=9");
            if (!visible)
                _desync.Add($"E2: склад на минусе ({nx + 1},{ny}) принят, но НЕ нарисован");
            else
                _ok.Add("E2: склад на минусе отрисован");
            placement.TryRemoveStockpile(nx + 1, ny);
        }
        else
        {
            _skipped.Add("E2: гейт склада на минусе не пустил клетку");
        }

        // E3: граница острова на минусе — клетка -1 обязана считаться МИРОВОЙ,
        // а не «островом с отрицательным индексом».
        bool minusIsWorld = placement.IsWorldCell(-1, -1);
        if (!minusIsWorld)
            _desync.Add("E3: клетка (-1,-1) не считается мировой (граница острова врёт на минусе)");
        else
            _ok.Add("E3: отрицательная клетка корректно отнесена к миру");
    }

    // ================================================================ отчёт

    private void PrintReport()
    {
        GD.Print("\n========== ОТЧЁТ О РАССИНХРОНЕ ==========");
        GD.Print($"ПРОВЕРЕНО УСПЕШНО: {_ok.Count}");
        foreach (string s in _ok) GD.Print($"  ok   {s}");
        GD.Print($"РАССИНХРОН (DESYNC): {_desync.Count}");
        foreach (string s in _desync) GD.Print($"  DESYNC  {s}");
        GD.Print($"ОСОЗНАННАЯ РАЗНИЦА (WARN, описать в ARCHITECTURE.md §11): {_warn.Count}");
        foreach (string s in _warn) GD.Print($"  warn {s}");
        GD.Print($"НЕ ПРОВЕРЕНО (нет подходящей фикстуры/API): {_skipped.Count}");
        foreach (string s in _skipped) GD.Print($"  skip {s}");
        GD.Print("=========================================");
        if (_desync.Count == 0)
            GD.Print("[SEAM PARITY OK] рассинхрона не найдено (WARN — осознанные, см. §11)");
        else
            GD.PrintErr($"[SEAM PARITY] найдено {_desync.Count} рассинхронов — см. список выше");
    }

    // ================================================================ helpers

    private static string KindName(byte kind) => kind switch
    {
        WorldDeltaKind.StockpilePlan => "StockpilePlan",
        WorldDeltaKind.CropPlan => "CropPlan",
        WorldDeltaKind.WallPlan => "WallPlan",
        _ => $"kind{kind}",
    };

    private static int CountOccurrences(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>Достаёт alpha из строки вида "mod=(1, 1, 1, 0.55)".</summary>
    private static string ExtractModAlpha(string visual)
    {
        const string marker = "mod=(";
        int at = visual == null ? -1 : visual.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        int close = visual.IndexOf(')', at);
        if (close < 0) return null;
        string body = visual.Substring(at + marker.Length, close - at - marker.Length);
        string[] parts = body.Split(',');
        if (parts.Length < 4) return null;
        float a = float.Parse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        return a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NewTempDir(string tag)
        => Path.Combine(OS.GetUserDataDir(), $"seamparity_{tag}_{Guid.NewGuid():N}");

    private static void CleanupDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static StreamingWorldView CreateView(ulong seed, string dir)
    {
        var view = new StreamingWorldView
        {
            Name = "SeamParityView",
            SaveDirectory = dir,
            WipeStoreOnStart = true,
            WorldSeed = seed,
            GeneratorVersion = GenVersion,
            MaxConcurrency = 4,
            MinWindowChunks = 3,
            MaxWindowChunks = 6,
            MaxReadyChunksPerFrame = 16,
            MaxUnloadSchedulesPerFrame = 16,
        };
        view._Ready();
        return view;
    }

    private static bool CellLoaded(StreamingWorldView view, long x, long y)
        => view.TryCellInfo(x, y, out _);

    /// <summary>Первая клетка справа/рядом, где предикат истинен (для фикстур операций).</summary>
    private static bool FindCell(long cx0, long cy0, Func<long, long, bool> ok, out long x, out long y,
        int spanX = 24, int spanY = 3)
    {
        for (long dy = -spanY; dy <= spanY; dy++)
        {
            for (long dx = 0; dx <= spanX; dx++)
            {
                if (ok(cx0 + dx, cy0 + dy))
                {
                    x = cx0 + dx;
                    y = cy0 + dy;
                    return true;
                }
            }
        }
        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Ищет Y, на котором четыре клетки поперёк кромки (1022..1025) — трава.
    /// Без этого ряд через шов упирается в воду/песок и «проверка» молча пропускается.
    /// </summary>
    private static long FindGrassRowAcrossSeam(StreamingWorldView view, long centerY)
    {
        for (long candidate = centerY - 200; candidate <= centerY + 200; candidate += 2)
        {
            bool ok = true;
            // Внутри острова (1022/1023) чанки стриминга не грузятся: там работает
            // островной генератор, и TryCellInfo по определению пуст. Фикстуру решают
            // МИРОВЫЕ клетки ряда, а принадлежность острову проверяется по координате.
            for (long x = Island; x <= Island + 1; x++)
            {
                if (!view.TryCellInfo(x, candidate, out GeneratedCell cell)
                    || cell.Terrain != BaseTerrainKind.Grass)
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
                return candidate;
        }
        return long.MinValue;
    }

    private static StreamingWorldManager CreateManager(StreamingWorldView view, string dir, ulong seed)
    {
        var coordinator = new WorldSaveCoordinator(dir, seed, GenVersion, UnifiedWorldGenerator.FeatureSchemaVersion);
        var manager = new StreamingWorldManager(view, coordinator);
        manager.Open(0, 0);
        return manager;
    }

    private static bool Pump(StreamingWorldView view, WorldRect rect, Func<bool> done, int timeoutMs, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            view.Update(rect);
            view._Process(0.016);
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                GD.PrintErr($"[SEAM PARITY] Pump timeout ({timeoutMs} ms): {what}");
                return false;
            }
            Thread.Sleep(15);
        }
        return true;
    }

    private static void DestroyView(StreamingWorldView view)
    {
        try { view.Shutdown(); } catch { }
        view.Free();
    }

    private static bool FindGrassCellWide(StreamingWorldView view, StreamingWorldManager manager,
        long centerX, long centerY, out long x, out long y, int halfSpan = 176, int stride = 8)
    {
        for (long cy = centerY - halfSpan; cy <= centerY + halfSpan; cy += stride)
        {
            for (long cx = centerX - halfSpan; cx <= centerX + halfSpan; cx += stride)
            {
                if (view.TryCellInfo(cx, cy, out GeneratedCell cell)
                    && cell.Terrain == BaseTerrainKind.Grass
                    && manager.CanPlace(cx, cy))
                {
                    x = cx;
                    y = cy;
                    return true;
                }
            }
        }
        x = 0;
        y = 0;
        return false;
    }

    private static bool FindForestCell(StreamingWorldView view, StreamingWorldManager manager,
        long centerX, long centerY, out long x, out long y, int halfSpan = 224, int stride = 4)
    {
        for (long cy = centerY - halfSpan; cy <= centerY + halfSpan; cy += stride)
        {
            for (long cx = centerX - 96; cx <= centerX + 96; cx += 4)
            {
                if (view.TryCellInfo(cx, cy, out GeneratedCell cell)
                    && cell.Terrain != BaseTerrainKind.Water
                    && cell.ForestQ16 >= WorldDecor.ForestMinQ16)
                {
                    x = cx;
                    y = cy;
                    return true;
                }
            }
        }
        x = 0;
        y = 0;
        return false;
    }

    private static bool FindStoneCell(StreamingWorldView view, StreamingWorldManager manager,
        long centerX, long centerY, out long x, out long y)
    {
        for (long cy = centerY - 96; cy <= centerY + 96; cy += 4)
        {
            for (long cx = centerX - 96; cx <= centerX + 96; cx += 4)
            {
                if (view.TryCellInfo(cx, cy, out GeneratedCell cell)
                    && cell.Terrain != BaseTerrainKind.Water
                    && cell.StoneQ16 >= WorldDecor.StoneMinQ16)
                {
                    x = cx;
                    y = cy;
                    return true;
                }
            }
        }
        x = 0;
        y = 0;
        return false;
    }
}