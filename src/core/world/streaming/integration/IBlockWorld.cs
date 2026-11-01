namespace Game.Core.WorldStreaming.Integration;

/// <summary>
/// Контракт мира с персистентными клеточными метками нескольких видов
/// (стены, здания, добытое, вырубленное, склады). Виды — константы
/// <see cref="WorldDeltaKind"/>; каждая запись — «присутствие» метки в клетке,
/// payload — opaque DTO конкретного вида (для здания: байт <c>BuildingType</c>).
/// Наследует IWallWorld: стены — частный случай блока с kind Wall.
/// </summary>
public interface IBlockWorld : IWallWorld
{
    /// <summary>Добавить метку указанного вида в загруженную клетку; false — чанк не загружен или метка уже есть.</summary>
    bool AddBlock(byte kind, long cellX, long cellY);

    /// <summary>
    /// Добавить метку с payload (например, тип здания). Реализации без payload
    /// могут игнорировать data — это допустимо для тестовых миров.
    /// </summary>
    bool AddBlock(byte kind, long cellX, long cellY, global::System.ReadOnlySpan<byte> data)
        => AddBlock(kind, cellX, cellY);

    /// <summary>Payload метки; пусто, если вид не хранит payload или метки нет.</summary>
    bool TryGetBlockData(byte kind, long cellX, long cellY, out byte[] data)
    {
        data = global::System.Array.Empty<byte>();
        return false;
    }

    /// <summary>Снять метку; false — метки нет или чанк не загружен.</summary>
    bool RemoveBlock(byte kind, long cellX, long cellY);

    /// <summary>Есть ли метка указанного вида в клетке (только для загруженных чанков).</summary>
    bool HasBlock(byte kind, long cellX, long cellY);

    /// <summary>Число загруженных в память меток данного вида.</summary>
    int BlockCount(byte kind);

    /// <summary>Payload Building/BuildingPlan: первый байт — <c>BuildingType</c>.</summary>
    static byte BuildingTypeOf(byte[] data)
        => data != null && data.Length >= 1 ? data[0] : (byte)Game.Core.BuildingType.WorkTable;
}