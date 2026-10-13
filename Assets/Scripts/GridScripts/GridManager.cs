using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Управляет логической сеткой: конвертация координат, оккупация клеток юнитами, блокировка клеток.
/// Синглтон — доступ через GridManager.Instance.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Settings")]
    public int size = 24;
    public float cellSize = 1f;

    /// <summary>Занятые клетки и юниты, которые их занимают.</summary>
    private readonly Dictionary<Vector2Int, Unit> occupancy = new Dictionary<Vector2Int, Unit>();

    /// <summary>Клетки, заблокированные для прохода/размещения.</summary>
    private readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GridManager] Дубликат экземпляра удалён.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Debug.Log($"[GridManager] Размер сетки: {size}x{size}, размер клетки: {cellSize}");
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Мировая позиция -> индекс клетки сетки.</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.z / cellSize)
        );
    }

    /// <summary>Индекс клетки -> мировая позиция её центра (y = 0).</summary>
    public Vector3 GridToWorld(Vector2Int cell)
    {
        return new Vector3(
            (cell.x + 0.5f) * cellSize,
            0f,
            (cell.y + 0.5f) * cellSize
        );
    }

    /// <summary>Находится ли клетка в границах сетки [0, size).</summary>
    public bool InBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < size && cell.y >= 0 && cell.y < size;
    }

    /// <summary>Свободна ли клетка: в границах, не занята и не заблокирована.</summary>
    public bool IsFree(Vector2Int cell)
    {
        return InBounds(cell) && !IsBlocked(cell) && !occupancy.ContainsKey(cell);
    }

    /// <summary>Попытаться занять клетку юнитом. Возвращает false, если клетка недоступна или уже занята другим юнитом.</summary>
    public bool TryOccupy(Vector2Int cell, Unit unit)
    {
        if (unit == null)
        {
            Debug.LogWarning("[GridManager] TryOccupy: unit == null.");
            return false;
        }

        if (!InBounds(cell) || IsBlocked(cell))
        {
            return false;
        }

        if (occupancy.TryGetValue(cell, out var existing))
        {
            // Клетка уже занята этим же юнитом — ок, иначе отказ.
            return existing == unit;
        }

        occupancy[cell] = unit;
        return true;
    }

    /// <summary>Освободить клетку от указанного юнита.</summary>
    public void Free(Vector2Int cell, Unit unit)
    {
        if (occupancy.TryGetValue(cell, out var occupant) && occupant == unit)
        {
            occupancy.Remove(cell);
        }
    }

    /// <summary>
    /// Освободить клетку, которую сейчас занимает этот юнит (по его GridPos).
    /// Удобно при перестроении: не нужно передавать старую клетку явно.
    /// </summary>
    public void FreeUnit(Unit unit)
    {
        if (unit != null)
        {
            Free(unit.GridPos, unit);
        }
    }

    /// <summary>Вернуть юнита, занимающего клетку (null, если клетка свободна).</summary>
    public Unit GetOccupant(Vector2Int cell)
    {
        occupancy.TryGetValue(cell, out var unit);
        return unit;
    }

    /// <summary>
    /// Шаг 8: все юниты, зарегистрированные в occupancy (копия списка —
    /// безопасно итерировать, пока юниты умирают/перестраиваются).
    /// </summary>
    public List<Unit> GetAllUnits()
    {
        return new List<Unit>(occupancy.Values);
    }

    /// <summary>Заблокирована ли клетка.</summary>
    public bool IsBlocked(Vector2Int cell)
    {
        return blocked.Contains(cell);
    }

    /// <summary>Заблокировать или разблокировать клетку.</summary>
    public void SetBlocked(Vector2Int cell, bool blocked)
    {
        if (blocked)
        {
            this.blocked.Add(cell);
        }
        else
        {
            this.blocked.Remove(cell);
        }
    }

    /// <summary>Отрисовка линий сетки в Scene View.</summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;

        float worldSize = size * cellSize;

        for (int i = 0; i <= size; i++)
        {
            float t = i * cellSize;

            // Вертикальные линии (вдоль оси Z)
            Gizmos.DrawLine(
                new Vector3(t, 0f, 0f),
                new Vector3(t, 0f, worldSize)
            );

            // Горизонтальные линии (вдоль оси X)
            Gizmos.DrawLine(
                new Vector3(0f, 0f, t),
                new Vector3(worldSize, 0f, t)
            );
        }
    }
    public void ForceOccupy(Vector2Int cell, Unit unit)
    {
        if (!InBounds(cell)) return;
        occupancy[cell] = unit;
    }
}
