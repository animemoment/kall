using Game.Core;

namespace Game.Simulation;

public struct CropData
{
    public int X;
    public int Y;
    public int ZoneId;
    /// <summary>Что посажено: сорт определяет спрайт по стадиям и что падает при сборе.</summary>
    public CropType Type;
    public int Stage;           // 0: пусто, 1..4: фазы роста
    public float GrowthTimer;   // Таймер текущей фазы (до 60 сек)
    public bool IsHarvestQueued;
    public bool IsPlantingQueued;
}