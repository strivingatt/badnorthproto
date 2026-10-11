using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Распределяет юнитов по слотам формации, сохраняя их относительный порядок.
/// Это предотвращает пересечение траекторий и "прохождение сквозь друг друга".
/// </summary>
public static class SlotAssigner
{
    public static List<(Unit unit, Vector2Int slot)> Assign(List<Unit> units, List<Vector2Int> slots)
    {
        var result = new List<(Unit, Vector2Int)>();

        if (units == null || slots == null) return result;

        // 1. Сортируем юниты: сначала по Y (снизу вверх), затем по X (слева направо)
        var sortedUnits = units.OrderBy(u => u.GridPos.y).ThenBy(u => u.GridPos.x).ToList();

        // 2. Сортируем целевые слоты по тому же правилу
        var sortedSlots = slots.OrderBy(s => s.y).ThenBy(s => s.x).ToList();

        // 3. Сопоставляем строго по порядку
        int count = Mathf.Min(sortedUnits.Count, sortedSlots.Count);

        for (int i = 0; i < count; i++)
        {
            result.Add((sortedUnits[i], sortedSlots[i]));
        }

        return result;
    }
}