using System;
using System.Collections.Generic;
using Godot;

namespace Game.Simulation;

/// <summary>
/// Тонкий фасад над ZoneManager для Farm-зон.
/// Сохраняет старый API 1-в-1 (FarmingTool, GardenController, CropGrowthManager,
/// PlantingJobHandler, FarmZoneRenderer, SelectTool компилируются без правок).
/// </summary>
public sealed class FarmZoneManager
{
    public static FarmZoneManager Instance { get; } = new();

    private ZoneManager Z => ZoneManager.Instance;

    public FarmZone HoveredZone => Adapt(Z.HoveredZone);
    public FarmZone SelectedZone => Adapt(Z.SelectedZone);

    private readonly Dictionary<Action<FarmZone>, Action<Zone>> _hoveredHandlers = new();
    private readonly Dictionary<Action<FarmZone>, Action<Zone>> _selectedHandlers = new();
    private readonly Dictionary<Action<FarmZone>, Action<Zone>> _autoPlantHandlers = new();
    private readonly object _evtLock = new();

    public event Action OnZonesUpdated
    {
        add => Z.OnZonesUpdated += value;
        remove => Z.OnZonesUpdated -= value;
    }
    public event Action<FarmZone> OnZoneHovered
    {
        add
        {
            if (value == null) return;
            lock (_evtLock)
            {
                // FIX (bughunt 12-13): повторная подписка тем же делегатом раньше
                // заводила ВТОРОЙ wrapper и затирала ссылку на первый в словаре.
                // Первый оставался подписан на ZoneManager навсегда (утечка +
                // обработчик звался дважды), а Remove снимал только последний —
                // после «отписки» события продолжали приходить. Теперь старый
                // wrapper снимаем ПЕРЕД подпиской нового: подписка идемпотентна.
                if (_hoveredHandlers.Remove(value, out var previousWrapper))
                    Z.OnZoneHovered -= previousWrapper;
                Action<Zone> wrapper = z => value(Adapt(z));
                _hoveredHandlers[value] = wrapper;
                Z.OnZoneHovered += wrapper;
            }
        }
        remove
        {
            if (value == null) return;
            lock (_evtLock)
            {
                if (_hoveredHandlers.Remove(value, out var wrapper))
                    Z.OnZoneHovered -= wrapper;
            }
        }
    }
    public event Action<FarmZone> OnZoneSelected
    {
        add
        {
            if (value == null) return;
            lock (_evtLock)
            {
                // FIX (bughunt 12-13): см. OnZoneHovered — снимаем прежний wrapper,
                // иначе повторная подписка = утечка + двойные события.
                if (_selectedHandlers.Remove(value, out var previousWrapper))
                    Z.OnZoneSelected -= previousWrapper;
                Action<Zone> wrapper = z => value(Adapt(z));
                _selectedHandlers[value] = wrapper;
                Z.OnZoneSelected += wrapper;
            }
        }
        remove
        {
            if (value == null) return;
            lock (_evtLock)
            {
                if (_selectedHandlers.Remove(value, out var wrapper))
                    Z.OnZoneSelected -= wrapper;
            }
        }
    }
    public event Action OnZoneDeselected
    {
        add => Z.OnZoneDeselected += value;
        remove => Z.OnZoneDeselected -= value;
    }
    public event Action<FarmZone> OnAutoPlantChanged
    {
        add
        {
            if (value == null) return;
            lock (_evtLock)
            {
                // FIX (bughunt 12-13): см. OnZoneHovered — снимаем прежний wrapper,
                // иначе повторная подписка = утечка + двойные события.
                if (_autoPlantHandlers.Remove(value, out var previousWrapper))
                    Z.OnAutoPlantChanged -= previousWrapper;
                Action<Zone> wrapper = z => value(Adapt(z));
                _autoPlantHandlers[value] = wrapper;
                Z.OnAutoPlantChanged += wrapper;
            }
        }
        remove
        {
            if (value == null) return;
            lock (_evtLock)
            {
                if (_autoPlantHandlers.Remove(value, out var wrapper))
                    Z.OnAutoPlantChanged -= wrapper;
            }
        }
    }

    private static FarmZone Adapt(Zone z)
    {
        if (z == null || z.Kind != ZoneKind.Farm) return null;
        var f = new FarmZone(z.Id, z.Name, z.Tiles)
        {
            AutoPlantEnabled = z.AutoPlantEnabled,
            RequiredSeedItem = z.RequiredSeedItem,
            // ВАЖНО: культура зоны обязана ехать в адаптер. Без этой строки любой
            // потребитель фасада (PlantingJobHandler при посадке, окно фермы)
            // получал FarmZone.Crop = Wheat по умолчанию, и выбранный лён на деле
            // сажался пшеницей.
            Crop = z.Crop,
            PlantedCount = z.PlantedCount
        };
        return f;
    }

    /// <summary>
    /// Сменить культуру Farm-зоны (одна культура на зону). Пишет в истинный
    /// владелец — <see cref="Zone.Crop"/> в ZoneManager; адаптер FarmZone только
    /// копия, поэтому запись в него ни на что не влияет.
    /// Возвращает false, если зоны нет или это не Farm-зона.
    /// </summary>
    public bool SetCrop(int zoneId, Game.Core.CropType crop)
    {
        if (!Z.TryGetZoneById(zoneId, out var z) || z == null || z.Kind != ZoneKind.Farm)
            return false;
        // CropType : byte — присваивание атомарно, лок ZoneManager не нужен
        // (поле-свойство публичное и читается рендером/посадкой без лока).
        z.Crop = crop;
        return true;
    }

    /// <summary>Культура Farm-зоны (или Wheat, если зона не найдена/не Farm).</summary>
    public Game.Core.CropType GetCrop(int zoneId)
    {
        if (Z.TryGetZoneById(zoneId, out var z) && z != null && z.Kind == ZoneKind.Farm)
            return z.Crop;
        return Game.Core.CropType.Wheat;
    }

    public void CreateZone(List<(int X, int Y)> tiles)
    {
        Z.CreateFarmZones(tiles);
    }

    /// <summary>
    /// AUDIT task-17 (M-4): восстановить Farm-зону из .zones.json С СОХРАНЁННЫМ id.
    /// false — данные битые/зона уже есть; вызывающая сторона решает, падать ли на
    /// старый путь CreateZone (новый id) как fallback.
    /// </summary>
    public bool RestoreZone(int id, string name, List<(int X, int Y)> tiles,
        bool autoPlant, Game.Core.CropType crop)
    {
        if (!Z.RestoreFarmZoneForPersistence(id, name, tiles, autoPlant, crop, out var zone)
            || zone == null)
            return false;
        if (autoPlant)
            Z.SetAutoPlant(zone.Id, true); // разложить заявки на посадку по грядкам
        return true;
    }

    public void SetAutoPlant(int zoneId, bool enabled)
    {
        Z.SetAutoPlant(zoneId, enabled);
    }

    public void RemoveTiles(List<(int X, int Y)> tilesToRemove) => Z.RemoveTiles(tilesToRemove);

    public void SetHoveredTile(int x, int y) => Z.SetHoveredTile(x, y);

    public void SelectZoneAt(int x, int y)
    {
        var hit = Z.SelectZoneAt(x, y);
        // Совместимость: раньше промах по ферме гасил выбор; теперь выбор един —
        // если попали в Work-зону, это тоже валидный выбор (окно откроет WorkZoneController).
        // Если промах мимо всех — ZoneManager уже снял выбор.
    }

    public void DeselectZone()
    {
        // Гасим только если выбрана именно ферма — чужую (Work) не трогаем.
        if (Z.SelectedZone != null && Z.SelectedZone.Kind == ZoneKind.Farm)
            Z.DeselectZone();
    }

    public bool TryGetZoneAt(int x, int y, out FarmZone zone)
    {
        if (Z.TryGetZoneAt(x, y, out var z) && z.Kind == ZoneKind.Farm)
        {
            zone = Adapt(z);
            return true;
        }
        zone = null;
        return false;
    }

    public bool TryGetZoneById(int zoneId, out FarmZone zone)
    {
        if (Z.TryGetZoneById(zoneId, out var z) && z.Kind == ZoneKind.Farm)
        {
            zone = Adapt(z);
            return true;
        }
        zone = null;
        return false;
    }

    public List<FarmZone> GetAllZones()
    {
        var all = Z.GetAllZones();
        var result = new List<FarmZone>(all.Count);
        foreach (var z in all)
        {
            if (z.Kind == ZoneKind.Farm)
                result.Add(Adapt(z));
        }
        return result;
    }
}
