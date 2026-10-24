using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ополченец (Militia) — базовый боевой класс. Наследник Unit: сюда вынесена
/// вся логика ближнего боя, ранее жившая в монолитном Unit (Fix-1 зоны атаки
/// переехали сюда без изменений геометрии).
///
/// Дерево классов:
///   Unit (базовый: HP, движение, facing, материалы, кулдаун) 
///     └─ Militia (ближний бой: зона 3 клетки перед собой)
///          ├─ Warrior   (Classes/Warrior.cs)   — усиленный ближний бой
///          ├─ Spearman  (Classes/Spearman.cs)  — бьёт через клетку (дистанция 2)
///          └─ Slinger   (Classes/Slinger.cs)   — дальний бой (дистанция 3)
///
/// Дефолтные статы совпадают с прежними значениями Unit (maxHp = 5,
/// attackDamage = 1, attackCooldown = 0.8f), поэтому поведение текущих
/// юнитов (префабы с компонентом Unit или Militia) не меняется.
/// </summary>
public class Militia : Unit
{
    // Дефолтные статы класса задаются значениями полей базового Unit
    // (maxHp = 5, attackDamage = 1, attackCooldown = 0.8f) — наследники
    // переопределяют их в Awake.

    /// <summary>
    /// Таблица целочисленных поворотов направлений сетки на ±45° (Fix-1, баг А).
    /// Ключ — направление (8 допустимых векторов), значение — {ccw45, cw45}.
    /// Порядок пар согласован с обходом против часовой: ccw = следующий
    /// по массиву секторов, cw = предыдущий.
    /// </summary>
    protected static readonly Dictionary<Vector2Int, (Vector2Int ccw45, Vector2Int cw45)> Rot45 =
        new Dictionary<Vector2Int, (Vector2Int, Vector2Int)>
    {
        { new Vector2Int( 0,  1), (new Vector2Int(-1,  1), new Vector2Int( 1,  1)) }, // N  -> NW / NE
        { new Vector2Int( 1,  1), (new Vector2Int( 0,  1), new Vector2Int( 1,  0)) }, // NE -> N  / E
        { new Vector2Int( 1,  0), (new Vector2Int( 1,  1), new Vector2Int( 1, -1)) }, // E  -> NE / SE
        { new Vector2Int( 1, -1), (new Vector2Int( 1,  0), new Vector2Int( 0, -1)) }, // SE -> E  / S
        { new Vector2Int( 0, -1), (new Vector2Int( 1, -1), new Vector2Int(-1, -1)) }, // S  -> SE / SW
        { new Vector2Int(-1, -1), (new Vector2Int( 0, -1), new Vector2Int(-1,  0)) }, // SW -> S  / W
        { new Vector2Int(-1,  0), (new Vector2Int(-1, -1), new Vector2Int(-1,  1)) }, // W  -> SW / NW
        { new Vector2Int(-1,  1), (new Vector2Int(-1,  0), new Vector2Int( 0,  1)) }, // NW -> W  / N
    };

    /// <summary>
    /// Клетки зоны атаки (Fix-1, баг А): множество {F, rot45cw(F), rot45ccw(F)},
    /// где F = facing — три СМЕЖНЫЕ клетки (фронт шириной 3 по 8-соседству).
    /// Реализовано целочисленной таблицей поворотов на 45°, а НЕ перпендикуляром
    /// right = (Facing.y, -Facing.x): для диагоналей перпендикуляр давал неверную
    /// зону (0,2),(1,1),(2,0) — удар через клетку без ортогональных соседей.
    /// Пример для facing=(1,1): {(1,0), (1,1), (0,1)} + origin.
    ///
    /// Геометрия параметризована (origin/facing), а не читает GridPos/Facing:
    /// ИИ проверяет зону для КАНДИДАТНЫХ клеток боя (Unit.CanAttackCellFrom).
    /// Наследники меняют форму зоны (Spearman: копьё на дистанции 2 + ближний бой).
    /// </summary>
    public override List<Vector2Int> GetAttackCells(Vector2Int origin, Vector2Int facing)
    {
        List<Vector2Int> cells = new List<Vector2Int>(3);

        if (Rot45.TryGetValue(facing, out var pair))
        {
            cells.Add(origin + facing);      // центр фронта
            cells.Add(origin + pair.cw45);   // фланг по часовой
            cells.Add(origin + pair.ccw45);  // фланг против часовой
        }
        else
        {
            // facing вне таблицы 8 направлений (защита): бьём хотя бы прямо.
            cells.Add(origin + facing);
        }

        return cells;
    }

    /// <summary>
    /// Попытка атаки: цели — активные враги команды в зоне атаки.
    /// Кулдаун запускается только при наличии целей; при успешной атаке
    /// дёргается триггер анимации "Attack" (хук базового Unit).
    ///
    /// Проверка кулдауна обязательна ЗДЕСЬ: CombatResolver вызывает TryAttack
    /// каждый боевой тик (0.1 с), поэтому без неё удар проходил 10 раз в секунду,
    /// а attackCooldown вообще ни на что не влиял.
    /// </summary>
    public override bool TryAttack()
    {
        // Мёртвый юнит не атакует (Destroy отложен — объект жив до конца кадра).
        if (isDead) return false;

        var gm = GridManager.Instance;
        if (gm == null) return false;

        // Кулдаун проверяется по защищённому полю базового Unit (не регресс Fix-1).
        if (cooldownTimer > CooldownEpsilon) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            IReadOnlyList<Unit> occupants = gm.GetOccupants(cell);

            if (occupants == null) continue;

            // В клетке может стоять несколько юнитов («проход сквозь друг друга»),
            // поэтому перебираем всех: иначе второй юнит в клетке неуязвим.
            for (int i = 0; i < occupants.Count; i++)
            {
                Unit occupant = occupants[i];

                // Проверяем, что враг активен (not destroyed и не умер) — иначе
                // мёртвый юнит мог формально оставаться в клетке до конца кадра.
                if (occupant != null && occupant.team != this.team &&
                    occupant.gameObject != null && !occupant.IsDead)
                {
                    targets.Add(occupant);
                }
            }
        }

        if (targets.Count == 0) return false;

        cooldownTimer = attackCooldown;

        PlayAttackAnimation();

        Debug.Log($"[Combat] {name} бьёт {targets.Count} целей на {attackDamage} урона");

        foreach (var target in targets)
        {
            target.TakeDamage(attackDamage, this);
        }

        return true;
    }
}
