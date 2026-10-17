using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Распределяет юнитов по слотам формации, сохраняя их относительный порядок.
/// Фактический алгоритм (Fix-4, баг В — комментарий приведён к реальности):
/// сортировка юнитов по (y, x) и слотов по тому же правилу, затем сопоставление
/// строго по индексу. Это НЕ жадное назначение по расстоянию: траектории
/// юнитов могут пересекаться (принято как осознанный выбор дизайна — при
/// проходимости занятых клеток «сквозь своих» пересечение не блокирует движение).
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