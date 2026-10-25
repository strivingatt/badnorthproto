using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Статические утилиты построения боевых порядков (формаций) на сетке.
///
/// Формация — ВСЕГДА ровная полоса клеток: направление перетаскивания
/// квантуется до одного из 8 секторов (0, 45, 90, ... 315 градусов), поэтому
/// линия строится простым шагом вдоль целочисленного направления, без Брезенхэма
/// (иначе диагонали дали бы «рваную лесенку»).
/// </summary>
public static class Formation
{
    /// <summary>Шаг сектора квантования: 45 градусов в радианах.</summary>
    private const float SectorRad = Mathf.PI * 0.25f;

    /// <summary>
    /// Единичные клеточные векторы для 8 секторов по порядку округления угла
    /// k = round(angle / 45°): 0°->(1,0), 45°->(1,1), 90°->(0,1), 135°->(-1,1),
    /// 180°->(-1,0), 225°->(-1,-1), 270°->(0,-1), 315°->(1,-1).
    /// </summary>
    private static readonly Vector2Int[] Sectors =
    {
        new Vector2Int(1, 0),   //   0°
        new Vector2Int(1, 1),   //  45°
        new Vector2Int(0, 1),   //  90°
        new Vector2Int(-1, 1),  // 135°
        new Vector2Int(-1, 0),  // 180°
        new Vector2Int(-1, -1), // 225°
        new Vector2Int(0, -1),  // 270°
        new Vector2Int(1, -1),  // 315°
    };

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
    /// Квантование вектора перетаскивания до ближайшего из 8 секторных направлений:
    /// angle = Atan2(dragVec.y, dragVec.x); сектор k = round(angle / 45°) mod 8.
    /// Нулевой вектор здесь не обрабатывается — degenerate-случай берёт на себя
    /// вызывающий код GetLineSlots.
    /// </summary>
    private static Vector2Int SnapDirection(Vector2Int dragVec)
    {
        float angle = Mathf.Atan2(dragVec.y, dragVec.x);
        int k = Mathf.RoundToInt(angle / SectorRad);
        k = ((k % 8) + 8) % 8; // защита от отрицательного остатка (angle < 0)
        return Sectors[k];
    }

    /// <summary>
    /// Слоты формации «шеренги» (line formation) для count юнитов.
    ///
    /// Спецификация:
    ///  1) dragVec = end - start (в клетках). Если dragVec == (0,0): dir = (1,0), len = 1 клетка.
    ///  2) Направление квантуется до ближайшего из 8 секторов (0,45,...,315°) -> dirSnapped.
    ///  3) len = число ШАГОВ вдоль dirSnapped: max(1, RoundToInt(Dot(dragVec, normalize(dirSnapped)))).
    ///     Выбран вариант через Dot с нормализованным dirSnapped, а не (|x|+|y|)/2:
    ///     Dot даёт точную геометрическую проекцию вектора на направление и одинаково
    ///     корректно работает и для осевых направлений (проекция = целому смещению),
    ///     и для диагоналей (проекция = (|x|+|y|)/√2 ≈ 0.707·(|x|+|y|)); формула
    ///     (|x|+|y|)/2 была бы верна только строго для диагоналей 45°.
    ///     Количество клеток линии = len + 1 (стартовая клетка включительно):
    ///     drag (4,0) -> 4 шага -> 5 клеток; drag (3,3) -> 6/√2 ≈ 4.24 -> 4 шага -> 5 клеток
    ///     (диагональная «ступенька» короче евклидовой клетки — длина в клетках совпадает).
    ///  4) line = клетки start + dirSnapped * i, i = 0..len; если конец линии вышел за
    ///     границы — линия укорачивается (clamp всей линии к краю сетки).
    ///  5) facing = поворот dirSnapped на +90°: (-dir.y, dir.x). Шеренги смещаются НАЗАД:
    ///     slot = line[i] + (-facing) * rank, заполнение rank-major (ранг за рангом),
    ///     ровно count слотов; клетки вне границ и клетки-камни (IsBlocked) пропускаются.
    ///  6) Если «идеальных» клеток фигуры не хватило (упёрлись в край карты или в
    ///     камни), включается ДОБОР: для каждой недостающей позиции ищется ближайшая
    ///     подходящая клетка. Приоритет внутри кольца: сначала полностью свободная
    ///     клетка (GridManager.IsFree — на ней никого нет), затем просто проходимая
    ///     и не занятая другой целью этой же команды (свои юниты уедут со своих
    ///     клеток). Поэтому функция возвращает ровно count слотов, а не «сколько
    ///     влезло»: юниты без места занимают ближайшее возможное.
    /// </summary>
    public static List<Vector2Int> GetLineSlots(Vector2Int start, Vector2Int end, int count, out Vector2Int facing)
    {
        facing = Vector2Int.zero;

        if (count <= 0)
        {
            return new List<Vector2Int>();
        }

        var gm = GridManager.Instance;

        // end заранее ограничиваем границами сетки (п. спецификации).
        end = ClampToGrid(end);

        Vector2Int dragVec = end - start;

        // Шаг 1: degenerate-случай — нет растягивания: dir = (1,0), линия из 1 клетки.
        Vector2Int dir;
        int steps; // число ШАГОВ вдоль dir; количество клеток линии = steps + 1

        if (dragVec == Vector2Int.zero)
        {
            dir = new Vector2Int(1, 0);
            steps = 0;
        }
        else
        {
            // Шаг 2: квантование направления до 8 секторов.
            dir = SnapDirection(dragVec);

            // Шаг 3: длина — проекция dragVec на НОРМАЛИЗОВАННЫЙ снапнутый вектор (Dot).
            // Для осевых dir нормализация результат не меняет, для диагональных
            // приводит (|x|+|y|) к истинной длине вдоль диагонали:
            // drag (4,0) -> 4 шага (5 клеток); drag (3,3) -> 6/√2 ≈ 4.24 -> округляется до 4.
            Vector2 dirNorm = new Vector2(dir.x, dir.y).normalized;
            float projection = Vector2.Dot(new Vector2(dragVec.x, dragVec.y), dirNorm);
            steps = Mathf.Max(1, Mathf.RoundToInt(projection));
        }

        // Шаг 4: clamp всей линии в границы — если конец упирается в край, укорачиваем len.
        while (steps > 0 && gm != null && !gm.InBounds(start + dir * steps))
        {
            steps--;
        }

        // Шаг 5: facing — поворот dir на +90°; назад = -facing.
        facing = new Vector2Int(-dir.y, dir.x);
        Vector2Int back = -facing;

        var slots = new List<Vector2Int>(count);

        // Клетки, уже отданные под слот: одна клетка не может быть целью двух юнитов.
        var used = new HashSet<Vector2Int>();

        // Потолок рангов: дальше всех возможных шеренг заведомо нет ни одной клетки
        // в границах (координата клетки не может уйти дальше 2*size шагов назад).
        int maxRank = count + 2 * (gm != null ? gm.size : 64);

        // Заполнение ПО ШЕРЕНГАМ (rank-major): rank = 0, 1, 2, ...
        // Неподходящие клетки (вне границ, камни, уже занятые слотом) пропускаются.
        for (int rank = 0; rank <= maxRank && slots.Count < count; rank++)
        {
            Vector2Int offset = back * rank; // смещение шеренги НАЗАД

            for (int i = 0; i <= steps && slots.Count < count; i++)
            {
                Vector2Int cell = start + dir * i + offset;

                if (!IsSlotUsable(cell, used))
                {
                    continue;
                }

                slots.Add(cell);
                used.Add(cell);
            }
        }

        int placedByShape = slots.Count;

        // ДОБОР (п. 6 спецификации): фигура не поместилась целиком — недостающие
        // юниты получают ближайшую возможную клетку вместо «остаюсь на месте».
        if (slots.Count < count)
        {
            for (int rank = 0; rank <= maxRank && slots.Count < count; rank++)
            {
                Vector2Int offset = back * rank;

                for (int i = 0; i <= steps && slots.Count < count; i++)
                {
                    Vector2Int desired = start + dir * i + offset;

                    // Позиция уже отдана под слот — пропускаем, иначе слот задублируется.
                    if (used.Contains(desired))
                    {
                        continue;
                    }

                    if (!TryFindNearestUsableCell(desired, used, out Vector2Int cell))
                    {
                        continue;
                    }

                    slots.Add(cell);
                    used.Add(cell);
                }
            }

            if (slots.Count > placedByShape)
            {
                Debug.Log($"[Formation] Формация не поместилась целиком ({start}->{end}): " +
                          $"{slots.Count - placedByShape} юнит(ов) получат ближайшие возможные клетки.");
            }
        }

        if (slots.Count < count)
        {
            // Безнадёжный случай: подходящих клеток нет ни в фигуре, ни рядом
            // (например, всё вокруг в камнях). Юниты без слота останутся на месте.
            Debug.LogWarning($"[Formation] Не хватает {count - slots.Count} слотов для формации: " +
                             $"{start}->{end} (нет подходящих клеток ни в фигуре, ни рядом).");
        }

        return slots;
    }

    /// <summary>Можно ли ставить слот в клетку: в границах, не камень и не занята другим слотом.</summary>
    private static bool IsSlotUsable(Vector2Int cell, HashSet<Vector2Int> used)
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return !used.Contains(cell);
        }

        return gm.InBounds(cell) && !gm.IsBlocked(cell) && !used.Contains(cell);
    }

    /// <summary>
    /// Перебор клеток вокруг desired «от ближних к дальним» (кольцами Чебышёва).
    /// Внутри кольца сначала отдаются полностью свободные клетки (GridManager.IsFree —
    /// на клетке никого нет), затем одна «запасная»: проходимая и не занятая другой
    /// целью этой же команды (свои юниты всё равно уедут со своих клеток).
    /// В сетке size×size дальше радиуса size клеток не существует — перебор конечен.
    /// </summary>
    public static IEnumerable<Vector2Int> EnumerateUsableCellsNearestFirst(Vector2Int desired,
                                                                            HashSet<Vector2Int> reserved)
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            yield break;
        }

        int maxRadius = gm.size;

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            Vector2Int? fallback = null;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Только периметр кольца: внутренние клетки уже отданы меньшими радиусами.
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    Vector2Int cell = desired + new Vector2Int(dx, dy);

                    if (!gm.InBounds(cell) || gm.IsBlocked(cell))
                    {
                        continue;
                    }

                    if (reserved != null && reserved.Contains(cell))
                    {
                        continue;
                    }

                    // tier-1: полностью свободная клетка — предпочтительный вариант.
                    if (gm.IsFree(cell))
                    {
                        yield return cell;
                        continue;
                    }

                    // tier-2: кандидат «на запас» — вернём его, только если в этом
                    // кольце не нашлось ни одной полностью свободной клетки.
                    if (!fallback.HasValue)
                    {
                        fallback = cell;
                    }
                }
            }

            if (fallback.HasValue)
            {
                yield return fallback.Value;
            }
        }
    }

    /// <summary>
    /// Ближайшая подходящая клетка для желаемой позиции: первый кандидат из
    /// EnumerateUsableCellsNearestFirst. false — подходящих клеток нет вовсе.
    /// </summary>
    public static bool TryFindNearestUsableCell(Vector2Int desired, HashSet<Vector2Int> reserved,
                                                out Vector2Int cell)
    {
        foreach (Vector2Int candidate in EnumerateUsableCellsNearestFirst(desired, reserved))
        {
            cell = candidate;
            return true;
        }

        cell = desired;
        return false;
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

        // T1: горизонтальная линия 5 клеток (drag на 4 клетки), 8 юнитов
        // -> шеренги 5+3, facing (0,1).
        TestGetLineSlots(
            "T1",
            new Vector2Int(5, 5), new Vector2Int(9, 5), 8,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(6, 5), new Vector2Int(7, 5),
                new Vector2Int(8, 5), new Vector2Int(9, 5),
                new Vector2Int(5, 4), new Vector2Int(6, 4), new Vector2Int(7, 4),
            },
            new Vector2Int(0, 1));

        // T2: диагональ: drag (3,3) -> dir (1,1), проекция 6/√2 ≈ 4.24 -> len 4 шага
        // (5 клеток в линии), 8 юнитов -> шеренги 5+3 (вторая смещена НАЗАД на
        // -facing = (1,-1)), facing (-1,1).
        TestGetLineSlots(
            "T2",
            new Vector2Int(5, 5), new Vector2Int(8, 8), 8,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(6, 6), new Vector2Int(7, 7),
                new Vector2Int(8, 8), new Vector2Int(9, 9),
                new Vector2Int(6, 4), new Vector2Int(7, 5), new Vector2Int(8, 6),
            },
            new Vector2Int(-1, 1));

        // T3: drag (4,1) — угол ~14°, снапится в 0°: dir (1,0), len 4 шага (5 клеток),
        // 4 юнита -> одна шеренга, facing (0,1).
        TestGetLineSlots(
            "T3",
            new Vector2Int(5, 5), new Vector2Int(9, 6), 4,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(6, 5), new Vector2Int(7, 5), new Vector2Int(8, 5),
            },
            new Vector2Int(0, 1));

        // T4: точка (без растягивания): dir (1,0), len 1 клетка, 4 юнита
        // -> 4 шеренги по 1 со смещением НАЗАД (0,-1), facing (0,1).
        TestGetLineSlots(
            "T4",
            new Vector2Int(5, 5), new Vector2Int(5, 5), 4,
            new Vector2Int[]
            {
                new Vector2Int(5, 5), new Vector2Int(5, 4),
                new Vector2Int(5, 3), new Vector2Int(5, 2),
            },
            new Vector2Int(0, 1));

        // T5: добор слотов у края карты. Тянем вдоль нижнего края (y = 0) на 12
        // юнитов: линия упирается в границу, поэтому часть бойцов не помещается
        // в фигуру и должна получить ближайшие возможные клетки. Проверяются
        // СВОЙСТВА набора (ровно count слотов, все в границах, без дубликатов),
        // а не конкретные клетки: они зависят от камней и стоящих юнитов.
        TestEdgeFormationSlots(
            "T5",
            new Vector2Int(30, 0), new Vector2Int(41, 0), 12);

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

    /// <summary>
    /// Тест добора слотов: сверяет не конкретные клетки, а свойства набора —
    /// количество слотов равно count, все клетки в границах сетки и не повторяются.
    /// </summary>
    private static void TestEdgeFormationSlots(string name, Vector2Int start, Vector2Int end, int count)
    {
        List<Vector2Int> actual = GetLineSlots(start, end, count, out Vector2Int facing);

        var gm = GridManager.Instance;
        var unique = new HashSet<Vector2Int>();

        bool allUnique = true;
        bool allInBounds = gm != null;

        foreach (Vector2Int cell in actual)
        {
            if (!unique.Add(cell))
            {
                allUnique = false;
            }

            if (gm != null && !gm.InBounds(cell))
            {
                allInBounds = false;
            }
        }

        bool ok = actual.Count == count && allUnique && allInBounds;

        Debug.Log($"[Formation.SelfTest] {name}: {(ok ? "PASS" : "FAIL")}\n" +
                  $"  start={start}, end={end}, count={count}, facing={facing}\n" +
                  $"  слотов: {actual.Count} (ожидалось {count}), уникальных: {allUnique}, " +
                  $"все в границах: {allInBounds}\n" +
                  $"  получено: [{string.Join(", ", actual)}]");
    }
}
