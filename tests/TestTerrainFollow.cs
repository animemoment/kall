using System;
using System.Reflection;
using Godot;
using Game.UI;
using Game.Core.WorldStreaming;

namespace Game.Tests;

/// <summary>
/// Живой прогон follow-окна террейна: грузим Main.tscn, ждём инициализацию мира,
/// ставим камеру в центр острова, потом ЗА кромку (юг), и на каждом шаге дампим
/// содержимое _terrainBuffer построчно. Это отвечает на вопрос «почему за кромкой
/// плоская зелень»: буфер обновляется? какие строки нулевые? какой origin применён?
/// </summary>
public partial class TestTerrainFollow : Node
{
	private int _frames;
	private int _phase;
	private const int Px = 64;

	/// <summary>Готово: раннер может закрывать сцену.</summary>
	public bool Done { get; private set; }

	public override void _Process(double delta)
	{
		_frames++;
		if (_phase == 0)
		{
			if (_frames < 600) return; // ждём инициализацию мира
			_phase = 1;
			MoveCamera(512, 512);
			GD.Print("[FOLLOW] камера в центр острова (512,512), ждём окно");
		}
		else if (_phase == 1)
		{
			if (_frames < 780) return;
			DumpBuffer("ЦЕНТР ОСТРОВА");
			_phase = 2;
			MoveCamera(512, 1128);
			GD.Print("[FOLLOW] камера за кромкой (512,1128), ждём окно");
		}
		else if (_phase == 2)
		{
			if (_frames < 1500) return;
			DumpBuffer("ЗА КРОМКОЙ (юг)");
			_phase = 3;
		}
		else if (_phase == 3)
		{
			if (_frames < 1700) return;
			DumpBuffer("ЗА КРОМКОЙ — ещё 200 кадров спустя");
			GD.Print("[FOLLOW] ГОТОВО");
			Done = true;
			GetTree().Quit(0);
		}
	}

	private static void MoveCamera(long tileX, long tileY)
	{
		Camera2D cam = ((SceneTree)Engine.GetMainLoop()).Root.GetViewport().GetCamera2D();
		if (cam == null)
		{
			GD.PrintErr("[FOLLOW] FAIL: Camera2D не найдена");
			return;
		}
		cam.Position = new Vector2(tileX * Px, tileY * Px);
		cam.ForceUpdateTransform();
		GD.Print($"[FOLLOW] камера -> ({tileX},{tileY}) клеток = {tileX * Px},{tileY * Px} px");
	}

	private static void DumpBuffer(string label)
	{
		object renderer = MapRenderer.Instance;
		if (renderer == null)
		{
			GD.PrintErr($"[FOLLOW] {label}: MapRenderer.Instance == null");
			return;
		}
		Type t = renderer.GetType();
		var buf = (byte[])t.GetField("_terrainBuffer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer);
		long ox = (long)(t.GetField("_currentTerrainOriginX", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer) ?? long.MinValue);
		long oy = (long)(t.GetField("_currentTerrainOriginY", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer) ?? long.MinValue);
		object pending = t.GetField("_pendingReadyBuffer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer);
		bool busy = (bool)(t.GetField("_terrainUpdateInProgress", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer) ?? false);
		object mapData = t.GetField("_currentMapData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer)
			?? t.GetField("_pendingMapData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer);

		if (buf == null)
		{
			GD.PrintErr($"[FOLLOW] {label}: буфер не создан (null)");
			return;
		}

		// Мир игры: сид, origin и ПРЯМОЙ сэмпл против решётки в точке камеры.
		ulong seed = UnifiedWorldGenerator.ActiveWorldSeed;
		long wox = UnifiedWorldGenerator.WorldOriginX;
		long woy = UnifiedWorldGenerator.WorldOriginY;
		GD.Print($"[FOLLOW] seed={seed:X16} WorldOrigin=({wox},{woy}) mapData={(mapData != null ? "готов" : "НЕТ")}");
		long camX = ox + 256, camY = oy + 256; // центр окна
		for (long dy = -256; dy <= 256; dy += 128)
		{
			long wy = woy + camY + dy, wx = wox + camX;
			var direct = UnifiedWorldGenerator.SampleCell(seed, 4, wx, wy);
			WorldClimateParams nw = WorldClimateGenerator.SampleBlendedCell(seed, 4, wx & ~15L, wy & ~15L);
			var cNw = UnifiedWorldGenerator.SampleCell(seed, 4, wx, wy, nw);
			GD.Print($"[FOLLOW] y+{dy}: ПРЯМОЙ {direct.Terrain}/{direct.Biome}/{direct.ElevationQ16} | УЗЕЛ {cNw.Terrain}/{cNw.Biome}/{cNw.ElevationQ16} | lat={nw.LatitudeQ16} glac={nw.GlaciationQ16}");
		}

		var counts = new int[6];
		int zeroRows = 0;
		foreach (byte b in buf) counts[b < 6 ? b : 5]++;

		// Построчно: строка «нулевая» (не-заполненная), если она вся = код 0 (трава-по-умолчанию)
		// ИЛИ вся = одному коду. Считаем полосы по 32 строки.
		var bandInfo = new System.Text.StringBuilder();
		for (int band = 0; band < 16; band++)
		{
			int zeros = 0, nonGrass = 0;
			for (int y = band * 32; y < band * 32 + 32 && y < 512; y++)
			{
				int rowNonGrass = 0;
				for (int x = 0; x < 512; x++)
					if (buf[y * 512 + x] != 0) rowNonGrass++;
				if (rowNonGrass == 0) zeros++;
				nonGrass += rowNonGrass;
			}
			bandInfo.Append($"[{band * 32}..{band * 32 + 31}]zg={zeros},ng={nonGrass} ");
			if (zeros == 32) zeroRows += 32;
		}

		GD.Print($"[FOLLOW] ===== {label} =====");
		GD.Print($"[FOLLOW] origin=({ox},{oy}), pending={(pending != null ? "есть" : "нет")}, busy={busy}");
		GD.Print($"[FOLLOW] трава={counts[0]} песок={counts[1]} вода={counts[2]} степь={counts[3]} ледник={counts[4]} горы={counts[5]}");
		GD.Print($"[FOLLOW] полосы (zg=строк целиком трава, ng=не-трава клеток): {bandInfo}");
		GD.Print($"[FOLLOW] строк целиком из травы: {zeroRows}/512");
	}
}