using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Копейщик (Spearman) — ближний бой увеличенной дистанции: бьёт НЕ по зоне
/// из 3 смежных клеток, а ровно в ОДНУ клетку на расстоянии 2 прямо по Facing
/// (через одну клетку). За счёт этого фронт может стоять во втором ряду строя
/// и доставать врага через своего же юнита (свои не получают урон — фильтр
/// по команде в TryAttack унаследован от Militia).
///
/// Статы: крепче ополченца (6 HP), сильнее бьёт (2), но медленнее перезарядка
/// (1.2 с). PreferredAttackRange = 2 — ИИ останавливается в двух клетках от
/// цели (там, где копьё уже достает).
/// </summary>
public class Spearman : Militia
{
    private void Awake()
    {
        maxHp = 6;
        attackDamage = 2;
        attackCooldown = 1.2f;
    }

    /// <summary>Дистанция боя для ИИ: 2 клетки (бьём через одну).</summary>
    public override int PreferredAttackRange => 2;

    /// <summary>
    /// Зона атаки копейщика: одна клетка GridPos + Facing * 2 (прямо через
    /// клетку). Осевые направления дают (±2,0)/(0,±2); диагональные — (±2,±2)
    /// (квантованный «луч» копья по сетке). Базовая таблица Rot45 здесь не
    /// используется — форма зоны принципиально другая.
    /// </summary>
    public override List<Vector2Int> GetAttackCells()
    {
        return new List<Vector2Int>(1)
        {
            GridPos + Facing * 2
        };
    }
}
