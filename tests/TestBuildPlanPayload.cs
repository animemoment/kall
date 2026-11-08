using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.WorldStreaming.Mutation;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 15-01 / 16-02: тип постройки должен пережить сейв/загрузку.
///
/// Было: `IslandPayload.WriteBuildPlan` не писал тип вообще, а
/// `IslandDomainReplay.StartPlanSites` подставлял на весь батч
/// `DefaultBuildingDefinition`. В игре это значит: построил кровать, сохранил,
/// загрузил — на её месте «постройка по умолчанию» (верстак), с чужими
/// требованиями к материалам. Скамья и всё остальное — так же.
///
/// Проверяем кодек: тип пишется, читается обратно, а старый payload без байта типа
/// продолжает читаться (иначе сломались бы существующие сохранения).
///
/// Запуск: godot --headless --path . --script res://tests/run_build_plan_payload.gd
/// </summary>
public partial class TestBuildPlanPayload : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[BUILD-PLAN] Тип постройки в payload (15-01/16-02)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= TypeRoundTrip();
        ok &= OldPayloadStillReads();
        ok &= SnapshotWritesTypeAndReplayUsesIt();
        return ok;
    }

    private bool TypeRoundTrip()
    {
        var delivered = new List<(ItemId Item, int Count)> { (ItemId.Log, 7) };
        byte[] payload = IslandPayload.WriteBuildPlan(
            ConstructionStage.Supplying, delivered, 0.42f, BuildingType.Bed);

        if (!IslandPayload.TryReadBuildPlan(payload, out var stage,
                out var back, out float wp, out BuildingType type))
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: свежий payload не читается");
            return false;
        }

        if (type != BuildingType.Bed)
        {
            GD.PrintErr($"[BUILD-PLAN] FAIL: тип вернулся как {type}, ожидалась кровать (Bed)");
            return false;
        }
        if (stage != ConstructionStage.Supplying || Math.Abs(wp - 0.42f) > 0.001f)
        {
            GD.PrintErr($"[BUILD-PLAN] FAIL: стадия/прогресс потерялись: stage={stage} wp={wp}");
            return false;
        }
        if (back.Count != 1 || back[0].Item != ItemId.Log || back[0].Count != 7)
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: доставленные материалы потерялись");
            return false;
        }

        GD.Print("[BUILD-PLAN] OK: тип, стадия, прогресс и материалы переживают запись/чтение");
        return true;
    }

    /// <summary>Payload старого формата (без байта типа) обязан читаться.</summary>
    private bool OldPayloadStillReads()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(IslandPayload.SchemaVersion);
        bw.Write((byte)ConstructionStage.Assembling);
        bw.Write(0);          // доставленных материалов нет
        bw.Write(0.5f);       // прогресс — и НИЧЕГО после (старый формат)
        bw.Flush();

        if (!IslandPayload.TryReadBuildPlan(ms.ToArray(), out var stage,
                out _, out float wp, out BuildingType type))
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: payload старого формата перестал читаться");
            return false;
        }
        if (stage != ConstructionStage.Assembling || Math.Abs(wp - 0.5f) > 0.001f)
        {
            GD.PrintErr($"[BUILD-PLAN] FAIL: старый payload прочитан неверно: stage={stage} wp={wp}");
            return false;
        }
        if (type != default)
        {
            GD.PrintErr($"[BUILD-PLAN] FAIL: у старого payload появился тип {type} — реплей возьмёт не то");
            return false;
        }

        GD.Print("[BUILD-PLAN] OK: старый payload читается, тип остаётся пустым (фолбэк)");
        return true;
    }

    /// <summary>Писатель и читатель обязаны быть подключены к сейву и реплею.</summary>
    private bool SnapshotWritesTypeAndReplayUsesIt()
    {
        const string snapPath = "res://src/core/world/mutation/IslandStateSnapshot.cs";
        const string replayPath = "res://src/core/world/mutation/IslandDomainReplay.cs";

        string snap = Godot.FileAccess.GetFileAsString(snapPath);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                snap, @"WriteBuildPlan\([^)]*Definition\.Type"))
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: снимок не передаёт тип постройки в payload");
            return false;
        }

        string replay = Godot.FileAccess.GetFileAsString(replayPath);
        if (!System.Text.RegularExpressions.Regex.IsMatch(replay, @"StructureRegistry\.Get\(\s*type\s*\)"))
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: реплей не выбирает определение по типу из payload");
            return false;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(replay, @"DefaultBuildingDefinition\s*,\s*kind\s*,\s*store"))
        {
            GD.PrintErr("[BUILD-PLAN] FAIL: реплей не передаёт стор в StartPlanSites — тип не прочитается");
            return false;
        }

        GD.Print("[BUILD-PLAN] OK: снимок пишет тип, реплей выбирает определение по типу");
        return true;
    }
}