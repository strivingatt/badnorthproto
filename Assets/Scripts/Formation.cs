using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Статические утилиты построения боевых порядков (формаций) на сетке:
/// линия по Брезенхэму и раскладка шеренг (линий) отрядов.
/// </summary>
public static class Formation
{
    /// <summary>
    /// Классический алгоритм Брезенхэма: список клеток на отрезке [a..b] включительно.
    /// </summary>
    public static List<Vector2Int> Bresenham(Vector2Int a, Vector2Int b)
    {
        var cells = new List<Vector2Int>();

        int dx = Mathf.Abs(b.x - a.x);
        int dy = Mathf.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1;
        int sy = a.y < b.y ? 1 : -1;
        int err = dx - dy;

        Vector2Int cur = a;

        while (true)
        {
            cells.Add(cur);

            if (cur == b)
            {
                break;
            }

            int e2 = 2 * err;

            if (e2 > -dy)
            {
                err -= dy;
                cur.x += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                cur.y += sy;
            }
        }

        return cells;
    }

    /// <summary>
    /// Слоты формации «шеренги»: базовая линия start->end (по Брезенхэму),
    /// каждая следующая шеренга смещена НАЗАД относительно направления facing.
    /// facing — перпендикуляр к линии; заполнение: сначала вся шеренга 0, затем 1 и т.д.
    /// Слоты вне границ сетки пропускаются (добавляется следующий вместо них).
    /// Если start == end, dir принимается за (0,1).
    /// </summary>
    public static List<Vector2Int> GetLineSlots(Vector2Int start, Vector2Int end, int count, out Vector2Int facing)
    {
        facing = Vector2Int.zero;

        if (count <= 0)
        {
            return new List<Vector2Int>();
        }

        // Направление вдоль линии — знак разности концов.
        Vector2Int dir = new Vector2Int(
            Mathf.Sign(end.x - start.x),
            Mathf.Sign(end.y - start.y)
        );

        if (start == end || dir == Vector2Int.zero)
        {
            dir = new Vector2Int(0, 1);
        }

        // Facing — перпендикуляр к направлению линии.
        facing = new Vector2Int(-dir.y, dir.x);

        List<Vector2Int> line = Bresenham(start, end);
        int rowLength = line.Count; // ширина шеренги = длина базовой линии

        var slots = new List<Vector2Int>(count);

        // Шеренга r смещается НАЗАД: offset = facing * (-r).
        for (int row = 0; row < rowLength + count && slots.Count < count; row++)
        {
            Vector2Int offset = facing * (-row);

            for (int i = 0; i < rowLength && slots.Count < count; i++)
            {
                Vector2Int cell = line[i] + offset;

                // Пропускаем слоты вне границ — вместо них добавится следующий.
                if (GridManager.Instance != null && !GridManager.Instance.InBounds(cell))
                {
                    continue;
                }

                slots.Add(cell);
            }
        }

        return slots;
    }
}
