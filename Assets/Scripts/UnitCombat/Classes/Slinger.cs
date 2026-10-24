using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Пращник (Slinger) — дальний бой. Наследник Militia, но зона атаки не
/// используется: вместо удара по клеткам ищет ближайшего живого врага в
/// радиусе shotRange (3 клетки, Чебышёв), проверяет линию видимости (БРЕЗЕНКО
/// по клеткам: камень или чужой юнит на пути блокируют выстрел) и наносит
/// урон одной цели.
///
/// Статы: хрупкий (4 HP), слабый удар (1), долгая перезарядка (1.5 с).
/// PreferredAttackRange = 3 — ИИ останавливается на дистанции выстрела,
/// вплотную не лезет.
/// </summary>
public class Slinger : Militia
{
    [Tooltip("Дальность выстрела, клетки (Чебышёв).")]
    public int shotRange = 3;

    private void Awake()
    {
        maxHp = 4;
        attackDamage = 1;
        attackCooldown = 1.5f;
    }

    /// <summary>Дистанция боя для ИИ: 3 клетки (стреляем издалека).</summary>
    public override int PreferredAttackRange => shotRange;

    /// <summary>
    /// Пращник бьёт НЕ по клеткам зоны, а по радиусу: достаёт любую цель в
    /// пределах shotRange (Чебышёв). Поэтому геометрическая проверка базового
    /// Unit здесь заменена радиусной — иначе ИИ (по зоне атаки) считал бы, что
    /// цель не достаётся ни с одной клетки, и пращник никогда не вступал бы в бой.
    /// </summary>
    public override bool CanAttackCellFrom(Vector2Int from, Vector2Int targetCell)
    {
        if (from == targetCell) return false;

        int dist = Mathf.Max(
            Mathf.Abs(targetCell.x - from.x),
            Mathf.Abs(targetCell.y - from.y)
        );

        return dist <= shotRange;
    }

    /// <summary>
    /// Попытка атаки дальним боем: цель — ближайший живой враг в радиусе
    /// shotRange с чистой линией видимости. Кулдаун-проверка и запуск
    /// анимации — как у ближнего боя (общий контракт CombatResolver:
    /// вызов каждый тик 0.1 с, удар только при готовности).
    /// </summary>
    public override bool TryAttack()
    {
        if (isDead) return false;

        var gm = GridManager.Instance;
        if (gm == null) return false;

        if (cooldownTimer > CooldownEpsilon) return false;

        Unit target = FindShotTarget(gm);

        if (target == null) return false;

        cooldownTimer = attackCooldown;

        PlayAttackAnimation();

        Debug.Log($"[Combat] {name} стреляет в {target.name} на {attackDamage} урона");

        target.TakeDamage(attackDamage, this);

        return true;
    }

    /// <summary>
    /// Ближайший живой враг другой команды в радиусе shotRange (Чебышёв),
    /// для которого есть линия видимости. Метрика унифицирована с ИИ — везде
    /// Чебышёв max(|dx|,|dy|).
    /// </summary>
    private Unit FindShotTarget(GridManager gm)
    {
        Unit best = null;
        int bestDist = int.MaxValue;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            if (u == null || u.gameObject == null || u.IsDead || u.team == this.team)
            {
                continue;
            }

            int dx = Mathf.Abs(u.GridPos.x - GridPos.x);
            int dy = Mathf.Abs(u.GridPos.y - GridPos.y);
            int dist = Mathf.Max(dx, dy);

            if (dist > shotRange || dist >= bestDist)
            {
                continue;
            }

            if (!HasLineOfSight(gm, u.GridPos))
            {
                continue;
            }

            bestDist = dist;
            best = u;
        }

        return best;
    }

    /// <summary>
    /// Линия видимости Bresenham между двумя клетками: промежуточные клетки
    /// (кроме стартовой и целевой) должны быть в границах, не заблокированы
    /// камнем и не заняты юнитом — по ТЗ блокируют ЛЮБЫЕ юниты на пути,
    /// включая своих. Клетка цели всегда пропускается (там стоит враг).
    /// </summary>
    private bool HasLineOfSight(GridManager gm, Vector2Int to)
    {
        int x0 = GridPos.x, y0 = GridPos.y;
        int x1 = to.x, y1 = to.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1)
            {
                return true; // дошли до цели — линия чистая
            }

            int e2 = 2 * err;

            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            if (x0 == x1 && y0 == y1)
            {
                return true;
            }

            Vector2Int cell = new Vector2Int(x0, y0);

            if (!gm.InBounds(cell)) return false;
            if (gm.IsBlocked(cell)) return false;          // камень перекрыл луч
            if (gm.GetOccupant(cell) != null) return false; // юнит стоит на линии
        }
    }
}
