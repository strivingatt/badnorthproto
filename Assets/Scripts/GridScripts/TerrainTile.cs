using UnityEngine;

/// <summary>
/// Компонент одного тайла местности. Висит на каждом GameObject'е тайла,
/// созданном TerrainGenerator'ом. Хранит позицию в сетке и тип местности,
/// синхронизирует блокировку клетки с GridManager (вода = blocked).
///
/// Оптимизация (минимальная): для GPU Instancing в материале тайла включи
/// Enable GPU Instancing — это настройка материала в инспекторе, не кода.
/// Тогда все тайлы одного типа рисуются одним draw call.
/// </summary>
public class TerrainTile : MonoBehaviour
{
    [Tooltip("Тип местности этого тайла. Назначается через Init()/SetType(), " +
             "значение в инспекторе используется только как дефолт для ручной расстановки.")]
    public TerrainType type = TerrainType.Grass;

    /// <summary>Клетка сетки, которую занимает тайл (x, y = gridX, gridZ).</summary>
    private Vector2Int gridPos;

    /// <summary>Позиция тайла в клетках сетки.</summary>
    public Vector2Int GridPos => gridPos;

    /// <summary>
    /// Инициализация тайла после Instantiate: сохраняет данные, ставит объект
    /// в центр клетки и блокирует клетку, если это вода.
    /// </summary>
    public void Init(Vector2Int cell, TerrainType tileType)
    {
        gridPos = cell;
        type = tileType;

        if (GridManager.Instance != null)
        {
            transform.position = GridManager.Instance.GridToWorld(cell);

            // Вода блокирует клетку; трава — явно разблокируется (на случай,
            // если клетка была заблокирована ранее, например тестовым камнем).
            GridManager.Instance.SetBlocked(cell, type == TerrainType.Water);
        }
    }

    /// <summary>
    /// Смена типа тайла в рантайме (например, береговая эрозия потом).
    /// Обновляет blocked в GridManager. В будущем здесь же будет смена
    /// меша/материала под новый тип.
    /// </summary>
    public void SetType(TerrainType newType)
    {
        if (newType == type) return;

        type = newType;

        if (GridManager.Instance != null)
        {
            // Вода и будущие непроходимые типы блокируют клетку, остальное — нет.
            GridManager.Instance.SetBlocked(gridPos, type == TerrainType.Water);
        }

        // TODO (будущее): подменить MeshFilter.sharedMesh / материал под newType.
    }
}
