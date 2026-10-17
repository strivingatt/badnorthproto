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
    public int size = 36;
    public float cellSize = 1f;

    /// <summary>
    /// Занятые клетки и юниты, которые их занимают. В одной клетке может
    /// оказаться НЕСКОЛЬКО юнитов: юниты не блокируют друг друга и проходят
    /// сквозь товарищей, поэтому клетка хранит список, а не одного владельца.
    /// Раньше здесь был Dictionary&lt;Vector2Int, Unit&gt;: вошедший вторым
    /// перезаписывал запись первого, и тот «исчезал» из учёта — переставал
    /// участвовать в бою и становился невидимым для ИИ, пока не сдвинется с места.
    /// </summary>
    private readonly Dictionary<Vector2Int, List<Unit>> occupancy
        = new Dictionary<Vector2Int, List<Unit>>();

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

    /// <summary>Свободна ли клетка: в границах, не заблокирована и на ней нет юнитов.</summary>
    public bool IsFree(Vector2Int cell)
    {
        return InBounds(cell) && !IsBlocked(cell) && !HasOccupants(cell);
    }

    /// <summary>Стоит ли в клетке хотя бы один юнит.</summary>
    private bool HasOccupants(Vector2Int cell)
    {
        return occupancy.TryGetValue(cell, out var units) && units.Count > 0;
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

        if (occupancy.TryGetValue(cell, out var units) && units.Count > 0)
        {
            // Клетка уже занята этим же юнитом — ок, иначе отказ.
            return units.Contains(unit);
        }

        AddToCell(cell, unit);
        return true;
    }

    /// <summary>Освободить клетку от указанного юнита (остальные юниты в клетке остаются).</summary>
    public void Free(Vector2Int cell, Unit unit)
    {
        if (!occupancy.TryGetValue(cell, out var units))
        {
            return;
        }

        units.Remove(unit);

        if (units.Count == 0)
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

    /// <summary>
    /// Основной юнит клетки (первый, кто её занял) — для выделения мышью.
    /// null, если клетка свободна.
    /// </summary>
    public Unit GetOccupant(Vector2Int cell)
    {
        return occupancy.TryGetValue(cell, out var units) && units.Count > 0 ? units[0] : null;
    }

    /// <summary>
    /// ВСЕ юниты клетки — их может быть несколько, если один прошёл сквозь другого.
    /// Нужно боевой системе: иначе второй юнит в клетке неуязвим.
    /// Возвращает null, если клетка пуста. Список внутренний — только для чтения.
    /// </summary>
    public IReadOnlyList<Unit> GetOccupants(Vector2Int cell)
    {
        return occupancy.TryGetValue(cell, out var units) ? units : null;
    }

    /// <summary>Занимает ли указанный юнит указанную клетку (для самовосстановления учёта).</summary>
    public bool IsOccupiedBy(Vector2Int cell, Unit unit)
    {
        return occupancy.TryGetValue(cell, out var units) && units.Contains(unit);
    }

    /// <summary>
    /// Шаг 8: все юниты, зарегистрированные в occupancy (копия списка —
    /// безопасно итерировать, пока юниты умирают/перестраиваются).
    /// Юнит может стоять в клетке не один, поэтому список «сплющивается» и
    /// дедуплицируется: каждый живой юнит попадает в результат ровно один раз.
    /// </summary>
    public List<Unit> GetAllUnits()
    {
        var result = new List<Unit>();
        var seen = new HashSet<Unit>();

        foreach (var units in occupancy.Values)
        {
            foreach (var unit in units)
            {
                if (unit != null && seen.Add(unit))
                {
                    result.Add(unit);
                }
            }
        }

        return result;
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

    /// <summary>
    /// Зарегистрировать юнита в клетке, НЕ отбирая её у тех, кто уже там стоит.
    /// Нужен при «проходе сквозь»: двое могут оказаться в одной клетке, и оба
    /// должны оставаться в учёте — иначе юнит выпадает из GetAllUnits и
    /// становится невидимым для боя и ИИ.
    /// </summary>
    public void ForceOccupy(Vector2Int cell, Unit unit)
    {
        if (unit == null || !InBounds(cell))
        {
            return;
        }

        AddToCell(cell, unit);
    }

    /// <summary>Добавить юнита в список клетки (без дублей, список создаётся по необходимости).</summary>
    private void AddToCell(Vector2Int cell, Unit unit)
    {
        if (!occupancy.TryGetValue(cell, out var units))
        {
            units = new List<Unit>(1);
            occupancy[cell] = units;
        }

        if (!units.Contains(unit))
        {
            units.Add(unit);
        }
    }
}
