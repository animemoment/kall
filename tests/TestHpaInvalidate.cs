using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 05-05 / 07-07: оверлей HPA и его инвалидация.
///
///  05-05 — читатель обязан брать карту из ОПУБЛИКОВАННОГО состояния
///  (`AcquireGeneration().Grid`), а не из полей класса: иначе пересборка
///  оверлея на живой карте отдаёт поиску смесь старой и новой сетки.
///  07-07 — `Invalidate()` имел окно 200 мс и ГЛОТАЛ инвалидацию, попавшую
///  в окно: стена, построенная сразу после пересборки, оставалась
///  «прозрачной» — первый же поиск вёл сквозь неё по старому оверлею.
///
/// Тест ловит именно потерю инвалидации: сетка меняется НА МЕСТЕ (без
/// `InitializeCost`), затем идут две инвалидации подряд — на старом коде
/// вторая проглатывалась, и поиск, выполненный сразу после них, шёл через
/// стену. Дополнительно проверяется публикация состояния и отсутствие
/// старых полей сетки в классе.
///
/// Запуск: godot --headless --path . --script res://tests/run_hpa_invalidate.gd
/// </summary>
public partial class TestHpaInvalidate : RefCounted
{
    private const string SourcePath = "res://src/simulation/pathfinding/HierarchicalPathfinder.cs";

    // Клетка (10, y) для всех y — стена поперёк коридора y = 1.
    private const int N = 32;
    private const int WallX = 10;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[HPA-INVALIDATE] 05-05 / 07-07: инвалидация оверлея не теряется");
        GD.Print("==================================================");

        bool ok = true;
        ok &= FirstSearchAfterInvalidateSeesWall();
        ok &= RepeatedInvalidateIsLossless();
        ok &= PublishedStateIsTheOnlyGridSource();
        ok &= ConcurrentSearchNeverMixesGenerations();

        GD.Print("==================================================");
        GD.Print(ok ? "[HPA-INVALIDATE] PASS" : "[HPA-INVALIDATE] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    // ------------------------------------------------------------------
    // 07-07 (главный кейс): стена появилась, инвалидация вызвана ДВАЖДЫ
    // подряд, поиск сразу после них обязан её видеть.
    // ------------------------------------------------------------------

    private bool FirstSearchAfterInvalidateSeesWall()
    {
        GD.Print("[HPA-INVALIDATE] --- (1) первый поиск после Invalidate() видит новую стену ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        byte[] cost = EmptyCost();

        pf.InitializeCost(cost, N, N);
        // Прямой коридор: контракт TryFindPathScratch — true и count == 0
        // (прямая видимость, промежуточных точек нет).
        bool foundBefore = pf.TryFindPathScratch(1, 1, 20, 1, out _, out _);
        if (!foundBefore)
        {
            GD.PrintErr("[HPA-INVALIDATE] FAIL (1): свободный коридор не находится — сетка не готова");
            return false;
        }

        // Стена появляется НА МЕСТЕ — так это и происходит в игре (стройка
        // пишет в тот же массив стоимостей, нового InitializeCost нет).
        WallAcross(ref cost);

        pf.Invalidate();
        pf.Invalidate(); // вторая инвалидация внутри старого окна 200 мс

        bool foundAfter = pf.TryFindPathScratch(1, 1, 20, 1, out _, out int countAfter);

        if (foundAfter)
        {
            GD.PrintErr($"[HPA-INVALIDATE] FAIL (1): поиск сразу после Invalidate() (×2) прошёл СКВОЗЬ " +
                        $"новую стену x={WallX} (точек {countAfter}) — инвалидация потеряна");
            return false;
        }

        // Контроль: убираем стену и снова инвалидируем — путь обязан вернуться.
        Unwall(ref cost);
        pf.Invalidate();
        bool foundAgain = pf.TryFindPathScratch(1, 1, 20, 1, out _, out _);
        if (!foundAgain)
        {
            GD.PrintErr("[HPA-INVALIDATE] FAIL (1): после снятия стены путь не вернулся — оверлей залип");
            return false;
        }

        GD.Print("[HPA-INVALIDATE] OK: стена видна первым же поиском, после снятия путь вернулся");
        return true;
    }

    // ------------------------------------------------------------------
    // 07-07: сколько бы инвалидаций ни пришло подряд, ни одна не теряется —
    // последняя всегда применяется.
    // ------------------------------------------------------------------

    private bool RepeatedInvalidateIsLossless()
    {
        GD.Print("[HPA-INVALIDATE] --- (2) серия инвалидаций подряд не теряется ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        byte[] cost = EmptyCost();

        pf.InitializeCost(cost, N, N);
        pf.TryFindPathScratch(1, 1, 20, 1, out _, out _);

        // Каждая итерация: меняем сетку и шлём сразу по 4 инвалидации.
        for (int round = 0; round < 4; round++)
        {
            bool walled = round % 2 == 0;
            if (walled)
                WallAcross(ref cost);
            else
                Unwall(ref cost);

            pf.Invalidate();
            pf.Invalidate();
            pf.Invalidate();
            pf.Invalidate();

            bool found = pf.TryFindPathScratch(1, 1, 20, 1, out _, out _);
            if (walled && found)
            {
                GD.PrintErr($"[HPA-INVALIDATE] FAIL (2): раунд {round}: путь сквозь стену после 4×Invalidate()");
                return false;
            }
            if (!walled && !found)
            {
                GD.PrintErr($"[HPA-INVALIDATE] FAIL (2): раунд {round}: путь не вернулся после снятия стены");
                return false;
            }
        }

        GD.Print("[HPA-INVALIDATE] OK: 4 раунда по 4 инвалидации — состояние всегда применялось");
        return true;
    }

    // ------------------------------------------------------------------
    // 05-05: карта берётся только из опубликованного состояния, старых
    // полей сетки в классе нет.
    // ------------------------------------------------------------------

    private bool PublishedStateIsTheOnlyGridSource()
    {
        GD.Print("[HPA-INVALIDATE] --- (3) чтение только через опубликованное состояние ---");

        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[HPA-INVALIDATE] FAIL (3): не прочитал {SourcePath}");
            return false;
        }

        // Комментарии не считаем: упоминание старого имени в пояснении — не поле.
        string code = StripComments(src);

        bool ok = true;
        ok &= Check("нет старых полей сетки (_mapWidth/_mapHeight/_cost[)",
            !Regex.IsMatch(code, @"\b_mapWidth\b") &&
            !Regex.IsMatch(code, @"\b_mapHeight\b") &&
            !Regex.IsMatch(code, @"\b_cost\s*\["));
        ok &= Check("состояние публикуется атомарно",
            Regex.IsMatch(code, @"Volatile\.Write\(ref _generation, new OverlayGeneration\("));
        ok &= Check("состояние читается атомарно",
            Regex.IsMatch(code, @"Volatile\.Read\(ref _generation\)"));
        ok &= Check("AcquireGeneration пересобирает при _dirty",
            Regex.IsMatch(code, @"gen == null \|\| _dirty"));
        ok &= Check("оба входа поиска берут сетку из AcquireGeneration",
            Regex.Matches(code, @"AcquireGeneration\(\)").Count >= 3);
        ok &= Check("Invalidate выставляет флаг под _buildLock без окна времени",
            Regex.IsMatch(code, @"public void Invalidate\(\)\s*\{\s*lock \(_buildLock\)") &&
            !Regex.IsMatch(ExtractMethod(code, "public void Invalidate()"),
                @"Stopwatch|_lastInvalidate|_invalidateDeadline|ElapsedMilliseconds"));

        return ok;
    }

    // ------------------------------------------------------------------
    // 05-05: поиск и пересборка одновременно не дают смеси поколений.
    // ------------------------------------------------------------------

    private bool ConcurrentSearchNeverMixesGenerations()
    {
        GD.Print("[HPA-INVALIDATE] --- (4) поиск параллельно с инвалидациями ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        byte[] cost = EmptyCost();
        pf.InitializeCost(cost, N, N);

        int bad = 0;
        int searched = 0;
        var stop = new ManualResetEventSlim(false);

        var searchers = new List<Thread>();
        for (int t = 0; t < 3; t++)
        {
            var th = new Thread(() =>
            {
                var rng = new Random(1234 + System.Environment.CurrentManagedThreadId);
                int mapW = pf.MapWidth;
                int mapH = pf.MapHeight;
                while (!stop.IsSet)
                {
                    int ty = 1 + rng.Next(N - 2);
                    bool found = pf.TryFindPathScratch(1, ty, N - 2, ty, out int[] path, out int count);
                    Interlocked.Increment(ref searched);
                    // count == 0 — прямая видимость, промежуточных точек нет.
                    if (!found || path == null || count <= 0)
                        continue;
                    for (int i = 0; i < count; i++)
                    {
                        int px = path[i] % mapW;
                        int py = path[i] / mapW;
                        // Смесь поколений проявлялась как клетки вне сетки.
                        if ((uint)px >= (uint)mapW || (uint)py >= (uint)mapH)
                        {
                            Interlocked.Increment(ref bad);
                            break;
                        }
                    }
                }
            })
            { IsBackground = true };
            searchers.Add(th);
            th.Start();
        }

        for (int i = 0; i < 40; i++)
        {
            if (i % 2 == 0)
                WallAcross(ref cost);
            else
                Unwall(ref cost);
            pf.Invalidate();
            Thread.Sleep(2);
        }

        stop.Set();
        foreach (Thread th in searchers)
            th.Join(5000);

        GD.Print($"[HPA-INVALIDATE] поисков: {searched}, некорректных: {bad}");
        if (searched == 0)
        {
            GD.PrintErr("[HPA-INVALIDATE] FAIL (4): ни одного поиска не выполнено — тест ничего не проверил");
            return false;
        }
        if (bad != 0)
        {
            GD.PrintErr($"[HPA-INVALIDATE] FAIL (4): {bad} поисков вернули клетки вне сетки (смесь поколений)");
            return false;
        }

        GD.Print("[HPA-INVALIDATE] OK: под инвалидациями поиск не отдаёт смесь поколений");
        return true;
    }

    // ------------------------------------------------------------------ helpers

    private static byte[] EmptyCost()
    {
        var cost = new byte[N * N];
        Array.Fill(cost, HierarchicalPathfinder.CostGrass);
        return cost;
    }

    private static void WallAcross(ref byte[] cost)
    {
        for (int y = 0; y < N; y++)
            cost[y * N + WallX] = HierarchicalPathfinder.CostBlocked;
    }

    private static void Unwall(ref byte[] cost)
    {
        for (int y = 0; y < N; y++)
            cost[y * N + WallX] = HierarchicalPathfinder.CostGrass;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[HPA-INVALIDATE] OK: {what}");
            return true;
        }
        GD.PrintErr($"[HPA-INVALIDATE] FAIL: {what}");
        return false;
    }

    /// <summary>
    /// Убирает строчные комментарии: упоминание старого имени в пояснении —
    /// не поле класса, и проверка «полей нет» не должна на нём спотыкаться.
    /// </summary>
    private static string StripComments(string src)
    {
        var sb = new System.Text.StringBuilder(src.Length);
        int i = 0;
        while (i < src.Length)
        {
            if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n')
                    i++;
                continue;
            }
            sb.Append(src[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string ExtractMethod(string src, string name)
    {
        int decl = src.IndexOf(name + "(", StringComparison.Ordinal);
        if (decl < 0) return string.Empty;
        int open = src.IndexOf('{', decl);
        if (open < 0) return string.Empty;
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return src.Substring(open, i - open + 1);
            }
        }
        return string.Empty;
    }
}
