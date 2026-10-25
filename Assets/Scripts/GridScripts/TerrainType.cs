/// <summary>
/// Тип местности клетки сетки. Используется TerrainTile и TerrainGenerator.
/// В будущем расширится: Stone, Sand, Forest и т.д. — каждый новый тип
/// добавляется в конец enum, чтобы не ломать сериализованные значения.
/// </summary>
public enum TerrainType
{
    Grass, // проходима
    Water  // непроходима (blocked в GridManager)
}
