namespace Game.Simulation;

/// <summary>
/// J-07: дешёвая сводка по агентам для HUD.
///
/// Раньше HUD считал «занято» как `население − бездельники`, где «население» —
/// плановое число (550 по умолчанию), а «бездельники» — размер плоского пула
/// свободных. В «работающих» попадало всё, что не Idle: Worker'ы зон (они по
/// дизайну НЕ лежат в общем пуле), эвакуирующиеся, агенты с маршрутами — отсюда
/// 200–300 фантомных «работа.т» при отсутствии реальной работы.
///
/// Теперь счётчики считаются по фактическому состоянию пула агентов в sim-потоке
/// (раз в секунду, O(N) — это микросекунды на 2k агентов) и публикуются здесь.
/// Значения — обычные int: запись/чтение атомарны на всех поддерживаемых
/// платформах, а точность «на глаз» для HUD достаточна.
/// </summary>
public static class SimStats
{
    /// <summary>Сколько агентов реально в пуле (не плановое население).</summary>
    public static int Population;

    /// <summary>Агенты, у которых есть взятая работа (<c>CurrentJobId &gt;= 0</c>) и они не Idle.</summary>
    public static int WorkingWithJob;

    /// <summary>Агенты в состоянии Idle (без работы, ждут выдачи).</summary>
    public static int Idle;

    /// <summary>Агенты в состоянии Evacuating (не работают, но и не в пуле свободных).</summary>
    public static int Evacuating;

    /// <summary>UI-20: Агенты с критическим уровнем голода (Hunger >= 80).</summary>
    public static int Starving;

    /// <summary>Снимок для чтения с UI-потока одним обращением (без «рваных» пар чисел).</summary>
    public static (int Population, int Working, int Idle, int Evacuating) Snapshot()
        => (Population, WorkingWithJob, Idle, Evacuating);
}