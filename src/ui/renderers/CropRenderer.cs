using Godot;
using Game.Core;
using Game.Simulation;
using System;

namespace Game.UI;

/// <summary>
/// Рендер культур: отдельный MultiMesh на пару (CropType, Stage), текстура —
/// res://ui/assets/textures/crops/&lt;id&gt;_s&lt;stage&gt;.png (id из CropRegistry.Get).
/// Всё ленивое: на старте не грузится ни одна из 72 текстур и не создаётся ни
/// одного MultiMesh — пара появляется в мире, только тогда создаются её
/// MultiMesh и текстура.
///
/// Данные — CropGrowthManager.SnapshotQueue (позиции И тип клетки по фазам),
/// потребляются на главном потоке без аллокаций на кадр: корзины (тип,стадия)
/// и их буферы переиспользуются между кадрами.
/// </summary>
public partial class CropRenderer : Node2D
{
	private const int MaxCrops = 16384;
	private const float CropSize = 64f;

	// CropType : byte, значения 0..18. Берём 32 слота (как CropRegistry), чтобы
	// enum можно было расширять без правок рендера.
	private const int MaxCropTypes = 32;
	private const int StageCount = 4;
	private const int ComboCount = MaxCropTypes * StageCount;

	private const string CropArtDir = "res://ui/assets/textures/crops/";
	private const string FallbackCropIcon = "res://ui/assets/textures/icons/Wheat.png";

	// Стартовая ёмкость инстансов на пару: не 16384 на каждую из 72 пар, а по
	// потребности (рост удвоением). Память тратится только под реальные пары.
	private const int InitialComboCapacity = 256;

	private readonly MultiMeshInstance2D[] _instances = new MultiMeshInstance2D[ComboCount];
	private readonly MultiMesh[] _multiMeshes = new MultiMesh[ComboCount];
	private readonly float[][] _renderBuffers = new float[ComboCount][];
	private readonly int[] _capacities = new int[ComboCount];
	private readonly int[] _comboCounts = new int[ComboCount];
	private readonly bool[] _warnedMissingArt = new bool[MaxCropTypes];

	/// <summary>Тени ростков: общий рендерер ставит Main, тикает солнцем.</summary>
	public ItemShadowRenderer ItemShadows { get; set; }

	public override void _Ready()
	{
		// Поверх грядок (ZIndex=0..5), под агентами (ZIndex=10). Текстуры и
		// MultiMesh здесь НЕ создаём — только по факту появления пары в снапшоте.
		ZIndex = 6;
	}

	public override void _Process(double delta)
	{
		if (!CropGrowthManager.Instance.SnapshotQueue.TryDequeue(out var latest))
			return;

		while (CropGrowthManager.Instance.SnapshotQueue.TryDequeue(out var newer))
			latest = newer;

		if (latest.PositionsByStage == null || latest.TypesByStage == null || latest.Counts == null)
			return;

		ItemShadows?.ClearSourceSpots(ItemShadowRenderer.ShadowSource.Crop);

		// ST-38: Фрустум-куллинг (viewport culling) при сборке MultiMesh урожая:
		// не добавлять экземпляры за пределами камеры с запасом 2 клетки, когда растений > 500.
		int totalCrops = 0;
		for (int s = 0; s < StageCount; s++)
			totalCrops += latest.Counts[s];

		bool useCulling = false;
		float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

		if (totalCrops > 500)
		{
			var viewport = GetViewport();
			var cam = viewport?.GetCamera2D();
			if (cam != null && viewport != null)
			{
				Vector2 camPos = cam.GetScreenCenterPosition();
				Vector2 vpSize = viewport.GetVisibleRect().Size;
				Vector2 zoom = cam.Zoom;
				float zx = Math.Max(0.001f, zoom.X);
				float zy = Math.Max(0.001f, zoom.Y);
				float padPx = 2f * CropSize; // 2 клетки запас
				float halfW = (vpSize.X / zx) * 0.5f + padPx;
				float halfH = (vpSize.Y / zy) * 0.5f + padPx;
				minX = camPos.X - halfW;
				maxX = camPos.X + halfW;
				minY = camPos.Y - halfH;
				maxY = camPos.Y + halfH;
				useCulling = true;
			}
		}

		// 1) Разложить клетки снапшота по корзинам (тип, стадия). Счётчики
		//    обнуляются дешёвым Array.Clear (128 int), буферы переиспользуются.
		Array.Clear(_comboCounts, 0, _comboCounts.Length);

		for (int stage = 0; stage < StageCount; stage++)
		{
			int count = Math.Min(latest.Counts[stage], MaxCrops);
			if (count <= 0) continue;

			var positions = latest.PositionsByStage[stage];
			var types = latest.TypesByStage[stage];

			for (int i = 0; i < count; i++)
			{
				var pos = positions[i];
				if (useCulling && (pos.X < minX || pos.X > maxX || pos.Y < minY || pos.Y > maxY))
					continue;

				int typeIdx = types[i];
				// None (0) или мусор — рисуем пшеницей: CropRegistry.Get для
				// незарегистрированного типа тоже отдаёт пшеницу.
				if ((uint)typeIdx >= MaxCropTypes || typeIdx == 0)
					typeIdx = (int)Game.Core.CropType.Wheat;

				int combo = typeIdx * StageCount + stage;
				AppendInstance(combo, pos);
			}

			// Тени: типу тень не нужна (общий батч), позиции фазы уже готовы.
			// Ростки 3-4 стадии выше — им тень, мелочь 1-2 пропускаем (Syx early-out).
			if (stage >= 2)
				ItemShadows?.AddSourceSpots(ItemShadowRenderer.ShadowSource.Crop, positions, count, CropSize);
		}

		// 2) Показать/скрыть пары. MultiMesh и текстура создаются при ПЕРВОМ
		//    появлении пары (лениво), а не все 72 сразу.
		for (int combo = 0; combo < ComboCount; combo++)
		{
			int count = _comboCounts[combo];
			var mm = _multiMeshes[combo];
			if (count == 0)
			{
				if (mm != null && mm.VisibleInstanceCount != 0)
					mm.VisibleInstanceCount = 0;
				continue;
			}

			if (mm == null)
			{
				CreateCombo(combo);
				mm = _multiMeshes[combo];
			}

			mm.VisibleInstanceCount = count;
			// Буфер ровно под InstanceCount: Capacity * 8 float (Transform2D).
			mm.Buffer = _renderBuffers[combo];
		}
	}

	private void AppendInstance(int combo, System.Numerics.Vector2 pos)
	{
		int count = _comboCounts[combo];
		if (count >= _capacities[combo])
			GrowCombo(combo, count + 1);

		int idx = count * 8;
		var buffer = _renderBuffers[combo];

		buffer[idx + 0] = 1.0f;
		buffer[idx + 1] = 0.0f;
		buffer[idx + 2] = 0.0f;
		buffer[idx + 3] = pos.X;

		buffer[idx + 4] = 0.0f;
		buffer[idx + 5] = 1.0f;
		buffer[idx + 6] = 0.0f;
		buffer[idx + 7] = pos.Y;

		_comboCounts[combo] = count + 1;
	}

	/// <summary>
	/// Выделить/расширить буфер корзины (тип, стадия). Рост удвоением от
	/// InitialComboCapacity: все 72 буфера по MaxCrops сразу — это ~9 МБ впустую,
	/// а так память пропорциональна числу реально посаженных пар.
	/// </summary>
	private void GrowCombo(int combo, int needed)
	{
		int capacity = _capacities[combo];
		int newCapacity = capacity > 0 ? capacity : InitialComboCapacity;
		while (newCapacity < needed && newCapacity < MaxCrops)
			newCapacity = Math.Min(MaxCrops, newCapacity * 2);
		if (newCapacity > MaxCrops) newCapacity = MaxCrops;
		if (newCapacity < needed) newCapacity = needed; // страховка (не должно случаться)

		var old = _renderBuffers[combo];
		var buffer = new float[newCapacity * 8];
		if (old != null && old.Length > 0)
			Array.Copy(old, buffer, Math.Min(old.Length, buffer.Length));

		_renderBuffers[combo] = buffer;
		_capacities[combo] = newCapacity;

		var mm = _multiMeshes[combo];
		if (mm != null)
			mm.InstanceCount = newCapacity; // Buffer перезапишется в этом же кадре
	}

	/// <summary>Создать MultiMeshInstance2D пары и загрузить её текстуру (один раз).</summary>
	private void CreateCombo(int combo)
	{
		int stage = combo % StageCount;   // 0..3
		int typeIdx = combo / StageCount; // 0..31
		var cropType = (Game.Core.CropType)typeIdx;

		if (_capacities[combo] <= 0)
		{
			_capacities[combo] = InitialComboCapacity;
			_renderBuffers[combo] = new float[InitialComboCapacity * 8];
		}

		var quadMesh = new QuadMesh { Size = new Vector2(CropSize, CropSize) };
		// R2-02: AABB охватывает мир с запасом (-262144..+262144 px = ±4096 тайлов), чтобы культуры за кромкой 1024 не отсекались.
		var mapAabb = new Aabb(new Godot.Vector3(-262144f, -262144f, -1000f), new Godot.Vector3(524288f, 524288f, 2000f));

		var mm = new MultiMesh
		{
			Mesh = quadMesh,
			TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
			UseColors = false,
			UseCustomData = false,
			InstanceCount = _capacities[combo],
			VisibleInstanceCount = 0,
			CustomAabb = mapAabb
		};
		_multiMeshes[combo] = mm;

		var instance = new MultiMeshInstance2D
		{
			Name = $"Crop_{CropRegistry.Get(cropType).Id}_s{stage + 1}",
			Multimesh = mm,
			Texture = LoadStageTexture(cropType, stage + 1)
		};
		_instances[combo] = instance;
		AddChild(instance);
	}

	/// <summary>
	/// Текстура пары (CropType, Stage): crops/&lt;id&gt;_s&lt;stage&gt;.png, id — из
	/// CropRegistry. Арта нет — один warning на культуру и фолбэк на пшеницу ТОЙ
	/// ЖЕ стадии (форма роста сохраняется), затем на общую иконку Wheat.png.
	/// </summary>
	private Texture2D LoadStageTexture(Game.Core.CropType cropType, int stage)
	{
		CropDefinition def = CropRegistry.Get(cropType);
		string artId = ResolveArtId(def);

		var texture = ResourceRef.Load<Texture2D>($"{CropArtDir}{artId}_s{stage}.png");
		if (texture != null)
			return texture;

		texture = ResourceRef.Load<Texture2D>($"{CropArtDir}wheat_s{stage}.png", FallbackCropIcon);

		int typeIdx = (byte)cropType;
		if ((uint)typeIdx < (uint)MaxCropTypes && !_warnedMissingArt[typeIdx])
		{
			_warnedMissingArt[typeIdx] = true;
			GD.PushWarning($"[CropRenderer] Нет арта для культуры '{def.Id}' " +
						   $"({CropArtDir}{artId}_s{stage}.png) — рисуется пшеницей. " +
						   "Положите <id>_s1..s4.png в ui/assets/textures/crops/.");
		}
		return texture;
	}

	/// <summary>
	/// id арта для культуры. У большинства совпадает с CropRegistry.Id; исключение
	/// — пенька: id в реестре «hemp_fiber», файлы арта называются hemp_s*.png.
	/// </summary>
	private static string ResolveArtId(in CropDefinition def)
	{
		if (def.Type == Game.Core.CropType.HempFiber)
			return "hemp";
		return def.Id;
	}
}
