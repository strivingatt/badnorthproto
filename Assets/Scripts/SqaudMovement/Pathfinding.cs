using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* по сетке с 8 направлениями (диагонали разрешены).
/// Единственный источник данных о границах и препятствиях — GridManager.Instance.
/// Никаких внешних типов карты: проект уже имеет GridManager.
/// </summary>
public static class Pathfinding
{
    private const float StraightCost = 1.0f;
    private const float DiagonalCost = 1.41421356f; // sqrt(2)

    /// <summary>4 ортогональных + 4 диагональных направления.</summary>
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0),  new Vector2Int(-1, 0), new Vector2Int(0, 1),  new Vector2Int(0, -1),
        new Vector2Int(1, 1),  new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    /// <summary>
    /// Путь ОТ клетки после start ДО goal включительно; null если пути нет;
    /// пустой список, если start == goal (юнит уже на месте).
    /// Занятые юнитами клетки проходимы (юниты убегут к моменту прибытия),
    /// заблокированные (камни) — нет.
    /// </summary>
    public static List<Vector2Int> AStar(Vector2Int start, Vector2Int goal)
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            Debug.LogError("[Pathfinding] GridManager.Instance == null.");
            return null;
        }

        if (!gm.InBounds(start) || !gm.InBounds(goal) || gm.IsBlocked(goal))
        {
            return null;
        }

        if (start == goal)
        {
            return new List<Vector2Int>();
        }

        var open = new List<Vector2Int> { start };
        var closed = new HashSet<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float> { [start] = 0f };

        while (open.Count > 0)
        {
            // Узел с минимальным f = g + h (для 24x24 простой перебор достаточно быстр).
            int bestIndex = 0;
            float bestF = float.MaxValue;

            for (int i = 0; i < open.Count; i++)
            {
                float f = gScore[open[i]] + Heuristic(open[i], goal);

                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            var current = open[bestIndex];
            open.RemoveAt(bestIndex);

            if (current == goal)
            {
                return Reconstruct(cameFrom, current);
            }

            closed.Add(current);

            foreach (var d in Dirs)
            {
                var next = current + d;

                if (!gm.InBounds(next) || closed.Contains(next) || gm.IsBlocked(next))
                {
                    continue;
                }

                // Запрет срезания углов: диагональ проходима, только если обе
                // соседние ортогональные клетки проходимы (иначе юнит протиснется
                // по диагонали между двумя камнями).
                if (d.x != 0 && d.y != 0)
                {
                    var a = current + new Vector2Int(d.x, 0);
                    var b = current + new Vector2Int(0, d.y);

                    if (!gm.InBounds(a) || gm.IsBlocked(a) || !gm.InBounds(b) || gm.IsBlocked(b))
                    {
                        continue;
                    }
                }

                float tentative = gScore[current] + (d.x != 0 && d.y != 0 ? DiagonalCost : StraightCost);

                if (gScore.TryGetValue(next, out float existing))
                {
                    if (tentative >= existing)
                    {
                        continue;
                    }
                }
                else
                {
                    open.Add(next);
                }

                gScore[next] = tentative;
                cameFrom[next] = current;
            }
        }

        return null; // путь не найден
    }

    /// <summary>Octile-эвристика: корректна для 8 направлений (Manhattan завышал бы оценку).</summary>
    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }

    /// <summary>Восстановление пути: от goal к start, затем разворот и удаление стартовой клетки.</summary>
    private static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.RemoveAt(path.Count - 1); // убираем start
        path.Reverse();
        return path;
    }
}