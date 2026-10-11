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

            unit.GridPos = slot;
            unit.Facing = facing;
            unit.StartMove(slot);
        }

        Debug.Log($"[Squad] CommandFormation: выполнено — пар {pairs.Count}, facing = {facing}.");
    }
}
