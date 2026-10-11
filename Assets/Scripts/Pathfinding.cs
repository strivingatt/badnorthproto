using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Поиск пути на клеточной сетке (A*). Статический класс — состояния не хранит,
/// всю информацию о границах и блокировках берёт из GridManager.Instance.
/// </summary>
public static class Pathfinding
{
    /// <summary>4 соседа (без диагоналей): +X, -X, +Y(сетка) / мир +Z, -Y / мир -Z.</summary>
    private static readonly Vector2Int[] Neighbors =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    /// <summary>
    /// A* от start до goal по клеткам сетки.
    ///
    /// Проходимость:
    /// - клетка вне границ или IsBlocked — НЕпроходима;
    /// - клетки, занятые юнитами, проходимы как ПРОМЕЖУТОЧНЫЕ (юниты успеют
    ///   уйти с пути к моменту нашего прибытия);
    /// - клетка goal проходима независимо от занятости — она наша по
    ///   резервации в Squad.CommandFormation.
    ///
    /// Возвращает путь ОТ клетки ПОСЛЕ start ДО goal включительно
    /// (сам start в список не входит). Если пути нет — null.
    /// Если start == goal — пустой список (цель уже достигнута).
    ///
    /// Очередь с приоритетом: выбрана простая List + линейный поиск минимума f
    /// (а не SortedSet) — это самое простое решение: для прототипа
    /// (сетка 24x24 = 576 клеток) O(n) на извлечение полностью достаточен,
    /// а у SortedSet одинаковые f у разных узлов требуют дополнительных
    /// tie-breaker'ов, чтобы словарь не «сливал» дубликаты ключей.
    /// </summary>
    public static List<Vector2Int> AStar(Vector2Int start, Vector2Int goal)
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            Debug.LogError("[Pathfinding] GridManager.Instance == null — поиск пути невозможен.");
            return null;
        }

        // Цель недостижима, если она вообще вне границ поля.
        if (!gm.InBounds(goal))
        {
            return null;
        }

        // Уже на месте — путь не нужен.
        if (start == goal)
        {
            return new List<Vector2Int>();
        }

        var openList = new List<Vector2Int>();                    // очередь с приоритетом (простая List)
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();  // восстановление пути
        var gScore = new Dictionary<Vector2Int, float>();         // стоимость от start до узла

        gScore[start] = 0f;
        openList.Add(start);

        while (openList.Count > 0)
        {
            // Извлекаем узел с минимальным f = g + h (линейный поиск минимума).
            int bestIndex = 0;
            float bestF = float.MaxValue;

            for (int i = 0; i < openList.Count; i++)
            {
                float f = gScore[openList[i]] + Heuristic(openList[i], goal);

                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            Vector2Int current = openList[bestIndex];
            openList.RemoveAt(bestIndex);

            if (current == goal)
            {
                return BuildPath(cameFrom, goal);
            }

            for (int n = 0; n < Neighbors.Length; n++)
            {
                Vector2Int next = current + Neighbors[n];

                if (!IsPassable(next, goal))
                {
                    continue;
                }

                float tentativeG = gScore[current] + 1f;

                if (gScore.TryGetValue(next, out float knownG) && tentativeG >= knownG)
                {
                    continue; // этот путь к next не лучше уже найденного
                }

                cameFrom[next] = current;
                gScore[next] = tentativeG;

                if (!openList.Contains(next))
                {
                    openList.Add(next);
                }
            }
        }

        return null; // открытый список опустел — цели не достать
    }

    /// <summary>Эвристика Manhattan (расстояние по 4 направлениям).</summary>
    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    /// <summary>
    /// Проходимость клетки: вне границ или заблокирована — false.
    /// Занятые юнитами клетки проходимы как промежуточные;
    /// goal проходима всегда (зарезервирована нами в CommandFormation).
    /// </summary>
    private static bool IsPassable(Vector2Int cell, Vector2Int goal)
    {
        var gm = GridManager.Instance;

        if (!gm.InBounds(cell))
        {
            return false;
        }

        if (cell == goal)
        {
            return true; // цель наша по резервации, даже если формально «занята»
        }

        if (gm.IsBlocked(cell))
        {
            return false;
        }

        // Оккупация юнитом НЕ блокирует проход — юниты уйдут с пути.
        return true;
    }

    /// <summary>
    /// Восстановление пути из cameFrom: клетки ОТ первой после start
    /// ДО goal включительно (сам start исключается — его в cameFrom нет).
    /// </summary>
    private static List<Vector2Int> BuildPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int goal)
    {
        var reversed = new List<Vector2Int>();
        Vector2Int current = goal;

        while (cameFrom.ContainsKey(current))
        {
            reversed.Add(current);
            current = cameFrom[current];
        }

        reversed.Reverse();
        return reversed;
    }
}
