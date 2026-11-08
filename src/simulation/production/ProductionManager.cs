using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Менеджер производства v1 (task-20): по потребности регистрирует работы
/// «станок перерабатывает сырьё» и снимает их, когда сырья больше нет.
///
/// Логика цикла (полный путь игрока):
///   добыли руду → hauling увёз на склад → ProductionManager видит: у печи есть
///   работа и на складе есть 2 руды → JobBroker-очередь получает Production-работу
///   → агент берёт руду, идёт к печи, работает N секунд, кладёт слиток на землю
///   → GroundItemManager.SpawnItems сам регистрирует hauling → слиток на складе.
///
/// Инварианты:
///  * станции учитываются через события StructureManager (плюс один полный скан на
///    старте — для построек, возведённых до менеджера/восстановленных из сейва);
///  * O(1) выбор рецепта (RecipeRegistry по BuildingType), аллокаций в тике нет
///    (переиспользуемые буферы);
///  * занятую работу (AssignedWorkers > 0) менеджер не трогает — иначе снял бы
///    работу у работающего агента;
///  * Тик вызывается из sim-потока в throttle-блоке AgentSimulationThread (~1с).
/// </summary>
public sealed class ProductionManager
{
    public static ProductionManager Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<(int X, int Y), BuildingType> _stations = new(64);
    private readonly List<StationRef> _tickList = new(64);
    private readonly List<(int X, int Y)> _removeList = new(8);

    // AUDIT task-17 (P3-1): кулдаун станции после неудачной попытки резерва входов.
    // Без него снятая работа пересоздавалась через тик (~1с), агент снова не мог
    // зарезервировать и цикл «взял→не смог→отпустил» жил вечно. Пишется из sim-потока
    // (OnStart/FailStart), читается в Tick под тем же _lock.
    private readonly Dictionary<(int X, int Y), long> _retryAfter = new(8);
    private const long StartFailureCooldownMs = 5000;

    private bool _bootstrapped;

    private readonly struct StationRef
    {
        public readonly int X;
        public readonly int Y;
        public readonly BuildingType Type;
        public StationRef(int x, int y, BuildingType type) { X = x; Y = y; Type = type; }
    }

    /// <summary>Сколько станций под наблюдением (диагностика/тесты).</summary>
    public int StationCount { get { lock (_lock) return _stations.Count; } }

    private ProductionManager()
    {
    }

    /// <summary>Подписаться на структуры и один раз собрать уже существующие станции.</summary>
    private void EnsureBootstrapped()
    {
        if (_bootstrapped)
            return;
        _bootstrapped = true;

        StructureManager.Instance.OnStructureAdded += OnStructureAdded;
        StructureManager.Instance.OnStructureRemoved += OnStructureRemoved;

        // Полный скан один раз: станции, возведённые до первого тика менеджера
        // (загрузка колонии, восстановление структур из дельт).
        Dictionary<(int X, int Y), StructureInstance> all = StructureManager.Instance.GetAllStructures();
        lock (_lock)
        {
            foreach (var kv in all)
            {
                StructureInstance s = kv.Value;
                if (s != null && RecipeRegistry.IsStation(s.Type))
                    _stations[(s.X, s.Y)] = s.Type;
            }
        }
    }

    private void OnStructureAdded(StructureInstance instance)
    {
        if (instance == null || !RecipeRegistry.IsStation(instance.Type))
            return;
        lock (_lock)
        {
            _stations[(instance.X, instance.Y)] = instance.Type;
        }
    }

    private void OnStructureRemoved((int X, int Y) pos, StructureDefinition def)
    {
        lock (_lock)
        {
            _stations.Remove(pos);
            _retryAfter.Remove(pos);
        }
        // Работа снятой станции больше не существует — убираем из индекса.
        JobDispatcher.Instance.UnregisterJobByPos(pos.X, pos.Y, JobTypeId.Production);
    }

    /// <summary>
    /// AUDIT task-17 (P3-1): сообщить, что попытка начать цикл провалилась на резерве
    /// входов. Работа уже снята хендлером; здесь ставим станции кулдаун, чтобы Tick не
    /// пересоздал её мгновенно (склад/куча за это время могут измениться, а спам
    /// «взял→не смог→отпустил» жжёт агентов и логи). Вызывается из sim-потока.
    /// </summary>
    public void NotifyStartFailure(int x, int y)
    {
        long retryAt = Environment.TickCount64 + StartFailureCooldownMs;
        lock (_lock)
            _retryAfter[(x, y)] = retryAt;
    }

    /// <summary>
    /// Тик производства: для каждой станции — есть ли рецепт, подходят ли входы,
    /// есть ли уже работа. Регистрирует/снимает Production-работы.
    /// </summary>
    public void Tick(SimulationContext ctx)
    {
        if (ctx == null)
            return;
        EnsureBootstrapped();

        _tickList.Clear();
        long nowMs = Environment.TickCount64;
        lock (_lock)
        {
            foreach (var kv in _stations)
            {
                // AUDIT task-17 (P3-1): станция на кулдауне после неудачного резерва —
                // не пересоздаём работу раньше времени (иначе цикл «взял→не смог→отпустил»).
                if (_retryAfter.TryGetValue(kv.Key, out long retryAt))
                {
                    if (nowMs < retryAt)
                        continue;
                    _retryAfter.Remove(kv.Key);
                }
                _tickList.Add(new StationRef(kv.Key.X, kv.Key.Y, kv.Value));
            }
        }
        if (_tickList.Count == 0)
            return;

        var index = JobDispatcher.Instance.JobIndex;
        _removeList.Clear();

        for (int i = 0; i < _tickList.Count; i++)
        {
            StationRef st = _tickList[i];

            // Структура могла исчезнуть/замениться — сверяем тип и живость.
            if (!StructureManager.Instance.TryGetOperationalStructure(st.X, st.Y, out var inst)
                || inst.Type != st.Type)
            {
                _removeList.Add((st.X, st.Y));
                JobDispatcher.Instance.UnregisterJobByPos(st.X, st.Y, JobTypeId.Production);
                continue;
            }

            var recipes = RecipeRegistry.GetRecipesForStation(st.Type);
            if (recipes == null || recipes.Count == 0)
                continue;

            int jobId = index.TryGetJobIdByPos(st.X, st.Y, JobTypeId.Production, out bool found);
            if (found && jobId >= 0)
            {
                JobData job = index.GetJobData(jobId);
                if (job.AssignedWorkers > 0)
                    continue; // работают — не трогаем (иначе сорвём чужой цикл)
                Recipe jobRecipe = RecipeRegistry.FindRecipeByInput(st.Type, job.TargetItemId) ?? recipes[0];
                if (!HasInputsAvailable(jobRecipe))
                {
                    JobDispatcher.Instance.UnregisterJobByPos(st.X, st.Y, JobTypeId.Production);
                }
                else
                {
                    continue;
                }
            }

            Recipe activeRecipe = null;
            ItemId activeInput = ItemId.None;
            for (int r = 0; r < recipes.Count; r++)
            {
                if (TryGetAvailableInput(recipes[r], out var inp))
                {
                    activeRecipe = recipes[r];
                    activeInput = inp;
                    break;
                }
            }

            if (activeRecipe == null)
                continue; // сырья нет — станок простаивает, работа не берётся

            if (!JobBroker.FindStandPosition(st.X, st.Y, ctx, out var stand))
                continue; // некуда подойти (застроено/непроходимо)

            int prodJobId = index.RegisterJob(new JobData
            {
                TypeId = JobTypeId.Production,
                ExecutionType = JobExecutionType.Stationary,
                PriorityTier = JobPriorityTier.Production,
                TargetX = st.X,
                TargetY = st.Y,
                StandX = stand.X,
                StandY = stand.Y,
                MaxWorkers = 1,
                WorkDuration = activeRecipe.DurationSec,
                TargetItemCount = activeRecipe.InputCount,
                TargetItemId = activeInput != ItemId.None ? activeInput : activeRecipe.InputItems[0],
            });

            if (prodJobId >= 0 && ZoneJobRouter.Instance != null)
            {
                ZoneJobRouter.Instance.Push(JobTypeId.Production, st.X, st.Y, prodJobId);
            }
        }

        if (_removeList.Count > 0)
        {
            lock (_lock)
            {
                for (int i = 0; i < _removeList.Count; i++)
                    _stations.Remove(_removeList[i]);
            }
        }
    }

    /// <summary>
    /// Есть ли входы рецепта в доступном виде: на складе (свободные, без чужих
    /// резервов) или на земле. Доступность точная по счётчикам менеджеров —
    /// без обхода клеток.
    /// </summary>
    public static bool HasInputsAvailable(Recipe recipe)
    {
        return TryGetAvailableInput(recipe, out _);
    }

    /// <summary>
    /// Найти первый доступный вариант входного сырья для рецепта на складе или на земле.
    /// </summary>
    public static bool TryGetAvailableInput(Recipe recipe, out ItemId availableItem)
    {
        availableItem = ItemId.None;
        if (recipe == null)
            return false;
        for (int i = 0; i < recipe.InputItems.Count; i++)
        {
            ItemId item = recipe.InputItems[i];
            // AUDIT task-17 (P3-1): критерий обязан совпадать с тем, что РЕАЛЬНО может
            // забрать ProductionJobHandler: ровно InputCount из ОДНОГО источника
            // (слот склада резервируется целиком либо входы берутся с земли).
            // Раньше здесь сравнивались суммарные тоталы (GetLiveItemCount) — они не
            // вычитают чужие ReservedOutgoing и не знают про гранулярность слота, из-за
            // чего работа регистрировалась, а резерв не удавался: агент брал работу,
            // FailStart, Tick снова видел «входы есть» и не снимал её — вечный цикл.
            if (StockpileManager.Instance.CanReserveAtLeast(item, recipe.InputCount))
            {
                availableItem = item;
                return true;
            }
            // Земля: берём только если доступного количества хватает на цикл.
            if (GroundItemManager.Instance.GetAvailableCount(item) >= recipe.InputCount)
            {
                availableItem = item;
                return true;
            }
        }
        return false;
    }
}