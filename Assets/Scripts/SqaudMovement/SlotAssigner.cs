using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Распределяет юнитов по слотам формации жадно: «ближайшая пара первой».
/// Фактический алгоритм (Fix-5, баг «ломаные траектории»): для всех пар
/// (юнит, слот) считается октальное расстояние — та же метрика, что у эвристики
/// Pathfinding.AStar (шаг по стороне = 1, по диагонали = √2). Пары сортируются
/// по (расстояние, y, x юнита, y, x слота) и разбираются жадно: пара берётся,
/// если и юнит, и слот ещё свободны. Это минимизирует суммарный путь и убирает
/// «чехарду» сопоставления строго по индексу (юнит мог уехать через весь строй).
/// </summary>
public static class SlotAssigner
{
    /// <summary>Стоимость диагонального шага — совпадает с Pathfinding.DiagonalCost.</summary>
    private const float DiagonalCost = 1.41421356f; // sqrt(2)

    /// <summary>
    /// Сопоставить юнитов со слотами формации. Возвращается min(units.Count,
    /// slots.Count) пар; лишние слоты и юниты не назначаются.
    /// </summary>
    public static List<(Unit unit, Vector2Int slot)> Assign(List<Unit> units, List<Vector2Int> slots)
    {
        var result = new List<(Unit, Vector2Int)>();

        if (units == null || slots == null || units.Count == 0 || slots.Count == 0)
        {
            return result;
        }

        // Стабильные порядки: сортировка по (y, x) делает результат независимым от
        // порядка элементов во входных списках и задаёт детерминированные ничьи.
        var orderedUnits = units.Where(u => u != null && u.gameObject != null)
                                .OrderBy(u => u.GridPos.y).ThenBy(u => u.GridPos.x).ToList();
        var orderedSlots = slots.OrderBy(s => s.y).ThenBy(s => s.x).ToList();

        int count = Mathf.Min(orderedUnits.Count, orderedSlots.Count);

        // Все пары с расстоянием: count² штук — для отряда в 12 бойцов незначительно.
        var candidates = new List<(float distance, int unitIndex, int slotIndex)>(
            orderedUnits.Count * orderedSlots.Count);

        for (int u = 0; u < orderedUnits.Count; u++)
        {
            for (int s = 0; s < orderedSlots.Count; s++)
            {
                candidates.Add((OctileDistance(orderedUnits[u].GridPos, orderedSlots[s]), u, s));
            }
        }

        // Детерминированный порядок разбора: расстояние, затем индексы юнита и слота.
        candidates.Sort((a, b) =>
        {
            int cmp = a.distance.CompareTo(b.distance);
            if (cmp != 0) return cmp;

            cmp = a.unitIndex.CompareTo(b.unitIndex);
            if (cmp != 0) return cmp;

            return a.slotIndex.CompareTo(b.slotIndex);
        });

        var unitTaken = new bool[orderedUnits.Count];
        var slotTaken = new bool[orderedSlots.Count];

        foreach (var candidate in candidates)
        {
            if (unitTaken[candidate.unitIndex] || slotTaken[candidate.slotIndex])
            {
                continue;
            }

            unitTaken[candidate.unitIndex] = true;
            slotTaken[candidate.slotIndex] = true;

            result.Add((orderedUnits[candidate.unitIndex], orderedSlots[candidate.slotIndex]));

            if (result.Count == count)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Октальное расстояние между клетками: сторона = 1, диагональ = √2 —
    /// та же метрика, что в эвристике Pathfinding.AStar, поэтому жадный выбор
    /// ориентируется на реальную стоимость пути.
    /// </summary>
    private static float OctileDistance(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);

        return (dx + dy) + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }
}