using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отряд юнитов: список состава и команда перестроения в формацию.
/// </summary>
public class Squad : MonoBehaviour
{
    /// <summary>Юниты, входящие в отряд.</summary>
    public List<Unit> units = new List<Unit>();

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
    /// Команда перестроения: назначить юнитам целевые слоты (жадное назначение по
    /// Manhattan-расстоянию через SlotAssigner.Assign), ЗАРЕЗЕРВИРОВАТЬ клетки ДО
    /// начала движения и запустить корутины перемещения.
    ///
    /// ПОЧЕМУ НЕВОЗМОЖНЫ ДУБЛИ ЗАНЯТЫХ КЛЕТОК (инвариант «одна клетка = один юнит»):
    /// 1) SlotAssigner.Assign биективен по построению: выбранная пара (юнит, слот)
    ///    удаляется из обоих рабочих списков, поэтому каждый юнит встречается ровно
    ///    в одной паре и каждый слот — ровно в одной. Двух юнитов с одним слотом не
    ///    бывает никогда.
    /// 2) Слоты из Formation.GetLineSlots уникальны — это разные клетки сетки.
    /// 3) RESERVE-DO-MOVE: для каждой пары сначала FreeUnit(юнит) освобождает ЕГО
    ///    СТАРУЮ клетку (по unit.GridPos), и только затем TryOccupy(новый слот).
    ///    FreeUnit удаляет запись из occupancy ТОЛЬКО если её занимает именно этот
    ///    юнит (occupant == unit) — снять чужую регистрацию он не может.
    /// 4) Освобождение всех старых клеток выполняется ПЕРЕД занятием новых (проход 1
    ///    до прохода 2). Поэтому когда юнит А въезжает в бывшую клетку товарища Б по
    ///    отряду, эта клетка уже пуста (Б освободил её своим FreeUnit в проходе 1) —
    ///    TryOccupy succeeds, перестановка мест внутри отряда работает без отказов.
    /// 5) Если TryOccupy всё же вернул false (слот занят сторонним юнитом вне
    ///    отряда или заблокирован) — юнит остаётся на месте: его старая клетка
    ///    гарантированно пуста (освободил её он же в проходе 1, а чужие записи
    ///    FreeUnit не трогает) и возвращается ему тем же TryOccupy(oldCell, unit).
    ///    Откат точечный и не создаёт дублей.
    /// Итого: после команды ни одна клетка не занята двумя юнитами — словарь
    /// occupancy физически не может содержать дубль, а каждое занятие предваряется
    /// проверкой занятости в TryOccupy.
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

        var pairs = SlotAssigner.Assign(units, slots);

        // Запоминаем СТАРЫЕ клетки всех назначенных юнитов ДО освобождения:
        // после FreeUnit юнит ещё не знает, куда откатываться (GridPos обновим
        // только при успешном TryOccupy).
        var oldCells = new Dictionary<Unit, Vector2Int>(pairs.Count);
        foreach (var pair in pairs)
        {
            oldCells[pair.unit] = pair.unit.GridPos;
        }

        // Проход 1 (RESERVE-DO-MOVE, часть «Free»): освобождаем СТАРЫЕ клетки
        // ВСЕХ назначенных юнитов до того, как кто-либо займёт новую.
        // Именно все сразу — иначе перестановка местами внутри отряда уперлась бы
        // в ещё не освобождённые клетки товарищей (см. п.4 комментария выше).
        foreach (var pair in pairs)
        {
            GridManager.Instance.FreeUnit(pair.unit);
        }

        // Проход 2 (часть «Occupy»): резервируем новые клетки и запускаем движение.
        //
        // ПОЧЕМУ ПОРЯДОК FreeUnit -> TryOccupy -> GridPos = slot -> StartMove(oldPos, slot)
        // КРИТИЧЕН (в частности, для исправления бага телепортации):
        // - FreeUnit (проход 1) ДО TryOccupy гарантирует отсутствие дублей занятых
        //   клеток и успешную перестановку мест внутри отряда (см. пп.3-5 выше).
        // - unit.GridPos обновляется ПЕРЕД StartMove, потому что логическое
        //   состояние (клетка + регистрация в occupancy) должно быть согласовано
        //   немедленно: пока юнит едет анимацией, сетка уже считает его стоящим
        //   на новом месте (иначе следующий приказ или другой юнит увидят
        //   устаревшую GridPos).
        // - Поэтому в StartMove НЕЛЬЗЯ брать стартовую клетку из unit.GridPos —
        //   там уже targetCell, и A* строил бы путь «от цели к цели» (пустой
        //   список), а финальная доводка корутины выглядела бы как телепорт.
        //   Стартовая клетка передаётся ЯВНО: Vector2Int oldPos = unit.GridPos
        //   сохраняется ДО обновления, и именно oldPos уходит в StartMove(oldPos, slot).
        foreach (var pair in pairs)
        {
            Unit unit = pair.unit;
            Vector2Int slot = pair.slot;
            Vector2Int oldCell = oldCells[unit];

            bool success = GridManager.Instance.TryOccupy(slot, unit);

            if (!success)
            {
                // Слот оказался занят (сторонний юнит/блокировка) — юнит остаётся
                // на месте: возвращаем СТАРУЮ регистрацию (она пуста после прохода 1).
                GridManager.Instance.TryOccupy(oldCell, unit);
                Debug.LogWarning($"[Squad] CommandFormation: слот {slot} занят — " +
                                 $"юнит {unit.name} остался на {oldCell}.");
                continue;
            }

            // Сохраняем СТАРУЮ позицию ДО обновления GridPos — она нужна A* как
            // стартовая клетка (GridPos после присваивания будет равен slot).
            Vector2Int oldPos = unit.GridPos;

            unit.GridPos = slot;
            unit.Facing = facing;
            // Визуализация направления: поворот юнита (локальный +Z вдоль Facing)
            // сразу после установки Facing — «нос» смотрит туда же, куда строй.
            unit.ApplyFacing();
            unit.StartMove(oldPos, slot);
        }

        Debug.Log($"[Squad] CommandFormation: выполнено — пар {pairs.Count}, facing = {facing}.");
    }
}
