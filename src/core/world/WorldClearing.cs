using System;

namespace Game.Core.World;

/// <summary>
/// Расчищенные площадки поселений — ПРАВИЛО МИРА, а не пост-обработка окна
/// GenerateColonyWindow.
///
/// Зачем отдельный класс (а не поле в WorldModel): генератор обязан регистрировать
/// площадку в момент расчистки окна колонии, иначе кэш острова говорит «трава», а
/// решётка/чанки/симуляция про те же клетки — «вода» (шов на старте, 149/262144
/// клеток в tests/TestIslandWorldAgreement.cs). Но если генератор начнёт звать
/// WorldModel, в core появится цикл «UnifiedWorldGenerator → WorldModel →
/// WorldCellQuery → UnifiedWorldGenerator» (файлы ссылаются друг на друга). Этот
/// класс — общий словарь правила без зависимостей: его пишет генератор, читает
/// WorldModel, чистит Reset мира.
///
/// Потокобезопасность: массив заменяется целиком (volatile), читатели работают со
/// своим снапшотом без lock — площадок единицы, гонка между регистрацией и чтением
/// безопасна (в худшем случае читатель ответит «не расчищена» один кадр).
/// </summary>
public static class WorldClearing
{
    private readonly record struct Site(long X, long Y, int RadiusSquared);

    private static volatile Site[] _sites = Array.Empty<Site>();

    /// <summary>
    /// Зарегистрировать площадку (центр в АБСОЛЮТНЫХ мировых координатах, радиус в
    /// тайлах). Дедупликация: окно колонии перегенерируется (смена сида, загрузка).
    /// </summary>
    public static void Register(long x, long y, int radius)
    {
        if (radius <= 0)
            return;
        Site[] current = _sites;
        int r2 = radius * radius;

        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].X == x && current[i].Y == y && current[i].RadiusSquared == r2)
                return;
        }

        var next = new Site[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = new Site(x, y, r2);
        _sites = next;
    }

    /// <summary>Попадает ли клетка (абсолютные координаты) в расчищенную площадку.</summary>
    public static bool Contains(long x, long y)
    {
        Site[] sites = _sites;
        for (int i = 0; i < sites.Length; i++)
        {
            long dx = x - sites[i].X;
            long dy = y - sites[i].Y;
            if (dx * dx + dy * dy <= sites[i].RadiusSquared)
                return true;
        }
        return false;
    }

    /// <summary>Число зарегистрированных площадок (диагностика/тесты).</summary>
    public static int Count => _sites.Length;

    /// <summary>Сброс к «площадок нет» — при смене мира/сида (WorldModel.Reset/Configure).</summary>
    public static void Clear() => _sites = Array.Empty<Site>();
}
