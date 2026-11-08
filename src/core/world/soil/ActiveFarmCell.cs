using System.Runtime.InteropServices;
using Game.Core;

namespace Game.Core.Soil;

/// <summary>
/// Точечная ячейка активной фермы (Sparse Farm Domain).
/// Упакована ровно в 16 байт с естественным выравниванием полей для максимальной скорости CPU-конвейера (L1-кэш).
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct ActiveFarmCell
{
    public int Id;                  // offset 0 (4 байта): Уникальный ID грядки
    public short X;                 // offset 4 (2 байта): Координата X на карте
    public short Y;                 // offset 6 (2 байта): Координата Y на карте
    public byte Moisture;           // offset 8 (1 байт): Текущая влажность (0..255)
    public byte Fertility;          // offset 9 (1 байт): Текущее плодородие (0..255)
    public byte SoilQuality;        // offset 10 (1 байт): Базовое качество почвы (0..255)
    public byte FertilizerBonus;    // offset 11 (1 байт): Активный бонус удобрений/компоста
    public byte WeedLevel;          // offset 12 (1 байт): Засорённость сорняками (0..100)
    public byte CropType;           // offset 13 (1 байт): Текущая культура ((byte)Game.Core.CropType)
    public byte PreviousCrop;       // offset 14 (1 байт): Предыдущая культура для севооборота
    public byte WaterSourceDist;    // offset 15 (1 байт): Дистанция до канала (0 = нет)

    public ActiveFarmCell(int id, short x, short y, byte soilQuality, byte moisture = 120, byte fertility = 120)
    {
        Id = id;
        X = x;
        Y = y;
        Moisture = moisture;
        Fertility = fertility;
        SoilQuality = soilQuality;
        FertilizerBonus = 0;
        WeedLevel = 0;
        CropType = (byte)Game.Core.CropType.None;
        PreviousCrop = (byte)Game.Core.CropType.None;
        WaterSourceDist = 0;
    }
}
