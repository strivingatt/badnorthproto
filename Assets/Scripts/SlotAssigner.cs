using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Жадное назначение юнитов на целевые слоты формации.
/// Алгоритм: пока есть и юниты, и слоты — выбираем пару (юнит, слот) с минимальным
/// Manhattan-расстоянием между текущей клеткой юнита и слотом, после чего убираем
/// обоих из рабочих списков. Так каждый юнит получает ровно один слот, и каждый
/// слот достаётся ровно одному юниту (дубли невозможны по построению).
/// </summary>
public static class SlotAssigner
{
    /// <summary>Manhattan-расстояние между двумя клетками сетки.</summary>
    private static int Distance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    /// <summary>
    /// Назначить каждому юниту слот жадным перебором минимума расстояний.
    /// Вход не мутируется: используются локальные копии списков.
    /// Если слотов меньше, чем юнитов — лишние юниты остаются без слота;
    /// если юнитов меньше, чем слотов — лишние слоты остаются пустыми.
    /// null-элементы входов игнорируются.
    /// </summary>
    public static List<(Unit unit, Vector2Int slot)> Assign(List<Unit> units, List<Vector2Int> slots)
    {
        var result = new List<(Unit unit, Vector2Int slot)>();

        if (units == null || slots == null)
        {
            Debug.LogWarning("[SlotAssigner] Assign: null-вход — назначение не выполнено.");
            return result;
        }

        // Рабочие копии: исходные списки не меняем.
        var remainingUnits = new List<Unit>(units.Count);
        foreach (var u in units)
        {
            if (u != null)
            {
                remainingUnits.Add(u);
            }
        }

        var remainingSlots = new List<Vector2Int>(slots);

        // Пока есть пары — ищем минимум Manhattan-расстояния и забираем обоих.
        while (remainingUnits.Count > 0 && remainingSlots.Count > 0)
        {
            int bestUnit = 0;
            int bestSlot = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i < remainingUnits.Count; i++)
            {
                for (int j = 0; j < remainingSlots.Count; j++)
                {
                    int d = Distance(remainingUnits[i].GridPos, remainingSlots[j]);

                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestUnit = i;
                        bestSlot = j;
                    }
                }
            }

            result.Add((remainingUnits[bestUnit], remainingSlots[bestSlot]));

            // Убираем обоих участников выбранной пары (с большим индексом сначала,
            // чтобы не сбить индексы).
            if (bestSlot > bestUnit)
            {
                remainingSlots.RemoveAt(bestSlot);
                remainingUnits.RemoveAt(bestUnit);
            }
            else
            {
                remainingUnits.RemoveAt(bestUnit);
                remainingSlots.RemoveAt(bestSlot);
            }
        }

        return result;
    }
}
