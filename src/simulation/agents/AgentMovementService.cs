using System;
using System.Numerics;
using System.Threading;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.Simulation;

public sealed class AgentMovementService
{
    private const float MoveSpeed = 120.0f;
    private const float WaterSpeedMultiplier = 0.45f;
    private const float AgentRadius = 8.0f;
    private const int TileShift = 6; // 1 << 6 = 64 (TileSize)

    private static readonly (int dx, int dy)[] PushAwayNeighbors = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // ==================================================================
    // R-04 / R-10: КЭШ ТЕРРЕЙНА ЗА КРОМКОЙ ОСТРОВА
    //
    // Дефект: `IsTileBlocked` для клеток вне окна колонии звал
    // `WorldModel.TerrainAt` → полный `UnifiedWorldGenerator.SampleCell`
    // (тектоника/Voronoi/18 полей) БЕЗ кэша, по 1–5 раз на суб-степ на агента.
    // Агенты штатно ходят за кромку (работа в мире), поэтому именно это, а не A*,
    // было главной причиной невыполнения бюджета `Phase2_ParallelUpdate`.
    //
    // Террейн детерминирован от (seed, origin, координата), поэтому кэшируем ответ
    // по клетке. Ключ — colony-координаты (они же уникальны при фиксированном
    // origin), значение — 1 байт «непроходимо». Кэш инвалидируется при смене
    // seed/origin (штамп) и ограничен по размеру, чтобы не расти бесконечно
    // при дальних походах.
    // ==================================================================
    private const int WorldTerrainCacheCap = 262_144; // ~256 КБ на записи словаря

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _worldTerrainCache = new();
    private static long _worldTerrainStamp = long.MinValue;

    /// <summary>R-04/R-10: террейн мира с кэшем по клетке (вода/гора = непроходимо).</summary>
    private static bool IsWorldTileBlockedCached(long wx, long wy, int tx, int ty)
    {
        // Штамп «мир тот же»: seed и origin. Смена любого — кэш невалиден.
        long stamp = unchecked((long)Game.Core.World.WorldModel.Seed
            ^ ((long)((ulong)Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX * 0x9E3779B97F4A7C15UL))
            ^ ((long)((ulong)Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY * 0xC2B2AE3D27D4EB4FUL)));
        if (Volatile.Read(ref _worldTerrainStamp) != stamp)
        {
            _worldTerrainCache.Clear();
            Volatile.Write(ref _worldTerrainStamp, stamp);
        }

        long key = ((long)tx << 32) ^ (uint)ty;
        if (_worldTerrainCache.TryGetValue(key, out byte cached))
            return cached != 0;

        var terrain = Game.Core.World.WorldModel.TerrainAt(wx, wy);
        bool blocked = terrain == Game.Core.WorldStreaming.BaseTerrainKind.Water
                    || terrain == Game.Core.WorldStreaming.BaseTerrainKind.Mountain
                    || Game.Core.World.WorldCostCache.CostAt(wx, wy) == Game.Core.World.WorldCostCache.CostBlocked;

        // Ограничение размера: при переполнении просто не кэшируем новые клетки —
        // деградация до прежнего поведения, а не утечка памяти.
        if (_worldTerrainCache.Count < WorldTerrainCacheCap)
            _worldTerrainCache[key] = blocked ? (byte)1 : (byte)0;
        return blocked;
    }

    /// <summary>
    /// Движение агента к цели с поддержкой иерархического поиска пути.
    /// Если у агента активны путевые точки (HierarchicalPathfinder) — движется
    /// по ним; иначе — напрямую (со скольжением и локальным обходом), а при
    /// большой дистанции запрашивает новый иерархический путь.
    /// Возвращает true, когда агент достиг цели в пределах reachDistance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveTowards(int agentIndex, Vector2 target, float reachDistance, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        // Кулдаун повторных запросов пути (игровые секунды).
        float cd = pool.PathRequestCooldown[agentIndex];
        if (cd > 0f)
        {
            pool.PathRequestCooldown[agentIndex] = Math.Max(0f, cd - deltaTime);
        }

        // ---- Фаза 1: следование по waypoints ----
        if (pool.WaypointCount[agentIndex] > 0)
        {
            int idx = pool.WaypointIndex[agentIndex];
            if (idx >= pool.WaypointCount[agentIndex])
            {
                pool.WaypointCount[agentIndex] = 0;
                pool.WaypointIndex[agentIndex] = 0;
            }
            else
            {
                float wpX = pool.StepTargetX[agentIndex];
                float wpY = pool.StepTargetY[agentIndex];
                if (wpX == 0f && wpY == 0f)
                {
                    int wpBase = agentIndex * AgentPathConfig.MaxWaypoints;
                    wpX = pool.WaypointX[wpBase + idx] * ctx.TileSize + 32f;
                    wpY = pool.WaypointY[wpBase + idx] * ctx.TileSize + 32f;
                    pool.StepTargetX[agentIndex] = wpX;
                    pool.StepTargetY[agentIndex] = wpY;
                }

                bool reachedWp = MoveToPoint(agentIndex, new Vector2(wpX, wpY), 16f, deltaTime, pool, ctx);

                if (reachedWp)
                {
                    // Пропускаем цепочку близких точек сразу: иначе агент
                    // топчется в середине пути (пошаговая остановка на каждой
                    // точке сглаженного пути давала осцилляцию туда-обратно).
                    int next = idx + 1;
                    int total = pool.WaypointCount[agentIndex];
                    int wpBase2 = agentIndex * AgentPathConfig.MaxWaypoints;
                    float px = pool.PositionX[agentIndex];
                    float py = pool.PositionY[agentIndex];
                    while (next < total)
                    {
                        float nx = pool.WaypointX[wpBase2 + next] * ctx.TileSize + 32f;
                        float ny = pool.WaypointY[wpBase2 + next] * ctx.TileSize + 32f;
                        float ddx = nx - px;
                        float ddy = ny - py;
                        if (ddx * ddx + ddy * ddy > 24f * 24f)
                            break;
                        next++;
                    }
                    pool.WaypointIndex[agentIndex] = (byte)next;
                    pool.StuckTimer[agentIndex] = 0f;
                    if (next >= total)
                    {
                        pool.WaypointCount[agentIndex] = 0;
                        pool.WaypointIndex[agentIndex] = 0;
                        pool.StepTargetX[agentIndex] = target.X;
                        pool.StepTargetY[agentIndex] = target.Y;
                        // Цепочка завершена этим тиком — проваливаемся в фазу 2
                        // сразу (иначе агент стоит один лишний тик: return false ниже).
                        goto Phase2;
                    }
                    else
                    {
                        pool.StepTargetX[agentIndex] = pool.WaypointX[wpBase2 + next] * ctx.TileSize + 32f;
                        pool.StepTargetY[agentIndex] = pool.WaypointY[wpBase2 + next] * ctx.TileSize + 32f;
                    }
                }
                else if (pool.StuckTimer[agentIndex] >= 1.5f)
                {
                    // Путь устарел (стена/толпа): отбрасываем и пересчитаем —
                    // ниже сработает прямой fallback + запрос нового пути.
                    pool.WaypointCount[agentIndex] = 0;
                    pool.WaypointIndex[agentIndex] = 0;
                    pool.StepTargetX[agentIndex] = target.X;
                    pool.StepTargetY[agentIndex] = target.Y;
                    // Waypoints сброшены — сразу прямое движение этим тиком.
                    goto Phase2;
                }
                return false;
            }
        }

    Phase2:
        // ---- Фаза 2: прямое движение (прежнее поведение) ----
        bool reached = MoveToPoint(agentIndex, target, reachDistance, deltaTime, pool, ctx);

        // ---- Фаза 3: запрос иерархического пути для дальних маршрутов или при застревании ----
        if (!reached && pool.WaypointCount[agentIndex] == 0)
        {
            float dx = target.X - pool.PositionX[agentIndex];
            float dy = target.Y - pool.PositionY[agentIndex];
            float minDist = 4.0f * ctx.TileSize;
            bool isStuck = pool.StuckTimer[agentIndex] >= 0.5f;
            if (dx * dx + dy * dy >= minDist * minDist || isStuck)
            {
                // P1 (Phase2-пик): stagger запросов — не все агенты в один тик.
                // Кулдаун и так 1.5с, но тысячи агентов выходят из него синхронно
                // (назначены одним диспатчем) и хором бьют в A*. Разносим по
                // остатку индекса: ~1/4 агентов за тик вместо всех сразу.
                // Джиттер кулдауна при спавне (0..1.5с) не даёт вечной фазовой
                // синхронизации одних и тех же агентов.
                uint gate = Game.Simulation.AgentSimTickGate.Current;
                if (!isStuck && (((uint)agentIndex ^ ((uint)agentIndex >> 4)) & 3u) != (gate & 3u))
                    return reached;
                RequestHierarchicalPath(agentIndex, target, pool, ctx);
            }
        }

        return reached;
    }

    /// <summary>
    /// Запрашивает иерархический путь и кладёт waypoints в пул агента.
    /// Частота — не чаще раза в 0.8-1.5 игровых секунды.
    /// B21/B22: счётчики запросов/успехов + отдельный замер A* (не движения).
    /// </summary>
    private void RequestHierarchicalPath(int agentIndex, Vector2 target, AgentDataPool pool, SimulationContext ctx)
    {
        // P0-1: без счётчика — кулдаун-хит случается на каждого агента каждый тик.
        if (pool.PathRequestCooldown[agentIndex] > 0f)
        {
            return;
        }

        int sx = (int)pool.PositionX[agentIndex] >> TileShift;
        int sy = (int)pool.PositionY[agentIndex] >> TileShift;
        int tx = (int)target.X >> TileShift;
        int ty = (int)target.Y >> TileShift;

        if (sx == tx && sy == ty)
            return;

        // Unified Ultra Pathfinder: абсолютный глобальный мир (0 байт GC, ~1.5 - 5.5 мкс)
        long originX = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX;
        long originY = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY;
        long wsx = originX + sx;
        long wsy = originY + sy;
        long wtx = originX + tx;
        long wty = originY + ty;

        Span<Game.Simulation.Pathfinding.Long2> waypoints = stackalloc Game.Simulation.Pathfinding.Long2[AgentPathConfig.MaxWaypoints];
        if (Game.Simulation.Pathfinding.UnifiedPathfinder.Instance.TryFindPath(wsx, wsy, wtx, wty, waypoints, out int count) && count > 0)
        {
            int cap = Math.Min(count, AgentPathConfig.MaxWaypoints);
            int wpBase = agentIndex * AgentPathConfig.MaxWaypoints;
            int written = 0;

            for (int k = 0; k < cap; k++)
            {
                long lx = waypoints[k].X - originX;
                long ly = waypoints[k].Y - originY;
                if (lx >= short.MinValue && lx <= short.MaxValue && ly >= short.MinValue && ly <= short.MaxValue)
                {
                    pool.WaypointX[wpBase + written] = (short)lx;
                    pool.WaypointY[wpBase + written] = (short)ly;
                    written++;
                }
            }

            if (written > 0)
            {
                pool.WaypointCount[agentIndex] = (byte)written;
                pool.WaypointIndex[agentIndex] = 0;
                pool.StepTargetX[agentIndex] = pool.WaypointX[wpBase] * ctx.TileSize + 32f;
                pool.StepTargetY[agentIndex] = pool.WaypointY[wpBase] * ctx.TileSize + 32f;
                pool.PathRequestCooldown[agentIndex] = 1.5f;
                return;
            }
        }

        // Если цель временно недостижима (тупик / заперт)
        pool.PathRequestCooldown[agentIndex] = 0.5f + (float)ParallelRng.NextDouble() * 0.5f;
    }

    /// <summary>
    /// Прямолинейное движение к точке: скольжение вдоль стен, локальный
    /// обход углов, детектор застревания.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveToPoint(int agentIndex, Vector2 target, float reachDistance, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        float curX = pool.PositionX[agentIndex];
        float curY = pool.PositionY[agentIndex];
        float dx = target.X - curX;
        float dy = target.Y - curY;
        float distSq = dx * dx + dy * dy;

        if (distSq <= reachDistance * reachDistance)
            return true;

        float dist = MathF.Sqrt(distSq);

        int curTx = (int)curX >> TileShift;
        int curTy = (int)curY >> TileShift;

        float speed = MoveSpeed;
        // Усталость > 80: агент еле волочит ноги (лёгкий NeedsJobSystem).
        if (pool.Fatigue[agentIndex] > AgentNeedsConfig.FatigueSlowThreshold)
            speed *= AgentNeedsConfig.FatigueSlowMultiplier;
        if ((uint)curTx < (uint)ctx.MapWidth && (uint)curTy < (uint)ctx.MapHeight && ctx.Ground[curTx, curTy] == TileType.Water)
        {
            speed *= WaterSpeedMultiplier;
        }

        float totalStep = speed * deltaTime;

        // Быстрый путь прибытия: цель ближе полного шага — ставим ровно в цель без перепрыгивания
        if (totalStep >= dist)
        {
            if (!IsStepBlocked(curX, curY, target.X, target.Y, ctx))
            {
                pool.PositionX[agentIndex] = target.X;
                pool.PositionY[agentIndex] = target.Y;
                pool.StuckTimer[agentIndex] = 0f;
                return true;
            }
            return distSq <= (reachDistance + 6f) * (reachDistance + 6f);
        }

        // Быстрый путь на открытой местности (90%+ случаев): шаг без препятствий
        float fastInvDist = 1.0f / dist;
        float fastStepX = dx * fastInvDist * totalStep;
        float fastStepY = dy * fastInvDist * totalStep;
        float fastDesiredX = curX + fastStepX;
        float fastDesiredY = curY + fastStepY;

        if (!IsStepBlocked(curX, curY, fastDesiredX, fastDesiredY, ctx))
        {
            pool.PositionX[agentIndex] = fastDesiredX;
            pool.PositionY[agentIndex] = fastDesiredY;
            pool.StuckTimer[agentIndex] = 0f;
            return false;
        }

        // Препятствие на прямом пути — дробление на суб-шаги со скольжением вдоль стен
        int subSteps = (int)(totalStep / 16f) + 1;
        if (subSteps > 4) subSteps = 4;
        if (subSteps < 1) subSteps = 1;
        float subDt = deltaTime / subSteps;
        bool anyMoved = false;

        for (int s = 0; s < subSteps; s++)
        {
            float sx = pool.PositionX[agentIndex];
            float sy = pool.PositionY[agentIndex];
            float sdx = target.X - sx;
            float sdy = target.Y - sy;
            float sDistSq = sdx * sdx + sdy * sdy;
            if (sDistSq <= reachDistance * reachDistance)
                return true;
            float sDist = MathF.Sqrt(sDistSq);
            float invDist = 1.0f / sDist;
            float stepLen = speed * subDt;
            if (stepLen >= sDist)
            {
                // Дошли бы до цели за этот суб-шаг: ставим точно в цель,
                // дёрганья из-за перепрыгивания мимо нет.
                if (!IsStepBlocked(sx, sy, target.X, target.Y, ctx))
                {
                    pool.PositionX[agentIndex] = target.X;
                    pool.PositionY[agentIndex] = target.Y;
                    pool.StuckTimer[agentIndex] = 0f;
                    return true;
                }
                // Цель внутри стены (редкий stand-кейс): считаем дошедшим,
                // коммит-фаза перепроверит дистанцию сама.
                return sDistSq <= (reachDistance + 6f) * (reachDistance + 6f);
            }
            float stepX = sdx * invDist * stepLen;
            float stepY = sdy * invDist * stepLen;

            float desiredX = sx + stepX;
            float desiredY = sy + stepY;

            // 1. Прямой шаг (с проверкой углов через IsStepBlocked)
            if (!IsStepBlocked(sx, sy, desiredX, desiredY, ctx))
            {
                pool.PositionX[agentIndex] = desiredX;
                pool.PositionY[agentIndex] = desiredY;
                anyMoved = true;
                continue;
            }

            // 2. Скольжение вдоль препятствий — детерминированно по
            // доминирующей оси (не минимум остатка: он давал чередование
            // X/Y каждый кадр = дрожание у стены и осцилляцию в углу).
            bool canX = !IsTileBlocked(desiredX, sy, ctx);
            bool canY = !IsTileBlocked(sx, desiredY, ctx);

            if (canX && canY)
            {
                if (MathF.Abs(sdx) >= MathF.Abs(sdy))
                {
                    pool.PositionX[agentIndex] = desiredX;
                    pool.PositionY[agentIndex] = sy;
                }
                else
                {
                    pool.PositionX[agentIndex] = sx;
                    pool.PositionY[agentIndex] = desiredY;
                }
                anyMoved = true;
                continue;
            }
            else if (canX)
            {
                pool.PositionX[agentIndex] = desiredX;
                pool.PositionY[agentIndex] = sy;
                anyMoved = true;
                continue;
            }
            else if (canY)
            {
                pool.PositionX[agentIndex] = sx;
                pool.PositionY[agentIndex] = desiredY;
                anyMoved = true;
                continue;
            }
            else
            {
                // Суб-шаг упёрся: дальше только обход угла (ниже).
                break;
            }
        }

        if (anyMoved)
        {
            // Скольжение вдоль препятствия (ветки canX/canY выше) ДВИГАЕТ агента,
            // но не обязательно ПРИБЛИЖАЕТ к цели: у берега крупной реки или
            // стены агент едет вдоль препятствия и дистанцию не сокращает.
            // Безусловный сброс StuckTimer здесь означал, что таймер
            // застревания не набирается НИКОГДА, и штатный вотчдог хендлеров
            // (StuckTimer >= 3.0f: TreeChoppingJobHandler.cs:72,
            // ConstructionJobHandler.cs:97, MiningJobHandler.cs:79, ...) не
            // срабатывал — агент бесконечно шёл «прямо» к недостижимой цели
            // за рекой (баг #3). Теперь сбрасываем таймер только при РЕАЛЬНОМ
            // прогрессе — сокращении дистанции до цели относительно значения
            // на входе в метод (distSq, строка 204); иначе копим время, и
            // хендлер снимает задачу с кулдауном недостижимости.
            // Ветка «3. Локальный обход углов» ниже (StuckTimer += deltaTime) в
            // этом вызове НЕ выполняется — здесь return, двойного инкремента нет.
            float ndx = target.X - pool.PositionX[agentIndex];
            float ndy = target.Y - pool.PositionY[agentIndex];
            float afterSq = ndx * ndx + ndy * ndy;
            if (afterSq < distSq)
                pool.StuckTimer[agentIndex] = 0f;
            else
                pool.StuckTimer[agentIndex] += deltaTime;
            return false;
        }

        // 3. Локальный обход углов (не чаще 1 раза в 2.0 секунды при застревании)
        float previousStuck = pool.StuckTimer[agentIndex];
        pool.StuckTimer[agentIndex] += deltaTime;
        float currentStuck = pool.StuckTimer[agentIndex];

        if (currentStuck >= 0.5f && (int)(previousStuck * 0.5f) != (int)(currentStuck * 0.5f))
        {
            int targetTileX = (int)target.X >> TileShift;
            int targetTileY = (int)target.Y >> TileShift;

            long originX = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX;
            long originY = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY;
            long curWx = originX + curTx;
            long curWy = originY + curTy;
            long targetWx = originX + targetTileX;
            long targetWy = originY + targetTileY;

            Span<Game.Simulation.Pathfinding.Long2> steerSteps = stackalloc Game.Simulation.Pathfinding.Long2[16];
            if (Game.Simulation.Pathfinding.UnifiedPathfinder.Instance.TryFindSteeringPath(curWx, curWy, targetWx, targetWy, steerSteps, out int steerCount) && steerCount > 1)
            {
                long nextWx = steerSteps[1].X;
                long nextWy = steerSteps[1].Y;
                float dirX = (nextWx - curWx);
                float dirY = (nextWy - curWy);
                float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
                if (len > 0.01f)
                {
                    dirX /= len;
                    dirY /= len;
                    float detourX = curX + dirX * (speed * deltaTime);
                    float detourY = curY + dirY * (speed * deltaTime);
                    if (!IsTileBlocked(detourX, detourY, ctx))
                    {
                        pool.PositionX[agentIndex] = detourX;
                        pool.PositionY[agentIndex] = detourY;
                        return false;
                    }
                }
            }
        }

        float reachWithMargin = reachDistance + 6.0f;
        if (distSq <= reachWithMargin * reachWithMargin)
            return true;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsCellBlocked(int tx, int ty, SimulationContext ctx)
    {
        if ((uint)tx >= (uint)ctx.MapWidth || (uint)ty >= (uint)ctx.MapHeight)
        {
            if (Game.Core.World.WorldModel.Seed != 0)
            {
                long wx = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX + tx;
                long wy = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY + ty;
                return !Game.Core.World.WorldCostCache.IsWalkable(wx, wy);
            }
            return true;
        }

        byte[] cost = HierarchicalPathfinder.Instance.Cost;
        if (cost != null && HierarchicalPathfinder.Instance.MapWidth == ctx.MapWidth)
        {
            return cost[ty * ctx.MapWidth + tx] == HierarchicalPathfinder.CostBlocked;
        }

        if (ctx.SolidWalls != null && ctx.SolidWalls[tx, ty])
            return true;

        if (ctx.Ground != null && (ctx.Ground[tx, ty] == TileType.Mountain || ctx.Ground[tx, ty] == TileType.Water))
            return true;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsTileBlocked(float worldX, float worldY, SimulationContext ctx)
    {
        int tx = (int)worldX >> TileShift;
        int ty = (int)worldY >> TileShift;

        // Проверка границ: внутри карты колонии проверяем сетку cost/arrays,
        // за пределами — проверяем террейн стримингового мира
        if ((uint)tx >= (uint)ctx.MapWidth || (uint)ty >= (uint)ctx.MapHeight)
        {
            if (Game.Core.World.WorldModel.Seed != 0)
            {
                long wx = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX + tx;
                long wy = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY + ty;
                return !Game.Core.World.WorldCostCache.IsWalkable(wx, wy);
            }
            return true;
        }

        byte[] cost = HierarchicalPathfinder.Instance.Cost;
        if (cost != null && HierarchicalPathfinder.Instance.MapWidth == ctx.MapWidth)
        {
            int w = ctx.MapWidth;
            int idx = ty * w + tx;
            if (cost[idx] == HierarchicalPathfinder.CostBlocked)
                return true;

            int subX = (int)worldX & 63;
            int subY = (int)worldY & 63;

            if (subX < AgentRadius && tx > 0 && cost[idx - 1] == HierarchicalPathfinder.CostBlocked) return true;
            if (subX > 64 - AgentRadius && tx < w - 1 && cost[idx + 1] == HierarchicalPathfinder.CostBlocked) return true;
            if (subY < AgentRadius && ty > 0 && cost[idx - w] == HierarchicalPathfinder.CostBlocked) return true;
            if (subY > 64 - AgentRadius && ty < ctx.MapHeight - 1 && cost[idx + w] == HierarchicalPathfinder.CostBlocked) return true;

            return false;
        }

        // Fallback: прямое чтение 2D-массивов (если оверлей пути еще не построен)
        if (ctx.SolidWalls != null && ctx.SolidWalls[tx, ty])
            return true;

        // Гора и вода блокируют движение агентов
        if (ctx.Ground != null && (ctx.Ground[tx, ty] == TileType.Mountain || ctx.Ground[tx, ty] == TileType.Water))
            return true;

        int fallbackSubX = (int)worldX & 63;
        int fallbackSubY = (int)worldY & 63;

        if (fallbackSubX < AgentRadius && tx > 0 && ((ctx.SolidWalls != null && ctx.SolidWalls[tx - 1, ty]) || (ctx.Ground != null && (ctx.Ground[tx - 1, ty] == TileType.Mountain || ctx.Ground[tx - 1, ty] == TileType.Water)))) return true;
        if (fallbackSubX > 64 - AgentRadius && tx < ctx.MapWidth - 1 && ((ctx.SolidWalls != null && ctx.SolidWalls[tx + 1, ty]) || (ctx.Ground != null && (ctx.Ground[tx + 1, ty] == TileType.Mountain || ctx.Ground[tx + 1, ty] == TileType.Water)))) return true;
        if (fallbackSubY < AgentRadius && ty > 0 && ((ctx.SolidWalls != null && ctx.SolidWalls[tx, ty - 1]) || (ctx.Ground != null && (ctx.Ground[tx, ty - 1] == TileType.Mountain || ctx.Ground[tx, ty - 1] == TileType.Water)))) return true;
        if (fallbackSubY > 64 - AgentRadius && ty < ctx.MapHeight - 1 && ((ctx.SolidWalls != null && ctx.SolidWalls[tx, ty + 1]) || (ctx.Ground != null && (ctx.Ground[tx, ty + 1] == TileType.Mountain || ctx.Ground[tx, ty + 1] == TileType.Water)))) return true;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsStepBlocked(float startX, float startY, float endX, float endY, SimulationContext ctx)
    {
        if (IsTileBlocked(endX, endY, ctx))
            return true;

        int sx = (int)startX >> TileShift;
        int sy = (int)startY >> TileShift;
        int ex = (int)endX >> TileShift;
        int ey = (int)endY >> TileShift;

        if (sx != ex && sy != ey)
        {
            int dx = ex - sx;
            int dy = ey - sy;
            if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1)
            {
                bool corner1Free = !IsCellBlocked(sx + dx, sy, ctx);
                bool corner2Free = !IsCellBlocked(sx, sy + dy, ctx);
                if (!HierarchicalPathfinder.IsDiagonalPassable(corner1Free, corner2Free))
                    return true;
            }
        }

        return false;
    }

    public void EjectFromWall(int agentIndex, int wallX, int wallY, AgentDataPool pool, SimulationContext ctx)
    {
        if ((uint)wallX >= (uint)ctx.MapWidth || (uint)wallY >= (uint)ctx.MapHeight)
            return;

        if (GridHelper.TryFindNearestFreeTile(wallX, wallY, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, 4, out var freeTile))
        {
            float px = (freeTile.X << TileShift) + 32f;
            float py = (freeTile.Y << TileShift) + 32f;
            pool.PositionX[agentIndex] = px;
            pool.PositionY[agentIndex] = py;
            pool.TargetPositionX[agentIndex] = px;
            pool.TargetPositionY[agentIndex] = py;
            pool.CurrentCellX[agentIndex] = freeTile.X;
            pool.CurrentCellY[agentIndex] = freeTile.Y;
            pool.WaypointCount[agentIndex] = 0;
            pool.WaypointIndex[agentIndex] = 0;
            pool.StuckTimer[agentIndex] = 0f;
        }
    }

    public void PushAgentAwayFrom(int agentIndex, int fromX, int fromY, AgentDataPool pool, SimulationContext ctx)
    {
        // P0.4: без heap-alloc в горячем пути — направления как static readonly вместо new (int,int)[4].
        foreach (var (dx, dy) in PushAwayNeighbors)
        {
            int nx = fromX + dx;
            int ny = fromY + dy;
            if ((uint)nx < (uint)ctx.MapWidth && (uint)ny < (uint)ctx.MapHeight)
            {
                if (!ctx.SolidWalls[nx, ny] &&
                    (ctx.TreeOnGrass == null || !ctx.TreeOnGrass[nx, ny]) &&
                    (ctx.StoneOnGrass == null || !ctx.StoneOnGrass[nx, ny]) &&
                    ctx.Ground[nx, ny] == TileType.Grass &&
                    !ctx.SpatialGrid.IsCellOvercrowded(nx, ny, 5, pool))
                {
                    pool.States[agentIndex] = AgentState.Evacuating;
                    pool.TargetPositionX[agentIndex] = (nx << TileShift) + 32f;
                    pool.TargetPositionY[agentIndex] = (ny << TileShift) + 32f;
                    return;
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // Мост «WorldPathfinder → пул агента» (RENDER_SYNC_PLAN, Фаза 4.2)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Распаковать путь МИРА (WorldPathfinder.TryFindPath) в ЛОКАЛЬНЫЕ short-вейпоинты
    /// агента — те же, что использует островной H-патфайндер.
    ///
    /// Контракт (tests/TestWorldPathfinder.MovementServiceBridge):
    ///   • path[i] упакован как y*windowWidth + x в координатах ОКНА патфайндера:
    ///     мировая клетка = (windowOriginX + packed % windowWidth, windowOriginY + packed / windowWidth);
    ///   • вейпоинт (lx, ly) ⇒ мировая (agentWorldX + lx, agentWorldY + ly) — LOCAL
    ///     относительно АГЕНТА, чтобы агент шёл по ним как по своим островным;
    ///   • каждый вейпоинт обязан попадать в short (±32768): дальше — честный false
    ///     (вызывающий даст кулдаун), а не выдуманный путь;
    ///   • skipFirst: сколько начальных вейпоинтов пропустить (1 = не класть клетку,
    ///     на которой агент уже стоит; 0 = все).
    /// </summary>
    public static bool TryUnpackWorldWaypoints(
        int[] path, int count,
        long windowOriginX, long windowOriginY, int windowWidth,
        long agentWorldX, long agentWorldY,
        short[] outLocalX, short[] outLocalY,
        int skipFirst)
    {
        if (path == null || count <= 0 || count > path.Length)
            return false;
        if (outLocalX == null || outLocalY == null || windowWidth <= 0)
            return false;
        int skip = Math.Clamp(skipFirst, 0, count);
        int outCount = count - skip;
        if (outCount == 0 || outLocalX.Length < outCount || outLocalY.Length < outCount)
            return false;

        for (int i = skip; i < count; i++)
        {
            long worldX = windowOriginX + (path[i] % windowWidth);
            long worldY = windowOriginY + (path[i] / windowWidth);
            long lx = worldX - agentWorldX;
            long ly = worldY - agentWorldY;
            if (lx < short.MinValue || lx > short.MaxValue || ly < short.MinValue || ly > short.MaxValue)
                return false;
            outLocalX[i - skip] = (short)lx;
            outLocalY[i - skip] = (short)ly;
        }
        return true;
    }
}