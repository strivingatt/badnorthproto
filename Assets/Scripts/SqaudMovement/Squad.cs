using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отряд юнитов: список состава и команда перестроения в формацию.
/// </summary>
public class Squad : MonoBehaviour
{
    /// <summary>Юниты, входящие в отряд.</summary>
    public List<Unit> units = new List<Unit>();

    /// <summary>
    /// Сколько врагов уничтожил этот отряд (счётчик ведётся из Unit.Die).
    /// Нужен как основа для будущей системы опыта/прокачки.
    /// </summary>
    public int kills;

    /// <summary>
    /// Сколько своих юнитов отряд потерял (счётчик ведётся из Unit.Die).
    /// Нужен как основа для будущей системы морали.
    /// </summary>
    public int losses;

    /// <summary>Зарегистрировать убийство врага юнитом этого отряда.</summary>
    public void RegisterKill()
    {
        kills++;
    }

    /// <summary>Зарегистрировать потерю своего юнита.</summary>
    public void RegisterLoss()
    {
        losses++;
    }

    /// <summary>Добавить юнит в отряд (null игнорируется).</summary>
    public void Add(Unit u)
    {
        if (u == null)
        {
            Debug.LogWarning("[Squad] Add: unit == null — пропущено.");
            return;
        }

        if (!units.Contains(u))
        {
            units.Add(u);
        }
    }

    /// <summary>
    /// Удалить юнита из состава отряда (вызывается из Unit.Die при смерти).
    /// Исправление бага MissingReferenceException: без этого Squad хранил бы
    /// ссылки на уничтоженных юнитов, и перестроение обращалось бы к мёртвым
    /// объектам. Remove молча ничего не делает, если юнита в списке нет.
    /// </summary>
    public void Remove(Unit unit)
    {
        units.Remove(unit);
    }

    /// <summary>
    /// Команда перестроения: назначить юнитам целевые слоты (через
    /// SlotAssigner.Assign — сортировка юнитов и слотов по (y, x) и сопоставление
    /// по индексу; пересечения траекторий возможны и приняты как дизайн) и
    /// запустить корутины перемещения.
    ///
    /// ИЗМЕНЕНИЕ (Fix-2, баг А — «призраки при null-пути»): добавлен ПРОХОД 0 —
    /// расчёт путей A* для всех пар ДО освобождения клеток. Раньше проход 1
    /// (FreeUnit) освобождал клетки ВСЕХ пар до проверки путей: если A* внутри
    /// StartMove возвращал null (слот под камнем/заблокирован/вне границ), юнит
    /// оставался стоять без регистрации в occupancy — «призрак»: не в
    /// GetAllUnits(), не атакует, не виден как цель. Теперь пары с null-путём
    /// исключаются полностью: их клетки НЕ освобождаются, движение НЕ запускается,
    /// логируется warning. Инвариант: каждый живой юнит команды либо успешно
    /// стартовал, либо остался на своей клетке СОХРАНЯЯ регистрацию в occupancy.
    ///
    /// ИЗМЕНЕНИЕ (Fix-5, баг «нет места»): пара с null-путём больше НЕ бросается
    /// молча — юниту подбирается ближайшая достижимая клетка (TryFindReachableTarget:
    /// перебор от ближайших, приоритет полностью свободным клеткам, обязательная
    /// проверка A*). Также обрабатываются юниты, которым слот не достался вовсе
    /// (слотов меньше, чем живых бойцов). Прежний warning и «остаюсь на месте»
    /// остаются только для случая, когда подходящей клетки нет вообще.
    ///
    /// ИЗМЕНЕНИЕ (исправление бага «урон до подхода»): целевая клетка НЕ
    /// резервируется заранее — TryOccupy(slot, unit) из этого метода УБРАН, вместе
    /// с блоком отката при занятом слоте. Почему:
    /// - Пока юнит резервировал слот ДО движения, GridManager считал его стоящим
    ///   в конечной точке, хотя физически юнит ещё ехал. Враг, чья зона атаки
    ///   пересекала зарезервированный слот, бил юнита «до подхода» — удар прилетал
    ///   в клетку, где юнита ещё нет.
    /// - Теперь GridPos отражает ФАКТИЧЕСКУЮ позицию (его ведёт Unit.FixedUpdate по
    ///   transform.position), и FixedUpdate займёт целевую клетку ровно в тот
    ///   момент, когда юнит физически до неё дойдёт. Это предотвращает атаки по
    ///   юниту, который ещё в пути.
    ///
    /// Что осталось:
    /// - FreeUnit в проходе 1: юнит покидает СТАРУЮ клетку сразу (иначе она висела
    ///   бы занятой, пока юнит уехал, и блокировала чужие перестроения).
    ///   Инвариант «одна клетка = один юнит» не нарушается: Free удаляет запись
    ///   только если её занимает сам юнит, а новые клетки занимают exclusively
    ///   через TryOccupy в FixedUpdate (Dictionary физически не хранит дубли).
    /// - Стартовая клетка передаётся в StartMove ЯВНО (oldPos = фактическая
    ///   GridPos на момент приказа) — A* строит путь от реального положения.
    /// - Мёртвые юниты фильтруются до назначения (страховка от
    ///   MissingReferenceException).
    /// </summary>
    public void CommandFormation(List<Vector2Int> slots, Vector2Int facing)
    {
        if (slots == null || slots.Count == 0)
        {
            Debug.LogWarning("[Squad] CommandFormation: пустой список слотов — команда игнорируется.");
            return;
        }

        if (GridManager.Instance == null)
        {
            Debug.LogError("[Squad] CommandFormation: GridManager.Instance == null — команда отменена.");
            return;
        }

        // ИСПРАВЛЕНИЕ БАГА MissingReferenceException: перед началом назначения
        // слотов фильтруем ЖИВЫХ юнитов — в units могли остаться ссылки на
        // уничтоженные объекты (юниты, погибшие в бою до того, как Die успел
        // их удалить; List.Remove не срабатывает для уже Destroy'нутых объектов,
        // поэтому фильтрация — надёжная страховка).
        List<Unit> aliveUnits = units.FindAll(u => u != null && u.gameObject != null);

        var pairs = SlotAssigner.Assign(aliveUnits, slots);

        // Проход 0 (Fix-2, баг А): расчёт путей A* ДО освобождения клеток.
        // Пары с null-путём (слот под камнем / заблокирован / вне границ) больше
        // НЕ бросаются (Fix-5): юниту подбирается ближайшая достижимая цель.
        // Если и её нет — юнит остаётся на своей клетке с сохранённой регистрацией
        // в occupancy (не становится «призраком»).
        var validPairs = new List<(Unit unit, Vector2Int slot, Vector2Int oldPos, List<Vector2Int> path)>(pairs.Count);

        // Клетки, уже занятые целями этой команды: две пары не должны метить в одну клетку.
        var reservedTargets = new HashSet<Vector2Int>();

        // Юниты, которым слот назначен — нужны, чтобы найти «безслотных» ниже.
        var pairedUnits = new HashSet<Unit>();

        foreach (var pair in pairs)
        {
            Unit unit = pair.unit;

            // Страховка от уничтожения между фильтром и этим моментом.
            if (unit == null || unit.gameObject == null)
            {
                continue;
            }

            pairedUnits.Add(unit);

            // ФАКТИЧЕСКАЯ стартовая позиция юнита на момент приказа — она же
            // start для A* (путь строим ЗДЕСЬ, до FreeUnit, от реального положения).
            Vector2Int oldPos = unit.GridPos;

            List<Vector2Int> path = Pathfinding.AStar(oldPos, pair.slot);
            Vector2Int target = pair.slot;

            if (path == null)
            {
                // Слот недостижим — ищем ближайшую подходящую клетку рядом с ним.
                if (TryFindReachableTarget(oldPos, pair.slot, reservedTargets,
                                           out Vector2Int alternate, out List<Vector2Int> alternatePath))
                {
                    Debug.Log($"[Squad] CommandFormation: слот {pair.slot} недостижим из {oldPos} — " +
                              $"юнит {unit.name} займёт ближайшую возможную клетку {alternate}.");

                    target = alternate;
                    path = alternatePath;
                }
                else
                {
                    Debug.LogWarning($"[Squad] CommandFormation: путь недоступен " +
                                     $"({oldPos} -> {pair.slot}) и подходящих клеток рядом нет — " +
                                     $"юнит {unit.name} остаётся на месте, слот пропущен.");
                    continue;
                }
            }

            reservedTargets.Add(target);
            validPairs.Add((unit, target, oldPos, path));
        }

        // Юниты БЕЗ слота (слотов меньше, чем живых бойцов): им тоже подбирается
        // ближайшая достижимая клетка — иначе они просто стоят и отстают от строя.
        foreach (Unit unit in aliveUnits)
        {
            if (unit == null || unit.gameObject == null)
            {
                continue;
            }

            if (pairedUnits.Contains(unit))
            {
                continue;
            }

            Vector2Int oldPos = unit.GridPos;

            if (TryFindReachableTarget(oldPos, oldPos, reservedTargets,
                                       out Vector2Int alternate, out List<Vector2Int> alternatePath))
            {
                Debug.Log($"[Squad] CommandFormation: юниту {unit.name} не досталось слота — " +
                          $"он займёт ближайшую возможную клетку {alternate}.");

                reservedTargets.Add(alternate);
                validPairs.Add((unit, alternate, oldPos, alternatePath));
                continue;
            }

            Debug.LogWarning($"[Squad] CommandFormation: юниту {unit.name} не досталось слота " +
                             $"и рядом нет подходящей клетки — юнит остаётся на месте.");
        }

        // Проход 1 (часть «Free»): освобождаем СТАРЫЕ клетки только пар с
        // валидным путём. Именно все сразу — иначе перестановка местами внутри
        // отряда упёрлась бы в ещё не освобождённые клетки товарищей (FixedUpdate
        // не пустил бы юнита в занятую клетку и откатил бы его назад).
        foreach (var entry in validPairs)
        {
            GridManager.Instance.FreeUnit(entry.unit);
        }

        // Проход 2 (движение): запускаем корутины по уже рассчитанным путям
        // (повторный A* внутри StartMove не требуется — путь валиден из прохода 0).
        //
        // ВАЖНО: Целевая клетка НЕ резервируется заранее (TryOccupy здесь УБРАН) —
        // FixedUpdate займёт её, когда юнит физически дойдёт. Это предотвращает
        // атаки по юниту, который ещё в пути («урон до подхода»).
        // unit.GridPos здесь тоже НЕ обновляется: его ведёт FixedUpdate по
        // фактическому transform.position.
        foreach (var entry in validPairs)
        {
            Unit unit = entry.unit;
            Vector2Int slot = entry.slot;
            Vector2Int oldPos = entry.oldPos;

            unit.Facing = facing;
            // Визуализация направления: поворот юнита (локальный +Z вдоль Facing)
            // сразу после установки Facing — «нос» смотрит туда же, куда строй.
            unit.ApplyFacing();
            unit.StartMoveAlongPath(entry.path, oldPos, slot);
        }

        Debug.Log($"[Squad] CommandFormation: выполнено — пар с валидным путём {validPairs.Count} " +
                  $"(из {pairs.Count}), facing = {facing}.");
    }

    /// <summary>
    /// Сколько ближайших клеток просматривается при подборе замены недостижимому
    /// слоту. Ограничение — страховка от перебора всей карты: для каждой клетки
    /// считается путь A*.
    /// </summary>
    private const int ReplacementSearchLimit = 32;

    /// <summary>
    /// Подбор достижимой цели вместо недостижимого слота. Клетки перебираются от
    /// ближайших к desired (Formation.EnumerateUsableCellsNearestFirst: сначала
    /// полностью свободные, затем просто проходимые и не занятые другой целью
    /// команды), для каждой проверяется наличие пути A* из oldPos.
    /// </summary>
    /// <param name="oldPos">Текущая клетка юнита — старт для A*.</param>
    /// <param name="desired">Желаемая клетка (недостижимый слот или своя же клетка).</param>
    /// <param name="reservedTargets">Цели, уже занятые другими парами этой команды.</param>
    /// <param name="target">Найденная клетка-цель.</param>
    /// <param name="path">Путь до найденной цели (гарантированно не null).</param>
    /// <returns>true, если подходящая достижимая клетка найдена.</returns>
    private static bool TryFindReachableTarget(Vector2Int oldPos, Vector2Int desired,
                                               HashSet<Vector2Int> reservedTargets,
                                               out Vector2Int target, out List<Vector2Int> path)
    {
        target = desired;
        path = null;

        int checkedCandidates = 0;

        foreach (Vector2Int candidate in Formation.EnumerateUsableCellsNearestFirst(desired, reservedTargets))
        {
            // Своя же клетка целью быть не может: путь до неё пустой.
            if (candidate == oldPos)
            {
                continue;
            }

            checkedCandidates++;

            if (checkedCandidates > ReplacementSearchLimit)
            {
                return false;
            }

            List<Vector2Int> candidatePath = Pathfinding.AStar(oldPos, candidate);

            if (candidatePath == null)
            {
                continue;
            }

            target = candidate;
            path = candidatePath;
            return true;
        }

        return false;
    }
}
