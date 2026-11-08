using System;
using System.Text.RegularExpressions;
using Godot;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 19-07: тени предметов заливали в GPU 512 КБ каждый кадр независимо от того, сколько
/// теней видно. Проверяем две вещи:
///  1) правило размера буфера (чистая функция) — растёт до нужного, ужимается только при
///     запасе больше чем вдвое, не «дёргается» на стабильном числе теней, база не нарушена;
///  2) структуру рендера: буфер больше не выделен на MaxShadows, размер считается по
///     фактическому числу видимых теней до записи (двухпроходный обход).
///
/// Почему нужна и текстовая часть: сам факт «в GPU ушло меньше данных» виден только в
/// живой игре (headless не рисует MultiMesh), поэтому структура вызова фиксируется тестом.
///
/// Запуск: godot --headless --path . --script res://tests/run_item_shadow_buffer.gd
/// </summary>
public partial class TestItemShadowBuffer : RefCounted
{
    private const string SourcePath = "res://src/ui/renderers/ItemShadowRenderer.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SHADOW-BUF] Буфер теней по факту, а не 512 КБ (19-07)");
        GD.Print("==================================================");

        bool ok = true;
        int floor = 512 * 8;

        // 1. Правило размера.
        ok &= Check("растёт вдвое до нужного",
            ItemShadowRenderer.NextBufferCapacity(4096, 10000, floor) >= 10000 &&
            ItemShadowRenderer.NextBufferCapacity(4096, 10000, floor) == 16384);
        ok &= Check("на стабильном числе теней размер не меняется",
            ItemShadowRenderer.NextBufferCapacity(16384, 9000, floor) == 16384 &&
            ItemShadowRenderer.NextBufferCapacity(16384, 8192, floor) == 16384);
        ok &= Check("ужимается, когда запас больше чем вдвое",
            ItemShadowRenderer.NextBufferCapacity(65536, 100, floor) < 65536);
        ok &= Check("ниже базы не опускается",
            ItemShadowRenderer.NextBufferCapacity(65536, 8, floor) == floor);
        ok &= Check("needed <= 0 не меняет размер",
            ItemShadowRenderer.NextBufferCapacity(4096, 0, floor) == 4096);
        ok &= Check("маленькая база поднимается до floor",
            ItemShadowRenderer.NextBufferCapacity(8, 100, floor) >= 100);

        // Кириллица/порог: 100 теней должны дать размер, кратный 8 (целые экземпляры).
        int cap = ItemShadowRenderer.NextBufferCapacity(1024, 800, floor);
        ok &= Check("размер кратен восьми", cap % 8 == 0);

        // 2. Структура рендера.
        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[SHADOW-BUF] FAIL: не прочитал {SourcePath}");
            return false;
        }

        ok &= Check("нет фиксированного буфера на MaxShadows",
            !Regex.IsMatch(src, @"_buffer\s*=\s*new\s+float\[MaxShadows\s*\*\s*8\]"));
        ok &= Check("есть InitialShadowCapacity и EnsureBuffer",
            src.Contains("InitialShadowCapacity") && src.Contains("EnsureBuffer("));
        ok &= Check("EnsureBuffer зовётся с фактическим числом видимых теней",
            Regex.IsMatch(src, @"EnsureBuffer\(visibleCount \* 8\)"));

        string render = ExtractMethod(src, "void UpdateAndRender");
        ok &= Check("UpdateAndRender найден", render.Length > 0);

        // Двухпроходность: сначала счёт видимых, потом запись.
        int countPos = render.IndexOf("visibleCount++;", StringComparison.Ordinal);
        int ensurePos = render.IndexOf("EnsureBuffer(", StringComparison.Ordinal);
        int writePos = render.IndexOf("_buffer[idx + 0]", StringComparison.Ordinal);
        ok &= Check("сначала считаем видимые", countPos > 0 && ensurePos > countPos);
        ok &= Check("буфер готовят до записи", ensurePos > 0 && writePos > ensurePos);
        ok &= Check("запись идёт по written, не по visibleCount",
            render.Contains("int idx = written * 8;"));
        ok &= Check("VisibleInstanceCount = written",
            render.Contains("_multiMesh.VisibleInstanceCount = written;"));
        ok &= Check("буфер в GPU — тот же управляемый массив", render.Contains("_multiMesh.Buffer = _buffer;"));

        // 3. 19-07 (живой баг): MultiMesh.Buffer обязан быть ровно InstanceCount*8 float
        // для Transform2D. Буфер рос, а InstanceCount оставался MaxShadows, поэтому Godot
        // отклонял каждую запись («different size from the Multimesh's existing buffer»)
        // и тени не рисовались: в живой игре это дало 8 FPS и ошибку на кадр.
        ok &= Check("инстансов на буфер: 8 float на инстанс",
            ItemShadowRenderer.InstancesForBuffer(4096) == 512 &&
            ItemShadowRenderer.InstancesForBuffer(131072) == 16384 &&
            ItemShadowRenderer.InstancesForBuffer(0) == 0);
        ok &= Check("InstanceCount синхронизируется с буфером",
            render.Contains("SyncInstanceCount();") &&
            src.Contains("_multiMesh.InstanceCount = instances;"));
        ok &= Check("синхронизация идёт ПОСЛЕ изменения размера буфера",
            render.IndexOf("EnsureBuffer(", StringComparison.Ordinal) <
            render.IndexOf("SyncInstanceCount();", StringComparison.Ordinal));
        ok &= Check("стартовый InstanceCount равен стартовой ёмкости, а не MaxShadows",
            Regex.IsMatch(src, @"InstanceCount\s*=\s*InitialShadowCapacity") &&
            !Regex.IsMatch(src, @"InstanceCount\s*=\s*MaxShadows"));

        GD.Print(ok ? "[SHADOW-BUF] PASS" : "[SHADOW-BUF] FAIL");
        return ok;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[SHADOW-BUF] OK: {what}");
            return true;
        }
        GD.PrintErr($"[SHADOW-BUF] FAIL: {what}");
        return false;
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