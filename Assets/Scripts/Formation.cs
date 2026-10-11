using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Статические утилиты построения боевых порядков (формаций) на сетке:
/// линия по Брезенхэму и раскладка слотов «шеренги» (rank-major).
/// </summary>
public static class Formation
{
    /// <summary>
    /// Классический алгоритм Брезенхэма: список клеток на отрезке [a..b] включительно.
    /// Если a == b — возвращается список из одной клетки.
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

    /// <summary>Поэлементный знак числа: -1, 0 или +1.</summary>
    private static int Sign(int v)
    {
        return v < 0 ? -1 : (v > 0 ? 1 : 0);
    }

    /// <summary>Ограничение клетки границами сетки [0, size).</summary>
    private static Vector2Int ClampToGrid(Vector2Int cell)
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return cell;
        }

        int max = gm.size - 1;

        return new Vector2Int(
            Mathf.Clamp(cell.x, 0, max),
            Mathf.Clamp(cell.y, 0, max)
        );
    }

    /// <summary>
    /// Слоты формации «шеренги» от линии start->end для count юнитов.
    /// Логика:
    ///  - end заранее clamp в границы сетки;
    ///  - line = Bresenham(start, end) включая оба конца; если start == end -> line = [start];
    ///  - в line оставляются только клетки внутри границ;
    ///  - dir — поэлементный знак (end - start), приведённый к (-1|0|1); если start == end -> dir = (1,0);
    ///  - facing = перпендикуляр (-dir.y, dir.x);
    ///  - заполнение ПО ШЕРЕНГАМ (rank-major): rank = 0,1,2,...; для каждой клетки c из line
    ///    добавляется слот c + (-facing)*rank (шеренга смещается НАЗАД); стоп при наборе count.
    /// Возвращает ровно count слотов; если из-за выхода за границы клетки кончились —
    /// возвращает сколько есть и логирует предупреждение.
    /// </summary>
    public static List<Vector2Int> GetLineSlots(Vector2Int start, Vector2Int end, int count, out Vector2Int facing)
    {
        facing = Vector2Int.zero;

        if (count <= 0)
        {
            return new List<Vector2Int>();
        }

        // end заранее ограничиваем границами сетки.
        end = ClampToGrid(end);

        // Базовая линия: Брезенхэм, включая оба конца; degenerate-случай — одна клетка.
        List<Vector2Int> line = (start == end)
            ? new List<Vector2Int> { start }
            : Bresenham(start, end);

        // Оставляем в линии только клетки внутри границ.
        var gm = GridManager.Instance;

        if (gm != null)
        {
            line.RemoveAll(c => !gm.InBounds(c));
        }

        // Направление вдоль линии — поэлементный знак разности концов.
        Vector2Int dir;

        if (start != end)
        {
            dir = new Vector2Int(Sign(end.x - start.x), Sign(end.y - start.y));
        }
        else
        {
            dir = new Vector2Int(1, 0);
        }

        // Facing — перпендикуляр к направлению линии.
        facing = new Vector2Int(-dir.y, dir.x);

        var slots = new List<Vector2Int>(count);

        // Заполнение по шеренгам (rank-major): rank = 0, 1, 2, ...
        int maxRank = count + 1;      // больше count рангов всегда хватает
        int lineLength = line.Count;

        for (int rank = 0; rank < maxRank && slots.Count < count; rank++)
        {
            Vector2Int offset = -facing * rank; // смещение шеренги НАЗАД

            for (int i = 0; i < lineLength && slots.Count < count; i++)
            {
                Vector2Int cell = line[i] + offset;

                // Пропускаем слоты вне границ — вместо них попробуется следующий.
                if (gm != null && !gm.InBounds(cell))
                {
                    continue;
                }

                slots.Add(cell);
            }
        }

        if (slots.Count < count)
        {
            Debug.LogWarning($"[Formation] GetLineSlots: набрано только {slots.Count} из {count} слотов " +
                             $"(линия {start}->{end}, клетки закончились из-за границ сетки).");
        }

        return slots;
    }

    // ------------------------------------------------------------------
    // Самотест (запуск по клавише T из InputController):
    // сверяет результаты GetLineSlots с ожиданиями и печатает PASS/FAIL.
    // ------------------------------------------------------------------

    /// <summary>Прогнать тесты GetLineSlots и залогировать PASS/FAIL по каждому.</summary>
    public static void SelfTest()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            Debug.LogError("[Formation.SelfTest] GridManager.Instance == null — тест невозможен.");
            return;
        }

        Debug.Log("[Formation.SelfTest] Начало самотеста...");

        // T1: горизонтальная линия 3 клетки, 8 юнитов -> шеренги 3+3+2.
        TestGetLineSlots(
            "T1",
            new Vector2Int(5, 5), new Vector2Int(7, 5), 8,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(6, 5), new Vector2Int(7, 5),
                new Vector2Int(5, 4), new Vector2Int(6, 4), new Vector2Int(7, 4),
                new Vector2Int(5, 3), new Vector2Int(6, 3),
            },
            new Vector2Int(0, 1));

        // T2: вырожденная точка, 4 юнита -> колонна назад от (5,5).
        TestGetLineSlots(
            "T2",
            new Vector2Int(5, 5), new Vector2Int(5, 5), 4,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(5, 4),
                new Vector2Int(5, 3), new Vector2Int(5, 2),
            },
            new Vector2Int(0, 1));

        // T3: вертикальная линия вверх вдоль края, 4 юнита -> первая шеренга целиком.
        TestGetLineSlots(
            "T3",
            new Vector2Int(0, 0), new Vector2Int(0, 9), 4,
            new Vector2Int[]
            {
                new Vector2Int(0, 0), new Vector2Int(0, 1),
                new Vector2Int(0, 2), new Vector2Int(0, 3),
            },
            new Vector2Int(-1, 0));

        Debug.Log("[Formation.SelfTest] Конец самотеста.");
    }

    /// <summary>Прогон одного теста GetLineSlots со сверкой слотов и facing, лог PASS/FAIL.</summary>
    private static void TestGetLineSlots(string name, Vector2Int start, Vector2Int end, int count,
                                         Vector2Int[] expectedSlots, Vector2Int expectedFacing)
    {
        List<Vector2Int> actual = GetLineSlots(start, end, count, out Vector2Int facing);

        bool ok = facing == expectedFacing && actual.Count == expectedSlots.Length;

        if (ok)
        {
            for (int i = 0; i < actual.Count; i++)
            {
                if (actual[i] != expectedSlots[i])
                {
                    ok = false;
                    break;
                }
            }
        }

        string result = ok ? "PASS" : "FAIL";

        Debug.Log($"[Formation.SelfTest] {name}: {result}\n" +
                  $"  start={start}, end={end}, count={count}\n" +
                  $"  ожидалось: facing={expectedFacing}, слоты=[{string.Join(", ", expectedSlots)}]\n" +
                  $"  получено:  facing={facing}, слоты=[{string.Join(", ", actual)}]");
    }
}
