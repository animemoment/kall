using System;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Simulation;
using Game.Simulation.Jobs;

namespace Game.Tests;

/// <summary>
/// §WanderJobSystem: утечка холостого агента. WanderJobSystem выбирал целевую
/// клетку только по <c>ctx.SolidWalls / TreeOnGrass / (Ground == null || water)</c> —
/// при <c>Ground == null</c> (за кромкой острова) проверка воды СБРАСЫВАЛАСЬ, а
/// движение (IsTileBlocked) за кромкой возвращает false. Итог: свободный агент мог
/// уйти в воду / за кромку и выпасть из островной выдачи (зона и сетка
/// бездельников островные) — агент "утёк" из рабочей силы, хотя формально оставался
/// в пуле.
///
/// Тест: набор idle-Laborer'ов расселяется у кромки (внутри острова, ровно на
/// кромке 1024, и "за" кромкой), имитируется 200 тиков блуждания. Ожидалось:
/// (a) агенты не уходят в непроходимое/за кромку, откуда их нельзя поднять,
/// (b) после починки блуждание для клеток за кромкой должно быть ограничено
///     (не подбирать цели за кромкой).
/// </summary>
public partial class TestWanderSystemLeak : RefCounted
{
    public bool Run()
    {
        GD.Print("=====================================================");
        GD.Print("[TEST] §WanderJobSystem: утечка холостого агента");
        GD.Print("=====================================================");

        try
        {
            int agentCount = 4;
            var pool = new AgentDataPool(16);
            int mapWidth = 1024;
            int mapHeight = 1024;

            var ctx = MakeContext(mapWidth, mapHeight);
            var wander = new WanderJobSystem();

            // Расселяем: (0) внутри острова на траве, (1) ровно на кромке,
            // (2) "за" кромкой (за пределами ctx.MapWidth), (3) рядом со стеной.
            var startPos = new (int x, int y)[4]
            {
                (512, 512),   // inside island
                (1023, 512),  // at edge
                (1024, 512),  // beyond island edge
                (400, 400),   // near wall (wall at 450,450)
            };

            for (int i = 0; i < agentCount; i++)
            {
                pool.Classes[i] = AgentClass.Laborer;
                pool.PositionX[i] = startPos[i].x * 64f + 32f;
                pool.PositionY[i] = startPos[i].y * 64f + 32f;
                pool.CurrentJobId[i] = -1;
                pool.CurrentJobType[i] = JobTypeId.None;
                pool.States[i] = AgentState.Idle;
                pool.JobSearchTimer[i] = 0f;
            }

            // Ставим стену на (450,450) — чтобы проверить отскок от неё.
            ctx.SolidWalls[450, 450] = true;

            for (int tick = 0; tick < 200; tick++)
            {
                for (int i = 0; i < agentCount; i++)
                {
                    wander.TryAssignJob(i, pool, ctx);
                    wander.ExecuteParallel(i, 0.016f, pool, ctx);
                }
                for (int i = 0; i < agentCount; i++)
                {
                    if ((i & 3) == (uint)(tick & 3))
                    {
                        pool.JobSearchTimer[i] -= 0.016f;
                        if (pool.JobSearchTimer[i] <= 0f)
                        {
                            pool.JobSearchTimer[i] = 6.0f + (float)ParallelRng.NextDouble() * 6.0f;
                            wander.TryAssignJob(i, pool, ctx);
                        }
                    }
                }
            }

            bool allOk = true;
            // Позиции старта: (0) внутри, (1) на кромке, (2) ЗА кромкой (тест на "не уходит
            // дальше" — фикс держит таких на месте), (3) у стены.
            bool[] wasBeyond = { false, false, true, false };

            for (int i = 0; i < agentCount; i++)
            {
                int tx = (int)(pool.PositionX[i] / 64);
                int ty = (int)(pool.PositionY[i] / 64);
                bool beyond = tx >= mapWidth || ty >= mapHeight || tx < 0 || ty < 0;
                bool inWall = tx >= 0 && ty >= 0 && tx < mapWidth && ty < mapHeight
                              && (ctx.SolidWalls[tx, ty] || ctx.Ground[tx, ty] == TileType.Water || ctx.Ground[tx, ty] == TileType.Mountain);

                GD.Print($"[agent {i}] tile=({tx},{ty}) beyond={beyond} inWall={inWall} wasBeyond={wasBeyond[i]}");

                // Агенты, стартующие внутри острова, должны остаться на проходимой клетке.
                if (!wasBeyond[i] && (beyond || inWall))
                {
                    allOk = false;
                    GD.PrintErr($"[LEAK] agent {i} ушёл в непроходимое/за кромку: beyond={beyond} inWall={inWall}");
                }
                // Агенты, стартовавшие ЗА кромкой, после починки не должны уходить дальше
                // (фикс держит их на месте — они не могут брать островные работы).
                if (wasBeyond[i] && tx < 1023)
                {
                    allOk = false;
                    GD.PrintErr($"[LEAK] агент {i} (был за кромкой) подвинулся к острову = цель не его: beyond={beyond}");
                }
            }

            if (!allOk)
            {
                GD.PrintErr("[P1] §WanderJobSystem FAIL: холостые агенты утекли в непроходимое");
                return false;
            }

            GD.Print("[OK] холостые агенты не уходят в непроходимое / за кромку");
            GD.Print("=====================================================");
            GD.Print("[OK] DONE");
            GD.Print("=====================================================");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TEST FAILED] {ex}");
            return false;
        }
    }

    private static SimulationContext MakeContext(int mapWidth, int mapHeight)
    {
        var ground = new TileType[mapWidth, mapHeight];
        var treeOnGrass = new bool[mapWidth, mapHeight];
        var solidWalls = new bool[mapWidth, mapHeight];
        var walkableTiles = new System.Collections.Generic.List<(int X, int Y)>();

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                ground[x, y] = TileType.Grass;
                walkableTiles.Add((x, y));
            }
        }

        return new SimulationContext(
            ground,
            null, treeOnGrass, solidWalls, walkableTiles,
            new AgentSpatialGrid(mapWidth, mapHeight),
            new AgentMovementService(),
            new Random(0xDE57), 64);
    }
}
